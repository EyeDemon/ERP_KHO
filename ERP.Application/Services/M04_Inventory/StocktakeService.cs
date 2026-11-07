using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public partial class StocktakeService : IStocktakeService
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

        public async Task ApproveStocktakeAsync(int id, int approvedByUserId)
        {
            if (_currentUser is not null) approvedByUserId = _currentUser.UserId;
            const int maxAttempts = 4;
            for (var attempt = 1; ; attempt++)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var stocktake = await _stocktakeRepository.GetByIdWithDetailsAsync(id);
                    if (stocktake == null) throw new NotFoundException($"Không tìm thấy phiếu kiểm kê id {id}");
                    if (_warehouseAuthorization is not null) await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);

                    Security.ApprovalSafetyGuard.EnsureDifferentChecker(stocktake.CreatedBy, approvedByUserId);

                    if (stocktake.Status is not (StocktakeStatus.Draft
                        or StocktakeStatus.InProgress
                        or StocktakeStatus.CountCompleted
                        or StocktakeStatus.PendingApproval
                        or StocktakeStatus.PendingRecount))
                    {
                        var stateConflict = new BusinessRuleException("Chỉ có thể duyệt phiếu ở trạng thái nháp hoặc đã hoàn tất kiểm đếm");
                        stateConflict.Data["HttpStatusCode"] = 409;
                        stateConflict.Data["ErrorCode"] = "STOCKTAKE_STATE_CONFLICT";
                        throw stateConflict;
                    }

                    foreach (var detailCheck in stocktake.Details.OrderBy(d => d.ProductId).ThenBy(d => d.LocationId ?? 0).ThenBy(d => d.Id))
                    {
                        if (detailCheck.ActualQuantity == null)
                            throw new BusinessRuleException($"Chưa nhập số lượng thực tế cho sản phẩm id {detailCheck.ProductId}");
                    }

                    var oldStatus = stocktake.Status;
                    var nowUtc = DateTime.UtcNow;
                    if (stocktake.Status is StocktakeStatus.Draft or StocktakeStatus.InProgress or StocktakeStatus.PendingRecount)
                    {
                        stocktake.TransitionTo(StocktakeStatus.CountCompleted, approvedByUserId, nowUtc);
                    }
                    stocktake.TransitionTo(StocktakeStatus.Approved, approvedByUserId, nowUtc);
                    stocktake.ApprovedBy = approvedByUserId;
                    stocktake.ApprovedAt = nowUtc;

                    await _inventoryStockRepository.AcquireAllocationLocksAsync(
                        stocktake.WarehouseId,
                        stocktake.Details.Select(d => d.ProductId));

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
                        var totalReserved = matchingStocks.Sum(s => s.ReservedQuantity);
                        if (detail.ActualQuantity.Value < totalReserved)
                        {
                            ThrowConflict(
                                $"Không thể duyệt kiểm kê: sản phẩm ID {detail.ProductId} có tồn thực tế {detail.ActualQuantity.Value} thấp hơn số lượng đang giữ {totalReserved}. Hãy xử lý reservation trước.",
                                "STOCKTAKE_BELOW_RESERVED_QUANTITY");
                        }

                        if (currentQuantity != detail.SystemQuantity)
                        {
                            ThrowConflict($"Không thể duyệt kiểm kê: tồn kho hiện tại ({currentQuantity}) của sản phẩm ID {detail.ProductId} đã thay đổi so với ảnh chụp kiểm kê ({detail.SystemQuantity}). Vui lòng kiểm đếm hoặc chụp lại phiếu.");
                        }

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
                                    ThrowConflict(
                                        $"Không đủ tồn kho khả dụng chưa giữ chỗ để điều chỉnh giảm {(-diff)} cho sản phẩm ID {detail.ProductId}. Hãy xử lý reservation trước.",
                                        "STOCKTAKE_BELOW_RESERVED_QUANTITY");
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
                        OldValues = $"Status: {oldStatus}",
                        Result = "Success",
                        Severity = "Information",
                        Timestamp = DateTime.UtcNow,
                        NewValues = $"Status: Approved, TotalAdjustments: {totalAdjustments}"
                    });

                    await _unitOfWork.CommitTransactionAsync();
                    return;
                }
                catch (ERP.Domain.Exceptions.DeadlockException) when (attempt < maxAttempts)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    await Task.Delay(15 * attempt);
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
        }

        private static void ThrowConflict(string message, string errorCode = "CONCURRENCY_CONFLICT")
        {
            var conflict = new BusinessRuleException(message);
            conflict.Data["HttpStatusCode"] = 409;
            conflict.Data["ErrorCode"] = errorCode;
            throw conflict;
        }
    }
}
