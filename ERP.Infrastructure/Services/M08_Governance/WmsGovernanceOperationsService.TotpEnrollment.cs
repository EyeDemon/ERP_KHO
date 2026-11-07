using System.Security.Cryptography;
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    public async Task<WmsEndpointOutcome> SetupTotpAsync(
        int userId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null || await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
        {
            return CreateUserNotFoundOutcome(userId);
        }

        var username = !string.IsNullOrWhiteSpace(user.Username) ? user.Username.Trim() : $"user_{userId}";
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var base32Secret = EncodeTotpBase32(secretBytes);
        var protectedSecret = GetTotpProtector().Protect(base32Secret);

        user.TotpSecretProtected = protectedSecret;
        user.TotpEnrollmentStatus = "PENDING_OTP";
        user.TotpRecoveryCodeHashesJson = null;
        user.TotpActivatedAtUtc = null;
        user.UpdatedAt = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = "AccountSecurity.TotpSetup",
            NewValues = "[REDACTED]",
            Reason = "Khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238 (Chờ xác minh OTP)",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        var otpauthUri = $"otpauth://totp/ERP_WMS:{username}?secret={base32Secret}&issuer=ERP_WMS&digits=6&period=30";
        return WmsEndpointOutcome.Ok(new
        {
            userId,
            username,
            status = "PENDING_OTP",
            enrollmentStatus = "PENDING_OTP",
            otpauthUri,
            recoveryCodes = Array.Empty<string>(),
            digits = 6,
            periodSeconds = 30,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            statusLabel = "Đã khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238 (Chờ xác minh mã 6 chữ số)"
        });
    }

    public async Task<WmsEndpointOutcome> RevealTotpSecretAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null || await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, null, cancellationToken))
        {
            return CreateUserNotFoundOutcome(userId);
        }

        if (string.IsNullOrWhiteSpace(user.TotpSecretProtected) ||
            string.Equals(user.TotpEnrollmentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.TotpEnrollmentStatus, "IDLE", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                userId,
                code = "TOTP_SECRET_SEALED",
                errorCode = "TOTP_SECRET_SEALED",
                message = "Khóa cấu hình 2 lớp đã được ẩn an toàn hoặc chưa khởi tạo phiên thiết lập."
            });
        }

        string base32Secret;
        try
        {
            base32Secret = UnprotectTotpSecret(user.TotpSecretProtected);
        }
        catch (CryptographicException)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                userId,
                code = "TOTP_SECRET_SEALED",
                errorCode = "TOTP_SECRET_SEALED",
                message = "Không thể giải mã khóa cấu hình 2 lớp. Vui lòng khởi tạo lại phiên thiết lập."
            });
        }

        return WmsEndpointOutcome.Ok(new
        {
            userId,
            status = user.TotpEnrollmentStatus,
            enrollmentStatus = user.TotpEnrollmentStatus,
            secret = base32Secret,
            base32Secret,
            secretKeyBase32 = base32Secret
        });
    }

    public async Task<WmsEndpointOutcome> VerifyTotpAsync(
        int userId,
        string? candidateCode,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null || await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
        {
            return CreateUserNotFoundOutcome(userId);
        }

        var username = !string.IsNullOrWhiteSpace(user.Username) ? user.Username.Trim() : $"user_{userId}";
        var now = DateTime.UtcNow;

        var recentFailedAttempts = await db.AuditLogs.AsNoTracking()
            .CountAsync(
                a => a.EntityId == userId &&
                     a.Action == "ACCOUNT_TOTP_VERIFY_FAILED" &&
                     a.Timestamp >= now.AddMinutes(-10),
                cancellationToken);
        if (recentFailedAttempts >= 5)
        {
            return WmsEndpointOutcome.Json(429, new
            {
                verified = false,
                userId,
                username,
                code = "TOO_MANY_TOTP_ATTEMPTS",
                errorCode = "TOO_MANY_TOTP_ATTEMPTS",
                message = "Tài khoản tạm khóa xác thực 2 lớp 10 phút do nhập sai mã TOTP quá 5 lần."
            });
        }

        if (string.IsNullOrWhiteSpace(user.TotpSecretProtected) ||
            string.Equals(user.TotpEnrollmentStatus, "IDLE", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                verified = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Chưa khởi tạo phiên thiết lập xác thực 2 lớp cho người dùng này."
            });
        }

        byte[] secretBytes;
        try
        {
            var base32Secret = UnprotectTotpSecret(user.TotpSecretProtected);
            secretBytes = DecodeTotpBase32(base32Secret);
        }
        catch (CryptographicException)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                verified = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Khóa cấu hình 2 lớp không hợp lệ. Vui lòng khởi tạo lại phiên thiết lập."
            });
        }

        var cleanCode = (candidateCode ?? string.Empty).Trim();
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        long? matchedStep = null;
        if (secretBytes.Length >= 10 && cleanCode.Length == 6)
        {
            foreach (var candidateStep in new[] { timeStep, timeStep - 1, timeStep + 1 })
            {
                if (string.Equals(cleanCode, ComputeRfc6238TotpCode(secretBytes, candidateStep), StringComparison.Ordinal))
                {
                    matchedStep = candidateStep;
                    break;
                }
            }
        }

        if (matchedStep is null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                EntityName = "UserTotpMfa",
                EntityId = userId,
                Action = "ACCOUNT_TOTP_VERIFY_FAILED",
                NewValues = "[REDACTED]",
                Reason = "Mã bảo mật 2 lớp 6 chữ số không hợp lệ hoặc đã hết hạn",
                Result = "Failed",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.BadRequest(new
            {
                verified = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Mã xác thực 6 chữ số không chính xác hoặc đã hết hạn. Vui lòng kiểm tra lại trên ứng dụng Authenticator."
            });
        }

        var stepMarker = $"step:{matchedStep.Value}";
        var isReplayedStep = await db.AuditLogs.AsNoTracking()
            .AnyAsync(
                a => a.EntityId == userId &&
                     (a.Action == "AccountSecurity.TotpVerify" || a.Action == "ACCOUNT_TOTP_VERIFIED") &&
                     a.Timestamp >= now.AddSeconds(-90) &&
                     a.OldValues == stepMarker,
                cancellationToken);
        if (isReplayedStep)
        {
            return WmsEndpointOutcome.Json(422, new
            {
                verified = false,
                userId,
                username,
                code = "TOTP_CODE_REPLAY_DETECTED",
                errorCode = "TOTP_CODE_REPLAY_DETECTED",
                message = "Mã xác thực 6 chữ số này đã được sử dụng trong chu kỳ 30 giây hiện tại. Vui lòng đợi mã mới."
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = "AccountSecurity.TotpVerify",
            OldValues = stepMarker,
            NewValues = "[REDACTED]",
            Reason = "Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số ([REDACTED]) và cấp 8 mã khôi phục dự phòng",
            Result = "Success",
            Timestamp = now
        });

        var issuedRecoveryCodes = GenerateTotpRecoveryCodeBatch();
        var recoveryHashes = issuedRecoveryCodes.Select(HashRecoveryCode).ToArray();

        user.TotpRecoveryCodeHashesJson = JsonSerializer.Serialize(recoveryHashes);
        user.TotpEnrollmentStatus = "PENDING_RECOVERY_ACK";
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            verified = true,
            userId,
            username,
            status = "PENDING_RECOVERY_ACK",
            enrollmentStatus = "PENDING_RECOVERY_ACK",
            recoveryCodes = issuedRecoveryCodes,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            verifiedAtUtc = now,
            statusLabel = "Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số (đã xác thực) và cấp 8 mã khôi phục dự phòng"
        });
    }

    public async Task<WmsEndpointOutcome> ConfirmTotpActivationAsync(
        int userId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null || await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
        {
            return CreateUserNotFoundOutcome(userId);
        }

        if (string.IsNullOrWhiteSpace(user.TotpSecretProtected) ||
            (!string.Equals(user.TotpEnrollmentStatus, "PENDING_RECOVERY_ACK", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(user.TotpEnrollmentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                activated = false,
                userId,
                code = "TOTP_RECOVERY_ACK_REQUIRED",
                errorCode = "TOTP_RECOVERY_ACK_REQUIRED",
                message = "Vui lòng xác minh mã 6 chữ số và lưu 8 mã khôi phục dự phòng trước khi hoàn tất kích hoạt xác thực 2 lớp."
            });
        }

        var now = DateTime.UtcNow;
        user.TotpEnrollmentStatus = "ACTIVE";
        user.TotpActivatedAtUtc = now;
        user.UpdatedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = "AccountSecurity.TotpConfirmActivation",
            NewValues = "ACTIVE",
            Reason = "Đã xác nhận lưu 8 mã khôi phục dự phòng và kích hoạt chính thức xác thực 2 lớp",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            activated = true,
            userId,
            status = "ACTIVE",
            enrollmentStatus = "ACTIVE",
            activatedAtUtc = now,
            statusLabel = "Đã bật xác thực 2 lớp. Khóa cấu hình và mã khôi phục đã được ẩn an toàn."
        });
    }
}
