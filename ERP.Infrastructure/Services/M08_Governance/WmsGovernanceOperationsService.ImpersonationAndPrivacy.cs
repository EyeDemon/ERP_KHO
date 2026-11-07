using System.Security.Cryptography;
using System.Text;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    public async Task<WmsEndpointOutcome> GrantSupportImpersonationAsync(
        string? supportTicketCode,
        int? expiresInMinutes,
        bool readOnlyScope,
        int? supportAgentUserId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiresMinutes = Math.Clamp(expiresInMinutes ?? 30, 5, 60);
        var expiresAtUtc = now.AddMinutes(expiresMinutes);
        var ticketCode = string.IsNullOrWhiteSpace(supportTicketCode) ? $"SUP-{now:yyyyMMdd}-01" : supportTicketCode.Trim();
        var supportToken = $"IMP-{Guid.NewGuid():N}".ToUpperInvariant();

        var session = new SupportImpersonationSession
        {
            TenantId = db.CurrentTenantId,
            SessionCode = $"IMP-SES-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32].ToUpperInvariant(),
            SupportTicketCode = ticketCode,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(supportToken))),
            GrantedByUserId = actorUserId ?? 1,
            SupportAgentUserId = supportAgentUserId ?? (actorUserId ?? 1),
            ReadOnlyScope = readOnlyScope,
            GrantedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            Status = "ACTIVE"
        };
        db.SupportImpersonationSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        int? sessionId = session.Id;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            ImpersonatedBySupportUserId = supportAgentUserId ?? actorUserId,
            EntityName = "SupportImpersonationConsent",
            EntityId = sessionId ?? db.CurrentTenantId,
            Action = "AccountSecurity.GrantSupportImpersonation",
            NewValues = $"Ticket={ticketCode}; ExpiresAtUtc={expiresAtUtc:O}; ReadOnly={readOnlyScope}",
            Reason = $"Chủ shop cấp quyền hỗ trợ kỹ thuật tạm thời ({expiresMinutes} phút) cho mã yêu cầu {ticketCode}",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            sessionId,
            supportTicketCode = ticketCode,
            supportImpersonationToken = supportToken,
            readOnlyScope,
            expiresInMinutes = expiresMinutes,
            grantedAtUtc = now,
            expiresAtUtc,
            statusLabel = $"Đã cấp quyền hỗ trợ kỹ thuật có thời hạn ({expiresMinutes} phút) kèm nhật ký AuditLog bất biến"
        });
    }

    private static string MaskSessionCode(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? "IMP-****"
            : $"IMP-****{(raw.Length >= 4 ? raw[^4..] : raw)}";

    public async Task<WmsEndpointOutcome> ListSupportImpersonationSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await db.SupportImpersonationSessions
            .AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var redacted = sessions.Select(s =>
        {
            var masked = MaskSessionCode(s.SessionCode);
            return new
            {
                id = s.Id,
                sessionCode = masked,
                maskedSessionCode = masked,
                tenantId = s.TenantId,
                grantedByUserId = s.GrantedByUserId,
                supportAgentUserId = s.SupportAgentUserId,
                impersonatorRole = "SupportAdmin",
                supportTicketCode = s.SupportTicketCode,
                readOnlyScope = s.ReadOnlyScope,
                status = s.Status,
                grantedAtUtc = s.GrantedAtUtc,
                expiresAtUtc = s.ExpiresAtUtc,
                revokedAtUtc = s.RevokedAtUtc
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(redacted);
    }

    public async Task<WmsEndpointOutcome> RevokeSupportImpersonationAsync(
        int sessionId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var session = await db.SupportImpersonationSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session == null)
            return WmsEndpointOutcome.NotFound(new { message = "Không tìm thấy phiên hỗ trợ kỹ thuật." });

        session.Status = "REVOKED";
        session.RevokedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            ImpersonatedBySupportUserId = session.SupportAgentUserId,
            EntityName = "SupportImpersonationConsent",
            EntityId = session.Id,
            Action = "AccountSecurity.RevokeSupportImpersonation",
            NewValues = $"SessionId={session.Id}; Status=REVOKED",
            Reason = $"Thu hồi quyền hỗ trợ kỹ thuật tạm thời cho phiếu {session.SupportTicketCode}",
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            sessionId = session.Id,
            supportTicketCode = session.SupportTicketCode,
            status = session.Status,
            revokedAtUtc = session.RevokedAtUtc,
            statusLabel = $"Đã thu hồi phiên hỗ trợ kỹ thuật #{session.Id} ({session.SupportTicketCode})"
        });
    }

    public async Task<WmsEndpointOutcome> AnonymizeExpiredBuyerPiiAsync(
        int? retentionDays,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        // Điều 41 Luật Kế toán 88/2015/QH13: Chứng từ kế toán (Phiếu xuất kho, hóa đơn, đơn bán hàng đã hạch toán)
        // bắt buộc lưu trữ tối thiểu 10 năm (3.650 ngày). Khi hết hạn lưu trữ PII vận chuyển ngắn hạn (>= 90 ngày)
        // theo Nghị định 13/2023/NĐ-CP nhưng chưa đủ 10 năm, chỉ ẩn danh hóa thông tin liên hệ giao nhận phụ trợ (Note/Phone),
        // tuyệt đối giữ nguyên định danh kế toán (CustomerNameSnapshot, TaxCode, số tiền hạch toán).
        const int StatutoryAccountingRetentionDays = 3650;
        var now = DateTime.UtcNow;
        var effectiveRetentionDays = Math.Max(1, retentionDays ?? 90);
        var cutoffUtc = now.AddDays(-effectiveRetentionDays);
        var accountingArchiveCutoffUtc = now.AddDays(-StatutoryAccountingRetentionDays);
        var anonymizedCount = 0;
        var anonymizedReceiptsCount = 0;
        var preservedAccountingReceiptsCount = 0;
        var anonymizedOrdersCount = 0;
        var anonymizedReturnsCount = 0;

        var expiredReceipts = await db.ExportReceipts
            .Where(r => r.CreatedAt <= cutoffUtc &&
                        (r.Status == ReceiptStatus.Dispatched || r.Status == ReceiptStatus.Cancelled) &&
                        r.CustomerNameSnapshot != null &&
                        !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_") &&
                        (r.Note == null || !r.Note.Contains("[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]")))
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var r in expiredReceipts)
        {
            if (r.CreatedAt <= accountingArchiveCutoffUtc)
            {
                r.CustomerNameSnapshot = $"ANONYMIZED_PII_{r.Id}";
                r.Note = "[PII_REDACTED_DECREE_13_2023_AFTER_10Y_STATUTORY_RETENTION]";
            }
            else
            {
                // Preserve CustomerNameSnapshot under Điều 41 Luật Kế toán 88/2015/QH13 (10-year retention)
                r.Note = "[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]";
                preservedAccountingReceiptsCount++;
            }
            anonymizedReceiptsCount++;
            anonymizedCount++;
        }

        var expiredOrders = await db.SalesOrders
            .Include(o => o.Customer)
            .Where(o => o.CreatedAt <= cutoffUtc &&
                        (o.Status == SalesOrderStatus.Fulfilled ||
                         o.Status == SalesOrderStatus.Cancelled) &&
                        (o.CancellationReason == null ||
                         (!o.CancellationReason.StartsWith("ANONYMIZED_PII_") &&
                          !o.CancellationReason.Contains("[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]"))))
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var o in expiredOrders)
        {
            if (o.CreatedAt <= accountingArchiveCutoffUtc)
            {
                o.CancellationReason = $"ANONYMIZED_PII_{o.Id}";
                if (o.Customer != null && !o.Customer.Name.StartsWith("ANONYMIZED_PII_"))
                {
                    o.Customer.Name = $"ANONYMIZED_PII_PARTNER_{o.Customer.Id}";
                    o.Customer.Phone = "***REDACTED***";
                    o.Customer.Email = null;
                    o.Customer.Address = "[PII_REDACTED_DECREE_13_2023]";
                }
            }
            else
            {
                o.CancellationReason = "[PII_CONTACT_REDACTED_ND13_KEEP_ACCOUNTING_10Y]";
                if (o.Customer != null)
                {
                    // Preserve Customer.Name and Customer.TaxCode for 10-year statutory accounting audit
                    o.Customer.Phone = "***REDACTED***";
                    o.Customer.Email = null;
                }
            }
            anonymizedOrdersCount++;
            anonymizedCount++;
        }

        var expiredReturns = await db.CustomerReturns
            .Where(cr => cr.CreatedAtUtc <= cutoffUtc &&
                         cr.ReturnReason != null &&
                         !cr.ReturnReason.StartsWith("[PII_REDACTED"))
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var cr in expiredReturns)
        {
            cr.ReturnReason = $"[PII_REDACTED_DECREE_13_2023]_{cr.Id}";
            anonymizedReturnsCount++;
            anonymizedCount++;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "BuyerPiiRetentionPolicy",
            EntityId = db.CurrentTenantId,
            Action = "AccountSecurity.AnonymizeExpiredBuyerPii",
            NewValues = $"RetentionDays={effectiveRetentionDays}; StatutoryAccountingRetentionDays={StatutoryAccountingRetentionDays}; AnonymizedTotal={anonymizedCount}; PreservedAccountingReceipts={preservedAccountingReceiptsCount}; Receipts={anonymizedReceiptsCount}; Orders={anonymizedOrdersCount}; Returns={anonymizedReturnsCount}",
            Reason = $"Ẩn danh hóa dữ liệu liên hệ giao nhận quá hạn {effectiveRetentionDays} ngày theo NĐ 13/2023/NĐ-CP và bảo toàn chứng từ kế toán 10 năm theo Điều 41 Luật Kế toán 88/2015/QH13",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            retentionDays = effectiveRetentionDays,
            statutoryAccountingRetentionDays = StatutoryAccountingRetentionDays,
            preservedAccountingReceiptsCount,
            cutoffUtc,
            anonymizedCount,
            anonymizedReceiptsCount,
            anonymizedOrdersCount,
            anonymizedReturnsCount,
            complianceStandard = "Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân",
            ledgerIntegrityPreserved = true,
            executedAtUtc = now,
            statusLabel = $"Đã ẩn danh hóa PII người mua quá hạn {effectiveRetentionDays} ngày ({anonymizedCount} chứng từ) theo Nghị định 13/2023/NĐ-CP"
        });
    }
}
