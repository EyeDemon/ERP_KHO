using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave61RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Wave61_F013_CrossDockAndBreakBulkReversal_InsufficientStock_DuplicateReversal_And_LedgerRule_Guards()
    {
        // Verify InventoryLedgerCalculationRules directly
        InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(TransactionType.Reversal, null).Should().BeTrue();
        InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(TransactionType.Reversal, 999).Should().BeFalse();
        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(TransactionType.Reversal, -6m, null).Should().Be(-6m);
        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(TransactionType.Reversal, 6m, 999).Should().Be(0m);

        var fx = await CreateF013FixtureAsync();
        try
        {
            await using var db = CreateContext();
            var putawaySvc = new PutawayService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

            // 1. Execute forward Cross-Dock of 6 units from RECEIVING-STAGE -> SHIPPING-STAGE
            var cdRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                fx.PutawayTaskId,
                new ExecuteCrossDockDto
                {
                    TargetBackorderId = fx.BackorderId,
                    TargetSalesOrderId = fx.SalesOrderId
                });
            cdRes.IsCrossDock.Should().BeTrue();
            cdRes.CrossDockAllocatedQty.Should().Be(6m);

            // 2. Simulate partial consumption at SHIPPING-STAGE (reduce quantity from 6 to 2 so reversal of 6 would go negative)
            var shipStock = await db.InventoryStocks.SingleAsync(
                s => s.WarehouseId == fx.WarehouseId && s.LocationId == fx.ShippingStageId);
            shipStock.Quantity = 2m;
            shipStock.ReservedQuantity = 2m;
            shipStock.RefreshGrainHash();
            await db.SaveChangesAsync();

            // 3. Verify F-013 guard: Cross-Dock reversal throws CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK instead of clamping with Math.Max(0, ...)
            var insuffEx = await Assert.ThrowsAsync<ConflictException>(() =>
                putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true }));
            insuffEx.ErrorCode.Should().Be("CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK");

            // Restore SHIPPING-STAGE stock back to 6 units and verify reversal succeeds once, then blocks 2nd reversal
            shipStock.Quantity = 6m;
            shipStock.ReservedQuantity = 6m;
            shipStock.RefreshGrainHash();
            await db.SaveChangesAsync();

            var revRes = await putawaySvc.EvaluateAndExecuteCrossDockAsync(
                fx.PutawayTaskId,
                new ExecuteCrossDockDto { Reverse = true });
            revRes.IsCrossDock.Should().BeFalse();

            var dupCdRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                putawaySvc.EvaluateAndExecuteCrossDockAsync(
                    fx.PutawayTaskId,
                    new ExecuteCrossDockDto { Reverse = true }));
            dupCdRevEx.ErrorCode.Should().Be("CROSS_DOCK_ALREADY_REVERSED");

            // 4. Verify F-013 Break-Bulk reversal guard when no unreversed original BreakBulkReplenishment exists
            var controlSvc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
            var noOrigBbEx = await Assert.ThrowsAsync<ConflictException>(() =>
                controlSvc.ExecuteBreakBulkReplenishmentAsync(
                    new ExecuteBreakBulkReplenishmentInput(
                        WarehouseId: fx.WarehouseId,
                        ProductId: fx.ProductId,
                        SourceLocationId: fx.ReceivingStageId,
                        TargetLocationId: fx.ShippingStageId,
                        SourceUomId: fx.UnitId,
                        SourcePackQty: 1m,
                        ConversionFactor: 10m,
                        Reverse: true,
                        OriginalReferenceId: 999999),
                    fx.UserId));
            noOrigBbEx.ErrorCode.Should().Be("BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED");
        }
        finally
        {
            await CleanupF013FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_1_DynamicAbcVelocitySlottingAndTaskInterleaving_HappyPath_Idempotency_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic901FixtureAsync();
        try
        {
            var evalId = $"ABC-W61-{fx.Suffix}";

            // (a) Happy Path: Evaluate ABC Velocity Slotting & Dual-Command Task Interleaving
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var res = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(
                        WarehouseId: fx.WarehouseId,
                        LookbackDays: 90,
                        PersistAuditAndOutbox: true,
                        OriginalEvaluationId: evalId),
                    fx.UserId);

                res.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(res.Payload);
                doc.RootElement.GetProperty("evaluationId").GetString().Should().Be(evalId);
                doc.RootElement.GetProperty("evaluatedSkuCount").GetInt32().Should().Be(3);
                doc.RootElement.GetProperty("classACount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("reslottingRecommendationCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("executedReslottingBaseQuantity").GetDecimal().Should().Be(10m);
                doc.RootElement.GetProperty("interleavedTaskPairCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
                doc.RootElement.GetProperty("averageDeadheadSavedPercent").GetDecimal().Should().Be(38.50m);

                var reserveProdA = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.ReserveBinId);
                var goldenProdA = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.GoldenBinId);
                reserveProdA.Quantity.Should().Be(40m);
                goldenProdA.Quantity.Should().Be(10m);

                var locMoveCount = await db.InventoryLocationMovements.AsNoTracking()
                    .CountAsync(m => m.WarehouseId == fx.WarehouseId && m.ReferenceType == "AbcSlottingReSlotting");
                locMoveCount.Should().Be(1);
            }

            // (b) Duplicate Evaluation Guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                        new EvaluateAbcSlottingInput(
                            WarehouseId: fx.WarehouseId,
                            LookbackDays: 90,
                            PersistAuditAndOutbox: true,
                            OriginalEvaluationId: evalId),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("ABC_SLOTTING_EVALUATION_ALREADY_COMPLETED");
            }

            // (c) Parallel Concurrency contention on a 2nd EvaluationId (strictly 1 x 200 and 1 x 409)
            var concEvalId = $"ABC-CONC-{fx.Suffix}";
            var concResults = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new InventoryControlOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.EvaluateAbcSlottingAndTaskInterleavingAsync(
                            new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, false, concEvalId),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new InventoryControlOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.EvaluateAbcSlottingAndTaskInterleavingAsync(
                            new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, false, concEvalId),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concResults.Count(c => c == 200).Should().Be(1);
            concResults.Count(c => c == 409).Should().Be(1);

            // (d) Reversal of Evaluations & duplicate reversal guard + 9D balance conservation
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var rev1 = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: evalId),
                    fx.UserId);
                rev1.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                        new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: evalId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("ABC_SLOTTING_EVALUATION_ALREADY_REVERSED");

                var rev2 = await svc.EvaluateAbcSlottingAndTaskInterleavingAsync(
                    new EvaluateAbcSlottingInput(fx.WarehouseId, 90, null, true, Reverse: true, OriginalEvaluationId: concEvalId),
                    fx.UserId);
                rev2.StatusCode.Should().Be(200);

                var reserveProdAAfter = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.ReserveBinId);
                var goldenProdAAfter = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProdAId && s.LocationId == fx.GoldenBinId);
                reserveProdAAfter.Quantity.Should().Be(50m);
                goldenProdAAfter.Quantity.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupEpic901FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_2_SelfService3PlDepositorPortal_OwnerIsolation_BillingPreview_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic902FixtureAsync();
        try
        {
            int ownerAStatementId;
            int ownerBStatementId;

            // (a) Happy Path + Cross-Owner Isolation Guard (Owner A cannot access Owner B's inventory/billing)
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

                // Cross-Owner access attempt must throw ForbiddenException
                var crossEx = await Assert.ThrowsAsync<ForbiddenException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(
                            AuthenticatedOwnerId: fx.OwnerAId,
                            RequestedOwnerId: fx.OwnerBId,
                            WarehouseId: fx.WarehouseId),
                        fx.UserId));
                crossEx.Message.Should().Contain("THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN");

                // Scoped query & statement persistence for Owner A
                var resA = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(
                        OwnerId: fx.OwnerAId,
                        AuthenticatedOwnerId: fx.OwnerAId,
                        RequestedOwnerId: fx.OwnerAId,
                        WarehouseId: fx.WarehouseId,
                        BillingPeriod: "2026-10",
                        PersistSnapshotAndStatement: true),
                    fx.UserId);

                resA.StatusCode.Should().Be(200);
                using var docA = JsonSerializer.SerializeToDocument(resA.Payload);
                docA.RootElement.GetProperty("ownerId").GetInt32().Should().Be(fx.OwnerAId);
                docA.RootElement.GetProperty("crossOwnerLeakCount").GetInt32().Should().Be(0);
                docA.RootElement.GetProperty("tenantAndOwnerIsolationVerified").GetBoolean().Should().BeTrue();
                docA.RootElement.GetProperty("totalOnHandQty").GetDecimal().Should().Be(40m);
                docA.RootElement.GetProperty("inboundHandledQty").GetDecimal().Should().Be(40m);
                docA.RootElement.GetProperty("storageFeeAmount").GetDecimal().Should().Be(600000m); // 40 * 15,000
                docA.RootElement.GetProperty("inboundHandlingFeeAmount").GetDecimal().Should().Be(320000m); // 40 * 8,000
                docA.RootElement.GetProperty("totalBillingAmount").GetDecimal().Should().Be(1012000m); // (600,000 + 320,000) * 1.1
                ownerAStatementId = docA.RootElement.GetProperty("statementId").GetInt32();
            }

            // (b) Duplicate 3PL Statement Issuance Guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(
                            OwnerId: fx.OwnerAId,
                            WarehouseId: fx.WarehouseId,
                            BillingPeriod: "2026-10",
                            PersistSnapshotAndStatement: true),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("THREEPL_STATEMENT_ALREADY_ISSUED");
            }

            // (c) Parallel Concurrency contention: 2 concurrent statement generations for Owner B
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new InventoryControlOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.GetThreePlDepositorPortalAsync(
                            new ThreePlDepositorPortalInput(
                                OwnerId: fx.OwnerBId,
                                WarehouseId: fx.WarehouseId,
                                BillingPeriod: "2026-10",
                                PersistSnapshotAndStatement: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new InventoryControlOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.GetThreePlDepositorPortalAsync(
                            new ThreePlDepositorPortalInput(
                                OwnerId: fx.OwnerBId,
                                WarehouseId: fx.WarehouseId,
                                BillingPeriod: "2026-10",
                                PersistSnapshotAndStatement: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concOutcomes.Count(c => c == 200).Should().Be(1);
            concOutcomes.Count(c => c == 409).Should().Be(1);

            await using (var db = CreateContext())
            {
                var stmtB = await db.ThreePlBillingStatements.AsNoTracking()
                    .SingleAsync(s => s.PartnerId == fx.OwnerBId);
                ownerBStatementId = stmtB.Id;
            }

            // (d) Reversal of both 3PL Billing Statements & duplicate reversal guard
            await using (var db = CreateContext())
            {
                var svc = new InventoryControlOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var revA = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerAStatementId),
                    fx.UserId);
                revA.StatusCode.Should().Be(200);

                var revB = await svc.GetThreePlDepositorPortalAsync(
                    new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerBStatementId),
                    fx.UserId);
                revB.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.GetThreePlDepositorPortalAsync(
                        new ThreePlDepositorPortalInput(Reverse: true, OriginalStatementId: ownerAStatementId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("THREEPL_STATEMENT_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupEpic902FixtureAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic90_3_VietnamAccountingCircular200AndEinvoiceNd70Outbox_HappyPath_CustomsGuard_Form04SS_Concurrency_And_Reversal()
    {
        var fx = await CreateEpic903FixtureAsync();
        try
        {
            string syncedEventId;

            // (a) Decree 70/2025/NĐ-CP Customs Clearance Guard + Happy Path Circular 200 Journal & S-Invoice Sync
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));

                // 0. Non-existent ExportReceiptId must throw KeyNotFoundException (no fake SKU-DEFAULT fallback)
                var notFoundEx = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: 99999999,
                            WarehouseId: fx.WarehouseId,
                            PersistOutboxAndAudit: true),
                        fx.UserId));
                notFoundEx.Message.Should().Contain("EXPORT_RECEIPT_NOT_FOUND");

                // 1. Export goods awaiting customs clearance must be blocked with EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE
                var customsEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            IsExportDeclaration: true,
                            CustomsCleared: false),
                        fx.UserId));
                customsEx.Message.Should().Contain("EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE");

                // 2. Verify cost masking when canReadCost == false
                var maskedPreview = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        PersistOutboxAndAudit: false),
                    fx.UserId,
                    canReadCost: false);
                using (var maskedDoc = JsonSerializer.SerializeToDocument(maskedPreview.Payload))
                {
                    maskedDoc.RootElement.GetProperty("canReadCost").GetBoolean().Should().BeFalse();
                    maskedDoc.RootElement.GetProperty("totalCogsAmount").ValueKind.Should().Be(JsonValueKind.Null);
                    maskedDoc.RootElement.GetProperty("totalRevenueAmount").GetDecimal().Should().Be(750000m); // 10 * 75,000
                }

                // 3. Full sync with canReadCost == true
                var syncRes = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        AccountingProvider: "MISA_AMIS",
                        EInvoiceProvider: "VIETTEL_SINVOICE",
                        IsExportDeclaration: true,
                        CustomsCleared: true,
                        PersistOutboxAndAudit: true),
                    fx.UserId,
                    canReadCost: true);

                syncRes.StatusCode.Should().Be(200);
                using var doc = JsonSerializer.SerializeToDocument(syncRes.Payload);
                syncedEventId = doc.RootElement.GetProperty("outboxEventId").GetString()!;
                doc.RootElement.GetProperty("totalCogsAmount").GetDecimal().Should().Be(500000m); // 10 * 50,000 MAC
                doc.RootElement.GetProperty("totalRevenueAmount").GetDecimal().Should().Be(750000m); // 10 * 75,000
                doc.RootElement.GetProperty("totalVatAmount").GetDecimal().Should().Be(75000m); // 10% VAT
                doc.RootElement.GetProperty("totalReceivableAmount").GetDecimal().Should().Be(825000m);
                doc.RootElement.GetProperty("journalImbalanceDelta").GetDecimal().Should().Be(0.0000m);
                doc.RootElement.GetProperty("doubleEntryBalanced").GetBoolean().Should().BeTrue();
            }

            // (b) Duplicate Sync Guard + Form 04/SS-HĐĐT Adjustment Allowed
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var dupEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            PersistOutboxAndAudit: true),
                        fx.UserId));
                dupEx.ErrorCode.Should().Be("ACCOUNTING_EINVOICE_ALREADY_SYNCED");

                // Form 04/SS-HĐĐT adjustment is permitted on an already-synced receipt
                var form04Res = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        IssueAdjustmentForm04SS: true,
                        AdjustmentReason: "Điều chỉnh đơn giá theo biên bản đối soát Mẫu 04/SS-HĐĐT",
                        PersistOutboxAndAudit: true),
                    fx.UserId);
                form04Res.StatusCode.Should().Be(200);
                using var doc04 = JsonSerializer.SerializeToDocument(form04Res.Payload);
                doc04.RootElement.GetProperty("isForm04Ss").GetBoolean().Should().BeTrue();
            }

            // (c) Parallel Concurrency contention on ExportReceipt2Id
            var concOutcomes = await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db1 = CreateContext();
                        var svc1 = new ExportReceiptOperationsService(db1, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc1.SyncAccountingAndEInvoiceOutboxAsync(
                            new SyncAccountingAndEInvoiceInput(ExportReceiptId: fx.ExportReceipt2Id, WarehouseId: fx.WarehouseId, PersistOutboxAndAudit: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        await using var db2 = CreateContext();
                        var svc2 = new ExportReceiptOperationsService(db2, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                        var r = await svc2.SyncAccountingAndEInvoiceOutboxAsync(
                            new SyncAccountingAndEInvoiceInput(ExportReceiptId: fx.ExportReceipt2Id, WarehouseId: fx.WarehouseId, PersistOutboxAndAudit: true),
                            fx.UserId);
                        return r.StatusCode;
                    }
                    catch (ConflictException) { return 409; }
                    catch (DbUpdateException) { return 409; }
                }));

            concOutcomes.Count(c => c == 200).Should().Be(1);
            concOutcomes.Count(c => c == 409).Should().Be(1);

            // (d) Reversal via Form 04/SS-HĐĐT cancellation & duplicate reversal guard
            await using (var db = CreateContext())
            {
                var svc = new ExportReceiptOperationsService(db, new AllowAllWarehouseAuth(), new FakeUser(fx.UserId));
                var rev1 = await svc.SyncAccountingAndEInvoiceOutboxAsync(
                    new SyncAccountingAndEInvoiceInput(
                        ExportReceiptId: fx.ExportReceipt1Id,
                        WarehouseId: fx.WarehouseId,
                        Reverse: true,
                        OriginalOutboxEventId: syncedEventId),
                    fx.UserId);
                rev1.StatusCode.Should().Be(200);

                var dupRevEx = await Assert.ThrowsAsync<ConflictException>(() =>
                    svc.SyncAccountingAndEInvoiceOutboxAsync(
                        new SyncAccountingAndEInvoiceInput(
                            ExportReceiptId: fx.ExportReceipt1Id,
                            WarehouseId: fx.WarehouseId,
                            Reverse: true,
                            OriginalOutboxEventId: syncedEventId),
                        fx.UserId));
                dupRevEx.ErrorCode.Should().Be("ACCOUNTING_EINVOICE_ALREADY_REVERSED");
            }
        }
        finally
        {
            await CleanupEpic903FixtureAsync(fx);
        }
    }

    private sealed class FakeUser(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => true;
        public string Role => "Admin";
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

    private sealed record F013Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int PartnerId,
        int WarehouseId,
        int ReceivingStageId,
        int ShippingStageId,
        int ImportReceiptId,
        int PutawayTaskId,
        int SalesOrderId,
        int BackorderId);

    private static async Task<F013Fixture> CreateF013FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61F13R{suffix}" };
        var user = new User { Username = $"W61F13U{suffix}", PasswordHash = "hash", FullName = "W61 F013 Tester", Role = role };
        var unit = new Unit { Code = $"UF{suffix}", Name = "Cái" };
        var prod = new Product { Code = $"SKU-F13-{suffix}", Name = "SP Test F-013", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PF13-{suffix}", Name = "KH F-013", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHF13-{suffix}", Name = "Kho Test F-013", IsActive = true };
        db.AddRange(role, user, unit, prod, partner, wh);
        await db.SaveChangesAsync();

        var recv = new WarehouseLocation { WarehouseId = wh.Id, Code = "RECEIVING-STAGE", Name = "Bãi Nhận", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = false, IsReceivable = true, IsSystemManaged = true, CreatedBy = user.Id };
        var ship = new WarehouseLocation { WarehouseId = wh.Id, Code = "SHIPPING-STAGE", Name = "Bãi Xuất", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = user.Id };
        db.AddRange(recv, ship);
        await db.SaveChangesAsync();

        var stock = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = recv.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m
        };
        stock.RefreshGrainHash();
        db.InventoryStocks.Add(stock);

        var ir = new ImportReceipt
        {
            Code = $"IRF13-{suffix}",
            WarehouseId = wh.Id,
            SupplierId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ImportReceipts.Add(ir);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = ir.Id,
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

        var pt = new PutawayTask
        {
            ReceiptId = ir.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Assigned,
            AssignedUserId = user.Id,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Items = new List<PutawayTaskItem>
            {
                new()
                {
                    ReceiptLineId = importDetail.Id,
                    ProductId = prod.Id,
                    SourceLocationId = recv.Id,
                    InventoryStatus = InventoryStatus.Available,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m,
                    MovedBaseQuantity = 0m
                }
            }
        };
        db.PutawayTasks.Add(pt);

        var so = new SalesOrder
        {
            OrderCode = $"SOF13-{suffix}",
            ExternalOrderId = $"EXTF13-{suffix}",
            CustomerId = partner.Id,
            WarehouseId = wh.Id,
            Status = SalesOrderStatus.Released,
            Priority = 1,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            ExternalLineId = "1",
            ProductId = prod.Id,
            OrderedQuantity = 6m,
            UnitPrice = 50000m,
            UomCode = unit.Code
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var bo = new Backorder
        {
            BackorderCode = $"BOF13-{suffix}",
            SalesOrderLineId = soLine.Id,
            WarehouseId = wh.Id,
            Status = BackorderStatus.Open,
            Quantity = 6m,
            RecoveredQuantity = 0m,
            CancelledQuantity = 0m,
            CreatedAt = DateTime.UtcNow
        };
        db.Backorders.Add(bo);
        await db.SaveChangesAsync();

        return new F013Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, partner.Id, wh.Id, recv.Id, ship.Id, ir.Id, pt.Id, so.Id, bo.Id);
    }

    private static async Task CleanupF013FixtureAsync(F013Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = NULL, ReversalOfTransactionId = NULL WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryLocationMovements WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.StockAllocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.StockReservations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Backorders WHERE Id = {fx.BackorderId};
            DELETE FROM dbo.SalesOrderLines WHERE SalesOrderId = {fx.SalesOrderId};
            DELETE FROM dbo.SalesOrders WHERE Id = {fx.SalesOrderId};
            DELETE FROM dbo.PutawayTaskItems WHERE PutawayTaskId = {fx.PutawayTaskId};
            DELETE FROM dbo.PutawayTasks WHERE Id = {fx.PutawayTaskId};
            DELETE FROM dbo.ImportReceiptDetails WHERE ImportReceiptId = {fx.ImportReceiptId};
            DELETE FROM dbo.ImportReceipts WHERE Id = {fx.ImportReceiptId};
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic901Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProdAId,
        int ProdBId,
        int ProdCId,
        int PartnerId,
        int WarehouseId,
        int GoldenBinId,
        int ReserveBinId,
        int ImportReceiptId,
        int PutawayTaskId);

    private static async Task<Epic901Fixture> CreateEpic901FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E1R{suffix}" };
        var user = new User { Username = $"W61E1U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.1 Tester", Role = role };
        var unit = new Unit { Code = $"UE1{suffix}", Name = "Cái" };
        var prodA = new Product { Code = $"SKU-A-{suffix}", Name = "SP Nhanh A", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var prodB = new Product { Code = $"SKU-B-{suffix}", Name = "SP Vừa B", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var prodC = new Product { Code = $"SKU-C-{suffix}", Name = "SP Chậm C", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PE1-{suffix}", Name = "Đối tác E90.1", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE1-{suffix}", Name = "Kho ABC E90.1", IsActive = true };
        db.AddRange(role, user, unit, prodA, prodB, prodC, partner, wh);
        await db.SaveChangesAsync();

        var goldenBin = new WarehouseLocation { WarehouseId = wh.Id, Code = $"PICK-A01-{suffix}", Name = "Kệ Vàng Mặt Nhặt", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, CreatedBy = user.Id };
        var reserveBin = new WarehouseLocation { WarehouseId = wh.Id, Code = $"RES-C09-{suffix}", Name = "Kệ Dự trữ Cao", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = false, CreatedBy = user.Id };
        db.AddRange(goldenBin, reserveBin);
        await db.SaveChangesAsync();

        // Put Fast-Mover SKU A in non-pickable Reserve Bin so dynamic re-slotting recommends moving it to Golden Pick Bin
        var sA = new InventoryStock { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, Status = InventoryStatus.Available, Quantity = 50m };
        var sB = new InventoryStock { WarehouseId = wh.Id, ProductId = prodB.Id, LocationId = goldenBin.Id, Status = InventoryStatus.Available, Quantity = 15m };
        var sC = new InventoryStock { WarehouseId = wh.Id, ProductId = prodC.Id, LocationId = goldenBin.Id, Status = InventoryStatus.Available, Quantity = 5m };
        sA.RefreshGrainHash();
        sB.RefreshGrainHash();
        sC.RefreshGrainHash();
        db.InventoryStocks.AddRange(sA, sB, sC);

        db.InventoryTransactions.AddRange(
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Import, Quantity = 50m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Move, Quantity = -20m, ReferenceId = 1, ReferenceType = "Picking", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodA.Id, LocationId = reserveBin.Id, TransactionType = TransactionType.Move, Quantity = 20m, ReferenceId = 1, ReferenceType = "Picking", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodB.Id, LocationId = goldenBin.Id, TransactionType = TransactionType.Import, Quantity = 15m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prodC.Id, LocationId = goldenBin.Id, TransactionType = TransactionType.Import, Quantity = 5m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id });

        var ir = new ImportReceipt { Code = $"IRE1-{suffix}", WarehouseId = wh.Id, SupplierId = partner.Id, Status = ReceiptStatus.Approved, CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        db.ImportReceipts.Add(ir);
        await db.SaveChangesAsync();

        var importDetail = new ImportReceiptDetail
        {
            ImportReceiptId = ir.Id,
            ProductId = prodA.Id,
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

        var pt = new PutawayTask
        {
            ReceiptId = ir.Id,
            WarehouseId = wh.Id,
            Status = PutawayTaskStatus.Open,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Items = new List<PutawayTaskItem>
            {
                new()
                {
                    ReceiptLineId = importDetail.Id,
                    ProductId = prodA.Id,
                    SourceLocationId = reserveBin.Id,
                    InventoryStatus = InventoryStatus.Available,
                    OperationUnitId = unit.Id,
                    OperationUnitCodeSnapshot = unit.Code,
                    BaseUnitId = unit.Id,
                    BaseUnitCodeSnapshot = unit.Code,
                    ConversionFactorSnapshot = 1m,
                    RequiredOperationQuantity = 10m,
                    RequiredBaseQuantity = 10m
                }
            }
        };
        db.PutawayTasks.Add(pt);
        await db.SaveChangesAsync();

        return new Epic901Fixture(suffix, user.Id, role.Id, unit.Id, prodA.Id, prodB.Id, prodC.Id, partner.Id, wh.Id, goldenBin.Id, reserveBin.Id, ir.Id, pt.Id);
    }

    private static async Task CleanupEpic901FixtureAsync(Epic901Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = NULL, ReversalOfTransactionId = NULL WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryLocationMovements WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.PutawayTaskItems WHERE PutawayTaskId = {fx.PutawayTaskId};
            DELETE FROM dbo.PutawayTasks WHERE Id = {fx.PutawayTaskId};
            DELETE FROM dbo.ImportReceiptDetails WHERE ImportReceiptId = {fx.ImportReceiptId};
            DELETE FROM dbo.ImportReceipts WHERE Id = {fx.ImportReceiptId};
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.IntegrationOutboxMessages WHERE AggregateId = {fx.WarehouseId} AND EventType = 'AbcSlottingAndInterleavingEvaluated';
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id IN ({fx.ProdAId}, {fx.ProdBId}, {fx.ProdCId});
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic902Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int OwnerAId,
        int OwnerBId,
        int WarehouseId,
        int LocationId);

    private static async Task<Epic902Fixture> CreateEpic902FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E2R{suffix}" };
        var user = new User { Username = $"W61E2U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.2 Tester", Role = role };
        var unit = new Unit { Code = $"UE2{suffix}", Name = "Thùng" };
        var prod = new Product { Code = $"SKU-3PL-{suffix}", Name = "SP Ký gửi 3PL", Unit = unit, TrackingType = ProductTrackingType.None, IsActive = true };
        var ownerA = new BusinessPartner { Code = $"OWNA-{suffix}", Name = "Chủ hàng 3PL Alpha", IsCustomer = true, IsSupplier = true, IsActive = true };
        var ownerB = new BusinessPartner { Code = $"OWNB-{suffix}", Name = "Chủ hàng 3PL Beta", IsCustomer = true, IsSupplier = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE2-{suffix}", Name = "Kho 3PL E90.2", IsActive = true };
        db.AddRange(role, user, unit, prod, ownerA, ownerB, wh);
        await db.SaveChangesAsync();

        var loc = new WarehouseLocation { WarehouseId = wh.Id, Code = $"3PL-BIN-{suffix}", Name = "Kệ Ký gửi 3PL", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, CreatedBy = user.Id };
        db.WarehouseLocations.Add(loc);
        await db.SaveChangesAsync();

        var stockA = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = loc.Id,
            OwnerId = ownerA.Id,
            Status = InventoryStatus.Available,
            Quantity = 40m
        };
        var stockB = new InventoryStock
        {
            WarehouseId = wh.Id,
            ProductId = prod.Id,
            LocationId = loc.Id,
            OwnerId = ownerB.Id,
            Status = InventoryStatus.Available,
            Quantity = 25m
        };
        stockA.RefreshGrainHash();
        stockB.RefreshGrainHash();
        db.InventoryStocks.AddRange(stockA, stockB);

        db.InventoryTransactions.AddRange(
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prod.Id, LocationId = loc.Id, OwnerId = ownerA.Id, TransactionType = TransactionType.Import, Quantity = 40m, ReferenceId = 1, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id },
            new InventoryTransaction { WarehouseId = wh.Id, ProductId = prod.Id, LocationId = loc.Id, OwnerId = ownerB.Id, TransactionType = TransactionType.Import, Quantity = 25m, ReferenceId = 2, ReferenceType = "ImportReceipt", TransactionDate = DateTime.UtcNow, CreatedBy = user.Id });
        await db.SaveChangesAsync();

        return new Epic902Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, ownerA.Id, ownerB.Id, wh.Id, loc.Id);
    }

    private static async Task CleanupEpic902FixtureAsync(Epic902Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DISABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.InventoryTransactions WHERE WarehouseId = {fx.WarehouseId};
            ENABLE TRIGGER TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;
            DELETE FROM dbo.ThreePlBillingStatements WHERE PartnerId IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.ThreePlStorageSnapshots WHERE OwnerId IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.InventoryStocks WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.WarehouseLocations WHERE WarehouseId = {fx.WarehouseId};
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id IN ({fx.OwnerAId}, {fx.OwnerBId});
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }

    private sealed record Epic903Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int PartnerId,
        int WarehouseId,
        int ExportReceipt1Id,
        int ExportReceipt2Id);

    private static async Task<Epic903Fixture> CreateEpic903FixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W61E3R{suffix}" };
        var user = new User { Username = $"W61E3U{suffix}", PasswordHash = "hash", FullName = "W61 Epic90.3 Tester", Role = role };
        var unit = new Unit { Code = $"UE3{suffix}", Name = "Hộp" };
        var prod = new Product { Code = $"SKU-ACC-{suffix}", Name = "SP Kế toán TT200", Unit = unit, MovingAverageCost = 50000m, TrackingType = ProductTrackingType.None, IsActive = true };
        var partner = new BusinessPartner { Code = $"PE3-{suffix}", Name = "Công ty TNHH Đại Dương", IsCustomer = true, IsActive = true };
        var wh = new Warehouse { Code = $"WHE3-{suffix}", Name = "Kho TT200 E90.3", IsActive = true };
        db.AddRange(role, user, unit, prod, partner, wh);
        await db.SaveChangesAsync();

        var er1 = new ExportReceipt
        {
            Code = $"PXK-E3A-{suffix}",
            WarehouseId = wh.Id,
            CustomerId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Details = new List<ExportReceiptDetail>
            {
                new() { ProductId = prod.Id, Quantity = 10m, UnitPrice = 75000m }
            }
        };
        var er2 = new ExportReceipt
        {
            Code = $"PXK-E3B-{suffix}",
            WarehouseId = wh.Id,
            CustomerId = partner.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            CreatedAt = DateTime.UtcNow,
            Details = new List<ExportReceiptDetail>
            {
                new() { ProductId = prod.Id, Quantity = 4m, UnitPrice = 75000m }
            }
        };
        db.AddRange(er1, er2);
        await db.SaveChangesAsync();

        return new Epic903Fixture(suffix, user.Id, role.Id, unit.Id, prod.Id, partner.Id, wh.Id, er1.Id, er2.Id);
    }

    private static async Task CleanupEpic903FixtureAsync(Epic903Fixture fx)
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM dbo.IntegrationOutboxMessages WHERE AggregateType = 'ExportReceipt' AND AggregateId IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.AuditLogs WHERE WarehouseId = {fx.WarehouseId} OR UserId = {fx.UserId};
            DELETE FROM dbo.ExportReceiptDetails WHERE ExportReceiptId IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.ExportReceipts WHERE Id IN ({fx.ExportReceipt1Id}, {fx.ExportReceipt2Id});
            DELETE FROM dbo.Warehouses WHERE Id = {fx.WarehouseId};
            DELETE FROM dbo.Products WHERE Id = {fx.ProductId};
            DELETE FROM dbo.BusinessPartners WHERE Id = {fx.PartnerId};
            DELETE FROM dbo.Units WHERE Id = {fx.UnitId};
            DELETE FROM dbo.Users WHERE Id = {fx.UserId};
            DELETE FROM dbo.Roles WHERE Id = {fx.RoleId};
        ");
    }
}
