using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> BookCarrierWaybillAsync(
        CarrierWaybillBookingRequest? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null || (string.IsNullOrWhiteSpace(request.RawAddress) && string.IsNullOrWhiteSpace(request.Ward)))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "WAYBILL_ADDRESS_REQUIRED",
                errorCode = "WAYBILL_ADDRESS_REQUIRED",
                message = "Bắt buộc cung cấp địa chỉ người nhận để tạo vận đơn điện tử (AWB)."
            });
        }

        ERP.Domain.Entities.Shipment? shipment = null;
        if (request.ShipmentId is > 0)
        {
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
            if (shipment == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "SHIPMENT_NOT_FOUND",
                    errorCode = "SHIPMENT_NOT_FOUND",
                    message = $"Không tìm thấy chuyến giao hàng #{request.ShipmentId.Value}."
                });
            }
        }

        ERP.Domain.Entities.ExportReceipt? exportReceipt = null;
        if (request.ExportReceiptId is > 0)
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.Id == request.ExportReceiptId.Value, cancellationToken);
            if (exportReceipt == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "EXPORT_RECEIPT_NOT_FOUND",
                    errorCode = "EXPORT_RECEIPT_NOT_FOUND",
                    message = $"Không tìm thấy phiếu xuất kho #{request.ExportReceiptId.Value}."
                });
            }
        }

        if (shipment != null && exportReceipt == null)
        {
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(
                e => e.ShipmentId == shipment.Id || (shipment.SourceType == "ExportReceipt" && e.Id == shipment.SourceId),
                cancellationToken);
        }
        else if (exportReceipt != null && shipment == null && exportReceipt.ShipmentId is > 0)
        {
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == exportReceipt.ShipmentId.Value, cancellationToken);
        }

        var targetWarehouseId = shipment?.WarehouseId
            ?? exportReceipt?.WarehouseId
            ?? (request.WarehouseId > 0 ? request.WarehouseId : 1);

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(db, cancellationToken);
        await EnsureCarrierConfigsSeededAsync(targetWarehouseId, cancellationToken);

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(request.CarrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        var config = await db.CarrierIntegrationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WarehouseId == targetWarehouseId && c.CarrierCode == adapter.CarrierCode, cancellationToken);

        var matchedUnit = await ResolveVnAdministrativeUnitAsync(
            request.RawAddress,
            request.Province,
            request.District,
            request.Ward,
            cancellationToken);

        string provinceName;
        string wardName;
        string normalizedAddress;
        string sortingHubCode;
        string routeCode;

        if (matchedUnit != null)
        {
            provinceName = matchedUnit.ProvinceName;
            wardName = matchedUnit.WardName;
            var streetPart = string.IsNullOrWhiteSpace(request.RawAddress) ? string.Empty : $"{request.RawAddress.Trim()}, ";
            normalizedAddress = streetPart.Contains(matchedUnit.WardName, StringComparison.OrdinalIgnoreCase)
                ? request.RawAddress.Trim()
                : $"{streetPart}{matchedUnit.WardName}, {matchedUnit.ProvinceName}";

            var mapping = matchedUnit.CarrierMappings.FirstOrDefault(m =>
                string.Equals(m.CarrierCode, adapter.CarrierCode, StringComparison.OrdinalIgnoreCase) ||
                (adapter.CarrierCode == "VIETTELPOST" && string.Equals(m.CarrierCode, "VTPOST", StringComparison.OrdinalIgnoreCase)));

            var hubPrefix = !string.IsNullOrWhiteSpace(config?.SortingHubPrefix)
                ? config.SortingHubPrefix.Trim()
                : $"HUB-{adapter.CarrierCode}";

            sortingHubCode = !string.IsNullOrWhiteSpace(mapping?.SortingHubCode)
                ? mapping.SortingHubCode
                : $"{hubPrefix}-{matchedUnit.ProvinceCode}-{matchedUnit.WardCode}";
            routeCode = !string.IsNullOrWhiteSpace(mapping?.RouteCode)
                ? mapping.RouteCode
                : $"RT-{adapter.CarrierCode}-{matchedUnit.ProvinceCode}-{matchedUnit.WardCode}";
        }
        else if (!string.IsNullOrWhiteSpace(request.Ward)
                 && !string.IsNullOrWhiteSpace(request.Province)
                 && !request.Ward.Contains("Không Tồn Tại", StringComparison.OrdinalIgnoreCase)
                 && !request.Ward.Contains("Không Có Thật", StringComparison.OrdinalIgnoreCase))
        {
            provinceName = request.Province.Trim();
            wardName = request.Ward.Trim();
            normalizedAddress = string.IsNullOrWhiteSpace(request.RawAddress)
                ? $"{wardName}, {provinceName}"
                : $"{request.RawAddress.Trim()}, {wardName}, {provinceName}";
            var hubPrefix = !string.IsNullOrWhiteSpace(config?.SortingHubPrefix)
                ? config.SortingHubPrefix.Trim()
                : $"HUB-{adapter.CarrierCode}";
            sortingHubCode = $"{hubPrefix}-VN-001";
            routeCode = $"RT-{adapter.CarrierCode}-VN-001";
        }
        else
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "ADDRESS_WARD_UNRESOLVED",
                errorCode = "ADDRESS_WARD_UNRESOLVED",
                matched = false,
                message = "Không thể xác định Phường/Xã theo mô hình hành chính 2 cấp (Tỉnh/TP - Phường/Xã) để tạo vận đơn."
            });
        }

        var existingBookedEvent = shipment != null
            ? await db.ShipmentTrackingEvents.AsNoTracking()
                .Where(e => e.ShipmentId == shipment.Id && e.EventType == "WaybillBooked")
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var existingCancelledAfterBooked = shipment != null && existingBookedEvent != null &&
            await db.ShipmentTrackingEvents.AsNoTracking()
                .AnyAsync(e => e.ShipmentId == shipment.Id && e.EventType == "WaybillCancelled" && e.Id > existingBookedEvent.Id, cancellationToken);

        var activeExistingAwb = existingBookedEvent != null && !existingCancelledAfterBooked && shipment?.Status != ShipmentStatus.Cancelled
            ? existingBookedEvent.SourceEventId
            : (!string.IsNullOrWhiteSpace(exportReceipt?.TrackingNumber) && exportReceipt.Status != ReceiptStatus.Cancelled
                ? exportReceipt.TrackingNumber
                : null);

        if (!string.IsNullOrWhiteSpace(activeExistingAwb))
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "GHN_WAYBILL_ALREADY_BOOKED",
                errorCode = "GHN_WAYBILL_ALREADY_BOOKED",
                existingOrderCode = activeExistingAwb,
                trackingNumber = activeExistingAwb,
                shipmentId = shipment?.Id,
                exportReceiptId = exportReceipt?.Id,
                message = $"Chuyến giao hàng đã có mã vận đơn đang hoạt động ({activeExistingAwb}). Vui lòng hủy vận đơn cũ trước khi tạo mới."
            });
        }

        var deterministicClientOrderCode = !string.IsNullOrWhiteSpace(request.ClientOrderCode)
            ? request.ClientOrderCode.Trim()
            : $"ERPKHO-T{db.CurrentTenantId}-S{shipment?.Id ?? exportReceipt?.Id ?? 0}-{shipment?.ShipmentCode ?? exportReceipt?.Code ?? "AWB"}";

        var effectiveRequest = request with
        {
            WarehouseId = targetWarehouseId,
            ShipmentId = shipment?.Id ?? request.ShipmentId,
            ExportReceiptId = exportReceipt?.Id ?? request.ExportReceiptId,
            CarrierCode = adapter.CarrierCode,
            Province = provinceName,
            Ward = wardName,
            ClientOrderCode = deterministicClientOrderCode
        };

        var booking = adapter.BookWaybill(
            effectiveRequest,
            sortingHubCode,
            routeCode,
            normalizedAddress,
            config?.BaseShippingFeeVnd ?? 22000m,
            config?.IsSandbox ?? true);

        var gatewayMode = "LOCAL_SANDBOX";
        var effectiveShopId = config?.ShopId ?? "235512";
        string? maskedApiToken = GhnLiveGatewayHelper.MaskApiToken(config?.EncryptedApiToken, configuration);
        string? expectedDeliveryTime = null;

        var shouldUseLiveGhnGateway =
            string.Equals(adapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase) &&
            (request.RequireLiveGateway == true ||
             configuration != null ||
             httpClientFactory != null ||
             (!string.IsNullOrWhiteSpace(config?.EncryptedApiToken) &&
              !config.EncryptedApiToken.StartsWith("SANDBOX_TOKEN_", StringComparison.OrdinalIgnoreCase)));

        if (shouldUseLiveGhnGateway)
        {
            var resolvedGhnToken = GhnLiveGatewayHelper.ResolveConfiguredGhnToken(
                null,
                config?.EncryptedApiToken,
                configuration);

            if (!string.IsNullOrWhiteSpace(resolvedGhnToken))
            {
                effectiveShopId = !string.IsNullOrWhiteSpace(config?.ShopId) && !config.ShopId.StartsWith("SHOP-GHN-", StringComparison.OrdinalIgnoreCase)
                    ? config.ShopId
                    : (configuration?["Carriers:GHN:ShopId"] ?? "235512");
                maskedApiToken = GhnLiveGatewayHelper.MaskApiToken(resolvedGhnToken, configuration);

                string? configFromWard = null;
                if (!string.IsNullOrWhiteSpace(config?.SortingHubPrefix))
                {
                    var parts = config.SortingHubPrefix.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && parts[^1].All(char.IsDigit))
                        configFromWard = parts[^1];
                }

                var ghnMap = matchedUnit?.CarrierMappings.FirstOrDefault(m =>
                    string.Equals(m.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase));
                var toDistId = ghnMap != null && int.TryParse(ghnMap.CarrierDistrictId, out var parsedDist) && parsedDist > 0
                    ? parsedDist
                    : 1442;
                var toWardCode = ghnMap?.CarrierWardCode;

                using var http = GhnLiveGatewayHelper.CreateHttpClient(httpClientFactory);
                var liveOrder = await GhnLiveGatewayHelper.CreateShippingOrderAsync(
                    http,
                    resolvedGhnToken,
                    effectiveShopId,
                    GhnLiveGatewayHelper.DevSandboxBaseUrl,
                    effectiveRequest,
                    fromWardCode: configFromWard,
                    fromWardName: "Phường Tân Thới Nhất",
                    toDistrictId: toDistId,
                    toWardCode: toWardCode,
                    normalizedAddress: normalizedAddress,
                    ct: cancellationToken);

                if (!liveOrder.Succeeded || string.IsNullOrWhiteSpace(liveOrder.OrderCode))
                {
                    return WmsEndpointOutcome.UnprocessableEntity(new
                    {
                        code = "GHN_SANDBOX_REJECTED",
                        errorCode = "GHN_SANDBOX_REJECTED",
                        carrierCode = "GHN",
                        shopId = effectiveShopId,
                        message = $"Cổng GHN Dev Sandbox từ chối tạo vận đơn: {liveOrder.ErrorMessage ?? "Không nhận được order_code hợp lệ."}"
                    });
                }

                booking = new CarrierWaybillBookingResult(
                    CarrierCode: "GHN",
                    AwbTrackingCode: liveOrder.OrderCode,
                    SortingHubCode: !string.IsNullOrWhiteSpace(liveOrder.SortCode) ? liveOrder.SortCode : sortingHubCode,
                    RouteCode: routeCode,
                    EstimatedShippingFeeVnd: liveOrder.TotalFeeVnd > 0m ? liveOrder.TotalFeeVnd : booking.EstimatedShippingFeeVnd,
                    AdministrativeModel: booking.AdministrativeModel,
                    NormalizedAddress: normalizedAddress,
                    ProviderPayloadJson: liveOrder.RawResponseJson,
                    IsSandbox: config?.IsSandbox ?? true);
                gatewayMode = "LIVE_GHN_DEV_SANDBOX";
                expectedDeliveryTime = liveOrder.ExpectedDeliveryTime;
            }
            else if (request.RequireLiveGateway == true)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "GHN_TOKEN_REQUIRED",
                    errorCode = "GHN_TOKEN_REQUIRED",
                    message = "Chưa cấu hình GHN API Token để tạo vận đơn trực tiếp trên cổng GHN."
                });
            }
        }

        return await PersistBookedWaybillTransactionAsync(
            shipment,
            exportReceipt,
            targetWarehouseId,
            adapter,
            booking,
            deterministicClientOrderCode,
            effectiveShopId,
            maskedApiToken,
            gatewayMode,
            expectedDeliveryTime,
            request.CodAmountVnd,
            actorUserId,
            cancellationToken);
    }
}
