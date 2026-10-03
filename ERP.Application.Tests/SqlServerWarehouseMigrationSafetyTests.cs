using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWarehouseMigrationSafetyTests(ITestOutputHelper output)
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task AppliedMigration_HasExpectedSchemaAndValidCurrentAssignments()
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
        accesses.Should().BeGreaterThanOrEqualTo(0);
        accesses.Should().BeLessThanOrEqualTo(users * warehouses);

        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM (SELECT UserId, WarehouseId FROM UserWarehouses GROUP BY UserId, WarehouseId HAVING COUNT(*) > 1) d"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM UserWarehouses uw LEFT JOIN Users u ON u.Id=uw.UserId LEFT JOIN Warehouses w ON w.Id=uw.WarehouseId WHERE u.Id IS NULL OR w.Id IS NULL"))
            .Should().Be(0);

        var fullAccessUsers = Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM Users u WHERE (SELECT COUNT(*) FROM UserWarehouses uw WHERE uw.UserId=u.Id) = @warehouses",
            new SqlParameter("@warehouses", warehouses)));
        output.WriteLine("Users={0}; Warehouses={1}; CurrentAssignments={2}; FullAccessUsers={3}", users, warehouses, accesses, fullAccessUsers);

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
        await using var database = await OwnedTemporaryMigrationDatabase.CreateAsync(ConnectionString);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        const string previousMigration = "20260721063435_AddInventoryTransactionCompositeIndex";
        await migrator.MigrateAsync(previousMigration);

        await context.Database.OpenConnectionAsync();
        await using var connection = (SqlConnection)context.Database.GetDbConnection();
        var suffix = Guid.NewGuid().ToString("N");
        var roleId = Convert.ToInt32(await ScalarAsync(connection,
            "INSERT Roles (RoleName) OUTPUT INSERTED.Id VALUES (@name)",
            new SqlParameter("@name", $"WHRole{suffix}")));
        var warehouseId = Convert.ToInt32(await ScalarAsync(connection,
            "INSERT Warehouses (Code,Name,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@code,@name,1,SYSUTCDATETIME())",
            new SqlParameter("@code", $"WH{suffix}"[..20]),
            new SqlParameter("@name", "Warehouse migration test")));
        var userId = Convert.ToInt32(await ScalarAsync(connection,
            "INSERT Users (Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@username,@hash,@name,@role,1,SYSUTCDATETIME())",
            new SqlParameter("@username", $"wh_user_{suffix}"),
            new SqlParameter("@hash", "test-only"),
            new SqlParameter("@name", "Warehouse migration test"),
            new SqlParameter("@role", roleId)));

        await migrator.MigrateAsync("20260824110529_AddUserWarehouseAccess");
        await migrator.MigrateAsync(previousMigration);

        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.tables WHERE name='UserWarehouses'"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId='20260824110529_AddUserWarehouseAccess'"))
            .Should().Be(0);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Users WHERE Id=@id", new SqlParameter("@id", userId))).Should().Be(1);
        Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM Warehouses WHERE Id=@id", new SqlParameter("@id", warehouseId))).Should().Be(1);
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
