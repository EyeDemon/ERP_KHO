using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    public async Task<WmsEndpointOutcome> CancelTotpEnrollmentAsync(
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

        var now = DateTime.UtcNow;
        user.TotpSecretProtected = null;
        user.TotpEnrollmentStatus = "IDLE";
        user.TotpRecoveryCodeHashesJson = null;
        user.TotpActivatedAtUtc = null;
        user.UpdatedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = "AccountSecurity.TotpCancel",
            NewValues = "CANCELLED",
            Reason = "Đã hủy thiết lập xác thực 2 lớp chưa hoàn tất và vô hiệu hóa bộ mã khôi phục tạm",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            cancelled = true,
            userId,
            status = "IDLE",
            enrollmentStatus = "IDLE",
            recoveryCodes = Array.Empty<string>(),
            statusLabel = "Đã hủy phiên thiết lập xác thực 2 lớp chưa hoàn tất và vô hiệu hóa bộ mã khôi phục tạm."
        });
    }

    public async Task<WmsEndpointOutcome> RegenerateRecoveryCodesAsync(
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
                regenerated = false,
                userId,
                username,
                code = "TOO_MANY_TOTP_ATTEMPTS",
                errorCode = "TOO_MANY_TOTP_ATTEMPTS",
                message = "Tài khoản tạm khóa xác thực 2 lớp 10 phút do nhập sai mã TOTP quá 5 lần."
            });
        }

        if (string.IsNullOrWhiteSpace(user.TotpSecretProtected))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                regenerated = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Chưa thiết lập khóa xác thực 2 lớp cho người dùng này."
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
                regenerated = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Khóa cấu hình 2 lớp không hợp lệ."
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
                Reason = "Xác minh mã TOTP thất bại khi yêu cầu sinh lại mã khôi phục",
                Result = "Failed",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.BadRequest(new
            {
                regenerated = false,
                userId,
                username,
                code = "INVALID_TOTP_CODE",
                errorCode = "INVALID_TOTP_CODE",
                message = "Mã xác thực 6 chữ số không chính xác hoặc đã hết hạn. Vui lòng nhập mã OTP hiện tại từ ứng dụng Authenticator để sinh lại mã khôi phục."
            });
        }

        var stepMarker = $"step:{matchedStep.Value}";
        var isReplayedStep = await db.AuditLogs.AsNoTracking()
            .AnyAsync(
                a => a.EntityId == userId &&
                     (a.Action == "AccountSecurity.TotpVerify" ||
                      a.Action == "AccountSecurity.TotpRegenerateRecoveryCodes" ||
                      a.Action == "ACCOUNT_TOTP_VERIFIED") &&
                     a.Timestamp >= now.AddSeconds(-90) &&
                     a.OldValues == stepMarker,
                cancellationToken);
        if (isReplayedStep)
        {
            return WmsEndpointOutcome.Json(422, new
            {
                regenerated = false,
                userId,
                username,
                code = "TOTP_CODE_REPLAY_DETECTED",
                errorCode = "TOTP_CODE_REPLAY_DETECTED",
                message = "Mã xác thực 6 chữ số này đã được sử dụng trong chu kỳ 30 giây hiện tại. Vui lòng đợi mã mới."
            });
        }

        var newRecoveryCodes = GenerateTotpRecoveryCodeBatch();
        var recoveryHashes = newRecoveryCodes.Select(HashRecoveryCode).ToArray();

        user.TotpRecoveryCodeHashesJson = JsonSerializer.Serialize(recoveryHashes);
        user.TotpEnrollmentStatus = "PENDING_RECOVERY_ACK";
        user.UpdatedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = "AccountSecurity.TotpRegenerateRecoveryCodes",
            OldValues = stepMarker,
            NewValues = "[REDACTED]",
            Reason = "Đã xác thực OTP 6 chữ số, thu hồi bộ mã khôi phục cũ và cấp 8 mã khôi phục dự phòng mới",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            regenerated = true,
            userId,
            username,
            status = "PENDING_RECOVERY_ACK",
            enrollmentStatus = "PENDING_RECOVERY_ACK",
            recoveryCodes = newRecoveryCodes,
            regeneratedAtUtc = now,
            statusLabel = "Đã thu hồi bộ mã khôi phục cũ và cấp 8 mã khôi phục dự phòng mới."
        });
    }

    private static string[] GenerateTotpRecoveryCodeBatch()
    {
        var recoveryCodes = new string[8];
        for (int i = 0; i < recoveryCodes.Length; i++)
        {
            var rBytes = RandomNumberGenerator.GetBytes(4);
            var hex = Convert.ToHexString(rBytes);
            recoveryCodes[i] = $"{hex[..4]}-{hex[4..8]}";
        }
        return recoveryCodes;
    }

    internal static string HashRecoveryCode(string recoveryCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recoveryCode.Trim().ToUpperInvariant())));

    public static string EncodeTotpBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
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
        if (bitsLeft > 0) output.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return output.ToString();
    }

    public static byte[] DecodeTotpBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0) return Array.Empty<byte>();

        var bytes = new List<byte>((clean.Length * 5) / 8);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var ch in clean)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0) return Array.Empty<byte>();
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }
        return bytes.ToArray();
    }

    public static string ComputeRfc6238TotpCode(byte[] secretBytes, long timeStep)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, timeStep);
        var hmac = HMACSHA1.HashData(secretBytes, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binaryCode =
            ((hmac[offset] & 0x7F) << 24) |
            ((hmac[offset + 1] & 0xFF) << 16) |
            ((hmac[offset + 2] & 0xFF) << 8) |
            (hmac[offset + 3] & 0xFF);
        return (binaryCode % 1_000_000).ToString("D6");
    }

    public async Task LogTotpAuditAsync(
        int userId, int? actorUserId, string action, string newValues, string reason, DateTime timestampUtc, CancellationToken cancellationToken = default)
    {
        _ = newValues;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "UserTotpMfa",
            EntityId = userId,
            Action = action,
            NewValues = "[REDACTED]",
            Reason = reason,
            Result = "Success",
            Timestamp = timestampUtc
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
