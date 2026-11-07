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

public sealed partial class InventoryStatusService(
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
                IsReservable = x.StatusDefinition!.IsReservable &&
                               (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsAllocatable = x.StatusDefinition!.IsAllocatable &&
                                (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsPickable = x.StatusDefinition!.IsPickable &&
                             (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsShippable = x.StatusDefinition!.IsShippable &&
                              (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                LotId = x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber.Trim(),
                ManufactureDate = x.Lot == null ? null : x.Lot.ManufactureDate,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber.Trim(),
                OnHandQuantity = x.Quantity,
                ReservedQuantity = x.ReservedQuantity,
                AvailableQuantity = x.StatusDefinition!.IsAvailable &&
                                    (x.Location == null || (x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable && x.Location.LocationType != WarehouseLocationType.Receiving)) &&
                                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today)
                    ? Math.Max(0m, x.Quantity - (x.ReservedQuantity >= x.AllocatedQuantity ? x.ReservedQuantity : x.AllocatedQuantity) - x.LockedQuantity)
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
                if (source.Quantity - Math.Max(source.ReservedQuantity, source.AllocatedQuantity) - source.LockedQuantity < request.Quantity)
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

                var nowUtc = DateTime.UtcNow;
                var fromStatus = source.Status;
                source.Quantity -= request.Quantity;
                source.LastUpdated = nowUtc;
                if (destination is null)
                {
                    destination = new InventoryStock
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        HandlingUnitId = source.HandlingUnitId,
                        OwnerId = source.OwnerId,
                        Status = toStatus,
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

                var ledgerOut = new InventoryTransaction
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId,
                    FromLocationId = source.LocationId,
                    ToLocationId = source.LocationId,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    HandlingUnitId = source.HandlingUnitId,
                    OwnerId = source.OwnerId,
                    InventoryStatus = fromStatus,
                    FromInventoryStatus = fromStatus,
                    ToInventoryStatus = toStatus,
                    TransactionType = TransactionType.StatusChange,
                    Quantity = -request.Quantity,
                    UnitCost = source.MovingAverageCost,
                    ReferenceId = source.Id,
                    ReferenceType = "InventoryStatusChange",
                    TransactionDate = nowUtc,
                    CreatedBy = currentUser.UserId,
                    Note = request.Reason.Trim()
                };
                var ledgerIn = new InventoryTransaction
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId,
                    FromLocationId = source.LocationId,
                    ToLocationId = source.LocationId,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    HandlingUnitId = source.HandlingUnitId,
                    OwnerId = source.OwnerId,
                    InventoryStatus = toStatus,
                    FromInventoryStatus = fromStatus,
                    ToInventoryStatus = toStatus,
                    TransactionType = TransactionType.StatusChange,
                    Quantity = request.Quantity,
                    UnitCost = source.MovingAverageCost,
                    ReferenceId = source.Id,
                    ReferenceType = "InventoryStatusChange",
                    TransactionDate = nowUtc,
                    CreatedBy = currentUser.UserId,
                    Note = request.Reason.Trim()
                };
                context.InventoryTransactions.AddRange(ledgerOut, ledgerIn);
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Action = "Inventory.StatusChanged",
                    EntityName = "InventoryStock",
                    EntityId = source.Id,
                    WarehouseId = source.WarehouseId,
                    Timestamp = nowUtc,
                    OldValues = $"Status: {fromStatus}; Quantity: {source.Quantity + request.Quantity}",
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
                    TransactionId = ledgerIn.Id,
                    FromStatus = fromStatus.ToString(),
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
}
