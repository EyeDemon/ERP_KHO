using ERP.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence;

public class ErpKhoDbContext : DbContext
{
    private readonly ERP.Application.Interfaces.IRequestMetadata? _requestMetadata;
    private IDbContextTransaction? _activeLockTransaction;
    private Guid? _activeLockTransactionId;
    private readonly HashSet<(int WarehouseId, int ProductId)> _lockedAllocationScopes = new();

    public ErpKhoDbContext(DbContextOptions<ErpKhoDbContext> options, ERP.Application.Interfaces.IRequestMetadata? requestMetadata = null) : base(options)
    {
        _requestMetadata = requestMetadata;
    }

    public Task AcquireInventoryAllocationLocksAsync(
        int warehouseId,
        IEnumerable<int> productIds,
        CancellationToken cancellationToken = default) =>
        AcquireInventoryAllocationLocksAsync(
            productIds.Select(productId => (warehouseId, productId)),
            cancellationToken);

    public async Task AcquireInventoryAllocationLocksAsync(
        IEnumerable<(int WarehouseId, int ProductId)> scopes,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational() || !Database.IsSqlServer() || Database.CurrentTransaction is null)
        {
            return;
        }

        var currentTx = Database.CurrentTransaction;
        var txId = currentTx.TransactionId;
        if (!ReferenceEquals(_activeLockTransaction, currentTx) || _activeLockTransactionId != txId)
        {
            _activeLockTransaction = currentTx;
            _activeLockTransactionId = txId;
            _lockedAllocationScopes.Clear();
        }

        var pendingScopes = scopes
            .Where(x => x.WarehouseId > 0 && x.ProductId > 0 && !_lockedAllocationScopes.Contains(x))
            .Distinct()
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductId)
            .ToList();

        if (pendingScopes.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var (warehouseId, productId) in pendingScopes)
            {
                var allocationLock = $"ERP:InventoryAllocation:{productId}:{warehouseId}";
                await Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = {allocationLock},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @lockResult < 0
BEGIN
    DECLARE @lockMessage nvarchar(2048) =
        CONCAT(N'Không thể khóa phân bổ tồn kho. Mã khóa SQL: ', @lockResult);
    THROW 51033, @lockMessage, 1;
END;", cancellationToken);
            }

            foreach (var (warehouseId, productId) in pendingScopes)
            {
                await Database.ExecuteSqlInterpolatedAsync($@"
SELECT Id
FROM dbo.InventoryStocks WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
WHERE WarehouseId = {warehouseId}
  AND ProductId = {productId}
ORDER BY Id;", cancellationToken);
                _lockedAllocationScopes.Add((warehouseId, productId));
            }
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch khóa tồn kho bị deadlock.", exception);
        }
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

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyInventoryLedger();
        EnforceAppendOnlyAuditLogs();
        EnrichAuditLogs();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyInventoryLedger();
        EnforceAppendOnlyAuditLogs();
        EnrichAuditLogs();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnlyAuditLogs()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("AuditLog is append-only and cannot be modified or deleted.");
            }
        }
    }

    private void EnforceAppendOnlyInventoryLedger()
    {
        foreach (var entry in ChangeTracker.Entries<InventoryTransaction>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "InventoryTransactions ledger is append-only; deleting ledger entries is forbidden (Rule 2).");
            }

            if (entry.State == EntityState.Modified && !IsAllowedReversalCorrectiveLinkUpdate(entry))
            {
                throw new InvalidOperationException(
                    "InventoryTransactions ledger is append-only; modifying existing ledger entries is forbidden (Rule 2).");
            }
        }
    }

    private static bool IsAllowedReversalCorrectiveLinkUpdate(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<InventoryTransaction> entry)
    {
        if (entry.Entity.TransactionType != ERP.Domain.Enums.TransactionType.Reversal)
            return false;

        var correctiveProp = entry.Property(x => x.CorrectiveTransactionId);
        if (!correctiveProp.IsModified || correctiveProp.OriginalValue != null || correctiveProp.CurrentValue == null)
            return false;

        return entry.Properties.All(p => !p.IsModified || p.Metadata.Name == nameof(InventoryTransaction.CorrectiveTransactionId));
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
