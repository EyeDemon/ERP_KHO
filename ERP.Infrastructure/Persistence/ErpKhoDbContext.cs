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
    public DbSet<User> Users => Set<User>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUom> ProductUoms => Set<ProductUom>();
    public DbSet<QcPolicy> QcPolicies => Set<QcPolicy>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
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
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ReceivingDiscrepancy> ReceivingDiscrepancies => Set<ReceivingDiscrepancy>();
    public DbSet<ReceivingObservationVersion> ReceivingObservationVersions => Set<ReceivingObservationVersion>();
    public DbSet<ReceivingObservationItem> ReceivingObservationItems => Set<ReceivingObservationItem>();
    public DbSet<ReceivingResolutionVersion> ReceivingResolutionVersions => Set<ReceivingResolutionVersion>();
    public DbSet<ReceivingReasonCode> ReceivingReasonCodes => Set<ReceivingReasonCode>();
    public DbSet<ReceivingTolerancePolicy> ReceivingTolerancePolicies => Set<ReceivingTolerancePolicy>();

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
            modelBuilder.Entity<ProductCategory>().Property(x => x.Code).UseCollation("Latin1_General_100_CI_AS");
            modelBuilder.Entity<ProductBarcode>().Property(x => x.Value).UseCollation("Latin1_General_100_BIN2");
            modelBuilder.Entity<ProductBarcode>().ToTable("ProductBarcodes", t => t.HasCheckConstraint(
                "CK_ProductBarcodes_Value",
                "[Value] = LTRIM(RTRIM([Value])) AND [Value] NOT LIKE '%[^-A-Za-z0-9._]%' COLLATE Latin1_General_100_BIN2 AND LEN([Value]) BETWEEN 1 AND 64"));
        }
    }
}
