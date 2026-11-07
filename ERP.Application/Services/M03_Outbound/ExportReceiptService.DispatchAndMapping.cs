using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public partial class ExportReceiptService
    {
        public async Task DispatchAsync(int id, int userId)
        {
            EnsureWriteEnabled();
            if (_currentUser is { UserId: > 0 }) userId = _currentUser.UserId;
            var maxDeadlockAttempts = _unitOfWork.HasExternalTransaction ? 1 : 4;
            for (var attempt = 1; attempt <= maxDeadlockAttempts; attempt++)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
                    if (receipt is null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
                    if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                    EnsureNotCancelledOrPendingPutback(receipt);
                    if (receipt.Status != ReceiptStatus.Approved || receipt.DispatchMode != ExportDispatchMode.RequireSeparateDispatch)
                        throw new ConcurrencyException("Phiếu xuất không ở trạng thái có thể xác nhận xuất.");
                    if (_options.RequireDifferentDispatcher && receipt.ApprovedBy == userId)
                        throw new BusinessRuleException("Người duyệt phải khác người xác nhận xuất.");
                    if (_stockReservationService is null) throw new InvalidOperationException("Stock reservation service is required for export dispatch.");
                    if (_pickingDispatchReadiness is not null)
                        await _pickingDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);
                    if (_packingDispatchReadiness is not null)
                        await _packingDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);
                    if (_shipmentDispatchReadiness is not null)
                        await _shipmentDispatchReadiness.EnsureSourceReadyAsync("ExportReceipt", receipt.Id);
                    if (receipt.ShipmentId.HasValue)
                        throw new ConcurrencyException("Phiếu xuất đã được gắn với Shipment. Hãy xác nhận xuất tại Shipment để tránh trừ tồn 2 lần.");

                    var existingExportTxs = await _inventoryTransactionRepository.FindAsync(t =>
                        t.WarehouseId == receipt.WarehouseId &&
                        t.ReferenceId == receipt.Id &&
                        t.ReferenceType == "ExportReceipt" &&
                        (t.TransactionType == TransactionType.Export || t.TransactionType == TransactionType.Ship));
                    if (existingExportTxs.Any())
                        throw new ConcurrencyException("Phiếu xuất này đã được ghi nhận bút toán xuất kho trước đó.");

                    await _inventoryStockRepository.AcquireAllocationLocksAsync(
                        receipt.WarehouseId,
                        receipt.Details.Select(x => x.ProductId));

                    foreach (var productGroup in receipt.Details.GroupBy(x => x.ProductId).OrderBy(g => g.Key))
                    {
                        var groupDetails = productGroup.ToList();
                        var reservation = await _stockReservationService.GetExportReservationAsync(receipt.Id, receipt.WarehouseId, productGroup.Key);
                        var consumptions = await _stockReservationService.ConsumeAsync(reservation, userId);
                        if (groupDetails.Count == 1)
                        {
                            await AddExportTransactionsAsync(receipt, groupDetails[0], consumptions, userId);
                        }
                        else
                        {
                            await AddGroupedExportTransactionsAsync(receipt, groupDetails, consumptions, userId);
                        }
                    }

                    receipt.Status = ReceiptStatus.Dispatched;
                    receipt.DispatchedBy = userId;
                    receipt.DispatchedAt = DateTime.UtcNow;
                    await _exportReceiptRepository.UpdateAsync(receipt);
                    if (_partnerDebt is not null)
                    {
                        var (subTotal, taxAmount) = CalculateReceiptTotals(receipt);
                        var outboxTask = _partnerDebt.EnqueueValuationOutboxEventAsync("ExportReceipt", receipt.Id, receipt.Code, receipt.WarehouseId, subTotal + taxAmount);
                        if (outboxTask is not null) await outboxTask;
                        if (receipt.CustomerId.HasValue)
                        {
                            await _partnerDebt.RecordOutboundArDebtAsync(
                                receipt.CustomerId.Value,
                                "ExportReceipt",
                                receipt.Id,
                                receipt.Code,
                                subTotal,
                                taxAmount,
                                userId);
                        }
                    }
                    await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                    {
                        UserId = userId,
                        Action = "ExportReceipt.Dispatched",
                        EntityName = "ExportReceipt",
                        EntityId = receipt.Id,
                        WarehouseId = receipt.WarehouseId,
                        Result = "Success",
                        Severity = "Information",
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {ReceiptStatus.Approved}",
                        NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {receipt.DispatchMode}; Status: {ReceiptStatus.Dispatched}"
                    });
                    await _unitOfWork.CommitTransactionAsync();
                    return;
                }
                catch (DeadlockException) when (attempt < maxDeadlockAttempts)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    await Task.Delay(15 * attempt);
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }
        }

        private async Task AddExportTransactionsAsync(
            ERP.Domain.Entities.ExportReceipt receipt,
            ERP.Domain.Entities.ExportReceiptDetail detail,
            IReadOnlyList<InventoryStockConsumption> consumptions,
            int userId)
        {
            if (consumptions.Count == 0 || consumptions.Any(x => x.LocationId <= 0 || x.Quantity <= 0) ||
                consumptions.Sum(x => x.Quantity) != detail.Quantity)
                throw new ConcurrencyException("Không thể đối soát vị trí tồn kho đã xuất.");

            foreach (var consumption in consumptions)
            {
                await _inventoryTransactionRepository.AddAsync(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = receipt.WarehouseId,
                    LocationId = consumption.LocationId,
                    LotId = consumption.LotId,
                    SerialId = consumption.SerialId,
                    InventoryStatus = consumption.Status,
                    TransactionType = TransactionType.Export,
                    Quantity = consumption.Quantity,
                    UnitCost = detail.Product?.MovingAverageCost is > 0m ? detail.Product.MovingAverageCost : detail.UnitPrice,
                    SourceDocumentLineId = detail.Id,
                    ReferenceId = receipt.Id,
                    ReferenceType = "ExportReceipt",
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    Note = detail.Note
                });
            }
        }

        private async Task AddGroupedExportTransactionsAsync(
            ERP.Domain.Entities.ExportReceipt receipt,
            IReadOnlyList<ERP.Domain.Entities.ExportReceiptDetail> groupDetails,
            IReadOnlyList<InventoryStockConsumption> consumptions,
            int userId)
        {
            var expectedTotal = groupDetails.Sum(d => d.Quantity);
            if (consumptions.Count == 0 || consumptions.Any(x => x.LocationId <= 0 || x.Quantity <= 0) ||
                consumptions.Sum(x => x.Quantity) != expectedTotal)
                throw new ConcurrencyException("Không thể đối soát vị trí tồn kho đã xuất.");

            var pool = consumptions
                .Select(c => (c.LocationId, c.LotId, c.SerialId, c.Status, Remaining: c.Quantity))
                .ToList();
            var poolIndex = 0;

            foreach (var detail in groupDetails)
            {
                var needed = detail.Quantity;
                var detailConsumptions = new List<InventoryStockConsumption>();
                while (needed > 0m && poolIndex < pool.Count)
                {
                    var current = pool[poolIndex];
                    if (current.Remaining <= 0m)
                    {
                        poolIndex++;
                        continue;
                    }

                    var take = Math.Min(needed, current.Remaining);
                    detailConsumptions.Add(new InventoryStockConsumption(
                        current.LocationId,
                        take,
                        current.Status,
                        current.LotId,
                        current.SerialId));
                    current.Remaining -= take;
                    pool[poolIndex] = current;
                    needed -= take;
                    if (current.Remaining <= 0m) poolIndex++;
                }

                await AddExportTransactionsAsync(receipt, detail, detailConsumptions, userId);
            }
        }

        private static void EnsureNotCancelledOrPendingPutback(ERP.Domain.Entities.ExportReceipt receipt)
        {
            if (receipt.Status == ReceiptStatus.CancelPendingPutback ||
                receipt.SalesOrder?.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.CancelPendingPutback)
            {
                var cancelEx = new ConcurrencyException(
                    "Đơn hàng đã bị hủy trên sàn hoặc đang chờ cất trả kệ (CancelPendingPutback). Không thể xác nhận xuất kho.",
                    "ORDER_CANCELLED_PUTBACK_REQUIRED");
                cancelEx.Data["ErrorCode"] = "ORDER_CANCELLED_PUTBACK_REQUIRED";
                throw cancelEx;
            }
        }

        public async Task<IEnumerable<ExportReceiptDto>> GetAllAsync()
        {
            var receipts = await _exportReceiptRepository.GetListAsync();
            return receipts.Select(MapToDto);
        }

        public async Task<ExportReceiptDto> GetByIdAsync(int id)
        {
            var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");

            return MapToDto(receipt);
        }

        public async Task CancelAsync(int id, int userId)
        {
            if (_currentUser is { UserId: > 0 }) userId = _currentUser.UserId;
            var receipt = _stockReservationService is null
                ? await _exportReceiptRepository.GetByIdAsync(id)
                : await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
            if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);

            if (receipt.Status is ReceiptStatus.Cancelled or ReceiptStatus.Dispatched)
            {
                throw new BusinessRuleException("Không thể hủy phiếu xuất đã xuất kho hoặc đã bị hủy.");
            }

            if (receipt.Status == ReceiptStatus.Approved) EnsureWriteEnabled();

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var wasApproved = receipt.Status == ReceiptStatus.Approved;
                if (receipt.Status == ReceiptStatus.Draft)
                {
                    receipt.CustomerCodeSnapshot = receipt.Customer?.Code;
                    receipt.CustomerNameSnapshot = receipt.Customer?.Name;
                }
                receipt.Status = ReceiptStatus.Cancelled;
                if (receipt.Details is { Count: > 0 })
                {
                    await _inventoryStockRepository.AcquireAllocationLocksAsync(
                        receipt.WarehouseId,
                        receipt.Details.Select(d => d.ProductId));
                }
                if (_stockReservationService is not null) await _stockReservationService.ReleaseSourceAsync("ExportReceipt", receipt.Id, userId, "Export receipt cancelled");
                await _exportReceiptRepository.UpdateAsync(receipt);

                await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                {
                    UserId = userId,
                    Action = wasApproved ? "ExportReceipt.CancelledAndReleased" : "ExportReceipt.Cancelled",
                    EntityName = "ExportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    Result = "Success",
                    Reason = "Export receipt cancelled",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    OldValues = $"Status: {(wasApproved ? ReceiptStatus.Approved : ReceiptStatus.Draft)}",
                    NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {receipt.DispatchMode}; Status: {ReceiptStatus.Cancelled}"
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        private static (decimal SubTotal, decimal TaxAmount) CalculateReceiptTotals(ERP.Domain.Entities.ExportReceipt receipt)
        {
            decimal subTotal = 0m;
            decimal taxAmount = 0m;
            foreach (var d in receipt.Details)
            {
                if (d.IsFreeGift) continue;
                var lineSub = decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero);
                var lineTax = decimal.Round(lineSub * (d.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
                subTotal += lineSub;
                taxAmount += lineTax;
            }
            return (subTotal, taxAmount);
        }

        private static string? NormalizeActorDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value?.Trim();
            var trimmed = value.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "QA MANAGER WH01" or "QA_MANAGER_WH01" => "Quản lý Kho WH01",
                "QA STAFF WH01" or "QA_STAFF_WH01" => "Nhân viên Kho WH01",
                "QA SUPERVISOR WH01" or "QA_SUPERVISOR_WH01" or "QA VIEWER WH01" or "QA_VIEWER_WH01" => "Giám sát Kho WH01",
                "QA SYSTEM ADMIN" or "QA ADMIN" or "QA_ADMIN" => "Quản trị viên Hệ thống",
                "QA_ADMIN_LOCAL" or "QA ADMIN LOCAL" => "Quản trị viên Nội bộ",
                "TENANT_ADMIN" or "TENANT ADMIN" => "Quản trị viên Đại Phát",
                "QA_ACCOUNTANT" or "QA ACCOUNTANT" => "Kế toán Kho Đại Phát",
                _ => trimmed
            };
        }

        private static string NormalizeUomValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "THÙNG";
            var trimmed = value.Trim();
            return string.Equals(trimmed, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                ? "THÙNG"
                : trimmed;
        }

        private ExportReceiptDto MapToDto(ERP.Domain.Entities.ExportReceipt receipt)
        {
            var viewer = string.Equals(_currentUser?.Role, "Viewer", StringComparison.Ordinal);
            var canReadCost = _currentUser is { IsAuthenticated: true } &&
                              _currentUser.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead);
            var details = (receipt.Details ?? []).Select(d =>
            {
                var effectiveUnitPrice = d.IsFreeGift ? 0m : d.UnitPrice;
                var lineSub = canReadCost ? decimal.Round(d.Quantity * effectiveUnitPrice, 2, MidpointRounding.AwayFromZero) : 0m;
                var lineTax = canReadCost ? decimal.Round(lineSub * (d.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
                var baseUomCode = NormalizeUomValue(!string.IsNullOrWhiteSpace(d.BaseUomCodeSnapshot) ? d.BaseUomCodeSnapshot : d.Product?.Unit?.Code);
                var baseUomName = NormalizeUomValue(!string.IsNullOrWhiteSpace(d.BaseUomNameSnapshot) ? d.BaseUomNameSnapshot : d.Product?.Unit?.Name);
                if (baseUomCode == "THÙNG") baseUomName = "THÙNG";
                return new ExportReceiptDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductCode = d.Product?.Code?.Trim(),
                    ProductName = d.Product?.Name?.Trim(),
                    UnitName = baseUomName,
                    Quantity = d.Quantity,
                    UnitPrice = canReadCost ? effectiveUnitPrice : null,
                    TaxPercent = d.TaxPercent,
                    LineSubTotal = canReadCost ? lineSub : null,
                    LineTaxAmount = canReadCost ? lineTax : null,
                    LineTotal = canReadCost ? lineSub + lineTax : null,
                    Note = viewer ? null : d.Note?.Trim(),
                    BaseUomIdSnapshot = d.BaseUomIdSnapshot ?? d.Product?.UnitId,
                    BaseUomCodeSnapshot = baseUomCode,
                    BaseUomNameSnapshot = baseUomName,
                    BaseUomDecimalPlacesSnapshot = d.BaseUomDecimalPlacesSnapshot ?? d.Product?.Unit?.DecimalPlaces
                };
            }).ToList();

            var subTotal = details.Sum(x => x.LineSubTotal ?? 0m);
            var taxAmount = details.Sum(x => x.LineTaxAmount ?? 0m);

            return new ExportReceiptDto
            {
                Id = receipt.Id,
                Code = receipt.Code.Trim(),
                WarehouseId = receipt.WarehouseId,
                WarehouseCode = receipt.Warehouse?.Code?.Trim(),
                WarehouseName = receipt.Warehouse?.Name?.Trim(),
                Status = receipt.Status.ToString(),
                Note = viewer ? null : receipt.Note?.Trim(),
                CreatedBy = receipt.CreatedBy,
                CreatedByName = NormalizeActorDisplayName(receipt.CreatedByUser?.FullName ?? receipt.CreatedByUser?.Username),
                ApprovedBy = receipt.ApprovedBy,
                ApprovedByName = NormalizeActorDisplayName(receipt.ApprovedByUser?.FullName ?? receipt.ApprovedByUser?.Username),
                CreatedAt = receipt.CreatedAt,
                ApprovedAt = receipt.ApprovedAt,
                DispatchMode = receipt.DispatchMode?.ToString(),
                DispatchedBy = receipt.DispatchedBy,
                DispatchedByName = NormalizeActorDisplayName(receipt.DispatchedByUser?.FullName ?? receipt.DispatchedByUser?.Username),
                DispatchedAt = receipt.DispatchedAt,
                CustomerId = receipt.CustomerId,
                CustomerCode = (!string.IsNullOrWhiteSpace(receipt.CustomerCodeSnapshot) ? receipt.CustomerCodeSnapshot : receipt.Customer?.Code)?.Trim(),
                CustomerName = (!string.IsNullOrWhiteSpace(receipt.CustomerNameSnapshot) ? receipt.CustomerNameSnapshot : receipt.Customer?.Name)?.Trim(),
                SalesOrderId = receipt.SalesOrderId,
                ShipmentId = receipt.ShipmentId,
                MarketplaceOrderId = receipt.MarketplaceOrderId?.Trim(),
                CarrierCode = receipt.CarrierCode?.Trim(),
                TrackingNumber = receipt.TrackingNumber?.Trim(),
                AwbLabelUrl = receipt.AwbLabelUrl?.Trim(),
                AwbPrintCount = receipt.AwbPrintCount,
                AwbFirstPrintedAtUtc = receipt.AwbFirstPrintedAtUtc,
                MarketplaceShipByUtc = receipt.MarketplaceShipByUtc,
                EInvoiceStatus = receipt.EInvoiceStatus?.Trim(),
                EInvoiceProvider = receipt.EInvoiceProvider?.Trim(),
                EInvoiceNumber = receipt.EInvoiceNumber?.Trim(),
                BuyerTaxCode = receipt.BuyerTaxCode?.Trim(),
                SubTotalAmount = canReadCost ? subTotal : null,
                TaxAmount = canReadCost ? taxAmount : null,
                TotalAmount = canReadCost ? subTotal + taxAmount : null,
                RowVersion = receipt.RowVersion is { Length: > 0 } ? Convert.ToBase64String(receipt.RowVersion) : null,
                AllowPerReceiptDispatchMode = _options.AllowPerReceiptDispatchMode,
                AllowWarehouseStaffDirectDispatch = _options.AllowWarehouseStaffDirectDispatch,
                WriteEnabled = _options.WriteEnabled,
                ReservationStatus = receipt.Status == ReceiptStatus.Approved ? "Active" : receipt.Status == ReceiptStatus.Dispatched ? "Consumed" : receipt.Status == ReceiptStatus.Cancelled ? "Released" : "PendingApproval",
                Details = details
            };
        }

        private void EnsureWriteEnabled()
        {
            if (!_options.WriteEnabled)
                throw new ServiceUnavailableException("Workflow xuất kho đang tạm dừng để bảo trì. Vui lòng thử lại sau.");
        }
    }

    public interface IExportReceiptBundleResolver
    {
        Task<IReadOnlyList<ERP.Domain.Entities.ProductBundleComponent>> GetActiveBundleComponentsAsync(IEnumerable<int> parentProductIds, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ERP.Domain.Entities.SalesOrderLine>> GetSalesOrderLinesAsync(int? salesOrderId, string? marketplaceOrderId, CancellationToken cancellationToken = default);
        Task<IReadOnlySet<int>> GetActiveGiftProductIdsAsync(int warehouseId, CancellationToken cancellationToken = default);
    }
}
