using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

public partial class AddOutboundBaseUomSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BaseUomCodeSnapshot",
            table: "ExportReceiptDetails",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BaseUomDecimalPlacesSnapshot",
            table: "ExportReceiptDetails",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BaseUomIdSnapshot",
            table: "ExportReceiptDetails",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BaseUomNameSnapshot",
            table: "ExportReceiptDetails",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BaseUomCodeSnapshot", table: "ExportReceiptDetails");
        migrationBuilder.DropColumn(name: "BaseUomDecimalPlacesSnapshot", table: "ExportReceiptDetails");
        migrationBuilder.DropColumn(name: "BaseUomIdSnapshot", table: "ExportReceiptDetails");
        migrationBuilder.DropColumn(name: "BaseUomNameSnapshot", table: "ExportReceiptDetails");
    }
}
