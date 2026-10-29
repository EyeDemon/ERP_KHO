using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261022120000_Wave48To51FullFiveLayerClosure")]
public sealed class Wave48To51FullFiveLayerClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.InventoryStocks', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InventoryStocks', 'AllocatedQuantity') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD AllocatedQuantity decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_AllocatedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.InventoryStocks', 'LockedQuantity') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD LockedQuantity decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_LockedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.InventoryStocks', 'HandlingUnitId') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD HandlingUnitId int NULL;
    IF COL_LENGTH('dbo.InventoryStocks', 'OwnerId') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD OwnerId int NULL;
    IF COL_LENGTH('dbo.InventoryStocks', 'MovingAverageCost') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD MovingAverageCost decimal(19,4) NOT NULL CONSTRAINT DF_InventoryStocks_MovingAverageCost DEFAULT(0);
    IF COL_LENGTH('dbo.InventoryStocks', 'GrainHash') IS NULL
        ALTER TABLE dbo.InventoryStocks ADD GrainHash varbinary(32) NULL;
    IF COL_LENGTH('dbo.InventoryStocks', 'CanonicalHandlingUnitId') IS NULL
        EXEC(N'ALTER TABLE dbo.InventoryStocks ADD CanonicalHandlingUnitId AS COALESCE([HandlingUnitId], 0) PERSISTED;');
    IF COL_LENGTH('dbo.InventoryStocks', 'CanonicalOwnerId') IS NULL
        EXEC(N'ALTER TABLE dbo.InventoryStocks ADD CanonicalOwnerId AS COALESCE([OwnerId], 0) PERSISTED;');

    EXEC(N'
        UPDATE dbo.InventoryStocks
        SET GrainHash = HASHBYTES(''SHA2_256'', CONVERT(varchar(256), CONCAT(
            TenantId, ''|'',
            WarehouseId, ''|'',
            ProductId, ''|'',
            Status, ''|'',
            ISNULL(LocationId, -1), ''|'',
            ISNULL(LotId, -1), ''|'',
            ISNULL(SerialId, -1), ''|'',
            ISNULL(HandlingUnitId, -1), ''|'',
            ISNULL(OwnerId, -1)
        )))
        WHERE GrainHash IS NULL OR DATALENGTH(GrainHash) <> 32;
    ');

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks')
          AND name = N'GrainHash'
          AND is_nullable = 1
    )
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks') AND name = N'IX_InventoryStocks_Tenant_GrainHash')
            EXEC(N'DROP INDEX IX_InventoryStocks_Tenant_GrainHash ON dbo.InventoryStocks;');
        EXEC(N'ALTER TABLE dbo.InventoryStocks ALTER COLUMN GrainHash varbinary(32) NOT NULL;');
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks') AND name = N'IX_InventoryStocks_Tenant_GrainHash')
        EXEC(N'CREATE UNIQUE INDEX IX_InventoryStocks_Tenant_GrainHash ON dbo.InventoryStocks (TenantId, GrainHash);');

    IF NOT EXISTS (
        SELECT 1 FROM sys.index_columns ic
        JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID('dbo.InventoryStocks')
          AND i.name = 'IX_InventoryStocks_CanonicalBucket'
          AND c.name = 'CanonicalHandlingUnitId'
    )
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InventoryStocks') AND name = 'IX_InventoryStocks_CanonicalBucket')
            EXEC(N'DROP INDEX IX_InventoryStocks_CanonicalBucket ON dbo.InventoryStocks;');
        EXEC(N'CREATE UNIQUE INDEX IX_InventoryStocks_CanonicalBucket ON dbo.InventoryStocks (TenantId, ProductId, WarehouseId, Status, CanonicalLocationId, CanonicalLotId, CanonicalSerialId, CanonicalHandlingUnitId, CanonicalOwnerId);');
    END;
END;

IF OBJECT_ID(N'dbo.PutawayTasks', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PutawayTasks', 'TaskType') IS NULL
        ALTER TABLE dbo.PutawayTasks ADD TaskType nvarchar(32) NOT NULL CONSTRAINT DF_PutawayTasks_TaskType DEFAULT(N'PutawayTask');
END;

IF OBJECT_ID(N'dbo.InventoryTransactions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.InventoryTransactions', 'HandlingUnitId') IS NULL
        ALTER TABLE dbo.InventoryTransactions ADD HandlingUnitId int NULL;
    IF COL_LENGTH('dbo.InventoryTransactions', 'OwnerId') IS NULL
        ALTER TABLE dbo.InventoryTransactions ADD OwnerId int NULL;
    IF COL_LENGTH('dbo.InventoryTransactions', 'UnitCost') IS NULL
        ALTER TABLE dbo.InventoryTransactions ADD UnitCost decimal(19,4) NOT NULL CONSTRAINT DF_InventoryTransactions_UnitCost DEFAULT(0);
    IF COL_LENGTH('dbo.InventoryTransactions', 'SourceDocumentLineId') IS NULL
        ALTER TABLE dbo.InventoryTransactions ADD SourceDocumentLineId int NULL;
END;

IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Products', 'MovingAverageCost') IS NULL
        ALTER TABLE dbo.Products ADD MovingAverageCost decimal(19,4) NOT NULL CONSTRAINT DF_Products_MovingAverageCost DEFAULT(0);
END;

IF OBJECT_ID(N'dbo.StockAllocations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.StockAllocations', 'HandlingUnitId') IS NULL
        ALTER TABLE dbo.StockAllocations ADD HandlingUnitId int NULL;
    IF COL_LENGTH('dbo.StockAllocations', 'OwnerId') IS NULL
        ALTER TABLE dbo.StockAllocations ADD OwnerId int NULL;
END;

IF OBJECT_ID(N'dbo.StockReservations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.StockReservations', 'BaseUomIdSnapshot') IS NULL
        ALTER TABLE dbo.StockReservations ADD BaseUomIdSnapshot int NOT NULL CONSTRAINT DF_StockReservations_BaseUomIdSnapshot DEFAULT(1);
    IF COL_LENGTH('dbo.StockReservations', 'BaseUomCodeSnapshot') IS NULL
        ALTER TABLE dbo.StockReservations ADD BaseUomCodeSnapshot nvarchar(32) NOT NULL CONSTRAINT DF_StockReservations_BaseUomCodeSnapshot DEFAULT(N'THÙNG');
    IF COL_LENGTH('dbo.StockReservations', 'BaseUomDecimalPlacesSnapshot') IS NULL
        ALTER TABLE dbo.StockReservations ADD BaseUomDecimalPlacesSnapshot int NOT NULL CONSTRAINT DF_StockReservations_BaseUomDecimalPlacesSnapshot DEFAULT(0);
END;

IF OBJECT_ID(N'dbo.ExportReceipts', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ExportReceipts', 'MarketplaceOrderId') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD MarketplaceOrderId nvarchar(64) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'CarrierCode') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD CarrierCode nvarchar(32) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'TrackingNumber') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD TrackingNumber nvarchar(64) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'AwbLabelUrl') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD AwbLabelUrl nvarchar(500) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'AwbPrintCount') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD AwbPrintCount int NOT NULL CONSTRAINT DF_ExportReceipts_AwbPrintCount DEFAULT(0);
    IF COL_LENGTH('dbo.ExportReceipts', 'AwbFirstPrintedAtUtc') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD AwbFirstPrintedAtUtc datetime2 NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'MarketplaceShipByUtc') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD MarketplaceShipByUtc datetime2 NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'EInvoiceStatus') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD EInvoiceStatus nvarchar(32) NULL CONSTRAINT DF_ExportReceipts_EInvoiceStatus DEFAULT(N'PENDING');
    IF COL_LENGTH('dbo.ExportReceipts', 'EInvoiceProvider') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD EInvoiceProvider nvarchar(32) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'EInvoiceNumber') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD EInvoiceNumber nvarchar(64) NULL;
    IF COL_LENGTH('dbo.ExportReceipts', 'BuyerTaxCode') IS NULL
        ALTER TABLE dbo.ExportReceipts ADD BuyerTaxCode nvarchar(32) NULL;
END;

IF OBJECT_ID(N'dbo.ImportReceiptDetails', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'ObservedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD ObservedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_ObservedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'BaseObservedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD BaseObservedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_BaseObservedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'DoorRejectedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD DoorRejectedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_DoorRejectedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'BaseDoorRejectedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD BaseDoorRejectedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_BaseDoorRejectedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'FinalReceivedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD FinalReceivedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_FinalReceivedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'BaseFinalReceivedQuantity') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD BaseFinalReceivedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_ImportReceiptDetails_BaseFinalReceivedQuantity DEFAULT(0);
    IF COL_LENGTH('dbo.ImportReceiptDetails', 'FinalResolutionVersionId') IS NULL
        ALTER TABLE dbo.ImportReceiptDetails ADD FinalResolutionVersionId int NULL;
END;

IF OBJECT_ID(N'dbo.SalesOrderLines', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.SalesOrderLines', 'IsFreeGift') IS NULL
        ALTER TABLE dbo.SalesOrderLines ADD IsFreeGift bit NOT NULL CONSTRAINT DF_SalesOrderLines_IsFreeGift DEFAULT(0);
    IF COL_LENGTH('dbo.SalesOrderLines', 'ParentBundleProductId') IS NULL
        ALTER TABLE dbo.SalesOrderLines ADD ParentBundleProductId int NULL;
END;

IF OBJECT_ID(N'dbo.ExportReceiptDetails', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ExportReceiptDetails', 'IsFreeGift') IS NULL
        ALTER TABLE dbo.ExportReceiptDetails ADD IsFreeGift bit NOT NULL CONSTRAINT DF_ExportReceiptDetails_IsFreeGift DEFAULT(0);
    IF COL_LENGTH('dbo.ExportReceiptDetails', 'ParentBundleProductId') IS NULL
        ALTER TABLE dbo.ExportReceiptDetails ADD ParentBundleProductId int NULL;
END;

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.AuditLogs', 'ImpersonatedBySupportUserId') IS NULL
        ALTER TABLE dbo.AuditLogs ADD ImpersonatedBySupportUserId int NULL;
END;

IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.Users SET TenantId = 1 WHERE Username = N'qa_admin' AND TenantId <> 1;
END;

IF OBJECT_ID(N'dbo.MarketplaceShops', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MarketplaceShops (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketplaceShops PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_MarketplaceShops_TenantId DEFAULT(1),
        ChannelCode nvarchar(32) NOT NULL,
        ShopCode nvarchar(64) NOT NULL,
        ShopName nvarchar(200) NOT NULL,
        DefaultWarehouseId int NOT NULL,
        ReserveRatioPct decimal(5,2) NOT NULL CONSTRAINT DF_MarketplaceShops_ReserveRatioPct DEFAULT(100),
        SafetyBufferQty decimal(19,4) NOT NULL CONSTRAINT DF_MarketplaceShops_SafetyBufferQty DEFAULT(0),
        LowStockThreshold decimal(19,4) NOT NULL CONSTRAINT DF_MarketplaceShops_LowStockThreshold DEFAULT(5),
        IsPrimaryShop bit NOT NULL CONSTRAINT DF_MarketplaceShops_IsPrimaryShop DEFAULT(0),
        EncryptedAccessToken nvarchar(500) NULL,
        EncryptedRefreshToken nvarchar(500) NULL,
        TokenExpiresAtUtc datetime2 NULL,
        WebhookSecret nvarchar(128) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_MarketplaceShops_IsActive DEFAULT(1),
        CreatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_MarketplaceShops_TenantId_ChannelCode_ShopCode ON dbo.MarketplaceShops(TenantId, ChannelCode, ShopCode);
END;

IF OBJECT_ID(N'dbo.ChannelSkuMappings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChannelSkuMappings (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChannelSkuMappings PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_ChannelSkuMappings_TenantId DEFAULT(1),
        ShopId int NOT NULL,
        ChannelItemId nvarchar(64) NOT NULL,
        ChannelVariationId nvarchar(64) NULL,
        ChannelSkuCode nvarchar(100) NOT NULL,
        InternalProductId int NOT NULL,
        ConversionFactor decimal(19,4) NOT NULL CONSTRAINT DF_ChannelSkuMappings_ConversionFactor DEFAULT(1),
        LastPushedQty decimal(19,4) NOT NULL CONSTRAINT DF_ChannelSkuMappings_LastPushedQty DEFAULT(0),
        LastPushedAtUtc datetime2 NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ChannelSkuMappings_IsActive DEFAULT(1)
    );
    CREATE UNIQUE INDEX UX_ChannelSkuMappings_TenantId_ShopId_ChannelSkuCode ON dbo.ChannelSkuMappings(TenantId, ShopId, ChannelSkuCode);
END;

IF OBJECT_ID(N'dbo.HandoverManifests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverManifests (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandoverManifests PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_HandoverManifests_TenantId DEFAULT(1),
        ManifestCode nvarchar(64) NOT NULL,
        WarehouseId int NOT NULL,
        CarrierCode nvarchar(32) NOT NULL,
        VehiclePlate nvarchar(32) NULL,
        DriverName nvarchar(128) NULL,
        DriverPhone nvarchar(32) NULL,
        Status nvarchar(32) NOT NULL,
        TotalPackages int NOT NULL CONSTRAINT DF_HandoverManifests_TotalPackages DEFAULT(0),
        CreatedBy int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        SealedBy int NULL,
        SealedAtUtc datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_HandoverManifests_TenantId_ManifestCode ON dbo.HandoverManifests(TenantId, ManifestCode);
END;

IF OBJECT_ID(N'dbo.HandoverManifestLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverManifestLines (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandoverManifestLines PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_HandoverManifestLines_TenantId DEFAULT(1),
        ManifestId int NOT NULL,
        ExportReceiptId int NOT NULL,
        TrackingNumber nvarchar(64) NOT NULL,
        MarketplaceOrderId nvarchar(64) NULL,
        ScannedBy int NOT NULL,
        ScannedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_HandoverManifestLines_TenantId_TrackingNumber ON dbo.HandoverManifestLines(TenantId, TrackingNumber);
END;

IF OBJECT_ID(N'dbo.SaasPlans', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SaasPlans (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaasPlans PRIMARY KEY,
        PlanCode nvarchar(32) NOT NULL,
        PlanName nvarchar(128) NOT NULL,
        MonthlyOrderQuota int NOT NULL,
        MaxWarehouses int NOT NULL,
        MaxUsers int NOT NULL,
        MonthlyPriceVnd decimal(19,4) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_SaasPlans_IsActive DEFAULT(1)
    );
    CREATE UNIQUE INDEX UX_SaasPlans_PlanCode ON dbo.SaasPlans(PlanCode);
END;

IF OBJECT_ID(N'dbo.TenantSubscriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TenantSubscriptions (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantSubscriptions PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_TenantSubscriptions_TenantId DEFAULT(1),
        PlanCode nvarchar(32) NOT NULL,
        Status nvarchar(32) NOT NULL,
        MonthlyOrderQuota int NOT NULL,
        CurrentMonthOrderCount int NOT NULL,
        MaxWarehouses int NOT NULL,
        MaxUsers int NOT NULL,
        TrialEndsAtUtc datetime2 NULL,
        CurrentPeriodStartUtc datetime2 NOT NULL,
        CurrentPeriodEndUtc datetime2 NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_TenantSubscriptions_TenantId ON dbo.TenantSubscriptions(TenantId);
END;

IF OBJECT_ID(N'dbo.SaasBillingInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SaasBillingInvoices (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaasBillingInvoices PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_SaasBillingInvoices_TenantId DEFAULT(1),
        InvoiceCode nvarchar(64) NOT NULL,
        PlanCode nvarchar(32) NOT NULL,
        AmountVnd decimal(19,4) NOT NULL,
        PaymentProvider nvarchar(32) NOT NULL,
        Status nvarchar(32) NOT NULL,
        QrPayload nvarchar(500) NULL,
        PaidAtUtc datetime2 NULL,
        CreatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_SaasBillingInvoices_TenantId_InvoiceCode ON dbo.SaasBillingInvoices(TenantId, InvoiceCode);
END;

IF OBJECT_ID(N'dbo.ShipmentFinancialLedgers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShipmentFinancialLedgers (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ShipmentFinancialLedgers PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_ShipmentFinancialLedgers_TenantId DEFAULT(1),
        ExportReceiptId int NULL,
        ShipmentId int NULL,
        CarrierCode nvarchar(32) NOT NULL,
        TrackingNumber nvarchar(64) NOT NULL,
        ExpectedCodAmount decimal(19,4) NOT NULL,
        ActualCodCollected decimal(19,4) NOT NULL,
        EstimatedShippingFee decimal(19,4) NOT NULL,
        ActualShippingFee decimal(19,4) NOT NULL,
        PlatformCommissionFee decimal(19,4) NOT NULL,
        ReturnFee decimal(19,4) NOT NULL,
        NetPayoutAmount decimal(19,4) NOT NULL,
        FeeOverchargeVariance decimal(19,4) NOT NULL,
        ReconciliationStatus nvarchar(32) NOT NULL,
        SettledAtUtc datetime2 NULL,
        CreatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_ShipmentFinancialLedgers_TenantId_TrackingNumber ON dbo.ShipmentFinancialLedgers(TenantId, TrackingNumber);
END;

IF OBJECT_ID(N'dbo.IntegrationOutboxMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationOutboxMessages (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationOutboxMessages PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_IntegrationOutboxMessages_TenantId DEFAULT(1),
        EventId nvarchar(64) NOT NULL,
        EventType nvarchar(100) NOT NULL,
        AggregateType nvarchar(64) NOT NULL,
        AggregateId int NULL,
        PayloadJson nvarchar(max) NOT NULL,
        Status nvarchar(32) NOT NULL,
        RetryCount int NOT NULL CONSTRAINT DF_IntegrationOutboxMessages_RetryCount DEFAULT(0),
        LastError nvarchar(1000) NULL,
        CreatedAt datetime2 NOT NULL,
        NextRetryAt datetime2 NULL,
        PublishedAt datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_IntegrationOutboxMessages_TenantId_EventId ON dbo.IntegrationOutboxMessages(TenantId, EventId);
    CREATE INDEX IX_IntegrationOutboxMessages_TenantId_Status_NextRetryAt ON dbo.IntegrationOutboxMessages(TenantId, Status, NextRetryAt);
END;

IF OBJECT_ID(N'dbo.IntegrationInboxMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationInboxMessages (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationInboxMessages PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_IntegrationInboxMessages_TenantId DEFAULT(1),
        SourceSystem nvarchar(64) NOT NULL,
        ExternalMessageId nvarchar(128) NOT NULL,
        EventType nvarchar(100) NOT NULL,
        PayloadJson nvarchar(max) NOT NULL,
        Status nvarchar(32) NOT NULL,
        ReceivedAt datetime2 NOT NULL,
        ProcessedAt datetime2 NULL,
        ErrorMessage nvarchar(1000) NULL
    );
    CREATE UNIQUE INDEX UX_IntegrationInboxMessages_TenantId_SourceSystem_ExternalMessageId ON dbo.IntegrationInboxMessages(TenantId, SourceSystem, ExternalMessageId);
END;

IF OBJECT_ID(N'dbo.ProductBundleComponents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductBundleComponents (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductBundleComponents PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_ProductBundleComponents_TenantId DEFAULT(1),
        ParentProductId int NOT NULL,
        ComponentProductId int NOT NULL,
        ComponentQuantity decimal(19,4) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ProductBundleComponents_IsActive DEFAULT(1),
        CreatedAt datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_ProductBundleComponents_TenantId_Parent_Component ON dbo.ProductBundleComponents(TenantId, ParentProductId, ComponentProductId);
END;

IF OBJECT_ID(N'dbo.VnAdministrativeUnits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VnAdministrativeUnits (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_VnAdministrativeUnits PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_VnAdministrativeUnits_TenantId DEFAULT(1),
        ProvinceCode nvarchar(32) NOT NULL,
        ProvinceName nvarchar(128) NOT NULL,
        DistrictCode nvarchar(32) NOT NULL,
        DistrictName nvarchar(128) NOT NULL,
        WardCode nvarchar(32) NOT NULL,
        WardName nvarchar(128) NOT NULL,
        Keywords nvarchar(500) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_VnAdministrativeUnits_IsActive DEFAULT(1),
        CreatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_VnAdministrativeUnits_TenantId_Codes ON dbo.VnAdministrativeUnits(TenantId, ProvinceCode, DistrictCode, WardCode);
END;

IF OBJECT_ID(N'dbo.CarrierLocationMappings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CarrierLocationMappings (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CarrierLocationMappings PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_CarrierLocationMappings_TenantId DEFAULT(1),
        AdministrativeUnitId int NOT NULL,
        CarrierCode nvarchar(32) NOT NULL,
        CarrierProvinceId nvarchar(64) NOT NULL,
        CarrierDistrictId nvarchar(64) NOT NULL,
        CarrierWardCode nvarchar(64) NOT NULL,
        SortingHubCode nvarchar(64) NOT NULL,
        RouteCode nvarchar(64) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_CarrierLocationMappings_IsActive DEFAULT(1)
    );
    CREATE UNIQUE INDEX UX_CarrierLocationMappings_TenantId_AdminUnit_Carrier ON dbo.CarrierLocationMappings(TenantId, AdministrativeUnitId, CarrierCode);
END;

IF OBJECT_ID(N'dbo.PackingVideoEvidences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingVideoEvidences (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PackingVideoEvidences PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_PackingVideoEvidences_TenantId DEFAULT(1),
        PackingSessionId int NULL,
        ExportReceiptId int NULL,
        TrackingNumber nvarchar(64) NOT NULL,
        OrderCode nvarchar(64) NOT NULL,
        CameraStationId nvarchar(64) NOT NULL,
        StartedAtUtc datetime2 NOT NULL,
        EndedAtUtc datetime2 NOT NULL,
        DurationSeconds int NOT NULL,
        VideoHashSha256 nvarchar(128) NOT NULL,
        StorageUrl nvarchar(500) NOT NULL,
        RecordedByUserId int NULL,
        RecordedAtUtc datetime2 NOT NULL
    );
    CREATE INDEX IX_PackingVideoEvidences_TenantId_TrackingNumber ON dbo.PackingVideoEvidences(TenantId, TrackingNumber);
END;

IF OBJECT_ID(N'dbo.PromotionGiftRules', N'U') IS NOT NULL AND COL_LENGTH('dbo.PromotionGiftRules', 'MinOrderAmount') IS NULL
BEGIN
    DROP TABLE dbo.PromotionGiftRules;
END;

IF OBJECT_ID(N'dbo.PromotionGiftRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromotionGiftRules (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromotionGiftRules PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_PromotionGiftRules_TenantId DEFAULT(1),
        RuleCode nvarchar(64) NOT NULL,
        RuleName nvarchar(200) NOT NULL,
        WarehouseId int NULL,
        TriggerProductId int NULL,
        MinOrderAmount decimal(18,2) NOT NULL,
        MinTriggerQuantity decimal(18,4) NOT NULL,
        GiftProductId int NOT NULL,
        GiftQuantity decimal(18,4) NOT NULL,
        GiftUnitPrice decimal(18,2) NOT NULL,
        AutoSkipWhenOutOfStock bit NOT NULL CONSTRAINT DF_PromotionGiftRules_AutoSkip DEFAULT(1),
        IsActive bit NOT NULL CONSTRAINT DF_PromotionGiftRules_IsActive DEFAULT(1),
        CreatedAtUtc datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX UX_PromotionGiftRules_TenantId_RuleCode ON dbo.PromotionGiftRules(TenantId, RuleCode);
END;

IF OBJECT_ID(N'dbo.SupportImpersonationSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupportImpersonationSessions (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupportImpersonationSessions PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_SupportImpersonationSessions_TenantId DEFAULT(1),
        SessionCode nvarchar(64) NOT NULL,
        SupportTicketCode nvarchar(64) NOT NULL,
        GrantedByUserId int NOT NULL,
        SupportAgentUserId int NOT NULL,
        ReadOnlyScope bit NOT NULL CONSTRAINT DF_SupportImpersonationSessions_ReadOnly DEFAULT(1),
        Status nvarchar(32) NOT NULL,
        TokenHash nvarchar(256) NOT NULL,
        GrantedAtUtc datetime2 NOT NULL,
        ExpiresAtUtc datetime2 NOT NULL,
        RevokedAtUtc datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_SupportImpersonationSessions_TenantId_SessionCode ON dbo.SupportImpersonationSessions(TenantId, SessionCode);
END;

IF OBJECT_ID(N'dbo.CustomerReturns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerReturns (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerReturns PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_CustomerReturns_TenantId DEFAULT(1),
        ReturnCode nvarchar(64) NOT NULL,
        WarehouseId int NOT NULL,
        SalesOrderId int NULL,
        ExportReceiptId int NULL,
        TrackingNumber nvarchar(64) NULL,
        OrderNumber nvarchar(64) NULL,
        ReturnReason nvarchar(500) NOT NULL,
        Status nvarchar(32) NOT NULL,
        CreatedBy int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CompletedAtUtc datetime2 NULL
    );
    CREATE UNIQUE INDEX UX_CustomerReturns_TenantId_ReturnCode ON dbo.CustomerReturns(TenantId, ReturnCode);
END;

IF OBJECT_ID(N'dbo.CustomerReturnLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerReturnLines (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerReturnLines PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_CustomerReturnLines_TenantId DEFAULT(1),
        CustomerReturnId int NOT NULL,
        ProductId int NOT NULL,
        QuarantineLocationId int NULL,
        TargetLocationId int NULL,
        Quantity decimal(18,4) NOT NULL,
        UnitCost decimal(18,4) NOT NULL,
        Condition nvarchar(32) NOT NULL,
        Disposition nvarchar(32) NOT NULL,
        PackagingProductId int NULL,
        PackagingQuantity decimal(18,4) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.StocktakeAdjustmentProposals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StocktakeAdjustmentProposals (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StocktakeAdjustmentProposals PRIMARY KEY,
        TenantId int NOT NULL CONSTRAINT DF_StocktakeAdjustmentProposals_TenantId DEFAULT(1),
        ProposalCode nvarchar(64) NOT NULL,
        WarehouseId int NOT NULL,
        StocktakeId int NULL,
        ProductId int NOT NULL,
        LocationId int NULL,
        ReasonCode nvarchar(32) NOT NULL,
        SystemQuantity decimal(18,4) NOT NULL,
        CountedQuantity decimal(18,4) NOT NULL,
        DeltaQuantity decimal(18,4) NOT NULL,
        UnitCost decimal(18,4) NOT NULL,
        VariancePct decimal(18,4) NOT NULL,
        VarianceValue decimal(18,2) NOT NULL,
        IsAutoApproved bit NOT NULL,
        Status nvarchar(32) NOT NULL,
        CreatedBy int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        ApprovedBy int NULL,
        ApprovedAtUtc datetime2 NULL,
        Note nvarchar(500) NULL
    );
    CREATE UNIQUE INDEX UX_StocktakeAdjustmentProposals_TenantId_ProposalCode ON dbo.StocktakeAdjustmentProposals(TenantId, ProposalCode);
END;

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RolePermissions', N'U') IS NOT NULL
BEGIN
    EXEC(N'
        IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N''inventory.cost.read'')
        BEGIN
            INSERT INTO dbo.Permissions (Code, Description, CreatedAt)
            VALUES (N''inventory.cost.read'', N''Xem đơn giá, giá vốn bình quân và giá trị tồn kho'', SYSUTCDATETIME());
        END;

        DECLARE @costPermId int = (SELECT TOP 1 Id FROM dbo.Permissions WHERE Code = N''inventory.cost.read'');
        IF @costPermId IS NOT NULL
        BEGIN
            INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
            SELECT r.Id, @costPermId, SYSUTCDATETIME()
            FROM dbo.Roles r
            WHERE r.RoleName IN (N''Admin'', N''Manager'', N''Accountant'')
              AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = @costPermId);
        END;
    ');
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.InventoryStocks', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryStocks') AND name = N'IX_InventoryStocks_Tenant_GrainHash')
    DROP INDEX IX_InventoryStocks_Tenant_GrainHash ON dbo.InventoryStocks;
IF COL_LENGTH('dbo.InventoryStocks', 'GrainHash') IS NOT NULL
    ALTER TABLE dbo.InventoryStocks ALTER COLUMN GrainHash varbinary(32) NULL;

IF OBJECT_ID(N'dbo.ShipmentFinancialLedgers', N'U') IS NOT NULL DROP TABLE dbo.ShipmentFinancialLedgers;
IF OBJECT_ID(N'dbo.HandoverManifestLines', N'U') IS NOT NULL DROP TABLE dbo.HandoverManifestLines;
IF OBJECT_ID(N'dbo.HandoverManifests', N'U') IS NOT NULL DROP TABLE dbo.HandoverManifests;
IF OBJECT_ID(N'dbo.PackingVideoEvidences', N'U') IS NOT NULL DROP TABLE dbo.PackingVideoEvidences;
IF OBJECT_ID(N'dbo.CustomerReturnLines', N'U') IS NOT NULL DROP TABLE dbo.CustomerReturnLines;
IF OBJECT_ID(N'dbo.CustomerReturns', N'U') IS NOT NULL DROP TABLE dbo.CustomerReturns;
IF OBJECT_ID(N'dbo.StocktakeAdjustmentProposals', N'U') IS NOT NULL DROP TABLE dbo.StocktakeAdjustmentProposals;
IF OBJECT_ID(N'dbo.PromotionGiftRules', N'U') IS NOT NULL DROP TABLE dbo.PromotionGiftRules;
IF OBJECT_ID(N'dbo.SupportImpersonationSessions', N'U') IS NOT NULL DROP TABLE dbo.SupportImpersonationSessions;
IF OBJECT_ID(N'dbo.CarrierLocationMappings', N'U') IS NOT NULL DROP TABLE dbo.CarrierLocationMappings;
IF OBJECT_ID(N'dbo.VnAdministrativeUnits', N'U') IS NOT NULL DROP TABLE dbo.VnAdministrativeUnits;
IF OBJECT_ID(N'dbo.ChannelSkuMappings', N'U') IS NOT NULL DROP TABLE dbo.ChannelSkuMappings;
IF OBJECT_ID(N'dbo.MarketplaceShops', N'U') IS NOT NULL DROP TABLE dbo.MarketplaceShops;
IF OBJECT_ID(N'dbo.ProductBundleComponents', N'U') IS NOT NULL DROP TABLE dbo.ProductBundleComponents;
IF OBJECT_ID(N'dbo.SaasBillingInvoices', N'U') IS NOT NULL DROP TABLE dbo.SaasBillingInvoices;
IF OBJECT_ID(N'dbo.TenantSubscriptions', N'U') IS NOT NULL DROP TABLE dbo.TenantSubscriptions;
IF OBJECT_ID(N'dbo.SaasPlans', N'U') IS NOT NULL DROP TABLE dbo.SaasPlans;
IF OBJECT_ID(N'dbo.IntegrationOutboxMessages', N'U') IS NOT NULL DROP TABLE dbo.IntegrationOutboxMessages;
IF OBJECT_ID(N'dbo.IntegrationInboxMessages', N'U') IS NOT NULL DROP TABLE dbo.IntegrationInboxMessages;
");
    }
}
