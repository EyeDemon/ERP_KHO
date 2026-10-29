using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261004143000_AddStorageCapacityProfiles")]
public partial class AddStorageCapacityProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StorageClass",
            table: "Products",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitWeightKg",
            table: "Products",
            type: "decimal(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitVolumeM3",
            table: "Products",
            type: "decimal(18,8)",
            precision: 18,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitPalletEquivalent",
            table: "Products",
            type: "decimal(18,8)",
            precision: 18,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StorageClass",
            table: "WarehouseLocations",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxWeightKg",
            table: "WarehouseLocations",
            type: "decimal(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxVolumeM3",
            table: "WarehouseLocations",
            type: "decimal(18,8)",
            precision: 18,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxPalletEquivalent",
            table: "WarehouseLocations",
            type: "decimal(18,8)",
            precision: 18,
            scale: 8,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Products_StorageClass",
            table: "Products",
            column: "StorageClass");

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseLocations_WarehouseId_StorageClass",
            table: "WarehouseLocations",
            columns: new[] { "WarehouseId", "StorageClass" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Products_StorageClass",
            table: "Products",
            sql: "[StorageClass] IS NULL OR ([StorageClass] = UPPER(LTRIM(RTRIM([StorageClass]))) AND LEN([StorageClass]) > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Products_StorageMetrics",
            table: "Products",
            sql: "([UnitWeightKg] IS NULL OR [UnitWeightKg] > 0) AND ([UnitVolumeM3] IS NULL OR [UnitVolumeM3] > 0) AND ([UnitPalletEquivalent] IS NULL OR [UnitPalletEquivalent] > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_WarehouseLocations_StorageClass",
            table: "WarehouseLocations",
            sql: "[StorageClass] IS NULL OR ([StorageClass] = UPPER(LTRIM(RTRIM([StorageClass]))) AND LEN([StorageClass]) > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_WarehouseLocations_Capacity",
            table: "WarehouseLocations",
            sql: "([MaxWeightKg] IS NULL OR [MaxWeightKg] > 0) AND ([MaxVolumeM3] IS NULL OR [MaxVolumeM3] > 0) AND ([MaxPalletEquivalent] IS NULL OR [MaxPalletEquivalent] > 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_Products_StorageClass", table: "Products");
        migrationBuilder.DropCheckConstraint(name: "CK_Products_StorageMetrics", table: "Products");
        migrationBuilder.DropCheckConstraint(name: "CK_WarehouseLocations_StorageClass", table: "WarehouseLocations");
        migrationBuilder.DropCheckConstraint(name: "CK_WarehouseLocations_Capacity", table: "WarehouseLocations");

        migrationBuilder.DropIndex(name: "IX_Products_StorageClass", table: "Products");
        migrationBuilder.DropIndex(name: "IX_WarehouseLocations_WarehouseId_StorageClass", table: "WarehouseLocations");

        migrationBuilder.DropColumn(name: "StorageClass", table: "Products");
        migrationBuilder.DropColumn(name: "UnitWeightKg", table: "Products");
        migrationBuilder.DropColumn(name: "UnitVolumeM3", table: "Products");
        migrationBuilder.DropColumn(name: "UnitPalletEquivalent", table: "Products");

        migrationBuilder.DropColumn(name: "StorageClass", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MaxWeightKg", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MaxVolumeM3", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "MaxPalletEquivalent", table: "WarehouseLocations");
    }
}
