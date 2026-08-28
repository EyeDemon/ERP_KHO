using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerExportWorkflowRollbackTests
{
    private const string PreviousMigration = "20260824131843_FixTransferInventoryReporting";

    [SqlServerFact]
    public async Task EmptyDatabase_UpDownUp_PreservesExpectedSchemaTransitions()
    {
        await using var database = await TemporaryMigrationDatabase.CreateAsync(BaseConnectionString());
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        (await HasDispatchColumnsAsync(context)).Should().BeTrue();

        await migrator.MigrateAsync(PreviousMigration);
        (await HasDispatchColumnsAsync(context)).Should().BeFalse();

        await migrator.MigrateAsync();
        (await HasDispatchColumnsAsync(context)).Should().BeTrue();
    }

    [SqlServerFact]
    public async Task DispatchedReceipt_DownIsBlockedBeforeAnySchemaOrDataMutation()
    {
        await using var database = await TemporaryMigrationDatabase.CreateAsync(BaseConnectionString());
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var role = new Role { RoleName = "RollbackTestRole" };
        var warehouse = new Warehouse { Code = "RB_WH", Name = "Rollback test warehouse", IsActive = true };
        context.AddRange(role, warehouse);
        await context.SaveChangesAsync();
        var user = new User { Username = "rollback_test_user", PasswordHash = "test-only", FullName = "Rollback test", RoleId = role.Id, IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var receipt = new ExportReceipt
        {
            Code = "RB_DISPATCHED",
            WarehouseId = warehouse.Id,
            CreatedBy = user.Id,
            ApprovedBy = user.Id,
            DispatchedBy = user.Id,
            Status = ReceiptStatus.Dispatched,
            DispatchMode = ExportDispatchMode.DispatchOnApproval,
            CreatedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow
        };
        context.ExportReceipts.Add(receipt);
        await context.SaveChangesAsync();

        var beforeCount = await context.ExportReceipts.CountAsync(x => x.Id == receipt.Id);
        var act = () => context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var error = await act.Should().ThrowAsync<SqlException>();
        error.Which.Number.Should().Be(51011);
        (await HasDispatchColumnsAsync(context)).Should().BeTrue();
        (await context.ExportReceipts.CountAsync(x => x.Id == receipt.Id)).Should().Be(beforeCount);
        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain("20260825104054_AddExportReceiptDispatchWorkflow");
    }

    private static string BaseConnectionString() =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static async Task<bool> HasDispatchColumnsAsync(ErpKhoDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('ExportReceipts') AND name IN ('DispatchMode','DispatchedAt','DispatchedBy')";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 3;
    }

    private sealed class TemporaryMigrationDatabase : IAsyncDisposable
    {
        private readonly string _masterConnection;
        private readonly string _databaseName;
        private readonly string _databaseConnection;

        private TemporaryMigrationDatabase(string masterConnection, string databaseName, string databaseConnection)
        {
            _masterConnection = masterConnection;
            _databaseName = databaseName;
            _databaseConnection = databaseConnection;
        }

        public static async Task<TemporaryMigrationDatabase> CreateAsync(string baseConnection)
        {
            var baseBuilder = new SqlConnectionStringBuilder(baseConnection);
            baseBuilder.IntegratedSecurity.Should().BeTrue("rollback tests may only use Windows authentication");
            var databaseName = $"ERP_KHO_RollbackTest_{Guid.NewGuid():N}";
            var masterBuilder = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
            var databaseBuilder = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName };
            return new TemporaryMigrationDatabase(masterBuilder.ConnectionString, databaseName, databaseBuilder.ConnectionString);
        }

        public ErpKhoDbContext CreateContext() => new(
            new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(_databaseConnection).Options);

        public async ValueTask DisposeAsync()
        {
            if (!_databaseName.StartsWith("ERP_KHO_RollbackTest_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to remove a database outside the rollback-test namespace.");
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
            await command.ExecuteNonQueryAsync();
        }
    }
}
