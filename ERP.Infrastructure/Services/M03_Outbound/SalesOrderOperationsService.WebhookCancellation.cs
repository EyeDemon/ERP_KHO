using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> WebhookBuyerCancellationAsync(
        string? channelCode,
        BuyerCancellationWebhookInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var resolvedActorId = actorUserId is > 0 ? actorUserId.Value : 1;
        var rawChannel = !string.IsNullOrWhiteSpace(channelCode)
            ? channelCode.Trim()
            : (!string.IsNullOrWhiteSpace(request?.ChannelCode) ? request.ChannelCode.Trim() : "SHOPEE");
        var adapter = _channelRegistry.ResolveAdapter(rawChannel);
        var normChannel = rawChannel.ToUpperInvariant();

        var matchedShop = await _db.MarketplaceShops
            .AsNoTracking()
            .Where(s => s.IsActive && (s.ChannelCode == normChannel || s.ChannelCode == adapter.ChannelCode))
            .OrderByDescending(s => s.IsPrimaryShop)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var configuredSecret = ResolveMarketplaceWebhookSecret(normChannel, adapter.ChannelCode, matchedShop?.WebhookSecret);
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SECRET_NOT_CONFIGURED",
                errorCode = "WEBHOOK_SECRET_NOT_CONFIGURED",
                channelCode = normChannel,
                message = "Chưa cấu hình khóa bí mật xác thực Webhook cho sàn TMĐT."
            });
        }

        if (string.IsNullOrWhiteSpace(request?.HmacSignature))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SIGNATURE_MISSING",
                errorCode = "WEBHOOK_SIGNATURE_MISSING",
                channelCode = normChannel,
                message = "Thiếu chữ ký HMAC-SHA256 xác thực Webhook sàn TMĐT."
            });
        }

        var candidatePayloads = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.RawPayload))
            candidatePayloads.Add(request.RawPayload);
        if (!string.IsNullOrWhiteSpace(request.OrderNumber))
            candidatePayloads.Add(request.OrderNumber);
        if (!string.IsNullOrWhiteSpace(request.MarketplaceOrderId))
            candidatePayloads.Add(request.MarketplaceOrderId);
        candidatePayloads.Add(JsonSerializer.Serialize(request));

        var isSignatureValid = candidatePayloads.Any(p =>
            adapter.VerifyWebhookHmacSignature(p, request.HmacSignature, configuredSecret));
        if (!isSignatureValid)
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "WEBHOOK_SIGNATURE_INVALID",
                errorCode = "WEBHOOK_SIGNATURE_INVALID",
                channelCode = normChannel,
                message = "Chữ ký HMAC-SHA256 của webhook sàn TMĐT không hợp lệ."
            });
        }

        var eventFingerprint = ComputeWebhookFingerprint(normChannel, request);
        var fingerprintMessageId = $"FP-{eventFingerprint[..32]}";
        var effectiveEventId = !string.IsNullOrWhiteSpace(request.EventId)
            ? request.EventId.Trim()
            : fingerprintMessageId;

        var inboxDuplicate = await _db.IntegrationInboxMessages
            .AsNoTracking()
            .AnyAsync(
                m => (m.SourceSystem == normChannel || m.SourceSystem == adapter.ChannelCode) &&
                     m.EventType == "ORDER_CANCELLED" &&
                     (m.ExternalMessageId == effectiveEventId || m.ExternalMessageId == fingerprintMessageId),
                cancellationToken);
        var auditDuplicate = !inboxDuplicate && await _db.AuditLogs
            .AsNoTracking()
            .AnyAsync(
                a => a.Action == "MarketplaceWebhook.Ingested" &&
                     a.NewValues != null &&
                     a.NewValues.Contains(eventFingerprint),
                cancellationToken);

        if (inboxDuplicate || auditDuplicate)
        {
            return WmsEndpointOutcome.Ok(new
            {
                idempotentReplay = true,
                code = "WEBHOOK_EVENT_REPLAY_DETECTED",
                eventId = effectiveEventId,
                eventFingerprint,
                channelCode = normChannel,
                statusLabel = "Sự kiện Webhook sàn TMĐT trùng lặp (Replay) đã được xử lý trước đó."
            });
        }

        var (order, receipt) = await ResolveWebhookOrderAndReceiptAsync(request, cancellationToken);
        if (order == null && receipt == null)
        {
            return WmsEndpointOutcome.Json(404, new
            {
                code = "NOT_FOUND",
                message = "Không tìm thấy đơn bán hàng hoặc phiếu xuất tương ứng với yêu cầu hủy từ sàn."
            });
        }

        var targetWarehouseId = order?.WarehouseId ?? receipt!.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var inboxPayloadJson = !string.IsNullOrWhiteSpace(request.RawPayload)
            ? request.RawPayload
            : JsonSerializer.Serialize(new
            {
                channelCode = normChannel,
                eventId = effectiveEventId,
                eventFingerprint,
                orderId = order?.Id,
                exportReceiptId = receipt?.Id,
                request.OrderNumber,
                request.MarketplaceOrderId,
                request.BuyerReason,
                receivedAtUtc = now
            });

        _db.IntegrationInboxMessages.Add(new IntegrationInboxMessage
        {
            SourceSystem = normChannel,
            ExternalMessageId = effectiveEventId,
            EventType = "ORDER_CANCELLED",
            PayloadJson = inboxPayloadJson,
            Status = "Processed",
            ReceivedAt = now,
            ProcessedAt = now
        });

        var auditUserId = await ResolveValidAuditUserIdAsync(resolvedActorId, cancellationToken);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = auditUserId,
            WarehouseId = targetWarehouseId,
            Action = "MarketplaceWebhook.Ingested",
            EntityName = order != null ? "SalesOrder" : "ExportReceipt",
            EntityId = order?.Id ?? receipt!.Id,
            NewValues = $"Channel={normChannel};EventId={effectiveEventId};Fingerprint={eventFingerprint}",
            Reason = "Ghi nhận sự kiện Webhook hủy đơn sàn TMĐT chống phát lại (Anti-Replay)",
            Result = "Success",
            Timestamp = now
        });

        var isSealedInManifest = receipt != null && await _db.HandoverManifestLines
            .Include(l => l.Manifest)
            .AnyAsync(l => l.ExportReceiptId == receipt.Id && l.Manifest != null && l.Manifest.Status == "SEALED", cancellationToken);

        var isAlreadyDispatched =
            (order != null && (order.Status == SalesOrderStatus.Fulfilled ||
                               order.Status.ToString() is "Dispatched" or "Delivered" or "Completed")) ||
            (receipt != null && receipt.Status == ReceiptStatus.Dispatched) ||
            isSealedInManifest;

        if (isAlreadyDispatched)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return WmsEndpointOutcome.Ok(new
            {
                decision = "REJECT",
                accepted = false,
                requiresPutback = false,
                idempotentReplay = false,
                eventId = effectiveEventId,
                reasonCode = "ALREADY_DISPATCHED_TO_CARRIER",
                orderId = order?.Id,
                exportReceiptId = receipt?.Id,
                orderStatus = order?.Status.ToString() ?? receipt?.Status.ToString(),
                statusLabel = "Từ chối yêu cầu hủy từ sàn — Kiện hàng đã bàn giao bưu tá / xuất kho"
            });
        }

        var allocations = await LoadActiveOrderAndReceiptAllocationsAsync(order, receipt, cancellationToken);
        var hasPickedOrPackedWork =
            allocations.Any(a => a.Status == StockAllocationStatus.Picked) ||
            (receipt != null && receipt.AwbPrintCount > 0);

        if (hasPickedOrPackedWork)
        {
            if (order != null)
            {
                order.Status = SalesOrderStatus.CancelPendingPutback;
                order.CancellationReason = request.BuyerReason ?? "Khách hủy trên sàn sau khi kho đã nhặt hàng/in vận đơn — chờ cất trả kệ";
            }
            if (receipt != null)
            {
                receipt.Status = ReceiptStatus.CancelPendingPutback;
            }

            var putbackTaskId = await CreateWebhookPutbackTaskAsync(
                order,
                receipt,
                allocations,
                targetWarehouseId,
                auditUserId ?? resolvedActorId,
                now,
                cancellationToken);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = auditUserId,
                WarehouseId = targetWarehouseId,
                Action = "SalesOrder.WebhookBuyerCancellation.PendingPutback",
                EntityName = order != null ? "SalesOrder" : "ExportReceipt",
                EntityId = order?.Id ?? receipt!.Id,
                NewValues = "CancelPendingPutback",
                Reason = request.BuyerReason ?? "Chấp nhận hủy từ sàn — khóa xuất bàn giao & sinh tác vụ cất trả hàng (Putback)",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            return WmsEndpointOutcome.Ok(new
            {
                decision = "ACCEPT_PENDING_PUTBACK",
                accepted = true,
                requiresPutback = true,
                idempotentReplay = false,
                eventId = effectiveEventId,
                orderId = order?.Id,
                exportReceiptId = receipt?.Id,
                orderStatus = "CancelPendingPutback",
                putbackTaskId,
                statusLabel = "Chấp nhận hủy đơn — Kiện đã nhặt/đóng gói được chuyển sang CancelPendingPutback & sinh tác vụ cất trả kệ"
            });
        }

        await ReleaseWebhookReservationsAndAllocationsAsync(
            order,
            receipt,
            allocations,
            resolvedActorId,
            request.BuyerReason,
            now,
            cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = auditUserId,
            WarehouseId = targetWarehouseId,
            Action = "SalesOrder.WebhookBuyerCancellation.Accepted",
            EntityName = order != null ? "SalesOrder" : "ExportReceipt",
            EntityId = order?.Id ?? receipt!.Id,
            NewValues = "Cancelled",
            Reason = request.BuyerReason ?? "Chấp nhận hủy đơn chưa nhặt từ sàn & hoàn trả ngay tồn khả dụng ATP",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return WmsEndpointOutcome.Ok(new
        {
            decision = "ACCEPT",
            accepted = true,
            requiresPutback = false,
            idempotentReplay = false,
            eventId = effectiveEventId,
            orderId = order?.Id,
            exportReceiptId = receipt?.Id,
            orderStatus = "Cancelled",
            statusLabel = "Chấp nhận hủy đơn — Đã giải phóng ngay tồn kho giữ chỗ (Reserved/Allocated) về ATP"
        });
    }
}
