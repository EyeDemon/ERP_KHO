using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

public partial class ErpKhoDbContext
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUom> ProductUoms => Set<ProductUom>();
    public DbSet<QcPolicy> QcPolicies => Set<QcPolicy>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();
    public DbSet<PartnerDebtInvoice> PartnerDebtInvoices => Set<PartnerDebtInvoice>();
    public DbSet<PartnerDebtLedger> PartnerDebtLedgers => Set<PartnerDebtLedger>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseCalendar> WarehouseCalendars => Set<WarehouseCalendar>();
    public DbSet<WarehouseCalendarDay> WarehouseCalendarDays => Set<WarehouseCalendarDay>();
    public DbSet<WarehouseShift> WarehouseShifts => Set<WarehouseShift>();
    public DbSet<Dock> Docks => Set<Dock>();
    public DbSet<YardSlot> YardSlots => Set<YardSlot>();
    public DbSet<DockAppointment> DockAppointments => Set<DockAppointment>();
    public DbSet<DockAppointmentEvent> DockAppointmentEvents => Set<DockAppointmentEvent>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Asn> Asns => Set<Asn>();
    public DbSet<AsnLine> AsnLines => Set<AsnLine>();
    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryStatusDefinition> InventoryStatusDefinitions => Set<InventoryStatusDefinition>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<InventorySerial> InventorySerials => Set<InventorySerial>();
    public DbSet<InventoryLock> InventoryLocks => Set<InventoryLock>();
    public DbSet<ImportReceiptInventoryIdentity> ImportReceiptInventoryIdentities => Set<ImportReceiptInventoryIdentity>();
    public DbSet<ImportReceipt> ImportReceipts => Set<ImportReceipt>();
    public DbSet<ImportReceiptDetail> ImportReceiptDetails => Set<ImportReceiptDetail>();
    public DbSet<ExportReceipt> ExportReceipts => Set<ExportReceipt>();
    public DbSet<ExportReceiptDetail> ExportReceiptDetails => Set<ExportReceiptDetail>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeDetail> StocktakeDetails => Set<StocktakeDetail>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserWarehouse> UserWarehouses => Set<UserWarehouse>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferDetail> StockTransferDetails => Set<StockTransferDetail>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockAllocation> StockAllocations => Set<StockAllocation>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ReceivingDiscrepancy> ReceivingDiscrepancies => Set<ReceivingDiscrepancy>();
    public DbSet<ReceivingObservationVersion> ReceivingObservationVersions => Set<ReceivingObservationVersion>();
    public DbSet<ReceivingObservationItem> ReceivingObservationItems => Set<ReceivingObservationItem>();
    public DbSet<ReceivingResolutionVersion> ReceivingResolutionVersions => Set<ReceivingResolutionVersion>();
    public DbSet<ReceivingReasonCode> ReceivingReasonCodes => Set<ReceivingReasonCode>();
    public DbSet<ReceivingTolerancePolicy> ReceivingTolerancePolicies => Set<ReceivingTolerancePolicy>();
    public DbSet<WarehouseLocation> WarehouseLocations => Set<WarehouseLocation>();
    public DbSet<PutawayTask> PutawayTasks => Set<PutawayTask>();
    public DbSet<PutawayTaskItem> PutawayTaskItems => Set<PutawayTaskItem>();
    public DbSet<InventoryLocationMovement> InventoryLocationMovements => Set<InventoryLocationMovement>();
    public DbSet<PickingTask> PickingTasks => Set<PickingTask>();
    public DbSet<PickingTaskLine> PickingTaskLines => Set<PickingTaskLine>();
    public DbSet<ShortPickException> ShortPickExceptions => Set<ShortPickException>();
    public DbSet<PackingSession> PackingSessions => Set<PackingSession>();
    public DbSet<HandlingUnit> HandlingUnits => Set<HandlingUnit>();
    public DbSet<HandlingUnitContent> HandlingUnitContents => Set<HandlingUnitContent>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentHandlingUnit> ShipmentHandlingUnits => Set<ShipmentHandlingUnit>();
    public DbSet<ShipmentTrackingEvent> ShipmentTrackingEvents => Set<ShipmentTrackingEvent>();
    public DbSet<ShipmentProofOfDelivery> ShipmentProofOfDeliveries => Set<ShipmentProofOfDelivery>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Backorder> Backorders => Set<Backorder>();
    public DbSet<IntegrationOutboxMessage> IntegrationOutboxMessages => Set<IntegrationOutboxMessage>();
    public DbSet<IntegrationInboxMessage> IntegrationInboxMessages => Set<IntegrationInboxMessage>();
    public DbSet<ProductBundleComponent> ProductBundleComponents => Set<ProductBundleComponent>();
    public DbSet<MarketplaceShop> MarketplaceShops => Set<MarketplaceShop>();
    public DbSet<ChannelSkuMapping> ChannelSkuMappings => Set<ChannelSkuMapping>();
    public DbSet<HandoverManifest> HandoverManifests => Set<HandoverManifest>();
    public DbSet<HandoverManifestLine> HandoverManifestLines => Set<HandoverManifestLine>();
    public DbSet<SaasTenant> SaasTenants => Set<SaasTenant>();
    public DbSet<SaasPlan> SaasPlans => Set<SaasPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SaasBillingInvoice> SaasBillingInvoices => Set<SaasBillingInvoice>();
    public DbSet<ShipmentFinancialLedger> ShipmentFinancialLedgers => Set<ShipmentFinancialLedger>();
    public DbSet<VnAdministrativeUnit> VnAdministrativeUnits => Set<VnAdministrativeUnit>();
    public DbSet<CarrierLocationMapping> CarrierLocationMappings => Set<CarrierLocationMapping>();
    public DbSet<PackingVideoEvidence> PackingVideoEvidences => Set<PackingVideoEvidence>();
    public DbSet<PromotionGiftRule> PromotionGiftRules => Set<PromotionGiftRule>();
    public DbSet<SupportImpersonationSession> SupportImpersonationSessions => Set<SupportImpersonationSession>();
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<CustomerReturnLine> CustomerReturnLines => Set<CustomerReturnLine>();
    public DbSet<StocktakeAdjustmentProposal> StocktakeAdjustmentProposals => Set<StocktakeAdjustmentProposal>();
    public DbSet<PickingWave> PickingWaves => Set<PickingWave>();
    public DbSet<PickingWaveTemplate> PickingWaveTemplates => Set<PickingWaveTemplate>();
    public DbSet<PutToWallSlot> PutToWallSlots => Set<PutToWallSlot>();
    public DbSet<CarrierIntegrationConfig> CarrierIntegrationConfigs => Set<CarrierIntegrationConfig>();
    public DbSet<ThreePlStorageSnapshot> ThreePlStorageSnapshots => Set<ThreePlStorageSnapshot>();
    public DbSet<ThreePlBillingStatement> ThreePlBillingStatements => Set<ThreePlBillingStatement>();
    public DbSet<KitWorkOrder> KitWorkOrders => Set<KitWorkOrder>();
    public DbSet<KitWorkOrderComponentLine> KitWorkOrderComponentLines => Set<KitWorkOrderComponentLine>();

    private Task BatchLookupPrincipalTenantIdsAsync(
        Type principalClrType,
        Type keyClrType,
        string principalKeyName,
        IReadOnlyCollection<object> rawKeys,
        Dictionary<(Type PrincipalType, object Key), int?> cache,
        CancellationToken cancellationToken)
    {
        var method = typeof(ErpKhoDbContext).GetMethod(
            nameof(BatchLookupPrincipalTenantIdsCoreAsync),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(principalClrType, keyClrType);

        return (Task)method.Invoke(this, [principalKeyName, rawKeys, cache, cancellationToken])!;
    }

    private async Task BatchLookupPrincipalTenantIdsCoreAsync<TPrincipal, TKey>(
        string principalKeyName,
        IReadOnlyCollection<object> rawKeys,
        Dictionary<(Type PrincipalType, object Key), int?> cache,
        CancellationToken cancellationToken)
        where TPrincipal : class, ERP.Domain.Common.ITenantEntity
        where TKey : notnull
    {
        var typedKeys = rawKeys.Cast<TKey>().Distinct().ToList();
        if (typedKeys.Count == 0)
        {
            return;
        }

        var rows = await Set<TPrincipal>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => typedKeys.Contains(EF.Property<TKey>(e, principalKeyName)))
            .Select(e => new
            {
                Key = EF.Property<TKey>(e, principalKeyName),
                e.TenantId
            })
            .ToListAsync(cancellationToken);

        foreach (var key in typedKeys)
        {
            cache[(typeof(TPrincipal), key)] = null;
        }

        foreach (var row in rows)
        {
            if (row.Key is not null)
            {
                cache[(typeof(TPrincipal), row.Key)] = row.TenantId;
            }
        }
    }

    private void BatchLookupPrincipalTenantIdsSync(
        Type principalClrType,
        Type keyClrType,
        string principalKeyName,
        IReadOnlyCollection<object> rawKeys,
        Dictionary<(Type PrincipalType, object Key), int?> cache)
    {
        var method = typeof(ErpKhoDbContext).GetMethod(
            nameof(BatchLookupPrincipalTenantIdsCoreSync),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(principalClrType, keyClrType);

        method.Invoke(this, [principalKeyName, rawKeys, cache]);
    }

    private void BatchLookupPrincipalTenantIdsCoreSync<TPrincipal, TKey>(
        string principalKeyName,
        IReadOnlyCollection<object> rawKeys,
        Dictionary<(Type PrincipalType, object Key), int?> cache)
        where TPrincipal : class, ERP.Domain.Common.ITenantEntity
        where TKey : notnull
    {
        var typedKeys = rawKeys.Cast<TKey>().Distinct().ToList();
        if (typedKeys.Count == 0)
        {
            return;
        }

        var rows = Set<TPrincipal>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => typedKeys.Contains(EF.Property<TKey>(e, principalKeyName)))
            .Select(e => new
            {
                Key = EF.Property<TKey>(e, principalKeyName),
                e.TenantId
            })
            .ToList();

        foreach (var key in typedKeys)
        {
            cache[(typeof(TPrincipal), key)] = null;
        }

        foreach (var row in rows)
        {
            if (row.Key is not null)
            {
                cache[(typeof(TPrincipal), row.Key)] = row.TenantId;
            }
        }
    }
}
