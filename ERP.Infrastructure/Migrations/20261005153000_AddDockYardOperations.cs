using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005153000_AddDockYardOperations")]
public partial class AddDockYardOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Docks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                SupportsInbound = table.Column<bool>(type: "bit", nullable: false),
                SupportsOutbound = table.Column<bool>(type: "bit", nullable: false),
                AllowedVehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                IsTemperatureControlled = table.Column<bool>(type: "bit", nullable: false),
                HazardAllowed = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Docks", x => x.Id);
                table.ForeignKey("FK_Docks_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_Docks_Direction", "[SupportsInbound] = 1 OR [SupportsOutbound] = 1");
            });

        migrationBuilder.CreateTable(
            name: "YardSlots",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_YardSlots", x => x.Id);
                table.ForeignKey("FK_YardSlots_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DockAppointments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Direction = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                PlannedStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                PlannedEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CarrierCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                VehiclePlate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                TrailerPlate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                VehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                RequiresTemperatureControl = table.Column<bool>(type: "bit", nullable: false),
                Hazardous = table.Column<bool>(type: "bit", nullable: false),
                DriverName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                DriverPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                SealNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                YardSlotId = table.Column<int>(type: "int", nullable: true),
                DockId = table.Column<int>(type: "int", nullable: true),
                ArrivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CheckedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DockAssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ServiceStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ServiceCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CheckedOutAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ExceptionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockAppointments", x => x.Id);
                table.ForeignKey("FK_DockAppointments_Docks_DockId", x => x.DockId, "Docks", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DockAppointments_Users_CreatedBy", x => x.CreatedBy, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DockAppointments_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DockAppointments_YardSlots_YardSlotId", x => x.YardSlotId, "YardSlots", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_DockAppointments_Window", "[PlannedStartUtc] < [PlannedEndUtc]");
            });

        migrationBuilder.CreateTable(
            name: "DockAppointmentEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                DockAppointmentId = table.Column<int>(type: "int", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                EventAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ActorUserId = table.Column<int>(type: "int", nullable: false),
                DockId = table.Column<int>(type: "int", nullable: true),
                YardSlotId = table.Column<int>(type: "int", nullable: true),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DockAppointmentEvents", x => x.Id);
                table.ForeignKey("FK_DockAppointmentEvents_DockAppointments_DockAppointmentId", x => x.DockAppointmentId, "DockAppointments", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DockAppointmentEvents_Docks_DockId", x => x.DockId, "Docks", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DockAppointmentEvents_Users_ActorUserId", x => x.ActorUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DockAppointmentEvents_YardSlots_YardSlotId", x => x.YardSlotId, "YardSlots", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("UX_Docks_Warehouse_Code", "Docks", new[] { "WarehouseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("UX_YardSlots_Warehouse_Code", "YardSlots", new[] { "WarehouseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("UX_DockAppointments_Warehouse_Code", "DockAppointments", new[] { "WarehouseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_DockAppointments_Warehouse_Window", "DockAppointments", new[] { "WarehouseId", "PlannedStartUtc", "PlannedEndUtc" });
        migrationBuilder.CreateIndex("IX_DockAppointments_Dock_Window_Status", "DockAppointments", new[] { "DockId", "PlannedStartUtc", "PlannedEndUtc", "Status" });
        migrationBuilder.CreateIndex("IX_DockAppointments_Yard_Status", "DockAppointments", new[] { "YardSlotId", "Status" });
        migrationBuilder.CreateIndex("IX_DockAppointments_CreatedBy", "DockAppointments", "CreatedBy");
        migrationBuilder.CreateIndex("IX_DockAppointmentEvents_Appointment_Time", "DockAppointmentEvents", new[] { "DockAppointmentId", "EventAtUtc" });
        migrationBuilder.CreateIndex("IX_DockAppointmentEvents_ActorUserId", "DockAppointmentEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_DockAppointmentEvents_DockId", "DockAppointmentEvents", "DockId");
        migrationBuilder.CreateIndex("IX_DockAppointmentEvents_YardSlotId", "DockAppointmentEvents", "YardSlotId");

        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='dock.read')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('dock.read',N'Xem dock cửa kho',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='dock.manage')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('dock.manage',N'Quản lý dock và yard slot',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='dock_appointment.read')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('dock_appointment.read',N'Xem lịch xe dock/yard',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='dock_appointment.manage')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('dock_appointment.manage',N'Quản lý lịch xe và trạng thái dịch vụ dock',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='yard.read')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('yard.read',N'Xem yard và trạng thái chờ',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='yard.checkin')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('yard.checkin',N'Ghi nhận xe đến và gate check-in',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='yard.assign_dock')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('yard.assign_dock',N'Điều phối appointment vào dock',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='yard.checkout')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('yard.checkout',N'Gate checkout xe',SYSUTCDATETIME());

INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r CROSS JOIN Permissions p
WHERE ((
    LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
    AND p.Code IN ('dock.read','dock.manage','dock_appointment.read','dock_appointment.manage','yard.read','yard.checkin','yard.assign_dock','yard.checkout')
) OR (
    LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
    AND p.Code IN ('dock.read','dock_appointment.read','yard.read','yard.checkin','yard.assign_dock','yard.checkout')
) OR (
    LOWER(LTRIM(RTRIM(r.RoleName)))='viewer'
    AND p.Code IN ('dock.read','dock_appointment.read','yard.read')
))
AND NOT EXISTS (
    SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id
);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('dock.read','dock.manage','dock_appointment.read','dock_appointment.manage','yard.read','yard.checkin','yard.assign_dock','yard.checkout');
DELETE FROM Permissions
WHERE Code IN ('dock.read','dock.manage','dock_appointment.read','dock_appointment.manage','yard.read','yard.checkin','yard.assign_dock','yard.checkout');
");
        migrationBuilder.DropTable("DockAppointmentEvents");
        migrationBuilder.DropTable("DockAppointments");
        migrationBuilder.DropTable("Docks");
        migrationBuilder.DropTable("YardSlots");
    }
}
