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
                        r.CustomerNameSnapshot != null &&
                        !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_"))
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expiredReceipts.Count == 0)
            return 0;

        foreach (var r in expiredReceipts)
        {
            r.CustomerNameSnapshot = $"ANONYMIZED_PII_{r.Id}";
            r.Note = "[PII_REDACTED_DECREE_13_2023]";
        }

        db.AuditLogs.Add(new AuditLog
        {
            EntityName = "BuyerPiiRetentionPolicy",
            EntityId = 1,
            Action = "BuyerPiiRetentionAnonymizerJob.Sweep",
            NewValues = $"RetentionDays={DefaultRetentionDays}; AnonymizedReceipts={expiredReceipts.Count}",
            Reason = $"Scheduled Decree 13/2023/ND-CP PII anonymization sweep ({expiredReceipts.Count} receipts)",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return expiredReceipts.Count;
    }
}
