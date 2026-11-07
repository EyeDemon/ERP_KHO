using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services.Carriers;
using ERP.Infrastructure.Services.Marketplaces;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that dispatches pending IntegrationOutboxMessages with optional HMAC-SHA256 signing
/// and exponential retry backoff inside isolated per-tenant scopes.
/// </summary>
public sealed class IntegrationOutboxDispatcherJob(
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationOutboxDispatcherJob> logger,
    IConfiguration? configuration = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int MaxRetryAttempts = 5;
    private bool _missingSecretWarningLogged;

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
        var now = DateTime.UtcNow;
        List<(int Id, int TenantId)> pendingCandidates;
        string? signingSecret;

        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetService<ErpKhoDbContext>();
            if (discoveryDb is null)
            {
                return 0;
            }

            var rawPending = await discoveryDb.IntegrationOutboxMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => (m.Status == "PENDING" || m.Status == "Pending" || m.Status == "RETRYING") &&
                            (m.NextRetryAt == null || m.NextRetryAt <= now))
                .OrderBy(m => m.Id)
                .Take(100)
                .Select(m => new { m.Id, m.TenantId })
                .ToListAsync(cancellationToken);

            pendingCandidates = rawPending
                .Select(m => (m.Id, m.TenantId > 0 ? m.TenantId : 1))
                .ToList();

            signingSecret = configuration?["Outbox:SigningSecret"]
                ?? discoveryScope.ServiceProvider.GetService<IConfiguration>()?["Outbox:SigningSecret"]
                ?? Environment.GetEnvironmentVariable("ERP_OUTBOX_SIGNING_SECRET");
        }

        if (pendingCandidates.Count == 0)
        {
            return 0;
        }

        var hasSigningSecret = !string.IsNullOrWhiteSpace(signingSecret);
        if (!hasSigningSecret && !_missingSecretWarningLogged)
        {
            _missingSecretWarningLogged = true;
            logger.LogWarning(
                "Outbox:SigningSecret (or ERP_OUTBOX_SIGNING_SECRET) is not configured; omitting HMAC header during outbox dispatch.");
        }

        var dispatchedCount = 0;

        foreach (var tenantGroup in pendingCandidates.GroupBy(m => m.TenantId))
        {
            var tenantId = tenantGroup.Key;
            var candidateIds = tenantGroup.Select(m => m.Id).ToList();

            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                var tenantDb = tenantScope.ServiceProvider.GetService<ErpKhoDbContext>();
                if (tenantDb is null)
                {
                    continue;
                }

                tenantDb.UseTenant(tenantId);

                var tenantMessages = await tenantDb.IntegrationOutboxMessages
                    .Where(m => m.TenantId == tenantId &&
                                candidateIds.Contains(m.Id) &&
                                (m.Status == "PENDING" || m.Status == "Pending" || m.Status == "RETRYING") &&
                                (m.NextRetryAt == null || m.NextRetryAt <= now))
                    .OrderBy(m => m.Id)
                    .ToListAsync(cancellationToken);

                if (tenantMessages.Count == 0)
                {
                    continue;
                }

                foreach (var msg in tenantMessages)
                {
                    try
                    {
                        var payload = msg.PayloadJson ?? "{}";
                        string? signature = null;

                        if (hasSigningSecret)
                        {
                            signature = ComputeHmacSha256Hex(signingSecret!.Trim(), payload);
                            if (string.IsNullOrWhiteSpace(signature))
                            {
                                throw new InvalidOperationException("Failed to compute HMAC-SHA256 signature for outbox payload.");
                            }
                        }

                        if (string.Equals(msg.EventType, "CARRIER_BOOK_WAYBILL", StringComparison.OrdinalIgnoreCase))
                        {
                            var carrierRegistry = tenantScope.ServiceProvider.GetService<ICarrierIntegrationRegistry>()
                                ?? VietnamCarrierIntegrationRegistry.Default;
                            var parsedCarrierCode = ParseCarrierCode(payload);
                            var adapter = carrierRegistry.ResolveAdapter(parsedCarrierCode);

                            if (hasSigningSecret &&
                                !adapter.VerifyWebhookHmacSignature(payload, $"sha256={signature}", signingSecret!.Trim()))
                            {
                                throw new InvalidOperationException(
                                    $"Outbox HMAC-SHA256 verification failed for carrier adapter {adapter.CarrierCode}.");
                            }
                        }
                        else if (string.Equals(msg.EventType, "Inventory.AvailableChanged", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(msg.EventType, "MARKETPLACE_OAUTH_CONNECTED", StringComparison.OrdinalIgnoreCase))
                        {
                            var channelRegistry = tenantScope.ServiceProvider.GetService<IMarketplaceChannelRegistry>()
                                ?? MarketplaceChannelRegistry.Default;
                            var channelAdapter = channelRegistry.ResolveAdapter("SHOPEE");
                            if (string.IsNullOrWhiteSpace(channelAdapter.ChannelCode))
                            {
                                throw new InvalidOperationException("Resolved marketplace channel adapter has an empty ChannelCode.");
                            }
                        }

                        msg.Status = "DISPATCHED";
                        msg.PublishedAt = now;
                        msg.RetryCount += 1;
                        msg.LastError = null;
                        msg.NextRetryAt = null;
                        dispatchedCount++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
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

                await tenantDb.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "IntegrationOutboxDispatcherJob failed for TenantId={TenantId}.", tenantId);
            }
        }

        return dispatchedCount;
    }

    public static string ComputeHmacSha256Hex(string secret, string payload)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ParseCarrierCode(string payload)
    {
        var parsedCarrierCode = "GHN";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return parsedCarrierCode;
            }

            if (doc.RootElement.TryGetProperty("carrierCode", out var ccEl) &&
                ccEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(ccEl.GetString()))
            {
                return ccEl.GetString()!;
            }

            if (doc.RootElement.TryGetProperty("provider", out var provEl) &&
                provEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(provEl.GetString()))
            {
                var prov = provEl.GetString()!.ToUpperInvariant();
                if (prov.Contains("GHTK", StringComparison.Ordinal)) return "GHTK";
                if (prov.Contains("VIETTEL", StringComparison.Ordinal)) return "VIETTELPOST";
                if (prov.Contains("JT", StringComparison.Ordinal)) return "JT_EXPRESS";
            }
        }
        catch (JsonException)
        {
            return parsedCarrierCode;
        }

        return parsedCarrierCode;
    }
}
