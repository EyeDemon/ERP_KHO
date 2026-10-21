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
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class Wave7FullChaosLifecycleSqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task FullWarehouseLifecycle_WithChaosExceptions_ConservesLedgerAndProtectsAllInvariants()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var state = new LifecycleState(suffix);

        try
        {
            // =========================================================================
            // STEP 0: Seed base Actors, Role, Unit, Partners & Products (Lot+Expiry, Serial, Standard)
            // =========================================================================
            await using (var seedDb = CreateContext())
            {
                var role = new Role { RoleName = $"W7Role_{suffix}" };
                var maker = new User
                {
                    Username = $"w7_maker_{suffix}",
                    PasswordHash = "not-used",
                    FullName = "Wave 7 Maker",
                    Role = role
                };
                var checker = new User
                {
                    Username = $"w7_checker_{suffix}",
                    PasswordHash = "not-used",
                    FullName = "Wave 7 Checker",
                    Role = role
                };
                var supplier = new BusinessPartner
                {
                    Code = $"SUP-{suffix}",
                    Name = "Wave 7 Supplier",
                    IsSupplier = true,
                    IsActive = true
                };
                var customer = new BusinessPartner
                {
                    Code = $"CUS-{suffix}",
                    Name = "Wave 7 Customer",
                    IsCustomer = true,
                    IsActive = true
                };
                var unit = new Unit
                {
                    Code = $"EA{suffix[..4]}",
                    Name = "Each",
                    DecimalPlaces = 0
                };
                var lotProduct = new Product
                {
                    Code = $"LOT-SKU-{suffix}",
                    Name = "Lot & Expiry Controlled Product",
                    Unit = unit,
                    TrackingType = ProductTrackingType.Lot,
                    ExpiryControl = true,
                    ShelfLifeDays = 180,
                    IsActive = true
                };
                var serialProduct = new Product
                {
                    Code = $"SER-SKU-{suffix}",
                    Name = "Serial Controlled Product",
                    Unit = unit,
                    TrackingType = ProductTrackingType.Serial,
                    ExpiryControl = false,
                    IsActive = true
                };
                var standardProduct = new Product
                {
                    Code = $"STD-SKU-{suffix}",
                    Name = "Standard Untracked Product",
                    Unit = unit,
                    TrackingType = ProductTrackingType.None,
                    ExpiryControl = false,
                    IsActive = true
                };

                seedDb.AddRange(role, maker, checker, supplier, customer, unit, lotProduct, serialProduct, standardProduct);
                await seedDb.SaveChangesAsync();

                var qcPolicy = new QcPolicy
                {
                    ProductId = lotProduct.Id,
                    SupplierId = supplier.Id,
                    RequiresQc = true,
                    Rule = "100% Inbound Lot Inspection",
                    Version = 1,
                    IsActive = true,
                    EffectiveFromUtc = DateTime.UtcNow.AddDays(-7)
                };
                seedDb.QcPolicies.Add(qcPolicy);
                await seedDb.SaveChangesAsync();

                state.RoleId = role.Id;
                state.MakerId = maker.Id;
                state.CheckerId = checker.Id;
                state.SupplierId = supplier.Id;
                state.CustomerId = customer.Id;
                state.UnitId = unit.Id;
                state.UnitCode = unit.Code;
                state.LotProductId = lotProduct.Id;
                state.LotProductCode = lotProduct.Code;
                state.SerialProductId = serialProduct.Id;
                state.SerialProductCode = serialProduct.Code;
                state.StandardProductId = standardProduct.Id;
                state.StandardProductCode = standardProduct.Code;
            }

            // =========================================================================
            // STEP 1: Create WH1 & WH2 via WarehouseService (verifying RECEIVING, LEGACY,
            //         DAMAGED system locations + W7-02 guard) & WarehouseCalendar exceptions
            // =========================================================================
            await using (var whDb = CreateContext())
            {
                var whService = CreateWarehouseService(whDb, state.MakerId);
                var wh1 = await whService.CreateWarehouseAsync(new CreateWarehouseDto
                {
                    Code = $"WH1-{suffix}",
                    Name = $"Wave 7 Primary Warehouse {suffix}",
                    Address = "KCN Sóng Thần 1"
                }, $"w7_maker_{suffix}");
                var wh2 = await whService.CreateWarehouseAsync(new CreateWarehouseDto
                {
                    Code = $"WH2-{suffix}",
                    Name = $"Wave 7 Secondary Warehouse {suffix}",
                    Address = "KCN Sóng Thần 2"
                }, $"w7_maker_{suffix}");

                state.Wh1Id = wh1.Id;
                state.Wh2Id = wh2.Id;

                var wh1Reloaded = await whService.GetWarehouseByIdAsync(state.Wh1Id);
                wh1Reloaded.Code.Should().Be($"WH1-{suffix}");
                var wh2Reloaded = await whService.GetWarehouseByIdAsync(state.Wh2Id);
                wh2Reloaded.Code.Should().Be($"WH2-{suffix}");

                whDb.UserWarehouses.AddRange(
                    new UserWarehouse { UserId = state.CheckerId, WarehouseId = state.Wh1Id, CreatedBy = state.MakerId },
                    new UserWarehouse { UserId = state.CheckerId, WarehouseId = state.Wh2Id, CreatedBy = state.MakerId });
                await whDb.SaveChangesAsync();

                var wh1Locations = await whDb.WarehouseLocations.AsNoTracking()
                    .Where(x => x.WarehouseId == state.Wh1Id)
                    .ToListAsync();
                wh1Locations.Select(x => x.Code).Should().BeEquivalentTo(["RECEIVING", "STAGING", "QC_HOLD", "QUARANTINE", "DAMAGED", "RETURNS_STAGE", "PACKING_STAGE", "SHIPPING_STAGE", "SCRAP", "LEGACY"]);
                wh1Locations.Should().OnlyContain(x => x.IsSystemManaged);

                var wh1Damaged = wh1Locations.Single(x => x.Code == "DAMAGED");
                wh1Damaged.LocationType.Should().Be(WarehouseLocationType.Damaged);
                wh1Damaged.IsPickable.Should().BeFalse();
                wh1Damaged.IsReceivable.Should().BeFalse();

                var wh2Locations = await whDb.WarehouseLocations.AsNoTracking()
                    .Where(x => x.WarehouseId == state.Wh2Id)
                    .ToListAsync();
                wh2Locations.Select(x => x.Code).Should().BeEquivalentTo(["RECEIVING", "STAGING", "QC_HOLD", "QUARANTINE", "DAMAGED", "RETURNS_STAGE", "PACKING_STAGE", "SHIPPING_STAGE", "SCRAP", "LEGACY"]);
                state.Wh2DamagedLocationId = wh2Locations.Single(x => x.Code == "DAMAGED").Id;
                state.Wh2LegacyLocationId = wh2Locations.Single(x => x.Code == "LEGACY").Id;
            }

            await using (var locDb = CreateContext())
            {
                var putaway = CreatePutawayService(locDb, state.MakerId);

                // W7-02 Guard: User cannot create a custom location named DAMAGED
                var createDamagedCollision = () => putaway.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.Wh1Id,
                    Code = "damaged",
                    Name = "Duplicate Damaged",
                    StructurePath = "ZONE-D/A01/R01/L01/B01",
                    LocationType = "Damaged",
                    IsPickable = false
                });
                await createDamagedCollision.Should().ThrowAsync<BusinessRuleException>()
                    .WithMessage("*RECEIVING, LEGACY, DAMAGED*");

                var binA = await putaway.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"W1-BINA-{suffix}",
                    Name = "WH1 Storage Bin A",
                    StructurePath = "ZONE-A/A01/R01/L01/B01",
                    LocationType = "Storage",
                    IsPickable = true
                });
                var binB = await putaway.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"W1-BINB-{suffix}",
                    Name = "WH1 Storage Bin B",
                    StructurePath = "ZONE-A/A01/R01/L01/B02",
                    LocationType = "Storage",
                    IsPickable = true
                });
                var staging = await putaway.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"W1-STG-{suffix}",
                    Name = "WH1 Outbound Staging",
                    StructurePath = "ZONE-S/A01/R01/L01/B01",
                    LocationType = "Staging",
                    IsPickable = false
                });

                state.Wh1BinAId = binA.Id;
                state.Wh1BinACode = binA.Code;
                state.Wh1BinBId = binB.Id;
                state.Wh1BinBCode = binB.Code;
                state.Wh1StagingId = staging.Id;
                state.Wh1StagingCode = staging.Code;
            }

            await using (var calDb = CreateContext())
            {
                var calService = CreateCalendarService(calDb, state.MakerId);
                var days = Enumerable.Range(0, 7).Select(d => new UpdateWarehouseCalendarDayDto
                {
                    DayOfWeek = d,
                    IsOpen = true,
                    OpensAtLocal = "00:00",
                    ClosesAtLocal = "23:59",
                    InboundCutoffLocal = "23:30",
                    OutboundCutoffLocal = "23:30"
                }).ToList();

                await calService.UpdateCalendarAsync(state.Wh1Id, new UpdateWarehouseCalendarDto
                {
                    TimeZoneId = "UTC",
                    Days = days
                });

                // W5-08: Add a MAINTENANCE_WINDOW exception on 2026-12-25 08:00-12:00 UTC
                await calDb.AuditLogs.AddAsync(new AuditLog
                {
                    UserId = state.MakerId,
                    WarehouseId = state.Wh1Id,
                    EntityName = "WarehouseCalendarException",
                    EntityId = state.Wh1Id,
                    Action = "MAINTENANCE_WINDOW",
                    OldValues = "2026-12-25 08:00-12:00",
                    NewValues = "2026-12-25 08:00-12:00",
                    Reason = "Bảo trì băng chuyền định kỳ",
                    Result = "Success",
                    Timestamp = DateTime.UtcNow
                });
                await calDb.SaveChangesAsync();

                var duringMaintenance = () => calService.ValidateOperatingWindowAsync(
                    state.Wh1Id,
                    new DateTime(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc));
                await duringMaintenance.Should().ThrowAsync<BusinessRuleException>()
                    .WithMessage("*bảo trì*");

                var outsideMaintenance = await calService.ValidateOperatingWindowAsync(
                    state.Wh1Id,
                    new DateTime(2026, 12, 25, 14, 0, 0, DateTimeKind.Utc));
                outsideMaintenance.Should().BeTrue();
            }

            // =========================================================================
            // STEP 2: Create PurchaseOrder -> Asn -> DockAppointment (Inbound)
            // =========================================================================
            PurchaseOrderDto po;
            AsnDto asn;
            await using (var inbDb = CreateContext())
            {
                var planning = CreateInboundPlanningService(inbDb, state.MakerId);
                po = await planning.CreatePurchaseOrderAsync(new CreatePurchaseOrderDto
                {
                    ExternalPoId = $"EXT-PO-{suffix}",
                    SourceSystem = "ERP",
                    Code = $"PO-{suffix}",
                    SupplierId = state.SupplierId,
                    WarehouseId = state.Wh1Id,
                    OrderDate = DateTime.UtcNow.Date,
                    ExpectedDate = DateTime.UtcNow.Date.AddDays(3),
                    Lines =
                    [
                        new()
                        {
                            ExternalLineId = "10",
                            ProductId = state.LotProductId,
                            OperationUnitId = state.UnitId,
                            OrderedQuantity = 10,
                            AllowedOverReceiptPct = 10 // Max allowed = 11
                        },
                        new()
                        {
                            ExternalLineId = "20",
                            ProductId = state.SerialProductId,
                            OperationUnitId = state.UnitId,
                            OrderedQuantity = 2,
                            AllowedOverReceiptPct = 0 // Max allowed = 2
                        },
                        new()
                        {
                            ExternalLineId = "30",
                            ProductId = state.StandardProductId,
                            OperationUnitId = state.UnitId,
                            OrderedQuantity = 2,
                            AllowedOverReceiptPct = 0 // Max allowed = 2
                        }
                    ]
                });
                po = await planning.OpenPurchaseOrderAsync(po.Id, new InboundStateCommandDto { RowVersion = po.RowVersion! });
                state.PoId = po.Id;

                var poLotLine = po.Lines.Single(x => x.ProductId == state.LotProductId);
                var poSerialLine = po.Lines.Single(x => x.ProductId == state.SerialProductId);
                var poStandardLine = po.Lines.Single(x => x.ProductId == state.StandardProductId);

                asn = await planning.CreateAsnAsync(new CreateAsnDto
                {
                    Code = $"ASN-{suffix}",
                    PurchaseOrderId = po.Id,
                    SupplierId = state.SupplierId,
                    WarehouseId = state.Wh1Id,
                    ExpectedArrivalAtUtc = DateTime.UtcNow.AddHours(2),
                    Lines =
                    [
                        new()
                        {
                            PurchaseOrderLineId = poLotLine.Id,
                            ProductId = state.LotProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 10
                        },
                        new()
                        {
                            PurchaseOrderLineId = poSerialLine.Id,
                            ProductId = state.SerialProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2
                        },
                        new()
                        {
                            PurchaseOrderLineId = poStandardLine.Id,
                            ProductId = state.StandardProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2
                        }
                    ]
                });
                asn = await planning.ConfirmAsnAsync(asn.Id, new InboundStateCommandDto { RowVersion = asn.RowVersion! });
                asn = await planning.MarkAsnInTransitAsync(asn.Id, new InboundStateCommandDto { RowVersion = asn.RowVersion! });
                asn = await planning.ArriveAsnAsync(asn.Id, new InboundStateCommandDto { RowVersion = asn.RowVersion! });
                state.AsnId = asn.Id;
                asn.Status.Should().Be(nameof(AsnStatus.Arrived));
            }

            await using (var dockDb = CreateContext())
            {
                var dock = new Dock
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"DOCK-IN-{suffix}",
                    Name = "Inbound Dock 1",
                    SupportsInbound = true,
                    SupportsOutbound = true,
                    IsActive = true
                };
                dockDb.Docks.Add(dock);
                await dockDb.SaveChangesAsync();
                state.Wh1DockId = dock.Id;

                var dockService = CreateDockYardService(dockDb, state.MakerId);
                var now = DateTimeOffset.UtcNow;
                var appt = await dockService.CreateAppointmentAsync(new UpsertDockAppointmentDto
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"APT-IN-{suffix}",
                    Direction = DockAppointmentDirection.Inbound,
                    AsnId = state.AsnId,
                    PurchaseOrderId = state.PoId,
                    PlannedStart = now.AddMinutes(5),
                    PlannedEnd = now.AddMinutes(65),
                    VehiclePlate = "51C-888.88"
                });
                appt = await dockService.ConfirmAsync(appt.Id, new DockAppointmentCommandDto { RowVersion = appt.RowVersion });
                appt = await dockService.ArriveAsync(appt.Id, new DockAppointmentArrivalDto
                {
                    RowVersion = appt.RowVersion,
                    ArrivedAt = now.AddMinutes(6)
                });
                appt = await dockService.CheckInAsync(appt.Id, new DockAppointmentCheckInDto
                {
                    RowVersion = appt.RowVersion,
                    VehiclePlate = "51C-888.88",
                    DriverName = "Nguyen Van Tai"
                });
                appt = await dockService.AssignDockAsync(appt.Id, new DockAppointmentAssignDockDto
                {
                    RowVersion = appt.RowVersion,
                    DockId = dock.Id
                });
                appt = await dockService.StartServiceAsync(appt.Id, new DockAppointmentCommandDto { RowVersion = appt.RowVersion });
                appt.Status.Should().Be(DockAppointmentStatus.InService);
            }

            // =========================================================================
            // STEP 3: Inbound Over-Tolerance Rejection (W5-07/W6-03), ASN Restoration
            //         on Cancel (W5-25), Valid Receipt Post, and Putaway Move (W5-01)
            // =========================================================================
            var asnLotLine = asn.Lines.Single(x => x.ProductId == state.LotProductId);
            var asnSerialLine = asn.Lines.Single(x => x.ProductId == state.SerialProductId);
            var asnStandardLine = asn.Lines.Single(x => x.ProductId == state.StandardProductId);

            int overReceiptId;
            await using (var irOverDb = CreateContext())
            {
                var irService = CreateImportReceiptService(irOverDb, state.MakerId);
                var overReceipt = await irService.CreateAsync(new CreateImportReceiptDto
                {
                    Code = $"IR-OVER-{suffix}",
                    WarehouseId = state.Wh1Id,
                    SupplierId = state.SupplierId,
                    PurchaseOrderId = state.PoId,
                    AsnId = state.AsnId,
                    Details =
                    [
                        new()
                        {
                            AsnLineId = asnLotLine.Id,
                            PurchaseOrderLineId = asnLotLine.PurchaseOrderLineId,
                            ProductId = state.LotProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 10,
                            UnitPrice = 100
                        },
                        new()
                        {
                            AsnLineId = asnSerialLine.Id,
                            PurchaseOrderLineId = asnSerialLine.PurchaseOrderLineId,
                            ProductId = state.SerialProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2,
                            UnitPrice = 200
                        },
                        new()
                        {
                            AsnLineId = asnStandardLine.Id,
                            PurchaseOrderLineId = asnStandardLine.PurchaseOrderLineId,
                            ProductId = state.StandardProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2,
                            UnitPrice = 50
                        }
                    ]
                }, state.MakerId);
                overReceiptId = overReceipt.Id;

                var overLotLineId = overReceipt.Details.Single(x => x.ProductId == state.LotProductId).Id;
                var overSerialLineId = overReceipt.Details.Single(x => x.ProductId == state.SerialProductId).Id;
                var overStdLineId = overReceipt.Details.Single(x => x.ProductId == state.StandardProductId).Id;

                await irService.ReceiveAsync(overReceiptId, new ReceiveImportReceiptDto
                {
                    Lines =
                    [
                        new() { LineId = overLotLineId, ReceivedQuantity = 10, AcceptedQuantity = 0 },
                        new() { LineId = overSerialLineId, ReceivedQuantity = 2, AcceptedQuantity = 2 },
                        new() { LineId = overStdLineId, ReceivedQuantity = 2, AcceptedQuantity = 2 }
                    ]
                }, state.MakerId);

                // ASN is now in Receiving status
                (await irOverDb.Asns.AsNoTracking().SingleAsync(x => x.Id == state.AsnId))
                    .Status.Should().Be(AsnStatus.Receiving);

                await irService.RecordQcDispositionAsync(overReceiptId, new RecordQcDispositionDto
                {
                    Lines =
                    [
                        new() { LineId = overLotLineId, AcceptedQuantity = 10, DamagedQuantity = 0, RejectedQuantity = 0 }
                    ]
                }, state.MakerId);

                // Simulate an over-tolerance AcceptedQuantity (12 > max 11) before ApproveImportReceiptAsync
                await irOverDb.ImportReceiptDetails
                    .Where(x => x.Id == overLotLineId)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.AcceptedQuantity, 12m)
                        .SetProperty(x => x.BaseAcceptedQuantity, 12m));
            }

            // W5-07 / W6-03: ApproveImportReceiptAsync must reject over-tolerance with 409 BEFORE ReadyToPost
            await using (var approveOverDb = CreateContext())
            {
                var checkerIrService = CreateImportReceiptService(approveOverDb, state.CheckerId);
                var act = () => checkerIrService.ApproveImportReceiptAsync(overReceiptId, state.CheckerId);
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>()
                    .WithMessage("*vượt dung sai*");
                thrown.Which.Data["HttpStatusCode"].Should().Be(409);

                var unchangedReceipt = await approveOverDb.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == overReceiptId);
                unchangedReceipt.Status.Should().Be(ReceiptStatus.QcCompleted);
            }

            // W5-25: Cancelling the unposted receipt restores ASN from Receiving back to Arrived
            await using (var cancelOverDb = CreateContext())
            {
                var makerIrService = CreateImportReceiptService(cancelOverDb, state.MakerId);
                await makerIrService.CancelAsync(overReceiptId, state.MakerId);

                (await cancelOverDb.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == overReceiptId))
                    .Status.Should().Be(ReceiptStatus.Cancelled);
                (await cancelOverDb.Asns.AsNoTracking().SingleAsync(x => x.Id == state.AsnId))
                    .Status.Should().Be(AsnStatus.Arrived);
            }

            // Now create, receive, QC, configure Lot/Serial identities, approve, and post the valid ImportReceipt
            int validReceiptId;
            await using (var irValidDb = CreateContext())
            {
                var irService = CreateImportReceiptService(irValidDb, state.MakerId);
                var validReceipt = await irService.CreateAsync(new CreateImportReceiptDto
                {
                    Code = $"IR-VALID-{suffix}",
                    WarehouseId = state.Wh1Id,
                    SupplierId = state.SupplierId,
                    PurchaseOrderId = state.PoId,
                    AsnId = state.AsnId,
                    Details =
                    [
                        new()
                        {
                            AsnLineId = asnLotLine.Id,
                            PurchaseOrderLineId = asnLotLine.PurchaseOrderLineId,
                            ProductId = state.LotProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 10,
                            UnitPrice = 100
                        },
                        new()
                        {
                            AsnLineId = asnSerialLine.Id,
                            PurchaseOrderLineId = asnSerialLine.PurchaseOrderLineId,
                            ProductId = state.SerialProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2,
                            UnitPrice = 200
                        },
                        new()
                        {
                            AsnLineId = asnStandardLine.Id,
                            PurchaseOrderLineId = asnStandardLine.PurchaseOrderLineId,
                            ProductId = state.StandardProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 2,
                            UnitPrice = 50
                        }
                    ]
                }, state.MakerId);
                validReceiptId = validReceipt.Id;

                var lotLineId = validReceipt.Details.Single(x => x.ProductId == state.LotProductId).Id;
                var serialLineId = validReceipt.Details.Single(x => x.ProductId == state.SerialProductId).Id;
                var stdLineId = validReceipt.Details.Single(x => x.ProductId == state.StandardProductId).Id;

                await irService.ReceiveAsync(validReceiptId, new ReceiveImportReceiptDto
                {
                    Lines =
                    [
                        new() { LineId = lotLineId, ReceivedQuantity = 10, AcceptedQuantity = 0 },
                        new() { LineId = serialLineId, ReceivedQuantity = 2, AcceptedQuantity = 2 },
                        new() { LineId = stdLineId, ReceivedQuantity = 2, AcceptedQuantity = 2 }
                    ]
                }, state.MakerId);

                await irService.RecordQcDispositionAsync(validReceiptId, new RecordQcDispositionDto
                {
                    Lines =
                    [
                        new() { LineId = lotLineId, AcceptedQuantity = 10, DamagedQuantity = 0, RejectedQuantity = 0 }
                    ]
                }, state.MakerId);

                await irService.SetInventoryIdentitiesAsync(validReceiptId, new SetImportReceiptInventoryIdentitiesDto
                {
                    Lines =
                    [
                        new()
                        {
                            LineId = lotLineId,
                            TargetStatus = nameof(InventoryStatus.Available),
                            BaseQuantity = 6,
                            LotNumber = $"LOT-FEFO1-{suffix}",
                            ManufactureDate = DateTime.UtcNow.Date.AddDays(-10),
                            ExpiryDate = DateTime.UtcNow.Date.AddDays(60)
                        },
                        new()
                        {
                            LineId = lotLineId,
                            TargetStatus = nameof(InventoryStatus.Available),
                            BaseQuantity = 4,
                            LotNumber = $"LOT-FEFO2-{suffix}",
                            ManufactureDate = DateTime.UtcNow.Date.AddDays(-5),
                            ExpiryDate = DateTime.UtcNow.Date.AddDays(90)
                        },
                        new()
                        {
                            LineId = serialLineId,
                            TargetStatus = nameof(InventoryStatus.Available),
                            BaseQuantity = 1,
                            SerialNumber = $"SER-01-{suffix}"
                        },
                        new()
                        {
                            LineId = serialLineId,
                            TargetStatus = nameof(InventoryStatus.Available),
                            BaseQuantity = 1,
                            SerialNumber = $"SER-02-{suffix}"
                        }
                    ]
                }, state.MakerId);
            }

            await using (var postDb = CreateContext())
            {
                var checkerIrService = CreateImportReceiptService(postDb, state.CheckerId);
                await checkerIrService.ApproveImportReceiptAsync(validReceiptId, state.CheckerId);
                await checkerIrService.PostAsync(validReceiptId, state.CheckerId);

                (await postDb.Asns.AsNoTracking().SingleAsync(x => x.Id == state.AsnId))
                    .Status.Should().Be(AsnStatus.Completed);
                (await postDb.PurchaseOrders.AsNoTracking().SingleAsync(x => x.Id == state.PoId))
                    .Status.Should().Be(PurchaseOrderStatus.Received);
            }

            // Execute Putaway from RECEIVING -> WH1-BIN-A and WH1-BIN-B (verifying W5-01 identity matching)
            await using (var putDb = CreateContext())
            {
                var putaway = CreatePutawayService(putDb, state.MakerId);
                var taskSummary = (await putaway.ListAsync()).Single(x => x.ReceiptId == validReceiptId);
                var task = await putaway.GetAsync(taskSummary.Id);
                task = await putaway.AssignAsync(task.Id, new PutawayStateCommandDto
                {
                    AssignedUserId = state.MakerId,
                    RowVersion = task.RowVersion!
                });
                task = await putaway.StartAsync(task.Id, new PutawayStateCommandDto { RowVersion = task.RowVersion! });

                var lotItem = task.Items.Single(x => x.ProductId == state.LotProductId);
                var serialItem = task.Items.Single(x => x.ProductId == state.SerialProductId);
                var stdItem = task.Items.Single(x => x.ProductId == state.StandardProductId);

                // Move 6 units of LotProduct (LOT-FEFO1) to Bin A, 4 units (LOT-FEFO2) to Bin B
                task = await putaway.MoveAsync(task.Id, new MovePutawayItemDto
                {
                    ItemId = lotItem.Id,
                    DestinationLocationId = state.Wh1BinAId,
                    Quantity = 6,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });
                task = await putaway.MoveAsync(task.Id, new MovePutawayItemDto
                {
                    ItemId = lotItem.Id,
                    DestinationLocationId = state.Wh1BinBId,
                    Quantity = 4,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });

                // Move 2 units of SerialProduct to Bin A
                task = await putaway.MoveAsync(task.Id, new MovePutawayItemDto
                {
                    ItemId = serialItem.Id,
                    DestinationLocationId = state.Wh1BinAId,
                    Quantity = 2,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });

                // Move 2 units of StandardProduct to Bin A
                task = await putaway.MoveAsync(task.Id, new MovePutawayItemDto
                {
                    ItemId = stdItem.Id,
                    DestinationLocationId = state.Wh1BinAId,
                    Quantity = 2,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });

                task.Status.Should().Be(nameof(PutawayTaskStatus.Completed));
            }

            // =========================================================================
            // STEP 4: SalesOrder -> Reservation -> Allocation -> Backorder Reallocation ->
            //         Short Pick -> Alternative Bin Redirect (W5-02) -> Complete Picking
            // =========================================================================
            SalesOrderDto salesOrder;
            await using (var soDb = CreateContext())
            {
                var demand = CreateDemandService(soDb, state.MakerId);
                salesOrder = await demand.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"SO-EXT-{suffix}",
                    CustomerId = state.CustomerId,
                    WarehouseId = state.Wh1Id,
                    Priority = 10,
                    Lines =
                    [
                        new()
                        {
                            ExternalLineId = "1",
                            ProductId = state.LotProductId,
                            OrderedQuantity = 12
                        }
                    ]
                });
                salesOrder = await demand.ReleaseSalesOrderAsync(salesOrder.Id, new()
                {
                    RowVersion = salesOrder.RowVersion
                });
                state.SalesOrderId = salesOrder.Id;

                salesOrder.Status.Should().Be(nameof(SalesOrderStatus.Released));
                salesOrder.ReservedQuantity.Should().Be(10);
                salesOrder.AllocatedQuantity.Should().Be(10);
                salesOrder.BackorderQuantity.Should().Be(2);
            }

            // Post a supplemental receipt of 3 units of LOT-FEFO2 and putaway into WH1-BIN-B
            // so WH1-BIN-B has 4 (reserved) + 3 (unreserved) = 7 units total
            await ReceiveAndPutawaySupplementalLotStockAsync(state, 3m);

            // Reallocate 1 unit of the open Backorder before picking starts (leaving 2 unreserved units in WH1-BIN-B)
            await using (var reallocDb = CreateContext())
            {
                var backorderRow = await reallocDb.Backorders.AsNoTracking()
                    .SingleAsync(x => x.SalesOrderLine.SalesOrderId == state.SalesOrderId);
                var demand = CreateDemandService(reallocDb, state.MakerId);
                var recovered = await demand.ReallocateBackorderAsync(backorderRow.Id, new BackorderReallocateDto
                {
                    Quantity = 1,
                    RowVersion = Convert.ToBase64String(backorderRow.RowVersion)
                });
                recovered.RecoveredQuantity.Should().Be(1);
                recovered.RemainingQuantity.Should().Be(1);
            }

            // Start PickingTask, pick 4 of 6 at WH1-BIN-A, report ShortPick for 2, and redirect to WH1-BIN-B (W5-02)
            int pickingTaskId;
            await using (var pickDb = CreateContext())
            {
                var picking = CreatePickingService(pickDb, state.MakerId);
                var taskSummary = (await picking.ListAsync(state.Wh1Id)).Single();
                pickingTaskId = taskSummary.Id;
                var detail = await picking.GetAsync(pickingTaskId);

                var assigned = await picking.AssignAsync(pickingTaskId, new PickingStateCommandDto
                {
                    AssignedUserId = state.MakerId,
                    RowVersion = detail.RowVersion!
                });
                var started = await picking.StartAsync(pickingTaskId, new PickingStateCommandDto
                {
                    RowVersion = assigned.RowVersion!
                });

                var binALine = started.Lines.Single(x => x.SourceLocationId == state.Wh1BinAId);
                var afterPartialPick = await picking.PickAsync(pickingTaskId, new PickScanDto
                {
                    TaskLineId = binALine.Id,
                    LocationBarcode = state.Wh1BinACode,
                    ProductBarcode = state.LotProductCode,
                    LotNumber = binALine.LotNumber,
                    Quantity = 4,
                    RowVersion = started.RowVersion!
                });

                var afterShortPick = await picking.ReportShortPickAsync(pickingTaskId, new ReportShortPickDto
                {
                    TaskLineId = binALine.Id,
                    ActualPickedQuantity = 4,
                    Reason = "DAMAGED_AT_BIN",
                    RowVersion = afterPartialPick.RowVersion!
                });
                afterShortPick.Status.Should().Be(nameof(PickingTaskStatus.ShortPick));
                var openExceptionId = afterShortPick.ShortPicks
                    .Single(x => x.Status == nameof(ShortPickExceptionStatus.Open)).Id;

                var afterRedirect = await picking.ResolveShortPickAsync(pickingTaskId, openExceptionId, new ResolveShortPickDto
                {
                    Resolution = nameof(ShortPickResolutionType.AlternativeLocation),
                    Note = "Chuyển hướng 2 đơn vị thiếu sang Bin B",
                    RowVersion = afterShortPick.RowVersion!
                });
                afterRedirect.Status.Should().Be(nameof(PickingTaskStatus.InProgress));
            }

            // Verify W5-02 invariant: Bin A ReservedQuantity dropped from 6 to 4; Bin B ReservedQuantity increased from 5 to 7
            await using (var verifyShortPickDb = CreateContext())
            {
                var binAStock = await verifyShortPickDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == state.Wh1Id && x.ProductId == state.LotProductId && x.LocationId == state.Wh1BinAId);
                binAStock.Quantity.Should().Be(6);
                binAStock.ReservedQuantity.Should().Be(4);

                var binBStock = await verifyShortPickDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == state.Wh1Id && x.ProductId == state.LotProductId && x.LocationId == state.Wh1BinBId);
                binBStock.Quantity.Should().Be(7);
                binBStock.ReservedQuantity.Should().Be(7);
            }

            // Complete picking all remaining open lines at Bin B (4 + 1 + 2 = 7 units at Bin B; 11 units total)
            await using (var finishPickDb = CreateContext())
            {
                var picking = CreatePickingService(finishPickDb, state.MakerId, withPacking: true);
                var current = await picking.GetAsync(pickingTaskId);
                foreach (var line in current.Lines.Where(x => x.RemainingQuantity > 0 && x.Status is "Open" or "InProgress").ToList())
                {
                    current = await picking.PickAsync(pickingTaskId, new PickScanDto
                    {
                        TaskLineId = line.Id,
                        LocationBarcode = state.Wh1BinBCode,
                        ProductBarcode = state.LotProductCode,
                        LotNumber = line.LotNumber,
                        Quantity = line.RemainingQuantity,
                        RowVersion = current.RowVersion!
                    });
                }

                var completedPick = await picking.CompleteAsync(pickingTaskId, new PickingStateCommandDto
                {
                    RowVersion = current.RowVersion!
                });
                completedPick.Status.Should().Be(nameof(PickingTaskStatus.Completed));
                completedPick.PickedQuantity.Should().Be(11);
            }

            // =========================================================================
            // STEP 5: Packing (HandlingUnit with SSCC) -> Shipment (Stage, Load, Dispatch)
            // =========================================================================
            int packingSessionId;
            string huBarcode = $"HU-W7-{suffix}";
            await using (var packDb = CreateContext())
            {
                var pickLotByLineId = await packDb.PickingTaskLines.AsNoTracking()
                    .Include(x => x.Allocation)
                        .ThenInclude(a => a.Lot)
                    .Where(x => x.PickingTaskId == pickingTaskId)
                    .ToDictionaryAsync(x => x.Id, x => x.Allocation.Lot != null ? x.Allocation.Lot.LotNumber : null);

                var packing = CreatePackingService(packDb, state.MakerId, withShipment: true);
                var sessionSummary = (await packing.ListAsync(state.Wh1Id)).Single();
                packingSessionId = sessionSummary.Id;
                var session = await packing.GetAsync(packingSessionId);

                session = await packing.CreateHandlingUnitAsync(packingSessionId, new CreateHandlingUnitDto
                {
                    Type = "Pallet",
                    HuCode = huBarcode,
                    Barcode = huBarcode,
                    Sscc = $"0034567890{suffix[..8]}",
                    RowVersion = session.RowVersion!
                });
                var hu = session.HandlingUnits.Single();

                foreach (var packLine in session.Lines.Where(x => x.RemainingQuantity > 0).ToList())
                {
                    session = await packing.PackAsync(packingSessionId, new PackIntoHandlingUnitDto
                    {
                        HandlingUnitId = hu.Id,
                        PickingTaskLineId = packLine.PickingTaskLineId,
                        ProductBarcode = state.LotProductCode,
                        LotNumber = pickLotByLineId[packLine.PickingTaskLineId],
                        Quantity = packLine.RemainingQuantity,
                        RowVersion = session.RowVersion!
                    });
                }

                session = await packing.CloseHandlingUnitAsync(packingSessionId, hu.Id, new PackingStateCommandDto
                {
                    RowVersion = session.RowVersion!
                });
                session = await packing.CompleteAsync(packingSessionId, new PackingStateCommandDto
                {
                    RowVersion = session.RowVersion!
                });
                session.Status.Should().Be(nameof(PackingSessionStatus.Packed));
                session.PackedQuantity.Should().Be(11);
            }

            await using (var shipDb = CreateContext())
            {
                var now = DateTime.UtcNow;
                var outAppt = new DockAppointment
                {
                    WarehouseId = state.Wh1Id,
                    Code = $"APT-OUT-{suffix}",
                    Direction = DockAppointmentDirection.Outbound,
                    Status = DockAppointmentStatus.InService,
                    PlannedStartUtc = now.AddMinutes(-20),
                    PlannedEndUtc = now.AddHours(1),
                    VehiclePlate = "51D-999.99",
                    DockId = state.Wh1DockId,
                    DockAssignedAtUtc = now.AddMinutes(-15),
                    ServiceStartedAtUtc = now.AddMinutes(-10),
                    CreatedBy = state.MakerId,
                    CreatedAtUtc = now.AddMinutes(-30),
                    UpdatedBy = state.MakerId,
                    UpdatedAtUtc = now
                };
                shipDb.DockAppointments.Add(outAppt);
                await shipDb.SaveChangesAsync();

                var shipmentService = CreateShipmentService(shipDb, state.MakerId);
                var shipmentSummary = (await shipmentService.ListAsync(state.Wh1Id)).Single();
                var shipment = await shipmentService.GetAsync(shipmentSummary.Id);

                shipment = await shipmentService.StageAsync(shipment.Id, new StageShipmentDto
                {
                    StagingLocationCode = state.Wh1StagingCode,
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.StartLoadingAsync(shipment.Id, new StartShipmentLoadingDto
                {
                    DockAppointmentId = outAppt.Id,
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.LoadHandlingUnitAsync(shipment.Id, new LoadShipmentHandlingUnitDto
                {
                    HandlingUnitBarcode = huBarcode,
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.CompleteLoadingAsync(shipment.Id, new CompleteShipmentLoadingDto
                {
                    SealNumber = $"SEAL-{suffix}",
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.DispatchAsync(shipment.Id, new ShipmentStateCommandDto
                {
                    RowVersion = shipment.RowVersion!
                });

                shipment.Status.Should().Be(nameof(ShipmentStatus.Dispatched));
            }

            // =========================================================================
            // STEP 6: StockTransfer WH1 -> WH2 with DamagedQuantity > 0 (W6-04 & W7-02)
            // =========================================================================
            await using (var tfDb = CreateContext())
            {
                var stdStockWh1 = await tfDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == state.Wh1Id && x.ProductId == state.StandardProductId && x.LocationId == state.Wh1BinAId);
                stdStockWh1.Quantity.Should().Be(2);
                stdStockWh1.ReservedQuantity.Should().Be(0);

                var makerTransfer = CreateStockTransferService(tfDb, state.MakerId);
                var createdTf = await makerTransfer.CreateAsync(new CreateStockTransferDto
                {
                    SourceWarehouseId = state.Wh1Id,
                    DestinationWarehouseId = state.Wh2Id,
                    Note = "Chuyển kho WH1 -> WH2 kiểm nghiệm W6-04/W7-02",
                    Details =
                    [
                        new()
                        {
                            ProductId = state.StandardProductId,
                            Quantity = 2
                        }
                    ]
                });
                state.StockTransferId = createdTf.Id;

                var checkerTransfer = CreateStockTransferService(tfDb, state.CheckerId);
                await checkerTransfer.ApproveAsync(createdTf.Id);
                await checkerTransfer.DispatchAsync(createdTf.Id);
                await checkerTransfer.ReceiveAsync(createdTf.Id, new ReceiveStockTransferDto
                {
                    Details =
                    [
                        new()
                        {
                            ProductId = state.StandardProductId,
                            ReceivedQuantity = 1,
                            DamagedQuantity = 1,
                            MissingQuantity = 0
                        }
                    ]
                });
                await checkerTransfer.CompleteAsync(createdTf.Id);

                var wh2Stocks = await tfDb.InventoryStocks.AsNoTracking()
                    .Include(x => x.Location)
                    .Where(x => x.WarehouseId == state.Wh2Id && x.ProductId == state.StandardProductId)
                    .ToListAsync();
                wh2Stocks.Should().HaveCount(2);

                var wh2Good = wh2Stocks.Single(x => x.Status == InventoryStatus.Available);
                wh2Good.Quantity.Should().Be(1);
                wh2Good.LocationId.Should().Be(state.Wh2LegacyLocationId);

                var wh2Damaged = wh2Stocks.Single(x => x.Status == InventoryStatus.Damaged);
                wh2Damaged.Quantity.Should().Be(1);
                wh2Damaged.LocationId.Should().Be(state.Wh2DamagedLocationId);
                wh2Damaged.Location!.Code.Should().Be("DAMAGED");
                wh2Damaged.Location.IsPickable.Should().BeFalse();
                wh2Damaged.Location.IsSystemManaged.Should().BeTrue();
            }

            // =========================================================================
            // STEP 7: [BLIND] Stocktake at WH2 (W6-02 Masking + Variance Adjustment)
            // =========================================================================
            int stocktakeId;
            int stocktakeDetailId;
            await using (var stDb = CreateContext())
            {
                var makerStocktake = CreateStocktakeService(stDb, state.MakerId);
                stocktakeId = await makerStocktake.CreateStocktakeAsync(new CreateStocktakeDto
                {
                    WarehouseId = state.Wh2Id,
                    IsBlindCount = true,
                    Note = "Kiểm kê mù định kỳ WH2"
                }, state.MakerId);
                state.StocktakeId = stocktakeId;

                var queryService = new StocktakeQueryService(
                    stDb,
                    new WarehouseAuthorizationService(stDb, new CurrentUser(state.MakerId)));
                var blindDto = await queryService.GetStocktakeByIdAsync(stocktakeId);
                blindDto.Note.Should().StartWith("[BLIND]");
                blindDto.Details.Should().ContainSingle();
                blindDto.Details.Single().SystemQuantity.Should().Be(0m, "W6-02: Blind count must mask SystemQuantity in StocktakeQueryService");
                stocktakeDetailId = blindDto.Details.Single().Id;

                var approvalWorkflow = new ApprovalWorkflowService(
                    stDb,
                    new CurrentUser(state.CheckerId),
                    new WarehouseAuthorizationService(stDb, new CurrentUser(state.CheckerId)),
                    new Moq.Mock<IRequestMetadata>().Object);
                var queue = await approvalWorkflow.GetQueueAsync(new ApprovalQueueQuery
                {
                    DocumentType = "Stocktake",
                    WarehouseId = state.Wh2Id,
                    PageIndex = 1,
                    PageSize = 20
                });
                var queueItem = queue.Items.Single(x => x.DocumentId == stocktakeId);
                queueItem.TotalQuantity.Should().Be(0m, "W6-02: Blind count must not leak SystemQuantity via ApprovalWorkflowService");

                // Count 3 units (actual = 3 vs system = 1 -> +2 AdjustmentIncrease)
                await makerStocktake.UpdateStocktakeDetailAsync(stocktakeId, stocktakeDetailId, new UpdateStocktakeDetailDto
                {
                    ActualQuantity = 3,
                    Note = "Kiểm đếm thực tế phát hiện thêm 2 đơn vị"
                });
            }

            await using (var approveStDb = CreateContext())
            {
                var checkerStocktake = CreateStocktakeService(approveStDb, state.CheckerId);
                await checkerStocktake.ApproveStocktakeAsync(stocktakeId, state.CheckerId);

                var approved = await approveStDb.Stocktakes.AsNoTracking()
                    .Include(x => x.Details)
                    .SingleAsync(x => x.Id == stocktakeId);
                approved.Status.Should().Be(ReceiptStatus.Approved);
                approved.Details.Single().DifferenceQuantity.Should().Be(2m);
            }

            // =========================================================================
            // STEP 8: Global Ledger Conservation Equations & Non-Negative/Location Invariants
            // =========================================================================
            await using (var auditDb = CreateContext())
            {
                var warehouseIds = new[] { state.Wh1Id, state.Wh2Id };
                var productIds = new[] { state.LotProductId, state.SerialProductId, state.StandardProductId };

                var allStocks = await auditDb.InventoryStocks.AsNoTracking()
                    .Include(x => x.Location)
                    .Where(x => warehouseIds.Contains(x.WarehouseId))
                    .ToListAsync();

                var allTxs = await auditDb.InventoryTransactions.AsNoTracking()
                    .Where(x => warehouseIds.Contains(x.WarehouseId))
                    .ToListAsync();

                // Invariant 8.1: No negative stock, no negative reservation, no ReservedQuantity > Quantity
                allStocks.Should().OnlyContain(s => s.Quantity >= 0m, "No InventoryStock row may have Quantity < 0");
                allStocks.Should().OnlyContain(s => s.ReservedQuantity >= 0m, "No InventoryStock row may have ReservedQuantity < 0");
                allStocks.Should().OnlyContain(s => s.ReservedQuantity <= s.Quantity, "No InventoryStock row may have ReservedQuantity > Quantity");

                // Invariant 8.2: Damaged stock must NEVER reside in a pickable location
                var damagedStocks = allStocks.Where(s => s.Status == InventoryStatus.Damaged && s.Quantity > 0).ToList();
                damagedStocks.Should().NotBeEmpty();
                damagedStocks.Should().OnlyContain(s => s.Location != null && !s.Location.IsPickable,
                    "Damaged inventory must strictly reside in non-pickable locations (DAMAGED)");

                // Invariant 8.3: Exact Ledger Conservation per (WarehouseId, ProductId)
                foreach (var whId in warehouseIds)
                {
                    foreach (var prodId in productIds)
                    {
                        var onHandSum = allStocks
                            .Where(s => s.WarehouseId == whId && s.ProductId == prodId)
                            .Sum(s => s.Quantity);

                        var netLedger = allTxs
                            .Where(t => t.WarehouseId == whId && t.ProductId == prodId)
                            .Sum(t => t.TransactionType switch
                            {
                                TransactionType.Import => t.Quantity,
                                TransactionType.TransferIn => t.Quantity,
                                TransactionType.AdjustmentIncrease => t.Quantity,
                                TransactionType.Export => -t.Quantity,
                                TransactionType.Ship => -t.Quantity,
                                TransactionType.TransferOut => -t.Quantity,
                                TransactionType.AdjustmentDecrease => -t.Quantity,
                                _ => 0m
                            });

                        onHandSum.Should().Be(netLedger,
                            $"Conservation equation failed for WarehouseId={whId}, ProductId={prodId}");
                    }
                }

                // Spot-check expected final quantities across WH1 and WH2:
                // WH1 LotProduct: Imported (10 + 3 = 13) - Shipped (11) = 2 remaining at WH1-BIN-A
                allStocks.Where(s => s.WarehouseId == state.Wh1Id && s.ProductId == state.LotProductId)
                    .Sum(s => s.Quantity).Should().Be(2m);
                // WH1 SerialProduct: Imported (2) = 2
                allStocks.Where(s => s.WarehouseId == state.Wh1Id && s.ProductId == state.SerialProductId)
                    .Sum(s => s.Quantity).Should().Be(2m);
                // WH1 StandardProduct: Imported (2) - TransferOut (2) = 0
                allStocks.Where(s => s.WarehouseId == state.Wh1Id && s.ProductId == state.StandardProductId)
                    .Sum(s => s.Quantity).Should().Be(0m);
                // WH2 StandardProduct: TransferIn (1 Available + 1 Damaged = 2) + Stocktake AdjustmentIncrease (2) = 4
                allStocks.Where(s => s.WarehouseId == state.Wh2Id && s.ProductId == state.StandardProductId)
                    .Sum(s => s.Quantity).Should().Be(4m);
            }
        }
        finally
        {
            await CleanupAsync(state);
        }
    }

    private static async Task ReceiveAndPutawaySupplementalLotStockAsync(LifecycleState state, decimal quantity)
    {
        int receiptId;
        await using (var db = CreateContext())
        {
            var service = CreateImportReceiptService(db, state.MakerId);
            var created = await service.CreateAsync(new CreateImportReceiptDto
            {
                Code = $"IR-SUPP-{state.Suffix}",
                WarehouseId = state.Wh1Id,
                SupplierId = state.SupplierId,
                Details =
                [
                    new()
                    {
                        ProductId = state.LotProductId,
                        OperationUnitId = state.UnitId,
                        ExpectedQuantity = quantity,
                        Quantity = quantity,
                        UnitPrice = 100
                    }
                ]
            }, state.MakerId);
            receiptId = created.Id;
            var lineId = created.Details.Single().Id;

            await service.ReceiveAsync(receiptId, new ReceiveImportReceiptDto
            {
                Lines = [new() { LineId = lineId, ReceivedQuantity = quantity, AcceptedQuantity = 0 }]
            }, state.MakerId);

            await service.RecordQcDispositionAsync(receiptId, new RecordQcDispositionDto
            {
                Lines = [new() { LineId = lineId, AcceptedQuantity = quantity, DamagedQuantity = 0, RejectedQuantity = 0 }]
            }, state.MakerId);

            await service.SetInventoryIdentitiesAsync(receiptId, new SetImportReceiptInventoryIdentitiesDto
            {
                Lines =
                [
                    new()
                    {
                        LineId = lineId,
                        TargetStatus = nameof(InventoryStatus.Available),
                        BaseQuantity = quantity,
                        LotNumber = $"LOT-FEFO2-{state.Suffix}",
                        ManufactureDate = DateTime.UtcNow.Date.AddDays(-5),
                        ExpiryDate = DateTime.UtcNow.Date.AddDays(90)
                    }
                ]
            }, state.MakerId);
        }

        await using (var postDb = CreateContext())
        {
            var checkerService = CreateImportReceiptService(postDb, state.CheckerId);
            await checkerService.ApproveImportReceiptAsync(receiptId, state.CheckerId);
            await checkerService.PostAsync(receiptId, state.CheckerId);
        }

        await using (var putDb = CreateContext())
        {
            var putaway = CreatePutawayService(putDb, state.MakerId);
            var summary = (await putaway.ListAsync()).Single(x => x.ReceiptId == receiptId);
            var task = await putaway.GetAsync(summary.Id);
            task = await putaway.AssignAsync(task.Id, new PutawayStateCommandDto
            {
                AssignedUserId = state.MakerId,
                RowVersion = task.RowVersion!
            });
            task = await putaway.StartAsync(task.Id, new PutawayStateCommandDto { RowVersion = task.RowVersion! });
            var item = task.Items.Single();
            await putaway.MoveAsync(task.Id, new MovePutawayItemDto
            {
                ItemId = item.Id,
                DestinationLocationId = state.Wh1BinBId,
                Quantity = quantity,
                UnitCode = state.UnitCode,
                RowVersion = task.RowVersion!
            });
        }
    }

    private static WarehouseService CreateWarehouseService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new WarehouseService(
            new WarehouseRepository(db),
            new WarehouseAuthorizationService(db, current),
            new InventoryStockRepository(db));
    }

    private static WarehouseCalendarService CreateCalendarService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new WarehouseCalendarService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            TimeProvider.System);
    }

    private static InboundPlanningService CreateInboundPlanningService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new InboundPlanningService(
            db,
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static DockYardService CreateDockYardService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new DockYardService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            TimeProvider.System);
    }

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new PutawayService(
            db,
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static ImportReceiptService CreateImportReceiptService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var auth = new WarehouseAuthorizationService(db, current);
        var putaway = new PutawayService(db, auth, current);
        var inboundPlanning = new ReceiptInboundPlanningIntegration(db);
        var identity = new ReceiptInventoryIdentityService(db, auth);
        var permissions = new Moq.Mock<IUserRepository>();
        permissions
            .Setup(x => x.HasPermissionAsync(Moq.It.IsAny<int>(), Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));
        return new ImportReceiptService(
            new ImportReceiptRepository(db),
            new InventoryStockRepository(db),
            new InventoryTransactionRepository(db),
            new WarehouseRepository(db),
            new ProductRepository(db),
            new UnitOfWork(db),
            new AuditLogRepository(db),
            auth,
            current,
            permissions.Object,
            putaway,
            inboundPlanning,
            identity);
    }

    private static SalesOrderBackorderService CreateDemandService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
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
        var auth = new WarehouseAuthorizationService(db, current);
        return new ShipmentService(
            db,
            auth,
            current,
            new StockReservationService(
                db,
                new InventoryStockRepository(db),
                new UnitOfWork(db),
                auth,
                current,
                new StockReservationOptions()));
    }

    private static StockTransferService CreateStockTransferService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StockTransferService(
            db,
            new InventoryStockRepository(db),
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static StocktakeService CreateStocktakeService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StocktakeService(
            new StocktakeRepository(db),
            new InventoryStockRepository(db),
            new InventoryTransactionRepository(db),
            new WarehouseRepository(db),
            new UnitOfWork(db),
            new AuditLogRepository(db),
            new WarehouseAuthorizationService(db, current),
            current,
            null,
            new ProductRepository(db));
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private static async Task CleanupAsync(LifecycleState state)
    {
        await using var db = CreateContext();
        var whIds = new[] { state.Wh1Id, state.Wh2Id }.Where(x => x > 0).ToArray();
        var userIds = new[] { state.MakerId, state.CheckerId }.Where(x => x > 0).ToArray();
        var prodIds = new[] { state.LotProductId, state.SerialProductId, state.StandardProductId }.Where(x => x > 0).ToArray();

        if (userIds.Length > 0 || whIds.Length > 0)
        {
            await db.AuditLogs
                .Where(x => (x.UserId.HasValue && userIds.Contains(x.UserId.Value)) ||
                            (x.WarehouseId.HasValue && whIds.Contains(x.WarehouseId.Value)))
                .ExecuteDeleteAsync();
        }

        if (whIds.Length > 0)
        {
            await db.StocktakeDetails.Where(x => whIds.Contains(x.Stocktake.WarehouseId)).ExecuteDeleteAsync();
            await db.Stocktakes.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.StockTransferDetails.Where(x => whIds.Contains(x.StockTransfer.SourceWarehouseId) || whIds.Contains(x.StockTransfer.DestinationWarehouseId)).ExecuteDeleteAsync();
            await db.StockTransfers.Where(x => whIds.Contains(x.SourceWarehouseId) || whIds.Contains(x.DestinationWarehouseId)).ExecuteDeleteAsync();

            await db.ShipmentProofOfDeliveries.Where(x => whIds.Contains(x.Shipment.WarehouseId)).ExecuteDeleteAsync();
            await db.ShipmentTrackingEvents.Where(x => whIds.Contains(x.Shipment.WarehouseId)).ExecuteDeleteAsync();
            await db.ShipmentHandlingUnits.Where(x => whIds.Contains(x.Shipment.WarehouseId)).ExecuteDeleteAsync();
            await db.Shipments.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.HandlingUnitContents.Where(x => whIds.Contains(x.HandlingUnit.WarehouseId)).ExecuteDeleteAsync();
            await db.HandlingUnits.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.PackingSessions.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.ShortPickExceptions.Where(x => whIds.Contains(x.PickingTaskLine.PickingTask.WarehouseId)).ExecuteDeleteAsync();
            await db.PickingTaskLines.Where(x => whIds.Contains(x.PickingTask.WarehouseId)).ExecuteDeleteAsync();
            await db.PickingTasks.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.StockAllocations.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.StockReservations.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.Backorders.Where(x => whIds.Contains(x.SalesOrderLine.SalesOrder.WarehouseId)).ExecuteDeleteAsync();
            await db.SalesOrderLines.Where(x => whIds.Contains(x.SalesOrder.WarehouseId)).ExecuteDeleteAsync();
            await db.SalesOrders.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.InventoryLocationMovements.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.PutawayTaskItems.Where(x => whIds.Contains(x.PutawayTask.WarehouseId)).ExecuteDeleteAsync();
            await db.PutawayTasks.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.ImportReceiptInventoryIdentities.Where(x => whIds.Contains(x.ImportReceiptDetail.ImportReceipt.WarehouseId)).ExecuteDeleteAsync();
            await db.ImportReceiptDetails.Where(x => whIds.Contains(x.ImportReceipt.WarehouseId)).ExecuteDeleteAsync();
            await db.ImportReceipts.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.ExportReceiptDetails.Where(x => whIds.Contains(x.ExportReceipt.WarehouseId)).ExecuteDeleteAsync();
            await db.ExportReceipts.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.DockAppointmentEvents.Where(x => whIds.Contains(x.Appointment.WarehouseId)).ExecuteDeleteAsync();
            await db.DockAppointments.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.Docks.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.AsnLines.Where(x => whIds.Contains(x.Asn.WarehouseId)).ExecuteDeleteAsync();
            await db.Asns.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.PurchaseOrderLines.Where(x => whIds.Contains(x.PurchaseOrder.WarehouseId)).ExecuteDeleteAsync();
            await db.PurchaseOrders.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.InventoryTransactions.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.InventoryStocks.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();

            await db.WarehouseCalendarDays.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.WarehouseShifts.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.WarehouseCalendars.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
        }

        if (prodIds.Length > 0)
        {
            await db.InventorySerials.Where(x => prodIds.Contains(x.ProductId)).ExecuteDeleteAsync();
            await db.InventoryLots.Where(x => prodIds.Contains(x.ProductId)).ExecuteDeleteAsync();
            await db.QcPolicies.Where(x => prodIds.Contains(x.ProductId)).ExecuteDeleteAsync();
        }

        if (userIds.Length > 0)
        {
            await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        }

        if (whIds.Length > 0)
        {
            await db.WarehouseLocations.Where(x => whIds.Contains(x.WarehouseId)).ExecuteDeleteAsync();
            await db.Warehouses.Where(x => whIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        if (prodIds.Length > 0)
        {
            await db.Products.Where(x => prodIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        if (state.UnitId > 0)
        {
            await db.Units.Where(x => x.Id == state.UnitId).ExecuteDeleteAsync();
        }

        var partnerIds = new[] { state.SupplierId, state.CustomerId }.Where(x => x > 0).ToArray();
        if (partnerIds.Length > 0)
        {
            await db.PartnerDebtLedgers.Where(x => partnerIds.Contains(x.PartnerId)).ExecuteDeleteAsync();
            await db.PartnerDebtInvoices.Where(x => partnerIds.Contains(x.PartnerId)).ExecuteDeleteAsync();
            await db.BusinessPartners.Where(x => partnerIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        if (userIds.Length > 0)
        {
            await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        if (state.RoleId > 0)
        {
            await db.Roles.Where(x => x.Id == state.RoleId).ExecuteDeleteAsync();
        }
    }

    private sealed class LifecycleState(string suffix)
    {
        public string Suffix { get; } = suffix;
        public int RoleId { get; set; }
        public int MakerId { get; set; }
        public int CheckerId { get; set; }
        public int SupplierId { get; set; }
        public int CustomerId { get; set; }
        public int UnitId { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public int LotProductId { get; set; }
        public string LotProductCode { get; set; } = string.Empty;
        public int SerialProductId { get; set; }
        public string SerialProductCode { get; set; } = string.Empty;
        public int StandardProductId { get; set; }
        public string StandardProductCode { get; set; } = string.Empty;
        public int Wh1Id { get; set; }
        public int Wh2Id { get; set; }
        public int Wh1BinAId { get; set; }
        public string Wh1BinACode { get; set; } = string.Empty;
        public int Wh1BinBId { get; set; }
        public string Wh1BinBCode { get; set; } = string.Empty;
        public int Wh1StagingId { get; set; }
        public string Wh1StagingCode { get; set; } = string.Empty;
        public int Wh1DockId { get; set; }
        public int Wh2LegacyLocationId { get; set; }
        public int Wh2DamagedLocationId { get; set; }
        public int PoId { get; set; }
        public int AsnId { get; set; }
        public int SalesOrderId { get; set; }
        public int StockTransferId { get; set; }
        public int StocktakeId { get; set; }
    }

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }
}
