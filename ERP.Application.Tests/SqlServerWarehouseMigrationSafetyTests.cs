using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWarehouseMigrationSafetyTests(ITestOutputHelper output)
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task PreMigration_BackupAndInventory()
    {
        if (Environment.GetEnvironmentVariable("ERP_KHO_RUN_MIGRATION_BACKUP_TEST") != "1")
            return;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var identity = await ScalarRowAsync(connection,
            "SELECT @@SERVERNAME, DB_NAME(), CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128))");
        identity[1].Should().Be("ERP_KHO");
        identity[2].Should().BeEquivalentTo(Environment.MachineName);
        output.WriteLine("Server={0}; Database={1}; Machine={2}; Edition={3}", identity);

        var users = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Users"));
        var lockedUsers = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Users WHERE IsActive = 0"));
        var warehouses = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Warehouses"));
        var migration = Convert.ToString(await ScalarAsync(connection, "SELECT TOP (1) MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC"));
        output.WriteLine("Users={0}; LockedUsers={1}; Warehouses={2}; ExpectedBackfill={3}; CurrentMigration={4}",
            users, lockedUsers, warehouses, users * warehouses, migration);

        var backupDirectory = Convert.ToString(await ScalarAsync(connection,
            "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))"));
        if (string.IsNullOrWhiteSpace(backupDirectory))
            backupDirectory = Environment.GetEnvironmentVariable("ERP_KHO_LOCAL_BACKUP_DIRECTORY");
        backupDirectory.Should().NotBeNullOrWhiteSpace();
        var backupPath = Path.Combine(backupDirectory!, $"ERP_KHO_pre_warehouse_isolation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak");

        await using var backup = connection.CreateCommand();
        backup.CommandTimeout = 300;
        backup.CommandText = "BACKUP DATABASE [ERP_KHO] TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT";
        backup.Parameters.AddWithValue("@path", backupPath);
        await backup.ExecuteNonQueryAsync();

        await using var verify = connection.CreateCommand();
        verify.CommandTimeout = 300;
        verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
        verify.Parameters.AddWithValue("@path", backupPath);
        await verify.ExecuteNonQueryAsync();
        output.WriteLine("Backup created: {0}", backupPath);
        output.WriteLine("Backup verification: RESTORE VERIFYONLY WITH CHECKSUM passed");
    }

    [SqlServerFact]
    public async Task AppliedMigration_HasExpectedSchemaAndBackfill()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260824110529_AddUserWarehouseAccess'"))
            .Should().Be(1);
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sys.tables WHERE name = 'UserWarehouses'"))
            .Should().Be(1);
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID('UserWarehouses') AND type='PK'"))
            .Should().Be(1);
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('UserWarehouses')"))
            .Should().Be(3);

        var users = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Users"));
        var warehouses = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Warehouses"));
        var accesses = Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM UserWarehouses"));
        accesses.Should().Be(users * warehouses);

        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM (SELECT UserId, WarehouseId FROM UserWarehouses GROUP BY UserId, WarehouseId HAVING COUNT(*) > 1) d"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM UserWarehouses uw LEFT JOIN Users u ON u.Id=uw.UserId LEFT JOIN Warehouses w ON w.Id=uw.WarehouseId WHERE u.Id IS NULL OR w.Id IS NULL"))
            .Should().Be(0);

        var fullAccessUsers = Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Users u WHERE (SELECT COUNT(*) FROM UserWarehouses uw WHERE uw.UserId=u.Id) = @warehouses",
            new SqlParameter("@warehouses", warehouses)));
        output.WriteLine("Users={0}; Warehouses={1}; Backfill={2}; FullAccessUsers={3}", users, warehouses, accesses, fullAccessUsers);

        await using var fullAccessCommand = connection.CreateCommand();
        fullAccessCommand.CommandText = "SELECT u.Username, r.RoleName, u.IsActive FROM Users u JOIN Roles r ON r.Id=u.RoleId WHERE (SELECT COUNT(*) FROM UserWarehouses uw WHERE uw.UserId=u.Id)=@warehouses ORDER BY u.Username";
        fullAccessCommand.Parameters.AddWithValue("@warehouses", warehouses);
        await using var reader = await fullAccessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            output.WriteLine("FullAccessUser={0}; Role={1}; Active={2}", reader.GetString(0), reader.GetString(1), reader.GetBoolean(2));
    }

    [SqlServerFact]
    public async Task RolledBackMigration_RemovesOnlyWarehouseAccessSchema()
    {
        if (Environment.GetEnvironmentVariable("ERP_KHO_EXPECT_WAREHOUSE_MIGRATION_ROLLED_BACK") != "1")
            return;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables WHERE name='UserWarehouses'"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId='20260824110529_AddUserWarehouseAccess'"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Users")).Should().BeGreaterThan(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Warehouses")).Should().BeGreaterThan(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM InventoryStocks")).Should().BeGreaterThanOrEqualTo(0);
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string[]> ScalarRowAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i)) ?? "").ToArray();
    }
}
