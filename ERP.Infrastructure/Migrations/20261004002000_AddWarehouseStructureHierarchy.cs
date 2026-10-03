using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261004002000_AddWarehouseStructureHierarchy")]
public partial class AddWarehouseStructureHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarehouseZones",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, collation: "Latin1_General_100_CI_AS"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ZoneType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                PickPriority = table.Column<int>(type: "int", nullable: true),
                PutawayPriority = table.Column<int>(type: "int", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseZones", x => x.Id);
                table.CheckConstraint("CK_WarehouseZones_Code", "[Code] = UPPER(LTRIM(RTRIM([Code]))) AND LEN([Code]) > 0");
                table.CheckConstraint("CK_WarehouseZones_Type", "[ZoneType] = UPPER(LTRIM(RTRIM([ZoneType]))) AND LEN([ZoneType]) > 0");
                table.ForeignKey(
                    name: "FK_WarehouseZones_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WarehouseAisles",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ZoneId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, collation: "Latin1_General_100_CI_AS"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseAisles", x => x.Id);
                table.CheckConstraint("CK_WarehouseAisles_Code", "[Code] = UPPER(LTRIM(RTRIM([Code]))) AND LEN([Code]) > 0");
                table.ForeignKey(
                    name: "FK_WarehouseAisles_WarehouseZones_ZoneId",
                    column: x => x.ZoneId,
                    principalTable: "WarehouseZones",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WarehouseRacks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AisleId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, collation: "Latin1_General_100_CI_AS"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseRacks", x => x.Id);
                table.CheckConstraint("CK_WarehouseRacks_Code", "[Code] = UPPER(LTRIM(RTRIM([Code]))) AND LEN([Code]) > 0");
                table.ForeignKey(
                    name: "FK_WarehouseRacks_WarehouseAisles_AisleId",
                    column: x => x.AisleId,
                    principalTable: "WarehouseAisles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "WarehouseRackLevels",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RackId = table.Column<int>(type: "int", nullable: false),
                LevelNo = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarehouseRackLevels", x => x.Id);
                table.CheckConstraint("CK_WarehouseRackLevels_LevelNo", "[LevelNo] > 0");
                table.ForeignKey(
                    name: "FK_WarehouseRackLevels_WarehouseRacks_RackId",
                    column: x => x.RackId,
                    principalTable: "WarehouseRacks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<string>(
            name: "Barcode",
            table: "WarehouseLocations",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PickPriority",
            table: "WarehouseLocations",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PutawayPriority",
            table: "WarehouseLocations",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RackLevelId",
            table: "WarehouseLocations",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ZoneId",
            table: "WarehouseLocations",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseZones_WarehouseId_Code",
            table: "WarehouseZones",
            columns: new[] { "WarehouseId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseAisles_ZoneId_Code",
            table: "WarehouseAisles",
            columns: new[] { "ZoneId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseRacks_AisleId_Code",
            table: "WarehouseRacks",
            columns: new[] { "AisleId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseRackLevels_RackId_LevelNo",
            table: "WarehouseRackLevels",
            columns: new[] { "RackId", "LevelNo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseLocations_Barcode",
            table: "WarehouseLocations",
            column: "Barcode");

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseLocations_RackLevelId",
            table: "WarehouseLocations",
            column: "RackLevelId");

        migrationBuilder.CreateIndex(
            name: "IX_WarehouseLocations_ZoneId",
            table: "WarehouseLocations",
            column: "ZoneId");

        migrationBuilder.AddForeignKey(
            name: "FK_WarehouseLocations_WarehouseRackLevels_RackLevelId",
            table: "WarehouseLocations",
            column: "RackLevelId",
            principalTable: "WarehouseRackLevels",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_WarehouseLocations_WarehouseZones_ZoneId",
            table: "WarehouseLocations",
            column: "ZoneId",
            principalTable: "WarehouseZones",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='warehouse_zone.manage')
    INSERT INTO Permissions (Code, Description, CreatedAt)
    VALUES ('warehouse_zone.manage', N'Quản lý cấu trúc khu vực kho', SYSUTCDATETIME());

INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt, GrantedByUserId)
SELECT r.Id, p.Id, SYSUTCDATETIME(), NULL
FROM Roles r
JOIN Permissions p ON p.Code='warehouse_zone.manage'
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (
      SELECT 1 FROM RolePermissions rp
      WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id
  );
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code='warehouse_zone.manage';
DELETE FROM Permissions WHERE Code='warehouse_zone.manage';
""");

        migrationBuilder.DropForeignKey(
            name: "FK_WarehouseLocations_WarehouseRackLevels_RackLevelId",
            table: "WarehouseLocations");
        migrationBuilder.DropForeignKey(
            name: "FK_WarehouseLocations_WarehouseZones_ZoneId",
            table: "WarehouseLocations");

        migrationBuilder.DropIndex(name: "IX_WarehouseLocations_Barcode", table: "WarehouseLocations");
        migrationBuilder.DropIndex(name: "IX_WarehouseLocations_RackLevelId", table: "WarehouseLocations");
        migrationBuilder.DropIndex(name: "IX_WarehouseLocations_ZoneId", table: "WarehouseLocations");

        migrationBuilder.DropColumn(name: "Barcode", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "PickPriority", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "PutawayPriority", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "RackLevelId", table: "WarehouseLocations");
        migrationBuilder.DropColumn(name: "ZoneId", table: "WarehouseLocations");

        migrationBuilder.DropTable(name: "WarehouseRackLevels");
        migrationBuilder.DropTable(name: "WarehouseRacks");
        migrationBuilder.DropTable(name: "WarehouseAisles");
        migrationBuilder.DropTable(name: "WarehouseZones");
    }
}
