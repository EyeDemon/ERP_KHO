using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261008230000_AddInventoryReversalReasonCode")]
public sealed class AddInventoryReversalReasonCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historic immutable ledger events have no verified canonical code.
        // Preserve them as null; never retroactively fabricate a reason.
        migrationBuilder.AddColumn<string>(
            name: "ReasonCode", table: "InventoryTransactions",
            type: "nvarchar(40)", maxLength: 40, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReasonCode", table: "InventoryTransactions");
    }
}
