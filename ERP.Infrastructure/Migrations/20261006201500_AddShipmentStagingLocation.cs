using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006201500_AddShipmentStagingLocation")]
public sealed class AddShipmentStagingLocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "StagingLocationId",
            table: "Shipments",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_StagingLocationId",
            table: "Shipments",
            column: "StagingLocationId");

        migrationBuilder.AddForeignKey(
            name: "FK_Shipments_WarehouseLocations_StagingLocationId",
            table: "Shipments",
            column: "StagingLocationId",
            principalTable: "WarehouseLocations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Shipments_WarehouseLocations_StagingLocationId",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_StagingLocationId",
            table: "Shipments");

        migrationBuilder.DropColumn(
            name: "StagingLocationId",
            table: "Shipments");
    }
}
