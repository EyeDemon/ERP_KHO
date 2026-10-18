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

    [SqlServerFact]
    public async Task Wave11_NonDefaultTenantAuthRefreshAndCrossTenantForeignKeyGuard_AreEnforcedOnSqlServer()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenant101 = Random.Shared.Next(100_000, 120_000);
        var tenant202 = tenant101 + 101;
        var sharedAdminUsername = $"admin_{suffix.ToLowerInvariant()}";
        const string pass101 = "Tenant101#Wave11!";
        const string pass202 = "Tenant202#Wave11!";

        var hasher = new PasswordHasherService();
        int unit101Id = 0;
        int cat101Id = 0;
        int wh101Id = 0;
        int unit202Id = 0;
        int cat202Id = 0;

        try
        {
            await using (var db101 = CreateTenantContext(tenant101, $"TENANT-{tenant101}"))
            {
                var role = await db101.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin")
                           ?? new Role { RoleName = "Admin" };
                if (role.Id == 0)
                {
                    db101.Roles.Add(role);
                    await db101.SaveChangesAsync();
                }

                var u101 = new User
                {
                    Username = sharedAdminUsername,
                    PasswordHash = hasher.HashPassword(pass101),
                    FullName = "Admin 101",
                    RoleId = role.Id,
                    IsActive = true
                };
                var unit101 = new Unit { Code = $"U101-{suffix[..3]}", Name = "Unit 101", DecimalPlaces = 0, IsActive = true };
                var cat101 = new ProductCategory { Code = $"C101-{suffix}", Name = "Cat 101", IsActive = true };
                var wh101 = new Warehouse { Code = $"WH101-{suffix}", Name = "WH 101", IsActive = true };
                db101.Users.Add(u101);
                db101.Units.Add(unit101);
                db101.ProductCategories.Add(cat101);
                db101.Warehouses.Add(wh101);
                await db101.SaveChangesAsync();

                unit101Id = unit101.Id;
                cat101Id = cat101.Id;
                wh101Id = wh101.Id;
            }

            await using (var db202 = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var role = await db202.Roles.SingleAsync(r => r.RoleName == "Admin");
                var u202 = new User
                {
                    Username = sharedAdminUsername,
                    PasswordHash = hasher.HashPassword(pass202),
                    FullName = "Admin 202",
                    RoleId = role.Id,
                    IsActive = true
                };
                var unit202 = new Unit { Code = $"U202-{suffix[..3]}", Name = "Unit 202", DecimalPlaces = 0, IsActive = true };
                var cat202 = new ProductCategory { Code = $"C202-{suffix}", Name = "Cat 202", IsActive = true };
                db202.Users.Add(u202);
                db202.Units.Add(unit202);
                db202.ProductCategories.Add(cat202);
                await db202.SaveChangesAsync();

                unit202Id = unit202.Id;
                cat202Id = cat202.Id;
            }

            // 1. Login and Refresh when DbContext is in anonymous DefaultTenantId = 1 context
            var jwtSection = new Moq.Mock<Microsoft.Extensions.Configuration.IConfigurationSection>();
            jwtSection.Setup(x => x["Secret"]).Returns("wave11_sql_integration_test_secret_key_at_least_32_bytes_long");
            jwtSection.Setup(x => x["Issuer"]).Returns("ErpKhoApi");
            jwtSection.Setup(x => x["Audience"]).Returns("ErpKhoClient");
            jwtSection.Setup(x => x["ExpiryMinutes"]).Returns("30");
            var config = new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            config.Setup(x => x.GetSection("JwtSettings")).Returns(jwtSection.Object);
            var tokenService = new TokenService(config.Object);

            string refreshToken202;
            await using (var anonymousLoginDb = CreateTenantContext(TenantContext.DefaultTenantId, TenantContext.DefaultTenantCode))
            {
                var userRepo = new UserRepository(anonymousLoginDb);
                var sessionService = new UserSessionService(
                    anonymousLoginDb,
                    tokenService,
                    new StubTenantCurrentUser(0, "anonymous", "", false, TenantContext.DefaultTenantId),
                    new SessionSecurityOptions());
                var authService = new AuthService(userRepo, hasher, tokenService, new AuthSecurityOptions(), null, null, sessionService);

                var loginResult = await authService.LoginAsync(new LoginRequestDto
                {
                    Username = sharedAdminUsername,
                    Password = pass202,
                    TenantId = tenant202
                });

                loginResult.TenantId.Should().Be(tenant202);
                loginResult.TenantCode.Should().Be($"TENANT-{tenant202}");
                loginResult.RefreshToken.Should().NotBeNullOrWhiteSpace();
                refreshToken202 = loginResult.RefreshToken;
            }

            await using (var anonymousRefreshDb = CreateTenantContext(TenantContext.DefaultTenantId, TenantContext.DefaultTenantCode))
            {
                var sessionService = new UserSessionService(
                    anonymousRefreshDb,
                    tokenService,
                    new StubTenantCurrentUser(0, "anonymous", "", false, TenantContext.DefaultTenantId),
                    new SessionSecurityOptions());

                var rotated = await sessionService.RefreshAsync(refreshToken202, new SessionContextDto());
                rotated.AccessToken.Should().NotBeNullOrWhiteSpace();
                var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(rotated.AccessToken);
                jwt.Claims.First(c => c.Type == TenantClaimTypes.TenantId).Value.Should().Be(tenant202.ToString());
                jwt.Claims.First(c => c.Type == TenantClaimTypes.TenantCode).Value.Should().Be($"TENANT-{tenant202}");

                await sessionService.LogoutAsync(rotated.RefreshToken, null);
            }

            // 2. Cross-Tenant Foreign Key Reference Injection on Added & Modified entities in Tenant 202 -> ForbiddenException (403)
            await using (var db202 = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                db202.Products.Add(new Product
                {
                    Code = $"SKU-FK-UNIT-{suffix}",
                    Name = "Cross Tenant Unit FK",
                    UnitId = unit101Id, // Belongs to Tenant 101!
                    CategoryId = cat202Id,
                    IsActive = true
                });

                var actUnit = () => db202.SaveChangesAsync();
                await actUnit.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
                db202.ChangeTracker.Clear();

                db202.Products.Add(new Product
                {
                    Code = $"SKU-FK-CAT-{suffix}",
                    Name = "Cross Tenant Category FK",
                    UnitId = unit202Id,
                    CategoryId = cat101Id, // Belongs to Tenant 101!
                    IsActive = true
                });

                var actCat = () => db202.SaveChangesAsync();
                await actCat.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
                db202.ChangeTracker.Clear();

                db202.WarehouseLocations.Add(new WarehouseLocation
                {
                    WarehouseId = wh101Id, // Belongs to Tenant 101!
                    Code = $"LOC-FK-{suffix}",
                    Name = "Cross Tenant Location",
                    IsActive = true
                });

                var actLoc = () => db202.SaveChangesAsync();
                await actLoc.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
                db202.ChangeTracker.Clear();

                // Valid Tenant 202 product creation succeeds, then modifying its UnitId to Tenant 101's UnitId is blocked
                var validProd = new Product
                {
                    Code = $"SKU-OK-{suffix}",
                    Name = "Valid Product 202",
                    UnitId = unit202Id,
                    CategoryId = cat202Id,
                    IsActive = true
                };
                db202.Products.Add(validProd);
                await db202.SaveChangesAsync();

                validProd.UnitId = unit101Id;
                var actModifyFk = () => db202.SaveChangesAsync();
                await actModifyFk.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenant101, tenant202);
        }
    }

    [SqlServerFact]
    public async Task Wave12_BatchedForeignKeyTenantValidationAndPerTenantLastAdminGuard_AreEnforcedOnSqlServer()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tenant101 = Random.Shared.Next(125_000, 140_000);
        var tenant202 = tenant101 + 101;

        int prod101Id = 0;
        int unit202Id = 0;
        int wh202Id = 0;
        int supplier202Id = 0;
        int admin202Id = 0;
        var prod202Ids = new List<int>();

        try
        {
            await using (var db101 = CreateTenantContext(tenant101, $"TENANT-{tenant101}"))
            {
                var adminRole = await EnsureAdminRoleWithRequiredPermissionsAsync(db101);
                var u101 = new User
                {
                    Username = $"w12_admin_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash101",
                    FullName = "Admin 101",
                    RoleId = adminRole.Id,
                    IsActive = true
                };
                var unit101 = new Unit { Code = $"W12U1-{suffix[..3]}", Name = "Unit 101", DecimalPlaces = 0, IsActive = true };
                var cat101 = new ProductCategory { Code = $"W12C1-{suffix}", Name = "Cat 101", IsActive = true };
                db101.Users.Add(u101);
                db101.Units.Add(unit101);
                db101.ProductCategories.Add(cat101);
                await db101.SaveChangesAsync();

                var p101 = new Product
                {
                    Code = $"W12-P101-{suffix}",
                    Name = "Product 101",
                    UnitId = unit101.Id,
                    CategoryId = cat101.Id,
                    IsActive = true
                };
                db101.Products.Add(p101);
                await db101.SaveChangesAsync();
                prod101Id = p101.Id;
            }

            await using (var db202 = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var adminRole = await EnsureAdminRoleWithRequiredPermissionsAsync(db202);
                var u202 = new User
                {
                    Username = $"w12_admin_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash202",
                    FullName = "Admin 202",
                    RoleId = adminRole.Id,
                    IsActive = true
                };
                var unit202 = new Unit { Code = $"W12U2-{suffix[..3]}", Name = "Unit 202", DecimalPlaces = 0, IsActive = true };
                var cat202 = new ProductCategory { Code = $"W12C2-{suffix}", Name = "Cat 202", IsActive = true };
                var wh202 = new Warehouse { Code = $"W12WH2-{suffix}", Name = "WH 202", IsActive = true };
                var sup202 = new BusinessPartner
                {
                    Code = $"W12SUP-{suffix}",
                    Name = "Supplier 202",
                    IsSupplier = true,
                    IsCustomer = true,
                    IsActive = true
                };

                db202.Users.Add(u202);
                db202.Units.Add(unit202);
                db202.ProductCategories.Add(cat202);
                db202.Warehouses.Add(wh202);
                db202.BusinessPartners.Add(sup202);
                await db202.SaveChangesAsync();

                admin202Id = u202.Id;
                unit202Id = unit202.Id;
                wh202Id = wh202.Id;
                supplier202Id = sup202.Id;

                for (var i = 1; i <= 20; i++)
                {
                    db202.Products.Add(new Product
                    {
                        Code = $"W12-P202-{i:D2}-{suffix}",
                        Name = $"Product 202 #{i}",
                        UnitId = unit202Id,
                        CategoryId = cat202.Id,
                        IsActive = true
                    });
                }
                await db202.SaveChangesAsync();

                prod202Ids = await db202.Products
                    .OrderBy(p => p.Code)
                    .Select(p => p.Id)
                    .ToListAsync();
                prod202Ids.Should().HaveCount(20);
            }

            // 1. Batch 20-line SalesOrder SaveChangesAsync in a fresh DbContext (empty ChangeTracker)
            int soOkId;
            await using (var dbBatchOk = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var so = new SalesOrder
                {
                    OrderCode = $"SO-W12-OK-{suffix}",
                    ExternalOrderId = $"EXT-W12-OK-{suffix}",
                    CustomerId = supplier202Id,
                    WarehouseId = wh202Id,
                    Status = SalesOrderStatus.Released,
                    CreatedBy = admin202Id,
                    CreatedAt = DateTime.UtcNow
                };
                dbBatchOk.SalesOrders.Add(so);
                await dbBatchOk.SaveChangesAsync();
                soOkId = so.Id;
            }

            await using (var dbBatchLinesOk = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var lines = new List<SalesOrderLine>();
                for (var i = 0; i < 20; i++)
                {
                    lines.Add(new SalesOrderLine
                    {
                        SalesOrderId = soOkId,
                        ExternalLineId = $"L-{i + 1:D2}",
                        ProductId = prod202Ids[i],
                        OrderedQuantity = 10m + i,
                        UomCode = $"W12U2-{suffix[..3]}"
                    });
                }

                dbBatchLinesOk.SalesOrderLines.AddRange(lines);
                await dbBatchLinesOk.SaveChangesAsync();

                lines.Should().HaveCount(20);
                lines.Should().OnlyContain(l => l.TenantId == tenant202);
            }

            // 2. Batch 20-line SalesOrderLines SaveChangesAsync where line #20 injects Tenant 101's ProductId -> 403 ForbiddenException
            int soBadId;
            await using (var dbBatchBadHeader = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var badSo = new SalesOrder
                {
                    OrderCode = $"SO-W12-BAD-{suffix}",
                    ExternalOrderId = $"EXT-W12-BAD-{suffix}",
                    CustomerId = supplier202Id,
                    WarehouseId = wh202Id,
                    Status = SalesOrderStatus.Released,
                    CreatedBy = admin202Id,
                    CreatedAt = DateTime.UtcNow
                };
                dbBatchBadHeader.SalesOrders.Add(badSo);
                await dbBatchBadHeader.SaveChangesAsync();
                soBadId = badSo.Id;
            }

            await using (var dbBatchBad = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                for (var i = 0; i < 19; i++)
                {
                    dbBatchBad.SalesOrderLines.Add(new SalesOrderLine
                    {
                        SalesOrderId = soBadId,
                        ExternalLineId = $"BL-{i + 1:D2}",
                        ProductId = prod202Ids[i],
                        OrderedQuantity = 5m,
                        UomCode = $"W12U2-{suffix[..3]}"
                    });
                }

                // Line 20 injects foreign ProductId from Tenant 101
                dbBatchBad.SalesOrderLines.Add(new SalesOrderLine
                {
                    SalesOrderId = soBadId,
                    ExternalLineId = "BL-20",
                    ProductId = prod101Id,
                    OrderedQuantity = 5m,
                    UomCode = $"W12U2-{suffix[..3]}"
                });

                var actBadBatch = () => dbBatchBad.SaveChangesAsync();
                await actBadBatch.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
            }

            // 2B. Simultaneous Parent + Child SaveChangesAsync (temporary Identity keys Id <= 0) for PurchaseOrder+Lines and PartnerDebtInvoice+Ledger
            await using (var dbSimulOk = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var poSimul = new PurchaseOrder
                {
                    Code = $"PO-W12-SIMUL-{suffix}",
                    SourceSystem = "ERP",
                    ExternalPoId = $"EXT-PO-SIMUL-{suffix}",
                    SupplierId = supplier202Id,
                    WarehouseId = wh202Id,
                    OrderDate = DateTime.UtcNow.Date,
                    Status = PurchaseOrderStatus.Open,
                    CreatedBy = admin202Id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                var poLineSimul = new PurchaseOrderLine
                {
                    PurchaseOrder = poSimul,
                    ExternalLineId = "PO-L-01",
                    LineNo = 1,
                    ProductId = prod202Ids[0],
                    OrderedQuantity = 15m,
                    OperationUnitId = unit202Id,
                    OperationUnitCodeSnapshot = $"W12U2-{suffix[..3]}",
                    BaseUnitId = unit202Id,
                    BaseUnitCodeSnapshot = $"W12U2-{suffix[..3]}",
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseOrderedQuantity = 15m,
                    UnitPrice = 100m,
                    TaxPercent = 10m
                };

                var debtInvSimul = new PartnerDebtInvoice
                {
                    Code = $"AP-W12-SIMUL-{suffix}",
                    PartnerId = supplier202Id,
                    DebtType = PartnerDebtType.AP,
                    SourceType = "ImportReceipt",
                    SourceId = 999999,
                    SourceCode = $"IR-SIMUL-{suffix}",
                    SubTotalAmount = 1500m,
                    TaxAmount = 150m,
                    TotalAmount = 1650m,
                    PaidAmount = 0m,
                    RemainingAmount = 1650m,
                    Status = PartnerDebtInvoiceStatus.Open,
                    IssuedAtUtc = DateTime.UtcNow,
                    CreatedBy = admin202Id
                };
                var debtLedgerSimul = new PartnerDebtLedger
                {
                    PartnerId = supplier202Id,
                    Invoice = debtInvSimul,
                    DebtType = PartnerDebtType.AP,
                    TransactionType = PartnerDebtTransactionType.InvoiceCreated,
                    ReferenceType = "ImportReceipt",
                    ReferenceId = 999999,
                    ReferenceCode = $"IR-SIMUL-{suffix}",
                    AmountDelta = 1650m,
                    BalanceAfter = 1650m,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = admin202Id
                };

                dbSimulOk.PurchaseOrders.Add(poSimul);
                dbSimulOk.PurchaseOrderLines.Add(poLineSimul);
                dbSimulOk.PartnerDebtInvoices.Add(debtInvSimul);
                dbSimulOk.PartnerDebtLedgers.Add(debtLedgerSimul);

                await dbSimulOk.SaveChangesAsync();

                poSimul.Id.Should().BeGreaterThan(0);
                poLineSimul.Id.Should().BeGreaterThan(0);
                poLineSimul.PurchaseOrderId.Should().Be(poSimul.Id);
                poLineSimul.TenantId.Should().Be(tenant202);

                debtInvSimul.Id.Should().BeGreaterThan(0);
                debtLedgerSimul.Id.Should().BeGreaterThan(0);
                debtLedgerSimul.InvoiceId.Should().Be(debtInvSimul.Id);
                debtLedgerSimul.TenantId.Should().Be(tenant202);
            }

            // Simultaneous Parent + Child where child references cross-tenant ProductId or cross-tenant Parent navigation -> 403 ForbiddenException
            await using (var dbSimulBad = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                var poBad = new PurchaseOrder
                {
                    Code = $"PO-W12-SIMUL-BAD-{suffix}",
                    SourceSystem = "ERP",
                    ExternalPoId = $"EXT-PO-SIMUL-BAD-{suffix}",
                    SupplierId = supplier202Id,
                    WarehouseId = wh202Id,
                    OrderDate = DateTime.UtcNow.Date,
                    Status = PurchaseOrderStatus.Open,
                    CreatedBy = admin202Id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                var poLineBad = new PurchaseOrderLine
                {
                    PurchaseOrder = poBad,
                    ExternalLineId = "PO-BL-01",
                    LineNo = 1,
                    ProductId = prod101Id, // Belongs to Tenant 101!
                    OrderedQuantity = 5m,
                    OperationUnitId = unit202Id,
                    OperationUnitCodeSnapshot = $"W12U2-{suffix[..3]}",
                    BaseUnitId = unit202Id,
                    BaseUnitCodeSnapshot = $"W12U2-{suffix[..3]}",
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseOrderedQuantity = 5m
                };

                dbSimulBad.PurchaseOrders.Add(poBad);
                dbSimulBad.PurchaseOrderLines.Add(poLineBad);

                var actSimulBad = () => dbSimulBad.SaveChangesAsync();
                await actSimulBad.Should().ThrowAsync<ForbiddenException>()
                    .WithMessage("*403 Cross-Tenant Reference Denied*");
            }

            // 3. Per-Tenant Last Active Admin Guard: deactivating Tenant 202's sole admin fails even though Tenant 101 has an active admin
            await using (var dbGuard202 = CreateTenantContext(tenant202, $"TENANT-{tenant202}"))
            {
                await using var tx = await dbGuard202.Database.BeginTransactionAsync();
                await PermissionAdministrationGuard.LockAsync(dbGuard202, CancellationToken.None);

                // While admin202 is active, EnsureAdministratorAsync succeeds for Tenant 202
                await PermissionAdministrationGuard.EnsureAdministratorAsync(dbGuard202, CancellationToken.None);

                var soleAdmin202 = await dbGuard202.Users.SingleAsync(u => u.Id == admin202Id);
                soleAdmin202.IsActive = false;
                await dbGuard202.SaveChangesAsync();

                var actLastAdmin = () => PermissionAdministrationGuard.EnsureAdministratorAsync(dbGuard202, CancellationToken.None);
                await actLastAdmin.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                    .WithMessage("*quản trị viên hợp lệ cuối cùng*");

                await tx.RollbackAsync();
            }
        }
        finally
        {
            await CleanupTenantsAsync(tenant101, tenant202);
        }
    }

    private static async Task<Role> EnsureAdminRoleWithRequiredPermissionsAsync(ErpKhoDbContext db)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
        if (role is null)
        {
            role = new Role { RoleName = "Admin" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        foreach (var code in PermissionAdministrationGuard.RequiredPermissions)
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission(code, code);
                db.Permissions.Add(perm);
                await db.SaveChangesAsync();
            }

            var hasGrant = await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id);
            if (!hasGrant)
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
                await db.SaveChangesAsync();
            }
        }

        return role;
    }

    private static async Task CleanupTenantsAsync(params int[] tenantIds)
    {
        await using var sysDb = CreateTenantContext(1, "SYSTEM", isSystemBypass: true);
        foreach (var tid in tenantIds)
        {
            await sysDb.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM dbo.PartnerDebtLedgers WHERE TenantId = {tid};
                DELETE FROM dbo.PartnerDebtInvoices WHERE TenantId = {tid};
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
