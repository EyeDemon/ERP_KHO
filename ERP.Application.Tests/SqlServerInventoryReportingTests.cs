using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerInventoryReportingTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task StoredProcedure_HandlesTransfersBoundariesAndWarehouseFilter()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        try
        {
            var roleId = await InsertIdAsync(connection, transaction,
                "INSERT Roles (RoleName) OUTPUT INSERTED.Id VALUES (@value)", $"ReportRole{suffix}");
            var userId = await InsertIdAsync(connection, transaction,
                "INSERT Users (Username,PasswordHash,FullName,RoleId,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@value,'x','Report user',@roleId,1,SYSUTCDATETIME())",
                $"ReportUser{suffix}", new SqlParameter("@roleId", roleId));
            var unitId = await InsertIdAsync(connection, transaction,
                "INSERT Units (Code,Name,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@value,'Report unit',1,SYSUTCDATETIME())", $"U{suffix}");
            var productId = await InsertIdAsync(connection, transaction,
                "INSERT Products (Code,Name,UnitId,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@value,'Report product',@unitId,1,SYSUTCDATETIME())",
                $"P{suffix}", new SqlParameter("@unitId", unitId));
            var warehouseId = await InsertIdAsync(connection, transaction,
                "INSERT Warehouses (Code,Name,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@value,'Scoped warehouse',1,SYSUTCDATETIME())", $"W{suffix}");
            var otherWarehouseId = await InsertIdAsync(connection, transaction,
                "INSERT Warehouses (Code,Name,IsActive,CreatedAt) OUTPUT INSERTED.Id VALUES (@value,'Other warehouse',1,SYSUTCDATETIME())", $"O{suffix}");

            var from = new DateTime(2026, 8, 24, 1, 0, 0, DateTimeKind.Utc);
            var toExclusive = from.AddDays(1);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 0, 100, from.AddSeconds(-1), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 0, 50, from, userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 5, 20, from.AddHours(1), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 2, 5, from.AddHours(2), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 1, 30, from.AddHours(3), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 4, 25, from.AddHours(4), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 3, 10, from.AddHours(5), userId);
            await InsertTransactionAsync(connection, transaction, productId, warehouseId, 0, 999, toExclusive, userId);
            await InsertTransactionAsync(connection, transaction, productId, otherWarehouseId, 0, 777, from, userId);

            await using var command = new SqlCommand("sp_GetInventoryInOutReport", connection, transaction)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            command.Parameters.AddWithValue("@FromDate", from);
            command.Parameters.AddWithValue("@ToDate", toExclusive);
            command.Parameters.AddWithValue("@WarehouseId", warehouseId);
            command.Parameters.AddWithValue("@ProductId", productId);
            await using var reader = await command.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetDecimal(reader.GetOrdinal("OpeningQuantity")).Should().Be(100);
            reader.GetDecimal(reader.GetOrdinal("ImportQuantity")).Should().Be(50);
            reader.GetDecimal(reader.GetOrdinal("TransferInQuantity")).Should().Be(20);
            reader.GetDecimal(reader.GetOrdinal("AdjustmentIncreaseQuantity")).Should().Be(5);
            reader.GetDecimal(reader.GetOrdinal("InQuantity")).Should().Be(75);
            reader.GetDecimal(reader.GetOrdinal("ExportQuantity")).Should().Be(30);
            reader.GetDecimal(reader.GetOrdinal("TransferOutQuantity")).Should().Be(25);
            reader.GetDecimal(reader.GetOrdinal("AdjustmentDecreaseQuantity")).Should().Be(10);
            reader.GetDecimal(reader.GetOrdinal("OutQuantity")).Should().Be(65);
            reader.GetDecimal(reader.GetOrdinal("ClosingQuantity")).Should().Be(110);
            (await reader.ReadAsync()).Should().BeFalse();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task RolledBackMigration_RestoresPreviousStoredProcedureContract()
    {
        await using var database = await OwnedTemporaryMigrationDatabase.CreateAsync(ConnectionString);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260824131843_FixTransferInventoryReporting");
        await migrator.MigrateAsync("20260824123633_AddStockReservations");
        await using var connection = (SqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand("sp_GetInventoryInOutReport", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@WarehouseId", -1);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();

        columns.Should().Contain(["OpeningQuantity", "InQuantity", "OutQuantity", "ClosingQuantity"]);
        columns.Should().NotContain(["TransferInQuantity", "TransferOutQuantity"]);
    }

    private static async Task<int> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, string value, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task InsertTransactionAsync(SqlConnection connection, SqlTransaction transaction, int productId, int warehouseId, int type, decimal quantity, DateTime date, int userId)
    {
        await using var command = new SqlCommand(
            "INSERT InventoryTransactions (ProductId,WarehouseId,TransactionType,Quantity,TransactionDate,CreatedBy) VALUES (@product,@warehouse,@type,@quantity,@date,@user)",
            connection,
            transaction);
        command.Parameters.AddWithValue("@product", productId);
        command.Parameters.AddWithValue("@warehouse", warehouseId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@date", date);
        command.Parameters.AddWithValue("@user", userId);
        await command.ExecuteNonQueryAsync();
    }
}
