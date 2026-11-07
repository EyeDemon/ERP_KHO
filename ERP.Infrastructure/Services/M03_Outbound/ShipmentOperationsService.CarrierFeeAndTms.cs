using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> CalculateCarrierFeeAsync(
        CalculateCarrierFeeInput? request = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var targetWarehouseId = request?.WarehouseId is > 0 ? request.WarehouseId.Value : 1;
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        await EnsureCarrierConfigsSeededAsync(targetWarehouseId, cancellationToken);
        var allConfigs = await db.CarrierIntegrationConfigs.AsNoTracking()
            .Where(c => c.WarehouseId == targetWarehouseId)
            .ToListAsync(cancellationToken);
        var config = allConfigs.FirstOrDefault(c => string.Equals(c.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase));

        var weightGrams = request?.WeightGrams ?? 0;
        if (weightGrams <= 0 && request?.ShipmentId is > 0)
        {
            var shipmentWeightKg = await db.ShipmentHandlingUnits.AsNoTracking()
                .Where(shu => shu.ShipmentId == request.ShipmentId.Value)
                .Join(db.HandlingUnits.AsNoTracking(),
                    shu => shu.HandlingUnitId,
                    hu => hu.Id,
                    (_, hu) => hu.GrossWeightKg ?? hu.NetWeightKg ?? 0m)
                .SumAsync(cancellationToken);
            if (shipmentWeightKg > 0m)
            {
                weightGrams = (int)Math.Round(shipmentWeightKg * 1000m);
            }
        }
        if (weightGrams <= 0)
            weightGrams = 1000;

        var requestedCarrierCode = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(request?.CarrierCode);
        var selectedAdapter = _carrierRegistry.ResolveAdapter(requestedCarrierCode);

        var resolvedToken = GhnLiveGatewayHelper.ResolveConfiguredGhnToken(
            null,
            config?.EncryptedApiToken,
            configuration);

        if (request?.CompareAllCarriers != true &&
            string.Equals(selectedAdapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(resolvedToken))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "GHN_TOKEN_REQUIRED",
                errorCode = "GHN_TOKEN_REQUIRED",
                message = "Chưa cấu hình GHN API Token để tính phí vận chuyển."
            });
        }

        var effectiveShopId = !string.IsNullOrWhiteSpace(config?.ShopId) && !config.ShopId.StartsWith("SHOP-GHN-", StringComparison.OrdinalIgnoreCase)
            ? config.ShopId
            : (configuration?["Carriers:GHN:ShopId"] ?? "235512");

        string? configFromWard = null;
        if (!string.IsNullOrWhiteSpace(config?.SortingHubPrefix))
        {
            var parts = config.SortingHubPrefix.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[^1].All(char.IsDigit))
                configFromWard = parts[^1];
        }

        GhnFeeCalculationResult? ghnFeeResult = null;
        if (!string.IsNullOrWhiteSpace(resolvedToken))
        {
            using var http = GhnLiveGatewayHelper.CreateHttpClient(httpClientFactory);
            ghnFeeResult = await GhnLiveGatewayHelper.CalculateShippingFeeAsync(
                http,
                resolvedToken,
                effectiveShopId,
                GhnLiveGatewayHelper.DevSandboxBaseUrl,
                fromDistrictId: request?.FromDistrictId ?? 1454,
                fromWardCode: request?.FromWardCode ?? configFromWard,
                toDistrictId: request?.ToDistrictId ?? 1442,
                toWardCode: request?.ToWardCode,
                weightGrams: weightGrams,
                lengthCm: request?.LengthCm ?? 20,
                widthCm: request?.WidthCm ?? 15,
                heightCm: request?.HeightCm ?? 10,
                insuranceValue: (int)Math.Round(request?.InsuranceValueVnd ?? 0m),
                serviceTypeId: request?.ServiceTypeId ?? 2,
                ct: cancellationToken);
        }

        if (request?.CompareAllCarriers != true &&
            string.Equals(selectedAdapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase) &&
            (ghnFeeResult == null || !ghnFeeResult.Succeeded))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "GHN_FEE_CALCULATION_FAILED",
                errorCode = "GHN_FEE_CALCULATION_FAILED",
                message = $"Không thể tính cước GHN: {ghnFeeResult?.ErrorMessage ?? "Chưa kết nối được cổng GHN."}"
            });
        }

        var quoteBookingRequest = new CarrierWaybillBookingRequest(
            WarehouseId: targetWarehouseId,
            ShipmentId: request?.ShipmentId,
            ExportReceiptId: request?.ExportReceiptId,
            CarrierCode: selectedAdapter.CarrierCode,
            WeightGrams: weightGrams,
            CodAmountVnd: 0m,
            OrderValueVnd: request?.InsuranceValueVnd ?? 0m);

        var carrierQuotes = _carrierRegistry.GetAllAdapters()
            .Select(adapter =>
            {
                var adapterCfg = allConfigs.FirstOrDefault(c => string.Equals(c.CarrierCode, adapter.CarrierCode, StringComparison.OrdinalIgnoreCase));
                var simulated = adapter.BookWaybill(
                    quoteBookingRequest with { CarrierCode = adapter.CarrierCode },
                    sortingHubCode: $"HUB-{adapter.CarrierCode}-HCM",
                    routeCode: $"RT-{adapter.CarrierCode}-Q1",
                    normalizedAddress: "Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
                    baseFeeVnd: adapterCfg?.BaseShippingFeeVnd ?? 0m,
                    isSandbox: adapterCfg?.IsSandbox ?? true);

                var isLiveGhn = string.Equals(adapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase) && ghnFeeResult is { Succeeded: true };
                var totalFeeVnd = isLiveGhn ? ghnFeeResult!.TotalFeeVnd : simulated.EstimatedShippingFeeVnd;
                var serviceFeeVnd = isLiveGhn ? ghnFeeResult!.ServiceFeeVnd : simulated.EstimatedShippingFeeVnd;
                var insFeeVnd = isLiveGhn ? ghnFeeResult!.InsuranceFeeVnd : 0m;
                var etaHours = adapter.CarrierCode switch
                {
                    "GHN" => 24,
                    "GHTK" => 28,
                    "VIETTELPOST" => 36,
                    "JT_EXPRESS" => 30,
                    _ => 24
                };

                return new
                {
                    carrierCode = adapter.CarrierCode,
                    carrierDisplayName = adapter.CarrierDisplayName,
                    totalFee = totalFeeVnd,
                    serviceFee = serviceFeeVnd,
                    insuranceFee = insFeeVnd,
                    estimatedDeliveryHours = etaHours,
                    estimatedDeliveryLabel = $"Dự kiến giao trong {etaHours}h",
                    gatewayMode = isLiveGhn ? "LIVE_GHN_DEV_SANDBOX" : "ADAPTER_RATE_CARD",
                    isSelected = string.Equals(adapter.CarrierCode, selectedAdapter.CarrierCode, StringComparison.OrdinalIgnoreCase)
                };
            })
            .OrderBy(q => q.totalFee)
            .ToList();

        var selectedQuote = carrierQuotes.FirstOrDefault(q => string.Equals(q.carrierCode, selectedAdapter.CarrierCode, StringComparison.OrdinalIgnoreCase))
            ?? carrierQuotes[0];

        if (request?.ShipmentId is > 0)
        {
            var ledger = await db.ShipmentFinancialLedgers
                .FirstOrDefaultAsync(l => l.ShipmentId == request.ShipmentId.Value, cancellationToken);
            if (ledger != null)
            {
                ledger.EstimatedShippingFee = selectedQuote.totalFee;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId = targetWarehouseId,
            shipmentId = request?.ShipmentId,
            exportReceiptId = request?.ExportReceiptId,
            carrierCode = selectedAdapter.CarrierCode,
            carrierDisplayName = selectedAdapter.CarrierDisplayName,
            shopId = effectiveShopId,
            maskedApiToken = GhnLiveGatewayHelper.MaskApiToken(resolvedToken, configuration),
            gatewayMode = selectedQuote.gatewayMode,
            totalFee = selectedQuote.totalFee,
            total = selectedQuote.totalFee,
            serviceFee = selectedQuote.serviceFee,
            insuranceFee = selectedQuote.insuranceFee,
            fromDistrictId = ghnFeeResult?.FromDistrictId ?? (request?.FromDistrictId ?? 1454),
            fromWardCode = ghnFeeResult?.FromWardCode ?? (request?.FromWardCode ?? configFromWard ?? "21211"),
            toDistrictId = ghnFeeResult?.ToDistrictId ?? (request?.ToDistrictId ?? 1442),
            toWardCode = ghnFeeResult?.ToWardCode ?? (request?.ToWardCode ?? "20101"),
            weightGrams = ghnFeeResult?.WeightGrams ?? weightGrams,
            carrierQuotes,
            recommendedCarrierCode = carrierQuotes[0].carrierCode,
            statusLabel = $"Cước vận chuyển {selectedAdapter.CarrierDisplayName}: {selectedQuote.totalFee:N0} VNĐ (Đã so sánh 4 hãng ĐVVC)"
        });
    }

    public async Task<WmsEndpointOutcome> SyncTmsManifestAsync(
        SyncTmsManifestInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var query = db.Shipments.AsNoTracking();
        var huQuery = db.HandlingUnits.AsNoTracking()
            .Where(h => h.Status != HandlingUnitStatus.Cancelled);
        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
            huQuery = huQuery.Where(h => h.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
            huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
        }

        var syncedShipmentCount = await query.CountAsync(cancellationToken);
        var stagedOrLoadedCount = await query.CountAsync(
            s => s.Status == ShipmentStatus.Ready
                 || s.Status == ShipmentStatus.Staging
                 || s.Status == ShipmentStatus.Loading
                 || s.Status == ShipmentStatus.Loaded
                 || s.Status == ShipmentStatus.Dispatched
                 || s.Status == ShipmentStatus.InTransit,
            cancellationToken);
        var totalHandlingUnitsCount = await huQuery.CountAsync(cancellationToken);
        var totalGrossWeightKg = Math.Round(await huQuery.SumAsync(h => h.GrossWeightKg ?? 0m, cancellationToken), 2);
        var totalVolumeM3 = Math.Round(await huQuery.SumAsync(h => h.VolumeM3 ?? 0m, cancellationToken), 4);

        var now = DateTime.UtcNow;
        var manifestId = $"TMS-MNF-{now:yyyyMMdd-HHmm}";
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            Action = "Shipment.SyncTmsManifest",
            EntityName = "TmsManifest",
            NewValues = manifestId,
            Reason = $"Đồng bộ bảng kê vận tải & ghép tải ({manifestId})",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            manifestId,
            warehouseId = targetWarehouseId,
            carrierCode = string.IsNullOrWhiteSpace(request?.CarrierCode) ? "VTP-EXPRESS" : request.CarrierCode.Trim(),
            syncedShipmentCount,
            stagedOrLoadedCount,
            totalHandlingUnitsCount,
            totalGrossWeightKg,
            totalVolumeM3,
            ediStatus = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            status = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            statusLabel = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            syncedAtUtc = now,
        });
    }
}
