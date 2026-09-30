using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Services;

public static class PermissionAdministrationGuard
{
    public static readonly string[] RequiredPermissions =
    ["permission.read", "permission.assign", "role.read", "role.manage", "user.read", "user.manage", "user_warehouse.read", "user_warehouse.manage"];

    public static async Task LockAsync(ErpKhoDbContext db, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Permission administration requires a transaction.");
        // ponytail: serialize rare administration writes globally; shard only with a proven cross-role invariant strategy.
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'ERP.PermissionAdministration',
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51009, 'Administration concurrency conflict.', 1;
            """, token);
        }
        catch (SqlException ex) when (ex.Number == 51009)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
    }

    public static async Task EnsureAdministratorAsync(ErpKhoDbContext db, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var valid = await db.Users.AsNoTracking().AnyAsync(u => u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= now) &&
            u.Role.RoleName.Trim().ToLower() == "admin" &&
            RequiredPermissions.All(code => u.Role.Permissions.Any(p => p.Permission.Code == code)), token);
        if (!valid)
            throw new ConcurrencyException("Không thể loại bỏ quản trị viên hợp lệ cuối cùng.");
    }
}
