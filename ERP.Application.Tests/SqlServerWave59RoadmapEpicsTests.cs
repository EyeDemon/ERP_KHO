using System.Text.Json;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using ERP.Infrastructure.Services.Carriers;
using ERP.Infrastructure.Services.Marketplaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave59RoadmapEpicsTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task Epic30_1_WaveTemplateRelease_ClusterToteRoute_And_PutToWallAutoPackingSessionHandoff()
    {
        var fx = await CreateFixtureAsync();
        int task1Id = 0;
        int task2Id = 0;
        int waveId = 0;
        int? autoPackingSessionId = null;
        try
        {
            await using (var db = CreateContext())
            {
                var rsv = new StockReservation
                {
                    ReservationCode = $"RSV-W59-{fx.Suffix}",
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    Quantity = 5m,
                    SourceType = "SalesOrder",
                    SourceId = 1001,
                    Status = StockReservationStatus.Active,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(2)
                };
                db.StockReservations.Add(rsv);
                await db.SaveChangesAsync();

                var alloc1 = new StockAllocation
                {
                    AllocationCode = $"ALC-W59-1-{fx.Suffix}",
                    ReservationId = rsv.Id,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationBId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 3m,
                    Status = StockAllocationStatus.Active,
                    AllocatedBy = fx.UserId,
                    AllocatedAt = DateTime.UtcNow
                };
                var alloc2 = new StockAllocation
                {
                    AllocationCode = $"ALC-W59-2-{fx.Suffix}",
                    ReservationId = rsv.Id,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationAId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 2m,
                    Status = StockAllocationStatus.Active,
                    AllocatedBy = fx.UserId,
                    AllocatedAt = DateTime.UtcNow
                };
                db.StockAllocations.AddRange(alloc1, alloc2);
                await db.SaveChangesAsync();

                var task1 = new PickingTask
                {
                    WarehouseId = fx.WarehouseId,
                    TaskCode = $"PICK-W59-A-{fx.Suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 1001,
                    SourceCode = $"SO-W59-A-{fx.Suffix}",
                    PickingType = "STANDARD",
                    Priority = 5,
                    Status = PickingTaskStatus.Open,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                task1.Lines.Add(new PickingTaskLine
                {
                    Sequence = 1,
                    AllocationId = alloc1.Id,
                    ProductId = fx.ProductId,
                    SourceLocationId = fx.LocationBId, // LOC-B
                    RequestedQuantity = 3m,
                    PickedQuantity = 0m,
                    Status = PickingTaskLineStatus.Open
                });

                var task2 = new PickingTask
                {
                    WarehouseId = fx.WarehouseId,
                    TaskCode = $"PICK-W59-B-{fx.Suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 1002,
                    SourceCode = $"SO-W59-B-{fx.Suffix}",
                    PickingType = "STANDARD",
                    Priority = 5,
                    Status = PickingTaskStatus.Open,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                task2.Lines.Add(new PickingTaskLine
                {
                    Sequence = 1,
                    AllocationId = alloc2.Id,
                    ProductId = fx.ProductId,
                    SourceLocationId = fx.LocationAId, // LOC-A (sorts before LOC-B)
                    RequestedQuantity = 2m,
                    PickedQuantity = 0m,
                    Status = PickingTaskLineStatus.Open
                });

                db.PickingTasks.AddRange(task1, task2);
                await db.SaveChangesAsync();
                task1Id = task1.Id;
                task2Id = task2.Id;
            }

            await using (var db = CreateContext())
            {
                var pickingOps = new PickingTaskOperationsService(db);

                // 1. Upsert & list wave templates
                var upsertRes = await pickingOps.UpsertWaveTemplateAsync(
                    new UpsertPickingWaveTemplateInput(
                        WarehouseId: fx.WarehouseId,
                        TemplateCode: "TPL-FLASH-W59",
                        TemplateName: "Sóng Flash Sale Wave 59",
                        CarrierCutoffWindow: "14:00",
                        SlaTierFilter: "CRITICAL_UNDER_2H",
                        ZoneCode: "ZONE-A",
                        MaxOrdersPerClusterTote: 1,
                        PickingType: "CLUSTER_TOTE",
                        IsActive: true),
                    fx.UserId);
                upsertRes.StatusCode.Should().Be(200);

                var listTplRes = await pickingOps.GetWaveTemplatesAsync(fx.WarehouseId);
                listTplRes.StatusCode.Should().Be(200);

                // 2. Release wave using template (MaxOrdersPerClusterTote = 1 -> 2 cluster totes TOTE-W59-01, TOTE-W59-02)
                var waveCode = $"WAVE-W59-{fx.Suffix}";
                var releaseRes = await pickingOps.ReleaseWaveAsync(
                    new ReleasePickingWaveInput(
                        WarehouseId: fx.WarehouseId,
                        WaveCode: waveCode,
                        TemplateCode: "TPL-FLASH-W59",
                        ClusterTotePrefix: "TOTE-W59"),
                    fx.UserId);
                releaseRes.StatusCode.Should().Be(200);

                using var releaseDoc = JsonSerializer.SerializeToDocument(releaseRes.Payload);
                releaseDoc.RootElement.GetProperty("templateCode").GetString().Should().Be("TPL-FLASH-W59");
                releaseDoc.RootElement.GetProperty("carrierCutoffWindow").GetString().Should().Be("14:00");
                releaseDoc.RootElement.GetProperty("releasedTasksCount").GetInt32().Should().Be(2);
                releaseDoc.RootElement.GetProperty("clusterTotes").GetArrayLength().Should().Be(2);

                var route = releaseDoc.RootElement.GetProperty("optimizedPickRoute");
                route.GetArrayLength().Should().Be(2);
                route[0].GetProperty("pickSequence").GetInt32().Should().Be(1);
                route[0].GetProperty("locationId").GetInt32().Should().Be(fx.LocationAId);
                route[1].GetProperty("pickSequence").GetInt32().Should().Be(2);
                route[1].GetProperty("locationId").GetInt32().Should().Be(fx.LocationBId);

                var persistedWave = await db.PickingWaves.AsNoTracking().SingleAsync(w => w.WaveCode == waveCode);
                waveId = persistedWave.Id;
                persistedWave.TemplateCode.Should().Be("TPL-FLASH-W59");
                persistedWave.ClusterToteBarcodesCsv.Should().Contain("TOTE-W59-01").And.Contain("TOTE-W59-02");

                // 3. Put-to-Wall sort completion -> auto-creates PackingSession in same transaction
                var sortRes = await pickingOps.PutToWallSortAsync(
                    task1Id.ToString(),
                    new PutToWallSortInput(
                        ScannedSkuOrBarcode: fx.ProductCode,
                        ScannedSlotCode: "SLOT-01",
                        Quantity: 3m),
                    fx.UserId);
                sortRes.StatusCode.Should().Be(200);

                using var sortDoc = JsonSerializer.SerializeToDocument(sortRes.Payload);
                sortDoc.RootElement.GetProperty("slotStatus").GetString().Should().Be("COMPLETED");
                sortDoc.RootElement.GetProperty("autoCreatedPackingSession").GetBoolean().Should().BeTrue();
                sortDoc.RootElement.GetProperty("handoffStatus").GetString().Should().Be("READY_FOR_PACKING");
                autoPackingSessionId = sortDoc.RootElement.GetProperty("packingSessionId").GetInt32();
                var packingSessionCode = sortDoc.RootElement.GetProperty("packingSessionCode").GetString();
                autoPackingSessionId.Should().BeGreaterThan(0);
                packingSessionCode.Should().StartWith($"PACK-PTW-{task1Id}-");

                var slotInDb = await db.PutToWallSlots.AsNoTracking()
                    .SingleAsync(s => s.PickingTaskId == task1Id && s.SlotCode == "SLOT-01");
                slotInDb.SlotStatus.Should().Be("COMPLETED");
                slotInDb.PackingSessionId.Should().Be(autoPackingSessionId);
                slotInDb.PackingSessionCode.Should().Be(packingSessionCode);

                var packSessionInDb = await db.PackingSessions.AsNoTracking()
                    .SingleAsync(ps => ps.Id == autoPackingSessionId!.Value);
                packSessionInDb.PickingTaskId.Should().Be(task1Id);
                packSessionInDb.Status.Should().Be(PackingSessionStatus.Open);
            }
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            if (task1Id > 0 || task2Id > 0)
            {
                await cleanupDb.PutToWallSlots.Where(s => s.PickingTaskId == task1Id || s.PickingTaskId == task2Id).ExecuteDeleteAsync();
                if (autoPackingSessionId.HasValue)
                    await cleanupDb.PackingSessions.Where(ps => ps.Id == autoPackingSessionId.Value).ExecuteDeleteAsync();
                await cleanupDb.PickingTaskLines.Where(l => l.PickingTaskId == task1Id || l.PickingTaskId == task2Id).ExecuteDeleteAsync();
                await cleanupDb.PickingTasks.Where(t => t.Id == task1Id || t.Id == task2Id).ExecuteDeleteAsync();
            }
            await cleanupDb.StockAllocations.Where(a => a.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            await cleanupDb.StockReservations.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            if (waveId > 0)
            {
                await cleanupDb.PickingWaves.Where(w => w.Id == waveId).ExecuteDeleteAsync();
            }
            await cleanupDb.PickingWaveTemplates.Where(t => t.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            await CleanupAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic30_2_VietnamCarrierAdapters_BookWaybillOutbox_WebhookHmacIdempotency_And_EpodFinancialReconciliation()
    {
        var fx = await CreateFixtureAsync();
        int pickTaskId = 0;
        int packSessionId = 0;
        int shipmentId = 0;
        int exportReceiptId = 0;
        string bookedAwb = string.Empty;
        try
        {
            await using (var db = CreateContext())
            {
                var pickTask = new PickingTask
                {
                    WarehouseId = fx.WarehouseId,
                    TaskCode = $"PICK-SHP-{fx.Suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 2001,
                    SourceCode = $"SO-SHP-{fx.Suffix}",
                    Status = PickingTaskStatus.Completed,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                db.PickingTasks.Add(pickTask);
                await db.SaveChangesAsync();
                pickTaskId = pickTask.Id;

                var packSession = new PackingSession
                {
                    WarehouseId = fx.WarehouseId,
                    PickingTaskId = pickTask.Id,
                    SessionCode = $"PACK-SHP-{fx.Suffix}",
                    Status = PackingSessionStatus.Open,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                db.PackingSessions.Add(packSession);
                await db.SaveChangesAsync();
                packSessionId = packSession.Id;

                var shipment = new Shipment
                {
                    WarehouseId = fx.WarehouseId,
                    PackingSessionId = packSession.Id,
                    ShipmentCode = $"SHP-W59-{fx.Suffix}",
                    SourceType = "SalesOrder",
                    SourceId = 2001,
                    SourceCode = $"SO-SHP-{fx.Suffix}",
                    Status = ShipmentStatus.Ready,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                db.Shipments.Add(shipment);
                await db.SaveChangesAsync();
                shipmentId = shipment.Id;

                var receipt = new ExportReceipt
                {
                    WarehouseId = fx.WarehouseId,
                    Code = $"XK-W59-{fx.Suffix}",
                    Status = ReceiptStatus.Approved,
                    ShipmentId = shipment.Id,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                db.ExportReceipts.Add(receipt);
                await db.SaveChangesAsync();
                exportReceiptId = receipt.Id;
            }

            await using (var db = CreateContext())
            {
                var shipmentOps = new ShipmentOperationsService(db);

                // 1. Verify all 4 carrier configs seeded & book waybill across GHN
                var configsRes = await shipmentOps.GetCarrierConfigsAsync(fx.WarehouseId);
                configsRes.StatusCode.Should().Be(200);
                (await db.CarrierIntegrationConfigs.AsNoTracking().CountAsync(c => c.WarehouseId == fx.WarehouseId)).Should().Be(4);

                var bookRes = await shipmentOps.BookCarrierWaybillAsync(
                    new CarrierWaybillBookingRequest(
                        WarehouseId: fx.WarehouseId,
                        ShipmentId: shipmentId,
                        ExportReceiptId: exportReceiptId,
                        CarrierCode: "GHN",
                        RecipientName: "Nguyễn Văn A",
                        RecipientPhone: "0901234567",
                        RawAddress: "123 Lê Lợi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
                        WeightGrams: 1200m,
                        CodAmountVnd: 350000m),
                    fx.UserId);
                bookRes.StatusCode.Should().Be(200);

                using var bookDoc = JsonSerializer.SerializeToDocument(bookRes.Payload);
                bookedAwb = bookDoc.RootElement.GetProperty("awbTrackingCode").GetString()!;
                bookedAwb.Should().StartWith("GHN");
                var outboxMsgId = bookDoc.RootElement.GetProperty("outboxMessageId").GetInt64();
                outboxMsgId.Should().BeGreaterThan(0);

                var outboxMsg = await db.IntegrationOutboxMessages.AsNoTracking().SingleAsync(m => m.Id == outboxMsgId);
                outboxMsg.EventType.Should().Be("CARRIER_BOOK_WAYBILL");

                // 2. Reject invalid HMAC signature with 401
                var invalidWhkRes = await shipmentOps.IngestCarrierWebhookAsync(
                    "GHN",
                    new CarrierWebhookEventInput(EventId: $"EVT-BAD-{fx.Suffix}", TrackingNumber: bookedAwb, CarrierStatus: "IN_TRANSIT"),
                    rawBody: "{\"trackingNumber\":\"" + bookedAwb + "\"}",
                    signatureHeader: "invalid-signature-hex",
                    actorUserId: fx.UserId);
                invalidWhkRes.StatusCode.Should().Be(401);

                // 3. Valid HMAC-SHA256 webhook: DELIVERED + e-POD + COD financial reconciliation
                var deliveredInput = new CarrierWebhookEventInput(
                    EventId: $"EVT-DELIV-{fx.Suffix}",
                    TrackingNumber: bookedAwb,
                    ShipmentId: shipmentId,
                    ExportReceiptId: exportReceiptId,
                    CarrierStatus: "DELIVERED",
                    ActualCodCollected: 350000m,
                    ActualShippingFee: 22000m,
                    PodRecipientName: "Nguyễn Văn A (Ký nhận)",
                    PodPhotoUrl: "https://cdn.erp-kho.vn/epod/ghn-w59.jpg",
                    PodNote: "Giao thành công đúng hẹn");
                var rawDeliveredJson = JsonSerializer.Serialize(deliveredInput);
                var validSig = VietnamCarrierAdapterBase.ComputeHmacSha256Hex(rawDeliveredJson, ShipmentOperationsService.DefaultCarrierWebhookSecret);

                var delivRes = await shipmentOps.IngestCarrierWebhookAsync(
                    "GHN",
                    deliveredInput,
                    rawBody: rawDeliveredJson,
                    signatureHeader: validSig,
                    actorUserId: fx.UserId);
                delivRes.StatusCode.Should().Be(200);
                using var delivDoc = JsonSerializer.SerializeToDocument(delivRes.Payload);
                delivDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeFalse();
                delivDoc.RootElement.GetProperty("shipmentStatus").GetString().Should().Be("Delivered");
                delivDoc.RootElement.GetProperty("epodCreated").GetBoolean().Should().BeTrue();
                delivDoc.RootElement.GetProperty("reconciliationStatus").GetString().Should().Be("MATCHED");
                delivDoc.RootElement.GetProperty("codVariance").GetDecimal().Should().Be(0m);

                // 4. Duplicate webhook replay returns idempotentReplay = true without duplicating tracking events
                var replayRes = await shipmentOps.IngestCarrierWebhookAsync(
                    "GHN",
                    deliveredInput,
                    rawBody: rawDeliveredJson,
                    signatureHeader: validSig,
                    actorUserId: fx.UserId);
                replayRes.StatusCode.Should().Be(200);
                using var replayDoc = JsonSerializer.SerializeToDocument(replayRes.Payload);
                replayDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();

                // 5. DeliveryFailed (RTS) transition test
                var rtsInput = new CarrierWebhookEventInput(
                    EventId: $"EVT-RTS-{fx.Suffix}",
                    TrackingNumber: bookedAwb,
                    ShipmentId: shipmentId,
                    ExportReceiptId: exportReceiptId,
                    CarrierStatus: "DELIVERY_FAILED",
                    FailureReason: "Khách hẹn giao lại ca sau");
                var rtsRes = await shipmentOps.IngestCarrierWebhookAsync("GHN", rtsInput, actorUserId: fx.UserId);
                rtsRes.StatusCode.Should().Be(200);
                using var rtsDoc = JsonSerializer.SerializeToDocument(rtsRes.Payload);
                rtsDoc.RootElement.GetProperty("shipmentStatus").GetString().Should().Be("DeliveryFailed");
                rtsDoc.RootElement.GetProperty("rtsReady").GetBoolean().Should().BeTrue();
            }
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            if (!string.IsNullOrWhiteSpace(bookedAwb))
            {
                await cleanupDb.IntegrationOutboxMessages.Where(m => m.AggregateId == shipmentId || m.AggregateId == exportReceiptId).ExecuteDeleteAsync();
                await cleanupDb.IntegrationInboxMessages.Where(m => m.ExternalMessageId.Contains(fx.Suffix)).ExecuteDeleteAsync();
                await cleanupDb.ShipmentFinancialLedgers.Where(l => l.TrackingNumber == bookedAwb || l.ShipmentId == shipmentId).ExecuteDeleteAsync();
            }
            if (shipmentId > 0)
            {
                await cleanupDb.ShipmentTrackingEvents.Where(e => e.ShipmentId == shipmentId).ExecuteDeleteAsync();
                await cleanupDb.ShipmentProofOfDeliveries.Where(p => p.ShipmentId == shipmentId).ExecuteDeleteAsync();
            }
            if (exportReceiptId > 0)
            {
                await cleanupDb.ExportReceipts.Where(r => r.Id == exportReceiptId).ExecuteDeleteAsync();
            }
            if (shipmentId > 0)
            {
                await cleanupDb.Shipments.Where(s => s.Id == shipmentId).ExecuteDeleteAsync();
            }
            if (packSessionId > 0)
            {
                await cleanupDb.PackingSessions.Where(p => p.Id == packSessionId).ExecuteDeleteAsync();
            }
            if (pickTaskId > 0)
            {
                await cleanupDb.PickingTasks.Where(t => t.Id == pickTaskId).ExecuteDeleteAsync();
            }
            await cleanupDb.CarrierIntegrationConfigs.Where(c => c.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            await CleanupAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task Epic30_3_MarketplaceOAuth_BufferedInventoryPush_OrderCancelledPutbackGate_And_ConcurrentWebhookSafety()
    {
        var fx = await CreateFixtureAsync();
        int shopId = 0;
        int customerId = 0;
        int prePickOrderId = 0;
        int postPickOrderId = 0;
        int postPickReceiptId = 0;
        string postPickAwb = $"SPX-W59-{fx.Suffix}";
        try
        {
            await using (var db = CreateContext())
            {
                var customer = new BusinessPartner
                {
                    Code = $"CUST-W59-{fx.Suffix}"[..16],
                    Name = "Khách Sàn Shopee Wave 59",
                    IsCustomer = true,
                    IsActive = true
                };
                db.BusinessPartners.Add(customer);
                await db.SaveChangesAsync();
                customerId = customer.Id;

                var salesOps = new SalesOrderOperationsService(db);

                // 1. OAuth 2.0 Exchange & Refresh Token Rotation for SHOPEE
                var oauthRes = await salesOps.ExchangeMarketplaceOAuthAsync(
                    new MarketplaceOAuthExchangeInput(
                        ChannelCode: "SHOPEE",
                        ShopCode: $"SHOPEE-W59-{fx.Suffix}",
                        ShopName: "Gian hàng Shopee Chính hãng W59",
                        AuthCode: "AUTH_CODE_SHOPEE_2026",
                        DefaultWarehouseId: fx.WarehouseId,
                        WebhookSecret: SalesOrderOperationsService.DefaultWebhookSecret));
                oauthRes.StatusCode.Should().Be(200);
                using var oauthDoc = JsonSerializer.SerializeToDocument(oauthRes.Payload);
                shopId = oauthDoc.RootElement.GetProperty("shopId").GetInt32();
                oauthDoc.RootElement.GetProperty("oauthConnected").GetBoolean().Should().BeTrue();

                var refreshRes = await salesOps.RefreshMarketplaceOAuthTokenAsync(shopId);
                refreshRes.StatusCode.Should().Be(200);
                using var refreshDoc = JsonSerializer.SerializeToDocument(refreshRes.Payload);
                refreshDoc.RootElement.GetProperty("tokenRotated").GetBoolean().Should().BeTrue();

                // Add SKU mapping & sync buffered inventory via IntegrationOutboxMessages
                db.ChannelSkuMappings.Add(new ChannelSkuMapping
                {
                    ShopId = shopId,
                    ChannelItemId = $"SP-ITEM-{fx.ProductId}",
                    ChannelSkuCode = fx.ProductCode,
                    InternalProductId = fx.ProductId,
                    ConversionFactor = 1m,
                    IsActive = true
                });
                await db.SaveChangesAsync();

                var syncRes = await salesOps.SyncMarketplaceInventoryAsync(
                    new SyncMarketplaceInventoryInput(
                        WarehouseId: fx.WarehouseId,
                        ShopId: shopId,
                        ProductId: fx.ProductId,
                        SafetyStockBufferPct: 20m));
                syncRes.StatusCode.Should().Be(200);
                using var syncDoc = JsonSerializer.SerializeToDocument(syncRes.Payload);
                syncDoc.RootElement.GetProperty("syncedCount").GetInt32().Should().Be(1);
                // ATP is 10, SafetyStockBufferPct = 20% -> ReserveRatioPct = 80% -> pushedQuantity = 8
                syncDoc.RootElement.GetProperty("items")[0].GetProperty("pushedQuantity").GetDecimal().Should().Be(8m);

                // 2. Pre-Pick Cancellation -> Releases ReservedQuantity immediately back to Available
                var prePickOrder = new SalesOrder
                {
                    WarehouseId = fx.WarehouseId,
                    CustomerId = customerId,
                    OrderCode = $"SO-PRE-{fx.Suffix}",
                    ExternalOrderId = $"MP-PRE-{fx.Suffix}",
                    RequestedShipDate = DateTime.UtcNow.Date.AddDays(1),
                    Status = SalesOrderStatus.Released,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                prePickOrder.Lines.Add(new SalesOrderLine
                {
                    ProductId = fx.ProductId,
                    OrderedQuantity = 3m,
                    UnitPrice = 100000m,
                    UomCode = "CÁI"
                });
                db.SalesOrders.Add(prePickOrder);
                await db.SaveChangesAsync();
                prePickOrderId = prePickOrder.Id;

                var stockRow = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProductId);
                stockRow.ReservedQuantity = 3m;
                db.StockReservations.Add(new StockReservation
                {
                    ReservationCode = $"RSV-PRE-{fx.Suffix}",
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    Quantity = 3m,
                    SourceType = "SalesOrder",
                    SourceId = prePickOrderId,
                    Status = StockReservationStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddHours(2),
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                var rawCancelPayload = $"{{\"order_sn\":\"MP-PRE-{fx.Suffix}\",\"status\":\"CANCELLED\"}}";
                var cancelSig = MarketplaceWebhookSignatureVerifier.ComputeHmacSha256Hex(rawCancelPayload, SalesOrderOperationsService.DefaultWebhookSecret);

                var preCancelRes = await salesOps.WebhookBuyerCancellationAsync(
                    "SHOPEE",
                    new BuyerCancellationWebhookInput(
                        SalesOrderId: prePickOrderId,
                        OrderNumber: prePickOrder.OrderCode,
                        ChannelCode: "SHOPEE",
                        EventId: $"WHK-PRE-{fx.Suffix}",
                        HmacSignature: cancelSig,
                        RawPayload: rawCancelPayload,
                        BuyerReason: "Khách đổi ý trước khi kho nhặt hàng"),
                    fx.UserId);
                preCancelRes.StatusCode.Should().Be(200);
                using var preCancelDoc = JsonSerializer.SerializeToDocument(preCancelRes.Payload);
                preCancelDoc.RootElement.GetProperty("decision").GetString().Should().Be("ACCEPT");
                preCancelDoc.RootElement.GetProperty("orderStatus").GetString().Should().Be("Cancelled");

                var stockAfterPreCancel = await db.InventoryStocks.AsNoTracking()
                    .SingleAsync(s => s.WarehouseId == fx.WarehouseId && s.ProductId == fx.ProductId);
                stockAfterPreCancel.ReservedQuantity.Should().Be(0m);

                // 3. Post-Pick / AWB Printed Cancellation -> CancelPendingPutback + PutawayTask (Putback) + 409 Handover Gate Block
                var postPickOrder = new SalesOrder
                {
                    WarehouseId = fx.WarehouseId,
                    CustomerId = customerId,
                    OrderCode = $"SO-POST-{fx.Suffix}",
                    ExternalOrderId = $"MP-POST-{fx.Suffix}",
                    RequestedShipDate = DateTime.UtcNow.Date.AddDays(1),
                    Status = SalesOrderStatus.Released,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                postPickOrder.Lines.Add(new SalesOrderLine
                {
                    ProductId = fx.ProductId,
                    OrderedQuantity = 2m,
                    UnitPrice = 120000m,
                    UomCode = "CÁI"
                });
                db.SalesOrders.Add(postPickOrder);
                await db.SaveChangesAsync();
                postPickOrderId = postPickOrder.Id;

                var postReceipt = new ExportReceipt
                {
                    WarehouseId = fx.WarehouseId,
                    SalesOrderId = postPickOrderId,
                    Code = $"XK-POST-{fx.Suffix}",
                    TrackingNumber = postPickAwb,
                    MarketplaceOrderId = postPickOrder.ExternalOrderId,
                    CarrierCode = "GHN",
                    AwbPrintCount = 1,
                    Status = ReceiptStatus.Approved,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                db.ExportReceipts.Add(postReceipt);
                await db.SaveChangesAsync();
                postPickReceiptId = postReceipt.Id;

                var eventIdPost = $"WHK-POST-{fx.Suffix}";
                var postCancelRes = await salesOps.WebhookBuyerCancellationAsync(
                    "TIKTOK_SHOP",
                    new BuyerCancellationWebhookInput(
                        SalesOrderId: postPickOrderId,
                        ExportReceiptId: postPickReceiptId,
                        OrderNumber: postPickOrder.OrderCode,
                        MarketplaceOrderId: postPickOrder.ExternalOrderId,
                        ChannelCode: "TIKTOK_SHOP",
                        EventId: eventIdPost,
                        BuyerReason: "Khách hủy đơn sau khi đã nhặt hàng & dán tem AWB"),
                    fx.UserId);
                postCancelRes.StatusCode.Should().Be(200);
                using var postCancelDoc = JsonSerializer.SerializeToDocument(postCancelRes.Payload);
                postCancelDoc.RootElement.GetProperty("decision").GetString().Should().Be("ACCEPT_PENDING_PUTBACK");
                postCancelDoc.RootElement.GetProperty("requiresPutback").GetBoolean().Should().BeTrue();
                postCancelDoc.RootElement.GetProperty("putbackTaskId").GetInt32().Should().BeGreaterThan(0);

                // Replay same webhook eventId -> idempotentReplay = true
                var postCancelReplay = await salesOps.WebhookBuyerCancellationAsync(
                    "TIKTOK_SHOP",
                    new BuyerCancellationWebhookInput(
                        SalesOrderId: postPickOrderId,
                        ExportReceiptId: postPickReceiptId,
                        ChannelCode: "TIKTOK_SHOP",
                        EventId: eventIdPost),
                    fx.UserId);
                postCancelReplay.StatusCode.Should().Be(200);
                using var postReplayDoc = JsonSerializer.SerializeToDocument(postCancelReplay.Payload);
                postReplayDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();

                // 4. HandoverManifestService.ScanHandoverAwbAsync MUST block with HTTP 409 ORDER_CANCELLED_PUTBACK_REQUIRED
                var handoverService = new HandoverManifestService(
                    db,
                    new ExportReceiptService(
                        new ExportReceiptRepository(db),
                        new InventoryStockRepository(db),
                        new InventoryTransactionRepository(db),
                        new UnitOfWork(db),
                        new AuditLogRepository(db)));

                var scanBlockRes = await handoverService.ScanHandoverAwbAsync(
                    null,
                    new ScanHandoverAwbInputDto(
                        WarehouseId: fx.WarehouseId,
                        CarrierCode: "GHN",
                        TrackingNumber: postPickAwb,
                        ExportReceiptId: postPickReceiptId),
                    fx.UserId,
                    isAuthenticated: true);

                scanBlockRes.StatusCode.Should().Be(409);
                using var scanBlockDoc = JsonSerializer.SerializeToDocument(scanBlockRes.Payload);
                scanBlockDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("ORDER_CANCELLED_PUTBACK_REQUIRED");
            }
        }
        finally
        {
            await using var cleanupDb = CreateContext();
            await cleanupDb.IntegrationInboxMessages.Where(m => m.ExternalMessageId.Contains(fx.Suffix)).ExecuteDeleteAsync();
            if (shopId > 0)
            {
                await cleanupDb.IntegrationOutboxMessages.Where(m => m.AggregateId == shopId || m.AggregateId == fx.ProductId).ExecuteDeleteAsync();
                await cleanupDb.ChannelSkuMappings.Where(m => m.ShopId == shopId).ExecuteDeleteAsync();
                await cleanupDb.MarketplaceShops.Where(s => s.Id == shopId).ExecuteDeleteAsync();
            }
            await cleanupDb.PutawayTaskItems.Where(i => i.ProductId == fx.ProductId).ExecuteDeleteAsync();
            await cleanupDb.PutawayTasks.Where(t => t.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            await cleanupDb.ImportReceiptDetails.Where(d => d.ProductId == fx.ProductId).ExecuteDeleteAsync();
            await cleanupDb.ImportReceipts.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            await cleanupDb.StockReservations.Where(r => r.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
            if (postPickReceiptId > 0)
            {
                await cleanupDb.ExportReceipts.Where(r => r.Id == postPickReceiptId).ExecuteDeleteAsync();
            }
            if (prePickOrderId > 0 || postPickOrderId > 0)
            {
                await cleanupDb.SalesOrderLines.Where(l => l.SalesOrderId == prePickOrderId || l.SalesOrderId == postPickOrderId).ExecuteDeleteAsync();
                await cleanupDb.SalesOrders.Where(o => o.Id == prePickOrderId || o.Id == postPickOrderId).ExecuteDeleteAsync();
            }
            if (customerId > 0)
            {
                await cleanupDb.BusinessPartners.Where(c => c.Id == customerId).ExecuteDeleteAsync();
            }
            await CleanupAsync(fx);
        }
    }

    private sealed record Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        string ProductCode,
        int WarehouseId,
        int LocationAId,
        int LocationBId,
        int LotId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"W59Role{suffix}" };
        var user = new User { Username = $"W59User{suffix}", PasswordHash = "hash", FullName = "Wave59 Tester", Role = role };
        var unit = new Unit { Code = $"U59{suffix}"[..10], Name = "Cái" };
        var productCode = $"SKU-W59-{suffix}";
        var product = new Product { Code = productCode, Name = "Sản phẩm Wave 59", Unit = unit, IsActive = true };
        var warehouse = new Warehouse { Code = $"WH59-{suffix}"[..12], Name = "Kho Wave 59", IsActive = true };
        db.AddRange(role, user, unit, product, warehouse);
        await db.SaveChangesAsync();

        var locA = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"A-01-01-{suffix}"[..16],
            Name = "Kệ A-01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var locB = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"B-02-01-{suffix}"[..16],
            Name = "Kệ B-02",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LotNumber = $"LOT-W59-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(11),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(locA, locB, lot);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id });
        var initialStock = new InventoryStock
        {
            TenantId = 1,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            LocationId = locA.Id,
            LotId = lot.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        initialStock.RefreshGrainHash();
        db.InventoryStocks.Add(initialStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            TenantId = 1,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            LocationId = locA.Id,
            LotId = lot.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-30),
            CreatedBy = user.Id
        });
        await db.SaveChangesAsync();

        return new Fixture(suffix, user.Id, role.Id, unit.Id, product.Id, productCode, warehouse.Id, locA.Id, locB.Id, lot.Id);
    }

    private static async Task CleanupAsync(Fixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && x.ReversalOfTransactionId != null).ExecuteDeleteAsync();
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.Id == fx.LotId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }
}
