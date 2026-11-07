using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007120000_AddShipmentPostDispatchLogistics")]
public sealed class AddShipmentPostDispatchLogistics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "InTransitAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveryFailedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ReturnInitiatedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CompletedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ShipmentProofOfDeliveries",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ShipmentId = table.Column<int>(type: "int", nullable: false),
                DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                CarrierReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                DeliveryNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShipmentProofOfDeliveries", x => x.Id);
                table.CheckConstraint(
                    "CK_ShipmentProofOfDeliveries_Coordinates",
                    "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
                table.ForeignKey(
                    name: "FK_ShipmentProofOfDeliveries_Shipments_ShipmentId",
                    column: x => x.ShipmentId,
                    principalTable: "Shipments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShipmentProofOfDeliveries_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ShipmentTrackingEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ShipmentId = table.Column<int>(type: "int", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                FromStatus = table.Column<int>(type: "int", nullable: false),
                ToStatus = table.Column<int>(type: "int", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                SourceEventId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                ReasonCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RecordedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShipmentTrackingEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShipmentTrackingEvents_Shipments_ShipmentId",
                    column: x => x.ShipmentId,
                    principalTable: "Shipments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ShipmentTrackingEvents_Users_RecordedBy",
                    column: x => x.RecordedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentProofOfDeliveries_CreatedBy",
            table: "ShipmentProofOfDeliveries",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentProofOfDeliveries_ShipmentId",
            table: "ShipmentProofOfDeliveries",
            column: "ShipmentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentTrackingEvents_RecordedBy",
            table: "ShipmentTrackingEvents",
            column: "RecordedBy");

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentTrackingEvents_ShipmentId_OccurredAt_Id",
            table: "ShipmentTrackingEvents",
            columns: new[] { "ShipmentId", "OccurredAt", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentTrackingEvents_ShipmentId_Source_SourceEventId",
            table: "ShipmentTrackingEvents",
            columns: new[] { "ShipmentId", "Source", "SourceEventId" },
            unique: true,
            filter: "[SourceEventId] IS NOT NULL");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.confirm_delivery')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.confirm_delivery',N'Xác nhận giao hàng và Proof of Delivery',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='shipment.confirm_delivery'
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager','warehousestaff')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code='shipment.confirm_delivery';
DELETE FROM Permissions WHERE Code='shipment.confirm_delivery';
""");

        migrationBuilder.DropTable(name: "ShipmentProofOfDeliveries");
        migrationBuilder.DropTable(name: "ShipmentTrackingEvents");

        migrationBuilder.DropColumn(name: "InTransitAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "DeliveryFailedAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "ReturnInitiatedAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "CompletedAt", table: "Shipments");
    }
}
