using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public sealed partial class InventoryTraceabilityQueryService
{
    private sealed record CurrentBucketsLoadResult(
        List<InventoryTraceabilityBucketDto> Buckets,
        bool BucketsTruncated);

    private sealed record DocumentAndReceiptExposuresResult(
        List<InventoryTraceabilityRelatedDocumentDto> RelatedDocuments,
        bool RelatedDocumentsTruncated,
        List<InventoryTraceabilityReceiptExposureDto> ReceiptExposures,
        bool ReceiptExposuresTruncated);

    private async Task<List<InventoryTraceabilityEventDto>> ExpandAndLinkReversalChainAsync(
        List<InventoryTraceabilityEventDto> events,
        IReadOnlyCollection<int> allowedWarehouseIds,
        int anchorId,
        int limit,
        CancellationToken cancellationToken)
    {
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
        return events.OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionId).Take(effectiveLimit).ToList();
    }

    private async Task<CurrentBucketsLoadResult> LoadCurrentStockBucketsAsync(
        IReadOnlyCollection<int> allowedWarehouseIds,
        int? warehouseId,
        int? productId,
        string? lot,
        string? serial,
        bool hasReference,
        IQueryable<InventoryTransaction> referenceStockEvents,
        int bucketOffset,
        CancellationToken cancellationToken)
    {
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
                ProductCode = x.Product.Code.Trim(),
                ProductName = x.Product.Name.Trim(),
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name.Trim(),
                LocationId = x.LocationId,
                LocationCode = x.Location == null ? null : x.Location.Code.Trim(),
                InventoryStatus = x.Status.ToString(),
                LotId = x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber.Trim(),
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber.Trim(),
                OnHandQuantity = x.Quantity,
                ReservedQuantity = x.ReservedQuantity
            })
            .ToListAsync(cancellationToken);

        var bucketsTruncated = buckets.Count > 500;
        if (bucketsTruncated) buckets = buckets.Take(500).ToList();
        return new CurrentBucketsLoadResult(buckets, bucketsTruncated);
    }

    private async Task<DocumentAndReceiptExposuresResult> LoadDocumentAndReceiptExposuresAsync(
        IQueryable<InventoryTransaction> scopedLedger,
        int anchorId,
        int? productId,
        string? lot,
        string? serial,
        bool hasReference,
        CancellationToken cancellationToken)
    {
        // A bounded document-occurrence view, not inferred ownership or a
        // causal custody graph. Require a product and tracked identity before
        // grouping any references; an explicit document filter is not broadened.
        var relatedDocuments = new List<InventoryTraceabilityRelatedDocumentDto>();
        var relatedDocumentsTruncated = false;
        var receiptExposures = new List<InventoryTraceabilityReceiptExposureDto>();
        var receiptExposuresTruncated = false;

        if (!productId.HasValue || (lot is null && serial is null) || hasReference)
        {
            return new DocumentAndReceiptExposuresResult(
                relatedDocuments, relatedDocumentsTruncated, receiptExposures, receiptExposuresTruncated);
        }

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
        relatedDocuments = documentWindow.Take(100)
            .Select(d =>
            {
                d.WarehouseName = (d.WarehouseName ?? string.Empty).Trim();
                d.ReferenceType = (d.ReferenceType ?? string.Empty).Trim();
                return d;
            })
            .ToList();

        // Posted receipt evidence, bounded to an authorized warehouse and ledger anchor.
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
            select new InventoryTraceabilityReceiptExposureDto
            {
                ReceiptId = g.Key.Id,
                WarehouseId = g.Key.WarehouseId,
                ReceiptCode = g.Key.Code.Trim(),
                PostedQuantity = g.Sum(x => x.Quantity),
                LastPostedAt = g.Max(x => x.TransactionDate),
                LedgerEventCount = g.Count(),
                LastTransactionId = g.Max(x => x.Id)
            }).OrderByDescending(x => x.LastTransactionId)
            .ThenBy(x => x.WarehouseId).ThenBy(x => x.ReceiptId)
            .Take(101).ToListAsync(cancellationToken);
        receiptExposuresTruncated = receiptWindow.Count > 100;
        receiptExposures = receiptWindow.Take(100).ToList();

        return new DocumentAndReceiptExposuresResult(
            relatedDocuments, relatedDocumentsTruncated, receiptExposures, receiptExposuresTruncated);
    }
}
