using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public sealed partial class InventoryTraceabilityQueryService
{
    private sealed record ShipmentTraceabilityEvidenceResult(
        List<InventoryTraceabilityShipmentExposureDto> ShipmentExposures,
        bool ShipmentExposuresTruncated,
        List<InventoryTraceabilityShipmentPickingEvidenceDto> ShipmentPickingEvidence,
        bool ShipmentPickingEvidenceTruncated,
        List<InventoryTraceabilityShipmentHuEvidenceDto> ShipmentHuEvidence,
        bool ShipmentHuEvidenceTruncated,
        List<InventoryTraceabilityShipmentExportSourceDto> ShipmentExportSources,
        bool ShipmentExportSourcesTruncated);

    private async Task<ShipmentTraceabilityEvidenceResult> LoadShipmentTraceabilityEvidenceAsync(
        IQueryable<InventoryTransaction> scopedLedger,
        IReadOnlyCollection<int> allowedWarehouseIds,
        int anchorId,
        int? productId,
        string? lot,
        string? serial,
        bool hasReference,
        CancellationToken cancellationToken)
    {
        // Gross dispatch impact is derived ONLY from committed SHIP ledger rows
        // joined to the canonical Shipment in the same authorized warehouse.
        // It is a read-only candidate view, not a return/POD/recall decision.
        var shipmentExposures = new List<InventoryTraceabilityShipmentExposureDto>();
        var shipmentExposuresTruncated = false;
        var shipmentPickingEvidence = new List<InventoryTraceabilityShipmentPickingEvidenceDto>();
        var shipmentPickingEvidenceTruncated = false;
        var shipmentHuEvidence = new List<InventoryTraceabilityShipmentHuEvidenceDto>();
        var shipmentHuEvidenceTruncated = false;
        var shipmentExportSources = new List<InventoryTraceabilityShipmentExportSourceDto>();
        var shipmentExportSourcesTruncated = false;

        if (!productId.HasValue || (lot is null && serial is null) || hasReference)
        {
            return new ShipmentTraceabilityEvidenceResult(
                shipmentExposures, shipmentExposuresTruncated,
                shipmentPickingEvidence, shipmentPickingEvidenceTruncated,
                shipmentHuEvidence, shipmentHuEvidenceTruncated,
                shipmentExportSources, shipmentExportSourcesTruncated);
        }

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
                ShipmentCode = x.ShipmentCode.Trim(),
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
        if (shipmentIds.Length == 0)
        {
            return new ShipmentTraceabilityEvidenceResult(
                shipmentExposures, shipmentExposuresTruncated,
                shipmentPickingEvidence, shipmentPickingEvidenceTruncated,
                shipmentHuEvidence, shipmentHuEvidenceTruncated,
                shipmentExportSources, shipmentExportSourcesTruncated);
        }

        // A canonical outbound document link is proven by BOTH source
        // IDs on Shipment and Picking, a dispatched ExportReceipt in
        // the same authorized warehouse, its real product detail,
        // and the anchored SHIP ledger already used for shipmentIds.
        // ExportReceipt detail is product-level, never lot provenance.
        var sources = await (
            from shipment in context.Shipments.AsNoTracking()
            join packing in context.PackingSessions.AsNoTracking()
                on shipment.PackingSessionId equals packing.Id
            join picking in context.PickingTasks.AsNoTracking()
                on packing.PickingTaskId equals picking.Id
            join exportReceipt in context.ExportReceipts.AsNoTracking()
                on new { Id = shipment.SourceId, shipment.WarehouseId }
                equals new { Id = (int?)exportReceipt.Id, exportReceipt.WarehouseId }
            where shipmentIds.Contains(shipment.Id) &&
                  allowedWarehouseIds.Contains(shipment.WarehouseId) &&
                  shipment.SourceType == "ExportReceipt" &&
                  picking.SourceType == "ExportReceipt" &&
                  picking.SourceId == shipment.SourceId &&
                  shipment.WarehouseId == packing.WarehouseId &&
                  shipment.WarehouseId == picking.WarehouseId &&
                  exportReceipt.Status == ReceiptStatus.Dispatched &&
                  exportReceipt.DispatchedAt.HasValue &&
                  context.ExportReceiptDetails.AsNoTracking().Any(d =>
                      d.ExportReceiptId == exportReceipt.Id &&
                      d.ProductId == productId.Value && d.Quantity > 0)
            select new InventoryTraceabilityShipmentExportSourceDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode.Trim(),
                WarehouseId = shipment.WarehouseId,
                PickingTaskId = picking.Id,
                ExportReceiptId = exportReceipt.Id,
                ExportReceiptCode = exportReceipt.Code.Trim(),
                ExportReceiptDispatchedAt = exportReceipt.DispatchedAt,
                ExportReceiptProductQuantity = context.ExportReceiptDetails.AsNoTracking()
                    .Where(d => d.ExportReceiptId == exportReceipt.Id &&
                                d.ProductId == productId.Value)
                    .Sum(d => d.Quantity)
            })
            .OrderByDescending(x => x.ShipmentId)
            .ThenBy(x => x.ExportReceiptId)
            .Take(101)
            .ToListAsync(cancellationToken);
        shipmentExportSourcesTruncated =
            sources.Count > 100 || shipmentExposuresTruncated;
        shipmentExportSources = sources.Take(100).ToList();

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
                ShipmentCode = shipment.ShipmentCode.Trim(),
                WarehouseId = shipment.WarehouseId,
                PackingSessionId = packing.Id,
                PackingSessionCode = packing.SessionCode.Trim(),
                PickingTaskId = picking.Id,
                PickingTaskCode = picking.TaskCode.Trim(),
                PickingTaskLineId = line.Id,
                AllocationId = allocation.Id,
                SourceLocationCode = line.SourceLocation.Code.Trim(),
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
            (shipmentHuEvidence, shipmentHuEvidenceTruncated) = await LoadShipmentHuHierarchyEvidenceAsync(
                shippedEvents, shipmentIds, verifiedLineIds, allowedWarehouseIds, productId.Value,
                shipmentPickingEvidence, shipmentPickingEvidenceTruncated, shipmentExposuresTruncated,
                cancellationToken);
        }

        return new ShipmentTraceabilityEvidenceResult(
            shipmentExposures, shipmentExposuresTruncated,
            shipmentPickingEvidence, shipmentPickingEvidenceTruncated,
            shipmentHuEvidence, shipmentHuEvidenceTruncated,
            shipmentExportSources, shipmentExportSourcesTruncated);
    }

    private async Task<(List<InventoryTraceabilityShipmentHuEvidenceDto> Evidence, bool Truncated)> LoadShipmentHuHierarchyEvidenceAsync(
        IQueryable<InventoryTransaction> shippedEvents,
        int[] shipmentIds,
        int[] verifiedLineIds,
        IReadOnlyCollection<int> allowedWarehouseIds,
        int productId,
        List<InventoryTraceabilityShipmentPickingEvidenceDto> shipmentPickingEvidence,
        bool shipmentPickingEvidenceTruncated,
        bool shipmentExposuresTruncated,
        CancellationToken cancellationToken)
    {
        var shipmentHuEvidence = new List<InventoryTraceabilityShipmentHuEvidenceDto>();
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
                  content.ProductId == productId &&
                  line.ProductId == productId &&
                  allocation.ProductId == productId &&
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
                    node.HuCode.Trim(), node.Barcode.Trim());
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

        var shipmentHuEvidenceTruncated = huWindow.Count > 100 ||
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

        return (shipmentHuEvidence, shipmentHuEvidenceTruncated);
    }
}
