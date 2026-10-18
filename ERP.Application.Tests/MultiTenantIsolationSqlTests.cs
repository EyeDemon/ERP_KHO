using System.Diagnostics;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class MultiTenantIsolationSqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateTenantContext(int tenantId, string? tenantCode = null, bool isSystemBypass = false)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        var tenantContext = new TenantContext(tenantId, tenantCode ?? $"TENANT-{tenantId}", isSystemBypass);
        return new ErpKhoDbContext(options, tenantContext: tenantContext);
    }

    private sealed class StubTenantCurrentUser(
        int userId,
        string username,
        string role,
        bool isGlobalAdmin,
        int tenantId = 1,
        string tenantCode = "DEFAULT") : ICurrentUser
    {
        public int UserId { get; } = userId;
        public string Username { get; } = username;
        public string Role { get; } = role;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin { get; } = isGlobalAdmin;
        public int TenantId { get; } = tenantId;
        public string TenantCode { get; } = tenantCode;
    }

    [SqlServerFact]
    public async Task CrossTenant_DuplicateBusinessCodes_AcrossAll19CompositeUniqueIndexes_Succeed100Percent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenantA = Random.Shared.Next(10_000, 40_000);
        var tenantB = tenantA + 1;

        var sharedWhCode = $"WH-01-{suffix}";
        var sharedUnitCode = $"PCS{suffix[..3]}";
        var sharedCatCode = $"CAT-01-{suffix}";
        var sharedPartnerCode = $"SUP-01-{suffix}";
        var sharedSkuCode = $"SKU-001-{suffix}";
        var sharedBarcode = $"8930001{suffix}";
        var sharedUsername = $"ops_admin_{suffix.ToLowerInvariant()}";
        var sharedIrCode = $"IR-001-{suffix}";
        var sharedErCode = $"ER-001-{suffix}";
        var sharedStCode = $"ST-001-{suffix}";
        var sharedTrfCode = $"TRF-001-{suffix}";
        var sharedPoExtId = $"PO-EXT-001-{suffix}";
        var sharedSoCode = $"SO-001-{suffix}";
        var sharedSoExtId = $"SO-EXT-001-{suffix}";
        var sharedBoCode = $"BO-001-{suffix}";
        var sharedRsvCode = $"RSV-001-{suffix}";
        var sharedAlcCode = $"ALC-001-{suffix}";
        var sharedSscc = $"001234567890{suffix[..6]}";
        var sharedReasonCode = $"REASON-{suffix}";
        var sharedIdempKeyHash = $"SAME-KEY-001-{suffix}";

        try
        {
            foreach (var tenantId in new[] { tenantA, tenantB })
            {
                await using var db = CreateTenantContext(tenantId);
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager")
                           ?? new Role { RoleName = "Manager" };
                if (role.Id == 0)
                {
                    db.Roles.Add(role);
                    await db.SaveChangesAsync();
                }

                var user = new User
                {
                    Username = sharedUsername,
                    PasswordHash = "Hash",
                    FullName = $"Ops Admin Tenant {tenantId}",
                    RoleId = role.Id,
                    IsActive = true
                };
                var wh1 = new Warehouse { Code = sharedWhCode, Name = $"Warehouse 01 T{tenantId}", IsActive = true };
                var wh2 = new Warehouse { Code = $"{sharedWhCode}-DST", Name = $"Warehouse 02 T{tenantId}", IsActive = true };
                var unit = new Unit { Code = sharedUnitCode, Name = "Pieces", DecimalPlaces = 0 };
                var category = new ProductCategory { Code = sharedCatCode, Name = "General Category" };
                var partner = new BusinessPartner
                {
                    Code = sharedPartnerCode,
                    Name = $"Partner T{tenantId}",
                    IsSupplier = true,
                    IsCustomer = true,
                    IsActive = true
                };

                db.Users.Add(user);
                db.Warehouses.AddRange(wh1, wh2);
                db.Units.Add(unit);
                db.ProductCategories.Add(category);
                db.BusinessPartners.Add(partner);
                await db.SaveChangesAsync();

                var loc = new WarehouseLocation
                {
                    WarehouseId = wh1.Id,
                    Code = "BIN-01",
                    Name = "Storage Bin 01",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var product = new Product
                {
                    Code = sharedSkuCode,
                    Name = $"Product 001 T{tenantId}",
                    UnitId = unit.Id,
                    CategoryId = category.Id,
                    TrackingType = ProductTrackingType.None,
                    IsActive = true
                };
                db.WarehouseLocations.Add(loc);
                db.Products.Add(product);
                await db.SaveChangesAsync();

                var barcode = new ProductBarcode
                {
                    ProductId = product.Id,
                    Value = sharedBarcode
                };
                var stock = new InventoryStock
                {
                    WarehouseId = wh1.Id,
                    ProductId = product.Id,
                    LocationId = loc.Id,
                    Status = InventoryStatus.Available,
                    Quantity = 100m,
                    ReservedQuantity = 10m
                };
                var importReceipt = new ImportReceipt
                {
                    Code = sharedIrCode,
                    WarehouseId = wh1.Id,
                    SupplierId = partner.Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var exportReceipt = new ExportReceipt
                {
                    Code = sharedErCode,
                    WarehouseId = wh1.Id,
                    CustomerId = partner.Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var stocktake = new Stocktake
                {
                    Code = sharedStCode,
                    WarehouseId = wh1.Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var transfer = new StockTransfer
                {
                    Code = sharedTrfCode,
                    SourceWarehouseId = wh1.Id,
                    DestinationWarehouseId = wh2.Id,
                    Status = StockTransferStatus.Draft,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var po = new PurchaseOrder
                {
                    Code = $"PO-{sharedPoExtId}",
                    SourceSystem = "SAP",
                    ExternalPoId = sharedPoExtId,
                    SupplierId = partner.Id,
                    WarehouseId = wh1.Id,
                    OrderDate = DateTime.UtcNow.Date,
                    Status = PurchaseOrderStatus.Open,
                    CreatedBy = user.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                var so = new SalesOrder
                {
                    OrderCode = sharedSoCode,
                    ExternalOrderId = sharedSoExtId,
                    CustomerId = partner.Id,
                    WarehouseId = wh1.Id,
                    Status = SalesOrderStatus.Released,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };

                db.ProductBarcodes.Add(barcode);
                db.InventoryStocks.Add(stock);
                db.ImportReceipts.Add(importReceipt);
                db.ExportReceipts.Add(exportReceipt);
                db.Stocktakes.Add(stocktake);
                db.StockTransfers.Add(transfer);
                db.PurchaseOrders.Add(po);
                db.SalesOrders.Add(so);
                await db.SaveChangesAsync();

                var soLine = new SalesOrderLine
                {
                    SalesOrderId = so.Id,
                    ExternalLineId = "LINE-1",
                    ProductId = product.Id,
                    OrderedQuantity = 10m,
                    UomCode = sharedUnitCode
                };
                db.SalesOrderLines.Add(soLine);
                await db.SaveChangesAsync();

                var reservation = new StockReservation
                {
                    ReservationCode = sharedRsvCode,
                    WarehouseId = wh1.Id,
                    ProductId = product.Id,
                    Quantity = 10m,
                    Status = StockReservationStatus.Allocated,
                    SourceType = "SalesOrder",
                    SourceId = so.Id,
                    SourceCode = so.OrderCode,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(2)
                };
                db.StockReservations.Add(reservation);
                await db.SaveChangesAsync();

                var allocation = new StockAllocation
                {
                    AllocationCode = sharedAlcCode,
                    ReservationId = reservation.Id,
                    WarehouseId = wh1.Id,
                    ProductId = product.Id,
                    LocationId = loc.Id,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 10m,
                    Status = StockAllocationStatus.Active,
                    AllocatedBy = user.Id,
                    AllocatedAt = DateTime.UtcNow
                };
                var backorder = new Backorder
                {
                    BackorderCode = sharedBoCode,
                    SalesOrderLineId = soLine.Id,
                    WarehouseId = wh1.Id,
                    Quantity = 5m,
                    Status = BackorderStatus.Open,
                    CreatedAt = DateTime.UtcNow
                };
                var pickingTask = new PickingTask
                {
                    TaskCode = $"PICK-{suffix}",
                    WarehouseId = wh1.Id,
                    SourceType = "SalesOrder",
                    SourceId = so.Id,
                    SourceCode = so.OrderCode,
                    Status = PickingTaskStatus.Open,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                db.StockAllocations.Add(allocation);
                db.Backorders.Add(backorder);
                db.PickingTasks.Add(pickingTask);
                await db.SaveChangesAsync();

                var packingSession = new PackingSession
                {
                    SessionCode = $"PACK-{suffix}",
                    PickingTaskId = pickingTask.Id,
                    WarehouseId = wh1.Id,
                    Status = PackingSessionStatus.Open,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                db.PackingSessions.Add(packingSession);
                await db.SaveChangesAsync();

                var hu = new HandlingUnit
                {
                    HuCode = $"HU-{suffix}",
                    Barcode = $"HUBC-{suffix}",
                    PackingSessionId = packingSession.Id,
                    WarehouseId = wh1.Id,
                    Type = HandlingUnitType.Carton,
                    Status = HandlingUnitStatus.Open,
                    Sscc = sharedSscc,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                var reasonCode = new ReceivingReasonCode
                {
                    Code = sharedReasonCode,
                    Name = "Shared Reason Code",
                    Category = "Quantity",
                    Version = 1,
                    IsActive = true,
                    EffectiveFromUtc = DateTime.UtcNow.Date
                };
                var idempRecord = new IdempotencyRecord
                {
                    UserId = user.Id,
                    CommandScope = "ImportReceipt.Create",
                    KeyHash = sharedIdempKeyHash,
                    RequestFingerprint = "fp-1",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 201,
                    ResponseBody = $"{{\"tenantId\":{tenantId}}}",
                    CorrelationId = $"corr-{tenantId}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };

                db.HandlingUnits.Add(hu);
                db.ReceivingReasonCodes.Add(reasonCode);
                db.IdempotencyRecords.Add(idempRecord);
                await db.SaveChangesAsync();

                wh1.TenantId.Should().Be(tenantId);
                product.TenantId.Should().Be(tenantId);
                importReceipt.TenantId.Should().Be(tenantId);
            }

            // Same-tenant duplicate code within Tenant A MUST still be rejected by SQL Server (2601/2627)
            await using (var dupDb = CreateTenantContext(tenantA))
            {
                dupDb.Warehouses.Add(new Warehouse { Code = sharedWhCode, Name = "Duplicate In Tenant A", IsActive = true });
                var act = () => dupDb.SaveChangesAsync();
                var ex = await act.Should().ThrowAsync<DbUpdateException>();
                ex.Which.InnerException.Should().BeOfType<SqlException>()
                    .Which.Number.Should().BeOneOf(2601, 2627);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantA, tenantB);
        }
    }

    [SqlServerFact]
    public async Task ZeroCrossTenantLeakage_ReadsAndMutations_AreStrictlyIsolatedAndBlocked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenantA = Random.Shared.Next(50_000, 70_000);
        var tenantB = tenantA + 1;

        int tenantAUserId = 0;
        int tenantAWarehouseId = 0;
        int tenantAProductId = 0;
        int tenantBWarehouseId = 0;
        int tenantBProductId = 0;

        try
        {
            await using (var dbA = CreateTenantContext(tenantA))
            {
                var role = await dbA.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin")
                           ?? new Role { RoleName = "Admin" };
                if (role.Id == 0)
                {
                    dbA.Roles.Add(role);
                    await dbA.SaveChangesAsync();
                }

                var userA = new User
                {
                    Username = $"admin_a_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = "Admin A",
                    RoleId = role.Id,
                    IsActive = true
                };
                var unitA = new Unit { Code = $"UA{suffix[..3]}", Name = "Unit A", DecimalPlaces = 0 };
                var whA = new Warehouse { Code = $"WHA-{suffix}", Name = "Warehouse A", IsActive = true };
                dbA.Users.Add(userA);
                dbA.Units.Add(unitA);
                dbA.Warehouses.Add(whA);
                await dbA.SaveChangesAsync();

                var prodA = new Product
                {
                    Code = $"SKUA-{suffix}",
                    Name = "Product A",
                    UnitId = unitA.Id,
                    IsActive = true
                };
                dbA.Products.Add(prodA);
                await dbA.SaveChangesAsync();

                tenantAUserId = userA.Id;
                tenantAWarehouseId = whA.Id;
                tenantAProductId = prodA.Id;
            }

            await using (var dbB = CreateTenantContext(tenantB))
            {
                var unitB = new Unit { Code = $"UB{suffix[..3]}", Name = "Unit B", DecimalPlaces = 0 };
                var whB = new Warehouse { Code = $"WHB-{suffix}", Name = "Warehouse B", IsActive = true };
                dbB.Units.Add(unitB);
                dbB.Warehouses.Add(whB);
                await dbB.SaveChangesAsync();

                var locB = new WarehouseLocation
                {
                    WarehouseId = whB.Id,
                    Code = "LEGACY",
                    Name = "Legacy B",
                    LocationType = WarehouseLocationType.Legacy,
                    IsPickable = true,
                    IsActive = true,
                    IsSystemManaged = true
                };
                var prodB = new Product
                {
                    Code = $"SKUB-{suffix}",
                    Name = "Product B",
                    UnitId = unitB.Id,
                    IsActive = true
                };
                dbB.WarehouseLocations.Add(locB);
                dbB.Products.Add(prodB);
                await dbB.SaveChangesAsync();

                dbB.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = whB.Id,
                    ProductId = prodB.Id,
                    LocationId = locB.Id,
                    Status = InventoryStatus.Available,
                    Quantity = 50m,
                    ReservedQuantity = 0m
                });
                await dbB.SaveChangesAsync();

                tenantBWarehouseId = whB.Id;
                tenantBProductId = prodB.Id;
            }

            // 1. Read isolation from Tenant A (even as GlobalAdmin in Tenant A)
            await using (var dbA = CreateTenantContext(tenantA))
            {
                var currentUserA = new StubTenantCurrentUser(tenantAUserId, $"admin_a_{suffix}", "Admin", isGlobalAdmin: true, tenantId: tenantA);
                var whAuthA = new WarehouseAuthorizationService(dbA, currentUserA);
                var uowA = new UnitOfWork(dbA);

                var productRepoA = new ProductRepository(dbA);
                var catalogRepoA = new ProductCatalogRepository(dbA);
                var catalogServiceA = new ProductCatalogService(catalogRepoA, productRepoA);
                var productServiceA = new ProductService(productRepoA, catalogServiceA);

                var allProductsA = (await productServiceA.GetAllProductsAsync()).ToList();
                allProductsA.Should().ContainSingle(p => p.Id == tenantAProductId);
                allProductsA.Should().NotContain(p => p.Id == tenantBProductId);

                (await productRepoA.GetByIdAsync(tenantBProductId)).Should().BeNull();
                var getProductBAct = () => productServiceA.GetProductByIdAsync(tenantBProductId);
                await getProductBAct.Should().ThrowAsync<NotFoundException>();

                var whRepoA = new WarehouseRepository(dbA);
                var stockRepoA = new InventoryStockRepository(dbA);
                var whServiceA = new WarehouseService(whRepoA, whAuthA, stockRepoA, currentUserA);
                var allWarehousesA = (await whServiceA.GetAllWarehousesAsync()).ToList();
                allWarehousesA.Should().ContainSingle(w => w.Id == tenantAWarehouseId);
                allWarehousesA.Should().NotContain(w => w.Id == tenantBWarehouseId);

                var getWhBAct = () => whServiceA.GetWarehouseByIdAsync(tenantBWarehouseId);
                await getWhBAct.Should().ThrowAsync<NotFoundException>();

                var reconServiceA = new InventoryReconciliationQueryService(dbA, whAuthA);
                var reconAct = () => reconServiceA.GetInvestigationAsync(tenantBWarehouseId, tenantBProductId);
                await reconAct.Should().ThrowAsync<NotFoundException>();
            }

            // 2. Write / Delete / TenantId-tampering isolation in ErpKhoDbContext.SaveChangesAsync
            Warehouse detachedTenantBWarehouse;
            await using (var dbB = CreateTenantContext(tenantB))
            {
                detachedTenantBWarehouse = await dbB.Warehouses.AsNoTracking().SingleAsync(w => w.Id == tenantBWarehouseId);
            }

            await using (var dbA = CreateTenantContext(tenantA))
            {
                detachedTenantBWarehouse.Name = "Spoofed By Tenant A";
                dbA.Warehouses.Update(detachedTenantBWarehouse);
                var updateAct = () => dbA.SaveChangesAsync();
                await updateAct.Should().ThrowAsync<ForbiddenException>();
            }

            await using (var dbA = CreateTenantContext(tenantA))
            {
                dbA.Warehouses.Remove(detachedTenantBWarehouse);
                var deleteAct = () => dbA.SaveChangesAsync();
                await deleteAct.Should().ThrowAsync<ForbiddenException>();
            }

            await using (var dbA = CreateTenantContext(tenantA))
            {
                var ownWh = await dbA.Warehouses.SingleAsync(w => w.Id == tenantAWarehouseId);
                ownWh.TenantId = tenantB;
                var mutateTenantAct = () => dbA.SaveChangesAsync();
                await mutateTenantAct.Should().ThrowAsync<ForbiddenException>();
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantA, tenantB);
        }
    }

    [SqlServerFact]
    public async Task ConcurrentCrossTenantLockIsolation_TenantALockDoesNotBlockTenantBInventoryMutation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenantA = Random.Shared.Next(70_000, 85_000);
        var tenantB = tenantA + 1;

        int whAId = 0, prodAId = 0;
        int whBId = 0, prodBId = 0;

        try
        {
            foreach (var (tenantId, isA) in new[] { (tenantA, true), (tenantB, false) })
            {
                await using var db = CreateTenantContext(tenantId);
                var unit = new Unit { Code = $"UL{suffix[..3]}", Name = "Unit", DecimalPlaces = 0 };
                var wh = new Warehouse { Code = $"WH-LK-{suffix}", Name = $"Warehouse Lock {tenantId}", IsActive = true };
                db.Units.Add(unit);
                db.Warehouses.Add(wh);
                await db.SaveChangesAsync();

                var loc = new WarehouseLocation
                {
                    WarehouseId = wh.Id,
                    Code = "BIN-LK",
                    Name = "Lock Bin",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var prod = new Product
                {
                    Code = $"SKU-LK-{suffix}",
                    Name = $"Product Lock {tenantId}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                db.WarehouseLocations.Add(loc);
                db.Products.Add(prod);
                await db.SaveChangesAsync();

                db.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = wh.Id,
                    ProductId = prod.Id,
                    LocationId = loc.Id,
                    Status = InventoryStatus.Available,
                    Quantity = 100m,
                    ReservedQuantity = 0m
                });
                await db.SaveChangesAsync();

                if (isA)
                {
                    whAId = wh.Id;
                    prodAId = prod.Id;
                }
                else
                {
                    whBId = wh.Id;
                    prodBId = prod.Id;
                }
            }

            await using var dbHoldA = CreateTenantContext(tenantA);
            await using var txA = await dbHoldA.Database.BeginTransactionAsync();
            await dbHoldA.AcquireInventoryAllocationLocksAsync([(whAId, prodAId)]);

            var sw = Stopwatch.StartNew();
            await using (var dbB = CreateTenantContext(tenantB))
            {
                await using var txB = await dbB.Database.BeginTransactionAsync();
                var stockRepoB = new InventoryStockRepository(dbB);
                var reserved = await stockRepoB.TryReserveAsync(prodBId, whBId, 25m);
                reserved.Should().BeTrue();
                await txB.CommitAsync();
            }
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
                "Tenant B inventory allocation lock and row lock must not be blocked by Tenant A's active transaction lock");

            await txA.RollbackAsync();

            await using (var verifyB = CreateTenantContext(tenantB))
            {
                var stockB = await verifyB.InventoryStocks.SingleAsync(x => x.WarehouseId == whBId && x.ProductId == prodBId);
                stockB.ReservedQuantity.Should().Be(25m);
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantA, tenantB);
        }
    }

    [SqlServerFact]
    public async Task IdempotencyAndWarehouseAuthorizationCache_AreIsolatedAcrossTenants()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenantA = Random.Shared.Next(85_000, 99_000);
        var tenantB = tenantA + 1;
        var sharedKeyHash = $"IDEMP-SHARED-{suffix}";

        int userAId = 0;
        int userBId = 0;
        int whAId = 0;
        int whBId = 0;

        try
        {
            await using (var dbA = CreateTenantContext(tenantA))
            {
                var role = await dbA.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin")
                           ?? new Role { RoleName = "Admin" };
                if (role.Id == 0)
                {
                    dbA.Roles.Add(role);
                    await dbA.SaveChangesAsync();
                }

                var userA = new User
                {
                    Username = $"idemp_admin_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = "Idemp Admin A",
                    RoleId = role.Id,
                    IsActive = true
                };
                var whA = new Warehouse { Code = $"WH-CA-{suffix}", Name = "Cache WH A", IsActive = true };
                dbA.Users.Add(userA);
                dbA.Warehouses.Add(whA);
                await dbA.SaveChangesAsync();
                userAId = userA.Id;
                whAId = whA.Id;

                dbA.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    UserId = userAId,
                    CommandScope = "Warehouse.Create",
                    KeyHash = sharedKeyHash,
                    RequestFingerprint = "fp-shared",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 201,
                    ResponseBody = "{\"tenant\":\"A\",\"warehouseCode\":\"WH-A\"}",
                    CorrelationId = $"corr-A-{suffix}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                });
                await dbA.SaveChangesAsync();
            }

            await using (var dbB = CreateTenantContext(tenantB))
            {
                var role = await dbB.Roles.SingleAsync(r => r.RoleName == "Admin");
                var userB = new User
                {
                    Username = $"idemp_admin_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = "Idemp Admin B",
                    RoleId = role.Id,
                    IsActive = true
                };
                var whB = new Warehouse { Code = $"WH-CB-{suffix}", Name = "Cache WH B", IsActive = true };
                dbB.Users.Add(userB);
                dbB.Warehouses.Add(whB);
                await dbB.SaveChangesAsync();
                userBId = userB.Id;
                whBId = whB.Id;

                dbB.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    UserId = userBId,
                    CommandScope = "Warehouse.Create",
                    KeyHash = sharedKeyHash,
                    RequestFingerprint = "fp-shared",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 201,
                    ResponseBody = "{\"tenant\":\"B\",\"warehouseCode\":\"WH-B\"}",
                    CorrelationId = $"corr-B-{suffix}",
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                });
                await dbB.SaveChangesAsync();
            }

            await using (var dbA = CreateTenantContext(tenantA))
            await using (var dbB = CreateTenantContext(tenantB))
            {
                var recordA = await dbA.IdempotencyRecords.SingleAsync(
                    x => x.CommandScope == "Warehouse.Create" && x.KeyHash == sharedKeyHash);
                var recordB = await dbB.IdempotencyRecords.SingleAsync(
                    x => x.CommandScope == "Warehouse.Create" && x.KeyHash == sharedKeyHash);

                recordA.ResponseBody.Should().Be("{\"tenant\":\"A\",\"warehouseCode\":\"WH-A\"}");
                recordB.ResponseBody.Should().Be("{\"tenant\":\"B\",\"warehouseCode\":\"WH-B\"}");

                var adminA = new StubTenantCurrentUser(userAId, "admin_a", "Admin", isGlobalAdmin: true, tenantId: tenantA);
                var adminB = new StubTenantCurrentUser(userBId, "admin_b", "Admin", isGlobalAdmin: true, tenantId: tenantB);

                var authA = new WarehouseAuthorizationService(dbA, adminA);
                var authB = new WarehouseAuthorizationService(dbB, adminB);

                var accessibleA = await authA.GetAccessibleWarehouseIdsAsync();
                var accessibleB = await authB.GetAccessibleWarehouseIdsAsync();

                accessibleA.Should().Contain(whAId).And.NotContain(whBId);
                accessibleB.Should().Contain(whBId).And.NotContain(whAId);

                (await authA.CanAccessWarehouseAsync(whBId)).Should().BeFalse();
                (await authB.CanAccessWarehouseAsync(whAId)).Should().BeFalse();
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenantA, tenantB);
        }
    }

    private static async Task CleanupTenantsAsync(params int[] tenantIds)
    {
        await using var sysDb = CreateTenantContext(1, "SYSTEM", isSystemBypass: true);
        foreach (var tid in tenantIds)
        {
            await sysDb.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM dbo.HandlingUnitContents WHERE TenantId = {tid};
                DELETE FROM dbo.HandlingUnits WHERE TenantId = {tid};
                DELETE FROM dbo.PackingSessions WHERE TenantId = {tid};
                DELETE FROM dbo.ShortPickExceptions WHERE TenantId = {tid};
                DELETE FROM dbo.PickingTaskLines WHERE TenantId = {tid};
                DELETE FROM dbo.PickingTasks WHERE TenantId = {tid};
                DELETE FROM dbo.StockAllocations WHERE TenantId = {tid};
                DELETE FROM dbo.StockReservations WHERE TenantId = {tid};
                DELETE FROM dbo.Backorders WHERE TenantId = {tid};
                DELETE FROM dbo.SalesOrderLines WHERE TenantId = {tid};
                DELETE FROM dbo.SalesOrders WHERE TenantId = {tid};
                DELETE FROM dbo.PurchaseOrderLines WHERE TenantId = {tid};
                DELETE FROM dbo.PurchaseOrders WHERE TenantId = {tid};
                DELETE FROM dbo.StockTransferDetails WHERE TenantId = {tid};
                DELETE FROM dbo.StockTransfers WHERE TenantId = {tid};
                DELETE FROM dbo.StocktakeDetails WHERE TenantId = {tid};
                DELETE FROM dbo.Stocktakes WHERE TenantId = {tid};
                DELETE FROM dbo.ExportReceiptDetails WHERE TenantId = {tid};
                DELETE FROM dbo.ExportReceipts WHERE TenantId = {tid};
                DELETE FROM dbo.ImportReceiptInventoryIdentities WHERE TenantId = {tid};
                DELETE FROM dbo.ImportReceiptDetails WHERE TenantId = {tid};
                DELETE FROM dbo.ImportReceipts WHERE TenantId = {tid};
                DELETE FROM dbo.InventoryTransactions WHERE TenantId = {tid};
                DELETE FROM dbo.InventoryStocks WHERE TenantId = {tid};
                DELETE FROM dbo.ProductBarcodes WHERE TenantId = {tid};
                DELETE FROM dbo.Products WHERE TenantId = {tid};
                DELETE FROM dbo.ProductCategories WHERE TenantId = {tid};
                DELETE FROM dbo.Units WHERE TenantId = {tid};
                DELETE FROM dbo.BusinessPartners WHERE TenantId = {tid};
                DELETE FROM dbo.WarehouseLocations WHERE TenantId = {tid};
                DELETE FROM dbo.UserWarehouses WHERE TenantId = {tid};
                DELETE FROM dbo.Warehouses WHERE TenantId = {tid};
                DELETE FROM dbo.ReceivingReasonCodes WHERE TenantId = {tid};
                DELETE FROM dbo.IdempotencyRecords WHERE TenantId = {tid};
                DELETE FROM dbo.AuditLogs WHERE TenantId = {tid};
                DELETE FROM dbo.UserSessions WHERE TenantId = {tid};
                DELETE FROM dbo.Users WHERE TenantId = {tid};
                """);
        }
    }
}
