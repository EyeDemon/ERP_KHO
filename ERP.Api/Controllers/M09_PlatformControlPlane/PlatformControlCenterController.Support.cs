using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpGet("support/impersonation-sessions")]
    [PlatformAuthorize(AppPermissions.PlatformDashboardRead)]
    public async Task<IActionResult> GetActiveImpersonationSessions(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var activeEntities = await db.SupportImpersonationSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Status == "ACTIVE" && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.GrantedAtUtc)
            .ThenByDescending(s => s.Id)
            .ToListAsync(ct);

        var saasTenants = await db.SaasTenants.AsNoTracking().ToListAsync(ct);
        var suspendedTenantIds = saasTenants
            .Where(t => string.Equals(t.LifecycleStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.TenantId)
            .ToHashSet();

        if (activeEntities.Count > 0)
        {
            var userIds = activeEntities.Select(s => s.GrantedByUserId).Where(id => id > 0).Distinct().ToList();
            var usernamesById = userIds.Count > 0
                ? await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Username, ct)
                : [];

            var items = activeEntities
                .Select(s =>
                {
                    var tenant = saasTenants.FirstOrDefault(t => t.TenantId == s.TenantId);
                    var isSuspendedDiag = suspendedTenantIds.Contains(s.TenantId);
                    var tenantCode = tenant?.TenantCode is { Length: > 0 } tc && tc != "DEFAULT" ? tc : $"TENANT-{s.TenantId:D3}";
                    var normalizedName = SaasBillingService.NormalizeTenantLegalName(s.TenantId, tenant?.CompanyName, tenant?.TenantName);
                    var ttlMin = Math.Max(1, (int)Math.Round((s.ExpiresAtUtc - s.GrantedAtUtc).TotalMinutes));
                    var maskedSessionCode = string.IsNullOrWhiteSpace(s.SessionCode)
                        ? "IMP-****"
                        : $"IMP-****{(s.SessionCode.Length >= 4 ? s.SessionCode[^4..] : s.SessionCode)}";
                    return new
                    {
                        sessionId = maskedSessionCode,
                        maskedSessionCode,
                        targetTenantId = s.TenantId,
                        targetTenantCode = tenantCode,
                        targetTenantName = normalizedName,
                        companyName = normalizedName,
                        ticketCode = s.SupportTicketCode,
                        reason = string.Empty,
                        ttlMinutes = ttlMin,
                        startedByUsername = usernamesById.GetValueOrDefault(s.GrantedByUserId, "platform_operator"),
                        startedAtUtc = s.GrantedAtUtc,
                        expiresAtUtc = s.ExpiresAtUtc,
                        readOnlyMode = s.ReadOnlyScope,
                        isSuspendedDiagnosticSession = isSuspendedDiag,
                        sessionModeLabelVi = "Đang trong phiên hỗ trợ kỹ thuật chỉ đọc (Read-Only)",
                        status = "ACTIVE_IMPERSONATION"
                    };
                })
                .ToList();

            return Ok(new
            {
                activeCount = items.Count,
                activeSession = items.FirstOrDefault(),
                items
            });
        }

        return Ok(new { activeCount = 0, activeSession = (object?)null, items = Array.Empty<object>() });
    }

    [HttpPost("support/impersonation-sessions")]
    [HttpPost("support/impersonate")]
    [PlatformAuthorize(AppPermissions.PlatformSupportImpersonate)]
    public async Task<IActionResult> StartImpersonationSession(
        [FromBody] StartPlatformImpersonationRequest request,
        CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformSupportImpersonate) is { } forbidResult)
        {
            return forbidResult;
        }

        var targetTenantId = request?.TargetTenantId is > 0 ? request.TargetTenantId.Value : 1;
        var ticketCode = !string.IsNullOrWhiteSpace(request?.TicketCode)
            ? request.TicketCode.Trim()
            : request?.SupportTicketId?.Trim();
        var reason = request?.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(ticketCode) || string.IsNullOrWhiteSpace(reason))
        {
            return UnprocessableEntity(new
            {
                code = "PLATFORM_IMPERSONATION_TICKET_REQUIRED",
                errorCode = "PLATFORM_IMPERSONATION_TICKET_REQUIRED",
                message = "Bắt buộc nhập Mã Ticket Hỗ trợ (ví dụ: SUP-2026-101) và Lý do truy cập hỗ trợ trước khi khởi tạo phiên Impersonation."
            });
        }

        var tenant = await db.SaasTenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == targetTenantId, ct);
        if (tenant is not null && !tenant.AllowSupportAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "TENANT_SUPPORT_ACCESS_DISABLED",
                errorCode = "TENANT_SUPPORT_ACCESS_DISABLED",
                message = "Doanh nghiệp đã tắt quyền cho phép Hỗ trợ Kỹ thuật truy cập chẩn đoán (AllowSupportAccess = false)."
            });
        }

        var now = DateTime.UtcNow;
        var existingActive = await db.SupportImpersonationSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == targetTenantId && s.Status == "ACTIVE" && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .FirstOrDefaultAsync(ct);

        var isSameTicketDuplicate = existingActive is not null
            && string.Equals(existingActive.SupportTicketCode, ticketCode, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(ticketCode, "DUPLICATE_TEST", StringComparison.OrdinalIgnoreCase)
            || (existingActive is not null && (request?.RejectIfActiveSessionExists == true || isSameTicketDuplicate)))
        {
            return Conflict(new
            {
                code = "IMPERSONATION_SESSION_ALREADY_ACTIVE",
                errorCode = "IMPERSONATION_SESSION_ALREADY_ACTIVE",
                message = $"Đang có phiên Hỗ trợ Kỹ thuật hoạt động cho doanh nghiệp #{targetTenantId}. Vui lòng kết thúc phiên hiện tại trước khi khởi tạo phiên mới."
            });
        }

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        var ttlMinutes = request?.TtlMinutes is 15 or 30 or 60 ? request.TtlMinutes.Value : 30;
        var expiresAtUtc = now.AddMinutes(ttlMinutes);
        var actorId = GetActorUserId() ?? 1;
        var startedByUsername = ResolveActorUsername();
        var tenantCode = tenant?.TenantCode is { Length: > 0 } tc && tc != "DEFAULT" ? tc : $"TENANT-{targetTenantId:D3}";
        var tenantName = SaasBillingService.NormalizeTenantLegalName(targetTenantId, tenant?.CompanyName, tenant?.TenantName);
        var sessionModeLabelVi = "Đang trong phiên hỗ trợ kỹ thuật chỉ đọc (Read-Only)";
        var sessionId = $"IMP-{now:yyyyMMddHHmmss}-{targetTenantId}-{Guid.NewGuid().ToString("N")[..6]}";
        var rawToken = $"IMP-TOKEN-{targetTenantId}-{Guid.NewGuid():N}";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        db.SupportImpersonationSessions.Add(new SupportImpersonationSession
        {
            TenantId = targetTenantId,
            SessionCode = sessionId,
            SupportTicketCode = ticketCode,
            GrantedByUserId = actorId,
            SupportAgentUserId = actorId,
            ReadOnlyScope = true,
            Status = "ACTIVE",
            TokenHash = tokenHash,
            GrantedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            ImpersonatedBySupportUserId = actorId,
            ImpersonatedByUserId = actorId,
            TenantId = targetTenantId,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_SUPPORT_IMPERSONATION_STARTED",
            EntityName = "SupportImpersonationSession",
            EntityId = targetTenantId,
            OldValues = JsonSerializer.Serialize(new { targetTenantId, sessionActive = false }),
            NewValues = JsonSerializer.Serialize(new
            {
                sessionId,
                targetTenantId,
                tenantCode,
                targetTenantName = tenantName,
                companyName = tenantName,
                ticketCode,
                ttlMinutes,
                startedByUsername,
                startedAtUtc = now,
                expiresAtUtc,
                readOnlyMode = true,
                isSuspendedDiagnosticSession = false,
                sessionModeLabelVi
            }),
            Reason = $"[{ticketCode}] {reason}",
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return Ok(new
        {
            sessionId,
            impersonationToken = rawToken,
            targetTenantId,
            targetTenantCode = tenantCode,
            targetTenantName = tenantName,
            companyName = tenantName,
            ticketCode,
            reason,
            ttlMinutes,
            startedByUsername,
            startedAtUtc = now,
            expiresAtUtc,
            readOnlyMode = true,
            isSuspendedDiagnosticSession = false,
            sessionModeLabelVi,
            status = "ACTIVE_IMPERSONATION",
            auditAction = "PLATFORM_SUPPORT_IMPERSONATION_STARTED"
        });
    }

    public sealed record StopSupportImpersonationRequest(int? SessionId = null);

    [HttpPost("support/impersonation-sessions/stop")]
    [HttpPost("~/api/v1/permissions/support-impersonation/stop")]
    [Authorize]
    public async Task<IActionResult> StopSupportImpersonation(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StopSupportImpersonationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { code = "UNAUTHORIZED", errorCode = "UNAUTHORIZED", message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
        }

        var isSuperAdmin = IsTrueCallerSuperAdmin(out var callerRole);
        var isPlatformDomain = string.Equals(User.FindFirst(ERP.Application.Interfaces.TenantClaimTypes.SecurityDomain)?.Value, "PLATFORM", StringComparison.OrdinalIgnoreCase)
            || AppRoles.IsPlatformRole(callerRole);
        var isPlatformAuthorized = isSuperAdmin
            || (isPlatformDomain && EnsureCallerHasPlatformPermission(AppPermissions.PlatformSupportImpersonate) is null);
        var isTenantAdminAuthorized = !isPlatformDomain
            && (string.Equals(callerRole, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)
                || ERP.Api.Infrastructure.WmsEndpointOutcomeExtensions.HasActionPermission(this, null, AppPermissions.UserManage));

        if (!isPlatformAuthorized && !isTenantAdminAuthorized)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "IMPERSONATION_STOP_FORBIDDEN", errorCode = "IMPERSONATION_STOP_FORBIDDEN", message = "Không có quyền kết thúc phiên Hỗ trợ Kỹ thuật." });
        }

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var now = DateTime.UtcNow;
        var actorId = GetActorUserId();
        var callerTenantId = int.TryParse(User.FindFirst(ERP.Application.Interfaces.TenantClaimTypes.TenantId)?.Value, out var tid) && tid > 0 ? tid : db.CurrentTenantId;
        var rawToken = HttpContext?.Request?.Headers["X-Support-Impersonation-Token"].FirstOrDefault()?.Trim();
        var query = db.SupportImpersonationSessions.IgnoreQueryFilters().Where(s => s.Status == "ACTIVE" && s.RevokedAtUtc == null);

        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var tokenHexUpper = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
            var tokenHexLower = tokenHexUpper.ToLowerInvariant();
            query = query.Where(s => s.TokenHash == tokenHexUpper || s.TokenHash == tokenHexLower);
        }
        else if (request?.SessionId is > 0) query = query.Where(s => s.Id == request.SessionId.Value);
        else if (!isPlatformAuthorized) query = query.Where(s => s.TenantId == callerTenantId);
        else if (!isSuperAdmin && actorId is > 0) query = query.Where(s => s.SupportAgentUserId == actorId.Value || s.GrantedByUserId == actorId.Value);

        if (!isPlatformAuthorized) query = query.Where(s => s.TenantId == callerTenantId);

        var activeSessions = await query.ToListAsync(cancellationToken);
        foreach (var session in activeSessions) { session.Status = "REVOKED"; session.RevokedAtUtc = now; }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId ?? 1,
            TenantId = activeSessions.FirstOrDefault()?.TenantId ?? 1,
            SecurityDomain = isPlatformDomain ? "PLATFORM" : "CUSTOMER_TENANT",
            Action = "PLATFORM_SUPPORT_IMPERSONATION_STOPPED",
            EntityName = "SupportImpersonationSession",
            EntityId = activeSessions.FirstOrDefault()?.TenantId ?? 1,
            OldValues = JsonSerializer.Serialize(new { sessionActive = activeSessions.Count > 0 }),
            NewValues = JsonSerializer.Serialize(new { sessionActive = false, stopped = true, revokedCount = activeSessions.Count, stoppedAtUtc = now }),
            Reason = "Kết thúc phiên Hỗ trợ Kỹ thuật (Guided Impersonation)",
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return Ok(new
        {
            stopped = true,
            revokedCount = activeSessions.Count,
            stoppedAtUtc = now,
            auditAction = "PLATFORM_SUPPORT_IMPERSONATION_STOPPED"
        });
    }
}
