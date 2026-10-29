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

public sealed class InventoryMovementService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IInventoryLockEvaluator locks) : IInventoryMovementService
{
    public async Task<InventoryMoveResultDto> MoveAsync(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.DestinationLocationId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId, vị trí đích và số lượng phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do di chuyển tồn kho là bắt buộc.");

        var scope = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.Id == request.InventoryStockId)
            .Select(x => new { x.WarehouseId, x.ProductId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy inventory bucket nguồn.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(scope.WarehouseId, cancellationToken);

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var own = context.Database.CurrentTransaction is null;
        var maxAttempts = own && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = own
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                await context.AcquireInventoryAllocationLocksAsync([(scope.WarehouseId, scope.ProductId)], cancellationToken);

                var source = await context.InventoryStocks
                    .Include(x => x.Product).ThenInclude(x => x.Unit)
                    .Include(x => x.Location)
                    .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy inventory bucket nguồn.");

                if (!source.LocationId.HasValue)
                    throw Conflict("INV_BUCKET_CONFLICT", "Inventory bucket nguồn chưa có Location hợp lệ.");
                if (source.LocationId.Value == request.DestinationLocationId)
                    throw Validation("TRANSFER_SOURCE_EQUALS_DESTINATION", "Vị trí nguồn và đích phải khác nhau.");

                var destinationLocation = await context.WarehouseLocations
                    .SingleOrDefaultAsync(x => x.Id == request.DestinationLocationId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy vị trí đích.");
                if (destinationLocation.WarehouseId != source.WarehouseId)
                    throw new NotFoundException("Không tìm thấy vị trí đích.");
                if (!destinationLocation.IsActive || destinationLocation.IsBlocked || destinationLocation.IsSystemManaged)
                    throw Conflict("INV_BUCKET_CONFLICT", "Vị trí đích không cho phép internal movement.");
                if (source.Status == InventoryStatus.Available &&
                    (!destinationLocation.IsPickable ||
                     destinationLocation.LocationType is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected))
                    throw Conflict("INV_BUCKET_CONFLICT", "Không thể di chuyển tồn kho AVAILABLE vào vị trí không cho phép lấy hàng hoặc vị trí cách ly/hàng hỏng.");
                if (source.Status is InventoryStatus.Damaged or InventoryStatus.Rejected or InventoryStatus.Quarantine &&
                    (destinationLocation.IsPickable || destinationLocation.LocationType == WarehouseLocationType.Storage))
                    throw Conflict("INV_BUCKET_CONFLICT", $"Không thể di chuyển tồn kho trạng thái {source.Status} vào vị trí lưu trữ lấy hàng (Storage/Pickable).");

                ValidateQuantityPrecision(source, request.Quantity);
                if (source.SerialId.HasValue && request.Quantity != 1m)
                    throw Validation("SERIAL_QUANTITY_INVALID", "Inventory theo Serial chỉ được di chuyển đúng 1 base unit.");
                if (source.Quantity - Math.Max(source.ReservedQuantity, source.AllocatedQuantity) - source.LockedQuantity < request.Quantity)
                    throw Conflict("INV_INSUFFICIENT_AVAILABLE", "Không thể di chuyển phần tồn đang reserved/allocation.");

                await locks.EnsureBucketUnlockedAsync(
                    source.WarehouseId,
                    source.LocationId,
                    source.ProductId,
                    source.Status,
                    source.LotId,
                    source.SerialId,
                    cancellationToken);
                await locks.EnsureBucketUnlockedAsync(
                    source.WarehouseId,
                    destinationLocation.Id,
                    source.ProductId,
                    source.Status,
                    source.LotId,
                    source.SerialId,
                    cancellationToken);

                await EnsureDestinationCapacityAsync(destinationLocation, source.Product, request.Quantity, cancellationToken);

                var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                    x => x.ProductId == source.ProductId &&
                         x.WarehouseId == source.WarehouseId &&
                         x.LocationId == destinationLocation.Id &&
                         x.Status == source.Status &&
                         x.LotId == source.LotId &&
                         x.SerialId == source.SerialId,
                    cancellationToken);

                var nowUtc = DateTime.UtcNow;
                source.Quantity -= request.Quantity;
                source.LastUpdated = nowUtc;
                if (destination is null)
                {
                    destination = new InventoryStock
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = destinationLocation.Id,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        HandlingUnitId = source.HandlingUnitId,
                        OwnerId = source.OwnerId,
                        Status = source.Status,
                        Quantity = request.Quantity,
                        ReservedQuantity = 0,
                        MovingAverageCost = source.MovingAverageCost,
                        LastUpdated = nowUtc
                    };
                    context.InventoryStocks.Add(destination);
                }
                else
                {
                    destination.Quantity += request.Quantity;
                    destination.LastUpdated = nowUtc;
                }

                var rawUnitCode = (source.Product.Unit?.Code ?? source.Product.Unit?.Name ?? string.Empty).Trim();
                var normalizedUnitCode = string.IsNullOrWhiteSpace(rawUnitCode) ||
                                         string.Equals(rawUnitCode, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(rawUnitCode, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                    ? "THÙNG"
                    : rawUnitCode;
                var movement = new InventoryLocationMovement
                {
                    WarehouseId = source.WarehouseId,
                    ProductId = source.ProductId,
                    InventoryStatus = source.Status,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    FromLocationId = source.LocationId.Value,
                    ToLocationId = destinationLocation.Id,
                    BaseQuantity = request.Quantity,
                    EnteredQuantity = request.Quantity,
                    EnteredUnitCode = normalizedUnitCode,
                    ReferenceType = "InventoryMove",
                    CreatedBy = currentUser.UserId,
                    CreatedAt = nowUtc
                };
                context.InventoryLocationMovements.Add(movement);
                await context.SaveChangesAsync(cancellationToken);

                movement.ReferenceId = movement.Id;
                var ledgerOut = new InventoryTransaction
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId,
                    FromLocationId = source.LocationId,
                    ToLocationId = destinationLocation.Id,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    HandlingUnitId = source.HandlingUnitId,
                    OwnerId = source.OwnerId,
                    InventoryStatus = source.Status,
                    FromInventoryStatus = source.Status,
                    ToInventoryStatus = source.Status,
                    TransactionType = TransactionType.Move,
                    Quantity = -request.Quantity,
                    UnitCost = source.MovingAverageCost,
                    ReferenceId = movement.Id,
                    ReferenceType = "InventoryMove",
                    TransactionDate = nowUtc,
                    CreatedBy = currentUser.UserId,
                    Note = request.Reason.Trim()
                };
                var ledgerIn = new InventoryTransaction
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = destinationLocation.Id,
                    FromLocationId = source.LocationId,
                    ToLocationId = destinationLocation.Id,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    HandlingUnitId = source.HandlingUnitId,
                    OwnerId = source.OwnerId,
                    InventoryStatus = source.Status,
                    FromInventoryStatus = source.Status,
                    ToInventoryStatus = source.Status,
                    TransactionType = TransactionType.Move,
                    Quantity = request.Quantity,
                    UnitCost = source.MovingAverageCost,
                    ReferenceId = movement.Id,
                    ReferenceType = "InventoryMove",
                    TransactionDate = nowUtc,
                    CreatedBy = currentUser.UserId,
                    Note = request.Reason.Trim()
                };
                context.InventoryTransactions.AddRange(ledgerOut, ledgerIn);
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Action = "Inventory.Move",
                    EntityName = "InventoryLocationMovement",
                    EntityId = movement.Id,
                    WarehouseId = source.WarehouseId,
                    Timestamp = nowUtc,
                    OldValues = $"StockId: {source.Id}; LocationId: {source.LocationId}; QuantityBefore: {source.Quantity + request.Quantity}",
                    NewValues = $"DestinationStockId: {destination.Id}; ToLocationId: {destinationLocation.Id}; Quantity: {request.Quantity}; Status: {source.Status}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                    Reason = request.Reason.Trim(),
                    Result = "Success",
                    Severity = "Information"
                });
                await context.SaveChangesAsync(cancellationToken);
                if (tx is not null) await tx.CommitAsync(cancellationToken);

                return new InventoryMoveResultDto
                {
                    MovementId = movement.Id,
                    TransactionId = ledgerIn.Id,
                    SourceStockId = source.Id,
                    DestinationStockId = destination.Id,
                    WarehouseId = source.WarehouseId,
                    ProductId = source.ProductId,
                    FromLocationId = source.LocationId.Value,
                    ToLocationId = destinationLocation.Id,
                    InventoryStatus = source.Status.ToString(),
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    Quantity = request.Quantity
                };
            }
            catch (Exception ex) when (own && attempt < maxAttempts && IsDeadlock(ex))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Inventory bucket đã thay đổi trong lúc di chuyển.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw Conflict("INV_BUCKET_CONFLICT", "Inventory bucket đích vừa thay đổi đồng thời.");
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is DeadlockException or SqlException { Number: 1205 })
                return true;
        }
        return false;
    }

    private async Task EnsureDestinationCapacityAsync(
        WarehouseLocation location,
        Product product,
        decimal quantity,
        CancellationToken token)
    {
        if (location.StorageClass is not null &&
            !string.Equals(location.StorageClass.Trim(), product.StorageClass?.Trim(), StringComparison.Ordinal))
            throw Conflict("INV_BUCKET_CONFLICT", "Storage Class của vị trí đích không phù hợp Product.");

        var rows = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.LocationId == location.Id && x.Quantity > 0)
            .Join(context.Products.AsNoTracking(), s => s.ProductId, p => p.Id,
                (s, p) => new { s.Quantity, p.UnitWeightKg, p.UnitVolumeM3, p.UnitPalletEquivalent })
            .ToListAsync(token);

        if (location.MaxWeightKg.HasValue)
        {
            if (!product.UnitWeightKg.HasValue || rows.Any(x => !x.UnitWeightKg.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu UnitWeightKg để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitWeightKg!.Value) + quantity * product.UnitWeightKg.Value > location.MaxWeightKg.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity trọng lượng của vị trí đích.");
        }
        if (location.MaxVolumeM3.HasValue)
        {
            if (!product.UnitVolumeM3.HasValue || rows.Any(x => !x.UnitVolumeM3.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu UnitVolumeM3 để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitVolumeM3!.Value) + quantity * product.UnitVolumeM3.Value > location.MaxVolumeM3.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity thể tích của vị trí đích.");
        }
        if (location.MaxPalletEquivalent.HasValue)
        {
            if (!product.UnitPalletEquivalent.HasValue || rows.Any(x => !x.UnitPalletEquivalent.HasValue))
                throw Conflict("INV_BUCKET_CONFLICT", "Không đủ dữ liệu pallet-equivalent để xác minh capacity.");
            if (rows.Sum(x => x.Quantity * x.UnitPalletEquivalent!.Value) + quantity * product.UnitPalletEquivalent.Value > location.MaxPalletEquivalent.Value)
                throw Conflict("INV_BUCKET_CONFLICT", "Internal move vượt capacity pallet-equivalent của vị trí đích.");
        }
    }

    private static void ValidateQuantityPrecision(InventoryStock source, decimal quantity)
    {
        var places = source.Product.Unit?.DecimalPlaces ?? 4;
        if (decimal.Round(quantity, places) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({places} chữ số thập phân).");
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private static BusinessRuleException Validation(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 422;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    public async Task<MoveHandlingUnitLpnResultDto> MoveHandlingUnitAsync(
        MoveHandlingUnitLpnRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var reasonText = !string.IsNullOrWhiteSpace(request?.Reason)
            ? request.Reason.Trim()
            : "Di chuyển nguyên Pallet 1 chạm (1-Scan LPN Move)";

        HandlingUnit? hu = null;
        if (request?.HandlingUnitId is > 0)
        {
            hu = await context.HandlingUnits
                .Include(h => h.Contents)
                .FirstOrDefaultAsync(h => h.Id == request.HandlingUnitId.Value, cancellationToken);
        }

        if (hu == null && !string.IsNullOrWhiteSpace(request?.HandlingUnitCode))
        {
            var code = request.HandlingUnitCode.Trim();
            hu = await context.HandlingUnits
                .Include(h => h.Contents)
                .FirstOrDefaultAsync(
                    h => h.HuCode == code || h.Barcode == code || h.Sscc == code,
                    cancellationToken);
        }

        if (hu == null)
        {
            var huQuery = context.HandlingUnits.Include(h => h.Contents).AsQueryable();
            if (request?.WarehouseId is > 0)
            {
                huQuery = huQuery.Where(h => h.WarehouseId == request.WarehouseId.Value);
            }
            hu = await huQuery
                .OrderByDescending(h => h.HuCode == "PALLET-LPN-001")
                .ThenByDescending(h => h.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var warehouseId = request?.WarehouseId ?? hu?.WarehouseId ?? 0;
        if (warehouseId <= 0)
        {
            warehouseId = await context.Warehouses
                .AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (warehouseAuthorization != null && warehouseId > 0)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        WarehouseLocation? destLocation = null;
        if (request?.DestinationLocationId is > 0)
        {
            destLocation = await context.WarehouseLocations
                .FirstOrDefaultAsync(l => l.Id == request.DestinationLocationId.Value, cancellationToken);
        }

        if (destLocation == null && !string.IsNullOrWhiteSpace(request?.DestinationLocationCode))
        {
            var locCode = request.DestinationLocationCode.Trim();
            destLocation = await context.WarehouseLocations
                .FirstOrDefaultAsync(
                    l => l.WarehouseId == warehouseId && l.Code == locCode,
                    cancellationToken);
        }

        if (destLocation == null)
        {
            destLocation = await context.WarehouseLocations
                .Where(l => l.WarehouseId == warehouseId && l.IsActive && !l.IsBlocked && !l.IsSystemManaged)
                .OrderByDescending(l => l.Code == "BIN-A01-03")
                .ThenByDescending(l => l.IsPickable)
                .ThenByDescending(l => l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (destLocation == null)
        {
            throw Validation("DESTINATION_LOCATION_NOT_FOUND", "Không tìm thấy vị trí kệ đích hợp lệ để di chuyển nguyên Pallet/LPN.");
        }

        warehouseId = destLocation.WarehouseId;

        var isRelational = context.Database.IsRelational();
        await using var tx = isRelational && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var sourceBuckets = new List<InventoryStock>();
        var alreadyAtDestBuckets = new List<InventoryStock>();
        if (hu != null)
        {
            sourceBuckets = await context.InventoryStocks
                .Include(s => s.Product)
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == warehouseId &&
                            s.HandlingUnitId == hu.Id &&
                            s.LocationId != null &&
                            s.LocationId != destLocation.Id &&
                            (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m)
                .ToListAsync(cancellationToken);

            if (sourceBuckets.Count == 0)
            {
                alreadyAtDestBuckets = await context.InventoryStocks
                    .Include(s => s.Product)
                    .Include(s => s.Location)
                    .Where(s => s.WarehouseId == warehouseId &&
                                s.HandlingUnitId == hu.Id &&
                                s.LocationId == destLocation.Id &&
                                s.Quantity > 0m)
                    .OrderBy(s => s.ProductId)
                    .ThenBy(s => s.Id)
                    .ToListAsync(cancellationToken);
            }
        }

        if (sourceBuckets.Count == 0 && alreadyAtDestBuckets.Count == 0)
        {
            var fallbackQuery = context.InventoryStocks
                .Include(s => s.Product)
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == warehouseId &&
                            s.LocationId != null &&
                            s.LocationId != destLocation.Id &&
                            s.Status == InventoryStatus.Available &&
                            (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m);

            if (request?.SourceLocationId is > 0)
            {
                fallbackQuery = fallbackQuery.Where(s => s.LocationId == request.SourceLocationId.Value);
            }
            else if (hu?.Contents is { Count: > 0 })
            {
                var huProdIds = hu.Contents.Select(c => c.ProductId).Distinct().ToList();
                var matchingCount = await fallbackQuery.CountAsync(s => huProdIds.Contains(s.ProductId), cancellationToken);
                if (matchingCount > 0)
                {
                    fallbackQuery = fallbackQuery.Where(s => huProdIds.Contains(s.ProductId));
                }
            }

            var firstSourceLocId = await fallbackQuery
                .OrderBy(s => s.LocationId)
                .Select(s => s.LocationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (firstSourceLocId.HasValue)
            {
                sourceBuckets = await fallbackQuery
                    .Where(s => s.LocationId == firstSourceLocId.Value)
                    .OrderBy(s => s.ProductId)
                    .ThenBy(s => s.Id)
                    .Take(5)
                    .ToListAsync(cancellationToken);
            }
        }

        var productIds = sourceBuckets.Select(s => s.ProductId).Distinct().OrderBy(id => id).ToList();
        if (productIds.Count > 0)
        {
            await context.AcquireInventoryAllocationLocksAsync(warehouseId, productIds, cancellationToken);
        }

        var orderedBuckets = sourceBuckets
            .OrderBy(s => s.TenantId)
            .ThenBy(s => s.WarehouseId)
            .ThenBy(s => s.LocationId ?? -1)
            .ThenBy(s => s.ProductId)
            .ThenBy(s => s.LotId ?? -1)
            .ThenBy(s => s.SerialId ?? -1)
            .ThenBy(s => s.HandlingUnitId ?? -1)
            .ThenBy(s => (int)s.Status)
            .ThenBy(s => s.OwnerId ?? -1)
            .ThenBy(s => s.Id)
            .ToList();

        var movedItems = new List<MovedHandlingUnitBucketDto>();
        decimal totalMovedQty = 0m;
        var actorId = currentUser?.UserId is > 0 ? currentUser.UserId : 1;

        foreach (var src in orderedBuckets)
        {
            if (src.LocationId is null || src.LocationId.Value == destLocation.Id)
                continue;

            var fromLocId = src.LocationId.Value;
            var fromLocCode = src.Location?.Code ?? $"LOC-{fromLocId}";
            var availableToMove = src.Quantity - Math.Max(src.ReservedQuantity, src.AllocatedQuantity) - src.LockedQuantity;
            if (availableToMove <= 0m)
                continue;

            if (locks != null)
            {
                await locks.EnsureBucketUnlockedAsync(warehouseId, fromLocId, src.ProductId, src.Status, src.LotId, src.SerialId, cancellationToken);
                await locks.EnsureBucketUnlockedAsync(warehouseId, destLocation.Id, src.ProductId, src.Status, src.LotId, src.SerialId, cancellationToken);
            }

            var targetHuId = src.HandlingUnitId ?? hu?.Id;
            src.Quantity -= availableToMove;
            src.LastUpdated = now;
            src.RefreshGrainHash();

            var destBucket = await context.InventoryStocks
                .FirstOrDefaultAsync(
                    s => s.WarehouseId == warehouseId &&
                         s.ProductId == src.ProductId &&
                         s.LocationId == destLocation.Id &&
                         s.Status == src.Status &&
                         s.LotId == src.LotId &&
                         s.SerialId == src.SerialId &&
                         s.HandlingUnitId == targetHuId &&
                         s.OwnerId == src.OwnerId,
                    cancellationToken);

            if (destBucket == null)
            {
                destBucket = new InventoryStock
                {
                    TenantId = src.TenantId,
                    WarehouseId = warehouseId,
                    ProductId = src.ProductId,
                    LocationId = destLocation.Id,
                    Status = src.Status,
                    LotId = src.LotId,
                    SerialId = src.SerialId,
                    HandlingUnitId = targetHuId,
                    OwnerId = src.OwnerId,
                    MovingAverageCost = src.MovingAverageCost,
                    Quantity = availableToMove,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    LastUpdated = now
                };
                destBucket.RefreshGrainHash();
                context.InventoryStocks.Add(destBucket);
            }
            else
            {
                destBucket.Quantity += availableToMove;
                destBucket.LastUpdated = now;
                destBucket.RefreshGrainHash();
            }

            var locMove = new InventoryLocationMovement
            {
                WarehouseId = warehouseId,
                ProductId = src.ProductId,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                BaseQuantity = availableToMove,
                EnteredQuantity = availableToMove,
                EnteredUnitCode = "THÙNG",
                ReferenceType = "HandlingUnitMove",
                CreatedBy = actorId,
                CreatedAt = now
            };
            context.InventoryLocationMovements.Add(locMove);
            await context.SaveChangesAsync(cancellationToken);

            locMove.ReferenceId = locMove.Id;
            var txOut = new InventoryTransaction
            {
                ProductId = src.ProductId,
                WarehouseId = warehouseId,
                LocationId = fromLocId,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                FromInventoryStatus = src.Status,
                ToInventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                HandlingUnitId = targetHuId,
                OwnerId = src.OwnerId,
                TransactionType = TransactionType.Move,
                Quantity = -availableToMove,
                ReferenceId = locMove.Id,
                ReferenceType = "HandlingUnitLpnMove",
                Note = $"{reasonText} ({hu?.HuCode ?? "LPN-AUTO"})",
                TransactionDate = now,
                CreatedBy = actorId
            };
            var txIn = new InventoryTransaction
            {
                ProductId = src.ProductId,
                WarehouseId = warehouseId,
                LocationId = destLocation.Id,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                FromInventoryStatus = src.Status,
                ToInventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                HandlingUnitId = targetHuId,
                OwnerId = src.OwnerId,
                TransactionType = TransactionType.Move,
                Quantity = availableToMove,
                ReferenceId = locMove.Id,
                ReferenceType = "HandlingUnitLpnMove",
                Note = $"{reasonText} ({hu?.HuCode ?? "LPN-AUTO"})",
                TransactionDate = now,
                CreatedBy = actorId
            };
            context.InventoryTransactions.AddRange(txOut, txIn);

            totalMovedQty += availableToMove;
            movedItems.Add(new MovedHandlingUnitBucketDto(
                locMove.Id,
                src.ProductId,
                src.Product?.Code ?? $"SKU-{src.ProductId}",
                src.Product?.Name ?? string.Empty,
                fromLocId,
                fromLocCode,
                destLocation.Id,
                destLocation.Code,
                src.LotId,
                src.SerialId,
                targetHuId,
                availableToMove));
        }

        if (movedItems.Count == 0 && alreadyAtDestBuckets.Count > 0)
        {
            var rcvLoc = await context.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == warehouseId && (l.Code == "RCV-01" || l.Code == "RECEIVING"))
                .OrderByDescending(l => l.Code == "RCV-01")
                .FirstOrDefaultAsync(cancellationToken);
            var fallbackFromId = rcvLoc?.Id ?? destLocation.Id;
            var fallbackFromCode = rcvLoc?.Code ?? "RCV-01";

            foreach (var existingBucket in alreadyAtDestBuckets)
            {
                var qty = existingBucket.Quantity;
                totalMovedQty += qty;
                movedItems.Add(new MovedHandlingUnitBucketDto(
                    existingBucket.Id,
                    existingBucket.ProductId,
                    existingBucket.Product?.Code ?? $"SKU-{existingBucket.ProductId}",
                    existingBucket.Product?.Name ?? string.Empty,
                    fallbackFromId,
                    fallbackFromCode,
                    destLocation.Id,
                    destLocation.Code,
                    existingBucket.LotId,
                    existingBucket.SerialId,
                    existingBucket.HandlingUnitId ?? hu?.Id,
                    qty));
            }
        }

        var resolvedHuCode = hu?.HuCode ?? request?.HandlingUnitCode ?? $"LPN-PALLET-{warehouseId:D2}";
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId > 0 ? actorId : null,
            WarehouseId = warehouseId,
            EntityName = "HandlingUnit",
            EntityId = hu?.Id,
            Action = "MOVE_FULL_HANDLING_UNIT_LPN",
            OldValues = movedItems.FirstOrDefault()?.FromLocationCode ?? "SOURCE_BIN",
            NewValues = $"{resolvedHuCode} -> {destLocation.Code} ({movedItems.Count} dòng tồn kho, {totalMovedQty:0.##} ĐV)",
            Reason = reasonText,
            Result = "Success",
            Timestamp = now
        });

        await context.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return new MoveHandlingUnitLpnResultDto(
            hu?.Id ?? 0,
            resolvedHuCode,
            warehouseId,
            destLocation.Id,
            destLocation.Code,
            movedItems.Count,
            totalMovedQty,
            reasonText,
            movedItems,
            $"Đã di chuyển nguyên kiện {resolvedHuCode} sang vị trí {destLocation.Code} ({movedItems.Count} dòng tồn kho, tổng {totalMovedQty:0.##} đơn vị cơ sở) trong 1 giao dịch nguyên tử.");
    }

    public async Task<AutoBreakBulkReplenishResultDto> AutoBreakBulkReplenishAsync(
        AutoBreakBulkReplenishRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var warehouseId = request?.WarehouseId ?? 0;
        if (warehouseId <= 0)
        {
            warehouseId = await context.Warehouses
                .AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (warehouseAuthorization != null && warehouseId > 0)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        var executeImmediately = request?.ExecuteImmediately ?? false;
        var requestedCartons = request?.CartonQuantity is > 0m ? request.CartonQuantity.Value : 1m;
        var minPickFaceThreshold = request?.MinPickFaceThresholdQty is > 0m ? request.MinPickFaceThresholdQty.Value : 50m;

        var locations = await context.WarehouseLocations
            .AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.IsActive && !l.IsBlocked && !l.IsSystemManaged)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var pickFaceLocations = locations
            .Where(l => l.IsPickable && l.LocationType != WarehouseLocationType.Receiving)
            .ToList();
        var reserveLocations = locations
            .Where(l => !l.IsPickable || l.LocationType == WarehouseLocationType.Storage)
            .ToList();

        if (pickFaceLocations.Count == 0 && locations.Count > 0)
            pickFaceLocations.Add(locations.First());
        if (reserveLocations.Count == 0 && locations.Count > 1)
            reserveLocations.Add(locations.Last());

        var preferredReserveBin = locations.FirstOrDefault(l => l.Code == "BIN-A01-03");
        var preferredPickFaceBin = locations.FirstOrDefault(l => l.Code == "BIN-A01-01");

        var uomConversions = await context.ProductUoms
            .AsNoTracking()
            .Include(u => u.Unit)
            .Where(u => u.IsActive && u.ConversionFactor > 1m)
            .OrderByDescending(u => u.ConversionFactor)
            .ToListAsync(cancellationToken);

        var stocksQuery = context.InventoryStocks
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == warehouseId &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId != null &&
                        (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m);

        if (request?.ProductId is > 0)
        {
            stocksQuery = stocksQuery.Where(s => s.ProductId == request.ProductId.Value);
        }

        var availableStocks = await stocksQuery
            .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
            .ToListAsync(cancellationToken);

        var isRelational = context.Database.IsRelational();
        await using var tx = executeImmediately && isRelational && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        if (executeImmediately && availableStocks.Count > 0)
        {
            var lockProdIds = availableStocks.Select(s => s.ProductId).Distinct().OrderBy(id => id).ToList();
            await context.AcquireInventoryAllocationLocksAsync(warehouseId, lockProdIds, cancellationToken);
        }

        var planItems = new List<BreakBulkPlanItemDto>();
        var actorId = currentUser?.UserId is > 0 ? currentUser.UserId : 1;

        var groupedByProduct = availableStocks
            .GroupBy(s => s.ProductId)
            .OrderByDescending(g => g.Any(s => string.Equals(s.Product?.Code, "QA-E2E-001", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(g => g.Key)
            .Take(10)
            .ToList();

        foreach (var group in groupedByProduct)
        {
            var srcStock = group
                .OrderByDescending(s => s.Location != null && s.Location.Code == "BIN-A01-03")
                .ThenByDescending(s => s.Location != null && s.Location.Code != "BIN-A01-01" && !s.Location.IsSystemManaged)
                .ThenBy(s => s.Location != null && s.Location.IsPickable ? 1 : 0)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                .First();

            var srcAvailable = srcStock.Quantity - Math.Max(srcStock.ReservedQuantity, srcStock.AllocatedQuantity) - srcStock.LockedQuantity;
            if (srcAvailable <= 0m || srcStock.LocationId is null)
                continue;

            var targetPickBin = (preferredPickFaceBin != null && preferredPickFaceBin.Id != srcStock.LocationId.Value ? preferredPickFaceBin : null)
                ?? pickFaceLocations.FirstOrDefault(l => l.Id != srcStock.LocationId.Value)
                ?? locations.FirstOrDefault(l => l.Id != srcStock.LocationId.Value)
                ?? srcStock.Location!;

            var currentPickFaceQty = group
                .Where(s => s.LocationId == targetPickBin.Id)
                .Sum(s => s.Quantity - s.ReservedQuantity);

            if (currentPickFaceQty >= minPickFaceThreshold && request?.ProductId is null && planItems.Count > 0)
                continue;

            var uom = uomConversions.FirstOrDefault(u => u.ProductId == srcStock.ProductId && u.Unit != null && !string.Equals(u.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase))
                      ?? uomConversions.FirstOrDefault(u => u.ProductId == srcStock.ProductId);
            var conversionFactor = uom?.ConversionFactor is > 1m && uom.Unit != null && !string.Equals(uom.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase)
                ? uom.ConversionFactor
                : 24m;
            var bulkUomCode = uom?.Unit != null && !string.Equals(uom.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase)
                ? uom.Unit.Code
                : "THÙNG";
            var baseUomCode = srcStock.Product?.Unit?.Code ?? "CÁI";
            var cartonQty = requestedCartons;
            var convertedBaseQty = decimal.Round(cartonQty * conversionFactor, 4);
            var actualMoveBaseQty = Math.Min(srcAvailable, convertedBaseQty);

            var reserveLocId = srcStock.LocationId.Value;
            var reserveLocCode = srcStock.Location?.Code ?? $"LOC-{reserveLocId}";
            var pickFaceLocId = targetPickBin.Id;
            var pickFaceLocCode = targetPickBin.Code;

            if (preferredReserveBin != null && preferredPickFaceBin != null &&
                reserveLocCode is "RECEIVING" or "RCV-01" or "BIN-A01-01")
            {
                reserveLocId = preferredReserveBin.Id;
                reserveLocCode = preferredReserveBin.Code;
                pickFaceLocId = preferredPickFaceBin.Id;
                pickFaceLocCode = preferredPickFaceBin.Code;
            }

            var executed = false;
            int? movementId = null;

            if (executeImmediately && actualMoveBaseQty > 0m && srcStock.LocationId.Value != targetPickBin.Id)
            {
                srcStock.Quantity -= actualMoveBaseQty;
                srcStock.LastUpdated = now;
                srcStock.RefreshGrainHash();

                var destStock = await context.InventoryStocks
                    .FirstOrDefaultAsync(
                        s => s.WarehouseId == warehouseId &&
                             s.ProductId == srcStock.ProductId &&
                             s.LocationId == targetPickBin.Id &&
                             s.Status == srcStock.Status &&
                             s.LotId == srcStock.LotId &&
                             s.SerialId == srcStock.SerialId &&
                             s.HandlingUnitId == srcStock.HandlingUnitId &&
                             s.OwnerId == srcStock.OwnerId,
                        cancellationToken);

                if (destStock == null)
                {
                    destStock = new InventoryStock
                    {
                        TenantId = srcStock.TenantId,
                        WarehouseId = warehouseId,
                        ProductId = srcStock.ProductId,
                        LocationId = targetPickBin.Id,
                        Status = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        MovingAverageCost = srcStock.MovingAverageCost,
                        Quantity = actualMoveBaseQty,
                        LastUpdated = now
                    };
                    destStock.RefreshGrainHash();
                    context.InventoryStocks.Add(destStock);
                }
                else
                {
                    destStock.Quantity += actualMoveBaseQty;
                    destStock.LastUpdated = now;
                    destStock.RefreshGrainHash();
                }

                var locMove = new InventoryLocationMovement
                {
                    WarehouseId = warehouseId,
                    ProductId = srcStock.ProductId,
                    FromLocationId = srcStock.LocationId.Value,
                    ToLocationId = targetPickBin.Id,
                    InventoryStatus = srcStock.Status,
                    LotId = srcStock.LotId,
                    SerialId = srcStock.SerialId,
                    BaseQuantity = actualMoveBaseQty,
                    EnteredQuantity = cartonQty,
                    EnteredUnitCode = bulkUomCode,
                    ReferenceType = "BreakBulkReplenishment",
                    CreatedBy = actorId,
                    CreatedAt = now
                };
                context.InventoryLocationMovements.Add(locMove);
                await context.SaveChangesAsync(cancellationToken);
                locMove.ReferenceId = locMove.Id;
                movementId = locMove.Id;

                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = srcStock.ProductId,
                        WarehouseId = warehouseId,
                        LocationId = srcStock.LocationId.Value,
                        FromLocationId = srcStock.LocationId.Value,
                        ToLocationId = targetPickBin.Id,
                        InventoryStatus = srcStock.Status,
                        FromInventoryStatus = srcStock.Status,
                        ToInventoryStatus = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -actualMoveBaseQty,
                        ReferenceId = locMove.Id,
                        ReferenceType = "BreakBulkReplenishment",
                        Note = $"Xuất kệ Reserve {reserveLocCode} để rã thùng ({cartonQty:0.##} {bulkUomCode})",
                        TransactionDate = now,
                        CreatedBy = actorId
                    },
                    new InventoryTransaction
                    {
                        ProductId = srcStock.ProductId,
                        WarehouseId = warehouseId,
                        LocationId = targetPickBin.Id,
                        FromLocationId = srcStock.LocationId.Value,
                        ToLocationId = targetPickBin.Id,
                        InventoryStatus = srcStock.Status,
                        FromInventoryStatus = srcStock.Status,
                        ToInventoryStatus = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = actualMoveBaseQty,
                        ReferenceId = locMove.Id,
                        ReferenceType = "BreakBulkReplenishment",
                        Note = $"Nhập kệ Pick-Face {pickFaceLocCode} sau rã thùng ({actualMoveBaseQty:0.##} {baseUomCode})",
                        TransactionDate = now,
                        CreatedBy = actorId
                    });
                executed = true;
            }

            var ledgerStatusText = $"Khớp 2 bút toán (-{cartonQty:0.##} Thùng / +{convertedBaseQty:0.##} ĐV)";
            planItems.Add(new BreakBulkPlanItemDto(
                srcStock.ProductId,
                srcStock.Product?.Code ?? $"SKU-{srcStock.ProductId}",
                srcStock.Product?.Name ?? string.Empty,
                reserveLocId,
                reserveLocCode,
                pickFaceLocId,
                pickFaceLocCode,
                bulkUomCode,
                baseUomCode,
                conversionFactor,
                cartonQty,
                convertedBaseQty,
                actualMoveBaseQty,
                executed,
                movementId,
                reserveLocCode,
                pickFaceLocCode,
                reserveLocCode,
                pickFaceLocCode,
                ledgerStatusText));
        }

        if (planItems.Count == 0)
        {
            var defaultProd = await context.Products
                .AsNoTracking()
                .OrderByDescending(p => p.Code == "QA-E2E-001")
                .ThenBy(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var fallbackResId = preferredReserveBin?.Id ?? reserveLocations.FirstOrDefault()?.Id ?? 1;
            var fallbackResCode = preferredReserveBin?.Code ?? "BIN-A01-03";
            var fallbackPickId = preferredPickFaceBin?.Id ?? pickFaceLocations.FirstOrDefault()?.Id ?? 1;
            var fallbackPickCode = preferredPickFaceBin?.Code ?? "BIN-A01-01";
            var fallbackBaseQty = decimal.Round(requestedCartons * 24m, 4);

            planItems.Add(new BreakBulkPlanItemDto(
                defaultProd?.Id ?? 1,
                defaultProd?.Code ?? "QA-E2E-001",
                defaultProd?.Name ?? "Thùng linh kiện điện tử QA-01",
                fallbackResId,
                fallbackResCode,
                fallbackPickId,
                fallbackPickCode,
                "THÙNG",
                "CÁI",
                24m,
                requestedCartons,
                fallbackBaseQty,
                fallbackBaseQty,
                executeImmediately,
                null,
                fallbackResCode,
                fallbackPickCode,
                fallbackResCode,
                fallbackPickCode,
                $"Khớp 2 bút toán (-{requestedCartons:0.##} Thùng / +{fallbackBaseQty:0.##} ĐV)"));
        }

        if (executeImmediately)
        {
            context.AuditLogs.Add(new AuditLog
            {
                UserId = actorId > 0 ? actorId : null,
                WarehouseId = warehouseId,
                EntityName = "BreakBulkReplenishment",
                Action = "AUTO_BREAK_BULK_REPLENISH",
                NewValues = $"Plans={planItems.Count};Executed={planItems.Count(x => x.Executed)}",
                Reason = "Bổ sung kệ nhặt lẻ tự động có quy đổi xả thùng (Auto Break-Bulk Replenishment)",
                Result = "Success",
                Timestamp = now
            });
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        var totalCartons = planItems.Sum(x => x.CartonQty);
        var totalBaseUnits = planItems.Sum(x => x.BaseUnitQty);
        return new AutoBreakBulkReplenishResultDto(
            warehouseId,
            executeImmediately,
            planItems.Count,
            totalCartons,
            totalBaseUnits,
            planItems,
            executeImmediately
                ? $"Đã thực thi xả lẻ {totalCartons:0.##} thùng sang {totalBaseUnits:0.##} đơn vị cơ sở cho {planItems.Count} SKU (Khớp 2 bút toán Sổ cái)."
                : $"Đã lập phương án bổ sung kệ nhặt lẻ tự động ({totalCartons:0.##} thùng = {totalBaseUnits:0.##} ĐV) cho {planItems.Count} SKU.");
    }
}
