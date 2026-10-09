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
    public async Task<InventoryTraceabilityResultDto> TraceAsync(
        int? warehouseId = null,
        int? productId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? referenceType = null,
        int? referenceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        // A malformed positive-identifier filter must never be treated as an
        // empty search or silently widened to all accessible warehouses.
        if (warehouseId is <= 0)
            throw new BusinessRuleException("ID kho truy vết phải là số nguyên dương.");
        if (productId is <= 0)
            throw new BusinessRuleException("ID sản phẩm truy vết phải là số nguyên dương.");
        if (referenceId is <= 0)
            throw new BusinessRuleException("ID tham chiếu truy vết phải là số nguyên dương.");

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

        var allowedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowedWarehouseIds = [warehouseId.Value];
        }

        var eventQuery = context.InventoryTransactions.AsNoTracking()
            .Where(x => allowedWarehouseIds.Contains(x.WarehouseId));
        if (warehouseId.HasValue) eventQuery = eventQuery.Where(x => x.WarehouseId == warehouseId.Value);
        if (productId.HasValue) eventQuery = eventQuery.Where(x => x.ProductId == productId.Value);
        if (lot is not null) eventQuery = eventQuery.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        if (serial is not null) eventQuery = eventQuery.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
        if (hasReference)
            eventQuery = eventQuery.Where(x => x.ReferenceType == refType && x.ReferenceId == referenceId);

        // Read one extra event so the operator can distinguish a complete
        // history from a capped window without running a full COUNT query.
        var eventWindow = await ProjectEvents(eventQuery)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.TransactionId)
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
                    .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) &&
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
                        .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) && chainIds.Contains(x.Id)))
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

        if (hasReference && !productId.HasValue && lot is null && serial is null)
        {
            // Reference-only searches must bind stock to identities in the
            // referenced chain. Warehouse-only searches must NOT use recent
            // event IDs: an older or never-posted product may still have stock.
            var eventIds = events.Select(x => x.TransactionId).ToArray();
            if (eventIds.Length == 0)
                return new InventoryTraceabilityResultDto
                {
                    Events = events, EventsTruncated = eventsTruncated
                };

            // A reference trace must resolve exact stock identities BEFORE the
            // 500-row response cap. Capping product-wide buckets first can hide
            // the referenced lot/serial in a warehouse with many locations.
            var referencedIdentities = context.InventoryTransactions.AsNoTracking()
                .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) && eventIds.Contains(x.Id));
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

        return new InventoryTraceabilityResultDto
        {
            CurrentBuckets = buckets,
            Events = events,
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
