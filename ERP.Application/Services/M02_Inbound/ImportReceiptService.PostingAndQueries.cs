using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public partial class ImportReceiptService
    {
        public async Task ApproveImportReceiptAsync(int id, int approvedByUserId)
        {
            if (_currentUser is { UserId: > 0 }) approvedByUserId = _currentUser.UserId;
            await EnsurePermissionAsync(approvedByUserId, "receipt.complete");
            try
            {
                await _unitOfWork.BeginTransactionAsync();
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

                Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, approvedByUserId);

                if (receipt.Status != ReceiptStatus.Received && receipt.Status != ReceiptStatus.QcCompleted)
                    throw Conflict("Chỉ có thể duyệt phiếu đã hoàn tất nhận hàng");

                var previousStatus = receipt.Status;
                if (previousStatus == ReceiptStatus.QcCompleted)
                    await EnsurePermissionAsync(approvedByUserId, "quality_disposition.approve");
                if (_inboundPlanning is not null)
                    await _inboundPlanning.ValidatePostAsync(receipt);
                if (_inventoryIdentity is not null)
                    await _inventoryIdentity.ValidateReadyToPostAsync(receipt.Id);
                receipt.SupplierCodeSnapshot = receipt.Supplier?.Code;
                receipt.SupplierNameSnapshot = receipt.Supplier?.Name;

                receipt.Status = ReceiptStatus.ReadyToPost;
                receipt.ApprovedBy = approvedByUserId;
                receipt.ApprovedAt = DateTime.UtcNow;

                await _importReceiptRepository.UpdateAsync(receipt);

                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = approvedByUserId,
                    Action = "ImportReceipt.Approved",
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    OldValues = $"Status: {previousStatus}",
                    NewValues = $"Status: {ReceiptStatus.ReadyToPost}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ConcurrencyException ex)
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw new ConcurrencyException("Phiếu nhập đang được duyệt bởi một người khác. Vui lòng thử lại.", ex);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task ReceiveAsync(int id, ReceiveImportReceiptDto dto, int receivedByUserId)
        {
            if (_currentUser is { UserId: > 0 }) receivedByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id)
                    ?? throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                if (receipt.Status != ReceiptStatus.Draft)
                    throw Conflict("Chỉ có thể hoàn tất nhận hàng cho phiếu nháp");
                if (receipt.Details.Count == 0 || dto.Lines.Count != receipt.Details.Count || dto.Lines.Select(x => x.LineId).Distinct().Count() != receipt.Details.Count)
                    throw new BusinessRuleException("Dữ liệu nhận hàng phải bao phủ chính xác tất cả dòng phiếu");
                foreach (var detail in receipt.Details)
                {
                    var line = dto.Lines.SingleOrDefault(x => x.LineId == detail.Id)
                        ?? throw new BusinessRuleException($"Thiếu dữ liệu nhận cho dòng {detail.Id}");
                    if (line.ReceivedQuantity <= 0 || line.AcceptedQuantity < 0 || line.DamagedQuantity < 0 || line.RejectedQuantity < 0)
                        throw new BusinessRuleException($"Số lượng dòng {detail.Id} không hợp lệ");
                    var expected = detail.ExpectedQuantity > 0 ? detail.ExpectedQuantity : detail.Quantity;
                    if (line.ReceivedQuantity != expected)
                        throw Conflict("Số lượng quan sát khác số lượng dự kiến; hãy dùng workflow receiving discrepancy.");
                    if (!detail.RequiresQc && line.AcceptedQuantity + line.DamagedQuantity + line.RejectedQuantity != line.ReceivedQuantity)
                        throw new BusinessRuleException($"Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận ở dòng {detail.Id}");
                    if (!detail.RequiresQc && (line.DamagedQuantity != 0 || line.RejectedQuantity != 0))
                        throw new BusinessRuleException("Nhánh no-QC chỉ chấp nhận toàn bộ số lượng nhận");
                    if (detail.RequiresQc && (line.AcceptedQuantity != 0 || line.DamagedQuantity != 0 || line.RejectedQuantity != 0))
                        throw new BusinessRuleException("Disposition của dòng yêu cầu QC phải được ghi bằng command QC riêng");
                    EnsurePrecision(line.ReceivedQuantity, detail.OperationUnitDecimalPlaces, "Số lượng nhận");
                    var factor = detail.ConversionFactor > 0 ? detail.ConversionFactor : 1;
                    detail.ReceivedQuantity = line.ReceivedQuantity;
                    detail.ObservedQuantity = line.ReceivedQuantity;
                    detail.FinalReceivedQuantity = line.ReceivedQuantity;
                    detail.AcceptedQuantity = detail.RequiresQc ? 0 : line.AcceptedQuantity;
                    detail.DamagedQuantity = detail.RequiresQc ? 0 : line.DamagedQuantity;
                    detail.RejectedQuantity = detail.RequiresQc ? 0 : line.RejectedQuantity;
                    detail.BaseReceivedQuantity = ConvertToBase(line.ReceivedQuantity, factor, detail);
                    detail.BaseObservedQuantity = detail.BaseReceivedQuantity;
                    detail.BaseFinalReceivedQuantity = detail.BaseReceivedQuantity;
                    detail.BaseAcceptedQuantity = detail.RequiresQc ? 0 : ConvertToBase(line.AcceptedQuantity, factor, detail);
                    detail.BaseDamagedQuantity = 0;
                    detail.BaseRejectedQuantity = 0;
                }
                receipt.SupplierCodeSnapshot ??= receipt.Supplier?.Code;
                receipt.SupplierNameSnapshot ??= receipt.Supplier?.Name;
                receipt.Status = receipt.Details.Any(x => x.RequiresQc) ? ReceiptStatus.QcPending : ReceiptStatus.Received;
                if (_inboundPlanning is not null) await _inboundPlanning.MarkReceivingAsync(receipt);
                await _importReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new AuditLog { UserId = receivedByUserId, Action = "ImportReceipt.Received", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.Draft}", NewValues = $"Status: {receipt.Status}", Result = "Success", Severity = "Information", Timestamp = DateTime.UtcNow });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
        }

        public async Task RecordQcDispositionAsync(int id, RecordQcDispositionDto dto, int completedByUserId)
        {
            if (_currentUser is { UserId: > 0 }) completedByUserId = _currentUser.UserId;
            await EnsurePermissionAsync(completedByUserId, "quality_inspection.execute");
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsForUpdateAsync(id)
                    ?? throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                if (receipt.Status != ReceiptStatus.QcPending) throw Conflict("Chỉ có thể ghi disposition khi phiếu đang chờ QC");
                if (dto.Lines.Count == 0 || dto.Lines.Select(x => x.LineId).Distinct().Count() != dto.Lines.Count)
                    throw new BusinessRuleException("Danh sách disposition không hợp lệ");
                foreach (var line in dto.Lines)
                {
                    var detail = receipt.Details.SingleOrDefault(x => x.Id == line.LineId && x.RequiresQc)
                        ?? throw new BusinessRuleException($"Dòng {line.LineId} không thuộc QC của phiếu");
                    if (detail.QcState != ReceiptLineQcState.QcPending) throw Conflict($"Dòng {line.LineId} đã được disposition");
                    if (line.AcceptedQuantity < 0 || line.DamagedQuantity < 0 || line.RejectedQuantity < 0 ||
                        line.AcceptedQuantity + line.DamagedQuantity + line.RejectedQuantity != detail.ReceivedQuantity)
                        throw new BusinessRuleException($"Accepted + Damaged + Rejected phải bằng Received ở dòng {line.LineId}");
                    if ((line.DamagedQuantity > 0 || line.RejectedQuantity > 0) && string.IsNullOrWhiteSpace(line.ReasonCode))
                        throw new BusinessRuleException($"Dòng {line.LineId} cần reason code cho lượng hư hỏng hoặc từ chối");
                    EnsurePrecision(line.AcceptedQuantity, detail.OperationUnitDecimalPlaces, "Số lượng chấp nhận");
                    EnsurePrecision(line.DamagedQuantity, detail.OperationUnitDecimalPlaces, "Số lượng hư hỏng");
                    EnsurePrecision(line.RejectedQuantity, detail.OperationUnitDecimalPlaces, "Số lượng từ chối");
                    var factor = detail.ConversionFactor > 0 ? detail.ConversionFactor : 1;
                    detail.AcceptedQuantity = line.AcceptedQuantity;
                    detail.DamagedQuantity = line.DamagedQuantity;
                    detail.RejectedQuantity = line.RejectedQuantity;
                    detail.BaseAcceptedQuantity = ConvertToBase(line.AcceptedQuantity, factor, detail);
                    detail.BaseDamagedQuantity = ConvertToBase(line.DamagedQuantity, factor, detail);
                    detail.BaseRejectedQuantity = ConvertToBase(line.RejectedQuantity, factor, detail);
                    detail.QcDispositionReasonCode = line.ReasonCode?.Trim();
                    detail.QcDispositionNote = line.Note?.Trim();
                    detail.QcCompletedBy = completedByUserId;
                    detail.QcCompletedAt = DateTime.UtcNow;
                    detail.QcState = ReceiptLineQcState.QcCompleted;
                }
                if (receipt.Details.Where(x => x.RequiresQc).All(x => x.QcState == ReceiptLineQcState.QcCompleted))
                {
                    await EnsurePermissionAsync(completedByUserId, "quality_inspection.complete");
                    receipt.Status = ReceiptStatus.QcCompleted;
                }
                await _importReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new AuditLog { UserId = completedByUserId, Action = "ImportReceipt.QcDispositionRecorded", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.QcPending}", NewValues = $"Status: {receipt.Status}", Result = "Success", Severity = "Information", Timestamp = DateTime.UtcNow });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ConcurrencyException ex) { try { await _unitOfWork.RollbackTransactionAsync(); } catch { } throw new ConcurrencyException("Disposition đã được cập nhật bởi yêu cầu khác. Vui lòng tải lại.", ex); }
            catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
        }

        public async Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> SetInventoryIdentitiesAsync(
            int id,
            SetImportReceiptInventoryIdentitiesDto dto,
            int updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            if (_inventoryIdentity is null)
                throw new InvalidOperationException("Receipt inventory identity service is not configured.");
            if (_currentUser is { UserId: > 0 }) updatedByUserId = _currentUser.UserId;
            return await _inventoryIdentity.SetAsync(id, dto, updatedByUserId, cancellationToken);
        }

        public async Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> GetInventoryIdentitiesAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            if (_inventoryIdentity is null)
                return Array.Empty<ImportReceiptInventoryIdentityDto>();
            return await _inventoryIdentity.GetAsync(id, cancellationToken);
        }

        public async Task PostAsync(int id, int postedByUserId)
        {
            if (_currentUser is { UserId: > 0 }) postedByUserId = _currentUser.UserId;
            var maxAttempts = _unitOfWork.HasExternalTransaction ? 1 : 4;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id)
                        ?? throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                    if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                    Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, postedByUserId);
                    if (receipt.Status != ReceiptStatus.ReadyToPost)
                        throw Conflict("Chỉ có thể ghi tồn cho phiếu sẵn sàng post");
                    if (_inboundPlanning is not null) await _inboundPlanning.ValidatePostAsync(receipt);
                    if (_inventoryIdentity is not null) await _inventoryIdentity.ValidateReadyToPostAsync(receipt.Id);
                    int? receivingLocationId = _putaway is null ? null : await _putaway.GetReceivingLocationIdAsync(receipt.WarehouseId);
                    await _inventoryStockRepository.AcquireAllocationLocksAsync(
                        receipt.WarehouseId,
                        receipt.Details.Select(d => d.ProductId));
                    foreach (var detail in receipt.Details.OrderBy(d => d.ProductId).ThenBy(d => d.Id))
                    {
                        var legacyNoQc = !detail.RequiresQc && detail.BaseReceivedQuantity == 0 && detail.BaseAcceptedQuantity > 0;
                        if (!legacyNoQc && detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity != detail.BaseReceivedQuantity)
                            throw new BusinessRuleException($"Dòng {detail.Id} chưa có disposition hợp lệ");
                        await PostBucketAsync(receipt, detail, InventoryStatus.Available, detail.BaseAcceptedQuantity, postedByUserId, receivingLocationId);
                        await PostBucketAsync(receipt, detail, InventoryStatus.Damaged, detail.BaseDamagedQuantity, postedByUserId, receivingLocationId);
                        await PostBucketAsync(receipt, detail, InventoryStatus.Rejected, detail.BaseRejectedQuantity, postedByUserId, receivingLocationId);
                        detail.PostedQuantity = detail.AcceptedQuantity + detail.DamagedQuantity + detail.RejectedQuantity;
                        detail.BasePostedQuantity = detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity;
                    }
                    receipt.Status = ReceiptStatus.Posted;
                    if (_inboundPlanning is not null) await _inboundPlanning.ApplyPostedStateAsync(receipt, postedByUserId);
                    if (_putaway is not null) await _putaway.CreateForPostedReceiptAsync(receipt, receivingLocationId!.Value, postedByUserId);
                    await _importReceiptRepository.UpdateAsync(receipt);
                    if (_partnerDebt is not null) await _partnerDebt.RecordImportReceiptApDebtAsync(receipt, postedByUserId);
                    await _auditLogRepository.AddAsync(new AuditLog { UserId = postedByUserId, Action = "ImportReceipt.Posted", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.ReadyToPost}", NewValues = $"Status: {ReceiptStatus.Posted}", Result = "Success", Severity = "Warning", Timestamp = DateTime.UtcNow });
                    await _unitOfWork.CommitTransactionAsync();
                    return;
                }
                catch (DeadlockException) when (attempt < maxAttempts)
                {
                    try { await _unitOfWork.RollbackTransactionAsync(); } catch { }
                    await Task.Delay(15 * attempt);
                }
                catch (ConcurrencyException ex) { try { await _unitOfWork.RollbackTransactionAsync(); } catch { } throw new ConcurrencyException("Phiếu nhập đã được xử lý bởi yêu cầu khác. Vui lòng tải lại.", ex); }
                catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
            }
        }

        private async Task PostBucketAsync(ImportReceipt receipt, ImportReceiptDetail detail, InventoryStatus status, decimal quantity, int userId, int? locationId)
        {
            if (quantity <= 0) return;
            if (_inventoryIdentity is not null &&
                await _inventoryIdentity.TryPostTrackedBucketAsync(receipt, detail, status, quantity, userId, locationId))
                return;
            var factor = detail.ConversionFactor > 0 ? detail.ConversionFactor : 1m;
            var baseUnitCost = factor > 0 ? decimal.Round(detail.UnitPrice / factor, 4, MidpointRounding.AwayFromZero) : detail.UnitPrice;
            var stock = locationId.HasValue ? await _inventoryStockRepository.GetByProductWarehouseStatusAndLocationAsync(detail.ProductId, receipt.WarehouseId, status, locationId.Value) : status == InventoryStatus.Available
                ? await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, receipt.WarehouseId)
                : await _inventoryStockRepository.GetByProductWarehouseAndStatusAsync(detail.ProductId, receipt.WarehouseId, status);
            var effectiveLocationId = locationId ?? stock?.LocationId;
            if (stock is null)
            {
                await _inventoryStockRepository.AddAsync(new InventoryStock { ProductId = detail.ProductId, WarehouseId = receipt.WarehouseId, Status = status, LocationId = effectiveLocationId, Quantity = quantity, MovingAverageCost = baseUnitCost, LastUpdated = DateTime.UtcNow });
                if (detail.Product is not null && baseUnitCost > 0m && detail.Product.MovingAverageCost <= 0m)
                    detail.Product.MovingAverageCost = baseUnitCost;
            }
            else
            {
                var oldQty = Math.Max(0m, stock.Quantity);
                var oldMac = stock.MovingAverageCost > 0m ? stock.MovingAverageCost : baseUnitCost;
                stock.MovingAverageCost = oldQty + quantity > 0m
                    ? decimal.Round(((oldQty * oldMac) + (quantity * baseUnitCost)) / (oldQty + quantity), 4, MidpointRounding.AwayFromZero)
                    : baseUnitCost;
                stock.Quantity += quantity;
                stock.LastUpdated = DateTime.UtcNow;
                await _inventoryStockRepository.UpdateAsync(stock);
                if (detail.Product is not null && stock.MovingAverageCost > 0m)
                    detail.Product.MovingAverageCost = stock.MovingAverageCost;
            }
            await _inventoryTransactionRepository.AddAsync(new InventoryTransaction { ProductId = detail.ProductId, WarehouseId = receipt.WarehouseId, LocationId = effectiveLocationId, InventoryStatus = status, TransactionType = TransactionType.Import, Quantity = quantity, UnitCost = baseUnitCost, SourceDocumentLineId = detail.Id, ReferenceId = receipt.Id, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = userId, Note = detail.Note });
        }

        private static decimal ConvertToBase(decimal quantity, decimal factor, ImportReceiptDetail detail)
        {
            var result = quantity * factor;
            EnsurePrecision(result, detail.BaseUnitDecimalPlaces, $"Số lượng Base UOM dòng {detail.Id}");
            return result;
        }

        private static void EnsurePrecision(decimal value, int decimalPlaces, string field)
        {
            if (decimalPlaces is < 0 or > 4 || decimal.Round(value, decimalPlaces) != value)
                throw new BusinessRuleException($"{field} vượt quá {decimalPlaces} chữ số thập phân; hệ thống không tự làm tròn");
        }

        private static BusinessRuleException Conflict(string message)
        {
            var exception = new BusinessRuleException(message);
            exception.Data["HttpStatusCode"] = 409;
            return exception;
        }

        private async Task EnsurePermissionAsync(int userId, string permission)
        {
            if (_permissionUsers is not null && !await _permissionUsers.HasPermissionAsync(userId, permission))
                throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
        }

        public async Task<IEnumerable<ImportReceiptDto>> GetAllAsync(Domain.Enums.ReceiptStatus? status = null)
        {
            var receipts = await _importReceiptRepository.GetAllWithDetailsAsync(status);
            return receipts.Select(r => MapToDto(r, includeDetailsWhenCostRedacted: false));
        }

        public async Task<ImportReceiptDto> GetByIdAsync(int id)
        {
            var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null)
                throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");

            return MapToDto(receipt);
        }

        public async Task CancelAsync(int id, int cancelledByUserId)
        {
            if (_currentUser is { UserId: > 0 }) cancelledByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt == null)
                    throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

                if (receipt.Status is not (Domain.Enums.ReceiptStatus.Draft
                    or Domain.Enums.ReceiptStatus.Received
                    or Domain.Enums.ReceiptStatus.QcPending
                    or Domain.Enums.ReceiptStatus.QcCompleted
                    or Domain.Enums.ReceiptStatus.ReadyToPost))
                    throw new BusinessRuleException("Chỉ có thể hủy phiếu nhập ở trạng thái nháp");

                var previousStatus = receipt.Status;
                if (receipt.Supplier is not null)
                {
                    receipt.SupplierCodeSnapshot ??= receipt.Supplier.Code;
                    receipt.SupplierNameSnapshot ??= receipt.Supplier.Name;
                }
                if (receipt.Asn is not null && receipt.Asn.Status == Domain.Enums.AsnStatus.Receiving)
                {
                    receipt.Asn.Status = Domain.Enums.AsnStatus.Arrived;
                }
                receipt.Status = Domain.Enums.ReceiptStatus.Cancelled;
                await _importReceiptRepository.UpdateAsync(receipt);

                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = cancelledByUserId,
                    Action = "ImportReceipt.Cancelled",
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    OldValues = $"Status: {previousStatus}",
                    NewValues = $"Status: {Domain.Enums.ReceiptStatus.Cancelled}",
                    Result = "Success",
                    Reason = "Import receipt cancelled",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
    }
}
