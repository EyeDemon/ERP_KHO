using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// GAP-P1-07: Background job that automatically anonymizes expired buyer PII (over 90 days)
/// on completed/cancelled ExportReceipts in compliance with Decree 13/2023/ND-CP while
/// preserving inventory & financial ledger integrity.
/// </summary>
public sealed class BuyerPiiRetentionAnonymizerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<BuyerPiiRetentionAnonymizerJob> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(12);
    private const int DefaultRetentionDays = 90;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay slightly after startup so migrations/seeding complete first
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BuyerPiiRetentionAnonymizerJob sweep encountered a non-fatal error.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> RunSingleSweepAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<ErpKhoDbContext>();
        if (db is null)
            return 0;

        var now = DateTime.UtcNow;
        var cutoffUtc = now.AddDays(-DefaultRetentionDays);

        var expiredReceipts = await db.ExportReceipts
            .IgnoreQueryFilters()
            .Where(r => r.CreatedAt <= cutoffUtc &&
                        (r.Status == ReceiptStatus.Dispatched || r.Status == ReceiptStatus.Cancelled) &&
                        ((r.CustomerNameSnapshot != null && !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_")) ||
                         (r.Note != null && r.Note != "[PII_REDACTED_DECREE_13_2023]")))
            .Take(200)
            .ToListAsync(cancellationToken);

        var expiredSalesOrders = await db.SalesOrders
            .IgnoreQueryFilters()
            .Where(so => so.CreatedAt <= cutoffUtc &&
                         (so.Status == SalesOrderStatus.Fulfilled || so.Status == SalesOrderStatus.Cancelled) &&
                         so.CancellationReason != null &&
                         so.CancellationReason != "[PII_REDACTED_DECREE_13_2023]")
            .Take(200)
            .ToListAsync(cancellationToken);

        var expiredReturns = await db.CustomerReturns
            .IgnoreQueryFilters()
            .Where(cr => cr.CreatedAtUtc <= cutoffUtc &&
                         (cr.Status == "COMPLETED" || cr.Status == "APPROVED" || cr.Status == "CANCELLED" ||
                          cr.Status == "Completed" || cr.Status == "Cancelled") &&
                         cr.ReturnReason != "[PII_REDACTED_DECREE_13_2023]")
            .Take(200)
            .ToListAsync(cancellationToken);

        var totalAnonymized = expiredReceipts.Count + expiredSalesOrders.Count + expiredReturns.Count;
        if (totalAnonymized == 0)
            return 0;

        var touchedTenants = expiredReceipts.Select(x => x.TenantId)
            .Concat(expiredSalesOrders.Select(x => x.TenantId))
            .Concat(expiredReturns.Select(x => x.TenantId))
            .Select(t => t > 0 ? t : 1)
            .Distinct()
            .ToList();

        foreach (var tenantId in touchedTenants)
        {
            db.UseTenant(tenantId);

            foreach (var r in expiredReceipts.Where(x => (x.TenantId > 0 ? x.TenantId : 1) == tenantId))
            {
                r.CustomerNameSnapshot = $"ANONYMIZED_PII_{r.Id}";
                r.Note = "[PII_REDACTED_DECREE_13_2023]";
            }

            foreach (var so in expiredSalesOrders.Where(x => (x.TenantId > 0 ? x.TenantId : 1) == tenantId))
            {
                so.CancellationReason = "[PII_REDACTED_DECREE_13_2023]";
            }

            foreach (var cr in expiredReturns.Where(x => (x.TenantId > 0 ? x.TenantId : 1) == tenantId))
            {
                cr.ReturnReason = "[PII_REDACTED_DECREE_13_2023]";
            }

            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                EntityName = "BuyerPiiRetentionPolicy",
                EntityId = 1,
                Action = "BuyerPiiRetentionAnonymizerJob.Sweep",
                NewValues = $"RetentionDays={DefaultRetentionDays}; AnonymizedReceipts={expiredReceipts.Count}; AnonymizedSalesOrders={expiredSalesOrders.Count}; AnonymizedReturns={expiredReturns.Count}",
                Reason = $"Scheduled Decree 13/2023/ND-CP PII anonymization sweep ({totalAnonymized} records)",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
        }

        return totalAnonymized;
    }
}
