using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave59RoadmapEpics301To303 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackingSessionCode",
                table: "PutToWallSlots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackingSessionId",
                table: "PutToWallSlots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierCutoffWindow",
                table: "PickingWaves",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClusterToteBarcodesCsv",
                table: "PickingWaves",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOrdersPerClusterTote",
                table: "PickingWaves",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SlaTierFilter",
                table: "PickingWaves",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateCode",
                table: "PickingWaves",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoneCode",
                table: "PickingWaves",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierIntegrationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CarrierCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ShopId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EncryptedApiToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSandbox = table.Column<bool>(type: "bit", nullable: false),
                    WebhookSecretHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SortingHubPrefix = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BaseShippingFeeVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierIntegrationConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PickingWaveTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CarrierCutoffWindow = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SlaTierFilter = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ZoneCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaxOrdersPerClusterTote = table.Column<int>(type: "int", nullable: false),
                    PickingType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingWaveTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierIntegrationConfigs_TenantId_WarehouseId_CarrierCode",
                table: "CarrierIntegrationConfigs",
                columns: new[] { "TenantId", "WarehouseId", "CarrierCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingWaveTemplates_TenantId_WarehouseId_TemplateCode",
                table: "PickingWaveTemplates",
                columns: new[] { "TenantId", "WarehouseId", "TemplateCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierIntegrationConfigs");

            migrationBuilder.DropTable(
                name: "PickingWaveTemplates");

            migrationBuilder.DropColumn(
                name: "PackingSessionCode",
                table: "PutToWallSlots");

            migrationBuilder.DropColumn(
                name: "PackingSessionId",
                table: "PutToWallSlots");

            migrationBuilder.DropColumn(
                name: "CarrierCutoffWindow",
                table: "PickingWaves");

            migrationBuilder.DropColumn(
                name: "ClusterToteBarcodesCsv",
                table: "PickingWaves");

            migrationBuilder.DropColumn(
                name: "MaxOrdersPerClusterTote",
                table: "PickingWaves");

            migrationBuilder.DropColumn(
                name: "SlaTierFilter",
                table: "PickingWaves");

            migrationBuilder.DropColumn(
                name: "TemplateCode",
                table: "PickingWaves");

            migrationBuilder.DropColumn(
                name: "ZoneCode",
                table: "PickingWaves");
        }
    }
}
