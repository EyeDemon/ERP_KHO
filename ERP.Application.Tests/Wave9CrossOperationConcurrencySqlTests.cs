using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class Wave9CrossOperationConcurrencySqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    [SqlServerFact]
    public async Task CrossRace1_ApproveStocktakeShrinkage_Vs_ReleaseSalesOrder_PreservesReservedQuantityInvariant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"U9A{suffix[..3]}";
        int makerId = 0;
        int checkerId = 0;
        int warehouseId = 0;
        int customerId = 0;
        int productAId = 0;
        int productBId = 0;
        int binAId = 0;
        int binBId = 0;
        int stocktakeAId = 0;
        int stocktakeBId = 0;
        int salesOrderBId = 0;

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

                var maker = new User
                {
                    Username = $"w9_r1_maker_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W9 R1 Maker {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                var checker = new User
                {
                    Username = $"w9_r1_checker_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W9 R1 Checker {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                setupDb.Users.AddRange(maker, checker);

                var unit = new Unit { Code = unitCode, Name = "Cái", DecimalPlaces = 0 };
                setupDb.Units.Add(unit);

                var wh = new Warehouse { Code = $"W9R1-{suffix}", Name = $"Kho W9 R1 {suffix}", IsActive = true };
                setupDb.Warehouses.Add(wh);

                var customer = new BusinessPartner
                {
                    Code = $"W9C1-{suffix}",
                    Name = $"Customer W9 R1 {suffix}",
                    IsCustomer = true,
                    IsActive = true
                };
                setupDb.BusinessPartners.Add(customer);
                await setupDb.SaveChangesAsync();

                makerId = maker.Id;
                checkerId = checker.Id;
                warehouseId = wh.Id;
                customerId = customer.Id;

                setupDb.UserWarehouses.AddRange(
                    new UserWarehouse { UserId = makerId, WarehouseId = warehouseId, CreatedBy = makerId },
                    new UserWarehouse { UserId = checkerId, WarehouseId = warehouseId, CreatedBy = makerId });

                var binA = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = "W9-BINA",
                    Name = "Kệ A",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var binB = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = "W9-BINB",
                    Name = "Kệ B",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                setupDb.WarehouseLocations.AddRange(binA, binB);

                var prodA = new Product
                {
                    Code = $"W9-SKUA-{suffix}",
                    Name = $"SKU A {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                var prodB = new Product
                {
                    Code = $"W9-SKUB-{suffix}",
                    Name = $"SKU B {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                setupDb.Products.AddRange(prodA, prodB);
                await setupDb.SaveChangesAsync();

                binAId = binA.Id;
                binBId = binB.Id;
                productAId = prodA.Id;
                productBId = prodB.Id;

                // Part A setup: Quantity = 10, ReservedQuantity = 8, Stocktake ActualQuantity = 5 (-5 shrinkage)
                setupDb.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = binAId,
                    ProductId = productAId,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 8m,
                    LastUpdated = DateTime.UtcNow
                });
                var stA = new Stocktake
                {
                    Code = $"ST-W9A-{suffix}",
                    WarehouseId = warehouseId,
                    Status = StocktakeStatus.Draft,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            ProductId = productAId,
                            LocationId = binAId,
                            SystemQuantity = 10m,
                            ActualQuantity = 5m,
                            DifferenceQuantity = -5m
                        }
                    ]
                };
                setupDb.Stocktakes.Add(stA);

                // Part B setup: Quantity = 10, ReservedQuantity = 0, Stocktake ActualQuantity = 5 (-5) vs SalesOrder Ordered = 8
                setupDb.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = binBId,
                    ProductId = productBId,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
                var stB = new Stocktake
                {
                    Code = $"ST-W9B-{suffix}",
                    WarehouseId = warehouseId,
                    Status = StocktakeStatus.Draft,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            ProductId = productBId,
                            LocationId = binBId,
                            SystemQuantity = 10m,
                            ActualQuantity = 5m,
                            DifferenceQuantity = -5m
                        }
                    ]
                };
                setupDb.Stocktakes.Add(stB);
                await setupDb.SaveChangesAsync();

                stocktakeAId = stA.Id;
                stocktakeBId = stB.Id;

                var demandSvc = CreateDemandService(setupDb, makerId);
                var createdOrder = await demandSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"SO-W9B-{suffix}",
                    CustomerId = customerId,
                    WarehouseId = warehouseId,
                    Priority = 1,
                    Lines =
                    [
                        new CreateSalesOrderLineDto
                        {
                            ExternalLineId = "LINE-1",
                            ProductId = productBId,
                            OrderedQuantity = 8m
                        }
                    ]
                });
                salesOrderBId = createdOrder.Id;
            }

            // Part A: ApproveStocktakeAsync when ActualQuantity (5) < ReservedQuantity (8) MUST fail with 409 STOCKTAKE_BELOW_RESERVED_QUANTITY
            await using (var dbA = CreateContext())
            {
                var stocktakeSvcA = CreateStocktakeService(dbA, checkerId);
                var actA = () => stocktakeSvcA.ApproveStocktakeAsync(stocktakeAId, checkerId);
                var exA = await actA.Should().ThrowAsync<BusinessRuleException>();
                exA.Which.Data["HttpStatusCode"].Should().Be(409);
                exA.Which.Data["ErrorCode"].Should().Be("STOCKTAKE_BELOW_RESERVED_QUANTITY");

                var stockA = await dbA.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.ProductId == productAId);
                stockA.Quantity.Should().Be(10m);
                stockA.ReservedQuantity.Should().Be(8m);
            }

            // Part B: Simultaneous millisecond race between ApproveStocktakeAsync (Actual=5) and ReleaseSalesOrderAsync (Ordered=8)
            string soRowVersion;
            await using (var readDb = CreateContext())
            {
                var demandSvc = CreateDemandService(readDb, makerId);
                soRowVersion = (await demandSvc.GetSalesOrderAsync(salesOrderBId)).RowVersion!;
            }

            using var gate = new ManualResetEventSlim(false);
            var stocktakeTask = Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var svc = CreateStocktakeService(workerDb, checkerId);
                try
                {
                    await svc.ApproveStocktakeAsync(stocktakeBId, checkerId);
                    return (Succeeded: true, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Error: (Exception?)ex);
                }
            });

            var releaseOrderTask = Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var svc = CreateDemandService(workerDb, makerId);
                try
                {
                    var dto = await svc.ReleaseSalesOrderAsync(salesOrderBId, new SalesOrderStateCommandDto
                    {
                        RowVersion = soRowVersion,
                        Reason = "Wave 9 Cross-Race 1"
                    });
                    return (Succeeded: true, Order: (SalesOrderDto?)dto, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Order: (SalesOrderDto?)null, Error: (Exception?)ex);
                }
            });

            gate.Set();
            await Task.WhenAll(stocktakeTask, releaseOrderTask);

            var stResult = await stocktakeTask;
            var soResult = await releaseOrderTask;

            // Neither thread may fail with an unhandled SQL 1205 deadlock
            soResult.Succeeded.Should().BeTrue($"ReleaseSalesOrderAsync must succeed without deadlock, but got: {soResult.Error}");

            await using (var verifyDb = CreateContext())
            {
                var stockB = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.ProductId == productBId);

                // Core ledger invariant: 0 <= ReservedQuantity <= Quantity
                stockB.ReservedQuantity.Should().BeGreaterThanOrEqualTo(0m);
                stockB.Quantity.Should().BeGreaterThanOrEqualTo(stockB.ReservedQuantity,
                    "ReservedQuantity must NEVER exceed Quantity under concurrent Stocktake + SalesOrder release");

                if (stResult.Succeeded)
                {
                    // Stocktake committed first -> Quantity became 5, then SalesOrder reserved 5 and backordered 3
                    stockB.Quantity.Should().Be(5m);
                    stockB.ReservedQuantity.Should().Be(5m);
                    soResult.Order!.ReservedQuantity.Should().Be(5m);
                    soResult.Order.BackorderQuantity.Should().Be(3m);
                }
                else
                {
                    // SalesOrder committed first -> ReservedQuantity became 8, then Stocktake rejected with 409 STOCKTAKE_BELOW_RESERVED_QUANTITY
                    stResult.Error.Should().BeOfType<BusinessRuleException>();
                    var brEx = (BusinessRuleException)stResult.Error!;
                    brEx.Data["HttpStatusCode"].Should().Be(409);
                    brEx.Data["ErrorCode"].Should().Be("STOCKTAKE_BELOW_RESERVED_QUANTITY");
                    stockB.Quantity.Should().Be(10m);
                    stockB.ReservedQuantity.Should().Be(8m);
                    soResult.Order!.ReservedQuantity.Should().Be(8m);
                    soResult.Order.BackorderQuantity.Should().Be(0m);
                }
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryTransactions.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockAllocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockReservations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Backorders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrderLines.Where(x => x.SalesOrder.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            int[] stIds = [stocktakeAId, stocktakeBId];
            await cleanDb.StocktakeDetails.Where(x => stIds.Contains(x.StocktakeId)).ExecuteDeleteAsync();
            await cleanDb.Stocktakes.Where(x => stIds.Contains(x.Id)).ExecuteDeleteAsync();
            await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            int[] pIds = [productAId, productBId];
            await cleanDb.Products.Where(x => pIds.Contains(x.Id)).ExecuteDeleteAsync();
            if (customerId > 0) await cleanDb.BusinessPartners.Where(x => x.Id == customerId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            int[] uIds = [makerId, checkerId];
            await cleanDb.Users.Where(x => uIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task CrossRace2_InventoryLockCreate_Vs_ConcurrentReleaseSalesOrderAndPutaway_SerializesWithoutDeadlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"U9B{suffix[..3]}";
        int userId = 0;
        int warehouseId = 0;
        int customerId = 0;
        int productId = 0;
        int lotId = 0;
        int receivingLocId = 0;
        int storageLocId = 0;
        int receiptId = 0;
        int putawayTask1Id = 0;
        int putawayItem1Id = 0;
        int putawayTask2Id = 0;
        int putawayItem2Id = 0;
        var salesOrders = new List<(int Id, string RowVersion)>();
        string pt1RowVersion = string.Empty;
        string pt2RowVersion = string.Empty;
        int reservedStockId = 0;

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

                var user = new User
                {
                    Username = $"w9_r2_user_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W9 R2 User {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                setupDb.Users.Add(user);

                var unit = new Unit { Code = unitCode, Name = "Cái", DecimalPlaces = 0 };
                setupDb.Units.Add(unit);

                var wh = new Warehouse { Code = $"W9R2-{suffix}", Name = $"Kho W9 R2 {suffix}", IsActive = true };
                setupDb.Warehouses.Add(wh);

                var customer = new BusinessPartner
                {
                    Code = $"W9C2-{suffix}",
                    Name = $"Customer W9 R2 {suffix}",
                    IsCustomer = true,
                    IsActive = true
                };
                setupDb.BusinessPartners.Add(customer);
                await setupDb.SaveChangesAsync();

                userId = user.Id;
                warehouseId = wh.Id;
                customerId = customer.Id;

                setupDb.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = warehouseId, CreatedBy = userId });

                var receivingLoc = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = "RECEIVING",
                    Name = "Khu tiếp nhận",
                    LocationType = WarehouseLocationType.Receiving,
                    IsReceivable = true,
                    IsPickable = false,
                    IsSystemManaged = true,
                    IsActive = true
                };
                var storageLoc = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = "W9-STOR-LOTX",
                    Name = "Kệ lưu trữ LOT-X",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                setupDb.WarehouseLocations.AddRange(receivingLoc, storageLoc);

                var product = new Product
                {
                    Code = $"W9-SKULOT-{suffix}",
                    Name = $"SKU Lot {suffix}",
                    UnitId = unit.Id,
                    TrackingType = ProductTrackingType.Lot,
                    IsActive = true
                };
                setupDb.Products.Add(product);
                await setupDb.SaveChangesAsync();

                receivingLocId = receivingLoc.Id;
                storageLocId = storageLoc.Id;
                productId = product.Id;

                var lot = new InventoryLot
                {
                    ProductId = productId,
                    LotNumber = $"LOT-X-{suffix}",
                    ReceivedAt = DateTime.UtcNow.AddDays(-2),
                    ExpiryDate = DateTime.UtcNow.Date.AddDays(90)
                };
                var lotReserved = new InventoryLot
                {
                    ProductId = productId,
                    LotNumber = $"LOT-RES-{suffix}",
                    ReceivedAt = DateTime.UtcNow.AddDays(-1),
                    ExpiryDate = DateTime.UtcNow.Date.AddDays(90)
                };
                setupDb.InventoryLots.AddRange(lot, lotReserved);
                await setupDb.SaveChangesAsync();
                lotId = lot.Id;

                // Stock at RECEIVING (10 units) for Putaway and at STORAGE (15 units) for SalesOrders
                setupDb.InventoryStocks.AddRange(
                    new InventoryStock
                    {
                        WarehouseId = warehouseId,
                        LocationId = receivingLocId,
                        ProductId = productId,
                        LotId = lotId,
                        Status = InventoryStatus.Available,
                        Quantity = 10m,
                        ReservedQuantity = 0m,
                        LastUpdated = DateTime.UtcNow
                    },
                    new InventoryStock
                    {
                        WarehouseId = warehouseId,
                        LocationId = storageLocId,
                        ProductId = productId,
                        LotId = lotId,
                        Status = InventoryStatus.Available,
                        Quantity = 15m,
                        ReservedQuantity = 0m,
                        LastUpdated = DateTime.UtcNow
                    });

                var reservedStock = new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = storageLocId,
                    ProductId = productId,
                    LotId = lotReserved.Id,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 7m,
                    LastUpdated = DateTime.UtcNow
                };
                setupDb.InventoryStocks.Add(reservedStock);

                var receipt = new ImportReceipt
                {
                    Code = $"IR-W9R2-{suffix}",
                    WarehouseId = warehouseId,
                    Status = ReceiptStatus.Posted,
                    CreatedBy = userId
                };
                setupDb.ImportReceipts.Add(receipt);
                await setupDb.SaveChangesAsync();
                receiptId = receipt.Id;
                reservedStockId = reservedStock.Id;

                var receiptDetail = new ImportReceiptDetail
                {
                    ImportReceiptId = receiptId,
                    ProductId = productId,
                    Quantity = 10m,
                    ExpectedQuantity = 10m,
                    ReceivedQuantity = 10m,
                    AcceptedQuantity = 10m,
                    PostedQuantity = 10m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unitCode,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unitCode,
                    ConversionFactor = 1m,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 10m,
                    BaseReceivedQuantity = 10m,
                    BaseAcceptedQuantity = 10m,
                    BasePostedQuantity = 10m
                };
                setupDb.ImportReceiptDetails.Add(receiptDetail);
                await setupDb.SaveChangesAsync();

                setupDb.ImportReceiptInventoryIdentities.Add(new ImportReceiptInventoryIdentity
                {
                    ImportReceiptDetailId = receiptDetail.Id,
                    ProductId = productId,
                    LotNumber = lot.LotNumber,
                    BaseQuantity = 10m,
                    TargetStatus = InventoryStatus.Available,
                    CreatedBy = userId
                });

                var pt1 = new PutawayTask
                {
                    ReceiptId = receiptId,
                    WarehouseId = warehouseId,
                    Status = PutawayTaskStatus.Assigned,
                    AssignedUserId = userId,
                    CreatedBy = userId
                };
                var pt1Item = new PutawayTaskItem
                {
                    PutawayTask = pt1,
                    ReceiptLineId = receiptDetail.Id,
                    ProductId = productId,
                    SourceLocationId = receivingLocId,
                    InventoryStatus = InventoryStatus.Available,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unitCode,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unitCode,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseUnitDecimalPlaces = 0,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m,
                    MovedBaseQuantity = 0m
                };
                setupDb.PutawayTasks.Add(pt1);
                setupDb.PutawayTaskItems.Add(pt1Item);
                await setupDb.SaveChangesAsync();

                putawayTask1Id = pt1.Id;
                putawayItem1Id = pt1Item.Id;
                pt1RowVersion = Convert.ToBase64String(pt1.RowVersion);
                putawayTask2Id = pt1.Id;
                putawayItem2Id = pt1Item.Id;
                pt2RowVersion = pt1RowVersion;

                var demandSvc = CreateDemandService(setupDb, userId);
                for (var i = 1; i <= 3; i++)
                {
                    var so = await demandSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                    {
                        ExternalOrderId = $"SO-W9R2-{i}-{suffix}",
                        CustomerId = customerId,
                        WarehouseId = warehouseId,
                        Priority = 1,
                        Lines =
                        [
                            new CreateSalesOrderLineDto
                            {
                                ExternalLineId = $"LINE-{i}",
                                ProductId = productId,
                                OrderedQuantity = 5m
                            }
                        ]
                    });
                    salesOrders.Add((so.Id, so.RowVersion!));
                }
            }

            // Verify W9-02: InventoryStatusService.ChangeAsync blocks changing status of reserved stock
            await using (var statusDb = CreateContext())
            {
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(statusDb, current);
                var lockSvc = new InventoryLockService(statusDb, auth, current);
                var statusSvc = new InventoryStatusService(statusDb, auth, current, lockSvc);

                var actStatus = () => statusSvc.ChangeAsync(new CreateInventoryStatusChangeDto
                {
                    InventoryStockId = reservedStockId,
                    ToStatus = "Quarantine",
                    Quantity = 5m, // Quantity=10, Reserved=7 -> Available=3 < 5
                    Reason = "Kiểm tra chặn đổi trạng thái phần tồn đã giữ chỗ"
                });
                var statusEx = await actStatus.Should().ThrowAsync<BusinessRuleException>();
                statusEx.Which.Data["HttpStatusCode"].Should().Be(409);
                statusEx.Which.Data["ErrorCode"].Should().Be("INV_STATUS_CHANGE_NOT_ALLOWED");
            }

            // Fire 1 thread creating InventoryLock on LOT-X simultaneously with 3 ReleaseSalesOrderAsync + 2 PutawayService.MoveAsync threads
            using var gate = new ManualResetEventSlim(false);

            var lockTask = Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, current);
                var lockSvc = new InventoryLockService(workerDb, auth, current);
                return await lockSvc.CreateAsync(new CreateInventoryLockDto
                {
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    LotId = lotId,
                    LockType = "QualityHold",
                    Reason = "Khóa khẩn cấp LOT-X khi đang phân bổ/putaway"
                });
            });

            var soTasks = salesOrders.Select(so => Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var svc = CreateDemandService(workerDb, userId);
                try
                {
                    var res = await svc.ReleaseSalesOrderAsync(so.Id, new SalesOrderStateCommandDto
                    {
                        RowVersion = so.RowVersion,
                        Reason = "Wave 9 Cross-Race 2"
                    });
                    return (Succeeded: true, Order: (SalesOrderDto?)res, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Order: (SalesOrderDto?)null, Error: (Exception?)ex);
                }
            })).ToArray();

            var putawaySpecs = new[]
            {
                (TaskId: putawayTask1Id, ItemId: putawayItem1Id, RowVersion: pt1RowVersion),
                (TaskId: putawayTask2Id, ItemId: putawayItem2Id, RowVersion: pt2RowVersion)
            };
            var putawayTasks = putawaySpecs.Select(spec => Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, current);
                var lockSvc = new InventoryLockService(workerDb, auth, current);
                var putawaySvc = new PutawayService(workerDb, auth, current, lockSvc);
                try
                {
                    var res = await putawaySvc.MoveAsync(spec.TaskId, new MovePutawayItemDto
                    {
                        ItemId = spec.ItemId,
                        DestinationLocationId = storageLocId,
                        UnitCode = unitCode,
                        Quantity = 5m,
                        RowVersion = spec.RowVersion
                    });
                    return (Succeeded: true, Result: (PutawayTaskDto?)res, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Result: (PutawayTaskDto?)null, Error: (Exception?)ex);
                }
            })).ToArray();

            gate.Set();
            var createdLock = await lockTask;
            var soResults = await Task.WhenAll(soTasks);
            var putawayResults = await Task.WhenAll(putawayTasks);

            createdLock.Status.Should().Be("Active");
            soResults.Should().OnlyContain(r => r.Succeeded, "no ReleaseSalesOrderAsync thread may fail with a SQL 1205 deadlock");
            putawayResults.Where(r => !r.Succeeded).Select(r => r.Error).Should().OnlyContain(
                ex => ex is BusinessRuleException || ex is ERP.Domain.Exceptions.ConcurrencyException,
                "any Putaway thread ordered after InventoryLock creation or concurrent move must fail cleanly and never deadlock");

            // Once LOT-X lock is committed, any subsequent ReleaseSalesOrder on LOT-X MUST be 100% blocked
            await using (var postLockDb = CreateContext())
            {
                var demandSvc = CreateDemandService(postLockDb, userId);

                var postSo = await demandSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"SO-W9R2-POST-{suffix}",
                    CustomerId = customerId,
                    WarehouseId = warehouseId,
                    Priority = 1,
                    Lines =
                    [
                        new CreateSalesOrderLineDto
                        {
                            ExternalLineId = "LINE-POST",
                            ProductId = productId,
                            OrderedQuantity = 5m
                        }
                    ]
                });
                var releasedPostSo = await demandSvc.ReleaseSalesOrderAsync(
                    postSo.Id,
                    new SalesOrderStateCommandDto
                    {
                        RowVersion = postSo.RowVersion!,
                        Reason = "Post-lock verification"
                    });

                var postReservationIds = await postLockDb.StockReservations.AsNoTracking()
                    .Where(x => x.SourceType == "SalesOrder" && x.SourceId == postSo.Id)
                    .Select(x => x.Id)
                    .ToArrayAsync();

                var lotXAllocationsAfterLock = await postLockDb.StockAllocations.AsNoTracking()
                    .CountAsync(x => postReservationIds.Contains(x.ReservationId) && x.LotId == lotId);
                lotXAllocationsAfterLock.Should().Be(0, "locked LOT-X must never be allocated after InventoryLock is active");
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryLocationMovements.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryTransactions.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockAllocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockReservations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Backorders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrderLines.Where(x => x.SalesOrder.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PutawayTaskItems.Where(x => x.PutawayTask.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PutawayTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.ImportReceiptInventoryIdentities.Where(x => x.ProductId == productId).ExecuteDeleteAsync();
            await cleanDb.ImportReceiptDetails.Where(x => x.ImportReceiptId == receiptId).ExecuteDeleteAsync();
            await cleanDb.ImportReceipts.Where(x => x.Id == receiptId).ExecuteDeleteAsync();
            await cleanDb.InventoryLocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryLots.Where(x => x.ProductId == productId).ExecuteDeleteAsync();
            await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            if (customerId > 0) await cleanDb.BusinessPartners.Where(x => x.Id == customerId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task CrossRace3_ReleaseReservation_Vs_5ConcurrentReallocateBackorder_NeverOverAllocates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"U9C{suffix[..3]}";
        int userId = 0;
        int warehouseId = 0;
        int customerId = 0;
        int productId = 0;
        int binId = 0;
        int manualReservationId = 0;
        var backorders = new List<(int Id, string RowVersion)>();

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

                var user = new User
                {
                    Username = $"w9_r3_user_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W9 R3 User {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                setupDb.Users.Add(user);

                var unit = new Unit { Code = unitCode, Name = "Cái", DecimalPlaces = 0 };
                setupDb.Units.Add(unit);

                var wh = new Warehouse { Code = $"W9R3-{suffix}", Name = $"Kho W9 R3 {suffix}", IsActive = true };
                setupDb.Warehouses.Add(wh);

                var customer = new BusinessPartner
                {
                    Code = $"W9C3-{suffix}",
                    Name = $"Customer W9 R3 {suffix}",
                    IsCustomer = true,
                    IsActive = true
                };
                setupDb.BusinessPartners.Add(customer);
                await setupDb.SaveChangesAsync();

                userId = user.Id;
                warehouseId = wh.Id;
                customerId = customer.Id;

                setupDb.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = warehouseId, CreatedBy = userId });

                var bin = new WarehouseLocation
                {
                    WarehouseId = warehouseId,
                    Code = "W9-BIN-R3",
                    Name = "Kệ R3",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                setupDb.WarehouseLocations.Add(bin);

                var product = new Product
                {
                    Code = $"W9-SKUR3-{suffix}",
                    Name = $"SKU R3 {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                setupDb.Products.Add(product);
                await setupDb.SaveChangesAsync();

                binId = bin.Id;
                productId = product.Id;

                // Seed 5 units on hand
                setupDb.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouseId,
                    LocationId = binId,
                    ProductId = productId,
                    Status = InventoryStatus.Available,
                    Quantity = 5m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
                await setupDb.SaveChangesAsync();

                // Reserve all 5 units via a Manual reservation so Available = 0
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(setupDb, current);
                var reservationSvc = new StockReservationService(
                    setupDb,
                    new InventoryStockRepository(setupDb),
                    new UnitOfWork(setupDb),
                    auth,
                    current,
                    new StockReservationOptions(),
                    new PickingTaskIntegration(setupDb));

                var manualRes = await reservationSvc.CreateAsync(new CreateStockReservationDto
                {
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    Quantity = 5m
                });
                manualReservationId = manualRes.Id;

                // Create and release 5 SalesOrders of 5 units each -> all 5 become Open Backorders (5 units each)
                var demandSvc = CreateDemandService(setupDb, userId);
                for (var i = 1; i <= 5; i++)
                {
                    var so = await demandSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                    {
                        ExternalOrderId = $"SO-W9R3-{i}-{suffix}",
                        CustomerId = customerId,
                        WarehouseId = warehouseId,
                        Priority = 1,
                        Lines =
                        [
                            new CreateSalesOrderLineDto
                            {
                                ExternalLineId = $"LINE-{i}",
                                ProductId = productId,
                                OrderedQuantity = 5m
                            }
                        ]
                    });
                    await demandSvc.ReleaseSalesOrderAsync(so.Id, new SalesOrderStateCommandDto
                    {
                        RowVersion = so.RowVersion!,
                        Reason = "Seed Open Backorders"
                    });
                }

                var openBackorders = await demandSvc.ListBackordersAsync(warehouseId, "Open");
                openBackorders.Should().HaveCount(5);
                foreach (var bo in openBackorders)
                {
                    backorders.Add((bo.Id, bo.RowVersion!));
                }
            }

            // Fire 1 thread releasing the 5-unit Manual Reservation simultaneously with 5 threads calling ReallocateBackorderAsync
            using var gate = new ManualResetEventSlim(false);

            var releaseTask = Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(workerDb, current);
                var reservationSvc = new StockReservationService(
                    workerDb,
                    new InventoryStockRepository(workerDb),
                    new UnitOfWork(workerDb),
                    auth,
                    current,
                    new StockReservationOptions(),
                    new PickingTaskIntegration(workerDb));
                await reservationSvc.ReleaseAsync(manualReservationId, new ReleaseStockReservationDto
                {
                    Quantity = 5m,
                    Reason = "Giải phóng 5 đơn vị cho các luồng ReallocateBackorder"
                });
            });

            var reallocateTasks = backorders.Select(bo => Task.Run(async () =>
            {
                gate.Wait();
                await using var workerDb = CreateContext();
                var demandSvc = CreateDemandService(workerDb, userId);
                try
                {
                    var dto = await demandSvc.ReallocateBackorderAsync(bo.Id, new BackorderReallocateDto
                    {
                        RowVersion = bo.RowVersion
                    });
                    return (Succeeded: true, Backorder: (BackorderDto?)dto, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Backorder: (BackorderDto?)null, Error: (Exception?)ex);
                }
            })).ToArray();

            gate.Set();
            await releaseTask;
            var reallocateResults = await Task.WhenAll(reallocateTasks);

            // Every thread either succeeded (recovered 5 units) or failed cleanly with 409 BACKORDER_INSUFFICIENT_AVAILABLE (if ordered before ReleaseAsync)
            reallocateResults.Where(r => !r.Succeeded).Select(r => r.Error).Should().OnlyContain(
                ex => ex is BusinessRuleException && Equals(ex.Data["ErrorCode"], "BACKORDER_INSUFFICIENT_AVAILABLE"),
                "no ReallocateBackorderAsync thread may fail with a SQL 1205 deadlock");

            // If ReleaseAsync committed after all 5 initial threads checked availability, run a second simultaneous 5-thread wave
            await using (var checkDb = CreateContext())
            {
                var demandSvc = CreateDemandService(checkDb, userId);
                var stillOpen = await demandSvc.ListBackordersAsync(warehouseId, "Open");
                var currentStock = await checkDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.ProductId == productId);
                if (currentStock.ReservedQuantity < 5m && stillOpen.Count > 0)
                {
                    using var wave2Gate = new ManualResetEventSlim(false);
                    var wave2Tasks = stillOpen.Select(bo => Task.Run(async () =>
                    {
                        wave2Gate.Wait();
                        await using var workerDb = CreateContext();
                        var svc = CreateDemandService(workerDb, userId);
                        try
                        {
                            var dto = await svc.ReallocateBackorderAsync(bo.Id, new BackorderReallocateDto
                            {
                                RowVersion = bo.RowVersion!
                            });
                            return (Succeeded: true, Backorder: (BackorderDto?)dto, Error: (Exception?)null);
                        }
                        catch (Exception ex)
                        {
                            return (Succeeded: false, Backorder: (BackorderDto?)null, Error: (Exception?)ex);
                        }
                    })).ToArray();
                    wave2Gate.Set();
                    var wave2Results = await Task.WhenAll(wave2Tasks);
                    wave2Results.Where(r => !r.Succeeded).Select(r => r.Error).Should().OnlyContain(
                        ex => ex is BusinessRuleException && Equals(ex.Data["ErrorCode"], "BACKORDER_INSUFFICIENT_AVAILABLE"));
                }
            }

            await using (var verifyDb = CreateContext())
            {
                var stock = await verifyDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == warehouseId && x.ProductId == productId);
                stock.Quantity.Should().Be(5m);
                stock.ReservedQuantity.Should().Be(5m, "total newly reserved quantity across all 5 competing backorders must be exactly 5");

                var activeReservationSum = await verifyDb.StockReservations.AsNoTracking()
                    .Where(x => x.WarehouseId == warehouseId &&
                                x.ProductId == productId &&
                                x.Status != StockReservationStatus.Released &&
                                x.Status != StockReservationStatus.Cancelled)
                    .SumAsync(x => x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity);
                activeReservationSum.Should().Be(5m, "active StockReservations sum must match InventoryStock.ReservedQuantity (5)");

                var allocatedBackorders = await verifyDb.Backorders.AsNoTracking()
                    .CountAsync(x => x.WarehouseId == warehouseId && x.Status == BackorderStatus.Allocated && x.RecoveredQuantity == 5m);
                var openBackorderCount = await verifyDb.Backorders.AsNoTracking()
                    .CountAsync(x => x.WarehouseId == warehouseId && x.Status == BackorderStatus.Open && x.RecoveredQuantity == 0m);
                allocatedBackorders.Should().Be(1, "exactly 1 of the 5 backorders (each needing 5 units) can be allocated by 5 released units");
                openBackorderCount.Should().Be(4, "the remaining 4 backorders must remain Open with 0 over-allocation");
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            await cleanDb.AuditLogs.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryTransactions.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.PickingTasks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockAllocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Backorders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.StockReservations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrderLines.Where(x => x.SalesOrder.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.SalesOrders.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.InventoryStocks.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.WarehouseLocations.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.UserWarehouses.Where(x => x.WarehouseId == warehouseId).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => x.Id == warehouseId).ExecuteDeleteAsync();
            if (productId > 0) await cleanDb.Products.Where(x => x.Id == productId).ExecuteDeleteAsync();
            if (customerId > 0) await cleanDb.BusinessPartners.Where(x => x.Id == customerId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    private static StocktakeService CreateStocktakeService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        return new StocktakeService(
            new StocktakeRepository(db),
            new InventoryStockRepository(db),
            new InventoryTransactionRepository(db),
            new WarehouseRepository(db),
            new UnitOfWork(db),
            new AuditLogRepository(db),
            auth,
            current,
            new InventoryLockService(db, auth, current),
            new ProductRepository(db));
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
