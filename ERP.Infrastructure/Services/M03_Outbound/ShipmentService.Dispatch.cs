using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentService
{
    public Task<ShipmentDto> DispatchAsync(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loaded, "Chỉ Shipment LOADED mới có thể dispatch.");
            if (shipment.HandlingUnits.Count == 0 || shipment.HandlingUnits.Any(x => !x.LoadedAt.HasValue))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Shipment chưa load đủ Handling Unit.");

            var activeHus = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, activeHus);
            if (activeHus.Any(x => x.Status != HandlingUnitStatus.Loaded))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Handling Unit hierarchy chưa ở trạng thái LOADED.");

            var picking = shipment.PackingSession.PickingTask;
            if (picking.Status != PickingTaskStatus.Completed)
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Picking task chưa hoàn tất.");
            if (picking.Lines.Any(x => x.Status is not PickingTaskLineStatus.Picked and not PickingTaskLineStatus.Resolved))
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Picking line chưa hoàn tất.");
            if (picking.Lines.SelectMany(x => x.ShortPicks).Any(x => x.Status == ShortPickExceptionStatus.Open))
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Shipment còn ngoại lệ thiếu hàng chưa xử lý.");

            var zeroPickedLines = picking.Lines.Where(x => x.PickedQuantity <= 0).ToList();
            if (zeroPickedLines.Any(x =>
                    x.PickedQuantity < 0 ||
                    x.Status != PickingTaskLineStatus.Resolved ||
                    x.Allocation.Status is not StockAllocationStatus.Released and not StockAllocationStatus.Reallocated))
                throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Picking line có PickedQuantity = 0 nhưng allocation chưa được release/reallocate.");

            var dispatchableLines = picking.Lines
                .Where(x => x.PickedQuantity > 0)
                .ToList();
            var reservationIds = dispatchableLines
                .Select(x => x.Allocation.ReservationId)
                .Distinct()
                .Order()
                .ToArray();
            if (reservationIds.Length == 0)
                throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Shipment không có reservation/allocation để dispatch.");

            var reservations = await context.StockReservations
                .Where(x => reservationIds.Contains(x.Id))
                .OrderBy(x => x.WarehouseId)
                .ThenBy(x => x.ProductId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);
            if (reservations.Count != reservationIds.Length)
                throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Reservation của Shipment không còn đầy đủ.");

            var alreadyShippedTx = await context.InventoryTransactions.AsNoTracking().AnyAsync(
                t => t.WarehouseId == shipment.WarehouseId &&
                     t.ReferenceType == "Shipment" &&
                     t.ReferenceId == shipment.Id &&
                     t.TransactionType == TransactionType.Ship,
                token);
            if (alreadyShippedTx)
                throw Conflict("SHIPMENT_ALREADY_DISPATCHED", "Shipment này đã được ghi nhận bút toán xuất kho trước đó.");

            if (shipment.SourceType == "ExportReceipt" && shipment.SourceId.HasValue)
            {
                var sourceReceiptCheck = await context.ExportReceipts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == shipment.SourceId.Value, token);
                if (sourceReceiptCheck is null)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Không tìm thấy phiếu xuất nguồn của Shipment.");
                if (sourceReceiptCheck.Status != ReceiptStatus.Approved)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Phiếu xuất nguồn không còn ở trạng thái APPROVED.");

                var receiptAlreadyDeducted = await context.InventoryTransactions.AsNoTracking().AnyAsync(
                    t => t.WarehouseId == shipment.WarehouseId &&
                         t.ReferenceType == "ExportReceipt" &&
                         t.ReferenceId == sourceReceiptCheck.Id &&
                         (t.TransactionType == TransactionType.Export || t.TransactionType == TransactionType.Ship),
                    token);
                if (receiptAlreadyDeducted)
                    throw Conflict("SHIPMENT_SOURCE_ALREADY_DISPATCHED", "Phiếu xuất nguồn đã phát sinh bút toán trừ tồn kho trước đó.");
            }
            else if (shipment.SourceType == "SalesOrder" && shipment.SourceId.HasValue)
            {
                var dispatchedLinkedReceiptIds = await context.ExportReceipts.AsNoTracking()
                    .Where(x => (x.ShipmentId == shipment.Id || x.SalesOrderId == shipment.SourceId.Value) &&
                                x.Status == ReceiptStatus.Dispatched)
                    .Select(x => x.Id)
                    .ToListAsync(token);
                if (dispatchedLinkedReceiptIds.Count > 0)
                    throw Conflict("SHIPMENT_SOURCE_ALREADY_DISPATCHED", "Đơn bán hàng đã có phiếu xuất kho ở trạng thái DISPATCHED, không thể trừ tồn lần 2.");
            }

            await context.AcquireInventoryAllocationLocksAsync(
                reservations.Select(x => (x.WarehouseId, x.ProductId)),
                token);

            var ledgerBuckets = new Dictionary<(int ProductId, int LocationId, InventoryStatus InventoryStatus, int? LotId, int? SerialId), decimal>();
            foreach (var reservation in reservations)
            {
                if (reservation.WarehouseId != shipment.WarehouseId)
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Reservation không cùng kho với Shipment.");

                var lines = dispatchableLines
                    .Where(x => x.Allocation.ReservationId == reservation.Id)
                    .OrderBy(x => x.Allocation.LocationId)
                    .ThenBy(x => x.Id)
                    .ToList();
                var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
                if (remaining <= 0 ||
                    lines.Sum(x => x.Allocation.Quantity) != remaining ||
                    reservation.AllocatedQuantity != remaining ||
                    lines.Any(x => x.Allocation.Status != StockAllocationStatus.Picked ||
                                   x.PickedQuantity != x.Allocation.Quantity))
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Allocation/Picked quantity không còn khớp reservation.");

                var expectedByBucket = lines
                    .GroupBy(x => new
                    {
                        x.Allocation.LocationId,
                        x.Allocation.InventoryStatus,
                        x.Allocation.LotId,
                        x.Allocation.SerialId
                    })
                    .ToDictionary(
                        x => (x.Key.LocationId, x.Key.InventoryStatus, x.Key.LotId, x.Key.SerialId),
                        x => x.Sum(y => y.Allocation.Quantity));

                var consumptions = await stockReservationService.ConsumeAsync(
                    reservation,
                    currentUser.UserId,
                    token);
                var actualByBucket = consumptions
                    .GroupBy(x => (x.LocationId, x.Status, x.LotId, x.SerialId))
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
                if (consumptions.Sum(x => x.Quantity) != remaining ||
                    actualByBucket.Count != expectedByBucket.Count ||
                    actualByBucket.Any(x => !expectedByBucket.TryGetValue(x.Key, out var expected) || expected != x.Value))
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Inventory consumption không khớp Allocation Location/Status/Lot/Serial.");

                foreach (var bucket in actualByBucket.OrderBy(x => x.Key.LocationId)
                             .ThenBy(x => x.Key.LotId)
                             .ThenBy(x => x.Key.SerialId))
                {
                    var key = (
                        reservation.ProductId,
                        bucket.Key.LocationId,
                        bucket.Key.Status,
                        bucket.Key.LotId,
                        bucket.Key.SerialId);
                    ledgerBuckets[key] = ledgerBuckets.GetValueOrDefault(key) + bucket.Value;
                }
            }

            var dispatchedAt = DateTime.UtcNow;
            foreach (var bucket in ledgerBuckets
                         .OrderBy(x => x.Key.ProductId)
                         .ThenBy(x => x.Key.LocationId)
                         .ThenBy(x => x.Key.InventoryStatus)
                         .ThenBy(x => x.Key.LotId)
                         .ThenBy(x => x.Key.SerialId))
            {
                context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = bucket.Key.ProductId,
                    WarehouseId = shipment.WarehouseId,
                    LocationId = bucket.Key.LocationId,
                    LotId = bucket.Key.LotId,
                    SerialId = bucket.Key.SerialId,
                    InventoryStatus = bucket.Key.InventoryStatus,
                    TransactionType = TransactionType.Ship,
                    Quantity = bucket.Value,
                    ReferenceId = shipment.Id,
                    ReferenceType = "Shipment",
                    TransactionDate = dispatchedAt,
                    CreatedBy = currentUser.UserId,
                    Note = $"Shipment dispatch {shipment.ShipmentCode}"
                });
            }

            foreach (var hu in activeHus) hu.Status = HandlingUnitStatus.Shipped;
            var previousStatus = shipment.Status;
            shipment.Status = ShipmentStatus.Dispatched;
            shipment.DispatchedAt = dispatchedAt;
            shipment.DispatchedBy = currentUser.UserId;
            AddTrackingEvent(
                shipment,
                "ShipmentDispatched",
                previousStatus,
                ShipmentStatus.Dispatched,
                dispatchedAt,
                null,
                "Đã xác nhận xuất kho và trừ tồn thực tế.");

            if (shipment.SourceType == "ExportReceipt" && shipment.SourceId.HasValue)
            {
                var receipt = await context.ExportReceipts
                    .Include(x => x.Details)
                    .SingleOrDefaultAsync(x => x.Id == shipment.SourceId.Value, token);
                if (receipt is null)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Không tìm thấy phiếu xuất nguồn của Shipment.");
                if (receipt.Status != ReceiptStatus.Approved)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Phiếu xuất nguồn không còn ở trạng thái APPROVED.");

                receipt.Status = ReceiptStatus.Dispatched;
                receipt.DispatchedAt = dispatchedAt;
                receipt.DispatchedBy = currentUser.UserId;
                receipt.ShipmentId = shipment.Id;

                AddAudit(
                    "Shipment.Dispatched",
                    shipment,
                    $"Reservations: {reservations.Count}; LedgerRows: {ledgerBuckets.Count}; Quantity: {ledgerBuckets.Values.Sum()}");
                await context.SaveChangesAsync(token);

                if (partnerDebt is not null && receipt.CustomerId.HasValue)
                {
                    var subTotal = receipt.Details.Sum(d => decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero));
                    var taxAmount = receipt.Details.Sum(d =>
                    {
                        var lineSub = decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero);
                        return decimal.Round(lineSub * (d.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
                    });
                    await partnerDebt.RecordOutboundArDebtAsync(
                        receipt.CustomerId.Value,
                        "ExportReceipt",
                        receipt.Id,
                        receipt.Code,
                        subTotal,
                        taxAmount,
                        currentUser.UserId,
                        "VND",
                        token);
                }
                return;
            }

            if (shipment.SourceType == "SalesOrder" && shipment.SourceId.HasValue)
            {
                var salesOrder = await context.SalesOrders.AsNoTracking()
                    .Include(x => x.Customer)
                    .Include(x => x.Lines)
                        .ThenInclude(l => l.Product)
                            .ThenInclude(p => p.Unit)
                    .SingleOrDefaultAsync(x => x.Id == shipment.SourceId.Value, token);

                AddAudit(
                    "Shipment.Dispatched",
                    shipment,
                    $"Reservations: {reservations.Count}; LedgerRows: {ledgerBuckets.Count}; Quantity: {ledgerBuckets.Values.Sum()}");

                if (salesOrder is not null)
                {
                    var existingReceipt = await context.ExportReceipts
                        .Include(x => x.Details)
                        .FirstOrDefaultAsync(
                            x => x.ShipmentId == shipment.Id ||
                                 (x.SalesOrderId == salesOrder.Id && x.Status == ReceiptStatus.Approved),
                            token);

                    if (existingReceipt is not null && existingReceipt.Status == ReceiptStatus.Approved)
                    {
                        existingReceipt.Status = ReceiptStatus.Dispatched;
                        existingReceipt.DispatchedAt = dispatchedAt;
                        existingReceipt.DispatchedBy = currentUser.UserId;
                        existingReceipt.ShipmentId = shipment.Id;
                    }

                    if (existingReceipt is null)
                    {
                        existingReceipt = new ExportReceipt
                        {
                            Code = $"ER-SHP-{shipment.Id:D6}",
                            WarehouseId = shipment.WarehouseId,
                            Status = ReceiptStatus.Dispatched,
                            DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
                            CustomerId = salesOrder.CustomerId,
                            CustomerCodeSnapshot = salesOrder.Customer?.Code,
                            CustomerNameSnapshot = salesOrder.Customer?.Name,
                            SalesOrderId = salesOrder.Id,
                            ShipmentId = shipment.Id,
                            CreatedBy = currentUser.UserId,
                            CreatedAt = dispatchedAt,
                            ApprovedBy = currentUser.UserId,
                            ApprovedAt = dispatchedAt,
                            DispatchedBy = currentUser.UserId,
                            DispatchedAt = dispatchedAt,
                            Note = $"Phiếu xuất tự động từ Shipment {shipment.ShipmentCode} (SO {salesOrder.OrderCode})",
                            Details = new List<ExportReceiptDetail>()
                        };

                        foreach (var group in dispatchableLines.GroupBy(x => x.ProductId).OrderBy(g => g.Key))
                        {
                            var soLine = salesOrder.Lines.FirstOrDefault(l => l.ProductId == group.Key);
                            var shippedBaseQty = group.Sum(x => x.PickedQuantity);
                            var unitPricePerBase = soLine?.UnitPrice ?? 0m;
                            var taxPercent = soLine?.TaxPercent ?? 0m;
                            var rawUomCode = (!string.IsNullOrWhiteSpace(soLine?.UomCode) ? soLine.UomCode : soLine?.Product?.Unit?.Code)?.Trim();
                            var uomCode = string.IsNullOrWhiteSpace(rawUomCode) ||
                                          string.Equals(rawUomCode, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(rawUomCode, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                                ? "THÙNG"
                                : rawUomCode;
                            var rawUomName = soLine?.Product?.Unit?.Name?.Trim();
                            var uomName = string.IsNullOrWhiteSpace(rawUomName) ||
                                          uomCode == "THÙNG" ||
                                          string.Equals(rawUomName, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(rawUomName, "QA-PCS", StringComparison.OrdinalIgnoreCase)
                                ? "THÙNG"
                                : rawUomName;

                            existingReceipt.Details.Add(new ExportReceiptDetail
                            {
                                ProductId = group.Key,
                                Quantity = shippedBaseQty,
                                UnitPrice = unitPricePerBase,
                                TaxPercent = taxPercent,
                                BaseUomIdSnapshot = soLine?.Product?.UnitId,
                                BaseUomCodeSnapshot = uomCode,
                                BaseUomNameSnapshot = uomName,
                                BaseUomDecimalPlacesSnapshot = soLine?.Product?.Unit?.DecimalPlaces,
                                Note = $"Xuất theo Shipment {shipment.ShipmentCode.Trim()}"
                            });
                        }

                        context.ExportReceipts.Add(existingReceipt);
                    }

                    await context.SaveChangesAsync(token);

                    if (partnerDebt is not null && salesOrder.CustomerId > 0)
                    {
                        var subTotal = existingReceipt.Details.Sum(d => decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero));
                        var taxAmount = existingReceipt.Details.Sum(d =>
                        {
                            var lineSub = decimal.Round(d.Quantity * d.UnitPrice, 2, MidpointRounding.AwayFromZero);
                            return decimal.Round(lineSub * (d.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
                        });
                        await partnerDebt.RecordOutboundArDebtAsync(
                            salesOrder.CustomerId,
                            "ExportReceipt",
                            existingReceipt.Id,
                            existingReceipt.Code,
                            subTotal,
                            taxAmount,
                            currentUser.UserId,
                            salesOrder.Currency,
                            token);
                    }
                }
                return;
            }

            AddAudit(
                "Shipment.Dispatched",
                shipment,
                $"Reservations: {reservations.Count}; LedgerRows: {ledgerBuckets.Count}; Quantity: {ledgerBuckets.Values.Sum()}");
        }, cancellationToken);
}
