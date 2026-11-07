using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261004160000_AddWarehouseMapLayout")]
public partial class AddWarehouseMapLayout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "MapX", table: "WarehouseLocations", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "MapY", table: "WarehouseLocations", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "MapWidth", table: "WarehouseLocations", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "MapHeight", table: "WarehouseLocations", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_WarehouseLocations_MapLayout",
            table: "WarehouseLocations",
            sql: "([MapX] IS NULL AND [MapY] IS NULL AND [MapWidth] IS NULL AND [MapHeight] IS NULL) OR ([MapX] IS NOT NULL AND [MapY] IS NOT NULL AND [MapWidth] IS NOT NULL AND [MapHeight] IS NOT NULL AND [MapX] >= 0 AND [MapY] >= 0 AND [MapWidth] > 0 AND [MapHeight] > 0 AND [MapX] <= 100 AND [MapY] <= 100 AND [MapWidth] <= 100 AND [MapHeight] <= 100 AND [MapX] + [MapWidth] <= 100 AND [MapY] + [MapHeight] <= 100)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_WarehouseLocations_MapLayout", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MapX", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MapY", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MapWidth", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MapHeight", table: "WarehouseLocations");
    }
}
