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

        db.UseTenant(1);
        var page = await reconciliationQuery.GetReconciliationsAsync(null, null, null, 1, 100);
        var mismatchedCount = page.Items.Count(x => (x.Difference ?? 0m) != 0m || (x.AllStatusDifference ?? 0m) != 0m);

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = 1,
            Action = "DAILY_INVENTORY_INTEGRITY_CHECK",
            EntityName = "InventoryStock",
            NewValues = $"DriftCount={mismatchedCount};CheckedBuckets={page.TotalRecords}",
            Reason = "Kiểm tra tự động tính toàn vẹn sổ cái 9 chiều hàng ngày",
            Result = mismatchedCount == 0 ? "Success" : "Warning",
            Timestamp = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return mismatchedCount;
    }
}
