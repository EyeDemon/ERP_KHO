using ERP.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence;

public class ErpKhoDbContext : DbContext
{
    private readonly ERP.Application.Interfaces.IRequestMetadata? _requestMetadata;
    private readonly ERP.Application.Interfaces.ITenantContext? _tenantContext;
    private int? _ambientTenantOverride;
    private IDbContextTransaction? _activeLockTransaction;
    private Guid? _activeLockTransactionId;
    private readonly HashSet<(int TenantId, int WarehouseId, int ProductId)> _lockedAllocationScopes = new();

    public ErpKhoDbContext(
        DbContextOptions<ErpKhoDbContext> options,
        ERP.Application.Interfaces.IRequestMetadata? requestMetadata = null,
        ERP.Application.Interfaces.ITenantContext? tenantContext = null) : base(options)
    {
        _requestMetadata = requestMetadata;
        _tenantContext = tenantContext;
    }

    public int CurrentTenantId => _ambientTenantOverride ?? (_tenantContext?.TenantId > 0 ? _tenantContext.TenantId : 1);

    public bool IsSystemTenantBypass => _tenantContext?.IsSystemBypass == true;

    public void UseTenant(int tenantId)
    {
        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), "TenantId must be positive.");
        }

        _ambientTenantOverride = tenantId;
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

        var tenantId = CurrentTenantId;
        var pendingScopes = scopes
            .Where(x => x.WarehouseId > 0 && x.ProductId > 0 && !_lockedAllocationScopes.Contains((tenantId, x.WarehouseId, x.ProductId)))
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
                var allocationLock = $"ERP:InventoryAllocation:{tenantId}:{warehouseId}:{productId}";
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
FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD)
WHERE TenantId = {tenantId}
  AND ProductId = {productId}
  AND WarehouseId = {warehouseId}
ORDER BY Id;", cancellationToken);
                _lockedAllocationScopes.Add((tenantId, warehouseId, productId));
            }
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch khóa tồn kho bị deadlock.", exception);
        }
    }

    public async Task AcquireBusinessPartnerLockAsync(
        int partnerId,
        CancellationToken cancellationToken = default)
    {
        if (partnerId <= 0 || !Database.IsRelational() || !Database.IsSqlServer() || Database.CurrentTransaction is null)
        {
            return;
        }

        var tenantId = CurrentTenantId;
        try
        {
            await Database.ExecuteSqlInterpolatedAsync($@"
SELECT Id
FROM dbo.BusinessPartners WITH (UPDLOCK, ROWLOCK)
WHERE TenantId = {tenantId}
  AND Id = {partnerId};", cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch khóa công nợ đối tác bị deadlock.", exception);
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

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceMultiTenantIsolation();
        EnforceAppendOnlyInventoryLedger();
        EnforceAppendOnlyLocationMovements();
        EnforceAppendOnlyPartnerDebtLedger();
        EnforceAppendOnlyAuditLogs();
        EnrichAuditLogs();
        EnsureNonSqlServerRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await EnforceMultiTenantIsolationAsync(cancellationToken);
        EnforceAppendOnlyInventoryLedger();
        EnforceAppendOnlyLocationMovements();
        EnforceAppendOnlyPartnerDebtLedger();
        EnforceAppendOnlyAuditLogs();
        EnrichAuditLogs();
        EnsureNonSqlServerRowVersions();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private readonly record struct PendingForeignKeyTenantCheck(
        string DependentTypeName,
        Type PrincipalClrType,
        string PrincipalKeyName,
        object FkValue);

    private void EnforceMultiTenantIsolation()
    {
        if (!PrepareAndCollectMultiTenantChecks(out var activeTenantId, out var pendingFkChecks))
        {
            return;
        }

        if (pendingFkChecks.Count == 0)
        {
            return;
        }

        var principalTenantCache = new Dictionary<(Type PrincipalType, object Key), int?>();
        foreach (var group in pendingFkChecks.GroupBy(x => (x.PrincipalClrType, x.PrincipalKeyName, KeyClrType: x.FkValue.GetType())))
        {
            var distinctKeys = group.Select(x => x.FkValue).Distinct().ToList();
            BatchLookupPrincipalTenantIdsSync(
                group.Key.PrincipalClrType,
                group.Key.KeyClrType,
                group.Key.PrincipalKeyName,
                distinctKeys,
                principalTenantCache);
        }

        VerifyCollectedForeignKeyTenantOwnership(pendingFkChecks, activeTenantId, principalTenantCache);
    }

    private async Task EnforceMultiTenantIsolationAsync(CancellationToken cancellationToken)
    {
        if (!PrepareAndCollectMultiTenantChecks(out var activeTenantId, out var pendingFkChecks))
        {
            return;
        }

        if (pendingFkChecks.Count == 0)
        {
            return;
        }

        var principalTenantCache = new Dictionary<(Type PrincipalType, object Key), int?>();
        foreach (var group in pendingFkChecks.GroupBy(x => (x.PrincipalClrType, x.PrincipalKeyName, KeyClrType: x.FkValue.GetType())))
        {
            var distinctKeys = group.Select(x => x.FkValue).Distinct().ToList();
            await BatchLookupPrincipalTenantIdsAsync(
                group.Key.PrincipalClrType,
                group.Key.KeyClrType,
                group.Key.PrincipalKeyName,
                distinctKeys,
                principalTenantCache,
                cancellationToken);
        }

        VerifyCollectedForeignKeyTenantOwnership(pendingFkChecks, activeTenantId, principalTenantCache);
    }

    private bool PrepareAndCollectMultiTenantChecks(
        out int activeTenantId,
        out List<PendingForeignKeyTenantCheck> pendingFkChecks)
    {
        activeTenantId = CurrentTenantId;
        var isBypass = IsSystemTenantBypass;
        pendingFkChecks = [];

        var tenantEntries = ChangeTracker.Entries<ERP.Domain.Common.ITenantEntity>().ToList();

        foreach (var entry in tenantEntries)
        {
            if (entry.State == EntityState.Added)
            {
                if (isBypass && entry.Entity.TenantId > 0)
                {
                    continue;
                }

                entry.Entity.TenantId = activeTenantId;
            }
        }

        if (isBypass)
        {
            return false;
        }

        foreach (var entry in tenantEntries)
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                var tenantProp = entry.Property(nameof(ERP.Domain.Common.ITenantEntity.TenantId));
                var originalTenantId = tenantProp.OriginalValue is int orig && orig > 0 ? orig : entry.Entity.TenantId;
                var currentTenantId = entry.Entity.TenantId;

                if (originalTenantId != activeTenantId
                    || currentTenantId != activeTenantId
                    || (entry.State == EntityState.Modified && tenantProp.IsModified && originalTenantId != currentTenantId))
                {
                    throw new ERP.Application.Exceptions.ForbiddenException(
                        $"Vi phạm cô lập dữ liệu Multi-Tenant: Không được phép thao tác hoặc sửa đổi thực thể '{entry.Metadata.ClrType.Name}' thuộc Tenant {originalTenantId} từ ngữ cảnh Tenant {activeTenantId}.");
                }
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                foreach (var navigation in entry.Navigations)
                {
                    if (navigation.CurrentValue is ERP.Domain.Common.ITenantEntity referencedEntity)
                    {
                        EnsureNavigationTenantMatch(entry.Metadata.ClrType.Name, navigation.Metadata.Name, referencedEntity, activeTenantId);
                    }
                    else if (navigation.CurrentValue is System.Collections.IEnumerable collection && navigation.CurrentValue is not string)
                    {
                        foreach (var item in collection)
                        {
                            if (item is ERP.Domain.Common.ITenantEntity childEntity)
                            {
                                EnsureNavigationTenantMatch(entry.Metadata.ClrType.Name, navigation.Metadata.Name, childEntity, activeTenantId);
                            }
                        }
                    }
                }

                CollectEntryForeignKeyChecks(entry, activeTenantId, pendingFkChecks);
            }
        }

        return true;
    }

    private static void EnsureNavigationTenantMatch(
        string entityTypeName,
        string navigationName,
        ERP.Domain.Common.ITenantEntity referencedEntity,
        int activeTenantId)
    {
        if (referencedEntity.TenantId > 0 && referencedEntity.TenantId != activeTenantId)
        {
            throw new ERP.Application.Exceptions.ForbiddenException(
                $"Vi phạm cô lập khóa ngoại Multi-Tenant (403 Cross-Tenant Reference Denied): Thực thể '{entityTypeName}' (Tenant {activeTenantId}) không được phép liên kết '{navigationName}' thuộc Tenant {referencedEntity.TenantId}.");
        }
    }

    private void CollectEntryForeignKeyChecks(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ERP.Domain.Common.ITenantEntity> entry,
        int activeTenantId,
        List<PendingForeignKeyTenantCheck> pendingFkChecks)
    {
        foreach (var fk in entry.Metadata.GetForeignKeys())
        {
            var principalClrType = fk.PrincipalEntityType.ClrType;
            // Skip global catalog tables that do not implement ITenantEntity (Role, Permission, RolePermission, InventoryStatusDefinition)
            if (!typeof(ERP.Domain.Common.ITenantEntity).IsAssignableFrom(principalClrType)
                || fk.Properties.Count != 1
                || fk.PrincipalKey.Properties.Count != 1)
            {
                continue;
            }

            if (fk.DependentToPrincipal?.Name is string depNavName
                && entry.Navigation(depNavName).CurrentValue is ERP.Domain.Common.ITenantEntity navPrincipal)
            {
                EnsureNavigationTenantMatch(entry.Metadata.ClrType.Name, depNavName, navPrincipal, activeTenantId);
                continue;
            }

            var fkPropEntry = entry.Property(fk.Properties[0].Name);
            if (entry.State == EntityState.Modified && !fkPropEntry.IsModified)
            {
                continue;
            }

            var fkValue = fkPropEntry.CurrentValue;
            if (fkPropEntry.IsTemporary
                || fkValue is null
                || (fkValue is int intKey && intKey <= 0)
                || (fkValue is long longKey && longKey <= 0)
                || (fkValue is Guid guidKey && guidKey == Guid.Empty))
            {
                continue;
            }

            var principalKeyName = fk.PrincipalKey.Properties[0].Name;
            var trackedPrincipal = ChangeTracker.Entries()
                .FirstOrDefault(e =>
                    principalClrType.IsAssignableFrom(e.Metadata.ClrType)
                    && Equals(e.Property(principalKeyName).CurrentValue, fkValue));

            if (trackedPrincipal?.Entity is ERP.Domain.Common.ITenantEntity trackedTenantPrincipal)
            {
                if (trackedTenantPrincipal.TenantId > 0 && trackedTenantPrincipal.TenantId != activeTenantId)
                {
                    throw new ERP.Application.Exceptions.ForbiddenException(
                        $"Vi phạm cô lập khóa ngoại Multi-Tenant (403 Cross-Tenant Reference Denied): Thực thể '{entry.Metadata.ClrType.Name}' (Tenant {activeTenantId}) không được phép tham chiếu '{principalClrType.Name}' (ID={fkValue}, Tenant {trackedTenantPrincipal.TenantId}).");
                }

                continue;
            }

            pendingFkChecks.Add(new PendingForeignKeyTenantCheck(
                entry.Metadata.ClrType.Name,
                principalClrType,
                principalKeyName,
                fkValue));
        }
    }

    private static void VerifyCollectedForeignKeyTenantOwnership(
        List<PendingForeignKeyTenantCheck> pendingFkChecks,
        int activeTenantId,
        Dictionary<(Type PrincipalType, object Key), int?> principalTenantCache)
    {
        foreach (var check in pendingFkChecks)
        {
            if (principalTenantCache.TryGetValue((check.PrincipalClrType, check.FkValue), out var principalTenantId)
                && principalTenantId.HasValue
                && principalTenantId.Value > 0
                && principalTenantId.Value != activeTenantId)
            {
                throw new ERP.Application.Exceptions.ForbiddenException(
                    $"Vi phạm cô lập khóa ngoại Multi-Tenant (403 Cross-Tenant Reference Denied): Thực thể '{check.DependentTypeName}' (Tenant {activeTenantId}) không được phép tham chiếu '{check.PrincipalClrType.Name}' (ID={check.FkValue}, Tenant {principalTenantId.Value}).");
            }
        }
    }

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

    private void EnforceAppendOnlyLocationMovements()
    {
        foreach (var entry in ChangeTracker.Entries<InventoryLocationMovement>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "InventoryLocationMovements is append-only; modifying or deleting location movement history is forbidden.");
            }

            if (entry.State == EntityState.Modified && !IsAllowedMovementSelfReferenceLinkUpdate(entry))
            {
                throw new InvalidOperationException(
                    "InventoryLocationMovements is append-only; modifying or deleting location movement history is forbidden.");
            }
        }
    }

    private static bool IsAllowedMovementSelfReferenceLinkUpdate(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<InventoryLocationMovement> entry)
    {
        var refProp = entry.Property(x => x.ReferenceId);
        if (!refProp.IsModified || refProp.OriginalValue != null || refProp.CurrentValue != entry.Entity.Id)
            return false;

        return entry.Properties.All(p => !p.IsModified || p.Metadata.Name == nameof(InventoryLocationMovement.ReferenceId));
    }

    private void EnforceAppendOnlyPartnerDebtLedger()
    {
        foreach (var entry in ChangeTracker.Entries<PartnerDebtLedger>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "PartnerDebtLedgers is append-only; modifying or deleting debt ledger entries is forbidden.");
            }
        }
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

    private void EnsureNonSqlServerRowVersions()
    {
        if (Database.IsSqlServer())
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
            {
                continue;
            }

            var rowVersionMeta = entry.Metadata.FindProperty("RowVersion");
            if (rowVersionMeta is null || rowVersionMeta.ClrType != typeof(byte[]))
            {
                continue;
            }

            var prop = entry.Property("RowVersion");
            var current = prop.CurrentValue as byte[];
            var nextToken = Guid.NewGuid().ToByteArray()[..8];

            if (entry.State == EntityState.Added)
            {
                if (current is null || current.Length == 0)
                {
                    prop.CurrentValue = nextToken;
                }
            }
            else if (entry.State == EntityState.Modified && rowVersionMeta.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            {
                var original = prop.OriginalValue as byte[];
                if (current is null || current.Length == 0 || (original is not null && current.AsSpan().SequenceEqual(original)))
                {
                    prop.CurrentValue = nextToken;
                }
            }
        }
    }

    private void ConfigureTenantEntity<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ERP.Domain.Common.ITenantEntity
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.Property(e => e.TenantId).HasDefaultValue(1);
            builder.HasQueryFilter(e => this.IsSystemTenantBypass || e.TenantId == this.CurrentTenantId);
        });
    }

    private void ConfigureMultiTenantEntities(ModelBuilder modelBuilder)
    {
        var method = typeof(ErpKhoDbContext).GetMethod(
            nameof(ConfigureTenantEntity),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ERP.Domain.Common.ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                method.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpKhoDbContext).Assembly);
        ConfigureMultiTenantEntities(modelBuilder);
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
            modelBuilder.Entity<InventoryStock>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<ImportReceipt>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<ExportReceipt>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<PartnerDebtInvoice>().Property(x => x.RowVersion).IsRowVersion();
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
