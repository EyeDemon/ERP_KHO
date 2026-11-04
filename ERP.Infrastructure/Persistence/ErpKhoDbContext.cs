using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence;

public partial class ErpKhoDbContext : DbContext
{
    private readonly ERP.Application.Interfaces.IRequestMetadata? _requestMetadata;
    private readonly ERP.Application.Interfaces.ITenantContext? _tenantContext;
    private int? _ambientTenantOverride;

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

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceMultiTenantIsolation();
        EnforceAppendOnlyInventoryLedger();
        EnforceAppendOnlyLocationMovements();
        EnforceAppendOnlyPartnerDebtLedger();
        EnforceAppendOnlyShipmentTrackingEvents();
        EnforceAppendOnlyDockAppointmentEvents();
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
        EnforceAppendOnlyShipmentTrackingEvents();
        EnforceAppendOnlyDockAppointmentEvents();
        EnforceAppendOnlyAuditLogs();
        EnrichAuditLogs();
        EnsureNonSqlServerRowVersions();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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

    private void EnforceAppendOnlyShipmentTrackingEvents()
    {
        foreach (var entry in ChangeTracker.Entries<ShipmentTrackingEvent>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ShipmentTrackingEvents is append-only; modifying or deleting shipment tracking events is forbidden.",
                    "APPEND_ONLY_VIOLATION");
            }
        }
    }

    private void EnforceAppendOnlyDockAppointmentEvents()
    {
        foreach (var entry in ChangeTracker.Entries<DockAppointmentEvent>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "DockAppointmentEvents is append-only; modifying or deleting dock appointment events is forbidden.",
                    "APPEND_ONLY_VIOLATION");
            }
        }
    }

    private void EnrichAuditLogs()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(x => x.State == EntityState.Added))
        {
            if (_requestMetadata is not null)
            {
                entry.Entity.CorrelationId ??= _requestMetadata.CorrelationId;
                entry.Entity.IdempotencyKeyHash ??= _requestMetadata.IdempotencyKeyHash;
                entry.Entity.RequestFingerprint ??= _requestMetadata.RequestFingerprint;
            }
            entry.Entity.Result ??= "Success";
            if (entry.Entity.Result.Length > 32)
            {
                entry.Entity.Reason ??= entry.Entity.Result;
                entry.Entity.Result = "Success";
            }
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
        modelBuilder.Entity<InventoryStock>(entity =>
        {
            entity.Property(e => e.GrainHash).HasMaxLength(32).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.GrainHash })
                .IsUnique()
                .HasDatabaseName("IX_InventoryStocks_Tenant_GrainHash");
        });
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Timestamp, e.EntityName })
                .HasDatabaseName("IX_AuditLogs_TenantId_Timestamp_EntityName");
        });
        modelBuilder.Entity<WarehouseLocation>(b =>
        {
            b.Property(x => x.MinBaseQty).HasPrecision(18, 4);
            b.Property(x => x.MaxBaseQty).HasPrecision(18, 4);
        });
        modelBuilder.Entity<KitWorkOrder>(b =>
        {
            b.Property(x => x.BaseUomCode).IsRequired().HasMaxLength(32).HasDefaultValue("PCS");
            b.Property(x => x.ConversionRateSnapshot).HasPrecision(18, 4).HasDefaultValue(1m);
        });
        modelBuilder.Entity<KitWorkOrderComponentLine>(b =>
        {
            b.Property(x => x.BaseUomCode).IsRequired().HasMaxLength(32).HasDefaultValue("PCS");
            b.Property(x => x.ConversionRateSnapshot).HasPrecision(18, 4).HasDefaultValue(1m);
        });
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
            modelBuilder.Entity<CustomerReturn>().Property(x => x.RowVersion).IsRowVersion();
            modelBuilder.Entity<SaasTenant>().Property(x => x.RowVersion).IsRowVersion();
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
        else
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var rowVersionProp = entityType.FindProperty("RowVersion");
                if (rowVersionProp is not null && rowVersionProp.ClrType == typeof(byte[]))
                {
                    rowVersionProp.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                }
            }
        }
    }
}
