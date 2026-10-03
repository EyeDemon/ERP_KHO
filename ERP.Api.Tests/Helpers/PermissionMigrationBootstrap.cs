using ERP.Infrastructure.Persistence;
using ERP.SqlIntegrationHarness;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.TestSupport;

public static class PermissionMigrationBootstrap
{
    public static async Task MigrateAsync(ErpKhoDbContext db, string runId, string username)
    {
        var connection = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        OwnedDatabasePolicy.EnsureAllowedName(connection.InitialCatalog);
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync();
        await using var verify = sql.CreateCommand();
        verify.CommandText = $"SELECT COUNT(*) FROM dbo.{OwnedDatabasePolicy.MarkerTable} WHERE MarkerType=@marker AND RunId=@run AND DatabaseName=@database AND DB_NAME()=@database";
        verify.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
        verify.Parameters.AddWithValue("@run", runId);
        verify.Parameters.AddWithValue("@database", connection.InitialCatalog);
        if (Convert.ToInt32(await verify.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("QA bootstrap ownership mismatch.");

        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains("20260930032852_AddPermissionCodeAuthorization"))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260927014531_AddInboundPutawayLocationMovement");
            // Only this marker-owned fixture creates its legitimate bootstrap identity before the strict production migration.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Roles(RoleName,Description)
                SELECT v.Name,N'Owned QA fixture' FROM (VALUES('Admin'),('Manager'),('Viewer'),('WarehouseStaff')) v(Name)
                WHERE NOT EXISTS(SELECT 1 FROM Roles r WHERE r.RoleName=v.Name);
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                IF NOT EXISTS(SELECT 1 FROM Users WHERE Username={username})
                INSERT INTO Users(Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt,FailedLoginCount)
                SELECT {username},'QA_FIXTURE_HASH_NOT_A_LOGIN',N'Owned QA bootstrap',Id,1,SYSUTCDATETIME(),0 FROM Roles WHERE RoleName='Admin';
                """);
        }
        await db.Database.MigrateAsync();
    }
}
