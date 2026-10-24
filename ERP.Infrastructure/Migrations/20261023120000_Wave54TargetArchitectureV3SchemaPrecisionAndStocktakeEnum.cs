using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

public partial class Wave54TargetArchitectureV3SchemaPrecisionAndStocktakeEnum : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Warehouses', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Warehouses', 'RowVersion') IS NULL
        ALTER TABLE dbo.Warehouses ADD RowVersion rowversion NOT NULL;
END;

IF OBJECT_ID(N'dbo.Stocktakes', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Stocktakes', 'RowVersion') IS NULL
        ALTER TABLE dbo.Stocktakes ADD RowVersion rowversion NOT NULL;
END;

IF OBJECT_ID(N'dbo.ShipmentFinancialLedgers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ShipmentFinancialLedgers', 'CodVariance') IS NULL
        ALTER TABLE dbo.ShipmentFinancialLedgers ADD CodVariance decimal(18,4) NOT NULL CONSTRAINT DF_ShipmentFinancialLedgers_CodVariance DEFAULT(0);
    IF COL_LENGTH('dbo.ShipmentFinancialLedgers', 'ShippingFeeVariance') IS NULL
        ALTER TABLE dbo.ShipmentFinancialLedgers ADD ShippingFeeVariance decimal(18,4) NOT NULL CONSTRAINT DF_ShipmentFinancialLedgers_ShippingFeeVariance DEFAULT(0);
    IF COL_LENGTH('dbo.ShipmentFinancialLedgers', 'SellerVoucherAmount') IS NULL
        ALTER TABLE dbo.ShipmentFinancialLedgers ADD SellerVoucherAmount decimal(18,4) NOT NULL CONSTRAINT DF_ShipmentFinancialLedgers_SellerVoucherAmount DEFAULT(0);
    IF COL_LENGTH('dbo.ShipmentFinancialLedgers', 'NetSettlementAmount') IS NULL
        ALTER TABLE dbo.ShipmentFinancialLedgers ADD NetSettlementAmount decimal(18,4) NOT NULL CONSTRAINT DF_ShipmentFinancialLedgers_NetSettlementAmount DEFAULT(0);
    IF COL_LENGTH('dbo.ShipmentFinancialLedgers', 'RowVersion') IS NULL
        ALTER TABLE dbo.ShipmentFinancialLedgers ADD RowVersion rowversion NOT NULL;

    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN ExpectedCodAmount decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN ActualCodCollected decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN EstimatedShippingFee decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN ActualShippingFee decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN PlatformCommissionFee decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN ReturnFee decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN NetPayoutAmount decimal(18,4) NOT NULL;
    ALTER TABLE dbo.ShipmentFinancialLedgers ALTER COLUMN FeeOverchargeVariance decimal(18,4) NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ShipmentFinancialLedgers') AND name = N'UX_ShipmentFinancialLedgers_TenantId_TrackingNumber')
        DROP INDEX UX_ShipmentFinancialLedgers_TenantId_TrackingNumber ON dbo.ShipmentFinancialLedgers;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ShipmentFinancialLedgers') AND name = N'IX_ShipmentFinancialLedgers_TenantId_TrackingNumber')
        CREATE UNIQUE INDEX IX_ShipmentFinancialLedgers_TenantId_TrackingNumber ON dbo.ShipmentFinancialLedgers (TenantId, TrackingNumber);
END;

IF OBJECT_ID(N'dbo.StocktakeAdjustmentProposals', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.StocktakeAdjustmentProposals', 'VarianceQuantity') IS NULL
        ALTER TABLE dbo.StocktakeAdjustmentProposals ADD VarianceQuantity decimal(18,4) NOT NULL CONSTRAINT DF_StocktakeAdjustmentProposals_VarianceQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.StocktakeAdjustmentProposals', 'RowVersion') IS NULL
        ALTER TABLE dbo.StocktakeAdjustmentProposals ADD RowVersion rowversion NOT NULL;

    ALTER TABLE dbo.StocktakeAdjustmentProposals ALTER COLUMN VarianceValue decimal(18,4) NOT NULL;
END;

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditLogs') AND name = N'IX_AuditLogs_TenantId_Timestamp_EntityName')
        CREATE INDEX IX_AuditLogs_TenantId_Timestamp_EntityName ON dbo.AuditLogs (TenantId, Timestamp, EntityName);
END;

IF OBJECT_ID(N'dbo.InventoryStocks', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks') AND name = N'IX_InventoryStocks_Tenant_GrainHash')
        EXEC(N'DROP INDEX IX_InventoryStocks_Tenant_GrainHash ON dbo.InventoryStocks;');

    EXEC(N'
        UPDATE dbo.InventoryStocks
        SET GrainHash = CAST(HASHBYTES(''SHA2_256'', CONVERT(varchar(256), CONCAT(
            TenantId, ''|'',
            WarehouseId, ''|'',
            ISNULL(LocationId, -1), ''|'',
            ProductId, ''|'',
            ISNULL(LotId, -1), ''|'',
            ISNULL(SerialId, -1), ''|'',
            ISNULL(HandlingUnitId, -1), ''|'',
            Status, ''|'',
            ISNULL(OwnerId, -1)
        ))) AS varbinary(32));
    ');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks') AND name = N'IX_InventoryStocks_Tenant_GrainHash')
        EXEC(N'CREATE UNIQUE INDEX IX_InventoryStocks_Tenant_GrainHash ON dbo.InventoryStocks (TenantId, GrainHash);');
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditLogs') AND name = N'IX_AuditLogs_TenantId_Timestamp_EntityName')
        DROP INDEX IX_AuditLogs_TenantId_Timestamp_EntityName ON dbo.AuditLogs;
END;
");
    }
}
