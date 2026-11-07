using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> IngestCarrierWebhookAsync(
        string carrierCode,
        CarrierWebhookEventInput? request,
        string? rawBody = null,
        string? signatureHeader = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normCarrier = VietnamCarrierIntegrationRegistry.NormalizeCarrierCode(carrierCode);
        var adapter = _carrierRegistry.ResolveAdapter(normCarrier);

        var config = await db.CarrierIntegrationConfigs.AsNoTracking()
            .Where(c => c.CarrierCode == normCarrier && c.IsActive && c.WebhookSecretHash != null && c.WebhookSecretHash != "")
            .OrderByDescending(c => c.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var configuredDbSecret = !string.IsNullOrWhiteSpace(config?.WebhookSecretHash)
            ? config.WebhookSecretHash.Trim()
            : null;

        var secret = configuration?[$"Webhooks:Carriers:{normCarrier}:Secret"]
            ?? configuration?[$"Carriers:{normCarrier}:WebhookSecret"]
            ?? configuration?[$"Carriers:{normCarrier}:WebhookHmacSecret"]
            ?? configuration?["Webhooks:Carriers:DefaultSecret"]
            ?? Environment.GetEnvironmentVariable("ERP_CARRIER_WEBHOOK_SECRET")
            ?? configuredDbSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SECRET_NOT_CONFIGURED",
                errorCode = "WEBHOOK_SECRET_NOT_CONFIGURED",
                message = "Chưa cấu hình khóa bí mật xác thực Webhook cho hãng vận chuyển."
            });
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SIGNATURE_MISSING",
                errorCode = "WEBHOOK_SIGNATURE_MISSING",
                message = "Thiếu chữ ký HMAC xác thực Webhook (X-Hub-Signature-256 / X-Carrier-Signature)."
            });
        }

        var candidatePayloads = new List<string>();
        if (!string.IsNullOrEmpty(rawBody))
            candidatePayloads.Add(rawBody);
        if (request != null)
        {
            candidatePayloads.Add(JsonSerializer.Serialize(request));
            candidatePayloads.Add(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        if (candidatePayloads.Count == 0)
            candidatePayloads.Add(string.Empty);

        var trimmedSecret = secret.Trim();
        var signatureValid = candidatePayloads.Any(p => adapter.VerifyWebhookHmacSignature(p, signatureHeader, trimmedSecret));
        if (!signatureValid)
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SIGNATURE_INVALID",
                errorCode = "WEBHOOK_SIGNATURE_INVALID",
                message = "Chữ ký HMAC Webhook không hợp lệ."
            });
        }

        if (request == null)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CARRIER_WEBHOOK_PAYLOAD_REQUIRED",
                errorCode = "CARRIER_WEBHOOK_PAYLOAD_REQUIRED",
                message = "Dữ liệu sự kiện webhook vận chuyển không được để trống."
            });
        }

        var resolvedStatusInput = !string.IsNullOrWhiteSpace(request.CarrierStatus)
            ? request.CarrierStatus
            : (!string.IsNullOrWhiteSpace(request.Status) ? request.Status : request.EventType);
        var rawStatus = string.IsNullOrWhiteSpace(resolvedStatusInput)
            ? "IN_TRANSIT"
            : resolvedStatusInput.Trim().ToUpperInvariant();

        var payloadForFingerprint = !string.IsNullOrWhiteSpace(rawBody)
            ? rawBody.Trim()
            : JsonSerializer.Serialize(request);
        var fingerprintInput = $"{normCarrier}|{request.TrackingNumber ?? string.Empty}|{rawStatus}|{request.EventId ?? string.Empty}|{payloadForFingerprint}";
        var eventFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();
        var fingerprintExternalId = $"FP-{eventFingerprint[..32]}";

        var eventId = !string.IsNullOrWhiteSpace(request.EventId)
            ? request.EventId.Trim()
            : fingerprintExternalId;
        var sourceSystem = $"CARRIER_{normCarrier}";

        var isReplayInInbox = await db.IntegrationInboxMessages.AsNoTracking()
            .AnyAsync(
                m => m.SourceSystem == sourceSystem &&
                     (m.ExternalMessageId == eventId || m.ExternalMessageId == fingerprintExternalId),
                cancellationToken);
        var isReplayInAudit = !isReplayInInbox && await db.AuditLogs.AsNoTracking()
            .AnyAsync(
                a => a.Action == "CarrierWebhook.Ingested" &&
                     a.NewValues != null &&
                     a.NewValues.Contains(eventFingerprint),
                cancellationToken);

        if (isReplayInInbox || isReplayInAudit)
        {
            return WmsEndpointOutcome.Ok(new
            {
                idempotentReplay = true,
                code = "WEBHOOK_EVENT_REPLAY_DETECTED",
                eventId,
                eventFingerprint,
                carrierCode = normCarrier,
                trackingNumber = request.TrackingNumber,
                statusLabel = "Sự kiện Webhook trùng lặp (Replay) đã được xử lý trước đó."
            });
        }

        var now = DateTime.UtcNow;
        await using var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var ledger = await db.ShipmentFinancialLedgers.FirstOrDefaultAsync(
            l => (!string.IsNullOrWhiteSpace(request.TrackingNumber) && l.TrackingNumber == request.TrackingNumber)
                 || (request.ShipmentId.HasValue && l.ShipmentId == request.ShipmentId.Value)
                 || (request.ExportReceiptId.HasValue && l.ExportReceiptId == request.ExportReceiptId.Value),
            cancellationToken);

        var exportReceiptId = request.ExportReceiptId ?? ledger?.ExportReceiptId;
        ERP.Domain.Entities.ExportReceipt? exportReceipt = null;
        if (exportReceiptId is > 0)
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.Id == exportReceiptId.Value, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
            exportReceipt = await db.ExportReceipts.FirstOrDefaultAsync(e => e.TrackingNumber == request.TrackingNumber, cancellationToken);

        var shipmentId = request.ShipmentId ?? ledger?.ShipmentId ?? exportReceipt?.ShipmentId;
        ERP.Domain.Entities.Shipment? shipment = null;
        if (shipmentId is > 0)
        {
            shipment = await db.Shipments
                .Include(s => s.ProofOfDelivery)
                .FirstOrDefaultAsync(s => s.Id == shipmentId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            shipment = await db.Shipments
                .Include(s => s.ProofOfDelivery)
                .FirstOrDefaultAsync(s => s.SourceCode == request.TrackingNumber, cancellationToken);
        }

        var inboxMsg = new ERP.Domain.Entities.IntegrationInboxMessage
        {
            SourceSystem = sourceSystem,
            ExternalMessageId = eventId,
            EventType = $"CARRIER_STATUS_{rawStatus}",
            PayloadJson = payloadForFingerprint,
            Status = "Processed",
            ReceivedAt = now,
            ProcessedAt = now
        };
        db.IntegrationInboxMessages.Add(inboxMsg);

        var isRtsOrReturnStatus = rawStatus is "RETURN" or "RETURNED" or "RETURN_TO_WAREHOUSE" or "WAITING_TO_RETURN" or "RETURN_TRANSPORTING" or "RETURN_SORTING" or "RETURNING" or "RTS";
        var targetStatus = rawStatus switch
        {
            "PICKED" or "PICKED_UP" or "DISPATCHED" or "READY_TO_PICK" => ShipmentStatus.Dispatched,
            "IN_TRANSIT" or "INTRANSIT" or "TRANSPORTING" or "SORTING" or "DELIVERING" or "STORING" => ShipmentStatus.InTransit,
            "DELIVERED" or "COMPLETED" or "SUCCESS" => ShipmentStatus.Delivered,
            "RETURN" or "RETURNED" or "RETURN_TO_WAREHOUSE" or "WAITING_TO_RETURN" or "RETURN_TRANSPORTING" or "RETURN_SORTING" or "RETURNING" or "RTS" => ShipmentStatus.ReturnToWarehouse,
            "DELIVERY_FAILED" or "DELIVERYFAILED" or "DELIVERY_FAIL" or "FAILED" => ShipmentStatus.DeliveryFailed,
            _ => ShipmentStatus.InTransit
        };

        var nullableActorId = await ResolveNullableUserIdAsync(actorUserId, cancellationToken);
        ERP.Domain.Entities.ShipmentProofOfDelivery? epod = null;
        ERP.Domain.Entities.CustomerReturn? autoRtsReturn = null;
        var stateRegressionBlocked = false;

        if (shipment != null)
        {
            var prevStatus = shipment.Status;
            if (prevStatus is ShipmentStatus.Delivered or ShipmentStatus.Completed or ShipmentStatus.ReturnToWarehouse or ShipmentStatus.Cancelled &&
                targetStatus is ShipmentStatus.Dispatched or ShipmentStatus.InTransit)
            {
                stateRegressionBlocked = true;
                targetStatus = prevStatus;
            }
            else
            {
                shipment.Status = targetStatus;
                if (targetStatus == ShipmentStatus.Dispatched)
                    shipment.DispatchedAt ??= now;
                else if (targetStatus == ShipmentStatus.InTransit)
                    shipment.InTransitAt ??= now;
                else if (targetStatus == ShipmentStatus.Delivered)
                    shipment.CompletedAt ??= now;
                else if (targetStatus is ShipmentStatus.DeliveryFailed or ShipmentStatus.ReturnToWarehouse)
                    shipment.DeliveryFailedAt ??= now;
            }

            db.ShipmentTrackingEvents.Add(new ERP.Domain.Entities.ShipmentTrackingEvent
            {
                ShipmentId = shipment.Id,
                EventType = $"CarrierWebhook.{normCarrier}.{rawStatus}",
                FromStatus = prevStatus,
                ToStatus = shipment.Status,
                OccurredAt = now,
                RecordedAt = now,
                Source = normCarrier,
                SourceEventId = eventId,
                ReasonCode = request.FailureReason,
                Note = request.PodNote ?? request.FailureReason ?? $"Cập nhật trạng thái vận đơn từ webhook {normCarrier}: {rawStatus}",
                RecordedBy = nullableActorId
            });

            if (targetStatus == ShipmentStatus.Delivered && !stateRegressionBlocked)
            {
                var epodCreatorId = await ResolveValidUserIdAsync(actorUserId ?? shipment.CreatedBy, cancellationToken);
                epod = shipment.ProofOfDelivery;
                if (epod == null)
                {
                    epod = new ERP.Domain.Entities.ShipmentProofOfDelivery
                    {
                        ShipmentId = shipment.Id,
                        DeliveredAt = now,
                        ReceiverName = !string.IsNullOrWhiteSpace(request.PodRecipientName) ? request.PodRecipientName.Trim() : "Người nhận ký nhận (e-POD)",
                        EvidenceReference = request.PodPhotoUrl ?? $"EPOD-{normCarrier}-{request.TrackingNumber ?? shipment.ShipmentCode}",
                        CarrierReference = request.TrackingNumber ?? shipment.ShipmentCode,
                        DeliveryNote = request.PodNote ?? $"Giao hàng thành công qua {adapter.CarrierDisplayName}",
                        CreatedAt = now,
                        CreatedBy = epodCreatorId
                    };
                    db.ShipmentProofOfDeliveries.Add(epod);
                }
                else
                {
                    epod.DeliveredAt = now;
                    if (!string.IsNullOrWhiteSpace(request.PodRecipientName))
                        epod.ReceiverName = request.PodRecipientName.Trim();
                    if (!string.IsNullOrWhiteSpace(request.PodPhotoUrl))
                        epod.EvidenceReference = request.PodPhotoUrl.Trim();
                    if (!string.IsNullOrWhiteSpace(request.PodNote))
                        epod.DeliveryNote = request.PodNote.Trim();
                }
            }
            else if (isRtsOrReturnStatus || targetStatus == ShipmentStatus.DeliveryFailed)
            {
                var trackingNo = request.TrackingNumber ?? ledger?.TrackingNumber ?? exportReceipt?.TrackingNumber ?? shipment.SourceCode ?? shipment.ShipmentCode;
                autoRtsReturn = await db.CustomerReturns.FirstOrDefaultAsync(
                    r => r.ShipmentId == shipment.Id || (!string.IsNullOrWhiteSpace(trackingNo) && r.TrackingNumber == trackingNo),
                    cancellationToken);

                if (autoRtsReturn == null)
                {
                    var rtsCreatorId = await ResolveValidUserIdAsync(actorUserId ?? shipment.CreatedBy, cancellationToken);
                    var quarantineLocationId = await db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == shipment.WarehouseId && l.IsActive)
                        .OrderByDescending(l => l.LocationType == WarehouseLocationType.Receiving)
                        .ThenBy(l => l.Id)
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    autoRtsReturn = new ERP.Domain.Entities.CustomerReturn
                    {
                        ReturnCode = $"RMA-RTS-{shipment.Id}-{now:yyyyMMddHHmmss}",
                        WarehouseId = shipment.WarehouseId,
                        ShipmentId = shipment.Id,
                        ExportReceiptId = exportReceipt?.Id,
                        SalesOrderId = exportReceipt?.SalesOrderId,
                        QuarantineLocationId = quarantineLocationId,
                        TrackingNumber = trackingNo,
                        OrderNumber = exportReceipt?.Code ?? shipment.ShipmentCode,
                        ReturnReason = request.FailureReason ?? $"CARRIER_RTS_{rawStatus}",
                        Status = "QUARANTINED",
                        CreatedBy = rtsCreatorId,
                        CreatedAtUtc = now
                    };
                    db.CustomerReturns.Add(autoRtsReturn);
                }
            }
        }

        if (ledger == null && (shipment != null || exportReceipt != null || !string.IsNullOrWhiteSpace(request.TrackingNumber)))
        {
            ledger = new ERP.Domain.Entities.ShipmentFinancialLedger
            {
                ShipmentId = shipment?.Id,
                ExportReceiptId = exportReceipt?.Id,
                CarrierCode = normCarrier,
                TrackingNumber = request.TrackingNumber ?? exportReceipt?.TrackingNumber ?? shipment?.ShipmentCode ?? eventId,
                ExpectedCodAmount = isRtsOrReturnStatus ? 0m : Math.Round(request.ActualCodCollected ?? 0m, 4),
                EstimatedShippingFee = Math.Round(request.ActualShippingFee ?? 22000m, 4),
                CreatedAtUtc = now
            };
            db.ShipmentFinancialLedgers.Add(ledger);
        }

        if (ledger != null)
        {
            if (isRtsOrReturnStatus)
            {
                ledger.ExpectedCodAmount = 0m;
                ledger.ActualCodCollected = 0m;
            }
            else if (request.ActualCodCollected.HasValue)
            {
                ledger.ActualCodCollected = Math.Round(request.ActualCodCollected.Value, 4);
            }

            if (request.ActualShippingFee.HasValue)
                ledger.ActualShippingFee = Math.Round(request.ActualShippingFee.Value, 4);

            ledger.CodVariance = Math.Round(ledger.ActualCodCollected - ledger.ExpectedCodAmount, 4);
            ledger.ShippingFeeVariance = Math.Round(ledger.ActualShippingFee - ledger.EstimatedShippingFee, 4);
            ledger.NetSettlementAmount = Math.Round(
                ledger.ActualCodCollected - ledger.ActualShippingFee - ledger.PlatformCommissionFee - ledger.SellerVoucherAmount - ledger.ReturnFee,
                4);

            if (request.ActualCodCollected.HasValue || request.ActualShippingFee.HasValue || isRtsOrReturnStatus)
            {
                ledger.ReconciliationStatus = ledger.CodVariance == 0m && ledger.ShippingFeeVariance <= 0m
                    ? "MATCHED"
                    : "DISCREPANCY";
                if (targetStatus == ShipmentStatus.Delivered && ledger.ReconciliationStatus == "MATCHED")
                    ledger.SettledAtUtc ??= now;
            }
        }

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = nullableActorId,
            WarehouseId = shipment?.WarehouseId ?? exportReceipt?.WarehouseId,
            EntityName = "CarrierWebhook",
            EntityId = shipment?.Id ?? exportReceipt?.Id,
            Action = "CarrierWebhook.Ingested",
            NewValues = $"Carrier={normCarrier}; EventId={eventId}; Status={rawStatus}; Fingerprint={eventFingerprint}",
            Reason = $"Xác thực & tiếp nhận sự kiện Webhook {adapter.CarrierDisplayName} ({rawStatus})",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
            await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            idempotentReplay = false,
            inboxMessageId = inboxMsg.Id,
            sourceSystem,
            eventId,
            eventFingerprint,
            carrierCode = normCarrier,
            carrierDisplayName = adapter.CarrierDisplayName,
            trackingNumber = request.TrackingNumber ?? ledger?.TrackingNumber ?? exportReceipt?.TrackingNumber,
            shipmentId = shipment?.Id,
            exportReceiptId = exportReceipt?.Id,
            shipmentStatus = shipment?.Status.ToString() ?? targetStatus.ToString(),
            stateRegressionBlocked,
            epodCreated = epod != null,
            rtsReady = targetStatus is ShipmentStatus.DeliveryFailed or ShipmentStatus.ReturnToWarehouse,
            autoCreatedReturnId = autoRtsReturn?.Id,
            autoCreatedReturnCode = autoRtsReturn?.ReturnCode,
            financialLedgerId = ledger?.Id,
            expectedCodAmount = ledger?.ExpectedCodAmount ?? 0m,
            actualCodCollected = ledger?.ActualCodCollected ?? 0m,
            actualShippingFee = ledger?.ActualShippingFee ?? 0m,
            codVariance = ledger?.CodVariance ?? 0m,
            shippingFeeVariance = ledger?.ShippingFeeVariance ?? 0m,
            reconciliationStatus = ledger?.ReconciliationStatus ?? "PENDING",
            statusLabel = $"Đã xử lý sự kiện webhook {rawStatus} từ {adapter.CarrierDisplayName}"
        });
    }
}
