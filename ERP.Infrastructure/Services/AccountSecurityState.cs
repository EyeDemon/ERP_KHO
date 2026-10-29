using System.Buffers.Binary;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

internal static class AccountSecurityState
{
    internal const string Conflict = "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.";

    internal static string Token(User user, DateTime now)
    {
        // Include natural lockout expiry without writing on reads.
        var bytes = new byte[9];
        BinaryPrimitives.WriteInt64BigEndian(bytes, user.SecurityRevision);
        bytes[8] = (byte)((user.LockoutEnd > now ? 1 : 0) | (user.IsActive ? 2 : 0));
        return Convert.ToBase64String(bytes);
    }

    internal static async Task<User> ReadAsync(ErpKhoDbContext db, int userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == userId, ct)
        ?? throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");

    internal static async Task<User> ValidateAsync(ErpKhoDbContext db, int userId, string? token, CancellationToken ct)
    {
        var user = await ReadAsync(db, userId, ct);
        if (!string.Equals(token, Token(user, DateTime.UtcNow), StringComparison.Ordinal))
            throw new ConcurrencyException(Conflict);
        return user;
    }

    internal static Task AdvanceAsync(ErpKhoDbContext db, int userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s =>
            s.SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1), ct);

    internal static async Task AuthorizeAsync(ErpKhoDbContext db, ICurrentUser actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!actor.IsAuthenticated || !await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= now) && u.Role.Permissions.Any(p => p.Permission.Code == "user.manage"), ct))
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
    }
}
