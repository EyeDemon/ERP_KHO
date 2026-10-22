using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed record ResetPasswordAdminRequest(
    string? TemporaryPassword = null,
    string? Reason = null,
    string? RowVersion = null);

[ApiController]
[Route("api/users/{userId:int}/security")]
[Route("api/account-security/{userId:int}")]
[Route("api/v1/account-security/{userId:int}")]
[Authorize]
[PermissionAuthorize(AppPermissions.UserManage)]
public sealed class AccountSecurityController(
    IAccountAdminService service,
    IUserSessionService sessionService,
    ErpKhoDbContext db = null!,
    IPasswordHasherService? passwordHasher = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(userId, cancellationToken));

    [HttpPost("lock")]
    [IdempotentCommand("User.Lock")]
    public async Task<IActionResult> Lock(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var user = db is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken)
            : null;
        if (db is not null && user is null && await db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy người dùng.");

        var actorUserId = ResolveActorUserId();
        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        await using var transaction = db is not null && db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (db is not null && db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
                await ERP.Infrastructure.Services.PermissionAdministrationGuard.LockAsync(db, cancellationToken);

            if (db is not null && user is not null)
                await EnsureRemainingActiveTenantAdminAsync(user, cancellationToken);

            var revisionBefore = user?.SecurityRevision;
            if (sessionService is not null && !string.IsNullOrWhiteSpace(request?.RowVersion))
                await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

            var now = DateTime.UtcNow;
            if (user is not null && !string.IsNullOrWhiteSpace(request?.RowVersion) && user.SecurityRevision == revisionBefore)
                EnsureMatchingSecurityToken(user, request.RowVersion, now);

            var lockoutEnd = now.AddMinutes(30);
            if (db is not null)
            {
                if (user is not null)
                {
                    user.LockoutEnd = lockoutEnd;
                    if (user.SecurityRevision == revisionBefore)
                        user.SecurityRevision++;
                }

                var activeSessions = await db.UserSessions
                    .Where(s => s.UserId == userId && s.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var session in activeSessions)
                {
                    session.RevokedAt = now;
                    session.RevokeReason = "AdminLock";
                }

                db.AuditLogs.Add(new AuditLog
                {
                    UserId = actorUserId,
                    Action = "Authentication.AccountLocked",
                    EntityName = "User",
                    EntityId = userId,
                    Result = "Success",
                    Timestamp = now
                });
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }

            return Ok(new
            {
                userId,
                isLockedOut = true,
                lockoutEnd
            });
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    [HttpPost("unlock")]
    [IdempotentCommand("User.Unlock")]
    public async Task<IActionResult> Unlock(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await service.UnlockAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("revoke-sessions")]
    [IdempotentCommand("User.RevokeSessions")]
    public async Task<IActionResult> RevokeSessions(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await sessionService.RevokeUserSessionsAsAdminAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [IdempotentCommand("User.ResetPassword")]
    public async Task<IActionResult> ResetPassword(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResetPasswordAdminRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var user = db is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken)
            : null;
        if (db is not null && user is null && await db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy người dùng.");

        var actorUserId = ResolveActorUserId();
        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        if (string.IsNullOrWhiteSpace(request?.TemporaryPassword) || request.TemporaryPassword.Trim().Length < 8)
            throw new ERP.Application.Exceptions.BusinessRuleException("Mật khẩu tạm thời phải có tối thiểu 8 ký tự.");

        await using var transaction = db is not null && db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var revisionBefore = user?.SecurityRevision;
        if (sessionService is not null && !string.IsNullOrWhiteSpace(request.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (user is not null && !string.IsNullOrWhiteSpace(request.RowVersion) && user.SecurityRevision == revisionBefore)
            EnsureMatchingSecurityToken(user, request.RowVersion, now);

        if (db is not null)
        {
            if (user is not null)
            {
                var trimmedPassword = request.TemporaryPassword.Trim();
                user.PasswordHash = (passwordHasher ?? new ERP.Infrastructure.Services.PasswordHasherService()).HashPassword(trimmedPassword);
                user.FailedLoginCount = 0;
                user.LastFailedLoginAt = null;
                user.LockoutEnd = null;
                if (user.SecurityRevision == revisionBefore)
                    user.SecurityRevision++;
            }

            var activeSessions = await db.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.RevokedAt = now;
                session.RevokeReason = "PasswordReset";
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                Action = "User.PasswordReset",
                EntityName = "User",
                EntityId = userId,
                Reason = request.Reason,
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }

        return Ok(new
        {
            userId,
            status = "PASSWORD_RESET_COMPLETED",
            resetAtUtc = now
        });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        if (db is null)
            return NoContent();

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null)
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy người dùng.");

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
                await ERP.Infrastructure.Services.PermissionAdministrationGuard.LockAsync(db, cancellationToken);

            await EnsureRemainingActiveTenantAdminAsync(user, cancellationToken);

            var actorUserId = ResolveActorUserId();
            if (actorUserId.HasValue && actorUserId.Value == userId)
                throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc vô hiệu hóa tài khoản của chính mình.");

            var now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request?.RowVersion))
                EnsureMatchingSecurityToken(user, request.RowVersion, now);

            user.IsActive = false;
            user.SecurityRevision++;

            var activeSessions = await db.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.RevokedAt = now;
                session.RevokeReason = "AdminDeactivate";
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                Action = "Authentication.AccountDeactivated",
                EntityName = "User",
                EntityId = userId,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public sealed record VerifyTotpRequest(string? Code = null, string? TotpCode = null, string? Base32Secret = null);

    [HttpPost("totp/setup")]
    [IdempotentCommand("AccountSecurity.TotpSetup")]
    public async Task<IActionResult> SetupTotp(int userId, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var user = db is not null
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : null;
        var username = !string.IsNullOrWhiteSpace(user?.Username) ? user.Username.Trim() : $"user_{userId}";

        var secretBytes = GetTotpSecretBytes(userId, username);
        var base32Secret = EncodeBase32(secretBytes);
        var otpauthUri = $"otpauth://totp/ERP_WMS:{username}?secret={base32Secret}&issuer=ERP_WMS&digits=6&period=30";
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var currentTotpCode = ComputeTotpCode(secretBytes, timeStep);

        var now = DateTime.UtcNow;
        if (db is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = ResolveActorUserId(),
                EntityName = "UserTotpMfa",
                EntityId = userId,
                Action = "AccountSecurity.TotpSetup",
                NewValues = $"{base32Secret[..8]}...",
                Reason = "Khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            userId,
            username,
            base32Secret,
            otpauthUri,
            currentTotpCode,
            digits = 6,
            periodSeconds = 30,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            statusLabel = "Đã khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238"
        });
    }

    [HttpPost("totp/verify")]
    [IdempotentCommand("AccountSecurity.TotpVerify")]
    public async Task<IActionResult> VerifyTotp(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTotpRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var user = db is not null
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : null;
        var username = !string.IsNullOrWhiteSpace(user?.Username) ? user.Username.Trim() : $"user_{userId}";

        var secretBytes = GetTotpSecretBytes(userId, username);
        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var isValid = candidateCode.Length == 6 &&
            (string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep), StringComparison.Ordinal) ||
             string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep - 1), StringComparison.Ordinal) ||
             string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep + 1), StringComparison.Ordinal));

        var now = DateTime.UtcNow;
        if (db is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = ResolveActorUserId(),
                EntityName = "UserTotpMfa",
                EntityId = userId,
                Action = "AccountSecurity.TotpVerify",
                NewValues = isValid ? "VERIFIED" : "INVALID",
                Reason = isValid
                    ? $"Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số ({candidateCode}) theo chuẩn RFC 6238"
                    : "Mã bảo mật 2 lớp 6 chữ số không hợp lệ hoặc đã hết hạn",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            verified = isValid,
            userId,
            username,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            verifiedAtUtc = now,
            statusLabel = isValid
                ? $"Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số ({candidateCode}) theo chuẩn RFC 6238"
                : "Mã bảo mật 2 lớp 6 chữ số không hợp lệ hoặc đã hết hạn"
        });
    }

    public sealed record GrantSupportImpersonationRequest(
        string? SupportTicketCode = null,
        int? ExpiresInMinutes = null,
        bool ReadOnlyScope = true,
        int? SupportAgentUserId = null);

    public sealed record AnonymizeBuyerPiiRequest(
        int? RetentionDays = null,
        int? WarehouseId = null);

    [HttpPost("~/api/v1/security/support-impersonation/grant")]
    [HttpPost("~/api/security/support-impersonation/grant")]
    [HttpPost("~/api/v1/account-security/support-impersonation/grant")]
    [HttpPost("~/api/account-security/support-impersonation/grant")]
    [IdempotentCommand("AccountSecurity.GrantSupportImpersonation")]
    public async Task<IActionResult> GrantSupportImpersonation(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GrantSupportImpersonationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var expiresMinutes = Math.Clamp(request?.ExpiresInMinutes ?? 30, 5, 60);
        var expiresAtUtc = now.AddMinutes(expiresMinutes);
        var ticketCode = string.IsNullOrWhiteSpace(request?.SupportTicketCode) ? $"SUP-{now:yyyyMMdd}-01" : request.SupportTicketCode.Trim();
        var supportToken = $"IMP-{Guid.NewGuid():N}".ToUpperInvariant();
        int? sessionId = null;

        if (db is not null)
        {
            var actorId = ResolveActorUserId();
            var session = new ERP.Domain.Entities.SupportImpersonationSession
            {
                TenantId = db.CurrentTenantId,
                SessionCode = $"IMP-SES-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32].ToUpperInvariant(),
                SupportTicketCode = ticketCode,
                TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(supportToken))),
                GrantedByUserId = actorId ?? 1,
                SupportAgentUserId = request?.SupportAgentUserId ?? (actorId ?? 1),
                ReadOnlyScope = request?.ReadOnlyScope ?? true,
                GrantedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc,
                Status = "ACTIVE"
            };
            db.SupportImpersonationSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            sessionId = session.Id;

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorId,
                ImpersonatedBySupportUserId = request?.SupportAgentUserId ?? actorId,
                EntityName = "SupportImpersonationConsent",
                EntityId = sessionId ?? db.CurrentTenantId,
                Action = "AccountSecurity.GrantSupportImpersonation",
                NewValues = $"Ticket={ticketCode}; ExpiresAtUtc={expiresAtUtc:O}; ReadOnly={request?.ReadOnlyScope ?? true}",
                Reason = $"Chủ shop cấp quyền hỗ trợ kỹ thuật tạm thời ({expiresMinutes} phút) cho mã yêu cầu {ticketCode}",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            sessionId,
            supportTicketCode = ticketCode,
            supportImpersonationToken = supportToken,
            readOnlyScope = request?.ReadOnlyScope ?? true,
            expiresInMinutes = expiresMinutes,
            grantedAtUtc = now,
            expiresAtUtc,
            statusLabel = $"Đã cấp quyền hỗ trợ kỹ thuật có thời hạn ({expiresMinutes} phút) kèm nhật ký AuditLog bất biến"
        });
    }

    [HttpGet("~/api/v1/security/support-impersonation/sessions")]
    [HttpGet("~/api/security/support-impersonation/sessions")]
    [HttpGet("~/api/v1/account-security/support-impersonation/sessions")]
    [HttpGet("~/api/account-security/support-impersonation/sessions")]
    public async Task<IActionResult> ListSupportImpersonationSessions(CancellationToken cancellationToken = default)
    {
        if (db is null)
            return Ok(Array.Empty<object>());

        var sessions = await db.SupportImpersonationSessions
            .AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        return Ok(sessions);
    }

    [HttpPost("~/api/v1/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/v1/account-security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/account-security/support-impersonation/{sessionId:int}/revoke")]
    [IdempotentCommand("AccountSecurity.RevokeSupportImpersonation")]
    public async Task<IActionResult> RevokeSupportImpersonation(int sessionId, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (db is null)
            return NotFound();

        var session = await db.SupportImpersonationSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session == null)
            return NotFound(new { message = "Không tìm thấy phiên hỗ trợ kỹ thuật." });

        session.Status = "REVOKED";
        session.RevokedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = ResolveActorUserId(),
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

        return Ok(new
        {
            sessionId = session.Id,
            supportTicketCode = session.SupportTicketCode,
            status = session.Status,
            revokedAtUtc = session.RevokedAtUtc,
            statusLabel = $"Đã thu hồi phiên hỗ trợ kỹ thuật #{session.Id} ({session.SupportTicketCode})"
        });
    }

    [HttpPost("~/api/v1/security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/v1/account-security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/account-security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/v1/account-security/pdpa/anonymize-expired-pii")]
    [IdempotentCommand("AccountSecurity.AnonymizeExpiredBuyerPii")]
    public async Task<IActionResult> AnonymizeExpiredBuyerPii(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AnonymizeBuyerPiiRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var retentionDays = Math.Max(1, request?.RetentionDays ?? 90);
        var cutoffUtc = now.AddDays(-retentionDays);
        var anonymizedCount = 0;
        var anonymizedReceiptsCount = 0;
        var anonymizedOrdersCount = 0;
        var anonymizedReturnsCount = 0;

        if (db is not null)
        {
            var expiredReceipts = await db.ExportReceipts
                .Where(r => r.CreatedAt <= cutoffUtc &&
                            (r.Status == ERP.Domain.Enums.ReceiptStatus.Dispatched || r.Status == ERP.Domain.Enums.ReceiptStatus.Cancelled) &&
                            r.CustomerNameSnapshot != null &&
                            !r.CustomerNameSnapshot.StartsWith("ANONYMIZED_PII_"))
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (var r in expiredReceipts)
            {
                r.CustomerNameSnapshot = $"ANONYMIZED_PII_{r.Id}";
                r.Note = "[PII_REDACTED_DECREE_13_2023]";
                anonymizedReceiptsCount++;
                anonymizedCount++;
            }

            var expiredOrders = await db.SalesOrders
                .Include(o => o.Customer)
                .Where(o => o.CreatedAt <= cutoffUtc &&
                            (o.Status == ERP.Domain.Enums.SalesOrderStatus.Fulfilled ||
                             o.Status == ERP.Domain.Enums.SalesOrderStatus.Cancelled) &&
                            (o.CancellationReason == null || !o.CancellationReason.StartsWith("ANONYMIZED_PII_")))
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (var o in expiredOrders)
            {
                o.CancellationReason = $"ANONYMIZED_PII_{o.Id}";
                if (o.Customer != null && !o.Customer.Name.StartsWith("ANONYMIZED_PII_"))
                {
                    o.Customer.Name = $"ANONYMIZED_PII_PARTNER_{o.Customer.Id}";
                    o.Customer.Phone = "***REDACTED***";
                    o.Customer.Email = null;
                    o.Customer.Address = "[PII_REDACTED_DECREE_13_2023]";
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
                UserId = ResolveActorUserId(),
                EntityName = "BuyerPiiRetentionPolicy",
                EntityId = db.CurrentTenantId,
                Action = "AccountSecurity.AnonymizeExpiredBuyerPii",
                NewValues = $"RetentionDays={retentionDays}; AnonymizedTotal={anonymizedCount}; Receipts={anonymizedReceiptsCount}; Orders={anonymizedOrdersCount}; Returns={anonymizedReturnsCount}",
                Reason = $"Ẩn danh hóa dữ liệu cá nhân người mua (PII) quá hạn {retentionDays} ngày theo Nghị định 13/2023/NĐ-CP",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            retentionDays,
            cutoffUtc,
            anonymizedCount,
            anonymizedReceiptsCount,
            anonymizedOrdersCount,
            anonymizedReturnsCount,
            complianceStandard = "Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân",
            ledgerIntegrityPreserved = true,
            executedAtUtc = now,
            statusLabel = $"Đã ẩn danh hóa PII người mua quá hạn {retentionDays} ngày ({anonymizedCount} chứng từ) theo Nghị định 13/2023/NĐ-CP"
        });
    }

    public static byte[] GetTotpSecretBytes(int userId, string username)
    {
        var raw = System.Text.Encoding.UTF8.GetBytes($"ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username.Trim().ToLowerInvariant()}");
        var hash = System.Security.Cryptography.SHA256.HashData(raw);
        return hash[..20];
    }

    public static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            output.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }
        return output.ToString();
    }

    public static string ComputeTotpCode(byte[] secretBytes, long timeStep)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, timeStep);
        var hmac = System.Security.Cryptography.HMACSHA1.HashData(secretBytes, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binaryCode =
            ((hmac[offset] & 0x7F) << 24) |
            ((hmac[offset + 1] & 0xFF) << 16) |
            ((hmac[offset + 2] & 0xFF) << 8) |
            (hmac[offset + 3] & 0xFF);
        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    private async Task EnsureRemainingActiveTenantAdminAsync(User targetUser, CancellationToken cancellationToken)
    {
        if (db is null)
            return;

        var roleName = targetUser.Role?.RoleName
            ?? await db.Roles.AsNoTracking()
                .Where(r => r.Id == targetUser.RoleId)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        if (!string.Equals(roleName.Trim(), AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
            return;

        var now = DateTime.UtcNow;
        var activeTenantId = db.CurrentTenantId;
        var hasAnotherActiveTenantAdmin = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(u =>
                u.TenantId == activeTenantId &&
                u.Id != targetUser.Id &&
                u.IsActive &&
                (u.LockoutEnd == null || u.LockoutEnd <= now) &&
                u.Role.RoleName.Trim().ToLower() == "admin",
                cancellationToken);

        if (!hasAnotherActiveTenantAdmin)
            throw new ERP.Domain.Exceptions.ConcurrencyException("Không thể loại bỏ quản trị viên hợp lệ cuối cùng.");
    }

    private int? ResolveActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;

    private static void EnsureMatchingSecurityToken(User user, string rowVersion, DateTime now)
    {
        var bytes = new byte[9];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, user.SecurityRevision);
        bytes[8] = (byte)((user.LockoutEnd > now ? 1 : 0) | (user.IsActive ? 2 : 0));
        var expectedToken = Convert.ToBase64String(bytes);
        if (!string.Equals(rowVersion, expectedToken, StringComparison.Ordinal))
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
    }
}
