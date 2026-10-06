using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006210000_AddShipmentDispatch")]
public sealed class AddShipmentDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DispatchedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DispatchedBy",
            table: "Shipments",
            type: "int",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EventKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                AggregateId = table.Column<int>(type: "int", nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_DispatchedBy",
            table: "Shipments",
            column: "DispatchedBy");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_EventKey",
            table: "OutboxMessages",
            column: "EventKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_ProcessedAtUtc_OccurredAtUtc",
            table: "OutboxMessages",
            columns: new[] { "ProcessedAtUtc", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_AggregateType_AggregateId",
            table: "OutboxMessages",
            columns: new[] { "AggregateType", "AggregateId" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId" },
            unique: true,
            filter: "[ReferenceType] = 'Shipment'");

        migrationBuilder.AddForeignKey(
            name: "FK_Shipments_Users_DispatchedBy",
            table: "Shipments",
            column: "DispatchedBy",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.dispatch')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.dispatch',N'Xác nhận Shipment rời kho và ghi SHIP ledger',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='shipment.dispatch'
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager','warehousestaff')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");

        migrationBuilder.Sql("""
CREATE OR ALTER PROCEDURE sp_GetInventoryInOutReport
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @WarehouseId INT = NULL,
    @ProductId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- TransactionType: Import=0, Export=1, AdjustmentIncrease=2,
    -- AdjustmentDecrease=3, TransferOut=4, TransferIn=5,
    -- TransferAdjustment=6, Ship=7.
    IF EXISTS
    (
        SELECT 1
        FROM InventoryTransactions t
        WHERE t.TransactionType NOT IN (0,1,2,3,4,5,6,7)
          AND (@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)
          AND (@ProductId IS NULL OR t.ProductId=@ProductId)
          AND (@ToDate IS NULL OR t.TransactionDate<@ToDate)
    )
        THROW 51000, 'Unsupported inventory transaction type in report range.', 1;

    SELECT
        p.Id AS ProductId,
        p.Code AS ProductCode,
        p.Name AS ProductName,
        ISNULL(u.Name,'') AS UnitName,
        w.Id AS WarehouseId,
        w.Name AS WarehouseName,
        CAST(ISNULL(SUM(CASE
            WHEN @FromDate IS NOT NULL AND t.TransactionDate<@FromDate THEN
                CASE
                    WHEN t.TransactionType IN (0,2,5) THEN t.Quantity
                    WHEN t.TransactionType IN (1,3,4,7) THEN -t.Quantity
                    ELSE 0
                END
            ELSE 0 END),0) AS DECIMAL(18,4)) AS OpeningQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=0 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS ImportQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=5 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS TransferInQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=2 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS AdjustmentIncreaseQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType IN (0,2,5) THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS InQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType IN (1,7) THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS ExportQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=4 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS TransferOutQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=3 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS AdjustmentDecreaseQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType IN (1,3,4,7) THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS OutQuantity
    INTO #TempReport
    FROM InventoryTransactions t
    JOIN Products p ON t.ProductId=p.Id
    LEFT JOIN Units u ON p.UnitId=u.Id
    JOIN Warehouses w ON t.WarehouseId=w.Id
    WHERE (@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR t.ProductId=@ProductId)
      AND (@ToDate IS NULL OR t.TransactionDate<@ToDate)
    GROUP BY p.Id,p.Code,p.Name,u.Name,w.Id,w.Name;

    SELECT *, CAST(OpeningQuantity+InQuantity-OutQuantity AS DECIMAL(18,4)) AS ClosingQuantity
    FROM #TempReport
    ORDER BY ProductCode,WarehouseName;

    DROP TABLE #TempReport;
END
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code='shipment.dispatch';
DELETE FROM Permissions WHERE Code='shipment.dispatch';
""");

        migrationBuilder.DropForeignKey(
            name: "FK_Shipments_Users_DispatchedBy",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_DispatchedBy",
            table: "Shipments");

        migrationBuilder.DropTable(name: "OutboxMessages");

        migrationBuilder.DropColumn(name: "DispatchedAt", table: "Shipments");
        migrationBuilder.DropColumn(name: "DispatchedBy", table: "Shipments");

        migrationBuilder.Sql("""
CREATE OR ALTER PROCEDURE sp_GetInventoryInOutReport
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @WarehouseId INT = NULL,
    @ProductId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1 FROM InventoryTransactions t
        WHERE t.TransactionType NOT IN (0,1,2,3,4,5,6)
          AND (@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)
          AND (@ProductId IS NULL OR t.ProductId=@ProductId)
          AND (@ToDate IS NULL OR t.TransactionDate<@ToDate)
    )
        THROW 51000, 'Unsupported inventory transaction type in report range.', 1;

    SELECT
        p.Id AS ProductId,p.Code AS ProductCode,p.Name AS ProductName,
        ISNULL(u.Name,'') AS UnitName,w.Id AS WarehouseId,w.Name AS WarehouseName,
        CAST(ISNULL(SUM(CASE WHEN @FromDate IS NOT NULL AND t.TransactionDate<@FromDate THEN CASE WHEN t.TransactionType IN (0,2,5) THEN t.Quantity WHEN t.TransactionType IN (1,3,4) THEN -t.Quantity ELSE 0 END ELSE 0 END),0) AS DECIMAL(18,4)) AS OpeningQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=0 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS ImportQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=5 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS TransferInQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=2 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS AdjustmentIncreaseQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType IN (0,2,5) THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS InQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=1 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS ExportQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=4 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS TransferOutQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType=3 THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS AdjustmentDecreaseQuantity,
        CAST(ISNULL(SUM(CASE WHEN (@FromDate IS NULL OR t.TransactionDate>=@FromDate) AND (@ToDate IS NULL OR t.TransactionDate<@ToDate) AND t.TransactionType IN (1,3,4) THEN t.Quantity ELSE 0 END),0) AS DECIMAL(18,4)) AS OutQuantity
    INTO #TempReport
    FROM InventoryTransactions t
    JOIN Products p ON t.ProductId=p.Id
    LEFT JOIN Units u ON p.UnitId=u.Id
    JOIN Warehouses w ON t.WarehouseId=w.Id
    WHERE (@WarehouseId IS NULL OR t.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR t.ProductId=@ProductId)
      AND (@ToDate IS NULL OR t.TransactionDate<@ToDate)
    GROUP BY p.Id,p.Code,p.Name,u.Name,w.Id,w.Name;

    SELECT *,CAST(OpeningQuantity+InQuantity-OutQuantity AS DECIMAL(18,4)) AS ClosingQuantity
    FROM #TempReport ORDER BY ProductCode,WarehouseName;
    DROP TABLE #TempReport;
END
""");
    }
}
