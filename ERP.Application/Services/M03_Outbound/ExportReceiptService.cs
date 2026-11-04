using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Application.Options;

namespace ERP.Application.Services
{
    public partial class ExportReceiptService : IExportReceiptService
    {
        private readonly IExportReceiptRepository _exportReceiptRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;
        private readonly IStockReservationService? _stockReservationService;
        private readonly IPickingDispatchReadiness? _pickingDispatchReadiness;
        private readonly IPackingDispatchReadiness? _packingDispatchReadiness;
        private readonly IShipmentDispatchReadiness? _shipmentDispatchReadiness;
        private readonly IPartnerDebtService? _partnerDebt;
        private readonly ExportReceiptOptions _options;

        internal ExportReceiptService(IExportReceiptRepository exportReceiptRepository, IInventoryStockRepository inventoryStockRepository, IInventoryTransactionRepository inventoryTransactionRepository, IUnitOfWork unitOfWork, IAuditLogRepository auditLogRepository, ExportReceiptOptions? options = null)
        {
            _exportReceiptRepository = exportReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _currentUser = InternalUnitTestActorContext.Instance;
            _stockReservationService = null;
            _pickingDispatchReadiness = null;
            _packingDispatchReadiness = null;
            _shipmentDispatchReadiness = null;
            _partnerDebt = null;
            _options = options ?? new ExportReceiptOptions();
        }

        public ExportReceiptService(
            IExportReceiptRepository exportReceiptRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser,
            IStockReservationService stockReservationService,
            ExportReceiptOptions? options = null,
            IPickingDispatchReadiness? pickingDispatchReadiness = null,
            IPackingDispatchReadiness? packingDispatchReadiness = null,
            IShipmentDispatchReadiness? shipmentDispatchReadiness = null,
            IPartnerDebtService? partnerDebt = null)
        {
            _exportReceiptRepository = exportReceiptRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
            _stockReservationService = stockReservationService;
            _pickingDispatchReadiness = pickingDispatchReadiness;
            _packingDispatchReadiness = packingDispatchReadiness;
            _shipmentDispatchReadiness = shipmentDispatchReadiness;
            _partnerDebt = partnerDebt;
            _options = options ?? new ExportReceiptOptions();
        }

        public async Task<ExportReceiptDto> CreateAsync(CreateExportReceiptDto dto, int userId)
        {
            if (_currentUser is { UserId: > 0 }) userId = _currentUser.UserId;
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new BusinessRuleException("Mã phiếu xuất không được để trống");

            if (dto.WarehouseId <= 0)
                throw new BusinessRuleException("Kho xuất không hợp lệ");

            if (_warehouseAuthorization is not null)
                await (_warehouseAuthorization.EnsureWarehouseCommandAccessAsync(dto.WarehouseId)
                    ?? _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId));

            if (dto.Details == null || !dto.Details.Any())
                throw new BusinessRuleException("Phiếu xuất phải có ít nhất 1 sản phẩm");

            if (dto.Details.GroupBy(x => (x.ProductId, IsFreeGift: x.UnitPrice == 0m && dto.Details.Any(y => y.ProductId == x.ProductId && y.UnitPrice > 0m))).Any(x => x.Count() > 1))
                throw new BusinessRuleException("Một sản phẩm không được lặp nhiều dòng.");

            if (await _exportReceiptRepository.ExistsByCodeAsync(dto.Code))
            {
                throw new BusinessRuleException($"Mã phiếu xuất '{dto.Code}' đã tồn tại");
            }

            var receipt = new ERP.Domain.Entities.ExportReceipt
            {
                Code = dto.Code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                Note = dto.Note,
                CustomerId = dto.CustomerId,
                SalesOrderId = dto.SalesOrderId,
                ShipmentId = dto.ShipmentId,
                MarketplaceOrderId = dto.MarketplaceOrderId?.Trim(),
                CarrierCode = dto.CarrierCode?.Trim(),
                TrackingNumber = dto.TrackingNumber?.Trim(),
                AwbLabelUrl = dto.AwbLabelUrl?.Trim(),
                MarketplaceShipByUtc = dto.MarketplaceShipByUtc,
                EInvoiceProvider = dto.EInvoiceProvider?.Trim(),
                BuyerTaxCode = dto.BuyerTaxCode?.Trim(),
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Details = new List<ERP.Domain.Entities.ExportReceiptDetail>()
            };

            foreach (var detailDto in dto.Details)
            {
                if (detailDto.ProductId <= 0)
                    throw new BusinessRuleException("Sản phẩm không hợp lệ");

                if (detailDto.Quantity <= 0)
                    throw new BusinessRuleException($"Số lượng xuất của sản phẩm ID {detailDto.ProductId} phải lớn hơn 0");

                if (detailDto.UnitPrice < 0)
                    throw new BusinessRuleException($"Đơn giá của sản phẩm ID {detailDto.ProductId} không được âm");
                if (detailDto.TaxPercent is < 0 or > 100)
                    throw new BusinessRuleException($"Thuế suất của sản phẩm ID {detailDto.ProductId} phải từ 0 đến 100%");
            }

            var bundleResolver = _exportReceiptRepository as IExportReceiptBundleResolver;
            var rawProductIds = dto.Details.Select(x => x.ProductId).Distinct().ToArray();
            var bundleComponents = bundleResolver is not null
                ? await bundleResolver.GetActiveBundleComponentsAsync(rawProductIds)
                : Array.Empty<ERP.Domain.Entities.ProductBundleComponent>();
            var bundleComponentsByParent = bundleComponents
                .GroupBy(x => x.ParentProductId)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ComponentProductId).ToList());
            var salesOrderLines = bundleResolver is not null
                ? await bundleResolver.GetSalesOrderLinesAsync(dto.SalesOrderId, dto.MarketplaceOrderId)
                : Array.Empty<ERP.Domain.Entities.SalesOrderLine>();
            var giftProductIds = bundleResolver is not null
                ? await bundleResolver.GetActiveGiftProductIdsAsync(dto.WarehouseId)
                : new HashSet<int>();

            var usedOrderLineIds = new HashSet<int>();
            var expandedDetails = new List<(int ProductId, decimal Quantity, decimal UnitPrice, decimal TaxPercent, string? Note, int? ParentBundleProductId, bool IsFreeGift)>();

            foreach (var detailDto in dto.Details)
            {
                if (bundleComponentsByParent.TryGetValue(detailDto.ProductId, out var components) && components.Count > 0)
                {
                    var totalComponentUnits = components.Sum(c => c.ComponentQuantity);
                    var isBundleFreeGift = detailDto.UnitPrice == 0m;
                    foreach (var comp in components)
                    {
                        var compQty = detailDto.Quantity * comp.ComponentQuantity;
                        var matchedOrderCompLine = salesOrderLines.FirstOrDefault(l =>
                            !usedOrderLineIds.Contains(l.Id) &&
                            l.ProductId == comp.ComponentProductId &&
                            l.ParentBundleProductId == detailDto.ProductId);
                        if (matchedOrderCompLine is not null)
                        {
                            usedOrderLineIds.Add(matchedOrderCompLine.Id);
                        }

                        var compUnitPrice = isBundleFreeGift
                            ? 0m
                            : matchedOrderCompLine?.UnitPrice ?? (totalComponentUnits > 0m
                                ? decimal.Round(detailDto.UnitPrice / totalComponentUnits, 2, MidpointRounding.AwayFromZero)
                                : detailDto.UnitPrice);

                        expandedDetails.Add((
                            comp.ComponentProductId,
                            compQty,
                            compUnitPrice,
                            detailDto.TaxPercent,
                            detailDto.Note,
                            detailDto.ProductId,
                            isBundleFreeGift));
                    }
                    continue;
                }

                var matchedLine = salesOrderLines.FirstOrDefault(l =>
                    !usedOrderLineIds.Contains(l.Id) &&
                    l.ProductId == detailDto.ProductId &&
                    l.OrderedQuantity == detailDto.Quantity &&
                    l.UnitPrice == detailDto.UnitPrice)
                    ?? salesOrderLines.FirstOrDefault(l =>
                        !usedOrderLineIds.Contains(l.Id) &&
                        l.ProductId == detailDto.ProductId);

                if (matchedLine is not null)
                {
                    usedOrderLineIds.Add(matchedLine.Id);
                }

                var isFreeGift = (matchedLine?.IsFreeGift == true) ||
                                 detailDto.UnitPrice == 0m ||
                                 (detailDto.UnitPrice == 0m && giftProductIds.Contains(detailDto.ProductId));
                var parentBundleId = matchedLine?.ParentBundleProductId;
                var effectiveUnitPrice = isFreeGift ? 0m : detailDto.UnitPrice;

                expandedDetails.Add((
                    detailDto.ProductId,
                    detailDto.Quantity,
                    effectiveUnitPrice,
                    detailDto.TaxPercent,
                    detailDto.Note,
                    parentBundleId,
                    isFreeGift));
            }

            var requiredByProduct = expandedDetails
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var checkedProducts = new HashSet<int>();

            foreach (var item in expandedDetails)
            {
                var stock = await _inventoryStockRepository.GetByProductAndWarehouseAsync(item.ProductId, dto.WarehouseId);
                if (stock == null)
                    throw new BusinessRuleException($"Không tìm thấy thông tin tồn kho khả dụng cho sản phẩm ID {item.ProductId} tại kho ID {dto.WarehouseId}");
                if (stock.Product == null || !stock.Product.IsActive || stock.Product.Unit == null || !stock.Product.Unit.IsActive)
                    throw new BusinessRuleException($"Sản phẩm ID {item.ProductId} hoặc Base UOM không hoạt động.");
                if (decimal.Round(item.Quantity, stock.Product.Unit.DecimalPlaces) != item.Quantity)
                    throw new BusinessRuleException($"Số lượng sản phẩm ID {item.ProductId} vượt quá độ chính xác Base UOM cho phép ({stock.Product.Unit.DecimalPlaces} chữ số thập phân).");

                if (checkedProducts.Add(item.ProductId))
                {
                    var currentStock = await _inventoryStockRepository.GetAvailableQuantityAsync(item.ProductId, dto.WarehouseId);
                    var totalRequestedForProduct = requiredByProduct[item.ProductId];
                    if (totalRequestedForProduct > currentStock)
                    {
                        throw new BusinessRuleException($"Sản phẩm có ID {item.ProductId} không đủ tồn kho. Tồn khả dụng: {currentStock}, yêu cầu xuất: {totalRequestedForProduct}");
                    }
                }

                var baseUomCode = NormalizeUomValue(stock.Product.Unit.Code);
                var baseUomName = baseUomCode == "THÙNG" ? "THÙNG" : NormalizeUomValue(stock.Product.Unit.Name);
                receipt.Details.Add(new ERP.Domain.Entities.ExportReceiptDetail
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TaxPercent = item.TaxPercent,
                    ParentBundleProductId = item.ParentBundleProductId,
                    IsFreeGift = item.IsFreeGift,
                    Note = item.Note?.Trim(),
                    BaseUomIdSnapshot = stock.Product.Unit.Id,
                    BaseUomCodeSnapshot = baseUomCode,
                    BaseUomNameSnapshot = baseUomName,
                    BaseUomDecimalPlacesSnapshot = stock.Product.Unit.DecimalPlaces
                });
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _exportReceiptRepository.AddAsync(receipt);
                
                await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                {
                    UserId = userId,
                    Action = "ExportReceipt.Created",
                    EntityName = "ExportReceipt",
                    EntityId = receipt.Id,
                    WarehouseId = receipt.WarehouseId,
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await _unitOfWork.CommitTransactionAsync();

                var createdReceipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(receipt.Id);
                return MapToDto(createdReceipt!);
            }
            catch
            {
                try { await _unitOfWork.RollbackTransactionAsync(); } catch { }
                throw;
            }
        }

        public Task ApproveAsync(int id, int approvedByUserId) =>
            ApproveAndReserveAsync(id, approvedByUserId);

        public Task ApproveAndReserveAsync(int id, int approvedByUserId) =>
            ApproveCoreAsync(id, approvedByUserId, ExportDispatchMode.RequireSeparateDispatch);

        public Task ApproveAndDispatchAsync(int id, int approvedByUserId) =>
            ApproveCoreAsync(id, approvedByUserId, ExportDispatchMode.DispatchOnApproval);

        private async Task ApproveCoreAsync(int id, int approvedByUserId, ExportDispatchMode mode)
        {
            EnsureWriteEnabled();
            if (_currentUser is { UserId: > 0 }) approvedByUserId = _currentUser.UserId;
            var maxDeadlockAttempts = _unitOfWork.HasExternalTransaction ? 1 : 4;
            for (var attempt = 1; attempt <= maxDeadlockAttempts; attempt++)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var receipt = await _exportReceiptRepository.GetByIdWithDetailsAsync(id);
                    if (receipt == null) throw new NotFoundException($"Không tìm thấy phiếu xuất id {id}");
                    if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(receipt.WarehouseId);
                    EnsureNotCancelledOrPendingPutback(receipt);
                    Security.ApprovalSafetyGuard.EnsureDifferentChecker(receipt.CreatedBy, approvedByUserId);
                    if (receipt.Status != ReceiptStatus.Draft)
                        throw new ConcurrencyException("Phiếu xuất đã được xử lý hoặc đang được xử lý bởi yêu cầu khác.");
                    if (receipt.Details == null || !receipt.Details.Any())
                        throw new BusinessRuleException("Phiếu xuất phải có ít nhất 1 sản phẩm để duyệt");
                    if (_stockReservationService is null)
                        throw new InvalidOperationException("Stock reservation service is required for export approval.");

                    receipt.CustomerCodeSnapshot = receipt.Customer?.Code;
                    receipt.CustomerNameSnapshot = receipt.Customer?.Name;
                    receipt.DispatchMode = mode;
                    receipt.ApprovedBy = approvedByUserId;
                    receipt.ApprovedAt = DateTime.UtcNow;

                    await _inventoryStockRepository.AcquireAllocationLocksAsync(
                        receipt.WarehouseId,
                        receipt.Details.Select(detail => detail.ProductId));

                    foreach (var productGroup in receipt.Details.GroupBy(detail => detail.ProductId).OrderBy(g => g.Key))
                    {
                        var groupDetails = productGroup.ToList();
                        var totalProductQuantity = groupDetails.Sum(d => d.Quantity);
                        var reservation = await _stockReservationService.ReserveForExportAsync(
                            receipt.Id, receipt.Code, receipt.WarehouseId, productGroup.Key, totalProductQuantity, approvedByUserId);
                        if (mode == ExportDispatchMode.DispatchOnApproval)
                        {
                            var consumptions = await _stockReservationService.ConsumeAsync(reservation, approvedByUserId);
                            if (groupDetails.Count == 1)
                            {
                                await AddExportTransactionsAsync(receipt, groupDetails[0], consumptions, approvedByUserId);
                            }
                            else
                            {
                                await AddGroupedExportTransactionsAsync(receipt, groupDetails, consumptions, approvedByUserId);
                            }
                        }
                    }

                    if (mode == ExportDispatchMode.DispatchOnApproval)
                    {
                        receipt.Status = ReceiptStatus.Dispatched;
                        receipt.DispatchedBy = approvedByUserId;
                        receipt.DispatchedAt = DateTime.UtcNow;
                    }
                    else receipt.Status = ReceiptStatus.Approved;

                    await _exportReceiptRepository.UpdateAsync(receipt);
                    if (mode == ExportDispatchMode.DispatchOnApproval && _partnerDebt is not null)
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
                                approvedByUserId);
                        }
                    }
                    await _auditLogRepository.AddAsync(new ERP.Domain.Entities.AuditLog
                    {
                        UserId = approvedByUserId,
                        Action = mode == ExportDispatchMode.DispatchOnApproval ? "ExportReceipt.ApprovedAndDispatched" : "ExportReceipt.ApprovedAndReserved",
                        EntityName = "ExportReceipt",
                        EntityId = receipt.Id,
                        WarehouseId = receipt.WarehouseId,
                        Result = "Success",
                        Severity = "Information",
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {ReceiptStatus.Draft}",
                        NewValues = $"WarehouseId: {receipt.WarehouseId}; DispatchMode: {mode}; Status: {receipt.Status}"
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
    }
}
