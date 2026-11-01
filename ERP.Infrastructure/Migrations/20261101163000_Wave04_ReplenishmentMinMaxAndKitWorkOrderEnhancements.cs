using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave04_ReplenishmentMinMaxAndKitWorkOrderEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinBaseQty",
                table: "WarehouseLocations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxBaseQty",
                table: "WarehouseLocations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplenishFromLocationId",
                table: "WarehouseLocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomCode",
                table: "KitWorkOrders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PCS");

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRateSnapshot",
                table: "KitWorkOrders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomCode",
                table: "KitWorkOrderComponentLines",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PCS");

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRateSnapshot",
                table: "KitWorkOrderComponentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinBaseQty",
                table: "WarehouseLocations");

            migrationBuilder.DropColumn(
                name: "MaxBaseQty",
                table: "WarehouseLocations");

            migrationBuilder.DropColumn(
                name: "ReplenishFromLocationId",
                table: "WarehouseLocations");

            migrationBuilder.DropColumn(
                name: "BaseUomCode",
                table: "KitWorkOrders");

            migrationBuilder.DropColumn(
                name: "ConversionRateSnapshot",
                table: "KitWorkOrders");

            migrationBuilder.DropColumn(
                name: "BaseUomCode",
                table: "KitWorkOrderComponentLines");

            migrationBuilder.DropColumn(
                name: "ConversionRateSnapshot",
                table: "KitWorkOrderComponentLines");
        }
    }
}
