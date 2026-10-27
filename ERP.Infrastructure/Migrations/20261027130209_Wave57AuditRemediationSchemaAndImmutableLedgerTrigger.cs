using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave57AuditRemediationSchemaAndImmutableLedgerTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CountAttempt1Qty",
                table: "StocktakeDetails",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CountAttempt2Qty",
                table: "StocktakeDetails",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CountAttempt3Qty",
                table: "StocktakeDetails",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountAttemptCount",
                table: "StocktakeDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastCountedByUserId",
                table: "StocktakeDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecountReason",
                table: "StocktakeDetails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualOccurredAtUtc",
                table: "InventoryTransactions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAtUtc",
                table: "InventoryTransactions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.CreateTable(
                name: "PickingWaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    WaveCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PickingType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: true),
                    ReleasedTasksCount = table.Column<int>(type: "int", nullable: false),
                    TotalLinesCount = table.Column<int>(type: "int", nullable: false),
                    TotalRequestedQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingWaves", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PutToWallSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PickingTaskId = table.Column<int>(type: "int", nullable: false),
                    PickingTaskLineId = table.Column<int>(type: "int", nullable: true),
                    SlotIndex = table.Column<int>(type: "int", nullable: false),
                    SlotCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ToteBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SortedQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SlotStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastSortedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PutToWallSlots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThreePlBillingStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    StatementCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PeriodFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodToUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalOnHandQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalInboundQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalOutboundQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    StorageFeeAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    InboundHandlingFeeAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OutboundHandlingFeeAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalBillingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ReceivableInvoiceCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreePlBillingStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThreePlStorageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: true),
                    OwnerCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SnapshotCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DistinctSkuCount = table.Column<int>(type: "int", nullable: false),
                    OccupiedBinCount = table.Column<int>(type: "int", nullable: false),
                    TotalOnHandQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalVolumeCbm = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PalletEquivalentCount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DailyPalletRateVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DailyCbmRateVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DailyBinRateVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    EstimatedDailyStorageFeeVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    EstimatedMonthlyStorageFeeVnd = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CapturedByUserId = table.Column<int>(type: "int", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreePlStorageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingWaves_TenantId_WarehouseId_WaveCode",
                table: "PickingWaves",
                columns: new[] { "TenantId", "WarehouseId", "WaveCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PutToWallSlots_TenantId_PickingTaskId_SlotCode",
                table: "PutToWallSlots",
                columns: new[] { "TenantId", "PickingTaskId", "SlotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThreePlBillingStatements_TenantId_PartnerId_StatementCode",
                table: "ThreePlBillingStatements",
                columns: new[] { "TenantId", "PartnerId", "StatementCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ThreePlStorageSnapshots_TenantId_WarehouseId_SnapshotDateUtc",
                table: "ThreePlStorageSnapshots",
                columns: new[] { "TenantId", "WarehouseId", "SnapshotDateUtc" });

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly;

IF OBJECT_ID(N'dbo.InventoryTransactions', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.InventoryTransactions
    SET ActualOccurredAtUtc = TransactionDate,
        PostedAtUtc = TransactionDate
    WHERE ActualOccurredAtUtc IS NOT NULL;

    -- F-001 Remediation: Normalize historical VAS_DEKIT TransferIn rows (TransactionType = 5) on ProductId = 517 where TransferOut deducted LotId = 1002
    IF EXISTS (SELECT 1 FROM dbo.InventoryLots WHERE Id = 1002 AND ProductId = 517)
    BEGIN
        UPDATE dbo.InventoryTransactions
        SET LotId = 1002
        WHERE ProductId = 517
          AND ReferenceType = N'VAS_DEKIT'
          AND TransactionType = 5
          AND LotId IS NULL;
    END;
END;

IF OBJECT_ID(N'dbo.InventoryStocks', N'U') IS NOT NULL
BEGIN
    -- Upsert missing 9D bucket (TenantId=1, WarehouseId=1, LocationId=1 [TIEP-NHAN], ProductId=517, LotId=1002, Status=0 [Available])
    IF EXISTS (SELECT 1 FROM dbo.Products WHERE Id = 517)
       AND EXISTS (SELECT 1 FROM dbo.Warehouses WHERE Id = 1)
       AND EXISTS (SELECT 1 FROM dbo.WarehouseLocations WHERE Id = 1)
       AND EXISTS (SELECT 1 FROM dbo.InventoryLots WHERE Id = 1002 AND ProductId = 517)
    BEGIN
        DELETE FROM dbo.InventoryStocks
        WHERE TenantId = 1 AND WarehouseId = 1 AND LocationId = 1 AND ProductId = 517
          AND LotId = 1002 AND SerialId IS NULL AND HandlingUnitId IS NULL AND Status = 1 AND OwnerId IS NULL;

        IF NOT EXISTS (
            SELECT 1 FROM dbo.InventoryStocks
            WHERE TenantId = 1 AND WarehouseId = 1 AND LocationId = 1 AND ProductId = 517
              AND LotId = 1002 AND SerialId IS NULL AND HandlingUnitId IS NULL AND Status = 0 AND OwnerId IS NULL
        )
        BEGIN
            INSERT INTO dbo.InventoryStocks (
                TenantId, ProductId, WarehouseId, LocationId, LotId, SerialId, HandlingUnitId, OwnerId,
                Status, Quantity, ReservedQuantity, AllocatedQuantity, LockedQuantity, MovingAverageCost, LastUpdated, GrainHash
            )
            VALUES (
                1, 517, 1, 1, 1002, NULL, NULL, NULL,
                0, 60.0000, 0.0000, 0.0000, 0.0000, 420000.0000, SYSUTCDATETIME(),
                CAST(HASHBYTES('SHA2_256', CONVERT(varchar(256), '1|1|1|517|1002|-1|-1|0|-1')) AS varbinary(32))
            );
        END
        ELSE
        BEGIN
            UPDATE dbo.InventoryStocks
            SET Quantity = 60.0000,
                LastUpdated = SYSUTCDATETIME()
            WHERE TenantId = 1 AND WarehouseId = 1 AND LocationId = 1 AND ProductId = 517
              AND LotId = 1002 AND SerialId IS NULL AND HandlingUnitId IS NULL AND Status = 0 AND OwnerId IS NULL;
        END;

        -- Update (LocationId=1009, LotId=1002, Status=0) to match exact 9D ledger sum (+5 - 5 - 4 + 4 = 0.0000)
        UPDATE dbo.InventoryStocks
        SET Quantity = 0.0000,
            LastUpdated = SYSUTCDATETIME()
        WHERE TenantId = 1 AND WarehouseId = 1 AND LocationId = 1009 AND ProductId = 517
          AND LotId = 1002 AND SerialId IS NULL AND Status = 0;
    END;
END;

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly
ON dbo.InventoryTransactions
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Block all DELETE operations unconditionally
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        RAISERROR(''INV_LEDGER_IMMUTABLE (50001): InventoryTransactions is append-only.'', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Block all UPDATE operations except one-time CorrectiveTransactionId link (NULL -> NOT NULL) on Reversal rows (TransactionType = 10) with ZERO changes to any other column
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM inserted i
            INNER JOIN deleted d ON i.Id = d.Id
            WHERE d.TransactionType <> 10
               OR i.TransactionType <> 10
               OR d.CorrectiveTransactionId IS NOT NULL
               OR i.CorrectiveTransactionId IS NULL
               OR d.TenantId <> i.TenantId
               OR d.ProductId <> i.ProductId
               OR d.WarehouseId <> i.WarehouseId
               OR ISNULL(d.LocationId, -1) <> ISNULL(i.LocationId, -1)
               OR ISNULL(d.FromLocationId, -1) <> ISNULL(i.FromLocationId, -1)
               OR ISNULL(d.ToLocationId, -1) <> ISNULL(i.ToLocationId, -1)
               OR ISNULL(d.LotId, -1) <> ISNULL(i.LotId, -1)
               OR ISNULL(d.SerialId, -1) <> ISNULL(i.SerialId, -1)
               OR ISNULL(d.HandlingUnitId, -1) <> ISNULL(i.HandlingUnitId, -1)
               OR ISNULL(d.OwnerId, -1) <> ISNULL(i.OwnerId, -1)
               OR d.InventoryStatus <> i.InventoryStatus
               OR ISNULL(d.FromInventoryStatus, -1) <> ISNULL(i.FromInventoryStatus, -1)
               OR ISNULL(d.ToInventoryStatus, -1) <> ISNULL(i.ToInventoryStatus, -1)
               OR d.Quantity <> i.Quantity
               OR d.UnitCost <> i.UnitCost
               OR ISNULL(d.ReferenceId, -1) <> ISNULL(i.ReferenceId, -1)
               OR ISNULL(d.ReferenceType, N'''') <> ISNULL(i.ReferenceType, N'''')
               OR ISNULL(d.SourceDocumentLineId, -1) <> ISNULL(i.SourceDocumentLineId, -1)
               OR ISNULL(d.ReversalOfTransactionId, -1) <> ISNULL(i.ReversalOfTransactionId, -1)
               OR d.TransactionDate <> i.TransactionDate
               OR d.ActualOccurredAtUtc <> i.ActualOccurredAtUtc
               OR d.PostedAtUtc <> i.PostedAtUtc
               OR d.CreatedBy <> i.CreatedBy
               OR ISNULL(d.Note, N'''') <> ISNULL(i.Note, N'''')
               OR ISNULL(d.ReasonCode, N'''') <> ISNULL(i.ReasonCode, N'''')
        )
        BEGIN
            RAISERROR(''INV_LEDGER_IMMUTABLE (50001): InventoryTransactions is append-only.'', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;
    END;
END;
');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly;
");

            migrationBuilder.DropTable(
                name: "PickingWaves");

            migrationBuilder.DropTable(
                name: "PutToWallSlots");

            migrationBuilder.DropTable(
                name: "ThreePlBillingStatements");

            migrationBuilder.DropTable(
                name: "ThreePlStorageSnapshots");

            migrationBuilder.DropColumn(
                name: "CountAttempt1Qty",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "CountAttempt2Qty",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "CountAttempt3Qty",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "CountAttemptCount",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "LastCountedByUserId",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "RecountReason",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "ActualOccurredAtUtc",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "PostedAtUtc",
                table: "InventoryTransactions");
        }
    }
}
