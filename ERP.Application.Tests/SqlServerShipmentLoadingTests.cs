using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerShipmentLoadingTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task PackedNestedHu_AutoCreatesReadyShipment_WithRootOnly_WithoutInventoryMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);

            await using var verify = CreateContext();
            var shipment = await verify.Shipments.AsNoTracking().SingleAsync();
            shipment.Status.Should().Be(ShipmentStatus.Ready);
            var link = await verify.ShipmentHandlingUnits.AsNoTracking().SingleAsync();
            link.HandlingUnitId.Should().Be(prepared.ParentHuId);
            (await verify.HandlingUnits.AsNoTracking().CountAsync()).Should().Be(2);
            (await verify.HandlingUnits.AsNoTracking().AllAsync(x => x.Status == HandlingUnitStatus.Closed)).Should().BeTrue();

            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.InventoryTransactions.CountAsync(x => x.WarehouseId == fixture.WarehouseId)).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task StageLoadComplete_TransitionsWholeHuHierarchy_WithoutOnHandMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            var canonicalSource = await AssignCanonicalExportSourceAsync(prepared.ShipmentId, fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
                staged.Status.Should().Be(nameof(ShipmentStatus.Staging));
                staged.StagingLocationId.Should().Be(fixture.StagingLocationId);
                staged.StagingLocationCode.Should().Be(fixture.StagingLocationCode);
            }

            int appointmentId;
            await using (var dock = CreateContext())
            {
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.InService);
            }

            ShipmentDto loading;
            await using (var start = CreateContext())
            {
                loading = await CreateShipmentService(start, fixture.UserId).StartLoadingAsync(
                    prepared.ShipmentId,
                    new StartShipmentLoadingDto
                    {
                        DockAppointmentId = appointmentId,
                        RowVersion = staged.RowVersion!
                    });
                loading.Status.Should().Be(nameof(ShipmentStatus.Loading));
                loading.VehiclePlate.Should().Be("51C-12345");
            }

            ShipmentDto huLoaded;
            await using (var load = CreateContext())
            {
                huLoaded = await CreateShipmentService(load, fixture.UserId).LoadHandlingUnitAsync(
                    prepared.ShipmentId,
                    new LoadShipmentHandlingUnitDto
                    {
                        HandlingUnitBarcode = prepared.ParentHuBarcode,
                        RowVersion = loading.RowVersion!
                    });
                huLoaded.LoadedHandlingUnitCount.Should().Be(1);
            }

            await using (var verifyHierarchy = CreateContext())
            {
                var statuses = await verifyHierarchy.HandlingUnits.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId)
                    .Select(x => x.Status)
                    .ToListAsync();
                statuses.Should().OnlyContain(x => x == HandlingUnitStatus.Loaded);
            }

            ShipmentDto loaded;
            await using (var complete = CreateContext())
            {
                loaded = await CreateShipmentService(complete, fixture.UserId).CompleteLoadingAsync(
                    prepared.ShipmentId,
                    new CompleteShipmentLoadingDto
                    {
                        SealNumber = "seal-001",
                        RowVersion = huLoaded.RowVersion!
                    });
                loaded.Status.Should().Be(nameof(ShipmentStatus.Loaded));
                loaded.SealNumber.Should().Be("SEAL-001");
            }

            await using (var readiness = CreateContext())
            {
                await CreatePackingService(readiness, fixture.UserId).EnsureSourceReadyAsync(
                    canonicalSource.SourceType,
                    canonicalSource.SourceId);
                await FluentActions.Awaiting(() => CreateShipmentService(readiness, fixture.UserId).EnsureSourceReadyAsync(
                        canonicalSource.SourceType,
                        canonicalSource.SourceId))
                    .Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                    .WithMessage("*dispatch tại Shipment*");
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.InventoryTransactions.CountAsync()).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DispatchReadiness_RejectsCanonicalShipmentBeforeLoaded()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            var canonicalSource = await AssignCanonicalExportSourceAsync(prepared.ShipmentId, fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var shipmentService = CreateShipmentService(stage, fixture.UserId);
                var current = await shipmentService.GetAsync(prepared.ShipmentId);
                staged = await shipmentService.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
            }

            await using var db = CreateContext();
            await CreatePackingService(db, fixture.UserId).EnsureSourceReadyAsync(
                canonicalSource.SourceType,
                canonicalSource.SourceId);

            var service = CreateShipmentService(db, fixture.UserId);
            await FluentActions.Awaiting(() => service.EnsureSourceReadyAsync(
                    canonicalSource.SourceType,
                    canonicalSource.SourceId))
                .Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                .WithMessage("*dispatch tại Shipment*");
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task StartLoading_RequiresOutboundInServiceAppointment()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
            }

            int appointmentId;
            await using (var dock = CreateContext())
            {
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.DockAssigned);
            }

            await using (var start = CreateContext())
            {
                var service = CreateShipmentService(start, fixture.UserId);
                var act = () => service.StartLoadingAsync(prepared.ShipmentId, new StartShipmentLoadingDto
                {
                    DockAppointmentId = appointmentId,
                    RowVersion = staged.RowVersion!
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_DOCK_APPOINTMENT_INVALID");
            }

            await using var verify = CreateContext();
            (await verify.Shipments.AsNoTracking().SingleAsync()).Status.Should().Be(ShipmentStatus.Staging);
            (await verify.HandlingUnits.AsNoTracking().AllAsync(x => x.Status == HandlingUnitStatus.Staged)).Should().BeTrue();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task LoadingChildHuScan_IsRejectedBecauseShipmentOwnsRootHu()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto { StagingLocationCode = fixture.StagingLocationCode, RowVersion = current.RowVersion! });
            }

            int appointmentId;
            await using (var dock = CreateContext())
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.InService);

            ShipmentDto loading;
            await using (var start = CreateContext())
            {
                loading = await CreateShipmentService(start, fixture.UserId).StartLoadingAsync(
                    prepared.ShipmentId,
                    new StartShipmentLoadingDto { DockAppointmentId = appointmentId, RowVersion = staged.RowVersion! });
            }

            await using (var load = CreateContext())
            {
                var service = CreateShipmentService(load, fixture.UserId);
                var act = () => service.LoadHandlingUnitAsync(prepared.ShipmentId, new LoadShipmentHandlingUnitDto
                {
                    HandlingUnitBarcode = prepared.ChildHuBarcode,
                    RowVersion = loading.RowVersion!
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_HU_MISMATCH");
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ShipmentDispatch_RequiresLoadedState_WithoutInventoryMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            await AssignCanonicalExportSourceAsync(prepared.ShipmentId, fixture);

            await using (var dispatch = CreateContext())
            {
                var service = CreateShipmentService(dispatch, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                var act = () => service.DispatchAsync(
                    prepared.ShipmentId,
                    new ShipmentStateCommandDto { RowVersion = current.RowVersion! });

                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_STATE_INVALID");
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ReferenceType == "Shipment" && x.ReferenceId == prepared.ShipmentId))
                .Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task LoadedShipment_DispatchesExactlyOnce_WithLocationShipLedger_AndSourceSync()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            var canonicalSource = await AssignCanonicalExportSourceAsync(prepared.ShipmentId, fixture);
            var loaded = await LoadShipmentAsync(prepared, fixture);

            ShipmentDto dispatched;
            await using (var dispatch = CreateContext())
            {
                dispatched = await CreateShipmentService(dispatch, fixture.UserId).DispatchAsync(
                    prepared.ShipmentId,
                    new ShipmentStateCommandDto { RowVersion = loaded.RowVersion! });
                dispatched.Status.Should().Be(nameof(ShipmentStatus.Dispatched));
                dispatched.DispatchedBy.Should().Be(fixture.UserId);
                dispatched.DispatchedAt.Should().NotBeNull();
            }

            await using (var verify = CreateContext())
            {
                var stock = await verify.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
                stock.Quantity.Should().Be(0);
                stock.ReservedQuantity.Should().Be(0);

                var reservation = await verify.StockReservations.AsNoTracking()
                    .SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
                reservation.Status.Should().Be(StockReservationStatus.Consumed);
                reservation.ConsumedQuantity.Should().Be(10);
                reservation.AllocatedQuantity.Should().Be(0);

                var allocation = await verify.StockAllocations.AsNoTracking()
                    .SingleAsync(x => x.ReservationId == reservation.Id);
                allocation.Status.Should().Be(StockAllocationStatus.Consumed);

                var ledger = await verify.InventoryTransactions.AsNoTracking()
                    .Where(x => x.ReferenceType == "Shipment" &&
                                x.ReferenceId == prepared.ShipmentId &&
                                x.TransactionType == TransactionType.Ship)
                    .ToListAsync();
                ledger.Should().ContainSingle();
                ledger.Single().ProductId.Should().Be(fixture.ProductId);
                ledger.Single().WarehouseId.Should().Be(fixture.WarehouseId);
                ledger.Single().LocationId.Should().Be(fixture.LocationId);
                ledger.Single().InventoryStatus.Should().Be(InventoryStatus.Available);
                ledger.Single().Quantity.Should().Be(10);

                (await verify.HandlingUnits.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId)
                    .AllAsync(x => x.Status == HandlingUnitStatus.Shipped))
                    .Should().BeTrue();

                var receipt = await verify.ExportReceipts.AsNoTracking()
                    .SingleAsync(x => x.Id == canonicalSource.SourceId);
                receipt.Status.Should().Be(ReceiptStatus.Dispatched);
                receipt.DispatchedBy.Should().Be(fixture.UserId);
                receipt.DispatchedAt.Should().NotBeNull();
            }

            await using (var retry = CreateContext())
            {
                var service = CreateShipmentService(retry, fixture.UserId);
                var act = () => service.DispatchAsync(
                    prepared.ShipmentId,
                    new ShipmentStateCommandDto { RowVersion = dispatched.RowVersion! });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_STATE_INVALID");
            }

            await using (var verifyRetry = CreateContext())
            {
                (await verifyRetry.InventoryTransactions.AsNoTracking()
                    .CountAsync(x => x.ReferenceType == "Shipment" &&
                                     x.ReferenceId == prepared.ShipmentId &&
                                     x.TransactionType == TransactionType.Ship))
                    .Should().Be(1);
                var stock = await verifyRetry.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
                stock.Quantity.Should().Be(0);
                stock.ReservedQuantity.Should().Be(0);
            }
        }
        finally { await CleanupAsync(fixture); }
    }


    [SqlServerFact]
    public async Task Traceability_TrackedShipmentExposure_UsesActualDispatchLedgerAndIgnoresOrphans()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            string lotNumber;
            await using (var tracked = CreateContext())
            {
                var lot = new InventoryLot
                {
                    ProductId = fixture.ProductId,
                    LotNumber = "SHIPLOT-" + fixture.Suffix,
                    ReceivedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                tracked.InventoryLots.Add(lot);
                await tracked.SaveChangesAsync();
                lotNumber = lot.LotNumber;
                var stock = await tracked.InventoryStocks.SingleAsync(x =>
                    x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
                stock.LotId = lot.Id;
                await tracked.SaveChangesAsync();
            }

            var prepared = await PrepareDispatchedShipmentAsync(fixture);
            await using var verify = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                verify, new WarehouseAuthorizationService(verify, new CurrentUser(fixture.UserId)));

            var posted = await verify.InventoryTransactions.AsNoTracking().SingleAsync(x =>
                x.TransactionType == TransactionType.Ship &&
                x.ReferenceType == "Shipment" && x.ReferenceId == prepared.Prepared.ShipmentId);
            posted.LotId.Should().NotBeNull();
            posted.Quantity.Should().Be(10m);

            var result = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber, limit: 20);
            var exposure = result.ShipmentExposures.Should().ContainSingle().Which;
            exposure.ShipmentId.Should().Be(prepared.Prepared.ShipmentId);
            exposure.WarehouseId.Should().Be(fixture.WarehouseId);
            exposure.ShipmentCode.Should().NotBeNullOrWhiteSpace();
            exposure.ShipmentStatus.Should().Be(nameof(ShipmentStatus.Dispatched));
            exposure.DispatchedAt.Should().NotBeNull();
            exposure.DispatchedQuantity.Should().Be(10m);
            exposure.LedgerEventCount.Should().Be(1);
            exposure.LastTransactionId.Should().Be(posted.Id);
            result.ShipmentExposuresTruncated.Should().BeFalse();

            // Linked records must be the canonical Shipment -> Packing ->
            // Picking -> Allocation chain and the same SHIP ledger bucket.
            var pickEvidence = result.ShipmentPickingEvidence.Should().ContainSingle().Which;
            pickEvidence.ShipmentId.Should().Be(prepared.Prepared.ShipmentId);
            pickEvidence.ShipmentCode.Should().Be(exposure.ShipmentCode);
            pickEvidence.WarehouseId.Should().Be(fixture.WarehouseId);
            pickEvidence.PackingSessionId.Should().BeGreaterThan(0);
            pickEvidence.PackingSessionCode.Should().NotBeNullOrWhiteSpace();
            pickEvidence.PickingTaskId.Should().BeGreaterThan(0);
            pickEvidence.PickingTaskCode.Should().NotBeNullOrWhiteSpace();
            pickEvidence.PickingTaskLineId.Should().BeGreaterThan(0);
            pickEvidence.AllocationId.Should().BeGreaterThan(0);
            pickEvidence.SourceLocationCode.Should().Be(fixture.LocationCode);
            pickEvidence.PickedQuantity.Should().Be(10m);
            result.ShipmentPickingEvidenceTruncated.Should().BeFalse();

            // A forged shipment reference in the immutable ledger alone is
            // never sufficient proof that a canonical Shipment exists.
            await using (var orphan = CreateContext())
            {
                orphan.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.LocationId, LotId = posted.LotId,
                    CreatedBy = fixture.UserId, TransactionType = TransactionType.Ship,
                    InventoryStatus = InventoryStatus.Available, Quantity = 99,
                    ReferenceType = "Shipment", ReferenceId = int.MaxValue,
                    TransactionDate = DateTime.UtcNow
                });
                await orphan.SaveChangesAsync();
            }

            var afterOrphan = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber, limit: 20);
            afterOrphan.ShipmentExposures.Should().ContainSingle();
            afterOrphan.ShipmentExposures[0].DispatchedQuantity.Should().Be(10m);
            afterOrphan.ShipmentPickingEvidence.Should().ContainSingle();
            afterOrphan.ShipmentPickingEvidence[0].PickingTaskLineId.Should()
                .Be(pickEvidence.PickingTaskLineId);
            var anchored = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber,
                limit: 20, eventAnchorId: result.EventAnchorId);
            anchored.ShipmentExposures.Should().ContainSingle();
            anchored.ShipmentExposures[0].LastTransactionId.Should().Be(posted.Id);
            anchored.ShipmentPickingEvidence.Should().ContainSingle();
            anchored.ShipmentPickingEvidence[0].AllocationId.Should()
                .Be(pickEvidence.AllocationId);

            var nonexistentLot = await query.TraceAsync(productId: fixture.ProductId,
                lotNumber: lotNumber + "-other", limit: 20);
            nonexistentLot.ShipmentExposures.Should().BeEmpty();
            nonexistentLot.ShipmentPickingEvidence.Should().BeEmpty();
            var untracked = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, limit: 20);
            untracked.ShipmentExposures.Should().BeEmpty();
            untracked.ShipmentPickingEvidence.Should().BeEmpty();
            var exactDocument = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber,
                referenceType: "Shipment", referenceId: prepared.Prepared.ShipmentId, limit: 20);
            exactDocument.ShipmentExposures.Should().BeEmpty();
            exactDocument.ShipmentPickingEvidence.Should().BeEmpty();

            // Losing the warehouse assignment after displaying a result
            // must fail closed on the next query, including canonical links.
            await using (var revoke = CreateContext())
                await revoke.UserWarehouses.Where(x =>
                    x.UserId == fixture.UserId &&
                    x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            var denied = () => query.TraceAsync(
                warehouseId: fixture.WarehouseId, productId: fixture.ProductId,
                lotNumber: lotNumber, limit: 20, eventAnchorId: result.EventAnchorId);
            await denied.Should().ThrowAsync<ERP.Application.Exceptions.NotFoundException>();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task PostDispatchDeliveryLifecycle_DoesNotChangeInventoryOrCreateSecondShipLedger()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var result = await PrepareDispatchedShipmentAsync(fixture);

            ShipmentDto inTransit;
            await using (var transit = CreateContext())
            {
                inTransit = await CreateShipmentService(transit, fixture.UserId).MarkInTransitAsync(
                    result.Prepared.ShipmentId,
                    new MarkShipmentInTransitDto
                    {
                        Note = "Carrier picked up",
                        RowVersion = result.Dispatched.RowVersion!
                    });
                inTransit.Status.Should().Be(nameof(ShipmentStatus.InTransit));
            }

            await using (var invalidPod = CreateContext())
            {
                var service = CreateShipmentService(invalidPod, fixture.UserId);
                await FluentActions.Awaiting(() => service.ConfirmDeliveryAsync(
                        result.Prepared.ShipmentId,
                        new ConfirmShipmentDeliveryDto
                        {
                            ReceiverName = "Nguyen Van A",
                            RowVersion = inTransit.RowVersion!
                        }))
                    .Should().ThrowAsync<BusinessRuleException>()
                    .WithMessage("*EvidenceReference hoặc CarrierReference*");
            }

            ShipmentDto delivered;
            await using (var delivery = CreateContext())
            {
                delivered = await CreateShipmentService(delivery, fixture.UserId).ConfirmDeliveryAsync(
                    result.Prepared.ShipmentId,
                    new ConfirmShipmentDeliveryDto
                    {
                        ReceiverName = "Nguyen Van A",
                        EvidenceReference = "pod://shipment/test-proof",
                        Latitude = 10.7769m,
                        Longitude = 106.7009m,
                        CarrierReference = "CARRIER-POD-001",
                        DeliveryNote = "Delivered intact",
                        RowVersion = inTransit.RowVersion!
                    });
                delivered.Status.Should().Be(nameof(ShipmentStatus.Delivered));
                delivered.ProofOfDelivery.Should().NotBeNull();
                delivered.ProofOfDelivery!.ReceiverName.Should().Be("Nguyen Van A");
            }

            await using (var complete = CreateContext())
            {
                var completed = await CreateShipmentService(complete, fixture.UserId).CompleteAsync(
                    result.Prepared.ShipmentId,
                    new ShipmentStateCommandDto { RowVersion = delivered.RowVersion! });
                completed.Status.Should().Be(nameof(ShipmentStatus.Completed));
                completed.TrackingEvents.Select(x => x.EventType)
                    .Should().ContainInOrder("ShipmentInTransit", "DeliveryConfirmed", "ShipmentCompleted");
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(0);
            stock.ReservedQuantity.Should().Be(0);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ReferenceType == "Shipment" &&
                                 x.ReferenceId == result.Prepared.ShipmentId &&
                                 x.TransactionType == TransactionType.Ship))
                .Should().Be(1);
            (await verify.ShipmentProofOfDeliveries.AsNoTracking()
                .CountAsync(x => x.ShipmentId == result.Prepared.ShipmentId))
                .Should().Be(1);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DeliveryFailureAndRetry_DoNotChangeInventoryOrCreateSecondShipLedger()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var result = await PrepareDispatchedShipmentAsync(fixture);

            ShipmentDto inTransit;
            await using (var transit = CreateContext())
            {
                inTransit = await CreateShipmentService(transit, fixture.UserId).MarkInTransitAsync(
                    result.Prepared.ShipmentId,
                    new MarkShipmentInTransitDto
                    {
                        Note = "Out for carrier handoff",
                        RowVersion = result.Dispatched.RowVersion!
                    });
            }

            ShipmentDto failed;
            await using (var fail = CreateContext())
            {
                failed = await CreateShipmentService(fail, fixture.UserId).FailDeliveryAsync(
                    result.Prepared.ShipmentId,
                    new FailShipmentDeliveryDto
                    {
                        ReasonCode = "CUSTOMER_UNAVAILABLE",
                        Note = "Customer not at address",
                        RowVersion = inTransit.RowVersion!
                    });
                failed.Status.Should().Be(nameof(ShipmentStatus.DeliveryFailed));
                failed.DeliveryFailedAt.Should().NotBeNull();
            }

            await using (var retry = CreateContext())
            {
                var retried = await CreateShipmentService(retry, fixture.UserId).RetryDeliveryAsync(
                    result.Prepared.ShipmentId,
                    new RetryShipmentDeliveryDto
                    {
                        Note = "Retry next route",
                        RowVersion = failed.RowVersion!
                    });
                retried.Status.Should().Be(nameof(ShipmentStatus.InTransit));
                retried.TrackingEvents.Select(x => x.EventType)
                    .Should().ContainInOrder("ShipmentInTransit", "DeliveryFailed", "DeliveryRetryStarted");
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(0);
            stock.ReservedQuantity.Should().Be(0);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ReferenceType == "Shipment" &&
                                 x.ReferenceId == result.Prepared.ShipmentId &&
                                 x.TransactionType == TransactionType.Ship))
                .Should().Be(1);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReturnToWarehouseInitiation_DoesNotIncreaseWarehouseInventory()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var result = await PrepareDispatchedShipmentAsync(fixture);

            ShipmentDto inTransit;
            await using (var transit = CreateContext())
            {
                inTransit = await CreateShipmentService(transit, fixture.UserId).MarkInTransitAsync(
                    result.Prepared.ShipmentId,
                    new MarkShipmentInTransitDto
                    {
                        RowVersion = result.Dispatched.RowVersion!
                    });
            }

            await using (var returnFlow = CreateContext())
            {
                var returning = await CreateShipmentService(returnFlow, fixture.UserId).InitiateReturnAsync(
                    result.Prepared.ShipmentId,
                    new InitiateShipmentReturnDto
                    {
                        ReasonCode = "CUSTOMER_REFUSED",
                        Note = "Return requires separate receiving workflow",
                        RowVersion = inTransit.RowVersion!
                    });
                returning.Status.Should().Be(nameof(ShipmentStatus.ReturnToWarehouse));
                returning.ReturnInitiatedAt.Should().NotBeNull();
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(0);
            stock.ReservedQuantity.Should().Be(0);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ReferenceType == "Shipment" &&
                                 x.ReferenceId == result.Prepared.ShipmentId &&
                                 x.TransactionType == TransactionType.Ship))
                .Should().Be(1);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ReferenceType == "Shipment" &&
                                 x.ReferenceId == result.Prepared.ShipmentId &&
                                 x.TransactionType != TransactionType.Ship))
                .Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    private static async Task<(Prepared Prepared, ShipmentDto Dispatched)> PrepareDispatchedShipmentAsync(Fixture fixture)
    {
        var prepared = await PreparePackedNestedShipmentAsync(fixture);
        await AssignCanonicalExportSourceAsync(prepared.ShipmentId, fixture);
        var loaded = await LoadShipmentAsync(prepared, fixture);

        await using var dispatch = CreateContext();
        var dispatched = await CreateShipmentService(dispatch, fixture.UserId).DispatchAsync(
            prepared.ShipmentId,
            new ShipmentStateCommandDto { RowVersion = loaded.RowVersion! });
        return (prepared, dispatched);
    }

    private static async Task<ShipmentDto> LoadShipmentAsync(Prepared prepared, Fixture fixture)
    {
        ShipmentDto staged;
        await using (var stage = CreateContext())
        {
            var service = CreateShipmentService(stage, fixture.UserId);
            var current = await service.GetAsync(prepared.ShipmentId);
            staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto
            {
                StagingLocationCode = fixture.StagingLocationCode,
                RowVersion = current.RowVersion!
            });
        }

        int appointmentId;
        await using (var dock = CreateContext())
        {
            appointmentId = await CreateOutboundAppointmentAsync(
                dock,
                fixture,
                DockAppointmentStatus.InService);
        }

        ShipmentDto loading;
        await using (var start = CreateContext())
        {
            loading = await CreateShipmentService(start, fixture.UserId).StartLoadingAsync(
                prepared.ShipmentId,
                new StartShipmentLoadingDto
                {
                    DockAppointmentId = appointmentId,
                    RowVersion = staged.RowVersion!
                });
        }

        ShipmentDto huLoaded;
        await using (var load = CreateContext())
        {
            huLoaded = await CreateShipmentService(load, fixture.UserId).LoadHandlingUnitAsync(
                prepared.ShipmentId,
                new LoadShipmentHandlingUnitDto
                {
                    HandlingUnitBarcode = prepared.ParentHuBarcode,
                    RowVersion = loading.RowVersion!
                });
        }

        await using var complete = CreateContext();
        return await CreateShipmentService(complete, fixture.UserId).CompleteLoadingAsync(
            prepared.ShipmentId,
            new CompleteShipmentLoadingDto
            {
                SealNumber = "SHIP-SEAL-001",
                RowVersion = huLoaded.RowVersion!
            });
    }

    private static async Task<(string SourceType, int SourceId)> AssignCanonicalExportSourceAsync(
        int shipmentId,
        Fixture fixture)
    {
        const string sourceType = "ExportReceipt";
        await using var db = CreateContext();
        var receipt = new ExportReceipt
        {
            Code = $"EXP-{fixture.Suffix}",
            WarehouseId = fixture.WarehouseId,
            Status = ReceiptStatus.Approved,
            DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
            CreatedBy = fixture.UserId,
            ApprovedBy = fixture.UserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ApprovedAt = DateTime.UtcNow.AddMinutes(-20),
            Details =
            [
                new ExportReceiptDetail
                {
                    ProductId = fixture.ProductId,
                    Quantity = 10
                }
            ]
        };
        db.ExportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var shipment = await db.Shipments
            .Include(x => x.PackingSession)
            .ThenInclude(x => x.PickingTask)
            .SingleAsync(x => x.Id == shipmentId);
        shipment.SourceType = sourceType;
        shipment.SourceId = receipt.Id;
        shipment.SourceCode = receipt.Code;
        shipment.PackingSession.PickingTask.SourceType = sourceType;
        shipment.PackingSession.PickingTask.SourceId = receipt.Id;
        shipment.PackingSession.PickingTask.SourceCode = receipt.Code;
        await db.SaveChangesAsync();
        return (sourceType, receipt.Id);
    }

    private static async Task<Prepared> PreparePackedNestedShipmentAsync(Fixture fixture)
    {
        var session = await PrepareCompletedPickingAsync(fixture);

        PackingSessionDto childCreated;
        await using (var create = CreateContext())
        {
            childCreated = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
            {
                Type = "Carton",
                HuCode = $"CHILD-{fixture.Suffix}",
                Barcode = $"CHILD-{fixture.Suffix}",
                RowVersion = session.RowVersion!
            });
        }

        PackingSessionDto parentCreated;
        await using (var create = CreateContext())
        {
            parentCreated = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
            {
                Type = "Pallet",
                HuCode = $"PARENT-{fixture.Suffix}",
                Barcode = $"PARENT-{fixture.Suffix}",
                RowVersion = childCreated.RowVersion!
            });
        }

        var child = parentCreated.HandlingUnits.Single(x => x.HuCode == $"CHILD-{fixture.Suffix}");
        var parent = parentCreated.HandlingUnits.Single(x => x.HuCode == $"PARENT-{fixture.Suffix}");

        PackingSessionDto packed;
        await using (var pack = CreateContext())
        {
            packed = await CreatePackingService(pack, fixture.UserId).PackAsync(session.Id, new()
            {
                HandlingUnitId = child.Id,
                PickingTaskLineId = parentCreated.Lines.Single().PickingTaskLineId,
                ProductBarcode = fixture.ProductCode,
                Quantity = 10,
                RowVersion = parentCreated.RowVersion!
            });
        }

        PackingSessionDto childClosed;
        await using (var close = CreateContext())
        {
            childClosed = await CreatePackingService(close, fixture.UserId).CloseHandlingUnitAsync(
                session.Id,
                child.Id,
                new PackingStateCommandDto { RowVersion = packed.RowVersion! });
        }

        PackingSessionDto nested;
        await using (var nest = CreateContext())
        {
            nested = await CreatePackingService(nest, fixture.UserId).NestHandlingUnitAsync(
                session.Id,
                child.Id,
                new NestHandlingUnitDto { ParentHandlingUnitId = parent.Id, RowVersion = childClosed.RowVersion! });
        }

        PackingSessionDto parentClosed;
        await using (var close = CreateContext())
        {
            parentClosed = await CreatePackingService(close, fixture.UserId).CloseHandlingUnitAsync(
                session.Id,
                parent.Id,
                new PackingStateCommandDto { RowVersion = nested.RowVersion! });
        }

        await using (var complete = CreateContext())
        {
            await CreatePackingService(complete, fixture.UserId, withShipment: true).CompleteAsync(
                session.Id,
                new PackingStateCommandDto { RowVersion = parentClosed.RowVersion! });
        }

        await using var read = CreateContext();
        var shipment = (await CreateShipmentService(read, fixture.UserId).ListAsync(fixture.WarehouseId)).Single();
        return new Prepared(
            shipment.Id,
            child.Id,
            child.Barcode,
            parent.Id,
            parent.Barcode);
    }

    private static async Task<PackingSessionDto> PrepareCompletedPickingAsync(Fixture fixture)
    {
        int reservationId;
        await using (var reserve = CreateContext())
        {
            reservationId = (await CreateReservationService(reserve, fixture.UserId).ReserveForExportAsync(
                exportReceiptId: fixture.UserId,
                exportCode: $"EX-SHIP-{fixture.Suffix}",
                warehouseId: fixture.WarehouseId,
                productId: fixture.ProductId,
                quantity: 10,
                userId: fixture.UserId)).Id;
        }

        await using (var allocate = CreateContext())
        {
            await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
            {
                ReservationId = reservationId,
                Quantity = 10
            });
        }

        PickingTaskDto assigned;
        await using (var assign = CreateContext())
        {
            var service = CreatePickingService(assign, fixture.UserId);
            var created = (await service.ListAsync(fixture.WarehouseId)).Single();
            var detail = await service.GetAsync(created.Id);
            assigned = await service.AssignAsync(created.Id, new()
            {
                AssignedUserId = fixture.UserId,
                RowVersion = detail.RowVersion!
            });
        }

        PickingTaskDto started;
        await using (var start = CreateContext())
        {
            started = await CreatePickingService(start, fixture.UserId).StartAsync(assigned.Id, new()
            {
                RowVersion = assigned.RowVersion!
            });
        }

        PickingTaskDto picked;
        await using (var pick = CreateContext())
        {
            picked = await CreatePickingService(pick, fixture.UserId).PickAsync(started.Id, new()
            {
                TaskLineId = started.Lines.Single().Id,
                LocationBarcode = fixture.LocationCode,
                ProductBarcode = fixture.ProductCode,
                LotNumber = started.Lines.Single().LotNumber,
                SerialNumber = started.Lines.Single().SerialNumber,
                Quantity = 10,
                RowVersion = started.RowVersion!
            });
        }

        await using (var complete = CreateContext())
        {
            await CreatePickingService(complete, fixture.UserId, withPacking: true).CompleteAsync(picked.Id, new()
            {
                RowVersion = picked.RowVersion!
            });
        }

        await using var read = CreateContext();
        var packing = CreatePackingService(read, fixture.UserId);
        var createdSession = (await packing.ListAsync(fixture.WarehouseId)).Single();
        return await packing.GetAsync(createdSession.Id);
    }

    private static async Task<int> CreateOutboundAppointmentAsync(
        ErpKhoDbContext db,
        Fixture fixture,
        DockAppointmentStatus status)
    {
        var dock = new Dock
        {
            WarehouseId = fixture.WarehouseId,
            Code = $"DOCK-{fixture.Suffix}",
            Name = "Outbound dock",
            SupportsInbound = false,
            SupportsOutbound = true,
            IsActive = true
        };
        db.Docks.Add(dock);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var appointment = new DockAppointment
        {
            WarehouseId = fixture.WarehouseId,
            Code = $"APT-{fixture.Suffix}",
            Direction = DockAppointmentDirection.Outbound,
            Status = status,
            PlannedStartUtc = now.AddMinutes(-30),
            PlannedEndUtc = now.AddHours(2),
            VehiclePlate = "51C-12345",
            TrailerPlate = "TR-100",
            DockId = dock.Id,
            DockAssignedAtUtc = now.AddMinutes(-20),
            ServiceStartedAtUtc = status == DockAppointmentStatus.InService ? now.AddMinutes(-10) : null,
            CreatedBy = fixture.UserId,
            CreatedAtUtc = now.AddHours(-1),
            UpdatedBy = fixture.UserId,
            UpdatedAtUtc = now
        };
        db.DockAppointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private static StockReservationService CreateReservationService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            new WarehouseAuthorizationService(db, current),
            current,
            new StockReservationOptions());
    }

    private static StockAllocationService CreateAllocationService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StockAllocationService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            new PickingTaskIntegration(db));
    }

    private static PickingService CreatePickingService(ErpKhoDbContext db, int userId, bool withPacking = false)
    {
        var current = new CurrentUser(userId);
        return new PickingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            withPacking ? new PackingSessionIntegration(db) : null);
    }

    private static PackingService CreatePackingService(ErpKhoDbContext db, int userId, bool withShipment = false)
    {
        var current = new CurrentUser(userId);
        return new PackingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            new PackingSessionIntegration(db),
            withShipment ? new ShipmentIntegration(db) : null);
    }

    private static ShipmentService CreateShipmentService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new ShipmentService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            CreateReservationService(db, userId));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"ShipRole{suffix}" };
        var user = new User
        {
            Username = $"ShipUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Shipment loading user",
            Role = role
        };
        var unit = new Unit { Code = $"SHU{suffix}", Name = "Shipment unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"SHP{suffix}", Name = "Shipment product", Unit = unit };
        var warehouse = new Warehouse { Code = $"SHW{suffix}", Name = "Shipment warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"SHL{suffix}",
            Name = "Shipment source",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var stagingLocation = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"STG{suffix}",
            Name = "Outbound staging lane",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = false,
            IsReceivable = false,
            CreatedBy = user.Id
        };
        db.AddRange(location, stagingLocation);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse
        {
            UserId = user.Id,
            WarehouseId = warehouse.Id,
            CreatedBy = user.Id
        });
        db.InventoryStocks.Add(new InventoryStock
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            LocationId = location.Id,
            Quantity = 10,
            Status = InventoryStatus.Available
        });
        await db.SaveChangesAsync();

        return new Fixture(
            suffix,
            user.Id,
            role.Id,
            unit.Id,
            product.Id,
            product.Code,
            warehouse.Id,
            location.Id,
            location.Code,
            stagingLocation.Id,
            stagingLocation.Code);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.InventoryTransactions
            .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
            .ExecuteDeleteAsync();
        await db.ShipmentProofOfDeliveries
            .Where(x => x.Shipment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.ShipmentTrackingEvents
            .Where(x => x.Shipment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.ShipmentHandlingUnits
            .Where(x => x.Shipment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.Shipments.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.DockAppointmentEvents
            .Where(x => x.Appointment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.DockAppointments.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Docks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.HandlingUnitContents
            .Where(x => x.HandlingUnit.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.HandlingUnits.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.PackingSessions.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.ShortPickExceptions
            .Where(x => x.PickingTaskLine.PickingTask.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.PickingTaskLines
            .Where(x => x.PickingTask.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.PickingTasks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(x => x.CreatedBy == fixture.UserId).ExecuteDeleteAsync();
        await db.ExportReceiptDetails
            .Where(x => x.ExportReceipt.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.ExportReceipts.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fixture.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fixture.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    private sealed record Fixture(
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        string ProductCode,
        int WarehouseId,
        int LocationId,
        string LocationCode,
        int StagingLocationId,
        string StagingLocationCode);

    private sealed record Prepared(
        int ShipmentId,
        int ChildHuId,
        string ChildHuBarcode,
        int ParentHuId,
        string ParentHuBarcode);
}
