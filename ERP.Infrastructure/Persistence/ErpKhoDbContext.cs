using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence;

public class ErpKhoDbContext : DbContext
{
    private readonly ERP.Application.Interfaces.IRequestMetadata? _requestMetadata;

    public ErpKhoDbContext(DbContextOptions<ErpKhoDbContext> options, ERP.Application.Interfaces.IRequestMetadata? requestMetadata = null) : base(options)
    {
        _requestMetadata = requestMetadata;
    }

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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public override int SaveChanges()
    {
        EnrichAuditLogs();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnrichAuditLogs();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnrichAuditLogs()
    {
        if (_requestMetadata is null) return;
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(x => x.State == EntityState.Added))
        {
            entry.Entity.CorrelationId ??= _requestMetadata.CorrelationId;
            entry.Entity.IdempotencyKeyHash ??= _requestMetadata.IdempotencyKeyHash;
            entry.Entity.RequestFingerprint ??= _requestMetadata.RequestFingerprint;
            entry.Entity.Result ??= "Success";
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpKhoDbContext).Assembly);
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<Permission>().Property(x => x.Code).UseCollation("Latin1_General_100_BIN2");
            modelBuilder.Entity<Role>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<RolePermission>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<WarehouseCalendar>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<WarehouseShift>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<Dock>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<YardSlot>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<DockAppointment>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<PurchaseOrder>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<Asn>().Property(x => x.RowVersion).IsRowVersion();
            // SQL deployments backfill LocationId before enforcing the final required relationship.
            // The nullable CLR shape also lets pre-migration compatibility tests represent legacy rows.
            modelBuilder.Entity<InventoryStock>().Property(x => x.LocationId).IsRequired();
            modelBuilder.Entity<ProductCategory>().Property(x => x.Code).UseCollation("Latin1_General_100_CI_AS");
            modelBuilder.Entity<ProductBarcode>().Property(x => x.Value).UseCollation("Latin1_General_100_BIN2");
            modelBuilder.Entity<WarehouseLocation>().Property(x => x.Code).UseCollation("Latin1_General_100_CI_AS");
            modelBuilder.Entity<Product>().ToTable("Products", t =>
            {
                t.HasCheckConstraint(
                    "CK_Products_StorageClass",
                    "[StorageClass] IS NULL OR ([StorageClass] = UPPER(LTRIM(RTRIM([StorageClass]))) AND LEN([StorageClass]) > 0)");
                t.HasCheckConstraint(
                    "CK_Products_StorageMetrics",
                    "([UnitWeightKg] IS NULL OR [UnitWeightKg] > 0) AND ([UnitVolumeM3] IS NULL OR [UnitVolumeM3] > 0) AND ([UnitPalletEquivalent] IS NULL OR [UnitPalletEquivalent] > 0)");
            });
            modelBuilder.Entity<WarehouseLocation>().ToTable("WarehouseLocations", t =>
            {
                t.HasCheckConstraint(
                    "CK_WarehouseLocations_Code",
                    "[Code] = UPPER(LTRIM(RTRIM([Code]))) AND LEN([Code]) > 0");
                t.HasCheckConstraint(
                    "CK_WarehouseLocations_StructurePath",
                    "[StructurePath] IS NULL OR ([StructurePath] = UPPER(LTRIM(RTRIM([StructurePath]))) AND LEN([StructurePath]) > 0)");
                t.HasCheckConstraint(
                    "CK_WarehouseLocations_StorageClass",
                    "[StorageClass] IS NULL OR ([StorageClass] = UPPER(LTRIM(RTRIM([StorageClass]))) AND LEN([StorageClass]) > 0)");
                t.HasCheckConstraint(
                    "CK_WarehouseLocations_Capacity",
                    "([MaxWeightKg] IS NULL OR [MaxWeightKg] > 0) AND ([MaxVolumeM3] IS NULL OR [MaxVolumeM3] > 0) AND ([MaxPalletEquivalent] IS NULL OR [MaxPalletEquivalent] > 0)");
                t.HasCheckConstraint(
                    "CK_WarehouseLocations_MapLayout",
                    "([MapX] IS NULL AND [MapY] IS NULL AND [MapWidth] IS NULL AND [MapHeight] IS NULL) OR ([MapX] IS NOT NULL AND [MapY] IS NOT NULL AND [MapWidth] IS NOT NULL AND [MapHeight] IS NOT NULL AND [MapX] >= 0 AND [MapY] >= 0 AND [MapWidth] > 0 AND [MapHeight] > 0 AND [MapX] <= 100 AND [MapY] <= 100 AND [MapWidth] <= 100 AND [MapHeight] <= 100 AND [MapX] + [MapWidth] <= 100 AND [MapY] + [MapHeight] <= 100)");
            });
            modelBuilder.Entity<PutawayTaskItem>().ToTable("PutawayTaskItems", t =>
            {
                t.HasCheckConstraint("CK_PutawayTaskItems_Required", "[RequiredBaseQuantity] > 0");
                t.HasCheckConstraint("CK_PutawayTaskItems_Moved", "[MovedBaseQuantity] >= 0 AND [MovedBaseQuantity] <= [RequiredBaseQuantity]");
            });
            modelBuilder.Entity<InventoryLocationMovement>().ToTable("InventoryLocationMovements", t =>
            {
                t.HasCheckConstraint("CK_InventoryLocationMovements_Quantity", "[BaseQuantity] > 0");
                t.HasCheckConstraint("CK_InventoryLocationMovements_Locations", "[FromLocationId] <> [ToLocationId]");
            });
            modelBuilder.Entity<ProductBarcode>().ToTable("ProductBarcodes", t => t.HasCheckConstraint(
                "CK_ProductBarcodes_Value",
                "[Value] = LTRIM(RTRIM([Value])) AND [Value] NOT LIKE '%[^-A-Za-z0-9._]%' COLLATE Latin1_General_100_BIN2 AND LEN([Value]) BETWEEN 1 AND 64"));
        }
    }
}
