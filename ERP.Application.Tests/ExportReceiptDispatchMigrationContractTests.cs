using ERP.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ERP.Application.Tests;

public sealed class ExportReceiptDispatchMigrationContractTests
{
    [Fact]
    public void Up_StartsWithReadOnlyPreflightAndCreatesExportTransactionUniquenessGuard()
    {
        var operations = MigrationProbe.BuildUpOperations();

        var preflight = operations.First().Should().BeOfType<SqlOperation>().Subject.Sql;
        preflight.Should().Contain("ReservedQuantity > Quantity");
        preflight.Should().Contain("HAVING COUNT(*) > 1");
        preflight.Should().Contain("d.Quantity <= 0");
        preflight.Should().Contain("t.WarehouseId = e.WarehouseId");
        preflight.Should().NotContain("UPDATE ");

        operations.OfType<AddColumnOperation>().Should().HaveCount(3);
        operations.OfType<CreateIndexOperation>().Should().ContainSingle(x =>
            x.Name == "IX_InventoryTransactions_ExportReceiptReference" &&
            x.IsUnique && x.Filter == "[ReferenceType] = 'ExportReceipt'");
    }

    [Fact]
    public void Down_RefusesToEraseDispatchMeaningAndDropsOnlyAfterGuard()
    {
        var operations = MigrationProbe.BuildDownOperations();

        var guard = operations.First().Should().BeOfType<SqlOperation>().Subject.Sql;
        guard.Should().Contain("IF EXISTS (SELECT 1 FROM ExportReceipts WHERE Status = 3)");
        guard.Should().Contain("THROW 51011");
        operations.OfType<DropColumnOperation>().Select(x => x.Name)
            .Should().BeEquivalentTo("DispatchMode", "DispatchedAt", "DispatchedBy");
    }

    private sealed class MigrationProbe : AddExportReceiptDispatchWorkflow
    {
        public static IReadOnlyList<MigrationOperation> BuildUpOperations()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            new MigrationProbe().Up(builder);
            return builder.Operations;
        }

        public static IReadOnlyList<MigrationOperation> BuildDownOperations()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
            new MigrationProbe().Down(builder);
            return builder.Operations;
        }
    }
}
