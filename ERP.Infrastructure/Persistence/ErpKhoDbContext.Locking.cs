using ERP.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence;

public partial class ErpKhoDbContext
{
    private IDbContextTransaction? _activeLockTransaction;
    private Guid? _activeLockTransactionId;
    private readonly HashSet<(int TenantId, int WarehouseId, int ProductId)> _lockedAllocationScopes = new();

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
    @LockTimeout = 60000;
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
ORDER BY TenantId ASC, WarehouseId ASC, LocationId ASC, ProductId ASC, LotId ASC, SerialId ASC, HandlingUnitId ASC, Status ASC, OwnerId ASC, Id ASC;", cancellationToken);
                _lockedAllocationScopes.Add((tenantId, warehouseId, productId));
            }
        }
        catch (SqlException exception) when (exception.Number is 1205 or 51033)
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
            if (entry.Entity is User platformUser && string.Equals(platformUser.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase))
            {
                if (platformUser.TenantId <= 0)
                {
                    platformUser.TenantId = 1;
                }
                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(ERP.Domain.Common.ITenantEntity.TenantId)).IsModified = false;
                }
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                if (isBypass && entry.Entity.TenantId > 0)
                {
                    continue;
                }

                entry.Entity.TenantId = activeTenantId;
            }
        }

        foreach (var stockEntry in ChangeTracker.Entries<InventoryStock>())
        {
            if (stockEntry.Entity.LotId is null && stockEntry.Entity.Lot is { Id: > 0 } resolvedLot)
            {
                stockEntry.Entity.LotId = resolvedLot.Id;
            }
            if (stockEntry.Entity.SerialId is null && stockEntry.Entity.Serial is { Id: > 0 } resolvedSerial)
            {
                stockEntry.Entity.SerialId = resolvedSerial.Id;
            }
            if (stockEntry.State is EntityState.Added or EntityState.Modified
                || stockEntry.Entity.GrainHash is null
                || stockEntry.Entity.GrainHash.Length != 32)
            {
                stockEntry.Entity.RefreshGrainHash();
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
}
