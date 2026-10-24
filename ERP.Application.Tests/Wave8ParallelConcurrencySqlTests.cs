using System.Data;
using System.Security.Cryptography;
using System.Text;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class Wave8ParallelConcurrencySqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    [SqlServerFact]
    public async Task W8_01_And_W8_02_NonGlobalAdminCreator_ImmediatelyAccessesCreatedWarehouse_And_AuxiliaryHasTransactionsGuardsHold()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"U8{suffix.Substring(0, 4)}";
        int creatorId = 0;
        int createdWarehouseId = 0;
        int createdProductId = 0;

        try
        {
            await using (var setupDb = CreateContext())
            {
                var role = await setupDb.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager")
                           ?? new Role { RoleName = "Manager" };
                if (role.Id == 0)
                {
                    setupDb.Roles.Add(role);
                    await setupDb.SaveChangesAsync();
                }

                var creator = new User
                {
                    Username = $"w8_creator_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"Wave 8 Creator {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                setupDb.Users.Add(creator);
                await setupDb.SaveChangesAsync();
                creatorId = creator.Id;
            }

            // Use a SINGLE scoped context + WarehouseAuthorizationService to prove cache invalidation works
            // within the same scope AND across subsequent requests for a non-GlobalAdmin user.
            await using (var db = CreateContext())
            {
                var currentUser = new ScopedUser(creatorId, "Manager", IsGlobalAdmin: false);
                var warehouseAuth = new WarehouseAuthorizationService(db, currentUser);
                var warehouseRepo = new WarehouseRepository(db);
                var stockRepo = new InventoryStockRepository(db);
                var warehouseService = new WarehouseService(warehouseRepo, warehouseAuth, stockRepo, currentUser);

                // Warm the accessible-warehouse cache BEFORE creating the warehouse
                var beforeIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync();
                beforeIds.Should().BeEmpty();

                var created = await warehouseService.CreateWarehouseAsync(new CreateWarehouseDto
                {
                    Code = $"W8-WH-{suffix}",
                    Name = $"Kho Tự Cấp Quyền {suffix}",
                    Address = "KCN VSIP 1"
                }, $"w8_creator_{suffix.ToLowerInvariant()}");

                createdWarehouseId = created.Id;

                // 1ms after creation: non-GlobalAdmin creator MUST immediately pass GetWarehouseByIdAsync
                var fetched = await warehouseService.GetWarehouseByIdAsync(createdWarehouseId);
                fetched.Id.Should().Be(createdWarehouseId);
                fetched.Code.Should().Be($"W8-WH-{suffix}");

                var afterIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync();
                afterIds.Should().Contain(createdWarehouseId);

                // And non-GlobalAdmin creator MUST immediately pass PutawayService.CreateLocationAsync
                var putawayService = new PutawayService(db, warehouseAuth, currentUser);
                var createdBin = await putawayService.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = createdWarehouseId,
                    Code = "W8-BINA",
                    Name = "Kệ lưu trữ A",
                    LocationType = "Storage",
                    IsPickable = true
                });
                createdBin.Code.Should().Be("W8-BINA");

                // Verify UserWarehouses row exists in DB
                var accessRow = await db.UserWarehouses.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.UserId == creatorId && x.WarehouseId == createdWarehouseId);
                accessRow.Should().NotBeNull();

                // Verify W8-02: HandlingUnits and ImportReceiptInventoryIdentities block HasTransactionsAsync
                var unit = new Unit { Code = unitCode, Name = "Cái", DecimalPlaces = 0 };
                db.Units.Add(unit);
                await db.SaveChangesAsync();

                var productRepo = new ProductRepository(db);
                var product = new Product
                {
                    Code = $"W8-SKU-{suffix}",
                    Name = $"Sản phẩm W8 {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                await productRepo.AddAsync(product);
                createdProductId = product.Id;

                var receipt = new ImportReceipt
                {
                    Code = $"W8-IR-{suffix}",
                    WarehouseId = createdWarehouseId,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = creatorId
                };
                db.ImportReceipts.Add(receipt);
                await db.SaveChangesAsync();

                var detail = new ImportReceiptDetail
                {
                    ImportReceiptId = receipt.Id,
                    ProductId = createdProductId,
                    Quantity = 1,
                    ExpectedQuantity = 1,
                    ReceivedQuantity = 1,
                    AcceptedQuantity = 1,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactor = 1,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 1,
                    BaseReceivedQuantity = 1,
                    BaseAcceptedQuantity = 1
                };
                db.ImportReceiptDetails.Add(detail);
                await db.SaveChangesAsync();

                var identity = new ImportReceiptInventoryIdentity
                {
                    ImportReceiptDetailId = detail.Id,
                    ProductId = createdProductId,
                    BaseQuantity = 1,
                    LotNumber = $"LOT-{suffix}",
                    TargetStatus = InventoryStatus.Available,
                    CreatedBy = creatorId
                };
                db.ImportReceiptInventoryIdentities.Add(identity);

                var pickTask = new PickingTask
                {
                    TaskCode = $"PCK-W8-{suffix}",
                    WarehouseId = createdWarehouseId,
                    SourceType = "SalesOrder",
                    SourceId = 1,
                    Status = PickingTaskStatus.Completed,
                    CreatedBy = creatorId
                };
                db.PickingTasks.Add(pickTask);
                await db.SaveChangesAsync();

                var packSession = new PackingSession
                {
                    SessionCode = $"PAK-W8-{suffix}",
                    PickingTaskId = pickTask.Id,
                    WarehouseId = createdWarehouseId,
                    Status = PackingSessionStatus.Open,
                    CreatedBy = creatorId
                };
                db.PackingSessions.Add(packSession);
                await db.SaveChangesAsync();

                var hu = new HandlingUnit
                {
                    HuCode = $"HU-W8-{suffix}",
                    Barcode = $"HU-W8-{suffix}",
                    Type = HandlingUnitType.Carton,
                    WarehouseId = createdWarehouseId,
                    PackingSessionId = packSession.Id,
                    Status = HandlingUnitStatus.Open,
                    CreatedBy = creatorId
                };
                db.HandlingUnits.Add(hu);
                await db.SaveChangesAsync();

                (await warehouseRepo.HasTransactionsAsync(createdWarehouseId)).Should().BeTrue();
                (await productRepo.HasTransactionsAsync(createdProductId)).Should().BeTrue();
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (createdWarehouseId > 0)
            {
                await cleanDb.ImportReceiptInventoryIdentities.Where(x => x.ProductId == createdProductId).ExecuteDeleteAsync();
                await cleanDb.ImportReceiptDetails.Where(x => x.ProductId == createdProductId).ExecuteDeleteAsync();
                await cleanDb.ImportReceipts.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.HandlingUnits.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.PackingSessions.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.PickingTasks.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.UserWarehouses.Where(x => x.WarehouseId == createdWarehouseId).ExecuteDeleteAsync();
                await cleanDb.Warehouses.Where(x => x.Id == createdWarehouseId).ExecuteDeleteAsync();
            }
            if (createdProductId > 0)
            {
                await cleanDb.Products.Where(x => x.Id == createdProductId).ExecuteDeleteAsync();
            }
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (creatorId > 0)
            {
                await cleanDb.Users.Where(x => x.Id == creatorId).ExecuteDeleteAsync();
            }
        }
    }

    [SqlServerFact]
    public async Task Race1_TenConcurrentPutawayWorkers_MovingTwoUnitsEach_FromTenUnitReceiptLine_SerializesExactlyFiveSuccesses()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"R1{suffix.Substring(0, 4)}";
        int userId = 0, warehouseId = 0, productId = 0, receivingId = 0, storageId = 0, receiptId = 0, taskId = 0, itemId = 0;
        string initialRowVersion = string.Empty;

        try
        {
            await using (var db = CreateContext())
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager") ?? new Role { RoleName = "Manager" };
                if (role.Id == 0) { db.Roles.Add(role); await db.SaveChangesAsync(); }

                var user = new User { Username = $"w8_r1_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R1", RoleId = role.Id, IsActive = true };
                var unit = new Unit { Code = unitCode, Name = "EA", DecimalPlaces = 0 };
                var wh = new Warehouse { Code = $"WH-R1-{suffix}", Name = $"Warehouse R1 {suffix}", IsActive = true };
                db.AddRange(user, unit, wh);
                await db.SaveChangesAsync();

                userId = user.Id;
                warehouseId = wh.Id;
                db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = warehouseId, CreatedBy = userId });

                var receiving = new WarehouseLocation { WarehouseId = warehouseId, Code = "RECEIVING", Name = "Nhận", LocationType = WarehouseLocationType.Receiving, IsActive = true, IsReceivable = true, IsSystemManaged = true };
                var storage = new WarehouseLocation { WarehouseId = warehouseId, Code = "BIN-R1-A", Name = "Kệ R1 A", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
                var product = new Product { Code = $"SKU-R1-{suffix}", Name = $"SKU R1 {suffix}", UnitId = unit.Id, IsActive = true };
                db.AddRange(receiving, storage, product);
                await db.SaveChangesAsync();

                receivingId = receiving.Id;
                storageId = storage.Id;
                productId = product.Id;

                var receipt = new ImportReceipt { Code = $"IR-R1-{suffix}", WarehouseId = warehouseId, Status = ReceiptStatus.Posted, CreatedBy = userId };
                db.ImportReceipts.Add(receipt);
                await db.SaveChangesAsync();
                receiptId = receipt.Id;

                var line = new ImportReceiptDetail
                {
                    ImportReceiptId = receiptId,
                    ProductId = productId,
                    Quantity = 10,
                    ExpectedQuantity = 10,
                    ReceivedQuantity = 10,
                    AcceptedQuantity = 10,
                    PostedQuantity = 10,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactor = 1,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 10,
                    BaseReceivedQuantity = 10,
                    BaseAcceptedQuantity = 10,
                    BasePostedQuantity = 10
                };
                db.ImportReceiptDetails.Add(line);
                await db.SaveChangesAsync();

                db.ImportReceiptInventoryIdentities.Add(new ImportReceiptInventoryIdentity
                {
                    ImportReceiptDetailId = line.Id,
                    ProductId = productId,
                    BaseQuantity = 10,
                    TargetStatus = InventoryStatus.Available,
                    CreatedBy = userId
                });

                db.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = receivingId,
                    ProductId = productId,
                    Status = InventoryStatus.Available,
                    Quantity = 10,
                    ReservedQuantity = 0
                });

                var task = new PutawayTask
                {
                    ReceiptId = receiptId,
                    WarehouseId = warehouseId,
                    Status = PutawayTaskStatus.Assigned,
                    AssignedUserId = userId,
                    CreatedBy = userId
                };
                var item = new PutawayTaskItem
                {
                    PutawayTask = task,
                    ReceiptLineId = line.Id,
                    ProductId = productId,
                    InventoryStatus = InventoryStatus.Available,
                    SourceLocationId = receivingId,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1,
                    ConversionVersionSnapshot = 1,
                    BaseUnitDecimalPlaces = 0,
                    RequiredOperationQuantity = 10,
                    RequiredBaseQuantity = 10,
                    MovedBaseQuantity = 0
                };
                db.PutawayTasks.Add(task);
                db.PutawayTaskItems.Add(item);
                await db.SaveChangesAsync();

                taskId = task.Id;
                itemId = item.Id;
                initialRowVersion = Convert.ToBase64String(task.RowVersion);
            }

            using var startGate = new ManualResetEventSlim(false);
            var workerTasks = Enumerable.Range(0, 10).Select(async _ =>
            {
                await Task.Yield();
                startGate.Wait();
                await using var workerDb = CreateContext();
                var currentUser = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, currentUser);
                var lockService = new InventoryLockService(workerDb, auth, currentUser);
                var putaway = new PutawayService(workerDb, auth, currentUser, lockService);

                try
                {
                    var dto = await putaway.MoveAsync(taskId, new MovePutawayItemDto
                    {
                        ItemId = itemId,
                        DestinationLocationId = storageId,
                        Quantity = 2m,
                        UnitCode = unitCode,
                        RowVersion = initialRowVersion
                    });
                    return (Succeeded: true, Error: (Exception?)null, Result: (PutawayTaskDto?)dto);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Error: (Exception?)ex, Result: (PutawayTaskDto?)null);
                }
            }).ToArray();

            startGate.Set();
            var outcomes = await Task.WhenAll(workerTasks);

            var successes = outcomes.Where(x => x.Succeeded).ToList();
            var failures = outcomes.Where(x => !x.Succeeded).ToList();

            successes.Should().HaveCount(5, "exactly 5 workers moving 2 units each can consume 10 units at RECEIVING");
            failures.Should().HaveCount(5, "remaining 5 workers must be rejected once 10 units are moved");
            failures.Select(x => x.Error).Should().OnlyContain(ex => ex is BusinessRuleException);

            await using (var verifyDb = CreateContext())
            {
                var receivingStock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == receivingId && x.ProductId == productId);
                receivingStock.Quantity.Should().Be(0m);

                var destStock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == storageId && x.ProductId == productId);
                destStock.Quantity.Should().Be(10m);

                var movements = await verifyDb.InventoryLocationMovements.AsNoTracking()
                    .Where(x => x.PutawayTaskId == taskId)
                    .ToListAsync();
                movements.Should().HaveCount(5);
                movements.Sum(x => x.BaseQuantity).Should().Be(10m);
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (warehouseId > 0)
            {
                await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.InventoryLocationMovements.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.InventoryTransactions.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.PutawayTaskItems.Where(x => x.PutawayTaskId == taskId).ExecuteDeleteAsync();
                await cleanDb.PutawayTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.ImportReceiptInventoryIdentities.Where(x => x.ProductId == productId).ExecuteDeleteAsync();
                await cleanDb.ImportReceiptDetails.Where(x => x.ProductId == productId).ExecuteDeleteAsync();
                await cleanDb.ImportReceipts.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            }
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task Race2_TenConcurrentSalesOrders_ReleasingAgainstLastFiveUnits_AllocatesFiveAndBackordersFive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"R2{suffix.Substring(0, 4)}";
        int userId = 0, warehouseId = 0, productId = 0, customerId = 0, binId = 0;
        var orders = new List<(int Id, string RowVersion)>();

        try
        {
            await using (var db = CreateContext())
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager") ?? new Role { RoleName = "Manager" };
                if (role.Id == 0) { db.Roles.Add(role); await db.SaveChangesAsync(); }

                var user = new User { Username = $"w8_r2_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R2", RoleId = role.Id, IsActive = true };
                var unit = new Unit { Code = unitCode, Name = "EA", DecimalPlaces = 0 };
                var wh = new Warehouse { Code = $"WH-R2-{suffix}", Name = $"Warehouse R2 {suffix}", IsActive = true };
                var customer = new BusinessPartner { Code = $"CUST-R2-{suffix}", Name = $"Customer R2 {suffix}", IsCustomer = true, IsActive = true };
                db.AddRange(user, unit, wh, customer);
                await db.SaveChangesAsync();

                userId = user.Id;
                warehouseId = wh.Id;
                customerId = customer.Id;
                db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = warehouseId, CreatedBy = userId });

                var bin = new WarehouseLocation { WarehouseId = warehouseId, Code = "WH1-BINA", Name = "Kệ A", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
                var product = new Product { Code = $"SKU-R2-{suffix}", Name = $"SKU R2 {suffix}", UnitId = unit.Id, IsActive = true };
                db.AddRange(bin, product);
                await db.SaveChangesAsync();

                binId = bin.Id;
                productId = product.Id;

                db.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = binId,
                    ProductId = productId,
                    Status = InventoryStatus.Available,
                    Quantity = 5m,
                    ReservedQuantity = 0m
                });
                await db.SaveChangesAsync();

                var soSvc = CreateDemandService(db, userId);
                for (var i = 1; i <= 10; i++)
                {
                    var createdSo = await soSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                    {
                        ExternalOrderId = $"EXT-R2-{suffix}-{i:00}",
                        CustomerId = customerId,
                        WarehouseId = warehouseId,
                        Priority = 1,
                        Lines =
                        [
                            new CreateSalesOrderLineDto
                            {
                                ExternalLineId = $"LINE-{i:00}",
                                ProductId = productId,
                                OrderedQuantity = 1m
                            }
                        ]
                    });
                    orders.Add((createdSo.Id, createdSo.RowVersion!));
                }
            }

            using var startGate = new ManualResetEventSlim(false);
            var releaseTasks = orders.Select(async orderInfo =>
            {
                await Task.Yield();
                startGate.Wait();
                await using var workerDb = CreateContext();
                var soSvc = CreateDemandService(workerDb, userId);

                return await soSvc.ReleaseSalesOrderAsync(orderInfo.Id, new SalesOrderStateCommandDto
                {
                    RowVersion = orderInfo.RowVersion,
                    Reason = "Concurrent release stress test"
                });
            }).ToArray();

            startGate.Set();
            var releasedOrders = await Task.WhenAll(releaseTasks);
            releasedOrders.Should().HaveCount(10);

            var reservedOrders = releasedOrders.Where(x => x.ReservedQuantity == 1m && x.BackorderQuantity == 0m).ToList();
            var backorderedOrders = releasedOrders.Where(x => x.ReservedQuantity == 0m && x.BackorderQuantity == 1m).ToList();

            reservedOrders.Should().HaveCount(5, "exactly 5 orders must reserve and allocate 1 unit each from the 5 available units");
            backorderedOrders.Should().HaveCount(5, "remaining 5 orders must cleanly transition to Backorder");

            await using (var verifyDb = CreateContext())
            {
                var stock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == binId && x.ProductId == productId);
                stock.Quantity.Should().Be(5m);
                stock.ReservedQuantity.Should().Be(5m);
                stock.ReservedQuantity.Should().BeLessThanOrEqualTo(stock.Quantity);

                var activeAllocations = await verifyDb.StockAllocations.AsNoTracking()
                    .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId)
                    .ToListAsync();
                activeAllocations.Should().HaveCount(5);
                activeAllocations.Sum(x => x.Quantity).Should().Be(5m);

                var openBackorders = await verifyDb.Backorders.AsNoTracking()
                    .Where(x => x.WarehouseId == warehouseId && x.Status == BackorderStatus.Open)
                    .ToListAsync();
                openBackorders.Should().HaveCount(5);
                openBackorders.Sum(x => x.Quantity).Should().Be(5m);
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (warehouseId > 0)
            {
                var orderIds = orders.Select(x => x.Id).ToArray();
                await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.PickingTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.StockAllocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.StockReservations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.Backorders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.SalesOrderLines.Where(x => orderIds.Contains(x.SalesOrderId)).ExecuteDeleteAsync();
                await cleanDb.SalesOrders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            }
            if (customerId > 0) await cleanDb.BusinessPartners.Where(x => x.Id == customerId).ExecuteDeleteAsync();
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task Race3_TwoConcurrentShortPickWorkers_CompetingForFourUnitsAtAlternativeLocation_GrantsOneAndRejectsOneWithoutStealingReservedStock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"R3{suffix.Substring(0, 4)}";
        int userId = 0, warehouseId = 0, productId = 0, srcBin1Id = 0, srcBin2Id = 0, altBinId = 0;
        int task1Id = 0, ex1Id = 0, task2Id = 0, ex2Id = 0;
        string task1RowVersion = string.Empty, task2RowVersion = string.Empty;

        try
        {
            await using (var db = CreateContext())
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager") ?? new Role { RoleName = "Manager" };
                if (role.Id == 0) { db.Roles.Add(role); await db.SaveChangesAsync(); }

                var user = new User { Username = $"w8_r3_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R3", RoleId = role.Id, IsActive = true };
                var unit = new Unit { Code = unitCode, Name = "EA", DecimalPlaces = 0 };
                var wh = new Warehouse { Code = $"WH-R3-{suffix}", Name = $"Warehouse R3 {suffix}", IsActive = true };
                db.AddRange(user, unit, wh);
                await db.SaveChangesAsync();

                userId = user.Id;
                warehouseId = wh.Id;
                db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = warehouseId, CreatedBy = userId });

                var src1 = new WarehouseLocation { WarehouseId = warehouseId, Code = "WH1-SRC-1", Name = "Nguồn 1", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
                var src2 = new WarehouseLocation { WarehouseId = warehouseId, Code = "WH1-SRC-2", Name = "Nguồn 2", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
                var alt = new WarehouseLocation { WarehouseId = warehouseId, Code = "WH1-ALT", Name = "Thay thế", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
                var product = new Product { Code = $"SKU-R3-{suffix}", Name = $"SKU R3 {suffix}", UnitId = unit.Id, IsActive = true };
                db.AddRange(src1, src2, alt, product);
                await db.SaveChangesAsync();

                srcBin1Id = src1.Id;
                srcBin2Id = src2.Id;
                altBinId = alt.Id;
                productId = product.Id;

                // WH1-SRC-1 has 3 reserved for Task 1; WH1-SRC-2 has 3 reserved for Task 2;
                // WH1-ALT has Quantity = 6, ReservedQuantity = 2 (2 reserved by another order, leaving exactly 4 unreserved available units).
                db.InventoryStocks.AddRange(
                    new InventoryStock { WarehouseId = warehouseId, LocationId = srcBin1Id, ProductId = productId, Status = InventoryStatus.Available, Quantity = 3m, ReservedQuantity = 3m },
                    new InventoryStock { WarehouseId = warehouseId, LocationId = srcBin2Id, ProductId = productId, Status = InventoryStatus.Available, Quantity = 3m, ReservedQuantity = 3m },
                    new InventoryStock { WarehouseId = warehouseId, LocationId = altBinId, ProductId = productId, Status = InventoryStatus.Available, Quantity = 6m, ReservedQuantity = 2m });

                var res1 = new StockReservation
                {
                    ReservationCode = $"RES-R3-1-{suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 9001,
                    SourceCode = $"SO-R3-1-{suffix}",
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    Quantity = 3m,
                    Status = StockReservationStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(2),
                    CreatedBy = userId
                };
                var res2 = new StockReservation
                {
                    ReservationCode = $"RES-R3-2-{suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 9002,
                    SourceCode = $"SO-R3-2-{suffix}",
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    Quantity = 3m,
                    Status = StockReservationStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(2),
                    CreatedBy = userId
                };
                db.StockReservations.AddRange(res1, res2);
                await db.SaveChangesAsync();

                var alloc1 = new StockAllocation
                {
                    AllocationCode = $"ALC-R3-1-{suffix}",
                    ReservationId = res1.Id,
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    LocationId = srcBin1Id,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 3m,
                    Status = StockAllocationStatus.Picking,
                    Strategy = AllocationStrategy.LocationOrder,
                    AllocatedBy = userId
                };
                var alloc2 = new StockAllocation
                {
                    AllocationCode = $"ALC-R3-2-{suffix}",
                    ReservationId = res2.Id,
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    LocationId = srcBin2Id,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 3m,
                    Status = StockAllocationStatus.Picking,
                    Strategy = AllocationStrategy.LocationOrder,
                    AllocatedBy = userId
                };
                db.StockAllocations.AddRange(alloc1, alloc2);
                await db.SaveChangesAsync();

                var task1 = new PickingTask
                {
                    TaskCode = $"PCK-R3-1-{suffix}",
                    WarehouseId = warehouseId,
                    PickingType = "STANDARD",
                    SourceType = "SalesOrder",
                    SourceId = 9001,
                    SourceCode = $"SO-R3-1-{suffix}",
                    Status = PickingTaskStatus.InProgress,
                    AssignedUserId = userId,
                    StartedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };
                var line1 = new PickingTaskLine
                {
                    PickingTask = task1,
                    AllocationId = alloc1.Id,
                    ProductId = productId,
                    SourceLocationId = srcBin1Id,
                    RequestedQuantity = 3m,
                    PickedQuantity = 0m,
                    Sequence = 1,
                    Status = PickingTaskLineStatus.Open
                };

                var task2 = new PickingTask
                {
                    TaskCode = $"PCK-R3-2-{suffix}",
                    WarehouseId = warehouseId,
                    PickingType = "STANDARD",
                    SourceType = "SalesOrder",
                    SourceId = 9002,
                    SourceCode = $"SO-R3-2-{suffix}",
                    Status = PickingTaskStatus.InProgress,
                    AssignedUserId = userId,
                    StartedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };
                var line2 = new PickingTaskLine
                {
                    PickingTask = task2,
                    AllocationId = alloc2.Id,
                    ProductId = productId,
                    SourceLocationId = srcBin2Id,
                    RequestedQuantity = 3m,
                    PickedQuantity = 0m,
                    Sequence = 1,
                    Status = PickingTaskLineStatus.Open
                };

                db.PickingTasks.AddRange(task1, task2);
                db.PickingTaskLines.AddRange(line1, line2);
                await db.SaveChangesAsync();

                var currentUser = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(db, currentUser);
                var pickingSvc = new PickingService(db, auth, currentUser);

                var short1 = await pickingSvc.ReportShortPickAsync(task1.Id, new ReportShortPickDto
                {
                    TaskLineId = line1.Id,
                    ActualPickedQuantity = 0m,
                    Reason = "DAMAGED_AT_BIN",
                    RowVersion = Convert.ToBase64String(task1.RowVersion)
                });
                task1Id = task1.Id;
                ex1Id = short1.ShortPicks.Single().Id;
                task1RowVersion = short1.RowVersion!;

                var short2 = await pickingSvc.ReportShortPickAsync(task2.Id, new ReportShortPickDto
                {
                    TaskLineId = line2.Id,
                    ActualPickedQuantity = 0m,
                    Reason = "DAMAGED_AT_BIN",
                    RowVersion = Convert.ToBase64String(task2.RowVersion)
                });
                task2Id = task2.Id;
                ex2Id = short2.ShortPicks.Single().Id;
                task2RowVersion = short2.RowVersion!;
            }

            using var startGate = new ManualResetEventSlim(false);
            var inputs = new[]
            {
                (TaskId: task1Id, ExId: ex1Id, SrcBinId: srcBin1Id, RowVersion: task1RowVersion),
                (TaskId: task2Id, ExId: ex2Id, SrcBinId: srcBin2Id, RowVersion: task2RowVersion)
            };

            var shortPickTasks = inputs.Select(async item =>
            {
                await Task.Yield();
                startGate.Wait();
                await using var workerDb = CreateContext();
                var currentUser = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, currentUser);
                var pickingSvc = new PickingService(workerDb, auth, currentUser);

                try
                {
                    var dto = await pickingSvc.ResolveShortPickAsync(item.TaskId, item.ExId, new ResolveShortPickDto
                    {
                        Resolution = nameof(ShortPickResolutionType.AlternativeLocation),
                        Note = "Lấy bù tại kệ WH1-ALT",
                        RowVersion = item.RowVersion
                    });
                    return (item.TaskId, item.SrcBinId, Succeeded: true, Error: (Exception?)null, Result: (PickingTaskDto?)dto);
                }
                catch (Exception ex)
                {
                    return (item.TaskId, item.SrcBinId, Succeeded: false, Error: (Exception?)ex, Result: (PickingTaskDto?)null);
                }
            }).ToArray();

            startGate.Set();
            var results = await Task.WhenAll(shortPickTasks);

            var winner = results.Single(x => x.Succeeded);
            var loser = results.Single(x => !x.Succeeded);

            var loserEx = loser.Error.Should().BeOfType<BusinessRuleException>().Subject;
            loserEx.Data["HttpStatusCode"].Should().Be(409);
            loserEx.Data["ErrorCode"].Should().Be("PICK_SHORT");

            await using (var verifyDb = CreateContext())
            {
                var altStock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == altBinId && x.ProductId == productId);
                altStock.Quantity.Should().Be(6m);
                altStock.ReservedQuantity.Should().Be(5m, "2 units from third-party reservation + 3 units reallocated to winner = 5 (never > 6)");

                var winnerSrcStock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == winner.SrcBinId && x.ProductId == productId);
                winnerSrcStock.ReservedQuantity.Should().Be(0m, "winner source bin reservation of 3 must be completely released");

                var loserSrcStock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.LocationId == loser.SrcBinId && x.ProductId == productId);
                loserSrcStock.ReservedQuantity.Should().Be(3m, "loser transaction rolled back so its source bin reservation remains intact");
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (warehouseId > 0)
            {
                await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                var lineIds = await cleanDb.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == warehouseId).Select(x => x.Id).ToArrayAsync();
                await cleanDb.ShortPickExceptions.Where(x => lineIds.Contains(x.PickingTaskLineId)).ExecuteDeleteAsync();
                await cleanDb.PickingTaskLines.Where(x => lineIds.Contains(x.Id)).ExecuteDeleteAsync();
                await cleanDb.PickingTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.StockAllocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.StockReservations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
                await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            }
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task Race4_FiveConcurrentIdenticalIdempotencyKeyRequests_ExecutesHandlerOnceAndNeverThrowsUniqueIndex500()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        int actorId = 0;
        var rawKey = $"idem-race4-{suffix}";
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        var scope = $"SalesOrder.Create.{suffix}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}\n{{\"code\":\"CMD-{suffix}\"}}")));
        var executionCount = 0;

        try
        {
            await using (var db = CreateContext())
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager") ?? new Role { RoleName = "Manager" };
                if (role.Id == 0) { db.Roles.Add(role); await db.SaveChangesAsync(); }

                var user = new User { Username = $"w8_r4_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R4", RoleId = role.Id, IsActive = true };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                actorId = user.Id;
            }

            using var startGate = new ManualResetEventSlim(false);
            var httpTasks = Enumerable.Range(0, 5).Select(async _ =>
            {
                await Task.Yield();
                startGate.Wait();
                await using var workerDb = CreateContext();
                await using var transaction = await workerDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var record = new IdempotencyRecord
                {
                    UserId = actorId,
                    CommandScope = scope,
                    KeyHash = keyHash,
                    RequestFingerprint = fingerprint,
                    Status = IdempotencyStatus.Processing,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
                };
                workerDb.IdempotencyRecords.Add(record);

                try
                {
                    await workerDb.SaveChangesAsync();
                }
                catch (Exception ex) when (
                    ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 } } ||
                    ex is SqlException { Number: 1205 })
                {
                    await transaction.RollbackAsync();
                    workerDb.ChangeTracker.Clear();
                    var existing = await workerDb.IdempotencyRecords.AsNoTracking()
                        .SingleOrDefaultAsync(x => x.UserId == actorId && x.CommandScope == scope && x.KeyHash == keyHash);
                    if (existing is null || existing.RequestFingerprint != fingerprint || existing.Status != IdempotencyStatus.Completed)
                    {
                        return (StatusCode: 409, Body: "{\"message\":\"Idempotency-Key đang được xử lý.\"}");
                    }

                    return (StatusCode: existing.ResponseStatusCode ?? 200, Body: existing.ResponseBody ?? string.Empty);
                }

                Interlocked.Increment(ref executionCount);
                await Task.Delay(35);

                record.Status = IdempotencyStatus.Completed;
                record.ResponseStatusCode = 201;
                record.ResponseBody = $"{{\"id\":777,\"code\":\"CMD-{suffix}\"}}";
                record.CompletedAtUtc = DateTime.UtcNow;
                await workerDb.SaveChangesAsync();
                await transaction.CommitAsync();
                return (StatusCode: 201, Body: record.ResponseBody);
            }).ToArray();

            startGate.Set();
            var responses = await Task.WhenAll(httpTasks);

            executionCount.Should().Be(1, "the underlying mutation handler must execute exactly once across 5 concurrent duplicate POSTs");
            responses.Select(x => x.StatusCode).Should().OnlyContain(code => code == 201 || code == 409);
            responses.Select(x => x.StatusCode).Should().Contain(201);

            await using (var verifyDb = CreateContext())
            {
                var records = await verifyDb.IdempotencyRecords.AsNoTracking()
                    .Where(x => x.UserId == actorId && x.CommandScope == scope && x.KeyHash == keyHash)
                    .ToListAsync();
                records.Should().HaveCount(1);
                records[0].Status.Should().Be(IdempotencyStatus.Completed);
                records[0].ResponseStatusCode.Should().Be(201);
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (actorId > 0)
            {
                await cleanDb.IdempotencyRecords.Where(x => x.UserId == actorId).ExecuteDeleteAsync();
                await cleanDb.Users.Where(x => x.Id == actorId).ExecuteDeleteAsync();
            }
        }
    }

    [SqlServerFact]
    public async Task Race5_FiveConcurrentTransferReceives_And_FiveConcurrentStocktakeApprovals_AllowExactlyOneWinnerEach()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"R5{suffix.Substring(0, 4)}";
        int makerId = 0, checkerId = 0, wh1Id = 0, wh2Id = 0, productId = 0, transferId = 0, stocktakeId = 0;

        try
        {
            await using (var db = CreateContext())
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager") ?? new Role { RoleName = "Manager" };
                if (role.Id == 0) { db.Roles.Add(role); await db.SaveChangesAsync(); }

                var maker = new User { Username = $"w8_r5_m_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R5 Maker", RoleId = role.Id, IsActive = true };
                var checker = new User { Username = $"w8_r5_c_{suffix.ToLowerInvariant()}", PasswordHash = "x", FullName = "W8 R5 Checker", RoleId = role.Id, IsActive = true };
                var unit = new Unit { Code = unitCode, Name = "EA", DecimalPlaces = 0 };
                var wh1 = new Warehouse { Code = $"WH1-R5-{suffix}", Name = $"Warehouse 1 R5 {suffix}", IsActive = true };
                var wh2 = new Warehouse { Code = $"WH2-R5-{suffix}", Name = $"Warehouse 2 R5 {suffix}", IsActive = true };
                db.AddRange(maker, checker, unit, wh1, wh2);
                await db.SaveChangesAsync();

                makerId = maker.Id;
                checkerId = checker.Id;
                wh1Id = wh1.Id;
                wh2Id = wh2.Id;

                db.UserWarehouses.AddRange(
                    new UserWarehouse { UserId = makerId, WarehouseId = wh1Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = makerId, WarehouseId = wh2Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = checkerId, WarehouseId = wh1Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = checkerId, WarehouseId = wh2Id, CreatedBy = makerId });

                var wh1Legacy = new WarehouseLocation { WarehouseId = wh1Id, Code = "LEGACY", Name = "Legacy 1", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true };
                var wh2Legacy = new WarehouseLocation { WarehouseId = wh2Id, Code = "LEGACY", Name = "Legacy 2", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true };
                var product = new Product { Code = $"SKU-R5-{suffix}", Name = $"SKU R5 {suffix}", UnitId = unit.Id, IsActive = true };
                db.AddRange(wh1Legacy, wh2Legacy, product);
                await db.SaveChangesAsync();

                productId = product.Id;

                // Seed WH1 stock = 10 for Stocktake; WH2 stock = 0 for StockTransfer Receive
                db.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = wh1Legacy.Id,
                    ProductId = productId,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 0m
                });

                var transfer = new StockTransfer
                {
                    Code = $"TF-R5-{suffix}",
                    SourceWarehouseId = wh1Id,
                    DestinationWarehouseId = wh2Id,
                    Status = StockTransferStatus.InTransit,
                    CreatedBy = makerId,
                    ApprovedBy = checkerId,
                    ApprovedAt = DateTime.UtcNow,
                    DispatchedBy = checkerId,
                    DispatchedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StockTransferDetail
                        {
                            ProductId = productId,
                            RequestedQuantity = 10m,
                            DispatchedQuantity = 10m,
                            ReceivedQuantity = 0m
                        }
                    ]
                };
                db.StockTransfers.Add(transfer);

                var stocktake = new Stocktake
                {
                    Code = $"ST-R5-{suffix}",
                    WarehouseId = wh1Id,
                    Status = StocktakeStatus.Draft,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            ProductId = productId,
                            LocationId = wh1Legacy.Id,
                            SystemQuantity = 10m,
                            ActualQuantity = 15m,
                            DifferenceQuantity = 5m
                        }
                    ]
                };
                db.Stocktakes.Add(stocktake);
                await db.SaveChangesAsync();

                transferId = transfer.Id;
                stocktakeId = stocktake.Id;
            }

            // 1. 5 Concurrent threads calling StockTransferService.ReceiveAsync on the same transfer
            using var transferGate = new ManualResetEventSlim(false);
            var receiveTasks = Enumerable.Range(0, 5).Select(async _ =>
            {
                await Task.Yield();
                transferGate.Wait();
                await using var workerDb = CreateContext();
                var currentUser = new ScopedUser(checkerId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, currentUser);
                var stockRepo = new InventoryStockRepository(workerDb);
                var transferSvc = new StockTransferService(workerDb, stockRepo, auth, currentUser);

                try
                {
                    await transferSvc.ReceiveAsync(transferId, new ReceiveStockTransferDto
                    {
                        Details = [new ReceiveStockTransferLineDto { ProductId = productId, ReceivedQuantity = 10m }]
                    });
                    return (Succeeded: true, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Error: (Exception?)ex);
                }
            }).ToArray();

            transferGate.Set();
            var receiveResults = await Task.WhenAll(receiveTasks);
            receiveResults.Count(x => x.Succeeded).Should().Be(1, "exactly 1 thread may receive an InTransit StockTransfer");
            receiveResults.Count(x => !x.Succeeded).Should().Be(4);
            receiveResults.Where(x => !x.Succeeded).Select(x => x.Error).Should().OnlyContain(
                ex => ex is BusinessRuleException || ex is ERP.Domain.Exceptions.ConcurrencyException);

            // 2. 5 Concurrent threads calling StocktakeService.ApproveStocktakeAsync on the same stocktake
            using var stocktakeGate = new ManualResetEventSlim(false);
            var approveTasks = Enumerable.Range(0, 5).Select(async _ =>
            {
                await Task.Yield();
                stocktakeGate.Wait();
                await using var workerDb = CreateContext();
                var currentUser = new ScopedUser(checkerId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, currentUser);
                var uow = new UnitOfWork(workerDb);
                var stocktakeSvc = new StocktakeService(
                    new StocktakeRepository(workerDb),
                    new InventoryStockRepository(workerDb),
                    new InventoryTransactionRepository(workerDb),
                    new WarehouseRepository(workerDb),
                    uow,
                    new AuditLogRepository(workerDb),
                    auth,
                    currentUser,
                    new InventoryLockService(workerDb, auth, currentUser),
                    new ProductRepository(workerDb));

                try
                {
                    await stocktakeSvc.ApproveStocktakeAsync(stocktakeId, checkerId);
                    return (Succeeded: true, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Error: (Exception?)ex);
                }
            }).ToArray();

            stocktakeGate.Set();
            var approveResults = await Task.WhenAll(approveTasks);
            approveResults.Count(x => x.Succeeded).Should().Be(1, "exactly 1 thread may approve a Draft Stocktake");
            approveResults.Count(x => !x.Succeeded).Should().Be(4);
            approveResults.Where(x => !x.Succeeded).Select(x => x.Error).Should().OnlyContain(ex => ex is BusinessRuleException);

            await using (var verifyDb = CreateContext())
            {
                var wh2Stock = await verifyDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == wh2Id && x.ProductId == productId)
                    .SumAsync(x => x.Quantity);
                wh2Stock.Should().Be(10m, "destination stock must increase by +10 exactly once");

                var transferInTxCount = await verifyDb.InventoryTransactions.AsNoTracking()
                    .CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == transferId && x.TransactionType == TransactionType.TransferIn);
                transferInTxCount.Should().Be(1);

                var wh1Stock = await verifyDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == wh1Id && x.ProductId == productId)
                    .SumAsync(x => x.Quantity);
                wh1Stock.Should().Be(15m, "WH1 stock must be adjusted from 10 to 15 (+5) exactly once");

                var stocktakeTxCount = await verifyDb.InventoryTransactions.AsNoTracking()
                    .CountAsync(x => x.ReferenceType == "Stocktake" && x.ReferenceId == stocktakeId);
                stocktakeTxCount.Should().Be(1);
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            int[] whIds = [wh1Id, wh2Id];
            await cleanDb.AuditLogs.Where(x => x.WarehouseId.HasValue && whIds.Contains(x.WarehouseId.Value)).ExecuteDeleteAsync();
            await cleanDb.InventoryTransactions.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StocktakeDetails.Where(x => x.StocktakeId == stocktakeId).ExecuteDeleteAsync();
            await cleanDb.Stocktakes.Where(x => x.Id == stocktakeId).ExecuteDeleteAsync();
            await cleanDb.StockTransferDetails.Where(x => x.StockTransferId == transferId).ExecuteDeleteAsync();
            await cleanDb.StockTransfers.Where(x => x.Id == transferId).ExecuteDeleteAsync();
            await cleanDb.InventoryStocks.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.WarehouseLocations.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.UserWarehouses.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => whIds.Contains(x.Id)).ExecuteDeleteAsync();
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            int[] uIds = [makerId, checkerId];
            await cleanDb.Users.Where(x => uIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    private static SalesOrderBackorderService CreateDemandService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        var reservation = new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            auth,
            current,
            new StockReservationOptions(),
            new PickingTaskIntegration(db));
        var allocation = new StockAllocationService(
            db,
            auth,
            current,
            new PickingTaskIntegration(db));
        return new SalesOrderBackorderService(
            db,
            new InventoryStockRepository(db),
            reservation,
            allocation,
            auth,
            current);
    }

    private sealed record ScopedUser(int UserId, string Role, bool IsGlobalAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
