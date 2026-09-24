using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class ImportReceiptService : IImportReceiptService
    {
        private readonly IImportReceiptRepository _importReceiptRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;

        internal ImportReceiptService(IImportReceiptRepository importReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IWarehouseRepository warehouseRepository, IProductRepository productRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository)
        {
            _importReceiptRepository = importReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
        }

        public ImportReceiptService(
            IImportReceiptRepository importReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser)
        {
            _importReceiptRepository = importReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
        }

        public async Task<ImportReceiptDto> CreateAsync(CreateImportReceiptDto dto, int userId)
        {
            if (_currentUser is not null) userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new BusinessRuleException("Mã phiếu nhập không được để trống");

            if (dto.WarehouseId <= 0)
                throw new BusinessRuleException("Kho nhập không hợp lệ");

            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);

            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new BusinessRuleException($"Không tìm thấy kho nhập với ID {dto.WarehouseId}");

            if (dto.Details == null || !dto.Details.Any())
                throw new BusinessRuleException("Phiếu nhập phải có ít nhất 1 sản phẩm");

            if (await _importReceiptRepository.ExistsByCodeAsync(dto.Code))
                throw new BusinessRuleException($"Mã phiếu nhập '{dto.Code}' đã tồn tại");

            var receipt = new ImportReceipt
            {
                Code = dto.Code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                Note = dto.Note,
                SupplierId = dto.SupplierId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Details = new List<ImportReceiptDetail>()
            };

            foreach (var detailDto in dto.Details)
            {
                if (detailDto.ProductId <= 0)
                    throw new BusinessRuleException("Sản phẩm không hợp lệ");

                var product = await _productRepository.GetByIdAsync(detailDto.ProductId);
                if (product == null)
                    throw new BusinessRuleException($"Không tìm thấy sản phẩm với ID {detailDto.ProductId}");

                if (!product.IsActive)
                    throw new BusinessRuleException($"Sản phẩm ID {detailDto.ProductId} không hoạt động");
                if (detailDto.ExpectedQuantity <= 0)
                    throw new BusinessRuleException($"Số lượng dự kiến của sản phẩm ID {detailDto.ProductId} phải lớn hơn 0");
                var operationUnitId = detailDto.OperationUnitId > 0 ? detailDto.OperationUnitId : product.UnitId;

                var conversion = operationUnitId == product.UnitId
                    ? new { Unit = product.Unit, Factor = 1m, Version = 1 }
                    : product.Uoms.Where(x => x.IsActive && x.EffectiveFromUtc <= DateTime.UtcNow && x.UnitId == operationUnitId)
                        .OrderByDescending(x => x.Version)
                        .Select(x => new { x.Unit, Factor = x.ConversionFactor, x.Version })
                        .FirstOrDefault();
                if (conversion is null || conversion.Factor <= 0)
                    throw new BusinessRuleException($"UOM thao tác không hợp lệ cho sản phẩm ID {detailDto.ProductId}");
                EnsurePrecision(detailDto.ExpectedQuantity, conversion.Unit.DecimalPlaces, "Số lượng dự kiến");
                var baseExpected = detailDto.ExpectedQuantity * conversion.Factor;
                EnsurePrecision(baseExpected, product.Unit.DecimalPlaces, "Số lượng Base UOM");

                if (detailDto.UnitPrice < 0)
                    throw new BusinessRuleException($"Đơn giá của sản phẩm ID {detailDto.ProductId} không được âm");

                var now = DateTime.UtcNow;
                var policy = product.QcPolicies
                    .Where(x => x.IsActive && x.EffectiveFromUtc <= now && (x.SupplierId == dto.SupplierId || x.SupplierId == null))
                    .OrderByDescending(x => x.SupplierId == dto.SupplierId && dto.SupplierId.HasValue)
                    .ThenByDescending(x => x.Version)
                    .FirstOrDefault();

                receipt.Details.Add(new ImportReceiptDetail
                {
                    ProductId = detailDto.ProductId,
                    Quantity = detailDto.ExpectedQuantity,
                    ExpectedQuantity = detailDto.ExpectedQuantity,
                    OperationUnitId = conversion.Unit.Id,
                    OperationUnitCodeSnapshot = conversion.Unit.Code,
                    OperationUnitDecimalPlaces = conversion.Unit.DecimalPlaces,
                    BaseUnitId = product.UnitId,
                    BaseUnitCodeSnapshot = product.Unit.Code,
                    BaseUnitDecimalPlaces = product.Unit.DecimalPlaces,
                    ConversionFactor = conversion.Factor,
                    ConversionVersion = conversion.Version,
                    BaseExpectedQuantity = baseExpected,
                    UnitPrice = detailDto.UnitPrice,
                    Note = detailDto.Note,
                    RequiresQc = policy?.RequiresQc == true,
                    QcPolicyId = policy?.Id,
                    QcPolicyVersion = policy?.Version,
                    QcPolicySourceSnapshot = policy is null ? "None" : policy.SupplierId.HasValue ? "ProductSupplier" : "Product",
                    QcPolicyEffectiveAtUtc = policy?.EffectiveFromUtc,
                    QcRuleSnapshot = policy?.Rule,
                    QcState = policy?.RequiresQc == true ? ReceiptLineQcState.QcPending : ReceiptLineQcState.NoQcRequired
                });
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _importReceiptRepository.AddAsync(receipt);
                
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = userId,
                    Action = "ImportReceipt.Created",
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    OldValues = "Status: None",
                    NewValues = $"Status: {receipt.Status}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();

                var createdReceipt = await _importReceiptRepository.GetByIdWithDetailsAsync(receipt.Id);
                return MapToDto(createdReceipt!);
            }
            catch
            {
                try { await _unitOfWork.RollbackTransactionAsync(); } catch { }
                throw;
            }
        }

        private ImportReceiptDto MapToDto(ImportReceipt receipt)
        {
            var canReadCost = _currentUser is null || _currentUser.IsGlobalAdmin || _currentUser.Role == "Manager";
            return new ImportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.Name,
                Status = receipt.Status.ToString(),
                Note = receipt.Note,
                CreatedBy = receipt.CreatedBy,
                CreatedByName = receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username,
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username,
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                SupplierId = receipt.SupplierId,
                SupplierCode = receipt.Status == ReceiptStatus.Draft ? receipt.Supplier?.Code : receipt.SupplierCodeSnapshot,
                SupplierName = receipt.Status == ReceiptStatus.Draft ? receipt.Supplier?.Name : receipt.SupplierNameSnapshot,
                RequiresQc = receipt.Details.Any(x => x.RequiresQc),
                Details = receipt.Details.Select(d => new ImportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code,
                    ProductName = d.Product?.Name,
                    UnitName = d.OperationUnitCodeSnapshot.Length > 0 ? d.OperationUnitCodeSnapshot : d.Product?.Unit?.Name,
                    OperationUnitId = d.OperationUnitId > 0 ? d.OperationUnitId : d.Product?.UnitId ?? 0,
                    OperationUnitCode = d.OperationUnitCodeSnapshot.Length > 0 ? d.OperationUnitCodeSnapshot : d.Product?.Unit?.Code ?? string.Empty,
                    OperationUnitDecimalPlaces = d.OperationUnitDecimalPlaces,
                    BaseUnitId = d.BaseUnitId > 0 ? d.BaseUnitId : d.Product?.UnitId ?? 0,
                    BaseUnitCode = d.BaseUnitCodeSnapshot.Length > 0 ? d.BaseUnitCodeSnapshot : d.Product?.Unit?.Code ?? string.Empty,
                    BaseUnitDecimalPlaces = d.BaseUnitDecimalPlaces,
                    ConversionFactor = d.ConversionFactor > 0 ? d.ConversionFactor : 1,
                    ConversionVersion = d.ConversionVersion > 0 ? d.ConversionVersion : 1,
                    ExpectedQuantity = d.ExpectedQuantity > 0 ? d.ExpectedQuantity : d.Quantity,
                    ReceivedQuantity = d.ReceivedQuantity,
                    AcceptedQuantity = d.AcceptedQuantity,
                    DamagedQuantity = d.DamagedQuantity,
                    RejectedQuantity = d.RejectedQuantity,
                    PostedQuantity = d.PostedQuantity,
                    BaseExpectedQuantity = d.BaseExpectedQuantity > 0 ? d.BaseExpectedQuantity : d.Quantity,
                    BaseReceivedQuantity = d.BaseReceivedQuantity,
                    BaseAcceptedQuantity = d.BaseAcceptedQuantity,
                    BasePostedQuantity = d.BasePostedQuantity,
                    ObservedQuantity = d.ObservedQuantity,
                    DoorRejectedQuantity = d.DoorRejectedQuantity,
                    FinalReceivedQuantity = d.FinalReceivedQuantity > 0 ? d.FinalReceivedQuantity : d.ReceivedQuantity,
                    BaseFinalReceivedQuantity = d.BaseFinalReceivedQuantity > 0 ? d.BaseFinalReceivedQuantity : d.BaseReceivedQuantity,
                    UnitPrice = canReadCost ? d.UnitPrice : null,
                    Note = d.Note,
                    RequiresQc = d.RequiresQc,
                    QcState = d.QcState.ToString(),
                    QcPolicyId = d.QcPolicyId,
                    QcPolicyVersion = d.QcPolicyVersion,
                    QcPolicySource = d.QcPolicySourceSnapshot,
                    QcPolicyEffectiveAtUtc = d.QcPolicyEffectiveAtUtc,
                    QcDispositionReasonCode = d.QcDispositionReasonCode,
                    QcDispositionNote = d.QcDispositionNote
                }).ToList()
            };
        }

        public async Task ApproveImportReceiptAsync(int id, int approvedByUserId)
        {
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
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
            if (_currentUser is not null) receivedByUserId = _currentUser.UserId;
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
                receipt.Status = receipt.Details.Any(x => x.RequiresQc) ? ReceiptStatus.QcPending : ReceiptStatus.Received;
                await _importReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new AuditLog { UserId = receivedByUserId, Action = "ImportReceipt.Received", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.Draft}", NewValues = $"Status: {ReceiptStatus.Received}", Result = "Success", Severity = "Information", Timestamp = DateTime.UtcNow });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
        }

        public async Task RecordQcDispositionAsync(int id, RecordQcDispositionDto dto, int completedByUserId)
        {
            if (_currentUser is not null) completedByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id)
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
                    receipt.Status = ReceiptStatus.QcCompleted;
                await _importReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new AuditLog { UserId = completedByUserId, Action = "ImportReceipt.QcDispositionRecorded", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.QcPending}", NewValues = $"Status: {receipt.Status}", Result = "Success", Severity = "Information", Timestamp = DateTime.UtcNow });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ConcurrencyException ex) { try { await _unitOfWork.RollbackTransactionAsync(); } catch { } throw new ConcurrencyException("Disposition đã được cập nhật bởi yêu cầu khác. Vui lòng tải lại.", ex); }
            catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
        }

        public async Task PostAsync(int id, int postedByUserId)
        {
            if (_currentUser is not null) postedByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id)
                    ?? throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, postedByUserId);
                if (receipt.Status != ReceiptStatus.ReadyToPost)
                    throw Conflict("Chỉ có thể ghi tồn cho phiếu sẵn sàng post");
                foreach (var detail in receipt.Details)
                {
                    var legacyNoQc = !detail.RequiresQc && detail.BaseReceivedQuantity == 0 && detail.BaseAcceptedQuantity > 0;
                    if (!legacyNoQc && detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity != detail.BaseReceivedQuantity)
                        throw new BusinessRuleException($"Dòng {detail.Id} chưa có disposition hợp lệ");
                    await PostBucketAsync(receipt, detail, InventoryStatus.Available, detail.BaseAcceptedQuantity, postedByUserId);
                    await PostBucketAsync(receipt, detail, InventoryStatus.Damaged, detail.BaseDamagedQuantity, postedByUserId);
                    await PostBucketAsync(receipt, detail, InventoryStatus.Rejected, detail.BaseRejectedQuantity, postedByUserId);
                    detail.PostedQuantity = detail.AcceptedQuantity + detail.DamagedQuantity + detail.RejectedQuantity;
                    detail.BasePostedQuantity = detail.BaseAcceptedQuantity + detail.BaseDamagedQuantity + detail.BaseRejectedQuantity;
                }
                receipt.Status = ReceiptStatus.Posted;
                await _importReceiptRepository.UpdateAsync(receipt);
                await _auditLogRepository.AddAsync(new AuditLog { UserId = postedByUserId, Action = "ImportReceipt.Posted", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = receipt.WarehouseId, OldValues = $"Status: {ReceiptStatus.ReadyToPost}", NewValues = $"Status: {ReceiptStatus.Posted}", Result = "Success", Severity = "Warning", Timestamp = DateTime.UtcNow });
                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ConcurrencyException ex) { try { await _unitOfWork.RollbackTransactionAsync(); } catch { } throw new ConcurrencyException("Phiếu nhập đã được xử lý bởi yêu cầu khác. Vui lòng tải lại.", ex); }
            catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
        }

        private async Task PostBucketAsync(ImportReceipt receipt, ImportReceiptDetail detail, InventoryStatus status, decimal quantity, int userId)
        {
            if (quantity <= 0) return;
            var stock = status == InventoryStatus.Available
                ? await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, receipt.WarehouseId)
                : await _inventoryStockRepository.GetByProductWarehouseAndStatusAsync(detail.ProductId, receipt.WarehouseId, status);
            if (stock is null)
                await _inventoryStockRepository.AddAsync(new InventoryStock { ProductId = detail.ProductId, WarehouseId = receipt.WarehouseId, Status = status, Quantity = quantity, LastUpdated = DateTime.UtcNow });
            else { stock.Quantity += quantity; stock.LastUpdated = DateTime.UtcNow; await _inventoryStockRepository.UpdateAsync(stock); }
            await _inventoryTransactionRepository.AddAsync(new InventoryTransaction { ProductId = detail.ProductId, WarehouseId = receipt.WarehouseId, InventoryStatus = status, TransactionType = TransactionType.Import, Quantity = quantity, ReferenceId = receipt.Id, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = userId, Note = detail.Note });
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
        public async Task<IEnumerable<ImportReceiptDto>> GetAllAsync(Domain.Enums.ReceiptStatus? status = null)
        {
            var receipts = await _importReceiptRepository.GetAllWithDetailsAsync(status);
            return receipts.Select(MapToDto);
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
            if (_currentUser is not null) cancelledByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var receipt = await _importReceiptRepository.GetByIdWithDetailsAsync(id);
                if (receipt == null)
                    throw new NotFoundException($"Không tìm thấy phiếu nhập id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

                if (receipt.Status != Domain.Enums.ReceiptStatus.Draft)
                    throw new BusinessRuleException("Chỉ có thể hủy phiếu nhập ở trạng thái nháp");

                if (receipt.Status == Domain.Enums.ReceiptStatus.Draft)
                {
                    receipt.SupplierCodeSnapshot = receipt.Supplier?.Code;
                    receipt.SupplierNameSnapshot = receipt.Supplier?.Name;
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
                    OldValues = $"Status: {Domain.Enums.ReceiptStatus.Draft}",
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


