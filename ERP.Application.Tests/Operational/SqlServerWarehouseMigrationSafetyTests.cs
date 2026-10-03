using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ERP.Application.Tests;

[Trait("Category", "Operational")]
[Trait("Operation", "Backup")]
[Trait("OwnerDecision", "DeferredByOwner")]
public sealed class SqlServerWarehouseMigrationSafetyTests(ITestOutputHelper output)
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ERP_KHO_SQLSERVER_TEST_CONNECTION")!;

    [Fact(Skip = "DEFERRED_BY_OWNER / OPERATIONAL_TEST: separate future authorization required.")]
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
