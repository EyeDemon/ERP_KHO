using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class StocktakeService : IStocktakeService
    {
        private readonly IStocktakeRepository _stocktakeRepository;
        private readonly IInventoryStockRepository _inventoryStockRepository;
        private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly ICurrentUser? _currentUser;
        private readonly IInventoryLockEvaluator? _inventoryLocks;
        private readonly IProductRepository? _productRepository;

        internal StocktakeService(
            IStocktakeRepository stocktakeRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IInventoryLockEvaluator? inventoryLocks = null,
            IProductRepository? productRepository = null)
        {
            _stocktakeRepository = stocktakeRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _inventoryLocks = inventoryLocks;
            _productRepository = productRepository;
        }

        public StocktakeService(
            IStocktakeRepository stocktakeRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            ICurrentUser currentUser,
            IInventoryLockEvaluator? inventoryLocks = null,
            IProductRepository? productRepository = null)
        {
            _stocktakeRepository = stocktakeRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _currentUser = currentUser;
            _inventoryLocks = inventoryLocks;
            _productRepository = productRepository;
        }

                public async Task<int> CreateStocktakeAsync(DTOs.CreateStocktakeDto dto, int createdByUserId)
        {
            if (_currentUser is not null) createdByUserId = _currentUser.UserId;
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId);
            var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho id {dto.WarehouseId}");

            var stocks = (await _inventoryStockRepository.FindAsync(s => s.WarehouseId == dto.WarehouseId && s.Status == InventoryStatus.Available))
                .Where(s => s.Status == InventoryStatus.Available)
                .ToList();
            if (dto.LocationId.HasValue)
            {
                stocks = stocks.Where(s => s.LocationId == dto.LocationId.Value).ToList();
            }

            var code = $"KK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..16].ToUpperInvariant()}";
            var normalizedNote = dto.Note?.Trim();
            if (dto.IsBlindCount)
            {
                normalizedNote = string.IsNullOrWhiteSpace(normalizedNote)
                    ? "[BLIND] Kiểm kê mù"
                    : normalizedNote.StartsWith("[BLIND]", StringComparison.OrdinalIgnoreCase)
                        ? normalizedNote
                        : $"[BLIND] {normalizedNote}";
            }
            else
            {
                normalizedNote = dto.Note;
            }

            var stocktake = new Stocktake
            {
                Code = code,
                WarehouseId = dto.WarehouseId,
                Status = ReceiptStatus.Draft,
                CreatedBy = createdByUserId,
                CreatedAt = DateTime.UtcNow,
                Note = normalizedNote,
                Details = stocks
                    .GroupBy(s => new { s.ProductId, s.LocationId })
                    .Select(g => new StocktakeDetail
                    {
                        ProductId = g.Key.ProductId,
                        LocationId = g.Key.LocationId,
                        SystemQuantity = g.Sum(s => s.Quantity),
                        DifferenceQuantity = 0
                    }).ToList()
            };

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _stocktakeRepository.AddAsync(stocktake);
                await _unitOfWork.SaveChangesAsync();

                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = createdByUserId,
                    Action = "Stocktake.Created",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"Status: {ReceiptStatus.Draft}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"WarehouseId: {dto.WarehouseId}, Note: {dto.Note}"
                });

                await _unitOfWork.CommitTransactionAsync();
                return stocktake.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task ApproveStocktakeAsync(int id, int approvedByUserId)
        {
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(id);
                if (stocktake == null) throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {id}");
                if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

                Security.ApprovalSafetyGuard.EnsureDifferentChecker(stocktake.CreatedBy, approvedByUserId);

                if (stocktake.Status != ReceiptStatus.Draft)
                    throw new BusinessRuleException("Chỉ có thể duyệt phiếu ở trạng thái nháp");

                stocktake.Status = ReceiptStatus.Approved;
                stocktake.ApprovedBy = approvedByUserId;
                stocktake.ApprovedAt = DateTime.UtcNow;

                foreach (var detail in stocktake.Details.OrderBy(d => d.ProductId).ThenBy(d => d.LocationId ?? 0).ThenBy(d => d.Id))
                {
                    if (detail.ActualQuantity == null)
                        throw new BusinessRuleException($"Chưa nhập số lượng thực tế cho sản phẩm id {detail.ProductId}");

                    var currentStock = detail.LocationId.HasValue
                        ? await _inventoryStockRepository.GetByProductWarehouseStatusAndLocationAsync(detail.ProductId, stocktake.WarehouseId, InventoryStatus.Available, detail.LocationId.Value)
                        : await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, stocktake.WarehouseId);

                    var matchingStocks = (await _inventoryStockRepository.FindAsync(s =>
                            s.ProductId == detail.ProductId &&
                            s.WarehouseId == stocktake.WarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value)))
                        ?.Where(s => s.Status == InventoryStatus.Available && (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value))
                        .OrderBy(s => s.LotId)
                        .ThenBy(s => s.SerialId)
                        .ThenBy(s => s.Id)
                        .ToList() ?? new List<InventoryStock>();

                    if (matchingStocks.Count == 0 && currentStock != null)
                    {
                        matchingStocks.Add(currentStock);
                    }
                    currentStock ??= matchingStocks.FirstOrDefault();

                    var targetLocationId = detail.LocationId ?? currentStock?.LocationId;
                    var isLocked = (_inventoryLocks is not null && await _inventoryLocks.HasActiveLockAsync(stocktake.WarehouseId, targetLocationId, detail.ProductId, InventoryStatus.Available, currentStock?.LotId, currentStock?.SerialId)) ||
                                   await _inventoryStockRepository.IsLockedAsync(stocktake.WarehouseId, detail.ProductId, targetLocationId, currentStock?.LotId, currentStock?.SerialId);
                    if (!isLocked && matchingStocks.Count > 1)
                    {
                        foreach (var bucket in matchingStocks.Skip(1))
                        {
                            var bucketLocationId = detail.LocationId ?? bucket.LocationId;
                            if ((_inventoryLocks is not null && await _inventoryLocks.HasActiveLockAsync(stocktake.WarehouseId, bucketLocationId, detail.ProductId, InventoryStatus.Available, bucket.LotId, bucket.SerialId)) ||
                                await _inventoryStockRepository.IsLockedAsync(stocktake.WarehouseId, detail.ProductId, bucketLocationId, bucket.LotId, bucket.SerialId))
                            {
                                isLocked = true;
                                break;
                            }
                        }
                    }
                    if (isLocked)
                    {
                        ThrowConflict($"Không thể duyệt kiểm kê: tồn kho của sản phẩm ID {detail.ProductId} đang bị khóa bởi InventoryLock.");
                    }

                    var currentQuantity = matchingStocks.Sum(s => s.Quantity);
                    if (currentQuantity != detail.SystemQuantity)
                    {
                        ThrowConflict($"Không thể duyệt kiểm kê: tồn kho hiện tại ({currentQuantity}) của sản phẩm ID {detail.ProductId} đã thay đổi so với ảnh chụp kiểm kê ({detail.SystemQuantity}). Vui lòng kiểm đếm hoặc chụp lại phiếu.");
                    }

                    var totalReserved = matchingStocks.Sum(s => s.ReservedQuantity);
                    if (detail.ActualQuantity.Value < totalReserved)
                        ThrowConflict($"Không thể duyệt kiểm kê: sản phẩm ID {detail.ProductId} có tồn thực tế {detail.ActualQuantity.Value} thấp hơn số lượng đang giữ {totalReserved}. Hãy xử lý reservation trước.");

                    var diff = detail.ActualQuantity.Value - currentQuantity;
                    if (diff != 0)
                    {
                        var product = detail.Product
                            ?? currentStock?.Product
                            ?? matchingStocks.FirstOrDefault(s => s.Product != null)?.Product;
                        if (product == null && _productRepository is not null)
                        {
                            product = await _productRepository.GetByIdAsync(detail.ProductId);
                        }

                        var isSerialTracked = product?.TrackingType == ProductTrackingType.Serial ||
                                              matchingStocks.Any(s => s.SerialId.HasValue);
                        var isLotTracked = !isSerialTracked &&
                                           (product?.TrackingType == ProductTrackingType.Lot ||
                                            product?.ExpiryControl == true ||
                                            matchingStocks.Any(s => s.LotId.HasValue));

                        if (isSerialTracked && decimal.Truncate(detail.ActualQuantity.Value) != detail.ActualQuantity.Value)
                        {
                            throw new BusinessRuleException($"Sản phẩm quản lý theo Serial (ID {detail.ProductId}) chỉ được kiểm kê theo số lượng nguyên.");
                        }

                        if (diff > 0)
                        {
                            if (isSerialTracked)
                            {
                                var zeroSerialBuckets = matchingStocks
                                    .Where(s => s.SerialId.HasValue && s.Quantity == 0m)
                                    .OrderBy(s => s.SerialId)
                                    .ThenBy(s => s.Id)
                                    .ToList();
                                var neededSerials = (int)diff;
                                if (decimal.Truncate(diff) != diff || zeroSerialBuckets.Count < neededSerials)
                                {
                                    throw new BusinessRuleException(
                                        $"Không thể duyệt kiểm kê thừa ({diff}) cho sản phẩm quản lý theo Serial (ID {detail.ProductId}) khi chưa xác định mã Serial cụ thể. Vui lòng tạo phiếu nhập/điều chỉnh có định danh Serial.");
                                }

                                foreach (var serialBucket in zeroSerialBuckets.Take(neededSerials))
                                {
                                    serialBucket.Quantity = 1m;
                                    serialBucket.LastUpdated = DateTime.UtcNow;
                                    await _inventoryStockRepository.UpdateAsync(serialBucket);

                                    var serialTx = new InventoryTransaction
                                    {
                                        ProductId = detail.ProductId,
                                        WarehouseId = stocktake.WarehouseId,
                                        LocationId = detail.LocationId ?? serialBucket.LocationId,
                                        LotId = serialBucket.LotId,
                                        SerialId = serialBucket.SerialId,
                                        InventoryStatus = serialBucket.Status,
                                        TransactionType = TransactionType.AdjustmentIncrease,
                                        Quantity = 1m,
                                        ReferenceId = stocktake.Id,
                                        ReferenceType = "Stocktake",
                                        TransactionDate = DateTime.UtcNow,
                                        CreatedBy = approvedByUserId,
                                        Note = detail.Note
                                    };
                                    await _inventoryTransactionRepository.AddAsync(serialTx);
                                }
                            }
                            else if (isLotTracked)
                            {
                                var today = DateTime.UtcNow.Date;
                                var lotBucket = matchingStocks.FirstOrDefault(s =>
                                        s.LotId.HasValue &&
                                        s.SerialId == null &&
                                        (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value >= today))
                                    ?? matchingStocks.FirstOrDefault(s => s.LotId.HasValue && s.SerialId == null);

                                if (lotBucket == null || !lotBucket.LotId.HasValue)
                                {
                                    throw new BusinessRuleException(
                                        $"Không thể duyệt kiểm kê thừa ({diff}) cho sản phẩm quản lý theo Lô (ID {detail.ProductId}) khi vị trí chưa có thông tin Lô. Vui lòng định danh Lô trước khi điều chỉnh.");
                                }

                                lotBucket.Quantity += diff;
                                lotBucket.LastUpdated = DateTime.UtcNow;
                                await _inventoryStockRepository.UpdateAsync(lotBucket);

                                var lotTx = new InventoryTransaction
                                {
                                    ProductId = detail.ProductId,
                                    WarehouseId = stocktake.WarehouseId,
                                    LocationId = detail.LocationId ?? lotBucket.LocationId,
                                    LotId = lotBucket.LotId,
                                    SerialId = null,
                                    InventoryStatus = lotBucket.Status,
                                    TransactionType = TransactionType.AdjustmentIncrease,
                                    Quantity = diff,
                                    ReferenceId = stocktake.Id,
                                    ReferenceType = "Stocktake",
                                    TransactionDate = DateTime.UtcNow,
                                    CreatedBy = approvedByUserId,
                                    Note = detail.Note
                                };
                                await _inventoryTransactionRepository.AddAsync(lotTx);
                            }
                            else
                            {
                                if (currentStock != null && currentStock.SerialId != null && currentStock.Quantity + diff > 1m)
                                {
                                    currentStock = matchingStocks.FirstOrDefault(s => s.SerialId == null);
                                }

                                if (currentStock == null)
                                {
                                    var locationId = detail.LocationId ?? matchingStocks.FirstOrDefault()?.LocationId ?? await _inventoryStockRepository.GetLegacyAdjustmentLocationIdAsync(stocktake.WarehouseId);
                                    currentStock = new InventoryStock
                                    {
                                        ProductId = detail.ProductId,
                                        WarehouseId = stocktake.WarehouseId,
                                        LocationId = locationId,
                                        LotId = matchingStocks.FirstOrDefault()?.LotId,
                                        Status = InventoryStatus.Available,
                                        Quantity = diff,
                                        LastUpdated = DateTime.UtcNow
                                    };
                                    await _inventoryStockRepository.AddAsync(currentStock);
                                }
                                else
                                {
                                    currentStock.Quantity += diff;
                                    currentStock.LastUpdated = DateTime.UtcNow;
                                    await _inventoryStockRepository.UpdateAsync(currentStock);
                                }

                                var tx = new InventoryTransaction
                                {
                                    ProductId = detail.ProductId,
                                    WarehouseId = stocktake.WarehouseId,
                                    LocationId = detail.LocationId ?? currentStock.LocationId,
                                    LotId = currentStock.LotId,
                                    SerialId = currentStock.SerialId,
                                    InventoryStatus = currentStock.Status,
                                    TransactionType = TransactionType.AdjustmentIncrease,
                                    Quantity = diff,
                                    ReferenceId = stocktake.Id,
                                    ReferenceType = "Stocktake",
                                    TransactionDate = DateTime.UtcNow,
                                    CreatedBy = approvedByUserId,
                                    Note = detail.Note
                                };
                                await _inventoryTransactionRepository.AddAsync(tx);
                            }
                        }
                        else
                        {
                            var remainingDecrease = -diff;
                            foreach (var bucket in matchingStocks)
                            {
                                if (remainingDecrease <= 0) break;
                                var availableInBucket = bucket.Quantity - bucket.ReservedQuantity;
                                if (availableInBucket <= 0) continue;
                                var take = Math.Min(remainingDecrease, availableInBucket);
                                if (bucket.SerialId.HasValue && take != 1m)
                                {
                                    throw new BusinessRuleException($"Sản phẩm quản lý theo Serial (ID {detail.ProductId}) chỉ được điều chỉnh giảm theo từng đơn vị nguyên.");
                                }
                                bucket.Quantity -= take;
                                bucket.LastUpdated = DateTime.UtcNow;
                                await _inventoryStockRepository.UpdateAsync(bucket);
                                remainingDecrease -= take;

                                var tx = new InventoryTransaction
                                {
                                    ProductId = detail.ProductId,
                                    WarehouseId = stocktake.WarehouseId,
                                    LocationId = detail.LocationId ?? bucket.LocationId,
                                    LotId = bucket.LotId,
                                    SerialId = bucket.SerialId,
                                    InventoryStatus = bucket.Status,
                                    TransactionType = TransactionType.AdjustmentDecrease,
                                    Quantity = take,
                                    ReferenceId = stocktake.Id,
                                    ReferenceType = "Stocktake",
                                    TransactionDate = DateTime.UtcNow,
                                    CreatedBy = approvedByUserId,
                                    Note = detail.Note
                                };
                                await _inventoryTransactionRepository.AddAsync(tx);
                            }

                            if (remainingDecrease > 0)
                            {
                                throw new BusinessRuleException($"Không đủ tồn kho khả dụng chưa giữ chỗ để điều chỉnh giảm {(-diff)} cho sản phẩm ID {detail.ProductId}.");
                            }
                        }
                    }
                    
                    detail.DifferenceQuantity = diff;
                }

                await _stocktakeRepository.UpdateAsync(stocktake);

                decimal totalAdjustments = stocktake.Details.Sum(d => Math.Abs(d.DifferenceQuantity));
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = approvedByUserId,
                    Action = "Stocktake.Approved",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"Status: {ReceiptStatus.Draft}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"Status: Approved, TotalAdjustments: {totalAdjustments}"
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch (ERP.Domain.Exceptions.ConcurrencyException)
            {
                await _unitOfWork.RollbackTransactionAsync();
                ThrowConflict("Dữ liệu đã bị thay đổi bởi người khác, vui lòng thử lại.");
            }
            catch (Exception)
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        private static void ThrowConflict(string message)
        {
            var conflict = new BusinessRuleException(message);
            conflict.Data["HttpStatusCode"] = 409;
            conflict.Data["ErrorCode"] = "CONCURRENCY_CONFLICT";
            throw conflict;
        }
        public async Task UpdateStocktakeDetailAsync(int stocktakeId, int detailId, DTOs.UpdateStocktakeDetailDto dto)
        {
            if (dto.ActualQuantity < 0)
                throw new BusinessRuleException("Số lượng thực tế không được âm.");
            if (decimal.Round(dto.ActualQuantity, 4) != dto.ActualQuantity)
                throw new BusinessRuleException("Số lượng kiểm đếm thực tế chỉ hỗ trợ tối đa 4 chữ số thập phân.");

            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(stocktakeId);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {stocktakeId}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            if (stocktake.Status != ReceiptStatus.Draft)
                throw new BusinessRuleException("Chỉ có thể cập nhật chi tiết phiếu ở trạng thái nháp");

            var detail = stocktake.Details.FirstOrDefault(d => d.Id == detailId);
            if (detail == null)
                throw new NotFoundException($"Không tìm thấy chi tiết phiếu id {detailId}");

            var decimalPlaces = detail.Product?.Unit?.DecimalPlaces;
            if (decimalPlaces is >= 0 and <= 4 && decimal.Round(dto.ActualQuantity, decimalPlaces.Value) != dto.ActualQuantity)
                throw new BusinessRuleException($"Số lượng kiểm đếm thực tế vượt quá {decimalPlaces.Value} chữ số thập phân của đơn vị tính cơ sở.");

            var isBlindDraft = IsBlindStocktake(stocktake.Note) && stocktake.Status == ReceiptStatus.Draft;
            var oldActualQuantity = detail.ActualQuantity;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                detail.ActualQuantity = dto.ActualQuantity;
                detail.DifferenceQuantity = isBlindDraft ? 0m : dto.ActualQuantity - detail.SystemQuantity;
                if (dto.Note != null) detail.Note = dto.Note;

                await _stocktakeRepository.UpdateAsync(stocktake);
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = _currentUser?.UserId,
                    Action = "Stocktake.DetailUpdated",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"DetailId: {detail.Id}; ActualQuantity: {oldActualQuantity}",
                    NewValues = $"DetailId: {detail.Id}; ActualQuantity: {detail.ActualQuantity}",
                    Result = "Success",
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

        public static bool IsBlindStocktake(string? note) =>
            !string.IsNullOrWhiteSpace(note) &&
            note.TrimStart().StartsWith("[BLIND]", StringComparison.OrdinalIgnoreCase);

        public static DTOs.StocktakeResponseDto MapToDto(Stocktake stocktake)
        {
            var maskBlindSnapshot = stocktake.Status == ReceiptStatus.Draft && IsBlindStocktake(stocktake.Note);
            return new DTOs.StocktakeResponseDto
            {
                Id = stocktake.Id,
                Code = stocktake.Code,
                WarehouseId = stocktake.WarehouseId,
                WarehouseName = stocktake.Warehouse?.Name ?? string.Empty,
                Status = stocktake.Status,
                Note = stocktake.Note,
                CreatedBy = stocktake.CreatedBy,
                CreatedByName = stocktake.CreatedByUser?.FullName ?? string.Empty,
                ApprovedBy = stocktake.ApprovedBy,
                ApprovedByName = stocktake.ApprovedByUser?.FullName,
                CreatedAt = stocktake.CreatedAt,
                ApprovedAt = stocktake.ApprovedAt,
                DetailCount = stocktake.Details.Count,
                Details = stocktake.Details.Select(d => new DTOs.StocktakeDetailRowDto
                {
                    Id = d.Id,
                    StocktakeId = d.StocktakeId,
                    ProductId = d.ProductId,
                    LocationId = d.LocationId,
                    LocationCode = d.Location?.Code,
                    LocationName = d.Location?.Name,
                    ProductCode = d.Product?.Code ?? string.Empty,
                    ProductName = d.Product?.Name ?? string.Empty,
                    UnitName = d.Product?.Unit?.Name ?? string.Empty,
                    SystemQuantity = maskBlindSnapshot ? 0m : d.SystemQuantity,
                    ActualQuantity = d.ActualQuantity,
                    DifferenceQuantity = maskBlindSnapshot ? 0m : d.DifferenceQuantity,
                    Note = d.Note
                }).ToList()
            };
        }

        public async Task RecountStocktakeDetailAsync(int stocktakeId, int detailId, string? reason, int userId = 0)
        {
            if (_currentUser is not null && _currentUser.UserId > 0) userId = _currentUser.UserId;
            var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(stocktakeId);
            if (stocktake == null)
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {stocktakeId}");
            if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

            if (stocktake.Status != ReceiptStatus.Draft)
                throw new BusinessRuleException("Chỉ có thể yêu cầu đếm lại chi tiết phiếu ở trạng thái nháp");

            var detail = stocktake.Details.FirstOrDefault(d => d.Id == detailId);
            if (detail == null)
                throw new NotFoundException($"Không tìm thấy chi tiết phiếu id {detailId}");

            var oldSystemQuantity = detail.SystemQuantity;
            var oldActualQuantity = detail.ActualQuantity;
            var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Yêu cầu kiểm đếm lại" : reason.Trim();
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var currentStock = detail.LocationId.HasValue
                    ? await _inventoryStockRepository.GetByProductWarehouseStatusAndLocationAsync(detail.ProductId, stocktake.WarehouseId, InventoryStatus.Available, detail.LocationId.Value)
                    : await _inventoryStockRepository.GetByProductAndWarehouseAsync(detail.ProductId, stocktake.WarehouseId);

                var matchingStocks = (await _inventoryStockRepository.FindAsync(s =>
                        s.ProductId == detail.ProductId &&
                        s.WarehouseId == stocktake.WarehouseId &&
                        s.Status == InventoryStatus.Available &&
                        (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value)))
                    ?.Where(s => s.Status == InventoryStatus.Available && (!detail.LocationId.HasValue || s.LocationId == detail.LocationId.Value))
                    .ToList() ?? new List<InventoryStock>();

                if (matchingStocks.Count == 0 && currentStock != null)
                {
                    matchingStocks.Add(currentStock);
                }

                detail.SystemQuantity = matchingStocks.Sum(s => s.Quantity);
                detail.ActualQuantity = null;
                detail.DifferenceQuantity = 0;
                detail.Note = string.IsNullOrWhiteSpace(detail.Note)
                    ? $"[RECOUNT: {cleanReason}]"
                    : $"{detail.Note} | [RECOUNT: {cleanReason}]";

                await _stocktakeRepository.UpdateAsync(stocktake);
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = userId > 0 ? userId : _currentUser?.UserId,
                    Action = "RECOUNT_STOCKTAKE_LINE",
                    EntityName = "Stocktake",
                    EntityId = stocktake.Id,
                    WarehouseId = stocktake.WarehouseId,
                    OldValues = $"DetailId: {detail.Id}; SystemQuantity: {oldSystemQuantity}; ActualQuantity: {oldActualQuantity}",
                    NewValues = $"DetailId: {detail.Id}; SystemQuantity: {detail.SystemQuantity}; ActualQuantity: null; Note: {detail.Note}",
                    Reason = cleanReason,
                    Result = "Success",
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

