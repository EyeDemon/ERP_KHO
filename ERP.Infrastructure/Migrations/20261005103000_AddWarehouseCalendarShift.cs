using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005103000_AddWarehouseCalendarShift")]
public partial class AddWarehouseCalendarShift : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarehouseCalendars",
            columns: table => new
            {
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseCalendars", x => x.WarehouseId);
                table.ForeignKey("FK_WarehouseCalendars_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WarehouseCalendarDays",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                DayOfWeek = table.Column<int>(type: "int", nullable: false),
                IsOpen = table.Column<bool>(type: "bit", nullable: false),
                OpensAtLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                ClosesAtLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                InboundCutoffLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                OutboundCutoffLocal = table.Column<TimeSpan>(type: "time", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseCalendarDays", x => x.Id);
                table.ForeignKey("FK_WarehouseCalendarDays_WarehouseCalendars_WarehouseId", x => x.WarehouseId, "WarehouseCalendars", "WarehouseId", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_WarehouseCalendarDays_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
                table.CheckConstraint("CK_WarehouseCalendarDays_OpenHours", "([IsOpen] = 0 AND [OpensAtLocal] IS NULL AND [ClosesAtLocal] IS NULL AND [InboundCutoffLocal] IS NULL AND [OutboundCutoffLocal] IS NULL) OR ([IsOpen] = 1 AND [OpensAtLocal] IS NOT NULL AND [ClosesAtLocal] IS NOT NULL AND [OpensAtLocal] <> [ClosesAtLocal])");
            });

        migrationBuilder.CreateTable(
            name: "WarehouseShifts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                StartTimeLocal = table.Column<TimeSpan>(type: "time", nullable: false),
                EndTimeLocal = table.Column<TimeSpan>(type: "time", nullable: false),
                BreakMinutes = table.Column<int>(type: "int", nullable: false),
                PlannedHeadcount = table.Column<int>(type: "int", nullable: false),
                InboundPalletsPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                OutboundOrdersPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                DockSlots = table.Column<int>(type: "int", nullable: true),
                LaborHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                StagingCapacity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                PackingStations = table.Column<int>(type: "int", nullable: true),
                EquipmentAvailable = table.Column<int>(type: "int", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseShifts", x => x.Id);
                table.ForeignKey("FK_WarehouseShifts_WarehouseCalendars_WarehouseId", x => x.WarehouseId, "WarehouseCalendars", "WarehouseId", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_WarehouseShifts_Time", "[StartTimeLocal] <> [EndTimeLocal]");
                table.CheckConstraint("CK_WarehouseShifts_NonNegative", "[BreakMinutes] >= 0 AND [PlannedHeadcount] >= 0 AND ([InboundPalletsPerHour] IS NULL OR [InboundPalletsPerHour] >= 0) AND ([OutboundOrdersPerHour] IS NULL OR [OutboundOrdersPerHour] >= 0) AND ([DockSlots] IS NULL OR [DockSlots] >= 0) AND ([LaborHours] IS NULL OR [LaborHours] >= 0) AND ([StagingCapacity] IS NULL OR [StagingCapacity] >= 0) AND ([PackingStations] IS NULL OR [PackingStations] >= 0) AND ([EquipmentAvailable] IS NULL OR [EquipmentAvailable] >= 0)");
            });

        migrationBuilder.CreateIndex("UX_WarehouseCalendarDays_Warehouse_Day", "WarehouseCalendarDays", new[] { "WarehouseId", "DayOfWeek" }, unique: true);
        migrationBuilder.CreateIndex("UX_WarehouseShifts_Warehouse_Code", "WarehouseShifts", new[] { "WarehouseId", "Code" }, unique: true);

        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='warehouse_calendar.manage')
    INSERT INTO Permissions (Code,Description,CreatedAt) VALUES ('warehouse_calendar.manage',N'Quản lý lịch vận hành và ca kho',SYSUTCDATETIME());
INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r CROSS JOIN Permissions p
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND p.Code='warehouse_calendar.manage'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE p.Code='warehouse_calendar.manage';
DELETE FROM Permissions WHERE Code='warehouse_calendar.manage';
");
        migrationBuilder.DropTable("WarehouseCalendarDays");
        migrationBuilder.DropTable("WarehouseShifts");
        migrationBuilder.DropTable("WarehouseCalendars");
    }
}
