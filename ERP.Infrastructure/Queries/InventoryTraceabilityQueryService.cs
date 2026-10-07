using ERP.Application.DTOs;
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
        var lot = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        var serial = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        var refType = string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim();
        var hasReference = refType is not null || referenceId.HasValue;
        if (hasReference && (refType is null || !referenceId.HasValue))
            throw new BusinessRuleException("ReferenceType và ReferenceId phải được nhập cùng nhau.");
        if (!productId.HasValue && lot is null && serial is null && !hasReference)
            throw new BusinessRuleException("Cần ít nhất Product, Lot, Serial hoặc Reference để truy vết.");
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

        var events = await ProjectEvents(eventQuery)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.TransactionId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var seedIds = events.Select(x => x.TransactionId).ToArray();
        var reversalMarkers = new List<InventoryTraceabilityEventDto>();
        var chainIds = new HashSet<int>(seedIds);
        if (seedIds.Length > 0)
        {
            reversalMarkers = await ProjectEvents(context.InventoryTransactions.AsNoTracking()
                    .Where(x => allowedWarehouseIds.Contains(x.WarehouseId) &&
                                x.TransactionType == TransactionType.Reversal &&
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

            marker.ReversalTransactionId = marker.TransactionId;
        }

        var effectiveLimit = Math.Max(limit, chainIds.Count);
        events = events.OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionId).Take(effectiveLimit).ToList();

        var stockQuery = context.InventoryStocks.AsNoTracking()
            .Where(x => allowedWarehouseIds.Contains(x.WarehouseId));
        if (warehouseId.HasValue) stockQuery = stockQuery.Where(x => x.WarehouseId == warehouseId.Value);
        if (productId.HasValue) stockQuery = stockQuery.Where(x => x.ProductId == productId.Value);
        if (lot is not null) stockQuery = stockQuery.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        if (serial is not null) stockQuery = stockQuery.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);

        if (!productId.HasValue && lot is null && serial is null)
        {
            var productIds = events.Select(x => x.ProductId).Distinct().ToArray();
            if (productIds.Length == 0)
                return new InventoryTraceabilityResultDto { Events = events };
            stockQuery = stockQuery.Where(x => productIds.Contains(x.ProductId));
        }

        var buckets = await stockQuery
            .OrderBy(x => x.Product.Code)
            .ThenBy(x => x.WarehouseId)
            .ThenBy(x => x.LocationId)
            .Take(500)
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

        if (hasReference && !productId.HasValue && lot is null && serial is null)
        {
            var keys = events
                .Select(x => (x.ProductId, x.WarehouseId, x.LotId, x.SerialId))
                .ToHashSet();
            buckets = buckets
                .Where(x => keys.Contains((x.ProductId, x.WarehouseId, x.LotId, x.SerialId)))
                .ToList();
        }

        return new InventoryTraceabilityResultDto
        {
            CurrentBuckets = buckets,
            Events = events
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
            ReversalOfTransactionId = x.ReversalOfTransactionId ??
                (x.TransactionType == TransactionType.Reversal && x.ReferenceType == "InventoryReversal"
                    ? x.ReferenceId
                    : null),
            CorrectiveTransactionId = x.CorrectiveTransactionId
        });
}
