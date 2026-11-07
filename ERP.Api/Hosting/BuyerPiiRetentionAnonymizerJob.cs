using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// GAP-P1-07: Background job that automatically anonymizes expired buyer PII (over 90 days)
/// on completed/cancelled documents in compliance with Decree 13/2023/ND-CP inside isolated per-tenant scopes.
/// </summary>
public sealed class BuyerPiiRetentionAnonymizerJob(
    IServiceScopeFactory scopeFactory,
    ILogger<BuyerPiiRetentionAnonymizerJob> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(12);
    private const int DefaultRetentionDays = 90;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        const int StatutoryAccountingRetentionDays = 3650;
        var now = DateTime.UtcNow;
        var cutoffUtc = now.AddDays(-DefaultRetentionDays);
        var accountingArchiveCutoffUtc = now.AddDays(-StatutoryAccountingRetentionDays);

        List<int> touchedTenants;
        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetService<ErpKhoDbContext>();
            if (discoveryDb is null)
            {
                return 0;
            }

            var receiptTenantIds = await discoveryDb.ExportReceipts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.CreatedAt <= cutoffUtc &&
                            (r.Status == ReceiptStatus.Dispatched || r.Status == ReceiptStatus.Cancelled) &&
                            ((r.CreatedAt <= accountingArchiveCutoffUtc && r.CustomerNameSnapshot != null && !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_")) ||
                             (r.Note == null || (!r.Note.Contains("[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]") && r.Note != "[PII_REDACTED_DECREE_13_2023]"))))
                .Select(r => r.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var salesOrderTenantIds = await discoveryDb.SalesOrders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(so => so.CreatedAt <= cutoffUtc &&
                             (so.Status == SalesOrderStatus.Fulfilled || so.Status == SalesOrderStatus.Cancelled) &&
                             so.CancellationReason != null &&
                             so.CancellationReason != "[PII_REDACTED_DECREE_13_2023]" &&
                             so.CancellationReason != "[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]")
                .Select(so => so.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var returnTenantIds = await discoveryDb.CustomerReturns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(cr => cr.CreatedAtUtc <= cutoffUtc &&
                             (cr.Status == "COMPLETED" || cr.Status == "APPROVED" || cr.Status == "CANCELLED" ||
                              cr.Status == "Completed" || cr.Status == "Cancelled") &&
                             cr.ReturnReason != "[PII_REDACTED_DECREE_13_2023]")
                .Select(cr => cr.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            touchedTenants = receiptTenantIds
                .Concat(salesOrderTenantIds)
                .Concat(returnTenantIds)
                .Select(t => t > 0 ? t : 1)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }

        if (touchedTenants.Count == 0)
        {
            return 0;
        }

        var totalAnonymized = 0;

        foreach (var tenantId in touchedTenants)
        {
            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                var tenantDb = tenantScope.ServiceProvider.GetService<ErpKhoDbContext>();
                if (tenantDb is null)
                {
                    continue;
                }

                tenantDb.UseTenant(tenantId);

                var tenantReceipts = await tenantDb.ExportReceipts
                    .Where(r => r.TenantId == tenantId &&
                                r.CreatedAt <= cutoffUtc &&
                                (r.Status == ReceiptStatus.Dispatched || r.Status == ReceiptStatus.Cancelled) &&
                                ((r.CreatedAt <= accountingArchiveCutoffUtc && r.CustomerNameSnapshot != null && !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_")) ||
                                 (r.Note == null || (!r.Note.Contains("[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]") && r.Note != "[PII_REDACTED_DECREE_13_2023]"))))
                    .OrderBy(r => r.Id)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                var tenantSalesOrders = await tenantDb.SalesOrders
                    .Where(so => so.TenantId == tenantId &&
                                 so.CreatedAt <= cutoffUtc &&
                                 (so.Status == SalesOrderStatus.Fulfilled || so.Status == SalesOrderStatus.Cancelled) &&
                                 so.CancellationReason != null &&
                                 so.CancellationReason != "[PII_REDACTED_DECREE_13_2023]" &&
                                 so.CancellationReason != "[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]")
                    .OrderBy(so => so.Id)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                var tenantReturns = await tenantDb.CustomerReturns
                    .Where(cr => cr.TenantId == tenantId &&
                                 cr.CreatedAtUtc <= cutoffUtc &&
                                 (cr.Status == "COMPLETED" || cr.Status == "APPROVED" || cr.Status == "CANCELLED" ||
                                  cr.Status == "Completed" || cr.Status == "Cancelled") &&
                                 cr.ReturnReason != "[PII_REDACTED_DECREE_13_2023]")
                    .OrderBy(cr => cr.Id)
                    .Take(200)
                    .ToListAsync(cancellationToken);

                var tenantCount = tenantReceipts.Count + tenantSalesOrders.Count + tenantReturns.Count;
                if (tenantCount == 0)
                {
                    continue;
                }

                foreach (var r in tenantReceipts)
                {
                    if (r.CreatedAt <= accountingArchiveCutoffUtc)
                    {
                        r.CustomerNameSnapshot = $"ANONYMIZED_PII_{r.Id}";
                        r.Note = "[PII_REDACTED_DECREE_13_2023]";
                    }
                    else
                    {
                        // Điều 41 Luật Kế toán 88/2015/QH13: Preserve CustomerNameSnapshot for 10 years
                        r.Note = "[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]";
                    }
                }

                foreach (var so in tenantSalesOrders)
                {
                    so.CancellationReason = "[PII_REDACTED_DECREE_13_2023]";
                }

                foreach (var cr in tenantReturns)
                {
                    cr.ReturnReason = "[PII_REDACTED_DECREE_13_2023]";
                }

                tenantDb.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenantId,
                    EntityName = "BuyerPiiRetentionPolicy",
                    EntityId = 1,
                    Action = "BuyerPiiRetentionAnonymizerJob.Sweep",
                    NewValues = $"RetentionDays={DefaultRetentionDays}; AnonymizedReceipts={tenantReceipts.Count}; AnonymizedSalesOrders={tenantSalesOrders.Count}; AnonymizedReturns={tenantReturns.Count}",
                    Reason = $"Scheduled Decree 13/2023/ND-CP PII anonymization sweep ({tenantCount} records)",
                    Result = "Success",
                    Timestamp = now
                });

                await tenantDb.SaveChangesAsync(cancellationToken);
                totalAnonymized += tenantCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "BuyerPiiRetentionAnonymizerJob failed for TenantId={TenantId}.", tenantId);
            }
        }

        return totalAnonymized;
    }
}
