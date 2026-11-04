using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that periodically runs 3-way 9D inventory ledger-to-balance reconciliation
/// and records an operational audit heartbeat per isolated tenant scope.
/// </summary>
public sealed class DailyInventoryIntegrityJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyInventoryIntegrityJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyIntegrityOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DailyInventoryIntegrityJob iteration failed; will retry on next tick.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    public async Task<int> VerifyIntegrityOnceAsync(CancellationToken cancellationToken = default)
    {
        List<int> activeTenantIds;
        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var probeRecon = discoveryScope.ServiceProvider.GetService<IInventoryReconciliationQueryService>();
            var discoveryDb = discoveryScope.ServiceProvider.GetService<ErpKhoDbContext>();
            if (probeRecon is null || discoveryDb is null)
            {
                return 0;
            }

            var saasTenantIds = await discoveryDb.SaasTenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TenantId > 0 &&
                            t.LifecycleStatus != "ARCHIVED" &&
                            t.LifecycleStatus != "SUSPENDED")
                .Select(t => t.TenantId)
                .ToListAsync(cancellationToken);

            var warehouseTenantIds = await discoveryDb.Warehouses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(w => w.IsActive && w.TenantId > 0)
                .Select(w => w.TenantId)
                .ToListAsync(cancellationToken);

            activeTenantIds = saasTenantIds
                .Concat(warehouseTenantIds)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }

        if (activeTenantIds.Count == 0)
        {
            activeTenantIds.Add(1);
        }

        const int pageSize = 100;
        var totalMismatchedCount = 0;

        foreach (var tenantId in activeTenantIds)
        {
            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                var tenantDb = tenantScope.ServiceProvider.GetService<ErpKhoDbContext>();
                var reconciliationQuery = tenantScope.ServiceProvider.GetService<IInventoryReconciliationQueryService>();
                if (tenantDb is null || reconciliationQuery is null)
                {
                    continue;
                }

                tenantDb.UseTenant(tenantId);

                if (reconciliationQuery is InventoryReconciliationQueryService)
                {
                    reconciliationQuery = new InventoryReconciliationQueryService(
                        tenantDb,
                        new SystemTenantWarehouseAuthorizationService(tenantDb));
                }

                var tenantMismatchedCount = 0;
                var totalRecords = 0;
                var pageIndex = 1;

                while (true)
                {
                    var page = await reconciliationQuery.GetReconciliationsAsync(null, null, null, pageIndex, pageSize);
                    if (pageIndex == 1)
                    {
                        totalRecords = page.TotalRecords;
                    }

                    tenantMismatchedCount += page.Items.Count(x =>
                        (x.Difference ?? 0m) != 0m || (x.AllStatusDifference ?? 0m) != 0m);

                    var totalPages = totalRecords > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 1;
                    if (pageIndex >= totalPages || page.Items.Count == 0)
                    {
                        break;
                    }

                    pageIndex++;
                }

                tenantDb.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenantId,
                    Action = "DAILY_INVENTORY_INTEGRITY_CHECK",
                    EntityName = "InventoryStock",
                    NewValues = $"DriftCount={tenantMismatchedCount};CheckedBuckets={totalRecords}",
                    Reason = "Kiểm tra tự động tính toàn vẹn sổ cái 9 chiều hàng ngày",
                    Result = tenantMismatchedCount == 0 ? "Success" : "Warning",
                    Timestamp = DateTime.UtcNow
                });

                await tenantDb.SaveChangesAsync(cancellationToken);
                totalMismatchedCount += tenantMismatchedCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "DailyInventoryIntegrityJob failed for TenantId={TenantId}.", tenantId);
            }
        }

        return totalMismatchedCount;
    }

    private sealed class SystemTenantWarehouseAuthorizationService(ErpKhoDbContext db) : IWarehouseAuthorizationService
    {
        public async Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) =>
            await db.Warehouses.AsNoTracking()
                .Where(w => w.IsActive)
                .Select(w => w.Id)
                .ToListAsync(cancellationToken);

        public async Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            await db.Warehouses.AsNoTracking()
                .AnyAsync(w => w.Id == warehouseId && w.IsActive, cancellationToken);

        public async Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
        {
            if (!await CanAccessWarehouseAsync(warehouseId, cancellationToken))
            {
                throw new ForbiddenException("Kho không hoạt động hoặc không thuộc tenant hiện tại.");
            }
        }
    }
}
