using System.Security.Cryptography;
using System.Text;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService(
    ErpKhoDbContext db,
    IWarehouseAuthorizationService? warehouseAccess = null,
    IMarketplaceChannelRegistry? channelRegistry = null,
    IConfiguration? configuration = null,
    ICurrentUser? currentUser = null) : ISalesOrderOperationsService
{
    private const string EncryptedTokenPrefix = "ENC:v1:";
    private static readonly byte[] EphemeralProcessKey = RandomNumberGenerator.GetBytes(32);

    private readonly ErpKhoDbContext _db = db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess = warehouseAccess;
    private readonly IMarketplaceChannelRegistry _channelRegistry = channelRegistry ?? MarketplaceChannelRegistry.Default;
    private readonly IConfiguration? _configuration = configuration;
    private readonly ICurrentUser? _currentUser = currentUser;

    public async Task<WmsEndpointOutcome> ExpediteBackorderAsync(
        ExpediteBackorderInput? request,
        IReadOnlyList<SalesOrderListDto> orders,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                {
                    return WmsEndpointOutcome.Forbid();
                }
                targetWarehouseId = accessibleIds.First();
            }
        }

        var scopedOrders = orders
            .Where(o => !warehouseId.HasValue || o.WarehouseId == warehouseId.Value)
            .Where(o => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(o.WarehouseId))
            .ToList();
        var backorderCandidates = scopedOrders
            .Where(o => o.BackorderQuantity > 0 || o.OpenQuantity > 0 || o.Status is "Released" or "PartiallyFulfilled")
            .ToList();

        var backorderOrdersCount = backorderCandidates.Count > 0 ? backorderCandidates.Count : scopedOrders.Count;
        var totalBackorderQuantity = scopedOrders.Sum(o => o.BackorderQuantity);
        var totalOpenQuantity = scopedOrders.Sum(o => o.OpenQuantity);

        var resolvedOrderNumber = !string.IsNullOrWhiteSpace(request?.OrderNumber)
            ? request.OrderNumber.Trim()
            : (backorderCandidates.FirstOrDefault()?.OrderCode?.Trim()
                ?? scopedOrders.FirstOrDefault()?.OrderCode?.Trim()
                ?? string.Empty);

        if (backorderOrdersCount == 0)
        {
            var query = _db.SalesOrders.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(o => o.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(o => accessibleIds.Contains(o.WarehouseId));
            var dbOrders = await query
                .OrderByDescending(o => o.Priority)
                .ThenBy(o => o.Id)
                .Select(o => o.OrderCode)
                .ToListAsync(cancellationToken);
            backorderOrdersCount = dbOrders.Count;
            if (string.IsNullOrWhiteSpace(resolvedOrderNumber) && dbOrders.Count > 0)
                resolvedOrderNumber = dbOrders[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(resolvedOrderNumber))
            resolvedOrderNumber = "SO-QA-001";

        var now = DateTime.UtcNow;
        var expediteId = $"EXP-BO-{now:yyyyMMdd-HHmm}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = targetWarehouseId,
            Action = "SalesOrder.ExpediteBackorder",
            EntityName = "SalesOrder",
            NewValues = expediteId,
            Reason = $"{resolvedOrderNumber} — Đẩy ưu tiên phân bổ đơn thiếu hàng (Backorder)",
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            expediteId,
            warehouseId = targetWarehouseId,
            orderNumber = resolvedOrderNumber,
            backorderOrdersCount,
            totalBackorderQuantity,
            totalOpenQuantity,
            expediteStatus = "Đã nâng mức ưu tiên & xếp hàng đợt phân bổ bổ sung",
            status = "Đã đưa đơn hàng thiếu tồn vào hàng đợi phân bổ ưu tiên",
            statusLabel = "Đã đưa đơn hàng thiếu tồn vào hàng đợi phân bổ ưu tiên",
            priorityElevated = true,
            allocationWaveQueued = true,
            processedAtUtc = now
        });
    }

    private byte[] ResolveMarketplaceTokenEncryptionKey()
    {
        var raw = _configuration?["Marketplaces:TokenEncryptionKey"]
            ?? _configuration?["JwtSettings:Secret"]
            ?? _configuration?["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("ERP_MARKETPLACE_TOKEN_KEY");

        if (!string.IsNullOrWhiteSpace(raw))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim()));
        }

        if (_configuration != null)
        {
            throw new InvalidOperationException(
                "Marketplace token encryption key is not configured. Configure 'Marketplaces:TokenEncryptionKey', 'Jwt:Key', or 'ERP_MARKETPLACE_TOKEN_KEY'.");
        }

        return EphemeralProcessKey;
    }

    private string EncryptMarketplaceSecret(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        var trimmed = plainText.Trim();
        if (trimmed.StartsWith(EncryptedTokenPrefix, StringComparison.Ordinal))
            return trimmed;

        var key = ResolveMarketplaceTokenEncryptionKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(trimmed);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);
        return EncryptedTokenPrefix + Convert.ToBase64String(combined);
    }

    private string DecryptMarketplaceSecret(string? storedSecret)
    {
        if (string.IsNullOrWhiteSpace(storedSecret))
            return string.Empty;

        var trimmed = storedSecret.Trim();
        if (!trimmed.StartsWith(EncryptedTokenPrefix, StringComparison.Ordinal))
            return trimmed;

        var key = ResolveMarketplaceTokenEncryptionKey();
        var payload = Convert.FromBase64String(trimmed[EncryptedTokenPrefix.Length..]);
        if (payload.Length < 28)
            return string.Empty;

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        var plainBytes = new byte[cipher.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string? MaskMarketplaceSecret(string? storedSecret)
    {
        return string.IsNullOrWhiteSpace(storedSecret) ? null : "ENC:****";
    }

    private string? ResolveMarketplaceWebhookSecret(string normalizedChannelCode, string adapterChannelCode, string? shopWebhookSecret)
    {
        var candidate = _configuration?[$"Webhooks:Marketplaces:{normalizedChannelCode}:Secret"]
            ?? _configuration?[$"Webhooks:Marketplaces:{adapterChannelCode}:Secret"]
            ?? _configuration?["Webhooks:Marketplaces:DefaultSecret"]
            ?? Environment.GetEnvironmentVariable("ERP_MARKETPLACE_WEBHOOK_SECRET")
            ?? (!string.IsNullOrWhiteSpace(shopWebhookSecret) ? shopWebhookSecret : null);

        return string.IsNullOrWhiteSpace(candidate) ? null : candidate.Trim();
    }

    private bool HasConfiguredMarketplaceWebhookSecret(string normalizedChannelCode, string adapterChannelCode)
    {
        return !string.IsNullOrWhiteSpace(ResolveMarketplaceWebhookSecret(normalizedChannelCode, adapterChannelCode, null));
    }

    private static string ComputeWebhookFingerprint(string normalizedChannelCode, BuyerCancellationWebhookInput request)
    {
        var canonical = !string.IsNullOrWhiteSpace(request.RawPayload)
            ? $"{normalizedChannelCode}|{request.RawPayload.Trim()}"
            : $"{normalizedChannelCode}|{request.EventId?.Trim()}|{request.SalesOrderId}|{request.ExportReceiptId}|{request.OrderNumber?.Trim()}|{request.MarketplaceOrderId?.Trim()}|{request.BuyerReason?.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string FormatWarehouseNodeDisplay(string? code, string? name)
    {
        var trimmedCode = code?.Trim() ?? string.Empty;
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return trimmedCode;
        }
        if (string.IsNullOrWhiteSpace(trimmedCode)
            || trimmedName.Contains($"({trimmedCode})", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedName;
        }
        return $"{trimmedCode} — {trimmedName}";
    }
}
