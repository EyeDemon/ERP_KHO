using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class Wave10FullWarehouseMixedStormSqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    [SqlServerFact]
    public async Task MixedStorm12Threads_SixSubsystems_TwoProducts_ZeroDeadlocks_And_LedgerConservation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var unitCode = $"U10{suffix[..3]}";
        var skuACode = $"W10-SKUA-{suffix}";
        var skuBCode = $"W10-SKUB-{suffix}";

        int makerId = 0;
        int checkerId = 0;
        int wh1Id = 0;
        int wh2Id = 0;
        int customerId = 0;
        int supplierId = 0;
        int skuAId = 0;
        int skuBId = 0;
        int wh1ReceivingId = 0;
        int wh1LegacyId = 0;
        int binA1Id = 0;
        int binA2Id = 0;
        int binB1Id = 0;
        int binB2Id = 0;
        int binCId = 0;
        int wh2LegacyId = 0;

        int putawayTaskAId = 0;
        int putawayItemAId = 0;
        string putawayTaskARowVersion = string.Empty;
        int putawayTaskBId = 0;
        int putawayItemBId = 0;
        string putawayTaskBRowVersion = string.Empty;

        int so1Id = 0;
        string so1RowVersion = string.Empty;
        int so2Id = 0;
        string so2RowVersion = string.Empty;
        int so3Id = 0;
        string so3RowVersion = string.Empty;

        int pickingTask1Id = 0;
        int pickingLine1Id = 0;
        string pickingTask1RowVersion = string.Empty;
        int pickingTask2Id = 0;
        int pickingLine2Id = 0;
        string pickingTask2RowVersion = string.Empty;

        int stockBina2Id = 0;
        int transferId = 0;
        int stocktake1Id = 0;
        int stocktake2Id = 0;
        int importReceiptId = 0;

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
                    Username = $"w10_maker_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W10 Maker {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                var checker = new User
                {
                    Username = $"w10_checker_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W10 Checker {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                setupDb.Users.AddRange(maker, checker);

                var unit = new Unit { Code = unitCode, Name = "Cái", DecimalPlaces = 0 };
                setupDb.Units.Add(unit);

                var wh1 = new Warehouse { Code = $"W10WH1-{suffix}", Name = $"Kho Chính W10 {suffix}", IsActive = true };
                var wh2 = new Warehouse { Code = $"W10WH2-{suffix}", Name = $"Kho Phụ W10 {suffix}", IsActive = true };
                setupDb.Warehouses.AddRange(wh1, wh2);

                var customer = new BusinessPartner
                {
                    Code = $"W10CUST-{suffix}",
                    Name = $"Customer W10 {suffix}",
                    IsCustomer = true,
                    IsActive = true
                };
                var supplier = new BusinessPartner
                {
                    Code = $"W10SUP-{suffix}",
                    Name = $"Supplier W10 {suffix}",
                    IsSupplier = true,
                    IsActive = true
                };
                setupDb.BusinessPartners.AddRange(customer, supplier);
                await setupDb.SaveChangesAsync();

                makerId = maker.Id;
                checkerId = checker.Id;
                wh1Id = wh1.Id;
                wh2Id = wh2.Id;
                customerId = customer.Id;
                supplierId = supplier.Id;

                setupDb.UserWarehouses.AddRange(
                    new UserWarehouse { UserId = makerId, WarehouseId = wh1Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = checkerId, WarehouseId = wh1Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = makerId, WarehouseId = wh2Id, CreatedBy = makerId },
                    new UserWarehouse { UserId = checkerId, WarehouseId = wh2Id, CreatedBy = makerId });

                var wh1Receiving = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "RECEIVING",
                    Name = "Khu tiếp nhận WH1",
                    LocationType = WarehouseLocationType.Receiving,
                    IsPickable = false,
                    IsReceivable = true,
                    IsActive = true,
                    IsSystemManaged = true
                };
                var wh1Legacy = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "LEGACY",
                    Name = "Vị trí mặc định WH1",
                    LocationType = WarehouseLocationType.Legacy,
                    IsPickable = true,
                    IsActive = true,
                    IsSystemManaged = true
                };
                var binA1 = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "W10-BINA1",
                    Name = "Kệ A1",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var binA2 = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "W10-BINA2",
                    Name = "Kệ A2",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var binB1 = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "W10-BINB1",
                    Name = "Kệ B1",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var binB2 = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "W10-BINB2",
                    Name = "Kệ B2",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var binC = new WarehouseLocation
                {
                    WarehouseId = wh1Id,
                    Code = "W10-BINC",
                    Name = "Kệ C",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsActive = true
                };
                var wh2Legacy = new WarehouseLocation
                {
                    WarehouseId = wh2Id,
                    Code = "LEGACY",
                    Name = "Vị trí mặc định WH2",
                    LocationType = WarehouseLocationType.Legacy,
                    IsPickable = true,
                    IsActive = true,
                    IsSystemManaged = true
                };
                setupDb.WarehouseLocations.AddRange(wh1Receiving, wh1Legacy, binA1, binA2, binB1, binB2, binC, wh2Legacy);

                var prodA = new Product
                {
                    Code = skuACode,
                    Name = $"SKU A {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                var prodB = new Product
                {
                    Code = skuBCode,
                    Name = $"SKU B {suffix}",
                    UnitId = unit.Id,
                    IsActive = true
                };
                setupDb.Products.AddRange(prodA, prodB);
                await setupDb.SaveChangesAsync();

                wh1ReceivingId = wh1Receiving.Id;
                wh1LegacyId = wh1Legacy.Id;
                binA1Id = binA1.Id;
                binA2Id = binA2.Id;
                binB1Id = binB1.Id;
                binB2Id = binB2.Id;
                binCId = binC.Id;
                wh2LegacyId = wh2Legacy.Id;
                skuAId = prodA.Id;
                skuBId = prodB.Id;

                // Seed initial stock buckets AND corresponding Import ledger rows so conservation holds from t=0:
                // SKU-A: RECEIVING=20, LEGACY=10, BINA1=30 (Reserved=10), BINA2=25 (Reserved=0) => Total = 85
                // SKU-B: RECEIVING=20, LEGACY=10, BINB1=30 (Reserved=10), BINB2=25 (Reserved=0) => Total = 85
                var stockRecA = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = wh1ReceivingId,
                    ProductId = skuAId,
                    Status = InventoryStatus.Available,
                    Quantity = 20m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockLegA = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = wh1LegacyId,
                    ProductId = skuAId,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockBina1 = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = binA1Id,
                    ProductId = skuAId,
                    Status = InventoryStatus.Available,
                    Quantity = 30m,
                    ReservedQuantity = 10m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockBina2 = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = binA2Id,
                    ProductId = skuAId,
                    Status = InventoryStatus.Available,
                    Quantity = 25m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockRecB = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = wh1ReceivingId,
                    ProductId = skuBId,
                    Status = InventoryStatus.Available,
                    Quantity = 20m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockLegB = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = wh1LegacyId,
                    ProductId = skuBId,
                    Status = InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockBinb1 = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = binB1Id,
                    ProductId = skuBId,
                    Status = InventoryStatus.Available,
                    Quantity = 30m,
                    ReservedQuantity = 10m,
                    LastUpdated = DateTime.UtcNow
                };
                var stockBinb2 = new InventoryStock
                {
                    WarehouseId = wh1Id,
                    LocationId = binB2Id,
                    ProductId = skuBId,
                    Status = InventoryStatus.Available,
                    Quantity = 25m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                };
                setupDb.InventoryStocks.AddRange(stockRecA, stockLegA, stockBina1, stockBina2, stockRecB, stockLegB, stockBinb1, stockBinb2);

                var importReceiptA = new ImportReceipt
                {
                    Code = $"NKA-W10-{suffix}",
                    WarehouseId = wh1Id,
                    SupplierId = supplierId,
                    Status = ReceiptStatus.Posted,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow
                };
                var importReceiptB = new ImportReceipt
                {
                    Code = $"NKB-W10-{suffix}",
                    WarehouseId = wh1Id,
                    SupplierId = supplierId,
                    Status = ReceiptStatus.Posted,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow
                };
                setupDb.ImportReceipts.AddRange(importReceiptA, importReceiptB);
                await setupDb.SaveChangesAsync();

                importReceiptId = importReceiptA.Id;
                stockBina2Id = stockBina2.Id;

                var lineA = new ImportReceiptDetail
                {
                    ImportReceiptId = importReceiptA.Id,
                    ProductId = skuAId,
                    Quantity = 20m,
                    ExpectedQuantity = 20m,
                    ReceivedQuantity = 20m,
                    AcceptedQuantity = 20m,
                    PostedQuantity = 20m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactor = 1m,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 20m,
                    BaseReceivedQuantity = 20m,
                    BaseAcceptedQuantity = 20m,
                    BasePostedQuantity = 20m
                };
                var lineB = new ImportReceiptDetail
                {
                    ImportReceiptId = importReceiptB.Id,
                    ProductId = skuBId,
                    Quantity = 20m,
                    ExpectedQuantity = 20m,
                    ReceivedQuantity = 20m,
                    AcceptedQuantity = 20m,
                    PostedQuantity = 20m,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactor = 1m,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = 20m,
                    BaseReceivedQuantity = 20m,
                    BaseAcceptedQuantity = 20m,
                    BasePostedQuantity = 20m
                };
                setupDb.ImportReceiptDetails.AddRange(lineA, lineB);
                await setupDb.SaveChangesAsync();

                var initialSeeds = new (int ProductId, int LocationId, decimal Qty)[]
                {
                    (skuAId, wh1ReceivingId, 20m),
                    (skuAId, wh1LegacyId, 10m),
                    (skuAId, binA1Id, 30m),
                    (skuAId, binA2Id, 25m),
                    (skuBId, wh1ReceivingId, 20m),
                    (skuBId, wh1LegacyId, 10m),
                    (skuBId, binB1Id, 30m),
                    (skuBId, binB2Id, 25m)
                };
                foreach (var seed in initialSeeds)
                {
                    setupDb.InventoryTransactions.Add(new InventoryTransaction
                    {
                        WarehouseId = wh1Id,
                        LocationId = seed.LocationId,
                        ProductId = seed.ProductId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Import,
                        Quantity = seed.Qty,
                        ReferenceType = "ImportReceipt",
                        ReferenceId = importReceiptId,
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = makerId,
                        Note = "Wave-10 Initial Seed"
                    });
                }

                // Subsystem 1 setup: 2 PutawayTasks from RECEIVING
                var ptA = new PutawayTask
                {
                    ReceiptId = importReceiptA.Id,
                    WarehouseId = wh1Id,
                    Status = PutawayTaskStatus.Assigned,
                    AssignedUserId = makerId,
                    CreatedBy = makerId
                };
                var ptItemA = new PutawayTaskItem
                {
                    PutawayTask = ptA,
                    ReceiptLineId = lineA.Id,
                    ProductId = skuAId,
                    InventoryStatus = InventoryStatus.Available,
                    SourceLocationId = wh1ReceivingId,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseUnitDecimalPlaces = 0,
                    RequiredOperationQuantity = 20m,
                    RequiredBaseQuantity = 20m,
                    MovedBaseQuantity = 0m
                };
                var ptB = new PutawayTask
                {
                    ReceiptId = importReceiptB.Id,
                    WarehouseId = wh1Id,
                    Status = PutawayTaskStatus.Assigned,
                    AssignedUserId = makerId,
                    CreatedBy = makerId
                };
                var ptItemB = new PutawayTaskItem
                {
                    PutawayTask = ptB,
                    ReceiptLineId = lineB.Id,
                    ProductId = skuBId,
                    InventoryStatus = InventoryStatus.Available,
                    SourceLocationId = wh1ReceivingId,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    ConversionVersionSnapshot = 1,
                    BaseUnitDecimalPlaces = 0,
                    RequiredOperationQuantity = 20m,
                    RequiredBaseQuantity = 20m,
                    MovedBaseQuantity = 0m
                };
                setupDb.PutawayTasks.AddRange(ptA, ptB);
                setupDb.PutawayTaskItems.AddRange(ptItemA, ptItemB);
                await setupDb.SaveChangesAsync();

                putawayTaskAId = ptA.Id;
                putawayItemAId = ptItemA.Id;
                putawayTaskARowVersion = Convert.ToBase64String(ptA.RowVersion);
                putawayTaskBId = ptB.Id;
                putawayItemBId = ptItemB.Id;
                putawayTaskBRowVersion = Convert.ToBase64String(ptB.RowVersion);

                // Subsystem 2 setup: 3 multi-line SalesOrders in Draft with opposite product orders (SKU-A, SKU-B) vs (SKU-B, SKU-A)
                var soSvc = CreateDemandService(setupDb, makerId);
                var createdSo1 = await soSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"EXT-W10-1-{suffix}",
                    CustomerId = customerId,
                    WarehouseId = wh1Id,
                    Priority = 1,
                    Lines =
                    [
                        new CreateSalesOrderLineDto { ExternalLineId = "L1", ProductId = skuAId, OrderedQuantity = 6m },
                        new CreateSalesOrderLineDto { ExternalLineId = "L2", ProductId = skuBId, OrderedQuantity = 6m }
                    ]
                });
                var createdSo2 = await soSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"EXT-W10-2-{suffix}",
                    CustomerId = customerId,
                    WarehouseId = wh1Id,
                    Priority = 1,
                    Lines =
                    [
                        new CreateSalesOrderLineDto { ExternalLineId = "L1", ProductId = skuBId, OrderedQuantity = 6m },
                        new CreateSalesOrderLineDto { ExternalLineId = "L2", ProductId = skuAId, OrderedQuantity = 6m }
                    ]
                });
                var createdSo3 = await soSvc.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"EXT-W10-3-{suffix}",
                    CustomerId = customerId,
                    WarehouseId = wh1Id,
                    Priority = 1,
                    Lines =
                    [
                        new CreateSalesOrderLineDto { ExternalLineId = "L1", ProductId = skuBId, OrderedQuantity = 8m },
                        new CreateSalesOrderLineDto { ExternalLineId = "L2", ProductId = skuAId, OrderedQuantity = 8m }
                    ]
                });
                so1Id = createdSo1.Id;
                so1RowVersion = createdSo1.RowVersion!;
                so2Id = createdSo2.Id;
                so2RowVersion = createdSo2.RowVersion!;
                so3Id = createdSo3.Id;
                so3RowVersion = createdSo3.RowVersion!;

                // Subsystem 3 setup: 2 Reservations + Allocations + PickingTasks (1 for SKU-A on BINA1, 1 for SKU-B on BINB1)
                var resPreA = new StockReservation
                {
                    ReservationCode = $"RES-W10A-{suffix}",
                    WarehouseId = wh1Id,
                    ProductId = skuAId,
                    Quantity = 10m,
                    Status = StockReservationStatus.Active,
                    SourceType = "SalesOrder",
                    SourceId = 9101,
                    SourceCode = $"SO-W10-PREA-{suffix}",
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(4)
                };
                var resPreB = new StockReservation
                {
                    ReservationCode = $"RES-W10B-{suffix}",
                    WarehouseId = wh1Id,
                    ProductId = skuBId,
                    Quantity = 10m,
                    Status = StockReservationStatus.Active,
                    SourceType = "SalesOrder",
                    SourceId = 9102,
                    SourceCode = $"SO-W10-PREB-{suffix}",
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(4)
                };
                setupDb.StockReservations.AddRange(resPreA, resPreB);
                await setupDb.SaveChangesAsync();

                var allocPreA = new StockAllocation
                {
                    AllocationCode = $"ALC-W10A-{suffix}",
                    ReservationId = resPreA.Id,
                    WarehouseId = wh1Id,
                    LocationId = binA1Id,
                    ProductId = skuAId,
                    InventoryStatus = InventoryStatus.Available,
                    Strategy = AllocationStrategy.LocationOrder,
                    Quantity = 10m,
                    Status = StockAllocationStatus.Picking,
                    AllocatedBy = makerId
                };
                var allocPreB = new StockAllocation
                {
                    AllocationCode = $"ALC-W10B-{suffix}",
                    ReservationId = resPreB.Id,
                    WarehouseId = wh1Id,
                    LocationId = binB1Id,
                    ProductId = skuBId,
                    InventoryStatus = InventoryStatus.Available,
                    Strategy = AllocationStrategy.LocationOrder,
                    Quantity = 10m,
                    Status = StockAllocationStatus.Picking,
                    AllocatedBy = makerId
                };
                setupDb.StockAllocations.AddRange(allocPreA, allocPreB);
                await setupDb.SaveChangesAsync();

                var pickTask1 = new PickingTask
                {
                    TaskCode = $"PK-W10A-{suffix}",
                    WarehouseId = wh1Id,
                    PickingType = "STANDARD",
                    SourceType = "SalesOrder",
                    SourceId = 9101,
                    SourceCode = $"SO-W10-PREA-{suffix}",
                    Status = PickingTaskStatus.InProgress,
                    AssignedUserId = makerId,
                    StartedAt = DateTime.UtcNow,
                    CreatedBy = makerId
                };
                var pickLine1 = new PickingTaskLine
                {
                    PickingTask = pickTask1,
                    AllocationId = allocPreA.Id,
                    ProductId = skuAId,
                    SourceLocationId = binA1Id,
                    RequestedQuantity = 10m,
                    PickedQuantity = 0m,
                    Sequence = 1,
                    Status = PickingTaskLineStatus.Open
                };
                var pickTask2 = new PickingTask
                {
                    TaskCode = $"PK-W10B-{suffix}",
                    WarehouseId = wh1Id,
                    PickingType = "STANDARD",
                    SourceType = "SalesOrder",
                    SourceId = 9102,
                    SourceCode = $"SO-W10-PREB-{suffix}",
                    Status = PickingTaskStatus.InProgress,
                    AssignedUserId = makerId,
                    StartedAt = DateTime.UtcNow,
                    CreatedBy = makerId
                };
                var pickLine2 = new PickingTaskLine
                {
                    PickingTask = pickTask2,
                    AllocationId = allocPreB.Id,
                    ProductId = skuBId,
                    SourceLocationId = binB1Id,
                    RequestedQuantity = 10m,
                    PickedQuantity = 0m,
                    Sequence = 1,
                    Status = PickingTaskLineStatus.Open
                };
                setupDb.PickingTasks.AddRange(pickTask1, pickTask2);
                setupDb.PickingTaskLines.AddRange(pickLine1, pickLine2);

                // Subsystem 4 setup: Approved StockTransfer (SKU-B & SKU-A) ready for DispatchAsync
                var transfer = new StockTransfer
                {
                    Code = $"STF-W10-{suffix}",
                    SourceWarehouseId = wh1Id,
                    DestinationWarehouseId = wh2Id,
                    Status = StockTransferStatus.Approved,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    ApprovedBy = checkerId,
                    ApprovedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StockTransferDetail
                        {
                            ProductId = skuBId,
                            RequestedQuantity = 4m,
                            DispatchedQuantity = 0m,
                            ReceivedQuantity = 0m
                        },
                        new StockTransferDetail
                        {
                            ProductId = skuAId,
                            RequestedQuantity = 4m,
                            DispatchedQuantity = 0m,
                            ReceivedQuantity = 0m
                        }
                    ]
                };
                setupDb.StockTransfers.Add(transfer);

                // Subsystem 5 setup: 2 Draft Stocktakes with multi-product lines
                var st1 = new Stocktake
                {
                    Code = $"ST-W10-1-{suffix}",
                    WarehouseId = wh1Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            ProductId = skuAId,
                            LocationId = binA2Id,
                            SystemQuantity = 25m,
                            ActualQuantity = 24m
                        },
                        new StocktakeDetail
                        {
                            ProductId = skuBId,
                            LocationId = binB2Id,
                            SystemQuantity = 25m,
                            ActualQuantity = 24m
                        }
                    ]
                };
                var st2 = new Stocktake
                {
                    Code = $"ST-W10-2-{suffix}",
                    WarehouseId = wh1Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = makerId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            ProductId = skuBId,
                            LocationId = binB1Id,
                            SystemQuantity = 30m,
                            ActualQuantity = 29m
                        },
                        new StocktakeDetail
                        {
                            ProductId = skuAId,
                            LocationId = binA1Id,
                            SystemQuantity = 30m,
                            ActualQuantity = 29m
                        }
                    ]
                };
                setupDb.Stocktakes.AddRange(st1, st2);
                await setupDb.SaveChangesAsync();

                pickingTask1Id = pickTask1.Id;
                pickingLine1Id = pickLine1.Id;
                pickingTask1RowVersion = Convert.ToBase64String(pickTask1.RowVersion);
                pickingTask2Id = pickTask2.Id;
                pickingLine2Id = pickLine2.Id;
                pickingTask2RowVersion = Convert.ToBase64String(pickTask2.RowVersion);
                transferId = transfer.Id;
                stocktake1Id = st1.Id;
                stocktake2Id = st2.Id;
            }

            // Launch all 12 concurrent threads across 6 subsystems at the exact same millisecond
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<(string Name, bool Succeeded, Exception? Error)> RunWorkerAsync(string name, Func<Task> action)
            {
                await startGate.Task;
                try
                {
                    await action();
                    return (name, true, null);
                }
                catch (Exception ex)
                {
                    return (name, false, ex);
                }
            }

            var tasks = new Task<(string Name, bool Succeeded, Exception? Error)>[]
            {
                // 1-2: PutawayService.MoveAsync (2 threads)
                Task.Run(() => RunWorkerAsync("1.Putaway_SKUA", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreatePutawayService(db, makerId);
                    await svc.MoveAsync(putawayTaskAId, new MovePutawayItemDto
                    {
                        ItemId = putawayItemAId,
                        DestinationLocationId = binA2Id,
                        Quantity = 10m,
                        UnitCode = unitCode,
                        RowVersion = putawayTaskARowVersion
                    });
                })),
                Task.Run(() => RunWorkerAsync("2.Putaway_SKUB", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreatePutawayService(db, makerId);
                    await svc.MoveAsync(putawayTaskBId, new MovePutawayItemDto
                    {
                        ItemId = putawayItemBId,
                        DestinationLocationId = binB2Id,
                        Quantity = 10m,
                        UnitCode = unitCode,
                        RowVersion = putawayTaskBRowVersion
                    });
                })),

                // 3-5: ReleaseSalesOrderAsync (3 threads with opposite product order (SKU-A, SKU-B) vs (SKU-B, SKU-A))
                Task.Run(() => RunWorkerAsync("3.ReleaseSO1_AB", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateDemandService(db, makerId);
                    await svc.ReleaseSalesOrderAsync(so1Id, new SalesOrderStateCommandDto
                    {
                        RowVersion = so1RowVersion,
                        Reason = "Wave-10 release SO1 (SKU-A, SKU-B)"
                    });
                })),
                Task.Run(() => RunWorkerAsync("4.ReleaseSO2_BA", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateDemandService(db, makerId);
                    await svc.ReleaseSalesOrderAsync(so2Id, new SalesOrderStateCommandDto
                    {
                        RowVersion = so2RowVersion,
                        Reason = "Wave-10 release SO2 (SKU-B, SKU-A)"
                    });
                })),
                Task.Run(() => RunWorkerAsync("5.ReleaseSO3_BA", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateDemandService(db, makerId);
                    await svc.ReleaseSalesOrderAsync(so3Id, new SalesOrderStateCommandDto
                    {
                        RowVersion = so3RowVersion,
                        Reason = "Wave-10 release SO3 (SKU-B, SKU-A)"
                    });
                })),

                // 6-7: PickingService (2 threads: 1 confirm pick, 1 short-pick + reallocate to alternative bin)
                Task.Run(() => RunWorkerAsync("6.Picking_Confirm_SKUA", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreatePickingService(db, makerId);
                    var picked = await svc.PickAsync(pickingTask1Id, new PickScanDto
                    {
                        TaskLineId = pickingLine1Id,
                        LocationBarcode = "W10-BINA1",
                        ProductBarcode = skuACode,
                        Quantity = 10m,
                        RowVersion = pickingTask1RowVersion
                    });
                    await svc.CompleteAsync(pickingTask1Id, new PickingStateCommandDto
                    {
                        RowVersion = picked.RowVersion!
                    });
                })),
                Task.Run(() => RunWorkerAsync("7.Picking_ShortPick_Reallocate_SKUB", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreatePickingService(db, makerId);
                    var reported = await svc.ReportShortPickAsync(pickingTask2Id, new ReportShortPickDto
                    {
                        TaskLineId = pickingLine2Id,
                        ActualPickedQuantity = 0m,
                        Reason = "Báo thiếu kiểm thử bão giao dịch Wave-10",
                        RowVersion = pickingTask2RowVersion
                    });
                    var exId = reported.ShortPicks.Single().Id;
                    await svc.ResolveShortPickAsync(pickingTask2Id, exId, new ResolveShortPickDto
                    {
                        Resolution = nameof(ShortPickResolutionType.AlternativeLocation),
                        Note = "Chuyển vị trí lấy hàng sang kệ B2",
                        RowVersion = reported.RowVersion!
                    });
                })),

                // 8-9: InventoryMovementService.MoveAsync & StockTransferService.DispatchAsync (2 threads)
                Task.Run(() => RunWorkerAsync("8.InventoryMove_SKUA_BINA2_to_BINC", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateMovementService(db, makerId);
                    await svc.MoveAsync(new CreateInventoryMoveDto
                    {
                        InventoryStockId = stockBina2Id,
                        DestinationLocationId = binCId,
                        Quantity = 5m,
                        Reason = "Wave-10 internal bin movement"
                    });
                })),
                Task.Run(() => RunWorkerAsync("9.StockTransfer_Dispatch_WH1_to_WH2", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateStockTransferService(db, makerId);
                    await svc.DispatchAsync(transferId);
                })),

                // 10-11: ApproveStocktakeAsync (2 threads)
                Task.Run(() => RunWorkerAsync("10.ApproveStocktake1_AB", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateStocktakeService(db, checkerId);
                    await svc.ApproveStocktakeAsync(stocktake1Id, checkerId);
                })),
                Task.Run(() => RunWorkerAsync("11.ApproveStocktake2_BA", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateStocktakeService(db, checkerId);
                    await svc.ApproveStocktakeAsync(stocktake2Id, checkerId);
                })),

                // 12: InventoryLockService.CreateAsync + ReleaseAsync (1 thread)
                Task.Run(() => RunWorkerAsync("12.InventoryLock_CreateAndRelease_SKUB", async () =>
                {
                    await using var db = CreateContext();
                    var svc = CreateLockService(db, makerId);
                    var created = await svc.CreateAsync(new CreateInventoryLockDto
                    {
                        WarehouseId = wh1Id,
                        LocationId = binB2Id,
                        ProductId = skuBId,
                        InventoryStatus = "Available",
                        LockType = nameof(InventoryLockType.QualityHold),
                        Reason = "Wave-10 transient inspection hold"
                    });
                    await svc.ReleaseAsync(created.Id, new ReleaseInventoryLockDto
                    {
                        Reason = "Wave-10 inspection cleared",
                        RowVersion = created.RowVersion
                    });
                }))
            };

            startGate.SetResult();
            var results = await Task.WhenAll(tasks);

            // 1. Verify 0 unhandled exceptions and 0 SQL Deadlock 1205 escaping across all 12 threads
            foreach (var r in results)
            {
                if (!r.Succeeded)
                {
                    IsSqlDeadlock(r.Error).Should().BeFalse(
                        $"thread {r.Name} must never fail with an unhandled SQL Deadlock 1205, but got: {r.Error}");
                    (r.Error is BusinessRuleException or ConcurrencyException).Should().BeTrue(
                        $"any rejected thread ({r.Name}) must fail with a deterministic business/concurrency conflict, not {r.Error}");
                }
            }

            results.Count(r => r.Succeeded).Should().BeGreaterThanOrEqualTo(6,
                "core non-conflicting operations across the 6 subsystems must complete during the mixed storm");

            // 2. Verify ledger conservation & stock invariants across WH1 and WH2
            await using (var verifyDb = CreateContext())
            {
                var allStocks = await verifyDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == wh1Id || x.WarehouseId == wh2Id)
                    .ToListAsync();

                allStocks.Should().NotBeEmpty();
                foreach (var stock in allStocks)
                {
                    stock.Quantity.Should().BeGreaterThanOrEqualTo(0m,
                        $"stock row {stock.Id} (Warehouse={stock.WarehouseId}, Product={stock.ProductId}, Location={stock.LocationId}) must have non-negative Quantity");
                    stock.ReservedQuantity.Should().BeGreaterThanOrEqualTo(0m,
                        $"stock row {stock.Id} must have non-negative ReservedQuantity");
                    stock.ReservedQuantity.Should().BeLessThanOrEqualTo(stock.Quantity,
                        $"stock row {stock.Id} (Product={stock.ProductId}, Location={stock.LocationId}) must satisfy 0 <= ReservedQuantity ({stock.ReservedQuantity}) <= Quantity ({stock.Quantity})");
                }

                // Verify Reservation alignment for both SKU-A and SKU-B in WH1
                foreach (var productId in new[] { skuAId, skuBId })
                {
                    var totalReservedOnStock = allStocks
                        .Where(x => x.WarehouseId == wh1Id && x.ProductId == productId)
                        .Sum(x => x.ReservedQuantity);

                    var activeReservationRemaining = await verifyDb.StockReservations.AsNoTracking()
                        .Where(x => x.WarehouseId == wh1Id &&
                                    x.ProductId == productId &&
                                    x.Status != StockReservationStatus.Released &&
                                    x.Status != StockReservationStatus.Cancelled &&
                                    x.Status != StockReservationStatus.Consumed &&
                                    x.Status != StockReservationStatus.Expired)
                        .SumAsync(x => x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity);

                    totalReservedOnStock.Should().Be(activeReservationRemaining,
                        $"Product {productId} ReservedQuantity sum across InventoryStocks must match active StockReservations remaining sum");
                }

                // Verify Warehouse-level and Location-level Ledger Conservation in WH1
                var wh1Transactions = await verifyDb.InventoryTransactions.AsNoTracking()
                    .Where(x => x.WarehouseId == wh1Id)
                    .ToListAsync();
                var wh1PutawayMovements = await verifyDb.InventoryLocationMovements.AsNoTracking()
                    .Where(x => x.WarehouseId == wh1Id)
                    .ToListAsync();

                foreach (var productId in new[] { skuAId, skuBId })
                {
                    var stockSum = allStocks
                        .Where(x => x.WarehouseId == wh1Id && x.ProductId == productId)
                        .Sum(x => x.Quantity);

                    var ledgerNet = wh1Transactions
                        .Where(t => t.ProductId == productId)
                        .Sum(GetWarehouseLedgerDelta);

                    stockSum.Should().Be(ledgerNet,
                        $"Product {productId} in WH1 must satisfy sum(InventoryStocks.Quantity) == sum(Ledger Net Movements)");

                    var locationIds = allStocks
                        .Where(x => x.WarehouseId == wh1Id && x.ProductId == productId && x.LocationId.HasValue)
                        .Select(x => x.LocationId!.Value)
                        .Distinct()
                        .ToList();

                    var totalLocationLedgerNet = locationIds.Sum(locationId =>
                        wh1Transactions
                            .Where(t => t.ProductId == productId)
                            .Sum(t => GetLocationLedgerDelta(t, locationId))
                        + wh1PutawayMovements
                            .Where(m => m.ProductId == productId)
                            .Sum(m => (m.ToLocationId == locationId ? m.BaseQuantity : 0m) -
                                      (m.FromLocationId == locationId ? m.BaseQuantity : 0m)));

                    stockSum.Should().Be(totalLocationLedgerNet,
                        $"Product {productId} total location stock across all bins in WH1 must equal total location ledger net movements");
                }
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            var warehouseIds = new[] { wh1Id, wh2Id };
            await cleanDb.AuditLogs.Where(x => x.WarehouseId.HasValue && warehouseIds.Contains(x.WarehouseId.Value)).ExecuteDeleteAsync();
            await cleanDb.InventoryLocks.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.InventoryLocationMovements.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.InventoryTransactions.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.PutawayTaskItems.Where(x => warehouseIds.Contains(x.PutawayTask.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.PutawayTasks.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            var lineIds = await cleanDb.PickingTaskLines.Where(x => warehouseIds.Contains(x.PickingTask.WarehouseId)).Select(x => x.Id).ToArrayAsync();
            if (lineIds.Length > 0)
            {
                await cleanDb.ShortPickExceptions.Where(x => lineIds.Contains(x.PickingTaskLineId)).ExecuteDeleteAsync();
                await cleanDb.PickingTaskLines.Where(x => lineIds.Contains(x.Id)).ExecuteDeleteAsync();
            }
            await cleanDb.PickingTasks.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StockAllocations.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.Backorders.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StockReservations.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.SalesOrderLines.Where(x => warehouseIds.Contains(x.SalesOrder.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.SalesOrders.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StockTransferDetails.Where(x => warehouseIds.Contains(x.StockTransfer.SourceWarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StockTransfers.Where(x => warehouseIds.Contains(x.SourceWarehouseId)).ExecuteDeleteAsync();
            await cleanDb.StocktakeDetails.Where(x => warehouseIds.Contains(x.Stocktake.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.Stocktakes.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.ImportReceiptDetails.Where(x => warehouseIds.Contains(x.ImportReceipt.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.ImportReceipts.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.InventoryStocks.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.WarehouseLocations.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.UserWarehouses.Where(x => warehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => warehouseIds.Contains(x.Id)).ExecuteDeleteAsync();
            if (skuAId > 0 || skuBId > 0)
                await cleanDb.Products.Where(x => x.Id == skuAId || x.Id == skuBId).ExecuteDeleteAsync();
            if (customerId > 0 || supplierId > 0)
                await cleanDb.BusinessPartners.Where(x => x.Id == customerId || x.Id == supplierId).ExecuteDeleteAsync();
            await cleanDb.Units.Where(x => x.Code == unitCode).ExecuteDeleteAsync();
            if (makerId > 0 || checkerId > 0)
                await cleanDb.Users.Where(x => x.Id == makerId || x.Id == checkerId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task WarehouseAuthorization_TransactionCache_PrunesAcrossSequentialTransactions_And_InvalidatesImmediately()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        int userId = 0;
        int wh1Id = 0;
        int wh2Id = 0;

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
                    Username = $"w10_cache_{suffix.ToLowerInvariant()}",
                    PasswordHash = "Hash",
                    FullName = $"W10 Cache User {suffix}",
                    RoleId = role.Id,
                    IsActive = true
                };
                var wh1 = new Warehouse { Code = $"W10C1-{suffix}", Name = $"Kho Cache 1 {suffix}", IsActive = true };
                var wh2 = new Warehouse { Code = $"W10C2-{suffix}", Name = $"Kho Cache 2 {suffix}", IsActive = true };
                setupDb.Users.Add(user);
                setupDb.Warehouses.AddRange(wh1, wh2);
                await setupDb.SaveChangesAsync();

                userId = user.Id;
                wh1Id = wh1.Id;
                wh2Id = wh2.Id;

                setupDb.UserWarehouses.Add(new UserWarehouse
                {
                    UserId = userId,
                    WarehouseId = wh1Id,
                    CreatedBy = userId
                });
                await setupDb.SaveChangesAsync();
            }

            await using (var db = CreateContext())
            {
                var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
                var auth = new WarehouseAuthorizationService(db, current);

                // Run sequential transactions on the same scoped instance
                await using (var tx1 = await db.Database.BeginTransactionAsync())
                {
                    (await auth.CanAccessWarehouseAsync(wh1Id)).Should().BeTrue();
                    (await auth.CanAccessWarehouseAsync(wh2Id)).Should().BeFalse();

                    // Grant access to wh2 mid-transaction and invalidate cache
                    db.UserWarehouses.Add(new UserWarehouse
                    {
                        UserId = userId,
                        WarehouseId = wh2Id,
                        CreatedBy = userId
                    });
                    await db.SaveChangesAsync();

                    auth.InvalidateWarehouseAccessCache(userId);

                    var accessible = await auth.GetAccessibleWarehouseIdsAsync();
                    accessible.Should().Contain(wh1Id).And.Contain(wh2Id,
                        "InvalidateWarehouseAccessCache must immediately clear _txAccessibleWarehouses and _txVerifiedAccess inside an active transaction");
                    await tx1.CommitAsync();
                }

                // Start a second transaction and revoke wh2 access
                await using (var tx2 = await db.Database.BeginTransactionAsync())
                {
                    await db.UserWarehouses
                        .Where(x => x.UserId == userId && x.WarehouseId == wh2Id)
                        .ExecuteDeleteAsync();
                    auth.InvalidateWarehouseAccessCache(userId);

                    (await auth.CanAccessWarehouseAsync(wh2Id)).Should().BeFalse(
                        "revoked warehouse access must be rejected immediately in subsequent transaction after cache invalidation");
                    await tx2.CommitAsync();
                }
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            await cleanDb.UserWarehouses.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await cleanDb.Warehouses.Where(x => x.Id == wh1Id || x.Id == wh2Id).ExecuteDeleteAsync();
            if (userId > 0) await cleanDb.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }
    }

    private static decimal GetWarehouseLedgerDelta(InventoryTransaction tx) => tx.TransactionType switch
    {
        TransactionType.Import => tx.Quantity,
        TransactionType.AdjustmentIncrease => tx.Quantity,
        TransactionType.TransferIn => tx.Quantity,
        TransactionType.Export => -tx.Quantity,
        TransactionType.AdjustmentDecrease => -tx.Quantity,
        TransactionType.TransferOut => -tx.Quantity,
        TransactionType.Ship => -tx.Quantity,
        _ => 0m
    };

    private static decimal GetLocationLedgerDelta(InventoryTransaction tx, int locationId)
    {
        if (tx.TransactionType == TransactionType.Move)
        {
            var delta = 0m;
            if (tx.FromLocationId == locationId) delta -= tx.Quantity;
            if (tx.ToLocationId == locationId) delta += tx.Quantity;
            return delta;
        }

        if (tx.LocationId != locationId)
            return 0m;

        return GetWarehouseLedgerDelta(tx);
    }

    private static bool IsSqlDeadlock(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DeadlockException || current is SqlException { Number: 1205 })
                return true;
        }

        return false;
    }

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        var locks = new InventoryLockService(db, auth, current);
        return new PutawayService(db, auth, current, locks);
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

    private static PickingService CreatePickingService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        return new PickingService(
            db,
            auth,
            current,
            null,
            new InventoryLockService(db, auth, current));
    }

    private static InventoryMovementService CreateMovementService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        var locks = new InventoryLockService(db, auth, current);
        return new InventoryMovementService(db, auth, current, locks);
    }

    private static StockTransferService CreateStockTransferService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        return new StockTransferService(
            db,
            new InventoryStockRepository(db),
            auth,
            current);
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

    private static InventoryLockService CreateLockService(ErpKhoDbContext db, int userId)
    {
        var current = new ScopedUser(userId, "Manager", IsGlobalAdmin: false);
        var auth = new WarehouseAuthorizationService(db, current);
        return new InventoryLockService(db, auth, current);
    }

    private sealed record ScopedUser(int UserId, string Role, bool IsGlobalAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
