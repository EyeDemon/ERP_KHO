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

        internal StocktakeService(
            IStocktakeRepository stocktakeRepository,
            IInventoryStockRepository inventoryStockRepository,
            IInventoryTransactionRepository inventoryTransactionRepository,
            IWarehouseRepository warehouseRepository,
            IUnitOfWork unitOfWork,
            IAuditLogRepository auditLogRepository,
            IInventoryLockEvaluator? inventoryLocks = null)
        {
            _stocktakeRepository = stocktakeRepository;
            _inventoryStockRepository = inventoryStockRepository;
            _inventoryTransactionRepository = inventoryTransactionRepository;
            _warehouseRepository = warehouseRepository;
            _unitOfWork = unitOfWork;
            _auditLogRepository = auditLogRepository;
            _inventoryLocks = inventoryLocks;
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
            IInventoryLockEvaluator? inventoryLocks = null)
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
                        if (diff > 0)
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
                        else
                        {
                            var remainingDecrease = -diff;
                            foreach (var bucket in matchingStocks)
                            {
                                if (remainingDecrease <= 0) break;
                                var availableInBucket = bucket.Quantity - bucket.ReservedQuantity;
                                if (availableInBucket <= 0) continue;
                                var take = Math.Min(remainingDecrease, availableInBucket);
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

            var oldActualQuantity = detail.ActualQuantity;
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                detail.ActualQuantity = dto.ActualQuantity;
                detail.DifferenceQuantity = dto.ActualQuantity - detail.SystemQuantity;
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

