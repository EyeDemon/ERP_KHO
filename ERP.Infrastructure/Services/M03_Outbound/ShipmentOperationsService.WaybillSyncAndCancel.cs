using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> SyncCarrierWaybillDetailAsync(
        int shipmentId,
        SyncGhnOrderDetailInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "SHIPMENT_NOT_FOUND",
                errorCode = "SHIPMENT_NOT_FOUND",
                message = $"Không tìm thấy chuyến giao hàng #{shipmentId}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(shipment.WarehouseId, cancellationToken);
        }

        var ledger = await db.ShipmentFinancialLedgers
            .FirstOrDefaultAsync(l => l.ShipmentId == shipment.Id, cancellationToken);
        var exportReceipt = await db.ExportReceipts
            .FirstOrDefaultAsync(e => e.ShipmentId == shipment.Id, cancellationToken);

        var orderCode = !string.IsNullOrWhiteSpace(request?.OrderCode)
            ? request.OrderCode.Trim()
            : (ledger?.TrackingNumber ?? exportReceipt?.TrackingNumber ?? shipment.SourceCode);

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "WAYBILL_ORDER_CODE_REQUIRED",
                errorCode = "WAYBILL_ORDER_CODE_REQUIRED",
                message = "Chuyến giao hàng chưa có mã vận đơn (order_code) để đồng bộ chi tiết."
            });
        }

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(
            request?.CarrierCode ?? ledger?.CarrierCode ?? exportReceipt?.CarrierCode ?? "GHN");
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        var config = await db.CarrierIntegrationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WarehouseId == shipment.WarehouseId && c.CarrierCode == adapter.CarrierCode, cancellationToken);

        var rawCarrierStatus = "ready_to_pick";
        var gatewayMode = "LOCAL_SANDBOX";
        string? leadtimeIso = null;
        string? updatedDateIso = null;
        decimal syncedFee = ledger?.EstimatedShippingFee ?? 22000m;
        decimal syncedCod = ledger?.ExpectedCodAmount ?? 0m;

        if (string.Equals(adapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedToken = GhnLiveGatewayHelper.ResolveConfiguredGhnToken(
                null,
                config?.EncryptedApiToken,
                configuration);

            if (!string.IsNullOrWhiteSpace(resolvedToken))
            {
                using var http = GhnLiveGatewayHelper.CreateHttpClient(httpClientFactory);
                var detailResult = await GhnLiveGatewayHelper.GetShippingOrderDetailAsync(
                    http,
                    resolvedToken,
                    GhnLiveGatewayHelper.DevSandboxBaseUrl,
                    orderCode,
                    cancellationToken);

                if (detailResult.Succeeded)
                {
                    rawCarrierStatus = detailResult.RawStatus;
                    gatewayMode = "LIVE_GHN_DEV_SANDBOX";
                    leadtimeIso = detailResult.LeadtimeIso;
                    updatedDateIso = detailResult.UpdatedDateIso;
                    if (detailResult.TotalFeeVnd > 0m)
                        syncedFee = detailResult.TotalFeeVnd;
                    if (detailResult.CodAmountVnd > 0m)
                        syncedCod = detailResult.CodAmountVnd;
                }
            }
        }

        var mappedStatus = MapCarrierRawStatusToShipmentStatus(rawCarrierStatus, shipment.Status, out var regressionBlocked);
        var now = DateTime.UtcNow;
        var prevStatus = shipment.Status;
        if (!regressionBlocked && mappedStatus != shipment.Status)
        {
            shipment.Status = mappedStatus;
            if (mappedStatus == ShipmentStatus.Dispatched)
                shipment.DispatchedAt ??= now;
            else if (mappedStatus == ShipmentStatus.InTransit)
                shipment.InTransitAt ??= now;
            else if (mappedStatus == ShipmentStatus.Delivered)
                shipment.CompletedAt ??= now;
            else if (mappedStatus is ShipmentStatus.DeliveryFailed or ShipmentStatus.ReturnToWarehouse)
                shipment.DeliveryFailedAt ??= now;
        }

        if (ledger != null)
        {
            if (syncedFee > 0m)
                ledger.EstimatedShippingFee = syncedFee;
            if (mappedStatus == ShipmentStatus.ReturnToWarehouse)
            {
                ledger.ExpectedCodAmount = 0m;
                ledger.CodVariance = 0m;
            }
        }

        var resolvedActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);
        db.ShipmentTrackingEvents.Add(new ERP.Domain.Entities.ShipmentTrackingEvent
        {
            ShipmentId = shipment.Id,
            EventType = $"CarrierSyncDetail.{adapter.CarrierCode}",
            FromStatus = prevStatus,
            ToStatus = shipment.Status,
            OccurredAt = now,
            RecordedAt = now,
            Source = adapter.CarrierCode,
            SourceEventId = $"{orderCode}-{rawCarrierStatus}-{now:HHmmss}",
            Note = $"Đồng bộ chi tiết vận đơn {orderCode} từ {adapter.CarrierDisplayName}: status={rawCarrierStatus}",
            RecordedBy = resolvedActorId
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment.Id,
            shipmentCode = shipment.ShipmentCode,
            warehouseId = shipment.WarehouseId,
            carrierCode = adapter.CarrierCode,
            carrierDisplayName = adapter.CarrierDisplayName,
            orderCode,
            trackingNumber = orderCode,
            carrierStatus = rawCarrierStatus,
            shipmentStatus = shipment.Status.ToString(),
            stateRegressionBlocked = regressionBlocked,
            gatewayMode,
            estimatedShippingFeeVnd = syncedFee,
            codAmountVnd = ledger?.ExpectedCodAmount ?? syncedCod,
            leadtimeIso,
            updatedDateIso,
            syncedAtUtc = now,
            statusLabel = $"Đã đồng bộ trạng thái vận đơn {orderCode} ({rawCarrierStatus} → {shipment.Status})"
        });
    }

    public async Task<WmsEndpointOutcome> CancelCarrierWaybillAsync(
        int shipmentId,
        CancelGhnWaybillInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "SHIPMENT_NOT_FOUND",
                errorCode = "SHIPMENT_NOT_FOUND",
                message = $"Không tìm thấy chuyến giao hàng #{shipmentId}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(shipment.WarehouseId, cancellationToken);
        }

        if (shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.InTransit or ShipmentStatus.Delivered or ShipmentStatus.Completed)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "WAYBILL_CANCEL_INVALID_STATE",
                errorCode = "WAYBILL_CANCEL_INVALID_STATE",
                shipmentStatus = shipment.Status.ToString(),
                message = $"Không thể hủy vận đơn khi chuyến giao hàng đã ở trạng thái '{shipment.Status}'."
            });
        }

        var ledger = await db.ShipmentFinancialLedgers
            .FirstOrDefaultAsync(l => l.ShipmentId == shipment.Id, cancellationToken);
        var exportReceipt = await db.ExportReceipts
            .FirstOrDefaultAsync(e => e.ShipmentId == shipment.Id, cancellationToken);

        var orderCode = !string.IsNullOrWhiteSpace(request?.OrderCode)
            ? request.OrderCode.Trim()
            : (ledger?.TrackingNumber ?? exportReceipt?.TrackingNumber ?? shipment.SourceCode);

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "WAYBILL_ORDER_CODE_REQUIRED",
                errorCode = "WAYBILL_ORDER_CODE_REQUIRED",
                message = "Chuyến giao hàng chưa có mã vận đơn (order_code) để hủy."
            });
        }

        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(
            request?.CarrierCode ?? ledger?.CarrierCode ?? exportReceipt?.CarrierCode ?? "GHN");
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        var config = await db.CarrierIntegrationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WarehouseId == shipment.WarehouseId && c.CarrierCode == adapter.CarrierCode, cancellationToken);

        var gatewayMode = "LOCAL_SANDBOX";
        var providerMessage = "Đã hủy vận đơn trên hệ thống nội bộ";

        if (string.Equals(adapter.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedToken = GhnLiveGatewayHelper.ResolveConfiguredGhnToken(
                null,
                config?.EncryptedApiToken,
                configuration);

            if (!string.IsNullOrWhiteSpace(resolvedToken))
            {
                var effectiveShopId = !string.IsNullOrWhiteSpace(config?.ShopId) && !config.ShopId.StartsWith("SHOP-GHN-", StringComparison.OrdinalIgnoreCase)
                    ? config.ShopId
                    : (configuration?["Carriers:GHN:ShopId"] ?? "235512");

                using var http = GhnLiveGatewayHelper.CreateHttpClient(httpClientFactory);
                var cancelResult = await GhnLiveGatewayHelper.CancelShippingOrdersAsync(
                    http,
                    resolvedToken,
                    effectiveShopId,
                    GhnLiveGatewayHelper.DevSandboxBaseUrl,
                    new[] { orderCode },
                    cancellationToken);

                if (cancelResult.Succeeded)
                {
                    gatewayMode = "LIVE_GHN_DEV_SANDBOX";
                    providerMessage = cancelResult.Message ?? "Đã hủy vận đơn trên cổng GHN Dev Sandbox";
                }
            }
        }

        var now = DateTime.UtcNow;
        var resolvedActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);

        if (exportReceipt != null && string.Equals(exportReceipt.TrackingNumber, orderCode, StringComparison.OrdinalIgnoreCase))
        {
            exportReceipt.TrackingNumber = null;
        }
        if (string.Equals(shipment.SourceCode, orderCode, StringComparison.OrdinalIgnoreCase))
        {
            shipment.SourceCode = null;
        }

        if (ledger != null)
        {
            ledger.ExpectedCodAmount = 0m;
            ledger.EstimatedShippingFee = 0m;
            ledger.CodVariance = 0m;
            ledger.ShippingFeeVariance = 0m;
            ledger.NetSettlementAmount = 0m;
            ledger.ReconciliationStatus = "VOIDED";
        }

        db.ShipmentTrackingEvents.Add(new ERP.Domain.Entities.ShipmentTrackingEvent
        {
            ShipmentId = shipment.Id,
            EventType = "WaybillCancelled",
            FromStatus = shipment.Status,
            ToStatus = shipment.Status,
            OccurredAt = now,
            RecordedAt = now,
            Source = adapter.CarrierCode,
            SourceEventId = $"CANCEL-{orderCode}-{now:HHmmss}",
            ReasonCode = request?.Reason ?? "USER_CANCELLED_WAYBILL",
            Note = $"Đã hủy vận đơn {orderCode} ({providerMessage})",
            RecordedBy = resolvedActorId
        });

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = shipment.WarehouseId,
            EntityName = "Shipment",
            EntityId = shipment.Id,
            Action = "Shipment.CancelCarrierWaybill",
            NewValues = $"Carrier={adapter.CarrierCode}; CancelledOrderCode={orderCode}; Mode={gatewayMode}",
            Reason = request?.Reason ?? $"Hủy vận đơn {orderCode} qua {adapter.CarrierDisplayName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment.Id,
            shipmentCode = shipment.ShipmentCode,
            warehouseId = shipment.WarehouseId,
            carrierCode = adapter.CarrierCode,
            carrierDisplayName = adapter.CarrierDisplayName,
            cancelledOrderCode = orderCode,
            orderCode,
            cancelled = true,
            gatewayMode,
            reconciliationStatus = ledger?.ReconciliationStatus ?? "VOIDED",
            cancelledAtUtc = now,
            statusLabel = $"Đã hủy vận đơn {orderCode} qua {adapter.CarrierDisplayName} ({gatewayMode})"
        });
    }

    private static ShipmentStatus MapCarrierRawStatusToShipmentStatus(
        string? rawStatusInput,
        ShipmentStatus currentStatus,
        out bool regressionBlocked)
    {
        regressionBlocked = false;
        var norm = (rawStatusInput ?? string.Empty).Trim().ToUpperInvariant();
        var mapped = norm switch
        {
            "READY_TO_PICK" => currentStatus is ShipmentStatus.Draft or ShipmentStatus.Staging or ShipmentStatus.Loading or ShipmentStatus.Loaded or ShipmentStatus.Ready
                ? ShipmentStatus.Ready
                : ShipmentStatus.Dispatched,
            "PICKING" or "MONEY_COLLECT_PICKING" => currentStatus is ShipmentStatus.Ready or ShipmentStatus.Staging
                ? ShipmentStatus.Staging
                : ShipmentStatus.Dispatched,
            "PICKED" or "PICKED_UP" or "DISPATCHED" => ShipmentStatus.Dispatched,
            "STORING" or "TRANSPORTING" or "SORTING" or "DELIVERING" or "MONEY_COLLECT_DELIVERING" or "IN_TRANSIT" or "INTRANSIT" => ShipmentStatus.InTransit,
            "DELIVERED" or "COMPLETED" or "SUCCESS" => ShipmentStatus.Delivered,
            "DELIVERY_FAIL" or "DELIVERY_FAILED" or "DELIVERYFAILED" or "FAILED" => ShipmentStatus.DeliveryFailed,
            "WAITING_TO_RETURN" or "RETURN" or "RETURN_TRANSPORTING" or "RETURN_SORTING" or "RETURNING" or "RETURN_FAIL" or "RETURNED" or "RETURN_TO_WAREHOUSE" or "RTS" => ShipmentStatus.ReturnToWarehouse,
            "CANCEL" or "CANCELLED" => ShipmentStatus.Cancelled,
            _ => ShipmentStatus.InTransit
        };

        // Monotonic state guard: terminal / post-delivery states cannot regress back to pre-delivery states
        if (currentStatus is ShipmentStatus.Delivered or ShipmentStatus.Completed or ShipmentStatus.ReturnToWarehouse or ShipmentStatus.Cancelled &&
            mapped is ShipmentStatus.Ready or ShipmentStatus.Staging or ShipmentStatus.Loading or ShipmentStatus.Loaded or ShipmentStatus.Dispatched or ShipmentStatus.InTransit)
        {
            regressionBlocked = true;
            return currentStatus;
        }

        return mapped;
    }
}
