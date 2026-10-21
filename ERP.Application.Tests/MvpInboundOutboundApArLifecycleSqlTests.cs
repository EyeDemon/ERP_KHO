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
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class MvpInboundOutboundApArLifecycleSqlTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task FullInboundOutboundApArLifecycle_EnforcesRowVersionScanPutawayCreditLimitSingleExportAndAppendOnlyDebtLedger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var state = new MvpLifecycleState(suffix);

        try
        {
            // =========================================================================
            // STEP 0: Seed Actors, Role, Unit, Supplier, Customer (with CreditLimit), Product & ProductBarcode
            // =========================================================================
            await using (var seedDb = CreateContext())
            {
                var role = new Role { RoleName = $"MvpRole_{suffix}" };
                var maker = new User
                {
                    Username = $"mvp_maker_{suffix}",
                    PasswordHash = "not-used",
                    FullName = "MVP Maker",
                    Role = role
                };
                var checker = new User
                {
                    Username = $"mvp_checker_{suffix}",
                    PasswordHash = "not-used",
                    FullName = "MVP Checker",
                    Role = role
                };
                var supplier = new BusinessPartner
                {
                    Code = $"SUP-MVP-{suffix}",
                    Name = "MVP Supplier Co.",
                    IsSupplier = true,
                    IsActive = true
                };
                var customer = new BusinessPartner
                {
                    Code = $"CUS-MVP-{suffix}",
                    Name = "MVP Customer Corp.",
                    IsCustomer = true,
                    IsActive = true,
                    CreditLimit = 5_000_000m // Initial credit limit: 5,000,000 VND
                };
                var unit = new Unit
                {
                    Code = $"BX{suffix[..4]}",
                    Name = "Box",
                    DecimalPlaces = 0
                };
                var product = new Product
                {
                    Code = $"MVP-SKU-{suffix}",
                    Name = "MVP Wireless Scanner Pro",
                    Unit = unit,
                    TrackingType = ProductTrackingType.None,
                    ExpiryControl = false,
                    IsActive = true
                };

                seedDb.AddRange(role, maker, checker, supplier, customer, unit, product);
                await seedDb.SaveChangesAsync();

                var productBarcode = new ProductBarcode
                {
                    ProductId = product.Id,
                    Value = $"893-ALT-{suffix}"
                };
                seedDb.ProductBarcodes.Add(productBarcode);
                await seedDb.SaveChangesAsync();

                state.RoleId = role.Id;
                state.MakerId = maker.Id;
                state.MakerUsername = maker.Username;
                state.CheckerId = checker.Id;
                state.SupplierId = supplier.Id;
                state.CustomerId = customer.Id;
                state.UnitId = unit.Id;
                state.UnitCode = unit.Code;
                state.ProductId = product.Id;
                state.ProductCode = product.Code;
                state.ProductBarcode = productBarcode.Value;
            }

            // =========================================================================
            // STEP 1: Create Warehouse & Storage/Staging Locations
            // =========================================================================
            await using (var whDb = CreateContext())
            {
                var whService = CreateWarehouseService(whDb, state.MakerId);
                var wh = await whService.CreateWarehouseAsync(new CreateWarehouseDto
                {
                    Code = $"WH-MVP-{suffix}",
                    Name = $"MVP Primary Warehouse {suffix}",
                    Address = "KCN VSIP 1"
                }, state.MakerUsername);

                state.WarehouseId = wh.Id;

                whDb.UserWarehouses.Add(
                    new UserWarehouse { UserId = state.CheckerId, WarehouseId = state.WarehouseId, CreatedBy = state.MakerId });
                await whDb.SaveChangesAsync();

                var putawayService = CreatePutawayService(whDb, state.MakerId);
                var storageBin = await putawayService.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.WarehouseId,
                    Code = $"BIN-MVP-{suffix}",
                    Name = "MVP Storage Bin A1",
                    StructurePath = "ZONE-A/A01/R01/L01/B01",
                    LocationType = "Storage",
                    IsPickable = true
                });
                var stagingBin = await putawayService.CreateLocationAsync(new CreateWarehouseLocationDto
                {
                    WarehouseId = state.WarehouseId,
                    Code = $"STG-MVP-{suffix}",
                    Name = "MVP Outbound Staging",
                    StructurePath = "ZONE-S/A01/R01/L01/B01",
                    LocationType = "Staging",
                    IsPickable = false
                });

                state.StorageBinId = storageBin.Id;
                state.StorageBinCode = storageBin.Code;
                state.StagingBinId = stagingBin.Id;
                state.StagingBinCode = stagingBin.Code;
            }

            // =========================================================================
            // STEP 2: INBOUND - Create Purchase Order with Pricing & Tax -> Open -> Receive -> Post ImportReceipt
            //         Verify RowVersion, PO Pricing Inheritance, and AP Debt Invoice + Append-Only Ledger
            // =========================================================================
            await using (var inboundDb = CreateContext())
            {
                var planning = CreateInboundPlanningService(inboundDb, state.MakerId);
                var po = await planning.CreatePurchaseOrderAsync(new CreatePurchaseOrderDto
                {
                    ExternalPoId = $"ERP-PO-{suffix}",
                    SourceSystem = "ERP",
                    Code = $"PO-MVP-{suffix}",
                    SupplierId = state.SupplierId,
                    WarehouseId = state.WarehouseId,
                    Currency = "VND",
                    OrderDate = DateTime.UtcNow.Date,
                    ExpectedDate = DateTime.UtcNow.Date.AddDays(1),
                    Lines =
                    [
                        new CreatePurchaseOrderLineDto
                        {
                            ExternalLineId = "1",
                            ProductId = state.ProductId,
                            OperationUnitId = state.UnitId,
                            OrderedQuantity = 100m,
                            UnitPrice = 100_000m,
                            TaxPercent = 10m
                        }
                    ]
                });

                po.SubTotalAmount.Should().Be(10_000_000m);
                po.TaxAmount.Should().Be(1_000_000m);
                po.TotalAmount.Should().Be(11_000_000m);
                po.Lines.Single().LineTotal.Should().Be(11_000_000m);

                po = await planning.OpenPurchaseOrderAsync(po.Id, new InboundStateCommandDto { RowVersion = po.RowVersion! });
                po.Status.Should().Be("Open");
                state.PurchaseOrderId = po.Id;
                var poLineId = po.Lines.Single().Id;

                var importService = CreateImportReceiptService(inboundDb, state.MakerId);
                var receipt = await importService.CreateAsync(new CreateImportReceiptDto
                {
                    Code = $"IR-MVP-{suffix}",
                    WarehouseId = state.WarehouseId,
                    SupplierId = state.SupplierId,
                    PurchaseOrderId = po.Id,
                    Note = "MVP Inbound from PO",
                    Details =
                    [
                        new CreateImportReceiptDetailDto
                        {
                            ProductId = state.ProductId,
                            PurchaseOrderLineId = poLineId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 100m
                        }
                    ]
                }, state.MakerId);

                // Pricing should be inherited automatically from PurchaseOrderLine
                receipt.Details.Single().UnitPrice.Should().Be(100_000m);
                receipt.Details.Single().TaxPercent.Should().Be(10m);

                var detailId = receipt.Details.Single().Id;
                await importService.ReceiveAsync(receipt.Id, new ReceiveImportReceiptDto
                {
                    Lines =
                    [
                        new ReceiveImportReceiptLineDto
                        {
                            LineId = detailId,
                            ReceivedQuantity = 100m,
                            AcceptedQuantity = 100m,
                            DamagedQuantity = 0m,
                            RejectedQuantity = 0m
                        }
                    ]
                }, state.MakerId);
                receipt = await importService.GetByIdAsync(receipt.Id);
                receipt.Status.Should().Be("Received");
                receipt.SubTotalAmount.Should().Be(10_000_000m);
                receipt.TaxAmount.Should().Be(1_000_000m);
                receipt.TotalAmount.Should().Be(11_000_000m);

                var checkerImportService = CreateImportReceiptService(inboundDb, state.CheckerId);
                await checkerImportService.ApproveImportReceiptAsync(receipt.Id, state.CheckerId);
                await checkerImportService.PostAsync(receipt.Id, state.CheckerId);
                receipt = await checkerImportService.GetByIdAsync(receipt.Id);
                receipt.Status.Should().Be("Posted");
                state.ImportReceiptId = receipt.Id;
                var putawayTask = await inboundDb.PutawayTasks.AsNoTracking()
                    .SingleAsync(x => x.ReceiptId == receipt.Id);
                state.PutawayTaskId = putawayTask.Id;
            }

            // Verify SQL Server RowVersion on ImportReceipt & InventoryStock, and AP Debt Invariants
            await using (var verifyApDb = CreateContext())
            {
                var dbReceipt = await verifyApDb.ImportReceipts.AsNoTracking()
                    .SingleAsync(x => x.Id == state.ImportReceiptId);
                dbReceipt.RowVersion.Should().NotBeNullOrEmpty();

                var receivingStock = await verifyApDb.InventoryStocks.AsNoTracking()
                    .Include(x => x.Location)
                    .SingleAsync(x => x.WarehouseId == state.WarehouseId && x.ProductId == state.ProductId);
                receivingStock.Quantity.Should().Be(100m);
                receivingStock.Location!.Code.Should().Be("RECEIVING");
                receivingStock.RowVersion.Should().NotBeNullOrEmpty();

                var supplier = await verifyApDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.SupplierId);
                supplier.PayableBalance.Should().Be(11_000_000m);

                var apInvoice = await verifyApDb.PartnerDebtInvoices.AsNoTracking()
                    .SingleAsync(x => x.PartnerId == state.SupplierId && x.DebtType == PartnerDebtType.AP);
                apInvoice.SourceType.Should().Be("ImportReceipt");
                apInvoice.SourceId.Should().Be(state.ImportReceiptId);
                apInvoice.SubTotalAmount.Should().Be(10_000_000m);
                apInvoice.TaxAmount.Should().Be(1_000_000m);
                apInvoice.TotalAmount.Should().Be(11_000_000m);
                apInvoice.PaidAmount.Should().Be(0m);
                apInvoice.RemainingAmount.Should().Be(11_000_000m);
                apInvoice.Status.Should().Be(PartnerDebtInvoiceStatus.Open);
                apInvoice.RowVersion.Should().NotBeNullOrEmpty();
                state.ApInvoiceId = apInvoice.Id;

                var apLedgers = await verifyApDb.PartnerDebtLedgers.AsNoTracking()
                    .Where(x => x.InvoiceId == apInvoice.Id)
                    .ToListAsync();
                apLedgers.Should().ContainSingle();
                apLedgers[0].TransactionType.Should().Be(PartnerDebtTransactionType.InvoiceCreated);
                apLedgers[0].AmountDelta.Should().Be(11_000_000m);
                apLedgers[0].BalanceAfter.Should().Be(11_000_000m);
            }

            // =========================================================================
            // STEP 3: SCAN-FIRST PUTAWAY - Verify Wrong Bin (409), Wrong Product (409),
            //         Valid Scan Move, and TransactionType.Move Ledger Entry
            // =========================================================================
            await using (var putawayDb = CreateContext())
            {
                var putawayService = CreatePutawayService(putawayDb, state.MakerId);
                var task = await putawayService.GetAsync(state.PutawayTaskId);
                task = await putawayService.AssignAsync(state.PutawayTaskId, new PutawayStateCommandDto
                {
                    AssignedUserId = state.MakerId,
                    RowVersion = task.RowVersion!
                });
                task = await putawayService.StartAsync(state.PutawayTaskId, new PutawayStateCommandDto
                {
                    RowVersion = task.RowVersion!
                });
                var itemId = task.Items.Single().Id;

                // 3a. Wrong Destination Bin Barcode -> 409 PUTAWAY_WRONG_BIN
                var wrongBinMove = () => putawayService.MoveAsync(state.PutawayTaskId, new MovePutawayItemDto
                {
                    ItemId = itemId,
                    DestinationLocationId = state.StorageBinId,
                    DestinationLocationBarcode = "WRONG-BIN-CODE",
                    ProductBarcode = state.ProductBarcode,
                    Quantity = 100m,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });
                (await wrongBinMove.Should().ThrowAsync<BusinessRuleException>())
                    .Which.Data["ErrorCode"].Should().Be("PUTAWAY_WRONG_BIN");

                // 3b. Wrong Product Barcode -> 409 PUTAWAY_WRONG_PRODUCT
                var wrongProductMove = () => putawayService.MoveAsync(state.PutawayTaskId, new MovePutawayItemDto
                {
                    ItemId = itemId,
                    DestinationLocationId = state.StorageBinId,
                    DestinationLocationBarcode = state.StorageBinCode,
                    ProductBarcode = "WRONG-SKU-BARCODE",
                    Quantity = 100m,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });
                (await wrongProductMove.Should().ThrowAsync<BusinessRuleException>())
                    .Which.Data["ErrorCode"].Should().Be("PUTAWAY_WRONG_PRODUCT");

                // 3c. Valid Scan-First Move (using ProductBarcode from ProductBarcodes table + StorageBinCode)
                var completedTask = await putawayService.MoveAsync(state.PutawayTaskId, new MovePutawayItemDto
                {
                    ItemId = itemId,
                    DestinationLocationId = state.StorageBinId,
                    DestinationLocationBarcode = state.StorageBinCode,
                    ProductBarcode = state.ProductBarcode,
                    Quantity = 100m,
                    UnitCode = state.UnitCode,
                    RowVersion = task.RowVersion!
                });
                completedTask.Status.Should().Be("Completed");
                completedTask.ReceiptCode.Should().Be($"IR-MVP-{suffix}").And.Be(completedTask.ReceiptCode.Trim());
                completedTask.WarehouseName.Should().Be($"MVP Primary Warehouse {suffix}").And.Be(completedTask.WarehouseName.Trim());
                var completedItem = completedTask.Items.Single();
                completedItem.MovedBaseQuantity.Should().Be(100m);
                completedItem.SourceLocationCode.Should().Be("RECEIVING").And.Be(completedItem.SourceLocationCode.Trim());
                var completedMovement = completedTask.Movements.Single();
                completedMovement.FromLocationCode.Should().Be("RECEIVING").And.Be(completedMovement.FromLocationCode.Trim());
                completedMovement.ToLocationCode.Should().Be(state.StorageBinCode).And.Be(completedMovement.ToLocationCode.Trim());

                // Verify TransactionType.Move was written to InventoryTransactions with FromLocationId & ToLocationId
                var moveTx = await putawayDb.InventoryTransactions.AsNoTracking()
                    .SingleAsync(x =>
                        x.WarehouseId == state.WarehouseId &&
                        x.ProductId == state.ProductId &&
                        x.TransactionType == TransactionType.Move &&
                        x.ReferenceType == "PutawayTask" &&
                        x.ReferenceId == state.PutawayTaskId &&
                        x.Quantity > 0);
                moveTx.Quantity.Should().Be(100m);
                moveTx.FromLocationId.Should().NotBeNull();
                moveTx.ToLocationId.Should().Be(state.StorageBinId);
                moveTx.LocationId.Should().Be(state.StorageBinId);

                // Verify total warehouse stock is still conserved at 100 (0 in RECEIVING, 100 in StorageBin)
                var stocks = await putawayDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == state.WarehouseId && x.ProductId == state.ProductId)
                    .ToListAsync();
                stocks.Sum(x => x.Quantity).Should().Be(100m);
                stocks.Single(x => x.LocationId == state.StorageBinId).Quantity.Should().Be(100m);
            }

            // =========================================================================
            // STEP 4: PAY AP DEBT & VERIFY APPEND-ONLY LEDGER GUARD
            // =========================================================================
            await using (var payApDb = CreateContext())
            {
                var debtService = CreatePartnerDebtService(payApDb, state.MakerId);

                // Partial payment: 4,000,000 VND
                var partial = await debtService.PayInvoiceAsync(state.ApInvoiceId, new PayPartnerDebtDto
                {
                    Amount = 4_000_000m,
                    PaymentMethod = "BankTransfer",
                    ReferenceCode = $"UNC-AP1-{suffix}",
                    Note = "Thanh toán đợt 1"
                });
                partial.Status.Should().Be("PartiallyPaid");
                partial.PaidAmount.Should().Be(4_000_000m);
                partial.RemainingAmount.Should().Be(7_000_000m);

                // Remaining payment: 7,000,000 VND
                var settled = await debtService.PayInvoiceAsync(state.ApInvoiceId, new PayPartnerDebtDto
                {
                    Amount = 7_000_000m,
                    PaymentMethod = "BankTransfer",
                    ReferenceCode = $"UNC-AP2-{suffix}",
                    Note = "Tất toán công nợ AP"
                });
                settled.Status.Should().Be("Paid");
                settled.PaidAmount.Should().Be(11_000_000m);
                settled.RemainingAmount.Should().Be(0m);

                var supplier = await payApDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.SupplierId);
                supplier.PayableBalance.Should().Be(0m);

                var apLedgers = await debtService.ListLedgerAsync(partnerId: state.SupplierId, invoiceId: state.ApInvoiceId);
                apLedgers.Should().HaveCount(3);

                // Verify Append-Only Guard on PartnerDebtLedger: modifying an existing entry must throw
                var trackedLedger = await payApDb.PartnerDebtLedgers.FirstAsync(x => x.InvoiceId == state.ApInvoiceId);
                trackedLedger.Note = "Tamper attempt";
                var tamperAct = () => payApDb.SaveChangesAsync();
                await tamperAct.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*Append-Only*");
            }

            // =========================================================================
            // STEP 5: OUTBOUND - SalesOrder with Pricing/Tax & CreditLimit Guard -> Release -> Pick -> Pack -> Ship
            //         Verify Auto-ExportReceipt (NO Double Stock Deduction) & Single AR Debt Invoice
            // =========================================================================
            await using (var outboundDb = CreateContext())
            {
                var demandService = CreateDemandService(outboundDb, state.MakerId);

                // Create SO: 40 units * 150,000 = 6,000,000 + 10% VAT (600,000) = 6,600,000 VND
                // Customer CreditLimit is currently 5,000,000 VND -> Release must be blocked with 409!
                var so = await demandService.CreateSalesOrderAsync(new CreateSalesOrderDto
                {
                    ExternalOrderId = $"SO-MVP-{suffix}",
                    CustomerId = state.CustomerId,
                    WarehouseId = state.WarehouseId,
                    Currency = "VND",
                    RequestedShipDate = DateTime.UtcNow.AddDays(1),
                    Lines =
                    [
                        new CreateSalesOrderLineDto
                        {
                            ExternalLineId = "1",
                            ProductId = state.ProductId,
                            OrderedQuantity = 40m,
                            UnitPrice = 150_000m,
                            TaxPercent = 10m
                        }
                    ]
                });
                so.SubTotalAmount.Should().Be(6_000_000m);
                so.TaxAmount.Should().Be(600_000m);
                so.TotalAmount.Should().Be(6_600_000m);
                state.SalesOrderId = so.Id;

                var blockedRelease = () => demandService.ReleaseSalesOrderAsync(so.Id, new SalesOrderStateCommandDto
                {
                    RowVersion = so.RowVersion,
                    Reason = "Release over credit limit"
                });
                (await blockedRelease.Should().ThrowAsync<BusinessRuleException>())
                    .Which.Data["ErrorCode"].Should().Be("SALES_ORDER_CREDIT_LIMIT_EXCEEDED");

                // Raise Customer CreditLimit to 20,000,000 VND and Release again
                var customer = await outboundDb.BusinessPartners.SingleAsync(x => x.Id == state.CustomerId);
                customer.CreditLimit = 20_000_000m;
                await outboundDb.SaveChangesAsync();

                so = await demandService.GetSalesOrderAsync(so.Id);
                so = await demandService.ReleaseSalesOrderAsync(so.Id, new SalesOrderStateCommandDto
                {
                    RowVersion = so.RowVersion,
                    Reason = "Release within credit limit"
                });
                so.Status.Should().Be("Released");
                so.AllocatedQuantity.Should().Be(40m);
            }

            // Execute Picking -> Packing -> Shipment Dispatch
            await using (var execDb = CreateContext())
            {
                var pickingService = CreatePickingService(execDb, state.MakerId, withPacking: true);
                var tasks = (await pickingService.ListAsync(state.WarehouseId))
                    .Where(x => x.SourceType == "SalesOrder" && x.SourceId == state.SalesOrderId)
                    .ToList();
                tasks.Should().ContainSingle();
                var pickTask = await pickingService.GetAsync(tasks[0].Id);
                pickTask = await pickingService.AssignAsync(pickTask.Id, new PickingStateCommandDto
                {
                    RowVersion = pickTask.RowVersion!,
                    AssignedUserId = state.MakerId
                });
                pickTask = await pickingService.StartAsync(pickTask.Id, new PickingStateCommandDto
                {
                    RowVersion = pickTask.RowVersion!
                });
                var pickLine = pickTask.Lines.Single();

                pickTask = await pickingService.PickAsync(pickTask.Id, new PickScanDto
                {
                    TaskLineId = pickLine.Id,
                    LocationBarcode = state.StorageBinCode,
                    ProductBarcode = state.ProductCode,
                    Quantity = 40m,
                    RowVersion = pickTask.RowVersion!
                });
                pickTask = await pickingService.CompleteAsync(pickTask.Id, new PickingStateCommandDto
                {
                    RowVersion = pickTask.RowVersion!
                });
                pickTask.Status.Should().Be("Completed");

                var packingService = CreatePackingService(execDb, state.MakerId, withShipment: true);
                var sessions = (await packingService.ListAsync(state.WarehouseId))
                    .Where(x => x.SourceType == "SalesOrder" && x.SourceId == state.SalesOrderId)
                    .ToList();
                sessions.Should().ContainSingle();
                var session = await packingService.GetAsync(sessions[0].Id);

                session = await packingService.CreateHandlingUnitAsync(session.Id, new CreateHandlingUnitDto
                {
                    Type = "Pallet",
                    GrossWeightKg = 12.5m,
                    RowVersion = session.RowVersion!
                });
                var hu = session.HandlingUnits.Single();
                session = await packingService.PackAsync(session.Id, new PackIntoHandlingUnitDto
                {
                    HandlingUnitId = hu.Id,
                    PickingTaskLineId = pickLine.Id,
                    ProductBarcode = state.ProductCode,
                    Quantity = 40m,
                    RowVersion = session.RowVersion!
                });
                session = await packingService.CloseHandlingUnitAsync(session.Id, hu.Id, new PackingStateCommandDto
                {
                    RowVersion = session.RowVersion!
                });
                session = await packingService.CompleteAsync(session.Id, new PackingStateCommandDto
                {
                    RowVersion = session.RowVersion!
                });
                session.Status.Should().Be("Packed");
                var huCode = session.HandlingUnits.Single().Barcode;

                // Seed outbound Dock & InService DockAppointment for loading
                var dock = new Dock
                {
                    WarehouseId = state.WarehouseId,
                    Code = $"DCK-MVP-{suffix}",
                    Name = "MVP Outbound Dock",
                    SupportsOutbound = true,
                    IsActive = true
                };
                execDb.Docks.Add(dock);
                await execDb.SaveChangesAsync();

                var appointment = new DockAppointment
                {
                    WarehouseId = state.WarehouseId,
                    DockId = dock.Id,
                    Code = $"APT-MVP-{suffix}",
                    Direction = DockAppointmentDirection.Outbound,
                    Status = DockAppointmentStatus.InService,
                    PlannedStartUtc = DateTime.UtcNow.AddMinutes(-10),
                    PlannedEndUtc = DateTime.UtcNow.AddHours(1),
                    VehiclePlate = "51C-888.99",
                    CreatedBy = state.MakerId
                };
                execDb.DockAppointments.Add(appointment);
                await execDb.SaveChangesAsync();

                var shipmentService = CreateShipmentService(execDb, state.MakerId);
                var shipments = await shipmentService.ListAsync(state.WarehouseId);
                var shipmentSummary = shipments.Single(x => x.SourceType == "SalesOrder" && x.SourceId == state.SalesOrderId);
                var shipment = await shipmentService.GetAsync(shipmentSummary.Id);

                shipment = await shipmentService.StageAsync(shipment.Id, new StageShipmentDto
                {
                    StagingLocationCode = state.StagingBinCode,
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.StartLoadingAsync(shipment.Id, new StartShipmentLoadingDto
                {
                    DockAppointmentId = appointment.Id,
                    RowVersion = shipment.RowVersion!
                });
                shipment = await shipmentService.LoadHandlingUnitAsync(shipment.Id, new LoadShipmentHandlingUnitDto
                {
                    HandlingUnitBarcode = huCode,
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
                shipment.Status.Should().Be("Dispatched");
                state.ShipmentId = shipment.Id;
            }

            // =========================================================================
            // STEP 6: VERIFY NO DOUBLE DEDUCTION, AUTO EXPORT RECEIPT & SINGLE AR DEBT INVOICE
            // =========================================================================
            await using (var verifyArDb = CreateContext())
            {
                // Stock must be deducted ONCE: 100 - 40 = 60 (never 20!)
                var binStock = await verifyArDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == state.WarehouseId && x.ProductId == state.ProductId && x.LocationId == state.StorageBinId);
                binStock.Quantity.Should().Be(60m);
                binStock.ReservedQuantity.Should().Be(0m);

                // ExportReceipt must be auto-created and linked to SalesOrderId & ShipmentId
                var autoExport = await verifyArDb.ExportReceipts.AsNoTracking()
                    .Include(x => x.Details)
                    .SingleAsync(x => x.WarehouseId == state.WarehouseId && x.ShipmentId == state.ShipmentId);
                autoExport.SalesOrderId.Should().Be(state.SalesOrderId);
                autoExport.CustomerId.Should().Be(state.CustomerId);
                autoExport.Status.Should().Be(ReceiptStatus.Dispatched);
                autoExport.RowVersion.Should().NotBeNullOrEmpty();
                autoExport.Details.Should().ContainSingle();
                var autoExportDetail = autoExport.Details.Single();
                autoExportDetail.Quantity.Should().Be(40m);
                autoExportDetail.UnitPrice.Should().Be(150_000m);
                autoExportDetail.TaxPercent.Should().Be(10m);
                autoExportDetail.BaseUomIdSnapshot.Should().Be(state.UnitId);
                autoExportDetail.BaseUomCodeSnapshot.Should().NotBeNullOrWhiteSpace();
                autoExportDetail.BaseUomCodeSnapshot.Should().Be(autoExportDetail.BaseUomCodeSnapshot!.Trim());
                autoExportDetail.BaseUomNameSnapshot.Should().NotBeNullOrWhiteSpace();
                autoExportDetail.BaseUomNameSnapshot.Should().Be(autoExportDetail.BaseUomNameSnapshot!.Trim());

                var exportReceiptService = new ExportReceiptService(
                    new ExportReceiptRepository(verifyArDb),
                    new InventoryStockRepository(verifyArDb),
                    new InventoryTransactionRepository(verifyArDb),
                    new UnitOfWork(verifyArDb),
                    new AuditLogRepository(verifyArDb));
                var autoExportDto = await exportReceiptService.GetByIdAsync(autoExport.Id);
                autoExportDto.Should().NotBeNull();
                autoExportDto!.SubTotalAmount.Should().Be(6_000_000m);
                autoExportDto.TaxAmount.Should().Be(600_000m);
                autoExportDto.TotalAmount.Should().Be(6_600_000m);
                var autoExportDetailDto = autoExportDto.Details.Single();
                autoExportDetailDto.UnitName.Should().NotBeNullOrWhiteSpace();
                autoExportDetailDto.UnitName.Should().Be(autoExportDetailDto.UnitName!.Trim());
                autoExportDetailDto.BaseUomCodeSnapshot.Should().NotBeNullOrWhiteSpace();
                autoExportDetailDto.BaseUomCodeSnapshot.Should().Be(autoExportDetailDto.BaseUomCodeSnapshot!.Trim());
                autoExportDetailDto.UnitPrice.Should().Be(150_000m);
                autoExportDetailDto.TaxPercent.Should().Be(10m);
                autoExportDetailDto.LineSubTotal.Should().Be(6_000_000m);
                autoExportDetailDto.LineTaxAmount.Should().Be(600_000m);
                autoExportDetailDto.LineTotal.Should().Be(6_600_000m);

                var verifyShipmentService = CreateShipmentService(verifyArDb, state.MakerId);
                var dispatchedShipmentDto = await verifyShipmentService.GetAsync(state.ShipmentId);
                dispatchedShipmentDto.HandlingUnits.Should().ContainSingle();
                Assert.All(dispatchedShipmentDto.HandlingUnits, huDto =>
                {
                    Assert.Equal(huDto.HuCode.Trim(), huDto.HuCode);
                    Assert.Equal(huDto.Barcode.Trim(), huDto.Barcode);
                });

                var shipmentTrackingDto = await verifyShipmentService.GetTrackingAsync(state.ShipmentId);
                shipmentTrackingDto.ShipmentCode.Should().Be(shipmentTrackingDto.ShipmentCode.Trim());
                shipmentTrackingDto.Events.Should().NotBeEmpty();
                Assert.All(shipmentTrackingDto.Events, ev =>
                {
                    Assert.Equal(ev.EventType.Trim(), ev.EventType);
                    Assert.Equal(ev.Source.Trim(), ev.Source);
                    if (ev.Note is not null)
                    {
                        Assert.Equal(ev.Note.Trim(), ev.Note);
                    }
                });

                // Customer ReceivableBalance must equal 6,600,000 VND with exactly 1 AR Invoice
                var customer = await verifyArDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.CustomerId);
                customer.ReceivableBalance.Should().Be(6_600_000m);

                var arInvoices = await verifyArDb.PartnerDebtInvoices.AsNoTracking()
                    .Where(x => x.PartnerId == state.CustomerId && x.DebtType == PartnerDebtType.AR)
                    .ToListAsync();
                arInvoices.Should().ContainSingle();
                var arInvoice = arInvoices[0];
                arInvoice.SourceType.Should().Be("ExportReceipt");
                arInvoice.SourceId.Should().Be(autoExport.Id);
                arInvoice.SubTotalAmount.Should().Be(6_000_000m);
                arInvoice.TaxAmount.Should().Be(600_000m);
                arInvoice.TotalAmount.Should().Be(6_600_000m);
                arInvoice.RemainingAmount.Should().Be(6_600_000m);
                arInvoice.Status.Should().Be(PartnerDebtInvoiceStatus.Open);

                // Settle AR Invoice and verify Customer ReceivableBalance drops to 0
                var debtService = CreatePartnerDebtService(verifyArDb, state.MakerId);
                var paidAr = await debtService.PayInvoiceAsync(arInvoice.Id, new PayPartnerDebtDto
                {
                    Amount = 6_600_000m,
                    PaymentMethod = "BankTransfer",
                    ReferenceCode = $"UNC-AR1-{suffix}",
                    Note = "Khách hàng thanh toán đủ đơn hàng"
                });
                paidAr.Status.Should().Be("Paid");
                paidAr.RemainingAmount.Should().Be(0m);

                var customerAfterPay = await verifyArDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.CustomerId);
                customerAfterPay.ReceivableBalance.Should().Be(0m);

                var summary = await debtService.GetSummaryAsync();
                summary.Should().NotBeNull();

                // Verify ListLedgerAsync(invoiceId) strictly isolates AP vs AR invoice ledger entries
                var apInvoiceLedger = await debtService.ListLedgerAsync(invoiceId: state.ApInvoiceId);
                apInvoiceLedger.Should().HaveCount(3);
                Assert.All(apInvoiceLedger, x =>
                {
                    Assert.Equal(state.ApInvoiceId, x.InvoiceId);
                    Assert.Equal("AP", x.DebtType);
                });
                Assert.DoesNotContain(apInvoiceLedger, x => x.DebtType == "AR" || x.InvoiceId != state.ApInvoiceId);

                var arInvoiceLedger = await debtService.ListLedgerAsync(invoiceId: arInvoice.Id);
                arInvoiceLedger.Should().HaveCount(2);
                Assert.All(arInvoiceLedger, x =>
                {
                    Assert.Equal(arInvoice.Id, x.InvoiceId);
                    Assert.Equal("AR", x.DebtType);
                });
                Assert.DoesNotContain(arInvoiceLedger, x => x.DebtType == "AP" || x.InvoiceId != arInvoice.Id);
            }

            // =========================================================================
            // STEP 7: CONCURRENT DEBT PAYMENT (5 parallel threads paying 60 on RemainingAmount = 100 without RowVersion)
            //         Verify SQL Server UPDLOCK, ROWLOCK, HOLDLOCK prevents over-payment & negative balance
            // =========================================================================
            int concurrentInvoiceId;
            await using (var seedConcDb = CreateContext())
            {
                var debtService = CreatePartnerDebtService(seedConcDb, state.MakerId);
                var concDto = await debtService.RecordOutboundArDebtAsync(
                    customerId: state.CustomerId,
                    sourceType: "ExportReceipt",
                    sourceId: 900_000 + state.WarehouseId,
                    sourceCode: $"ER-CONC-{suffix}",
                    subTotalAmount: 100m,
                    taxAmount: 0m,
                    actorUserId: state.MakerId,
                    currency: "VND");
                concDto.Should().NotBeNull();
                concDto!.RemainingAmount.Should().Be(100m);
                concurrentInvoiceId = concDto.Id;
            }

            var paymentTasks = Enumerable.Range(1, 5).Select(async workerIndex =>
            {
                await using var workerDb = CreateContext();
                var workerDebtService = CreatePartnerDebtService(workerDb, state.MakerId);
                try
                {
                    var res = await workerDebtService.PayInvoiceAsync(concurrentInvoiceId, new PayPartnerDebtDto
                    {
                        Amount = 60m,
                        PaymentMethod = "BANK_TRANSFER",
                        ReferenceCode = $"CONC-PAY-{workerIndex}-{suffix}",
                        Note = $"Concurrent payment worker {workerIndex}"
                        // Intentionally omit RowVersion to test SQL Server UPDLOCK, ROWLOCK, HOLDLOCK
                    });
                    return (Succeeded: true, Result: (PartnerDebtInvoiceDto?)res, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Result: (PartnerDebtInvoiceDto?)null, Error: (Exception?)ex);
                }
            });

            var paymentOutcomes = await Task.WhenAll(paymentTasks);
            paymentOutcomes.Count(x => x.Succeeded).Should().Be(1);
            paymentOutcomes.Single(x => x.Succeeded).Result!.PaidAmount.Should().Be(60m);
            paymentOutcomes.Single(x => x.Succeeded).Result!.RemainingAmount.Should().Be(40m);
            paymentOutcomes.Single(x => x.Succeeded).Result!.Status.Should().Be("PartiallyPaid");

            var failedOutcomes = paymentOutcomes.Where(x => !x.Succeeded).ToList();
            failedOutcomes.Should().HaveCount(4);
            foreach (var failed in failedOutcomes)
            {
                failed.Error.Should().BeOfType<BusinessRuleException>();
                var bre = (BusinessRuleException)failed.Error!;
                bre.Data["ErrorCode"].Should().Be("DEBT_PAYMENT_EXCEEDS_REMAINING");
                bre.Data["HttpStatusCode"].Should().Be(409);
            }

            await using (var verifyConcDb = CreateContext())
            {
                var dbConcInvoice = await verifyConcDb.PartnerDebtInvoices.AsNoTracking()
                    .SingleAsync(x => x.Id == concurrentInvoiceId);
                dbConcInvoice.PaidAmount.Should().Be(60m);
                dbConcInvoice.RemainingAmount.Should().Be(40m);
                dbConcInvoice.Status.Should().Be(PartnerDebtInvoiceStatus.PartiallyPaid);

                var dbCustomer = await verifyConcDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.CustomerId);
                dbCustomer.ReceivableBalance.Should().Be(40m);
            }

            // =========================================================================
            // STEP 8: INVENTORY REVERSAL <-> PARTNER DEBT SYNCHRONIZATION
            //         8a. Paid/PartiallyPaid Debt Invoice blocks Reversal with 409 DEBT_INVOICE_ALREADY_PAID
            //         8b. Unpaid Debt Invoice (PaidAmount == 0) is auto-cancelled with negative Ledger entry & balance deduction
            // =========================================================================
            await using (var revBlockedDb = CreateContext())
            {
                var paidImportTx = await revBlockedDb.InventoryTransactions.AsNoTracking()
                    .SingleAsync(x =>
                        x.WarehouseId == state.WarehouseId &&
                        x.TransactionType == TransactionType.Import &&
                        x.ReferenceType == "ImportReceipt" &&
                        x.ReferenceId == state.ImportReceiptId);

                var reversalService = CreateInventoryReversalService(revBlockedDb, state.CheckerId);
                var blockedReverseAct = () => reversalService.ReverseAsync(new CreateInventoryReversalDto
                {
                    OriginalTransactionId = paidImportTx.Id,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Thử đảo giao dịch nhập của phiếu đã thanh toán công nợ AP"
                });

                (await blockedReverseAct.Should().ThrowAsync<BusinessRuleException>())
                    .Which.Data["ErrorCode"].Should().Be("DEBT_INVOICE_ALREADY_PAID");
            }

            int unpaidImportReceiptId;
            int unpaidImportTxId;
            int unpaidApInvoiceId;
            await using (var unpaidInboundDb = CreateContext())
            {
                var importService = CreateImportReceiptService(unpaidInboundDb, state.MakerId);
                var receipt2 = await importService.CreateAsync(new CreateImportReceiptDto
                {
                    Code = $"IR-REV-{suffix}",
                    WarehouseId = state.WarehouseId,
                    SupplierId = state.SupplierId,
                    Note = "Unpaid Inbound for Reversal Sync Test",
                    Details =
                    [
                        new CreateImportReceiptDetailDto
                        {
                            ProductId = state.ProductId,
                            OperationUnitId = state.UnitId,
                            ExpectedQuantity = 10m,
                            UnitPrice = 50_000m,
                            TaxPercent = 10m
                        }
                    ]
                }, state.MakerId);

                var detail2Id = receipt2.Details.Single().Id;
                await importService.ReceiveAsync(receipt2.Id, new ReceiveImportReceiptDto
                {
                    Lines =
                    [
                        new ReceiveImportReceiptLineDto
                        {
                            LineId = detail2Id,
                            ReceivedQuantity = 10m,
                            AcceptedQuantity = 10m,
                            DamagedQuantity = 0m,
                            RejectedQuantity = 0m
                        }
                    ]
                }, state.MakerId);

                var checkerImportService = CreateImportReceiptService(unpaidInboundDb, state.CheckerId);
                await checkerImportService.ApproveImportReceiptAsync(receipt2.Id, state.CheckerId);
                await checkerImportService.PostAsync(receipt2.Id, state.CheckerId);

                unpaidImportReceiptId = receipt2.Id;
                unpaidImportTxId = await unpaidInboundDb.InventoryTransactions.AsNoTracking()
                    .Where(x =>
                        x.WarehouseId == state.WarehouseId &&
                        x.TransactionType == TransactionType.Import &&
                        x.ReferenceType == "ImportReceipt" &&
                        x.ReferenceId == unpaidImportReceiptId)
                    .Select(x => x.Id)
                    .SingleAsync();

                var unpaidApInvoice = await unpaidInboundDb.PartnerDebtInvoices.AsNoTracking()
                    .SingleAsync(x => x.SourceType == "ImportReceipt" && x.SourceId == unpaidImportReceiptId);
                unpaidApInvoice.TotalAmount.Should().Be(550_000m);
                unpaidApInvoice.PaidAmount.Should().Be(0m);
                unpaidApInvoice.RemainingAmount.Should().Be(550_000m);
                unpaidApInvoiceId = unpaidApInvoice.Id;

                var supplierBeforeRev = await unpaidInboundDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.SupplierId);
                supplierBeforeRev.PayableBalance.Should().Be(550_000m);
            }

            await using (var executeRevDb = CreateContext())
            {
                var reversalService = CreateInventoryReversalService(executeRevDb, state.CheckerId);
                var revResult = await reversalService.ReverseAsync(new CreateInventoryReversalDto
                {
                    OriginalTransactionId = unpaidImportTxId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Đảo phiếu nhập chưa thanh toán công nợ để đồng bộ sổ cái Kho & Công nợ"
                });

                revResult.OriginalTransactionId.Should().Be(unpaidImportTxId);
                revResult.Quantity.Should().Be(10m);

                // Verify warehouse stock returned to 60 (10 units from IR-REV reversed)
                var totalStockAfterRev = await executeRevDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == state.WarehouseId && x.ProductId == state.ProductId)
                    .SumAsync(x => x.Quantity);
                totalStockAfterRev.Should().Be(60m);

                // Verify AP Debt Invoice is automatically Cancelled with RemainingAmount = 0
                var cancelledApInvoice = await executeRevDb.PartnerDebtInvoices.AsNoTracking()
                    .SingleAsync(x => x.Id == unpaidApInvoiceId);
                cancelledApInvoice.Status.Should().Be(PartnerDebtInvoiceStatus.Cancelled);
                cancelledApInvoice.RemainingAmount.Should().Be(0m);
                cancelledApInvoice.PaidAmount.Should().Be(0m);

                // Verify Supplier PayableBalance returned to 0
                var supplierAfterRev = await executeRevDb.BusinessPartners.AsNoTracking()
                    .SingleAsync(x => x.Id == state.SupplierId);
                supplierAfterRev.PayableBalance.Should().Be(0m);

                // Verify Append-Only PartnerDebtLedger has negative Adjustment entry
                var revLedgers = await executeRevDb.PartnerDebtLedgers.AsNoTracking()
                    .Where(x => x.InvoiceId == unpaidApInvoiceId)
                    .OrderBy(x => x.Id)
                    .ToListAsync();
                revLedgers.Should().HaveCount(2);
                revLedgers[1].TransactionType.Should().Be(PartnerDebtTransactionType.Adjustment);
                revLedgers[1].AmountDelta.Should().Be(-550_000m);
                revLedgers[1].BalanceAfter.Should().Be(0m);
            }
        }
        finally
        {
            await CleanupAsync(state);
        }
    }

    private static InventoryReversalService CreateInventoryReversalService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var auth = new WarehouseAuthorizationService(db, current);
        var locks = new Moq.Mock<IInventoryLockEvaluator>();
        return new InventoryReversalService(
            db,
            auth,
            current,
            new InventoryMovementService(db, auth, current, locks.Object),
            new InventoryStatusService(db, auth, current, locks.Object));
    }

    private static WarehouseService CreateWarehouseService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new WarehouseService(
            new WarehouseRepository(db),
            new WarehouseAuthorizationService(db, current),
            new InventoryStockRepository(db));
    }

    private static InboundPlanningService CreateInboundPlanningService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new InboundPlanningService(
            db,
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static PutawayService CreatePutawayService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new PutawayService(
            db,
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static PartnerDebtService CreatePartnerDebtService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new PartnerDebtService(db, current);
    }

    private static ImportReceiptService CreateImportReceiptService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var auth = new WarehouseAuthorizationService(db, current);
        var putaway = new PutawayService(db, auth, current);
        var inboundPlanning = new ReceiptInboundPlanningIntegration(db);
        var identity = new ReceiptInventoryIdentityService(db, auth);
        var partnerDebt = new PartnerDebtService(db, current);
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
            identity,
            partnerDebt);
    }

    private static SalesOrderBackorderService CreateDemandService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var auth = new WarehouseAuthorizationService(db, current);
        var pickingIntegration = new PickingTaskIntegration(db);
        var reservation = new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            auth,
            current,
            new StockReservationOptions(),
            pickingIntegration);
        var allocation = new StockAllocationService(
            db,
            auth,
            current,
            pickingIntegration);
        return new SalesOrderBackorderService(
            db,
            new InventoryStockRepository(db),
            reservation,
            allocation,
            auth,
            current,
            pickingIntegration);
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
        var partnerDebt = new PartnerDebtService(db, current);
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
                new StockReservationOptions()),
            partnerDebt);
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private static async Task CleanupAsync(MvpLifecycleState state)
    {
        await using var db = CreateContext();
        var whId = state.WarehouseId;
        var userIds = new[] { state.MakerId, state.CheckerId }.Where(x => x > 0).ToArray();
        var partnerIds = new[] { state.SupplierId, state.CustomerId }.Where(x => x > 0).ToArray();

        if (userIds.Length > 0 || whId > 0)
        {
            await db.AuditLogs
                .Where(x => (x.UserId.HasValue && userIds.Contains(x.UserId.Value)) ||
                            (x.WarehouseId.HasValue && x.WarehouseId.Value == whId))
                .ExecuteDeleteAsync();
        }

        if (partnerIds.Length > 0)
        {
            await db.PartnerDebtLedgers.Where(x => partnerIds.Contains(x.PartnerId)).ExecuteDeleteAsync();
            await db.PartnerDebtInvoices.Where(x => partnerIds.Contains(x.PartnerId)).ExecuteDeleteAsync();
        }

        if (whId > 0)
        {
            await db.ExportReceiptDetails.Where(x => x.ExportReceipt.WarehouseId == whId).ExecuteDeleteAsync();
            await db.ExportReceipts.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.ShipmentProofOfDeliveries.Where(x => x.Shipment.WarehouseId == whId).ExecuteDeleteAsync();
            await db.ShipmentTrackingEvents.Where(x => x.Shipment.WarehouseId == whId).ExecuteDeleteAsync();
            await db.ShipmentHandlingUnits.Where(x => x.Shipment.WarehouseId == whId).ExecuteDeleteAsync();
            await db.Shipments.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.DockAppointments.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.Docks.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.HandlingUnitContents.Where(x => x.HandlingUnit.WarehouseId == whId).ExecuteDeleteAsync();
            await db.HandlingUnits.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PackingSessions.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.ShortPickExceptions.Where(x => x.PickingTaskLine.PickingTask.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PickingTaskLines.Where(x => x.PickingTask.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PickingTasks.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.StockAllocations.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.StockReservations.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.Backorders.Where(x => x.SalesOrderLine.SalesOrder.WarehouseId == whId).ExecuteDeleteAsync();
            await db.SalesOrderLines.Where(x => x.SalesOrder.WarehouseId == whId).ExecuteDeleteAsync();
            await db.SalesOrders.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.InventoryLocationMovements.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PutawayTaskItems.Where(x => x.PutawayTask.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PutawayTasks.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.ImportReceiptInventoryIdentities.Where(x => x.ImportReceiptDetail.ImportReceipt.WarehouseId == whId).ExecuteDeleteAsync();
            await db.ImportReceiptDetails.Where(x => x.ImportReceipt.WarehouseId == whId).ExecuteDeleteAsync();
            await db.ImportReceipts.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.PurchaseOrderLines.Where(x => x.PurchaseOrder.WarehouseId == whId).ExecuteDeleteAsync();
            await db.PurchaseOrders.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();

            await db.InventoryTransactions.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.InventoryStocks.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
        }

        if (userIds.Length > 0)
        {
            await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        }

        if (whId > 0)
        {
            await db.WarehouseLocations.Where(x => x.WarehouseId == whId).ExecuteDeleteAsync();
            await db.Warehouses.Where(x => x.Id == whId).ExecuteDeleteAsync();
        }

        if (state.ProductId > 0)
        {
            await db.ProductBarcodes.Where(x => x.ProductId == state.ProductId).ExecuteDeleteAsync();
            await db.Products.Where(x => x.Id == state.ProductId).ExecuteDeleteAsync();
        }

        if (state.UnitId > 0)
        {
            await db.Units.Where(x => x.Id == state.UnitId).ExecuteDeleteAsync();
        }

        if (partnerIds.Length > 0)
        {
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

    private sealed class MvpLifecycleState(string suffix)
    {
        public string Suffix { get; } = suffix;
        public int RoleId { get; set; }
        public int MakerId { get; set; }
        public string MakerUsername { get; set; } = string.Empty;
        public int CheckerId { get; set; }
        public int SupplierId { get; set; }
        public int CustomerId { get; set; }
        public int UnitId { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductBarcode { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public int StorageBinId { get; set; }
        public string StorageBinCode { get; set; } = string.Empty;
        public int StagingBinId { get; set; }
        public string StagingBinCode { get; set; } = string.Empty;
        public int PurchaseOrderId { get; set; }
        public int ImportReceiptId { get; set; }
        public int PutawayTaskId { get; set; }
        public int ApInvoiceId { get; set; }
        public int SalesOrderId { get; set; }
        public int ShipmentId { get; set; }
    }

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }
}
