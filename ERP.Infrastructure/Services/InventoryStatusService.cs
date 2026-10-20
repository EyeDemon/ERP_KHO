using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryStatusService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IInventoryLockEvaluator? inventoryLocks = null) : IInventoryStatusService
{
    public async Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default) =>
        await context.InventoryStatusDefinitions.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new InventoryStatusDefinitionDto
            {
                Code = x.Code.Trim(),
                Name = x.Name.Trim(),
                IsAvailable = x.IsAvailable,
                IsReservable = x.IsReservable,
                IsAllocatable = x.IsAllocatable,
                IsPickable = x.IsPickable,
                IsShippable = x.IsShippable
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryBucketDto>> GetBucketsAsync(
        int? warehouseId = null,
        int? productId = null,
        string? status = null,
        string? lotNumber = null,
        string? serialNumber = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = context.InventoryStocks.AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId));
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseStatus(status, out var parsed))
                throw new BusinessRuleException("Inventory status filter không hợp lệ.");
            query = query.Where(x => x.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            var lot = lotNumber.Trim();
            query = query.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        }
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            var serial = serialNumber.Trim();
            query = query.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
        }

        var today = DateTime.UtcNow.Date;
        return await query
            .OrderBy(x => x.Product.Code)
            .ThenBy(x => x.Status)
            .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .Take(1000)
            .Select(x => new InventoryBucketDto
            {
                InventoryStockId = x.Id,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code.Trim(),
                ProductName = x.Product.Name.Trim(),
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name.Trim(),
                LocationId = x.LocationId ?? 0,
                LocationCode = x.Location == null ? string.Empty : x.Location.Code.Trim(),
                Status = x.StatusDefinition != null ? x.StatusDefinition.Code.Trim() : x.Status.ToString().Trim(),
                IsReservable = x.StatusDefinition.IsReservable &&
                               (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsAllocatable = x.StatusDefinition.IsAllocatable &&
                                (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsPickable = x.StatusDefinition.IsPickable &&
                             (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsShippable = x.StatusDefinition.IsShippable &&
                              (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                LotId = x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber.Trim(),
                ManufactureDate = x.Lot == null ? null : x.Lot.ManufactureDate,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber.Trim(),
                OnHandQuantity = x.Quantity,
                ReservedQuantity = x.ReservedQuantity,
                AvailableQuantity = x.StatusDefinition.IsAvailable &&
                                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today)
                    ? x.Quantity - x.ReservedQuantity
                    : 0,
                LastUpdated = x.LastUpdated
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryStatusChangeResultDto> ChangeAsync(
        CreateInventoryStatusChangeDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId và Quantity phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do status change là bắt buộc.");
        if (!TryParseStatus(request.ToStatus, out var toStatus))
            throw new BusinessRuleException("Inventory status đích không hợp lệ.");

        var scope = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.Id == request.InventoryStockId)
            .Select(x => new { x.ProductId, x.WarehouseId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy inventory bucket.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(scope.WarehouseId, cancellationToken);

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var ownTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                await AcquireProductWarehouseLockAndStockLocksAsync(scope.ProductId, scope.WarehouseId, cancellationToken);

                var source = await context.InventoryStocks
                    .Include(x => x.StatusDefinition)
                    .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy inventory bucket.");

                if (source.Status == toStatus)
                    throw new BusinessRuleException("Status đích phải khác status nguồn.");
                ValidateTransition(source.Status, toStatus);
                if (source.Quantity - source.ReservedQuantity < request.Quantity)
                    throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", "Không thể đổi status phần tồn đang reserved/allocation.");
                if (!await context.InventoryStatusDefinitions.AnyAsync(x => x.Id == toStatus, cancellationToken))
                    throw new BusinessRuleException("Inventory status đích chưa được cấu hình.");

                if (inventoryLocks is not null)
                {
                    await inventoryLocks.EnsureBucketUnlockedAsync(
                        source.WarehouseId, source.LocationId, source.ProductId, source.Status, source.LotId, source.SerialId, cancellationToken);
                    await inventoryLocks.EnsureBucketUnlockedAsync(
                        source.WarehouseId, source.LocationId, source.ProductId, toStatus, source.LotId, source.SerialId, cancellationToken);
                }

                var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == source.ProductId &&
                         x.WarehouseId == source.WarehouseId &&
                         x.LocationId == source.LocationId &&
                         x.Status == toStatus &&
                         x.LotId == source.LotId &&
                         x.SerialId == source.SerialId,
                    cancellationToken);

                source.Quantity -= request.Quantity;
                source.LastUpdated = DateTime.UtcNow;
                if (destination is null)
                {
                    destination = new InventoryStock
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        Status = toStatus,
                        Quantity = request.Quantity,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    };
                    context.InventoryStocks.Add(destination);
                }
                else
                {
                    destination.Quantity += request.Quantity;
                    destination.LastUpdated = DateTime.UtcNow;
                }

                var ledger = new InventoryTransaction
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    InventoryStatus = toStatus,
                    FromInventoryStatus = source.Status,
                    ToInventoryStatus = toStatus,
                    TransactionType = TransactionType.StatusChange,
                    Quantity = request.Quantity,
                    ReferenceId = source.Id,
                    ReferenceType = "InventoryStatusChange",
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = currentUser.UserId,
                    Note = request.Reason.Trim()
                };
                context.InventoryTransactions.Add(ledger);
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Action = "Inventory.StatusChanged",
                    EntityName = "InventoryStock",
                    EntityId = source.Id,
                    WarehouseId = source.WarehouseId,
                    Timestamp = DateTime.UtcNow,
                    OldValues = $"Status: {source.Status}; Quantity: {source.Quantity + request.Quantity}",
                    NewValues = $"Status: {toStatus}; MovedQuantity: {request.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                    Reason = request.Reason.Trim(),
                    Result = "Success",
                    Severity = "Information"
                });

                await context.SaveChangesAsync(cancellationToken);
                var result = new InventoryStatusChangeResultDto
                {
                    SourceStockId = source.Id,
                    DestinationStockId = destination.Id,
                    TransactionId = ledger.Id,
                    FromStatus = source.Status.ToString(),
                    ToStatus = toStatus.ToString(),
                    Quantity = request.Quantity,
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId ?? 0,
                    LotId = source.LotId,
                    SerialId = source.SerialId
                };
                if (tx is not null) await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw Conflict("CONCURRENCY_CONFLICT", "Inventory bucket đích đã thay đổi đồng thời.");
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    public async Task<InventoryDispositionResultDto> ExecuteDispositionAsync(
        CreateInventoryDispositionDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId và Quantity phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do xử lý (disposition) là bắt buộc.");

        var normalizedAction = request.Action?.Trim().Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();
        var isRelease = normalizedAction is "RELEASETOAVAILABLE" or "RELEASE";
        var isScrap = normalizedAction is "SCRAP";
        if (!isRelease && !isScrap)
            throw new BusinessRuleException("Hành động disposition không hợp lệ (chỉ hỗ trợ ReleaseToAvailable hoặc Scrap).");

        var scope = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.Id == request.InventoryStockId)
            .Select(x => new { x.ProductId, x.WarehouseId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy inventory bucket.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(scope.WarehouseId, cancellationToken);

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var ownTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                await AcquireProductWarehouseLockAndStockLocksAsync(scope.ProductId, scope.WarehouseId, cancellationToken);

                var source = await context.InventoryStocks
                    .Include(x => x.StatusDefinition)
                    .Include(x => x.Lot)
                    .Include(x => x.Location)
                    .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy inventory bucket.");

                if (source.Status == InventoryStatus.Available)
                    throw Conflict("INV_DISPOSITION_NOT_ALLOWED", "Disposition chỉ áp dụng cho nhóm tồn không ở trạng thái AVAILABLE.");
                if (source.Quantity - source.ReservedQuantity < request.Quantity)
                    throw Conflict("INV_INSUFFICIENT_ON_HAND", "Không đủ tồn khả dụng chưa giữ để xử lý disposition.");

                var targetLocationId = source.LocationId;
                if (isRelease)
                {
                    var needsPickableRelocation = source.Location is null ||
                        !source.Location.IsPickable ||
                        !source.Location.IsActive ||
                        source.Location.IsBlocked ||
                        source.Location.LocationType is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected;
                    if (needsPickableRelocation)
                    {
                        var pickableLocationId = await context.WarehouseLocations
                            .Where(x => x.WarehouseId == source.WarehouseId &&
                                        x.IsActive &&
                                        !x.IsBlocked &&
                                        x.IsPickable &&
                                        x.LocationType != WarehouseLocationType.Damaged &&
                                        x.LocationType != WarehouseLocationType.Rejected)
                            .OrderBy(x => x.LocationType == WarehouseLocationType.Storage ? 0 : x.Code == "LEGACY" ? 1 : 2)
                            .ThenBy(x => x.Id)
                            .Select(x => (int?)x.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (!pickableLocationId.HasValue)
                            throw Conflict("INV_DISPOSITION_NOT_ALLOWED", "Kho chưa có vị trí lưu trữ khả dụng (IsPickable) để giải tỏa tồn kho về AVAILABLE.");
                        targetLocationId = pickableLocationId.Value;
                    }
                }

                if (inventoryLocks is not null)
                {
                    await inventoryLocks.EnsureBucketUnlockedAsync(
                        source.WarehouseId, source.LocationId, source.ProductId, source.Status, source.LotId, source.SerialId, cancellationToken);
                    if (isRelease)
                    {
                        await inventoryLocks.EnsureBucketUnlockedAsync(
                            source.WarehouseId, targetLocationId, source.ProductId, InventoryStatus.Available, source.LotId, source.SerialId, cancellationToken);
                    }
                }

                var fromStatus = source.Status;
                int? destinationStockId = null;
                string? toStatusCode = null;
                InventoryTransaction ledger;

                if (isRelease)
                {
                    if (fromStatus is InventoryStatus.Expired or InventoryStatus.RecallBlocked)
                        throw Conflict("INV_DISPOSITION_NOT_ALLOWED", $"Không thể phát hành (ReleaseToAvailable) nhóm tồn đang ở trạng thái {fromStatus}.");
                    if (source.Lot?.ExpiryDate != null && source.Lot.ExpiryDate.Value.Date < DateTime.UtcNow.Date)
                        throw Conflict("INV_DISPOSITION_EXPIRED_LOT", "Không thể phát hành lô đã hết hạn về trạng thái AVAILABLE.");

                    var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                        x => x.ProductId == source.ProductId &&
                             x.WarehouseId == source.WarehouseId &&
                             x.LocationId == targetLocationId &&
                             x.Status == InventoryStatus.Available &&
                             x.LotId == source.LotId &&
                             x.SerialId == source.SerialId,
                        cancellationToken);

                    source.Quantity -= request.Quantity;
                    source.LastUpdated = DateTime.UtcNow;
                    if (destination is null)
                    {
                        destination = new InventoryStock
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = targetLocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            Status = InventoryStatus.Available,
                            Quantity = request.Quantity,
                            ReservedQuantity = 0,
                            LastUpdated = DateTime.UtcNow
                        };
                        context.InventoryStocks.Add(destination);
                    }
                    else
                    {
                        destination.Quantity += request.Quantity;
                        destination.LastUpdated = DateTime.UtcNow;
                    }

                    ledger = new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = targetLocationId,
                        FromLocationId = source.LocationId,
                        ToLocationId = targetLocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.StatusChange,
                        Quantity = request.Quantity,
                        ReferenceId = source.Id,
                        ReferenceType = "QualityDispositionRelease",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = currentUser.UserId,
                        Note = request.Reason.Trim()
                    };
                    context.InventoryTransactions.Add(ledger);
                    context.AuditLogs.Add(new AuditLog
                    {
                        UserId = currentUser.UserId,
                        Action = "Inventory.DispositionReleased",
                        EntityName = "InventoryStock",
                        EntityId = source.Id,
                        WarehouseId = source.WarehouseId,
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {fromStatus}; LocationId: {source.LocationId}; Quantity: {source.Quantity + request.Quantity}",
                        NewValues = $"Status: {InventoryStatus.Available}; LocationId: {targetLocationId}; ReleasedQuantity: {request.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                        Reason = request.Reason.Trim(),
                        Result = "Success",
                        Severity = "Information"
                    });

                    await context.SaveChangesAsync(cancellationToken);
                    destinationStockId = destination.Id;
                    toStatusCode = ToCode(InventoryStatus.Available);
                }
                else
                {
                    source.Quantity -= request.Quantity;
                    source.LastUpdated = DateTime.UtcNow;

                    ledger = new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        InventoryStatus = fromStatus,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = null,
                        TransactionType = TransactionType.AdjustmentDecrease,
                        Quantity = request.Quantity,
                        ReferenceId = source.Id,
                        ReferenceType = "QualityDispositionScrap",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = currentUser.UserId,
                        Note = request.Reason.Trim()
                    };
                    context.InventoryTransactions.Add(ledger);
                    context.AuditLogs.Add(new AuditLog
                    {
                        UserId = currentUser.UserId,
                        Action = "Inventory.DispositionScrapped",
                        EntityName = "InventoryStock",
                        EntityId = source.Id,
                        WarehouseId = source.WarehouseId,
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {fromStatus}; Quantity: {source.Quantity + request.Quantity}",
                        NewValues = $"ScrappedQuantity: {request.Quantity}; RemainingQuantity: {source.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                        Reason = request.Reason.Trim(),
                        Result = "Success",
                        Severity = "Information"
                    });

                    await context.SaveChangesAsync(cancellationToken);
                }

                var result = new InventoryDispositionResultDto
                {
                    SourceStockId = source.Id,
                    DestinationStockId = destinationStockId,
                    TransactionId = ledger.Id,
                    Action = isRelease ? "ReleaseToAvailable" : "Scrap",
                    FromStatus = ToCode(fromStatus),
                    ToStatus = toStatusCode,
                    Quantity = request.Quantity,
                    RemainingSourceQuantity = source.Quantity,
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = (isRelease ? targetLocationId : source.LocationId) ?? 0,
                    LotId = source.LotId,
                    SerialId = source.SerialId
                };
                if (tx is not null) await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw Conflict("CONCURRENCY_CONFLICT", "Inventory bucket đích đã thay đổi đồng thời.");
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    public async Task<InventoryRecallBlockResultDto> ExecuteRecallBlockAsync(
        CreateInventoryRecallBlockDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.WarehouseId <= 0 || request.ProductId <= 0)
            throw new BusinessRuleException("WarehouseId và ProductId phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.LotNumber))
            throw new BusinessRuleException("LotNumber là bắt buộc khi kích hoạt thu hồi lô.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do thu hồi lô là bắt buộc.");

        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var normalizedLot = request.LotNumber.Trim();
        var cleanReason = request.Reason.Trim();

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var ownTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                await AcquireProductWarehouseLockAndStockLocksAsync(request.ProductId, request.WarehouseId, cancellationToken);

                var lot = await context.InventoryLots
                    .FirstOrDefaultAsync(x => x.ProductId == request.ProductId && x.LotNumber == normalizedLot, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy lô {normalizedLot} của sản phẩm #{request.ProductId}.");

                var sources = await context.InventoryStocks
                    .Where(x => x.WarehouseId == request.WarehouseId &&
                                x.ProductId == request.ProductId &&
                                x.LotId == lot.Id &&
                                x.Status != InventoryStatus.RecallBlocked &&
                                x.Quantity - x.ReservedQuantity > 0)
                    .OrderBy(x => x.Id)
                    .ToListAsync(cancellationToken);

                decimal totalBlocked = 0m;
                var affectedCount = 0;

                foreach (var source in sources)
                {
                    var moveQty = source.Quantity - source.ReservedQuantity;
                    if (moveQty <= 0) continue;

                    var fromStatus = source.Status;
                    var destination = context.InventoryStocks.Local.FirstOrDefault(
                                          x => x.ProductId == source.ProductId &&
                                               x.WarehouseId == source.WarehouseId &&
                                               x.LocationId == source.LocationId &&
                                               x.Status == InventoryStatus.RecallBlocked &&
                                               x.LotId == source.LotId &&
                                               x.SerialId == source.SerialId)
                                      ?? await context.InventoryStocks.SingleOrDefaultAsync(
                                          x => x.ProductId == source.ProductId &&
                                               x.WarehouseId == source.WarehouseId &&
                                               x.LocationId == source.LocationId &&
                                               x.Status == InventoryStatus.RecallBlocked &&
                                               x.LotId == source.LotId &&
                                               x.SerialId == source.SerialId,
                                          cancellationToken);

                    source.Quantity -= moveQty;
                    source.LastUpdated = DateTime.UtcNow;

                    if (destination is null)
                    {
                        destination = new InventoryStock
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = source.LocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            Status = InventoryStatus.RecallBlocked,
                            Quantity = moveQty,
                            ReservedQuantity = 0,
                            LastUpdated = DateTime.UtcNow
                        };
                        context.InventoryStocks.Add(destination);
                    }
                    else
                    {
                        destination.Quantity += moveQty;
                        destination.LastUpdated = DateTime.UtcNow;
                    }

                    context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        InventoryStatus = InventoryStatus.RecallBlocked,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = InventoryStatus.RecallBlocked,
                        TransactionType = TransactionType.StatusChange,
                        Quantity = moveQty,
                        ReferenceId = source.Id,
                        ReferenceType = "RecallCampaignBlock",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = currentUser.UserId,
                        Note = cleanReason
                    });

                    totalBlocked += moveQty;
                    affectedCount++;
                }

                var recallLock = new InventoryLock
                {
                    LockType = InventoryLockType.RecallHold,
                    Status = InventoryLockStatus.Active,
                    WarehouseId = request.WarehouseId,
                    ProductId = request.ProductId,
                    LotId = lot.Id,
                    InventoryStatus = InventoryStatus.RecallBlocked,
                    Reason = cleanReason,
                    CreatedBy = currentUser.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                context.InventoryLocks.Add(recallLock);
                await context.SaveChangesAsync(cancellationToken);

                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Action = "Inventory.RecallCampaignBlocked",
                    EntityName = "InventoryLot",
                    EntityId = lot.Id,
                    WarehouseId = request.WarehouseId,
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"LotNumber: {lot.LotNumber}; AffectedBuckets: {affectedCount}; BlockedQuantity: {totalBlocked}; LockId: {recallLock.Id}",
                    Reason = cleanReason,
                    Result = "Success",
                    Severity = "Warning"
                });
                await context.SaveChangesAsync(cancellationToken);

                if (tx is not null) await tx.CommitAsync(cancellationToken);

                return new InventoryRecallBlockResultDto
                {
                    WarehouseId = request.WarehouseId,
                    ProductId = request.ProductId,
                    LotId = lot.Id,
                    LotNumber = lot.LotNumber.Trim(),
                    AffectedBucketCount = affectedCount,
                    BlockedQuantity = totalBlocked,
                    InventoryLockId = recallLock.Id,
                    Reason = cleanReason
                };
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private Task AcquireProductWarehouseLockAndStockLocksAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken) =>
        context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], cancellationToken);

    private static void ValidateTransition(InventoryStatus fromStatus, InventoryStatus toStatus)
    {
        if (fromStatus is InventoryStatus.Expired or InventoryStatus.RecallBlocked)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Status {fromStatus} chỉ được release qua disposition/recall workflow chuyên biệt.");

        if (toStatus == InventoryStatus.Available &&
            fromStatus is InventoryStatus.Quarantine or InventoryStatus.Damaged or InventoryStatus.Rejected)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Không thể generic-release {fromStatus} về AVAILABLE; cần disposition/release workflow chuyên biệt.");
    }

    private static bool TryParseStatus(string value, out InventoryStatus status)
    {
        var normalized = value?.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<InventoryStatus>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ToCode(candidate).Replace("_", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }
        status = default;
        return false;
    }

    private static string ToCode(InventoryStatus status) => status switch
    {
        InventoryStatus.QcHold => "QC_HOLD",
        InventoryStatus.RecallBlocked => "RECALL_BLOCKED",
        _ => status.ToString().ToUpperInvariant()
    };

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
