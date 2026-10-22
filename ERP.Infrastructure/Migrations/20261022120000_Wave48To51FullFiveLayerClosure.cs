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
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.StocktakeAdjustmentProposals', N'U') IS NOT NULL DROP TABLE dbo.StocktakeAdjustmentProposals;
IF OBJECT_ID(N'dbo.CustomerReturnLines', N'U') IS NOT NULL DROP TABLE dbo.CustomerReturnLines;
IF OBJECT_ID(N'dbo.CustomerReturns', N'U') IS NOT NULL DROP TABLE dbo.CustomerReturns;
IF OBJECT_ID(N'dbo.SupportImpersonationSessions', N'U') IS NOT NULL DROP TABLE dbo.SupportImpersonationSessions;
IF OBJECT_ID(N'dbo.PromotionGiftRules', N'U') IS NOT NULL DROP TABLE dbo.PromotionGiftRules;
IF OBJECT_ID(N'dbo.PackingVideoEvidences', N'U') IS NOT NULL DROP TABLE dbo.PackingVideoEvidences;
IF OBJECT_ID(N'dbo.CarrierLocationMappings', N'U') IS NOT NULL DROP TABLE dbo.CarrierLocationMappings;
IF OBJECT_ID(N'dbo.VnAdministrativeUnits', N'U') IS NOT NULL DROP TABLE dbo.VnAdministrativeUnits;
");
    }
}
