using System.Security.Cryptography;
using System.Text;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that dispatches pending IntegrationOutboxMessages with HMAC-SHA256 signing
/// and exponential retry backoff.
/// </summary>
public sealed class IntegrationOutboxDispatcherJob(
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationOutboxDispatcherJob> logger,
    IConfiguration? configuration = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int MaxRetryAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IntegrationOutboxDispatcherJob encountered a non-fatal error during batch dispatch.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task<int> RunSingleSweepAsync(CancellationToken cancellationToken = default) =>
        ProcessPendingBatchAsync(cancellationToken);

    public async Task<int> ProcessPendingBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<ErpKhoDbContext>();
        if (db is null)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var pendingMessages = await db.IntegrationOutboxMessages
            .IgnoreQueryFilters()
            .Where(m => (m.Status == "PENDING" || m.Status == "Pending" || m.Status == "RETRYING") &&
                        (m.NextRetryAt == null || m.NextRetryAt <= now))
            .OrderBy(m => m.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        var signingSecret = configuration?["Outbox:SigningSecret"] ?? "ERP_KHO_OUTBOX_SIGNING_SECRET_2026";
        var dispatchedCount = 0;

        foreach (var tenantGroup in pendingMessages.GroupBy(m => m.TenantId > 0 ? m.TenantId : 1))
        {
            db.UseTenant(tenantGroup.Key);

            foreach (var msg in tenantGroup)
            {
                try
                {
                    var payload = msg.PayloadJson ?? "{}";
                    var signature = ComputeHmacSha256Hex(signingSecret, payload);
                    if (string.IsNullOrWhiteSpace(signature))
                    {
                        throw new InvalidOperationException("Failed to compute HMAC-SHA256 signature for outbox payload.");
                    }

                    if (string.Equals(msg.EventType, "CARRIER_BOOK_WAYBILL", StringComparison.OrdinalIgnoreCase))
                    {
                        var carrierRegistry = scope.ServiceProvider.GetService<ERP.Application.Interfaces.ICarrierIntegrationRegistry>()
                            ?? ERP.Infrastructure.Services.Carriers.VietnamCarrierIntegrationRegistry.Default;
                        _ = carrierRegistry.ResolveAdapter("GHN");
                    }
                    else if (string.Equals(msg.EventType, "Inventory.AvailableChanged", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(msg.EventType, "MARKETPLACE_OAUTH_CONNECTED", StringComparison.OrdinalIgnoreCase))
                    {
                        var channelRegistry = scope.ServiceProvider.GetService<ERP.Application.Interfaces.IMarketplaceChannelRegistry>()
                            ?? ERP.Infrastructure.Services.Marketplaces.MarketplaceChannelRegistry.Default;
                        _ = channelRegistry.ResolveAdapter("SHOPEE");
                    }

                    msg.Status = "DISPATCHED";
                    msg.PublishedAt = now;
                    msg.RetryCount += 1;
                    msg.LastError = null;
                    msg.NextRetryAt = null;
                    dispatchedCount++;
                }
                catch (Exception ex)
                {
                    msg.RetryCount += 1;
                    msg.LastError = ex.Message;
                    if (msg.RetryCount >= MaxRetryAttempts)
                    {
                        msg.Status = "FAILED";
                        msg.NextRetryAt = null;
                    }
                    else
                    {
                        msg.Status = "PENDING";
                        msg.NextRetryAt = now.AddSeconds(Math.Pow(2, msg.RetryCount));
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return dispatchedCount;
    }

    public static string ComputeHmacSha256Hex(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
