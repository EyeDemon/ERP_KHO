using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that periodically runs 3-way 9D inventory ledger-to-balance reconciliation
/// and records an operational audit heartbeat.
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
        using var scope = scopeFactory.CreateScope();
        var reconciliationQuery = scope.ServiceProvider.GetService<IInventoryReconciliationQueryService>();
        var db = scope.ServiceProvider.GetService<ErpKhoDbContext>();
        if (reconciliationQuery is null || db is null)
        {
            return 0;
        }

        var activeTenantIds = await db.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.IsActive && w.TenantId > 0)
            .Select(w => w.TenantId)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);

        if (activeTenantIds.Count == 0)
        {
            activeTenantIds.Add(1);
        }

        var originalTenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var totalMismatchedCount = 0;

        try
        {
            const int pageSize = 100;
            foreach (var tenantId in activeTenantIds)
            {
                db.UseTenant(tenantId);
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

                    tenantMismatchedCount += page.Items.Count(x => (x.Difference ?? 0m) != 0m || (x.AllStatusDifference ?? 0m) != 0m);

                    var totalPages = totalRecords > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 1;
                    if (pageIndex >= totalPages || page.Items.Count == 0)
                    {
                        break;
                    }

                    pageIndex++;
                }

                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenantId,
                    Action = "DAILY_INVENTORY_INTEGRITY_CHECK",
                    EntityName = "InventoryStock",
                    NewValues = $"DriftCount={tenantMismatchedCount};CheckedBuckets={totalRecords}",
                    Reason = "Kiểm tra tự động tính toàn vẹn sổ cái 9 chiều hàng ngày",
                    Result = tenantMismatchedCount == 0 ? "Success" : "Warning",
                    Timestamp = DateTime.UtcNow
                });

                totalMismatchedCount += tenantMismatchedCount;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.UseTenant(originalTenantId);
        }

        return totalMismatchedCount;
    }
}
