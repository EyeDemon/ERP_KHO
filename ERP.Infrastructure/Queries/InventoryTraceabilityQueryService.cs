using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public sealed class InventoryTraceabilityQueryService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization) : IInventoryTraceabilityQueryService
{
    public async Task<IReadOnlyList<InventoryReversalWarehouseDto>> GetAccessibleWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await context.Warehouses.AsNoTracking()
            .Where(x => accessible.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new InventoryReversalWarehouseDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name
            }).ToListAsync(cancellationToken);
    }

    public async Task<InventoryTraceabilityResultDto> TraceAsync(
        int? warehouseId = null,
        int? productId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? referenceType = null,
        int? referenceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default,
        int bucketOffset = 0,
        int eventOffset = 0,
        int? eventAnchorId = null)
    {
        // A malformed positive-identifier filter must never be treated as an
        // empty search or silently widened to all accessible warehouses.
        if (warehouseId is <= 0)
            throw new BusinessRuleException("ID kho truy vết phải là số nguyên dương.");
        if (productId is <= 0)
            throw new BusinessRuleException("ID sản phẩm truy vết phải là số nguyên dương.");
        if (referenceId is <= 0)
            throw new BusinessRuleException("ID tham chiếu truy vết phải là số nguyên dương.");
        // The stock window advances by 500 rows. Reject negative, unaligned
        // and excessive offsets before any warehouse query is executed.
        if (bucketOffset < 0 || bucketOffset > 50_000 || bucketOffset % 500 != 0)
            throw new BusinessRuleException("Trang nhóm tồn không hợp lệ; vui lòng tải lại trang đầu hoặc dùng bước 500 nhóm.");

        var lot = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        var serial = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        var refType = string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim();
        var hasReference = refType is not null || referenceId.HasValue;
        if (hasReference && (refType is null || !referenceId.HasValue))
            throw new BusinessRuleException("Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.");
        // A single explicit warehouse is a bounded, authorized inventory view.
        // Never allow a completely empty query to fan out across all assigned
        // warehouses: a product/identity/reference or warehouse is required.
        if (!warehouseId.HasValue && !productId.HasValue && lot is null && serial is null && !hasReference)
            throw new BusinessRuleException("Cần ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu để truy vết.");
        limit = limit is < 1 or > 500 ? 200 : limit;
        // Ledger history is paged independently of the current-stock buckets.
        // Limit changes reset the page; reject misaligned/oversized offsets
        // before the first warehouse authorization or SQL query.
        if (eventOffset < 0 || eventOffset > 50_000 || eventOffset % limit != 0)
            throw new BusinessRuleException("Trang sự kiện sổ cái không hợp lệ; hãy quay về trang đầu hoặc tải lại theo bước của giới hạn sự kiện.");

        if (eventAnchorId is < 0)
            throw new BusinessRuleException("Giá trị mốc sự kiện sổ cái không hợp lệ.");
        if (eventOffset > 0 && !eventAnchorId.HasValue)
            throw new BusinessRuleException("Thiếu mốc lịch sử khi chuyển trang sự kiện; hãy truy vết lại từ trang đầu.");

        var allowedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowedWarehouseIds = [warehouseId.Value];
        }

        // Snapshot the whole authorized warehouse ledger, not only direct
        // reference matches. An existing reversal/corrective leg can use
        // another ReferenceType and a higher transaction ID than its original.
        var scopedLedger = context.InventoryTransactions.AsNoTracking()
            .Where(x => allowedWarehouseIds.Contains(x.WarehouseId));
        if (warehouseId.HasValue) scopedLedger = scopedLedger.Where(x => x.WarehouseId == warehouseId.Value);
        var eventQuery = scopedLedger;
        if (productId.HasValue) eventQuery = eventQuery.Where(x => x.ProductId == productId.Value);
        if (lot is not null) eventQuery = eventQuery.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        if (serial is not null) eventQuery = eventQuery.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
        if (hasReference)
            eventQuery = eventQuery.Where(x => x.ReferenceType == refType && x.ReferenceId == referenceId);

        // Stock-reference matching intentionally uses the full document history:
        // anchoring the timeline must not hide live current-stock identities.
        var referenceStockEvents = eventQuery;
        // Identity IDs grow monotonically. New commits cannot shift older pages,
        // even when a later transaction carries a backdated TransactionDate.
        var anchorId = eventAnchorId ?? await scopedLedger.MaxAsync(x => (int?)x.Id, cancellationToken) ?? 0;
        eventQuery = eventQuery.Where(x => x.Id <= anchorId);

        // Read one extra event so the operator can distinguish a complete
        // history from a capped window without running a full COUNT query.
        var eventWindow = await ProjectEvents(eventQuery)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.TransactionId)
            .Skip(eventOffset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var eventsTruncated = eventWindow.Count > limit;
        var events = eventWindow.Take(limit).ToList();

        var seedIds = events.Select(x => x.TransactionId).ToArray();
        var reversalMarkers = new List<InventoryTraceabilityEventDto>();
        var chainIds = new HashSet<int>(seedIds);
        if (seedIds.Length > 0)
        {
            reversalMarkers = await ProjectEvents(context.InventoryTransactions.AsNoTracking()
                    .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) && x.Id <= anchorId &&
                                (x.TransactionType == TransactionType.Reversal || x.ReversalOfTransactionId.HasValue) &&
                                (seedIds.Contains(x.Id) ||
                                 (x.ReversalOfTransactionId.HasValue && seedIds.Contains(x.ReversalOfTransactionId.Value)) ||
                                 (x.CorrectiveTransactionId.HasValue && seedIds.Contains(x.CorrectiveTransactionId.Value)) ||
                                 (x.ReversalOfTransactionId == null &&
                                  x.ReferenceType == "InventoryReversal" &&
                                  x.ReferenceId.HasValue &&
                                  seedIds.Contains(x.ReferenceId.Value)))))
                .ToListAsync(cancellationToken);

            foreach (var marker in reversalMarkers)
            {
                chainIds.Add(marker.TransactionId);
                if (marker.ReversalOfTransactionId.HasValue)
                    chainIds.Add(marker.ReversalOfTransactionId.Value);
                if (marker.CorrectiveTransactionId.HasValue)
                    chainIds.Add(marker.CorrectiveTransactionId.Value);
            }

            if (chainIds.Count > seedIds.Length)
            {
                var relatedEvents = await ProjectEvents(context.InventoryTransactions.AsNoTracking()
                        .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) && x.Id <= anchorId && chainIds.Contains(x.Id)))
                    .ToListAsync(cancellationToken);
                foreach (var related in relatedEvents)
                    if (events.All(x => x.TransactionId != related.TransactionId))
                        events.Add(related);
            }
        }

        foreach (var marker in reversalMarkers)
        {
            var returnedMarker = events.FirstOrDefault(x => x.TransactionId == marker.TransactionId);
            if (returnedMarker is not null)
            {
                returnedMarker.ReversalOfTransactionId = marker.ReversalOfTransactionId;
                returnedMarker.CorrectiveTransactionId = marker.CorrectiveTransactionId;
                returnedMarker.ReversalTransactionId = marker.TransactionId;
            }

            var original = marker.ReversalOfTransactionId.HasValue
                ? events.FirstOrDefault(x => x.TransactionId == marker.ReversalOfTransactionId.Value)
                : null;
            if (original is not null)
            {
                original.IsReversed = true;
                original.ReversalTransactionId = marker.TransactionId;
                original.CorrectiveTransactionId = marker.CorrectiveTransactionId;
            }

            if (marker.CorrectiveTransactionId.HasValue)
            {
                var corrective = events.FirstOrDefault(x => x.TransactionId == marker.CorrectiveTransactionId.Value);
                if (corrective is not null)
                    corrective.ReversalTransactionId = marker.TransactionId;
            }
        }

        var effectiveLimit = Math.Max(limit, chainIds.Count);
        events = events.OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionId).Take(effectiveLimit).ToList();

        var stockQuery = context.InventoryStocks.AsNoTracking()
            .Where(x => allowedWarehouseIds.Contains(x.WarehouseId));
        if (warehouseId.HasValue) stockQuery = stockQuery.Where(x => x.WarehouseId == warehouseId.Value);
        if (productId.HasValue) stockQuery = stockQuery.Where(x => x.ProductId == productId.Value);
        if (lot is not null) stockQuery = stockQuery.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        if (serial is not null) stockQuery = stockQuery.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);

        if (hasReference)
        {
            // A document reference is an intersection with every other filter,
            // never a hint that may be dropped when product/lot/serial is set.
            // Use ALL direct reference events in the authorized warehouse scope,
            // not the limited timeline window or expanded reversal markers.
            // Otherwise an older document line disappears from current stock
            // when the operator lowers the event limit.
            var referencedIdentities = referenceStockEvents;
            stockQuery = stockQuery.Where(stock => referencedIdentities.Any(transaction =>
                transaction.ProductId == stock.ProductId &&
                transaction.WarehouseId == stock.WarehouseId &&
                transaction.LotId == stock.LotId &&
                transaction.SerialId == stock.SerialId));
        }

        var buckets = await stockQuery
            .OrderBy(x => x.Product.Code)
            .ThenBy(x => x.WarehouseId)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Status)
            .ThenBy(x => x.LotId)
            .ThenBy(x => x.SerialId)
            .ThenBy(x => x.Id)
            // Stable ordering when multiple lot/serial statuses share a location.
            // Include one sentinel bucket for accurate truncation feedback.
            .Skip(bucketOffset)
            .Take(501)
            .Select(x => new InventoryTraceabilityBucketDto
            {
                InventoryStockId = x.Id,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                ProductName = x.Product.Name,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                LocationId = x.LocationId,
                LocationCode = x.Location == null ? null : x.Location.Code,
                InventoryStatus = x.Status.ToString(),
                LotId = x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber,
                OnHandQuantity = x.Quantity,
                ReservedQuantity = x.ReservedQuantity
            })
            .ToListAsync(cancellationToken);

        var bucketsTruncated = buckets.Count > 500;
        if (bucketsTruncated) buckets = buckets.Take(500).ToList();


        // A bounded document-occurrence view, not inferred ownership or a
        // causal custody graph. Require a product and tracked identity before
        // grouping any references; an explicit document filter is not broadened.
        var relatedDocuments = new List<InventoryTraceabilityRelatedDocumentDto>();
        var relatedDocumentsTruncated = false;
        if (productId.HasValue && (lot is not null || serial is not null) && !hasReference)
        {
            var documentEvents = scopedLedger.Where(x =>
                x.Id <= anchorId &&
                x.ProductId == productId.Value &&
                x.ReferenceType != null && x.ReferenceType != "" &&
                x.ReferenceId.HasValue);
            if (lot is not null)
                documentEvents = documentEvents.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
            if (serial is not null)
                documentEvents = documentEvents.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);

            var documentWindow = await documentEvents
                .GroupBy(x => new { x.WarehouseId, x.ReferenceType, x.ReferenceId })
                .Select(group => new InventoryTraceabilityRelatedDocumentDto
                {
                    WarehouseId = group.Key.WarehouseId,
                    WarehouseName = group.Max(x => x.Warehouse.Name)!,
                    ReferenceType = group.Key.ReferenceType!,
                    ReferenceId = group.Key.ReferenceId!.Value,
                    EventCount = group.Count(),
                    FirstTransactionDate = group.Min(x => x.TransactionDate),
                    LastTransactionDate = group.Max(x => x.TransactionDate),
                    LastTransactionId = group.Max(x => x.Id)
                })
                .OrderByDescending(x => x.LastTransactionId)
                .ThenBy(x => x.WarehouseId)
                .ThenBy(x => x.ReferenceType)
                .ThenBy(x => x.ReferenceId)
                .Take(101)
                .ToListAsync(cancellationToken);
            relatedDocumentsTruncated = documentWindow.Count > 100;
            relatedDocuments = documentWindow.Take(100).ToList();
        }


        // Posted receipt evidence, bounded to an authorized warehouse and ledger anchor.
        var receiptExposures = new List<InventoryTraceabilityReceiptExposureDto>();
        var receiptExposuresTruncated = false;
        if (productId.HasValue && (lot is not null || serial is not null) && !hasReference)
        {
            var postedEvents = scopedLedger.Where(x => x.Id <= anchorId &&
                x.ProductId == productId.Value && x.TransactionType == TransactionType.Import &&
                x.ReferenceType == "ImportReceipt" && x.ReferenceId.HasValue);
            if (lot is not null) postedEvents = postedEvents.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
            if (serial is not null) postedEvents = postedEvents.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
            var receiptWindow = await (
                from transaction in postedEvents
                join receipt in context.ImportReceipts.AsNoTracking()
                    on new { Id = transaction.ReferenceId, transaction.WarehouseId }
                    equals new { Id = (int?)receipt.Id, receipt.WarehouseId }
                where receipt.Status == ReceiptStatus.Posted
                group transaction by new { receipt.Id, receipt.WarehouseId, receipt.Code } into g
                select new InventoryTraceabilityReceiptExposureDto {
                    ReceiptId = g.Key.Id, WarehouseId = g.Key.WarehouseId,
                    ReceiptCode = g.Key.Code, PostedQuantity = g.Sum(x => x.Quantity),
                    LastPostedAt = g.Max(x => x.TransactionDate),
                    LedgerEventCount = g.Count(), LastTransactionId = g.Max(x => x.Id)
                }).OrderByDescending(x => x.LastTransactionId)
                .ThenBy(x => x.WarehouseId).ThenBy(x => x.ReceiptId)
                .Take(101).ToListAsync(cancellationToken);
            receiptExposuresTruncated = receiptWindow.Count > 100;
            receiptExposures = receiptWindow.Take(100).ToList();
        }

        // Gross dispatch impact is derived ONLY from committed SHIP ledger rows
        // joined to the canonical Shipment in the same authorized warehouse.
        // It is a read-only candidate view, not a return/POD/recall decision.
        var shipmentExposures = new List<InventoryTraceabilityShipmentExposureDto>();
        var shipmentExposuresTruncated = false;
        var shipmentPickingEvidence = new List<InventoryTraceabilityShipmentPickingEvidenceDto>();
        var shipmentPickingEvidenceTruncated = false;
        var shipmentHuEvidence = new List<InventoryTraceabilityShipmentHuEvidenceDto>();
        var shipmentHuEvidenceTruncated = false;
        if (productId.HasValue && (lot is not null || serial is not null) && !hasReference)
        {
            var shippedEvents = scopedLedger.Where(x =>
                x.Id <= anchorId && x.ProductId == productId.Value &&
                x.TransactionType == TransactionType.Ship &&
                x.ReferenceType == "Shipment" && x.ReferenceId.HasValue);
            if (lot is not null)
                shippedEvents = shippedEvents.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
            if (serial is not null)
                shippedEvents = shippedEvents.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);

            var exposureWindow = await (
                from transaction in shippedEvents
                join shipment in context.Shipments.AsNoTracking()
                    on new { Id = transaction.ReferenceId, transaction.WarehouseId }
                    equals new { Id = (int?)shipment.Id, shipment.WarehouseId }
                group transaction by new
                {
                    shipment.Id, shipment.WarehouseId, shipment.ShipmentCode,
                    shipment.Status, shipment.DispatchedAt
                } into grouped
                select new
                {
                    ShipmentId = grouped.Key.Id,
                    grouped.Key.WarehouseId,
                    grouped.Key.ShipmentCode,
                    grouped.Key.Status,
                    grouped.Key.DispatchedAt,
                    DispatchedQuantity = grouped.Sum(x => x.Quantity),
                    LedgerEventCount = grouped.Count(),
                    LastTransactionId = grouped.Max(x => x.Id)
                })
                .OrderByDescending(x => x.LastTransactionId)
                .ThenBy(x => x.WarehouseId)
                .ThenBy(x => x.ShipmentId)
                .Take(101)
                .ToListAsync(cancellationToken);
            shipmentExposuresTruncated = exposureWindow.Count > 100;
            shipmentExposures = exposureWindow.Take(100)
                .Select(x => new InventoryTraceabilityShipmentExposureDto
                {
                    ShipmentId = x.ShipmentId,
                    WarehouseId = x.WarehouseId,
                    ShipmentCode = x.ShipmentCode,
                    ShipmentStatus = x.Status.ToString(),
                    DispatchedAt = x.DispatchedAt,
                    DispatchedQuantity = x.DispatchedQuantity,
                    LedgerEventCount = x.LedgerEventCount,
                    LastTransactionId = x.LastTransactionId
                }).ToList();
            // A canonical Shipment -> Packing -> Picking link is stronger
            // evidence than mere document co-occurrence by lot. Require the
            // exact allocated source bucket to have a real SHIP ledger row
            // inside the same authorized warehouse and event anchor.
            var shipmentIds = shipmentExposures.Select(x => x.ShipmentId).ToArray();
            if (shipmentIds.Length > 0)
            {
                var pickingWindow = await (
                    from shipment in context.Shipments.AsNoTracking()
                    join packing in context.PackingSessions.AsNoTracking()
                        on shipment.PackingSessionId equals packing.Id
                    join picking in context.PickingTasks.AsNoTracking()
                        on packing.PickingTaskId equals picking.Id
                    join line in context.PickingTaskLines.AsNoTracking()
                        on picking.Id equals line.PickingTaskId
                    join allocation in context.StockAllocations.AsNoTracking()
                        on line.AllocationId equals allocation.Id
                    where shipmentIds.Contains(shipment.Id) &&
                          allowedWarehouseIds.Contains(shipment.WarehouseId) &&
                          shipment.WarehouseId == packing.WarehouseId &&
                          shipment.WarehouseId == picking.WarehouseId &&
                          shipment.WarehouseId == allocation.WarehouseId &&
                          line.ProductId == productId.Value &&
                          allocation.ProductId == productId.Value &&
                          line.SourceLocationId == allocation.LocationId &&
                          line.PickedQuantity > 0 &&
                          shippedEvents.Any(t =>
                              t.ReferenceId == shipment.Id &&
                              t.WarehouseId == shipment.WarehouseId &&
                              t.ProductId == allocation.ProductId &&
                              t.LocationId == allocation.LocationId &&
                              t.InventoryStatus == allocation.InventoryStatus &&
                              t.LotId == allocation.LotId &&
                              t.SerialId == allocation.SerialId)
                    select new InventoryTraceabilityShipmentPickingEvidenceDto
                    {
                        ShipmentId = shipment.Id,
                        ShipmentCode = shipment.ShipmentCode,
                        WarehouseId = shipment.WarehouseId,
                        PackingSessionId = packing.Id,
                        PackingSessionCode = packing.SessionCode,
                        PickingTaskId = picking.Id,
                        PickingTaskCode = picking.TaskCode,
                        PickingTaskLineId = line.Id,
                        AllocationId = allocation.Id,
                        SourceLocationCode = line.SourceLocation.Code,
                        PickedQuantity = line.PickedQuantity
                    })
                    .OrderByDescending(x => x.ShipmentId)
                    .ThenBy(x => x.PickingTaskLineId)
                    .Take(101)
                    .ToListAsync(cancellationToken);
                shipmentPickingEvidenceTruncated = pickingWindow.Count > 100;
                shipmentPickingEvidence = pickingWindow.Take(100).ToList();

                // HU evidence requires actual packed content on a verified Picking
                // line AND a chain of parent HUs ending at an assigned shipment
                // root. A shared packing session by itself is not proof.
                var verifiedLineIds = shipmentPickingEvidence
                    .Select(x => x.PickingTaskLineId).Distinct().ToArray();
                if (verifiedLineIds.Length > 0)
                {
                    var huWindow = await (
                        from content in context.HandlingUnitContents.AsNoTracking()
                        join hu in context.HandlingUnits.AsNoTracking()
                            on content.HandlingUnitId equals hu.Id
                        join packing in context.PackingSessions.AsNoTracking()
                            on hu.PackingSessionId equals packing.Id
                        join shipment in context.Shipments.AsNoTracking()
                            on packing.Id equals shipment.PackingSessionId
                        join line in context.PickingTaskLines.AsNoTracking()
                            on content.PickingTaskLineId equals line.Id
                        join allocation in context.StockAllocations.AsNoTracking()
                            on line.AllocationId equals allocation.Id
                        where shipmentIds.Contains(shipment.Id) &&
                              verifiedLineIds.Contains(line.Id) &&
                              allowedWarehouseIds.Contains(shipment.WarehouseId) &&
                              shipment.WarehouseId == packing.WarehouseId &&
                              shipment.WarehouseId == hu.WarehouseId &&
                              shipment.WarehouseId == allocation.WarehouseId &&
                              packing.PickingTaskId == line.PickingTaskId &&
                              line.SourceLocationId == allocation.LocationId &&
                              content.ProductId == productId.Value &&
                              line.ProductId == productId.Value &&
                              allocation.ProductId == productId.Value &&
                              content.Quantity > 0 &&
                              shippedEvents.Any(t =>
                                  t.ReferenceId == shipment.Id &&
                                  t.WarehouseId == shipment.WarehouseId &&
                                  t.ProductId == allocation.ProductId &&
                                  t.LocationId == allocation.LocationId &&
                                  t.InventoryStatus == allocation.InventoryStatus &&
                                  t.LotId == allocation.LotId &&
                                  t.SerialId == allocation.SerialId)
                        select new
                        {
                            ShipmentId = shipment.Id, shipment.WarehouseId,
                            shipment.PackingSessionId,
                            HandlingUnitId = hu.Id,
                            PickingTaskLineId = line.Id,
                            PackedQuantity = content.Quantity
                        })
                        .OrderByDescending(x => x.ShipmentId)
                        .ThenBy(x => x.HandlingUnitId)
                        .ThenBy(x => x.PickingTaskLineId)
                        .Take(101)
                        .ToListAsync(cancellationToken);

                    // Every ancestor is read from authorized current-state HU
                    // records. Limit depth to 16; never claim unverified deep
                    // or cyclic chains to be loaded/shipped.
                    var huNodes = new Dictionary<int,
                        (int? ParentId, int PackingSessionId, int WarehouseId,
                         string HuCode, string Barcode)>();
                    var pendingHuIds = huWindow.Select(x => x.HandlingUnitId)
                        .Distinct().ToArray();
                    for (var depth = 0; depth < 16 && pendingHuIds.Length > 0; depth++)
                    {
                        var parents = await context.HandlingUnits.AsNoTracking()
                            .Where(x => pendingHuIds.Contains(x.Id) &&
                                        allowedWarehouseIds.Contains(x.WarehouseId))
                            .Select(x => new
                            {
                                x.Id, x.ParentHandlingUnitId, x.PackingSessionId,
                                x.WarehouseId, x.HuCode, x.Barcode
                            })
                            .ToListAsync(cancellationToken);
                        foreach (var node in parents)
                            huNodes[node.Id] = (node.ParentHandlingUnitId,
                                node.PackingSessionId, node.WarehouseId,
                                node.HuCode, node.Barcode);
                        pendingHuIds = parents
                            .Where(x => x.ParentHandlingUnitId.HasValue &&
                                        !huNodes.ContainsKey(x.ParentHandlingUnitId.Value))
                            .Select(x => x.ParentHandlingUnitId!.Value)
                            .Distinct().ToArray();
                    }

                    var rootWindow = await (
                        from link in context.ShipmentHandlingUnits.AsNoTracking()
                        join shipment in context.Shipments.AsNoTracking()
                            on link.ShipmentId equals shipment.Id
                        join hu in context.HandlingUnits.AsNoTracking()
                            on link.HandlingUnitId equals hu.Id
                        where shipmentIds.Contains(shipment.Id) &&
                              allowedWarehouseIds.Contains(shipment.WarehouseId) &&
                              shipment.WarehouseId == hu.WarehouseId &&
                              shipment.PackingSessionId == hu.PackingSessionId &&
                              hu.ParentHandlingUnitId == null
                        orderby link.ShipmentId, link.HandlingUnitId
                        select new { link.ShipmentId, link.HandlingUnitId })
                        .Take(2001)
                        .ToListAsync(cancellationToken);
                    var assignedRoots = rootWindow.Take(2000)
                        .Select(x => (x.ShipmentId, x.HandlingUnitId))
                        .ToHashSet();

                    shipmentHuEvidenceTruncated = huWindow.Count > 100 ||
                        pendingHuIds.Length > 0 || rootWindow.Count > 2000 ||
                        shipmentPickingEvidenceTruncated ||
                        shipmentExposuresTruncated;
                    foreach (var record in huWindow.Take(100))
                    {
                        if (!shipmentPickingEvidence.Any(x =>
                                x.ShipmentId == record.ShipmentId &&
                                x.PickingTaskLineId == record.PickingTaskLineId))
                            continue;

                        var visited = new HashSet<int>();
                        var chain = new List<(int Id, string Code, string Barcode)>();
                        var cursor = record.HandlingUnitId;
                        var valid = true;
                        while (true)
                        {
                            if (!visited.Add(cursor) ||
                                !huNodes.TryGetValue(cursor, out var node) ||
                                node.WarehouseId != record.WarehouseId ||
                                node.PackingSessionId != record.PackingSessionId)
                            {
                                valid = false;
                                break;
                            }
                            chain.Add((cursor, node.HuCode, node.Barcode));
                            if (!node.ParentId.HasValue) break;
                            cursor = node.ParentId.Value;
                        }
                        if (!valid || chain.Count == 0 ||
                            !assignedRoots.Contains((record.ShipmentId, chain[^1].Id)))
                            continue;

                        chain.Reverse();
                        shipmentHuEvidence.Add(new InventoryTraceabilityShipmentHuEvidenceDto
                        {
                            ShipmentId = record.ShipmentId,
                            WarehouseId = record.WarehouseId,
                            PickingTaskLineId = record.PickingTaskLineId,
                            RootHandlingUnitId = chain[0].Id,
                            RootHandlingUnitCode = chain[0].Code,
                            ContentHandlingUnitId = record.HandlingUnitId,
                            ContentHandlingUnitCode = chain[^1].Code,
                            ContentHandlingUnitBarcode = chain[^1].Barcode,
                            ParentHandlingUnitId = chain.Count > 1 ? chain[^2].Id : null,
                            HierarchyPath = string.Join(" → ", chain.Select(x => x.Code)),
                            PackedQuantity = record.PackedQuantity
                        });
                    }
                }
            }

        }

        return new InventoryTraceabilityResultDto
        {
            CurrentBuckets = buckets,
            Events = events,
            RelatedDocuments = relatedDocuments,
            RelatedDocumentsTruncated = relatedDocumentsTruncated,
            ReceiptExposures = receiptExposures,
            ReceiptExposuresTruncated = receiptExposuresTruncated,
            ShipmentExposures = shipmentExposures,
            ShipmentExposuresTruncated = shipmentExposuresTruncated,
            ShipmentPickingEvidence = shipmentPickingEvidence,
            ShipmentPickingEvidenceTruncated = shipmentPickingEvidenceTruncated,
            ShipmentHuEvidence = shipmentHuEvidence,
            ShipmentHuEvidenceTruncated = shipmentHuEvidenceTruncated,
            EventAnchorId = anchorId,
            EventsTruncated = eventsTruncated,
            BucketsTruncated = bucketsTruncated
        };
    }

    private static IQueryable<InventoryTraceabilityEventDto> ProjectEvents(IQueryable<InventoryTransaction> query) =>
        query.Select(x => new InventoryTraceabilityEventDto
        {
            TransactionId = x.Id,
            ProductId = x.ProductId,
            ProductCode = x.Product.Code,
            ProductName = x.Product.Name,
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse.Name,
            TransactionType = x.TransactionType.ToString(),
            InventoryStatus = x.InventoryStatus.ToString(),
            FromInventoryStatus = x.FromInventoryStatus.HasValue ? x.FromInventoryStatus.Value.ToString() : null,
            ToInventoryStatus = x.ToInventoryStatus.HasValue ? x.ToInventoryStatus.Value.ToString() : null,
            LocationId = x.LocationId,
            LocationCode = x.Location == null ? null : x.Location.Code,
            FromLocationId = x.FromLocationId,
            FromLocationCode = x.FromLocation == null ? null : x.FromLocation.Code,
            ToLocationId = x.ToLocationId,
            ToLocationCode = x.ToLocation == null ? null : x.ToLocation.Code,
            LotId = x.LotId,
            LotNumber = x.Lot == null ? null : x.Lot.LotNumber,
            ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
            SerialId = x.SerialId,
            SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber,
            Quantity = x.Quantity,
            ReferenceType = x.ReferenceType,
            ReferenceId = x.ReferenceId,
            TransactionDate = x.TransactionDate,
            CreatedBy = x.CreatedBy,
            CreatedByName = x.CreatedByUser.FullName ?? x.CreatedByUser.Username,
            Note = x.Note,
            ReasonCode = x.ReasonCode,
            ReversalOfTransactionId = x.ReversalOfTransactionId ??
                (x.TransactionType == TransactionType.Reversal && x.ReferenceType == "InventoryReversal"
                    ? x.ReferenceId
                    : null),
            CorrectiveTransactionId = x.CorrectiveTransactionId
        });
}
