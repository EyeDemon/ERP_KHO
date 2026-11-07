using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> GetCarrierConfigsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else if (currentUser == null || (currentUser.IsAuthenticated && !currentUser.IsGlobalAdmin))
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var targetWarehouseId = warehouseId is > 0
            ? warehouseId.Value
            : (accessibleIds is { Count: > 0 } ? accessibleIds.First() : 1);

        if (warehouseId.HasValue || accessibleIds == null || accessibleIds.Count > 0)
        {
            await EnsureCarrierConfigsSeededAsync(targetWarehouseId, cancellationToken);
        }

        var query = db.CarrierIntegrationConfigs.AsNoTracking();
        if (warehouseId is > 0)
        {
            query = query.Where(c => c.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(c => accessibleIds.Contains(c.WarehouseId));
        }

        var configs = await query
            .OrderBy(c => c.WarehouseId)
            .ThenBy(c => c.CarrierCode)
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = configs.Count,
            warehouseId = targetWarehouseId,
            items = configs.Select(c =>
            {
                var adapter = _carrierRegistry.ResolveAdapter(c.CarrierCode);
                var isGhn = string.Equals(c.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase);
                var ghnToken = isGhn
                    ? GhnLiveGatewayHelper.ResolveConfiguredGhnToken(null, c.EncryptedApiToken, configuration)
                    : null;
                var maskedToken = isGhn && !string.IsNullOrWhiteSpace(ghnToken)
                    ? GhnLiveGatewayHelper.MaskApiToken(ghnToken, configuration)
                    : GhnLiveGatewayHelper.MaskApiToken(c.EncryptedApiToken, configuration);
                var effectiveShopId = isGhn && !string.IsNullOrWhiteSpace(ghnToken) &&
                                      (string.IsNullOrWhiteSpace(c.ShopId) || c.ShopId.StartsWith("SHOP-GHN-", StringComparison.OrdinalIgnoreCase))
                    ? (configuration?["Carriers:GHN:ShopId"] ?? "235512")
                    : c.ShopId;
                var gatewayMode = isGhn && !string.IsNullOrWhiteSpace(ghnToken)
                    ? (c.IsSandbox ? "LIVE_GHN_DEV_SANDBOX" : "LIVE_GHN_PRODUCTION")
                    : "LOCAL_SANDBOX";

                return new
                {
                    c.Id,
                    c.WarehouseId,
                    c.CarrierCode,
                    carrierDisplayName = adapter.CarrierDisplayName,
                    shopId = effectiveShopId,
                    hasApiToken = !string.IsNullOrWhiteSpace(maskedToken),
                    maskedApiToken = maskedToken,
                    gatewayMode,
                    c.IsSandbox,
                    c.SortingHubPrefix,
                    c.BaseShippingFeeVnd,
                    c.IsActive,
                    c.UpdatedAtUtc
                };
            }),
            statusLabel = $"Đã tải {configs.Count} cấu hình kết nối hãng vận chuyển (GHN, GHTK, ViettelPost, J&T Express)"
        });
    }

    public async Task<WmsEndpointOutcome> UpsertCarrierConfigAsync(
        UpsertCarrierConfigInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CARRIER_CODE_REQUIRED",
                errorCode = "CARRIER_CODE_REQUIRED",
                message = "Bắt buộc chọn mã đơn vị vận chuyển (CarrierCode: GHN, GHTK, VIETTELPOST, JT_EXPRESS)."
            });
        }

        var targetWarehouseId = request.WarehouseId is > 0 ? request.WarehouseId.Value : 1;
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(request.CarrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);
        var now = DateTime.UtcNow;

        var config = await db.CarrierIntegrationConfigs
            .FirstOrDefaultAsync(c => c.WarehouseId == targetWarehouseId && c.CarrierCode == normCarrier, cancellationToken);

        var encryptedInputToken = !string.IsNullOrWhiteSpace(request.ApiToken) && !request.ApiToken.Contains("****", StringComparison.Ordinal)
            ? GhnLiveGatewayHelper.EncryptToken(request.ApiToken, configuration)
            : null;

        if (config == null)
        {
            config = new ERP.Domain.Entities.CarrierIntegrationConfig
            {
                WarehouseId = targetWarehouseId,
                CarrierCode = normCarrier,
                ShopId = !string.IsNullOrWhiteSpace(request.ShopId) ? request.ShopId.Trim() : $"SHOP-{normCarrier}-{targetWarehouseId:D2}",
                EncryptedApiToken = encryptedInputToken ?? string.Empty,
                IsSandbox = request.IsSandbox ?? true,
                WebhookSecretHash = !string.IsNullOrWhiteSpace(request.WebhookSecret) ? request.WebhookSecret.Trim() : string.Empty,
                SortingHubPrefix = !string.IsNullOrWhiteSpace(request.SortingHubPrefix) ? request.SortingHubPrefix.Trim() : $"HUB-{normCarrier}",
                BaseShippingFeeVnd = request.BaseShippingFeeVnd is > 0m ? Math.Round(request.BaseShippingFeeVnd.Value, 4) : 22000m,
                IsActive = request.IsActive ?? true,
                UpdatedAtUtc = now
            };
            db.CarrierIntegrationConfigs.Add(config);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.ShopId))
                config.ShopId = request.ShopId.Trim();
            if (!string.IsNullOrWhiteSpace(encryptedInputToken))
                config.EncryptedApiToken = encryptedInputToken;
            if (request.IsSandbox.HasValue)
                config.IsSandbox = request.IsSandbox.Value;
            if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
                config.WebhookSecretHash = request.WebhookSecret.Trim();
            if (!string.IsNullOrWhiteSpace(request.SortingHubPrefix))
                config.SortingHubPrefix = request.SortingHubPrefix.Trim();
            if (request.BaseShippingFeeVnd is > 0m)
                config.BaseShippingFeeVnd = Math.Round(request.BaseShippingFeeVnd.Value, 4);
            if (request.IsActive.HasValue)
                config.IsActive = request.IsActive.Value;
            config.UpdatedAtUtc = now;
        }

        var isGhn = string.Equals(normCarrier, "GHN", StringComparison.OrdinalIgnoreCase);
        var ghnToken = isGhn
            ? GhnLiveGatewayHelper.ResolveConfiguredGhnToken(request.ApiToken, config.EncryptedApiToken, configuration)
            : null;
        var maskedToken = isGhn && !string.IsNullOrWhiteSpace(ghnToken)
            ? GhnLiveGatewayHelper.MaskApiToken(ghnToken, configuration)
            : GhnLiveGatewayHelper.MaskApiToken(config.EncryptedApiToken, configuration);
        var gatewayMode = isGhn && !string.IsNullOrWhiteSpace(ghnToken)
            ? (config.IsSandbox ? "LIVE_GHN_DEV_SANDBOX" : "LIVE_GHN_PRODUCTION")
            : "LOCAL_SANDBOX";

        var resolvedActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);
        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "CarrierIntegrationConfig",
            Action = "Shipment.UpsertCarrierConfig",
            NewValues = $"CarrierCode={normCarrier}; ShopId={config.ShopId}; Token={maskedToken}; IsSandbox={config.IsSandbox}; BaseShippingFeeVnd={config.BaseShippingFeeVnd}",
            Reason = $"Cập nhật cấu hình kết nối ĐVVC {adapter.CarrierDisplayName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            config.Id,
            config.WarehouseId,
            config.CarrierCode,
            carrierDisplayName = adapter.CarrierDisplayName,
            config.ShopId,
            hasApiToken = !string.IsNullOrWhiteSpace(maskedToken),
            maskedApiToken = maskedToken,
            gatewayMode,
            config.IsSandbox,
            config.SortingHubPrefix,
            config.BaseShippingFeeVnd,
            config.IsActive,
            config.UpdatedAtUtc,
            statusLabel = $"Đã lưu cấu hình kết nối {adapter.CarrierDisplayName} cho kho #{targetWarehouseId}"
        });
    }

    private async Task EnsureCarrierConfigsSeededAsync(int warehouseId, CancellationToken ct)
    {
        var existingCodes = await db.CarrierIntegrationConfigs.AsNoTracking()
            .Where(c => c.WarehouseId == warehouseId)
            .Select(c => c.CarrierCode)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var added = false;
        var now = DateTime.UtcNow;

        foreach (var adapter in _carrierRegistry.GetAllAdapters())
        {
            if (existingSet.Contains(adapter.CarrierCode))
                continue;

            var defaultFee = adapter.CarrierCode switch
            {
                "GHN" => 22000m,
                "GHTK" => 20000m,
                "VIETTELPOST" => 21000m,
                "JT_EXPRESS" => 19500m,
                _ => 22000m
            };

            db.CarrierIntegrationConfigs.Add(new ERP.Domain.Entities.CarrierIntegrationConfig
            {
                WarehouseId = warehouseId,
                CarrierCode = adapter.CarrierCode,
                ShopId = $"SHOP-{adapter.CarrierCode}-{warehouseId:D2}",
                EncryptedApiToken = string.Empty,
                IsSandbox = true,
                WebhookSecretHash = string.Empty,
                SortingHubPrefix = $"HUB-{adapter.CarrierCode}",
                BaseShippingFeeVnd = defaultFee,
                IsActive = true,
                UpdatedAtUtc = now
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
