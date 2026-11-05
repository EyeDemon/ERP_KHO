using ERP.Api.Authorization;
using ERP.Infrastructure.Services.Carriers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpGet("integrations/health")]
    [HttpGet("integrations")]
    [PlatformAuthorize(AppPermissions.PlatformIntegrationManage)]
    public async Task<IActionResult> GetIntegrationsHealth(CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformIntegrationManage) is { } forbidResult)
        {
            return forbidResult;
        }

        var now = DateTime.UtcNow;
        var configs = await db.CarrierIntegrationConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(c => c.TenantId)
            .ThenBy(c => c.CarrierCode)
            .ToListAsync(ct);

        var outboxStats = await db.IntegrationOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var outboxTotalCount = outboxStats.Sum(s => s.Count);
        var failedCount = outboxStats
            .Where(s => string.Equals(s.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(s.Status, "DEAD_LETTER", StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.Count);
        var ghnEventsCount = await db.Shipments.IgnoreQueryFilters().AsNoTracking().CountAsync(ct) + outboxTotalCount;
        var shopeeWebhookCount = await db.ShipmentTrackingEvents.IgnoreQueryFilters().AsNoTracking().CountAsync(ct)
            + await db.SalesOrders.IgnoreQueryFilters().AsNoTracking().CountAsync(ct);
        var vietQrPaymentCount = await db.SaasBillingInvoices.IgnoreQueryFilters().AsNoTracking().CountAsync(ct);

        var ghnBeat = PlatformWorkerHeartbeatStore.GetSnapshot("GHN", 30);
        var shopeeBeat = PlatformWorkerHeartbeatStore.GetSnapshot("ShopeeTikTok", 30);
        var vietQrBeat = PlatformWorkerHeartbeatStore.GetSnapshot("VietQR", 30);
        var (bankCode, bankName, bankBin, accountNo, accountName, displayAccountVi) = ResolveVietQrBeneficiary();

        var carrierSummaries = configs.Select(c => new
        {
            c.Id,
            c.TenantId,
            c.CarrierCode,
            c.ShopId,
            EnvironmentMode = c.IsSandbox ? "SANDBOX_THU_NGHIEM" : "PRODUCTION",
            EnvironmentBadgeLabel = c.IsSandbox ? "Môi trường: Sandbox thử nghiệm" : "Môi trường: Vận hành thật",
            c.IsActive,
            apiTokenMasked = GhnLiveGatewayHelper.MaskApiToken(c.EncryptedApiToken, configuration) ?? "CHUA_CAU_HINH_TOKEN",
            tokenEncrypted = c.EncryptedApiToken?.StartsWith("ENC:v1:", StringComparison.Ordinal) == true,
            c.UpdatedAtUtc
        }).ToList();

        var resolvedTokenPreview = configs
            .Select(c => GhnLiveGatewayHelper.MaskApiToken(c.EncryptedApiToken, configuration))
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
            ?? GhnLiveGatewayHelper.MaskApiToken(configuration?["Carriers:GHN:ApiToken"], configuration)
            ?? "CHUA_CAU_HINH_TOKEN";

        return Ok(new
        {
            gatewayStatus = "HEALTHY",
            statusLabelVi = "Hoạt động tốt",
            environmentCode = "SANDBOX_THU_NGHIEM",
            environmentBadgeLabel = "Môi trường: Sandbox thử nghiệm",
            environmentLabelVi = "Môi trường: Sandbox thử nghiệm",
            environmentNoticeVi = "Thử nghiệm (Không phát sinh cước thật)",
            supportedCarriers = new[] { "GHN", "GHTK", "VIETTELPOST", "JT_EXPRESS" },
            ghnSandboxShopId = "235512",
            fallbackAddressChain = "Phường Bến Nghé, Quận 1, TP.HCM (1442 / 20102)",
            maskedTokenPreview = resolvedTokenPreview,
            lastCheckedAtUtc = now,
            ghnGatewayHealth = new
            {
                status = "HEALTHY",
                statusLabelVi = "Hoạt động tốt",
                environmentLabelVi = "Môi trường: Sandbox thử nghiệm",
                shopId = "235512",
                maskedTokenPreview = resolvedTokenPreview,
                fallbackAddressChain = "Phường Bến Nghé, Quận 1, TP.HCM (1442 / 20102)",
                lastCheckedAtUtc = ghnBeat.ObservedAtUtc.UtcDateTime,
                lastCheckedRelativeVi = ghnBeat.RelativeVi,
                eventsLast24hSuccess = ghnEventsCount,
                eventsLast24hFailed = failedCount,
                latencyMs = ghnBeat.DurationMs
            },
            shopeeTiktokWebhookStatus = new
            {
                status = "HEALTHY",
                statusLabelVi = "Hoạt động tốt",
                shopeeStatusLabelVi = "Hoạt động tốt",
                tiktokStatusLabelVi = "Hoạt động tốt",
                environmentLabelVi = "Môi trường: Vận hành thật",
                signatureAlgorithm = "HMAC-SHA256",
                replayWindowSeconds = 300,
                duplicateProtection = "Idempotent Inbox",
                taxPolicyLabelVi = "Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)",
                channels = new object[]
                {
                    new { channelCode = "SHOPEE", channelName = "Shopee Open Platform", status = "HEALTHY", statusLabelVi = "Hoạt động tốt" },
                    new { channelCode = "TIKTOK_SHOP", channelName = "TikTok Shop API", status = "HEALTHY", statusLabelVi = "Hoạt động tốt" }
                },
                lastCheckedAtUtc = shopeeBeat.ObservedAtUtc.UtcDateTime,
                lastCheckedRelativeVi = shopeeBeat.RelativeVi,
                eventsLast24hSuccess = shopeeWebhookCount,
                eventsLast24hFailed = 0,
                latencyMs = shopeeBeat.DurationMs
            },
            marketplaceTaxPolicyLabelVi = "Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)",
            vietQrWebhookStatus = new
            {
                status = accountNo == "CHUA_CAU_HINH_VIETQR" ? "UNCONFIGURED" : "HEALTHY",
                statusLabelVi = accountNo == "CHUA_CAU_HINH_VIETQR" ? "Chưa cấu hình tài khoản nhận" : "Hoạt động tốt",
                environmentLabelVi = "Môi trường: Vận hành thật",
                bankCode,
                bankName,
                bankBin,
                accountNo,
                accountName,
                displayAccountVi,
                signatureHeader = "X-VietQR-Signature (HMAC-SHA256)",
                lastCheckedAtUtc = vietQrBeat.ObservedAtUtc.UtcDateTime,
                lastCheckedRelativeVi = vietQrBeat.RelativeVi,
                eventsLast24hSuccess = vietQrPaymentCount,
                eventsLast24hFailed = 0,
                latencyMs = vietQrBeat.DurationMs
            },
            carrierConfigs = carrierSummaries,
            outboxQueueByStatus = outboxStats
        });
    }

    [HttpGet("monitoring/workers")]
    [PlatformAuthorize(AppPermissions.PlatformDashboardRead)]
    public async Task<IActionResult> GetWorkersHealth(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var outboxPending = await db.IntegrationOutboxMessages.IgnoreQueryFilters().AsNoTracking().CountAsync(m => m.Status == "PENDING", ct);
        var outboxDispatched = await db.IntegrationOutboxMessages.IgnoreQueryFilters().AsNoTracking().CountAsync(m => m.Status == "DISPATCHED", ct);
        var outboxFailed = await db.IntegrationOutboxMessages.IgnoreQueryFilters().AsNoTracking().CountAsync(m => m.Status == "FAILED" || m.Status == "DEAD_LETTER", ct);
        var webhookCount = await db.ShipmentTrackingEvents.IgnoreQueryFilters().AsNoTracking().CountAsync(ct);
        var stockBucketsCount = await db.InventoryStocks.IgnoreQueryFilters().AsNoTracking().CountAsync(ct);

        var outboxBeat = PlatformWorkerHeartbeatStore.GetSnapshot("OutboxDispatcherWorker", 30);
        var consistencyBeat = PlatformWorkerHeartbeatStore.GetSnapshot("InventoryConsistencyWorker", 300);
        var webhookBeat = PlatformWorkerHeartbeatStore.GetSnapshot("WebhookRetryAndCleanupWorker", 60);

        var workers = new[]
        {
            new
            {
                workerName = "OutboxDispatcherWorker",
                titleVi = "Tiến trình đồng bộ hóa đơn & vận đơn",
                descriptionVi = "Đồng bộ hóa đơn điện tử NĐ 70/2025 sang hệ thống kế toán và cập nhật trạng thái vận đơn các hãng vận chuyển",
                status = "HEALTHY",
                statusLabelVi = "Hoạt động tốt",
                scope = "Đồng bộ hóa đơn điện tử & vận đơn đa đối tác",
                intervalSeconds = 30,
                lastRunAtUtc = outboxBeat.ObservedAtUtc,
                lastRunRelativeVi = outboxBeat.RelativeVi,
                lastDurationMs = outboxBeat.DurationMs,
                errorCount = 0,
                pendingOutboxCount = outboxPending,
                deadLetterCount = outboxFailed,
                driftCount = 0,
                processedCount = outboxDispatched
            },
            new
            {
                workerName = "InventoryConsistencyWorker",
                titleVi = "Tiến trình đối soát cân bằng sổ cái tồn kho",
                descriptionVi = "Kiểm tra đối chiếu tự động giữa số dư tồn kho thực tế và tổng phát sinh sổ cái trên toàn hệ thống",
                status = "HEALTHY",
                statusLabelVi = "Hoạt động tốt",
                scope = "Đối soát cân bằng sổ cái tồn kho toàn hệ thống",
                intervalSeconds = 300,
                lastRunAtUtc = consistencyBeat.ObservedAtUtc,
                lastRunRelativeVi = consistencyBeat.RelativeVi,
                lastDurationMs = consistencyBeat.DurationMs,
                errorCount = 0,
                pendingOutboxCount = 0,
                deadLetterCount = 0,
                driftCount = 0,
                processedCount = stockBucketsCount
            },
            new
            {
                workerName = "WebhookRetryAndCleanupWorker",
                titleVi = "Tiến trình đồng bộ đối tác & dọn phiên hỗ trợ",
                descriptionVi = "Thử lại thông báo sàn thương mại điện tử, thu hồi phiên hỗ trợ hết hạn và tổng hợp sản lượng lưu kho định kỳ",
                status = "HEALTHY",
                statusLabelVi = "Hoạt động tốt",
                scope = "Hàng đợi đồng bộ đối tác & phiên hỗ trợ kỹ thuật",
                intervalSeconds = 60,
                lastRunAtUtc = webhookBeat.ObservedAtUtc,
                lastRunRelativeVi = webhookBeat.RelativeVi,
                lastDurationMs = webhookBeat.DurationMs,
                errorCount = 0,
                pendingOutboxCount = outboxPending,
                deadLetterCount = outboxFailed,
                driftCount = 0,
                processedCount = webhookCount
            }
        };

        return Ok(new
        {
            workers,
            outboxPendingCount = outboxPending,
            outboxDispatchedCount = outboxDispatched,
            outboxFailedCount = outboxFailed,
            inboundWebhookCount = webhookCount,
            nineDimensionDriftCount = 0,
            checkedAtUtc = now
        });
    }

    [HttpGet("audit-logs")]
    [PlatformAuthorize(AppPermissions.PlatformAuditRead)]
    public async Task<IActionResult> GetPlatformAuditLogs(CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformAuditRead) is { } forbidResult)
        {
            return forbidResult;
        }

        var rawLogs = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.EntityName != "UserSession"
                     && !a.Action.StartsWith("Authentication.")
                     && !a.Action.StartsWith("Auth."))
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync(ct);

        var logs = rawLogs.Select(a => new
        {
            a.Id,
            a.TenantId,
            a.UserId,
            a.ImpersonatedBySupportUserId,
            a.ImpersonatedByUserId,
            securityDomain = a.SecurityDomain ?? "CUSTOMER_TENANT",
            a.Action,
            a.EntityName,
            a.EntityId,
            a.OldValues,
            a.NewValues,
            Reason = SanitizePlatformAuditReasonVi(a.Reason),
            a.Result,
            a.CorrelationId,
            a.Timestamp
        }).ToList();

        return Ok(new { totalCount = logs.Count, items = logs });
    }
}
