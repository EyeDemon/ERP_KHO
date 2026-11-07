using System.Data;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that periodically expires soft stock reservations whose ExpiresAt has passed,
/// releasing ReservedQuantity on the corresponding 9D InventoryStocks buckets inside per-tenant transactions.
/// </summary>
public sealed class SoftReservationExpiryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SoftReservationExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireStaleSoftReservationsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SoftReservationExpiryJob iteration failed; will retry on next tick.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    public async Task<int> ExpireStaleSoftReservationsOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        List<(int Id, int TenantId, int WarehouseId, int ProductId)> expiredCandidates;

        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetService<ErpKhoDbContext>();
            if (discoveryDb is null)
            {
                return 0;
            }

            var rawCandidates = await discoveryDb.StockReservations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => (r.Status == StockReservationStatus.Active ||
                             r.Status == StockReservationStatus.PartiallyConsumed ||
                             r.Status == StockReservationStatus.PartiallyAllocated) &&
                            r.ExpiresAt <= now)
                .OrderBy(r => r.TenantId)
                .ThenBy(r => r.ExpiresAt)
                .ThenBy(r => r.Id)
                .Take(100)
                .Select(r => new { r.Id, r.TenantId, r.WarehouseId, r.ProductId })
                .ToListAsync(cancellationToken);

            expiredCandidates = rawCandidates
                .Select(r => (r.Id, r.TenantId > 0 ? r.TenantId : 1, r.WarehouseId, r.ProductId))
                .ToList();
        }

        if (expiredCandidates.Count == 0)
        {
            return 0;
        }

        var expiredCount = 0;

        foreach (var tenantGroup in expiredCandidates.GroupBy(r => r.TenantId))
        {
            var tenantId = tenantGroup.Key;
            var candidateIds = tenantGroup.Select(r => r.Id).ToList();

            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                var tenantDb = tenantScope.ServiceProvider.GetService<ErpKhoDbContext>();
                if (tenantDb is null)
                {
                    continue;
                }

                tenantDb.UseTenant(tenantId);

                await using var tx = tenantDb.Database.IsRelational() && tenantDb.Database.CurrentTransaction is null
                    ? await tenantDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    : null;

                var lockKeys = tenantGroup
                    .Select(r => (r.WarehouseId, r.ProductId))
                    .Distinct()
                    .OrderBy(k => k.WarehouseId)
                    .ThenBy(k => k.ProductId)
                    .ToList();

                await tenantDb.AcquireInventoryAllocationLocksAsync(lockKeys, cancellationToken);

                var tenantReservations = await tenantDb.StockReservations
                    .Include(r => r.Allocations)
                    .Where(r => r.TenantId == tenantId &&
                                candidateIds.Contains(r.Id) &&
                                (r.Status == StockReservationStatus.Active ||
                                 r.Status == StockReservationStatus.PartiallyConsumed ||
                                 r.Status == StockReservationStatus.PartiallyAllocated) &&
                                r.ExpiresAt <= now &&
                                !tenantDb.PickingTaskLines.Any(line =>
                                    line.Allocation.ReservationId == r.Id &&
                                    line.PickingTask.Status != PickingTaskStatus.Cancelled))
                    .OrderBy(r => r.WarehouseId)
                    .ThenBy(r => r.ProductId)
                    .ThenBy(r => r.Id)
                    .ToListAsync(cancellationToken);

                if (tenantReservations.Count == 0)
                {
                    if (tx is not null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }
                    continue;
                }

                foreach (var reservation in tenantReservations)
                {
                    var remainingReserved = Math.Max(
                        0m,
                        reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity);
                    var toRelease = remainingReserved;

                    var activeAllocations = reservation.Allocations
                        .Where(a => a.Status == StockAllocationStatus.Active ||
                                    a.Status == StockAllocationStatus.Picking ||
                                    a.Status == StockAllocationStatus.Picked)
                        .OrderBy(a => a.LocationId)
                        .ThenBy(a => a.LotId)
                        .ThenBy(a => a.Id)
                        .ToList();

                    foreach (var allocation in activeAllocations)
                    {
                        var exactBucket = await tenantDb.InventoryStocks.FirstOrDefaultAsync(
                            s => s.TenantId == tenantId &&
                                 s.WarehouseId == reservation.WarehouseId &&
                                 s.ProductId == reservation.ProductId &&
                                 s.Status == allocation.InventoryStatus &&
                                 s.LocationId == allocation.LocationId &&
                                 s.LotId == allocation.LotId &&
                                 s.SerialId == allocation.SerialId &&
                                 s.HandlingUnitId == allocation.HandlingUnitId &&
                                 s.OwnerId == allocation.OwnerId,
                            cancellationToken);

                        if (exactBucket is not null)
                        {
                            exactBucket.AllocatedQuantity = Math.Max(0m, exactBucket.AllocatedQuantity - allocation.Quantity);
                            var allocDeduct = Math.Min(Math.Min(exactBucket.ReservedQuantity, allocation.Quantity), toRelease);
                            exactBucket.ReservedQuantity = Math.Max(0m, exactBucket.ReservedQuantity - allocDeduct);
                            exactBucket.LastUpdated = now;
                            toRelease = Math.Max(0m, toRelease - allocDeduct);
                        }

                        allocation.Status = StockAllocationStatus.Released;
                        allocation.ReleasedAt = now;
                        allocation.ReleaseReason = "TTL_EXPIRED_BY_WORKER";
                        allocation.Version++;
                    }

                    if (activeAllocations.Count > 0 || reservation.AllocatedQuantity > 0m)
                    {
                        reservation.AllocatedQuantity = 0m;
                        reservation.AllocationVersion++;
                    }

                    if (toRelease > 0m)
                    {
                        var stockBuckets = await tenantDb.InventoryStocks
                            .Where(s => s.TenantId == tenantId &&
                                        s.WarehouseId == reservation.WarehouseId &&
                                        s.ProductId == reservation.ProductId &&
                                        s.Status == InventoryStatus.Available &&
                                        s.ReservedQuantity > 0m)
                            .OrderByDescending(s => s.ReservedQuantity > s.AllocatedQuantity ? 1 : 0)
                            .ThenBy(s => s.LocationId)
                            .ThenBy(s => s.LotId)
                            .ThenBy(s => s.SerialId)
                            .ThenBy(s => s.HandlingUnitId)
                            .ThenBy(s => s.OwnerId)
                            .ThenBy(s => s.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var bucket in stockBuckets)
                        {
                            if (toRelease <= 0m)
                            {
                                break;
                            }

                            var unallocatedReserved = Math.Max(0m, bucket.ReservedQuantity - bucket.AllocatedQuantity);
                            var releasableInBucket = unallocatedReserved > 0m ? unallocatedReserved : bucket.ReservedQuantity;
                            var deduct = Math.Min(releasableInBucket, toRelease);
                            if (deduct <= 0m)
                            {
                                continue;
                            }

                            bucket.ReservedQuantity = Math.Max(0m, bucket.ReservedQuantity - deduct);
                            bucket.LastUpdated = now;
                            toRelease = Math.Max(0m, toRelease - deduct);
                        }
                    }

                    reservation.ReleasedQuantity += remainingReserved;
                    reservation.ReleasedAt = now;
                    reservation.ReleaseReason ??= "TTL_EXPIRED_BY_WORKER";
                    reservation.Status = StockReservationStatus.Expired;

                    tenantDb.AuditLogs.Add(new AuditLog
                    {
                        TenantId = tenantId,
                        WarehouseId = reservation.WarehouseId,
                        Action = "StockReservation.ExpiredSweep",
                        EntityName = "StockReservation",
                        EntityId = reservation.Id,
                        NewValues = $"Code={reservation.ReservationCode}; Released={remainingReserved}; Status=Expired",
                        Reason = reservation.ReleaseReason,
                        Result = "Success",
                        Timestamp = now
                    });

                    expiredCount++;
                }

                await tenantDb.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                {
                    await tx.CommitAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SoftReservationExpiryJob failed for TenantId={TenantId}.", tenantId);
            }
        }

        return expiredCount;
    }
}
