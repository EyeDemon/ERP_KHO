using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261009140000_AddStockTransferDraftRevision")]
public sealed class AddStockTransferDraftRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Historical documents have no reliable edit counter. Start at 1.
        migrationBuilder.AddColumn<int>(
            name: "DraftRevision", table: "StockTransfers", type: "int",
            nullable: false, defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DraftRevision", table: "StockTransfers");
    }
}
