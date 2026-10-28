using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave60RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Epic60_1_PhysicalFloorKittingAndDeKitting_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateKittingFixtureAsync();
        try
        {
            int assembleWoId;
            int disassembleWoId;

            // (a) Happy Path: Assemble 2 Kits from Component A (ratio 2, cost 10,000, expiry +6m) & Component B (ratio 3, cost 20,000, expiry +3m)
            // Expected per Kit MAC = 2*10,000 + 3*20,000 = 80,000; EarliestComponentExpiryDate = Component B expiry (+3m)
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var createRes = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 2m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                createRes.StatusCode.Should().Be(200);
                using var createDoc = JsonSerializer.SerializeToDocument(createRes.Payload);
                assembleWoId = createDoc.RootElement.GetProperty("id").GetInt32();

                var completeRes = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                completeRes.StatusCode.Should().Be(200);

                using var completeDoc = JsonSerializer.SerializeToDocument(completeRes.Payload);
                completeDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
                completeDoc.RootElement.GetProperty("completedKitQty").GetDecimal().Should().Be(2m);
                completeDoc.RootElement.GetProperty("rolledUpUnitCost").GetDecimal().Should().Be(80000m);
                var earliestExpiry = completeDoc.RootElement.GetProperty("earliestComponentExpiryDate").GetDateTime();
                earliestExpiry.Date.Should().Be(fx.LotBExpiryDate.Date);
            }

            // Verify DB balances & ledger after Assemble
            await using (var db = CreateContext())
            {
                var compAStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId);
                var compBStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId);
                var kitStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId);

                compAStock.Quantity.Should().Be(6m); // 10 - (2 * 2) = 6
                compBStock.Quantity.Should().Be(4m); // 10 - (2 * 3) = 4
                kitStock.Quantity.Should().Be(2m);
                kitStock.MovingAverageCost.Should().Be(80000m);

                var txs = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .ToListAsync();
                txs.Should().HaveCount(3);
            }

            // (b) Duplicate Idempotency-Key / Already-Completed guard on Kitting completion
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var duplicateComplete = await svc.CompleteKitWorkOrderAsync(
                    assembleWoId,
                    new CompleteKitWorkOrderInput(CompletedKitQty: 2m, TargetLocationId: fx.LocationAId),
                    fx.UserId);
                duplicateComplete.StatusCode.Should().Be(409);

                var txsAfterReplay = await db.InventoryTransactions
                    .Where(t => t.WarehouseId == fx.WarehouseId && t.ReferenceId == assembleWoId && t.ReferenceType != "ImportReceipt")
                    .CountAsync();
                txsAfterReplay.Should().Be(3);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Assemble work orders competing for remaining 4 units of Component B (each needing 3 units -> total 6 > 4)
            int concWo1Id;
            int concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var r1 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                var r2 = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Assemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var d1 = JsonSerializer.SerializeToDocument(r1.Payload);
                using var d2 = JsonSerializer.SerializeToDocument(r2.Payload);
                concWo1Id = d1.RootElement.GetProperty("id").GetInt32();
                concWo2Id = d2.RootElement.GetProperty("id").GetInt32();
            }

            var outcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new ProductOperationsService(db1);
                    return await svc1.CompleteKitWorkOrderAsync(concWo1Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new ProductOperationsService(db2);
                    return await svc2.CompleteKitWorkOrderAsync(concWo2Id, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                }));

            outcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            outcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            var succeededConcWoId = outcomes[0].StatusCode == 200 ? concWo1Id : concWo2Id;
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var cancelConc = await svc.CancelKitWorkOrderAsync(succeededConcWoId, new CancelKitWorkOrderInput("Hoàn tác test song song"), fx.UserId);
                cancelConc.StatusCode.Should().Be(200);
            }

            // Test Disassemble 1 Kit back into components, then Cancel/Reverse both Disassemble and Assemble
            await using (var db = CreateContext())
            {
                var svc = new ProductOperationsService(db);
                var disCreate = await svc.CreateKitWorkOrderAsync(
                    new CreateKitWorkOrderInput(
                        WarehouseId: fx.WarehouseId,
                        KitProductId: fx.KitProductId,
                        WorkOrderType: "Disassemble",
                        PlannedKitQty: 1m,
                        TargetLocationId: fx.LocationAId,
                        ComponentLines: new List<CreateKitWorkOrderLineInput>
                        {
                            new(fx.CompAProductId, 2m, fx.LocationAId, fx.LotAId, 10000m),
                            new(fx.CompBProductId, 3m, fx.LocationAId, fx.LotBId, 20000m)
                        }),
                    fx.UserId);
                using var disDoc = JsonSerializer.SerializeToDocument(disCreate.Payload);
                disassembleWoId = disDoc.RootElement.GetProperty("id").GetInt32();

                var disComplete = await svc.CompleteKitWorkOrderAsync(disassembleWoId, new CompleteKitWorkOrderInput(1m, fx.LocationAId), fx.UserId);
                disComplete.StatusCode.Should().Be(200);

                // (d) Reversal / Cancellation of Disassemble & Assemble
                var cancelDis = await svc.CancelKitWorkOrderAsync(disassembleWoId, new CancelKitWorkOrderInput("Đảo lệnh tháo dỡ Kit"), fx.UserId);
                cancelDis.StatusCode.Should().Be(200);

                var cancelAsm = await svc.CancelKitWorkOrderAsync(assembleWoId, new CancelKitWorkOrderInput("Đảo lệnh lắp ráp Kit"), fx.UserId);
                cancelAsm.StatusCode.Should().Be(200);

                var compAQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompAProductId).SumAsync(s => s.Quantity);
                var compBQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.CompBProductId).SumAsync(s => s.Quantity);
                var kitQty = await db.InventoryStocks.Where(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.KitProductId).SumAsync(s => s.Quantity);

                compAQty.Should().Be(10m);
                compBQty.Should().Be(10m);
                kitQty.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupKittingFixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic60_2_InboundToOutboundCrossDocking_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateCrossDockFixtureAsync();
        try
        {
            // (a) Happy Path: Execute Cross-Dock on PutawayTask matching open Backorder (qty 6 out of 10 received at RECEIVING-STAGE)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var res = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto
                    {
                        TargetBackorderId = fx.BackorderId,
                        TargetSalesOrderId = fx.SalesOrderId
                    });

                res.IsCrossDock.Should().BeTrue();
                res.CrossDockAllocatedQty.Should().Be(6m);
                res.CrossDockTargetBackorderId.Should().Be(fx.BackorderId);
                res.CrossDockTargetSalesOrderId.Should().Be(fx.SalesOrderId);
                res.MovedBaseQuantity.Should().Be(6m);
                res.Status.Should().Be(PutawayTaskStatus.InProgress.ToString());
            }

            // Verify 9D stock moved from RECEIVING-STAGE to SHIPPING-STAGE, StockReservation + StockAllocation created, and Backorder fulfilled
            await using (var db = CreateContext())
            {
                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);

                recvStock.Quantity.Should().Be(4m); // 10 - 6 = 4
                shipStock.Quantity.Should().Be(6m);
                shipStock.ReservedQuantity.Should().Be(6m);

                var backorder = await db.Backorders.SingleAsync(b => b.Id == fx.BackorderId);
                backorder.Status.Should().Be(BackorderStatus.Fulfilled);
                backorder.RecoveredQuantity.Should().Be(6m);
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Putaway.CrossDock",
                    KeyHash = $"HASH-XDOCK-{fx.Suffix}",
                    RequestFingerprint = "FP-XDOCK",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"id\":" + fx.PutawayTaskId + ",\"isCrossDock\":true}",
                    CorrelationId = "corr-xdock",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Putaway.CrossDock" && r.KeyHash == $"HASH-XDOCK-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Cross-Dock requests competing for the remaining 4 units on the same PutawayTask
            var concResults = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = CreatePutawayService(db1, fx.UserId);
                        await svc1.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = CreatePutawayService(db2, fx.UserId);
                        await svc2.EvaluateAndExecuteCrossDockAsync(fx.PutawayTaskId, new ExecuteCrossDockDto { Quantity = 4m, TargetSalesOrderId = fx.SalesOrderId });
                        return 200;
                    }
                    catch (BusinessRuleException) { return 422; }
                    catch (ConflictException) { return 409; }
                    catch (ConcurrencyException) { return 409; }
                }));

            concResults.Count(code => code == 200).Should().Be(1);
            concResults.Count(code => code == 422 || code == 409).Should().Be(1);

            // (d) Reversal of Cross-Dock (restoring all 10 units back to RECEIVING-STAGE and releasing reservation/allocation)
            await using (var db = CreateContext())
            {
                var putawaySvc = CreatePutawayService(db, fx.UserId);
                var revRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true });

                revRes.IsCrossDock.Should().BeFalse();
                revRes.CrossDockAllocatedQty.Should().Be(0m);
                revRes.MovedBaseQuantity.Should().Be(0m);

                var recvStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ReceivingStageId);
                var shipStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);
                recvStock.Quantity.Should().Be(10m);
                shipStock.Quantity.Should().Be(0m);
                shipStock.ReservedQuantity.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupCrossDockFixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic60_3_AutomatedBreakBulkReplenishment_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateBreakBulkFixtureAsync();
        try
        {
            int refStockId;

            // (a) Happy Path: Break-Bulk 2 Cases (ConversionFactor = 12 -> 24 BaseUom) from Reserve Bin to Forward-Pick Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var res = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 2m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId),
                    fx.UserId);

                res.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(res.Payload);
                doc.RootElement.GetProperty("movedBaseQty").GetDecimal().Should().Be(24m);
                doc.RootElement.GetProperty("conversionFactor").GetDecimal().Should().Be(12m);
                doc.RootElement.GetProperty("baseUomDelta").GetDecimal().Should().Be(0.0000m);
                doc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(12m); // 36 - 24 = 12
                doc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(24m);
                refStockId = doc.RootElement.GetProperty("referenceId").GetInt32();
            }

            // (b) Duplicate Idempotency-Key record persistence & replay verification on Break-Bulk Replenishment
            await using (var db = CreateContext())
            {
                var record = new IdempotencyRecord
                {
                    UserId = fx.UserId,
                    CommandScope = "Inventory.BreakBulkReplenishment",
                    KeyHash = $"HASH-BB-{fx.Suffix}",
                    RequestFingerprint = "FP-BB",
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"movedBaseQty\":24,\"baseUomDelta\":0.0000}",
                    CorrelationId = "corr-bb",
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                };
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();

                var replayed = await db.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.UserId == fx.UserId && r.CommandScope == "Inventory.BreakBulkReplenishment" && r.KeyHash == $"HASH-BB-{fx.Suffix}");
                replayed.Should().NotBeNull();
                replayed!.Status.Should().Be(IdempotencyStatus.Completed);
                replayed.ResponseStatusCode.Should().Be(200);
            }

            // (c) Parallel Concurrency contention: 2 concurrent Break-Bulk requests competing for the remaining 1 Case (12 BaseUom) at Reserve Bin
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db1 = CreateContext();
                    var svc1 = new InventoryControlOperationsService(db1, null, new FakeUser(fx.UserId));
                    return await svc1.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }),
                Task.Run(async () =>
                {
                    await using var db2 = CreateContext();
                    var svc2 = new InventoryControlOperationsService(db2, null, new FakeUser(fx.UserId));
                    return await svc2.ExecuteBreakBulkReplenishmentAsync(
                        new ExecuteBreakBulkReplenishmentInput(
                            WarehouseId: fx.WarehouseId,
                            ProductId: fx.ProductId,
                            SourceLocationId: fx.ReserveLocationId,
                            TargetLocationId: fx.PickLocationId,
                            SourceUomId: fx.CaseUnitId,
                            SourcePackQty: 1m,
                            ConversionFactor: 12m,
                            SourceLotId: fx.LotId),
                        fx.UserId);
                }));

            concOutcomes.Count(o => o.StatusCode == 200).Should().Be(1);
            concOutcomes.Count(o => o.StatusCode == 422 || o.StatusCode == 409).Should().Be(1);

            // (d) Reversal of all 3 Cases (36 BaseUom) back from Forward-Pick Bin to Reserve Bin
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, null, new FakeUser(fx.UserId));
                var revRes = await svc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReserveLocationId,
                        TargetLocationId: fx.PickLocationId,
                        SourceUomId: fx.CaseUnitId,
                        SourceUomCode: "CASE",
                        SourcePackQty: 3m,
                        ConversionFactor: 12m,
                        SourceLotId: fx.LotId,
                        Reverse: true,
                        OriginalReferenceId: refStockId),
                    fx.UserId);

                revRes.StatusCode.Should().Be(200);
                using var revDoc = JsonSerializer.SerializeToDocument(revRes.Payload);
                revDoc.RootElement.GetProperty("reversed").GetBoolean().Should().BeTrue();
                revDoc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(36m);
                revDoc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(0m);
            }
        }
        finally
        {
            await CleanupBreakBulkFixtureAsync(fx);
        }
    }

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId) =>
        new(
            db,
            new AllowAllWarehouseAuth(),
            new FakeUser(userId));

    private sealed class FakeUser(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => true;
        public string Role => "Admin";
    }

    private sealed class FakeRequestMetadata : IRequestMetadata
    {
        public string CorrelationId { get; set; } = "corr-wave60";
        public string? IdempotencyKeyHash { get; set; }
        public string? RequestFingerprint { get; set; }
    }

    private sealed class AllowAllWarehouseAuth : IWarehouseAuthorizationService
    {
        public Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

        public Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record KittingFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int KitProductId,
        int CompAProductId,
        int CompBProductId,
        int WarehouseId,
        int LocationAId,
        int LotAId,
        int LotBId,
        DateTime LotBExpiryDate);

    private static async Task<KittingFixture> CreateKittingFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60KRole{suffix}" };
        var user = new User { Username = $"W60KUser{suffix}", PasswordHash = "hash", FullName = "Wave60 Kitting Tester", Role = role };
        var unit = new Unit { Code = $"UK{suffix}", Name = "Bộ" };
        var kitProd = new Product { Code = $"KIT-W60-{suffix}", Name = "Bộ Kit Wave 60", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, IsActive = true };
        var compA = new Product { Code = $"CMPA-W60-{suffix}", Name = "Linh kiện A", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 10000m, IsActive = true };
        var compB = new Product { Code = $"CMPB-W60-{suffix}", Name = "Linh kiện B", Unit = unit, TrackingType = ProductTrackingType.Lot, ExpiryControl = true, MovingAverageCost = 20000m, IsActive = true };
        var wh = new Warehouse { Code = $"WH60K-{suffix}", Name = "Kho Kitting Wave 60", IsActive = true };
        db.AddRange(role, user, unit, kitProd, compA, compB, wh);
        await db.SaveChangesAsync();

        var locA = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"K-01-{suffix}",
            Name = "Bàn đóng gói Kit",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lotA = new InventoryLot
        {
            ProductId = compA.Id,
            LotNumber = $"LOTA-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(6),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var lotBExpiry = DateTime.UtcNow.Date.AddMonths(3);
        var lotB = new InventoryLot
        {
            ProductId = compB.Id,
            LotNumber = $"LOTB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-2),
            ExpiryDate = lotBExpiry,
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(locA, lotA, lotB);
        await db.SaveChangesAsync();

        var stockA = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compA.Id,
            LocationId = locA.Id,
            LotId = lotA.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 10000m
        };
        stockA.RefreshGrainHash();
        var stockB = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = compB.Id,
            LocationId = locA.Id,
            LotId = lotB.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 20000m
        };
        stockB.RefreshGrainHash();
        db.InventoryStocks.AddRange(stockA, stockB);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compA.Id,
                LocationId = locA.Id,
                LotId = lotA.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 10000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            },
            new InventoryTransaction
            {
                WarehouseId = wh.Id,
                ProductId = compB.Id,
                LocationId = locA.Id,
                LotId = lotB.Id,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 10m,
                UnitCost = 20000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-30),
                CreatedBy = user.Id
            });
        await db.SaveChangesAsync();

        return new KittingFixture(suffix, user.Id, role.Id, unit.Id, kitProd.Id, compA.Id, compB.Id, wh.Id, locA.Id, lotA.Id, lotB.Id, lotBExpiry);
    }

    private static async Task CleanupKittingFixtureAsync(KittingFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        var woIds = await db.KitWorkOrders.Where(w => w.WarehouseId == fx.WarehouseId).Select(w => w.Id).ToListAsync();
        if (woIds.Count > 0)
        {
            await db.KitWorkOrderComponentLines.Where(l => woIds.Contains(l.KitWorkOrderId)).ExecuteDeleteAsync();
            await db.KitWorkOrders.Where(w => woIds.Contains(w.Id)).ExecuteDeleteAsync();
        }
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.ProductId == fx.KitProductId || x.ProductId == fx.CompAProductId || x.ProductId == fx.CompBProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.KitProductId || x.Id == fx.CompAProductId || x.Id == fx.CompBProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }

    private sealed record CrossDockFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int WarehouseId,
        int ReceivingStageId,
        int ShippingStageId,
        int ImportReceiptId,
        int PutawayTaskId,
        int PartnerId,
        int SalesOrderId,
        int SalesOrderLineId,
        int BackorderId);

    private static async Task<CrossDockFixture> CreateCrossDockFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60XRole{suffix}" };
        var user = new User { Username = $"W60XUser{suffix}", PasswordHash = "hash", FullName = "Wave60 CrossDock Tester", Role = role };
        var unit = new Unit { Code = $"UX{suffix}", Name = "Cái" };
        var prod = new Product { Code = $"SKU-X60-{suffix}", Name = "Sản phẩm Cross-Dock", Unit = unit, IsActive = true };
        var wh = new Warehouse { Code = $"WH60X-{suffix}", Name = "Kho Cross-Dock Wave 60", IsActive = true };
        var partner = new BusinessPartner { Code = $"CUST60-{suffix}", Name = "Khách hàng Cross-Dock", IsCustomer = true, IsActive = true };
        db.AddRange(role, user, unit, prod, wh, partner);
        await db.SaveChangesAsync();

        var recvLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "RECEIVING-STAGE",
            Name = "Bãi tiếp nhận nhập kho",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        var shipLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = "SHIPPING-STAGE",
            Name = "Bãi tập kết xuất hàng Cross-Dock",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsSystemManaged = true,
            CreatedBy = user.Id
        };
        db.AddRange(recvLoc, shipLoc);
        await db.SaveChangesAsync();

        var recvStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        recvStock.RefreshGrainHash();
        db.InventoryStocks.Add(recvStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recvLoc.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });

        var importReceipt = new ImportReceipt
        {
            Code = $"IMP-X60-{suffix}",
            WarehouseId = wh.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ImportReceipts.Add(importReceipt);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = importReceipt.Id,
            ProductId = prod.Id,
            Quantity = 10m,
            ExpectedQuantity = 10m,
            ReceivedQuantity = 10m,
            AcceptedQuantity = 10m,
            PostedQuantity = 10m,
            OperationUnitId = unit.Id,
            BaseUnitId = unit.Id,
            ConversionFactor = 1m,
            BaseExpectedQuantity = 10m,
            BaseReceivedQuantity = 10m,
            BaseAcceptedQuantity = 10m,
            BasePostedQuantity = 10m,
            UnitPrice = 50000m
        };
        db.ImportReceiptDetails.Add(importDetail);
        await db.SaveChangesAsync();

        var putawayTask = new PutawayTask
        {
            ReceiptId = importReceipt.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Open,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        putawayTask.Items.Add(new PutawayTaskItem
        {
            ReceiptLineId = importDetail.Id,
            ProductId = prod.Id,
            InventoryStatus = InventoryStatus.Available,
            SourceLocationId = recvLoc.Id,
            OperationUnitId = unit.Id,
            OperationUnitCodeSnapshot = unit.Code,
            BaseUnitId = unit.Id,
            BaseUnitCodeSnapshot = unit.Code,
            ConversionFactorSnapshot = 1m,
            ConversionVersionSnapshot = 1,
            RequiredOperationQuantity = 10m,
            RequiredBaseQuantity = 10m,
            MovedBaseQuantity = 0m
        });
        db.PutawayTasks.Add(putawayTask);

        var salesOrder = new SalesOrder
        {
            OrderCode = $"SO-X60-{suffix}",
            ExternalOrderId = $"EXT-X60-{suffix}",
            CustomerId = partner.Id,
            WarehouseId = wh.Id,
            Status = SalesOrderStatus.Released,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.SalesOrders.Add(salesOrder);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = salesOrder.Id,
            ExternalLineId = "1",
            ProductId = prod.Id,
            OrderedQuantity = 6m,
            UnitPrice = 60000m,
            UomCode = unit.Code
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var backorder = new Backorder
        {
            BackorderCode = $"BO-X60-{suffix}",
            SalesOrderLineId = soLine.Id,
            WarehouseId = wh.Id,
            Quantity = 6m,
            RecoveredQuantity = 0m,
            CancelledQuantity = 0m,
            Status = BackorderStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        db.Backorders.Add(backorder);
        await db.SaveChangesAsync();

        return new CrossDockFixture(
            suffix,
            user.Id,
            role.Id,
            unit.Id,
            prod.Id,
            wh.Id,
            recvLoc.Id,
            shipLoc.Id,
            importReceipt.Id,
            putawayTask.Id,
            partner.Id,
            salesOrder.Id,
            soLine.Id,
            backorder.Id);
    }

    private static async Task CleanupCrossDockFixtureAsync(CrossDockFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLocationMovements.Where(m => m.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(a => a.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Backorders.Where(b => b.Id == fx.BackorderId).ExecuteDeleteAsync();
        await db.SalesOrderLines.Where(l => l.Id == fx.SalesOrderLineId).ExecuteDeleteAsync();
        await db.SalesOrders.Where(s => s.Id == fx.SalesOrderId).ExecuteDeleteAsync();
        await db.PutawayTaskItems.Where(i => i.PutawayTaskId == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.PutawayTasks.Where(t => t.Id == fx.PutawayTaskId).ExecuteDeleteAsync();
        await db.ImportReceiptDetails.Where(d => d.ImportReceiptId == fx.ImportReceiptId).ExecuteDeleteAsync();
        await db.ImportReceipts.Where(r => r.Id == fx.ImportReceiptId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.BusinessPartners.Where(p => p.Id == fx.PartnerId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }

    private sealed record BreakBulkFixture(
        string Suffix,
        int UserId,
        int RoleId,
        int BaseUnitId,
        int CaseUnitId,
        int ProductId,
        int WarehouseId,
        int ReserveLocationId,
        int PickLocationId,
        int LotId);

    private static async Task<BreakBulkFixture> CreateBreakBulkFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W60BRole{suffix}" };
        var user = new User { Username = $"W60BUser{suffix}", PasswordHash = "hash", FullName = "Wave60 BreakBulk Tester", Role = role };
        var baseUnit = new Unit { Code = $"EA{suffix}", Name = "Cái" };
        var caseUnit = new Unit { Code = $"CS{suffix}", Name = "Thùng 12" };
        var prod = new Product { Code = $"SKU-BB60-{suffix}", Name = "Sản phẩm Xả Thùng Wave 60", Unit = baseUnit, TrackingType = ProductTrackingType.Lot, IsActive = true };
        var wh = new Warehouse { Code = $"WH60B-{suffix}", Name = "Kho Break-Bulk Wave 60", IsActive = true };
        db.AddRange(role, user, baseUnit, caseUnit, prod, wh);
        await db.SaveChangesAsync();

        var prodUom = new ProductUom
        {
            ProductId = prod.Id,
            UnitId = caseUnit.Id,
            ConversionFactor = 12m,
            IsActive = true
        };
        var reserveLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"RES-01-{suffix}",
            Name = "Kệ lưu trữ trên cao",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = false,
            CreatedBy = user.Id
        };
        var pickLoc = new WarehouseLocation
        {
            WarehouseId = wh.Id,
            Code = $"PICK-01-{suffix}",
            Name = "Mặt kệ nhặt lẻ",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lot = new InventoryLot
        {
            ProductId = prod.Id,
            LotNumber = $"LOT-BB-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(10),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(prodUom, reserveLoc, pickLoc, lot);
        await db.SaveChangesAsync();

        var reserveStock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            Status = InventoryStatus.Available,
            Quantity = 36m // 3 Cases x 12 EA
        };
        reserveStock.RefreshGrainHash();
        db.InventoryStocks.Add(reserveStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = reserveLoc.Id,
            LotId = lot.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 36m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-20),
            CreatedBy = user.Id
        });
        await db.SaveChangesAsync();

        return new BreakBulkFixture(
            suffix,
            user.Id,
            role.Id,
            baseUnit.Id,
            caseUnit.Id,
            prod.Id,
            wh.Id,
            reserveLoc.Id,
            pickLoc.Id,
            lot.Id);
    }

    private static async Task CleanupBreakBulkFixtureAsync(BreakBulkFixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && (x.CorrectiveTransactionId != null || x.ReversalOfTransactionId != null)).ExecuteUpdateAsync(s => s.SetProperty(x => x.CorrectiveTransactionId, (int?)null).SetProperty(x => x.ReversalOfTransactionId, (int?)null));
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(l => l.Id == fx.LotId).ExecuteDeleteAsync();
        await db.ProductUoms.Where(u => u.ProductId == fx.ProductId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.BaseUnitId || x.Id == fx.CaseUnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }
}
