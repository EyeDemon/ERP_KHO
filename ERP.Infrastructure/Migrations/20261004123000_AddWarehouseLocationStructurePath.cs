using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261004123000_AddWarehouseLocationStructurePath")]
public partial class AddWarehouseLocationStructurePath : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StructurePath",
            table: "WarehouseLocations",
            type: "nvarchar(160)",
            maxLength: 160,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseLocations_WarehouseId_StructurePath",
            table: "WarehouseLocations",
            columns: new[] { "WarehouseId", "StructurePath" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_WarehouseLocations_StructurePath",
            table: "WarehouseLocations",
            sql: "[StructurePath] IS NULL OR ([StructurePath] = UPPER(LTRIM(RTRIM([StructurePath]))) AND LEN([StructurePath]) > 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_WarehouseLocations_StructurePath",
            table: "WarehouseLocations");

        migrationBuilder.DropIndex(
            name: "IX_WarehouseLocations_WarehouseId_StructurePath",
            table: "WarehouseLocations");

        migrationBuilder.DropColumn(
            name: "StructurePath",
            table: "WarehouseLocations");
    }
}
