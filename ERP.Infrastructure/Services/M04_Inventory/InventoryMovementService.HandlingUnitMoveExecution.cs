using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryMovementService
{
    private async Task<(List<MovedHandlingUnitBucketDto> MovedItems, decimal TotalMovedQty)> ExecuteHandlingUnitBucketMovesAsync(
        int warehouseId,
        HandlingUnit hu,
        WarehouseLocation destLocation,
        List<InventoryStock> sourceBuckets,
        List<InventoryStock> alreadyAtDestBuckets,
        string reasonText,
        DateTime now,
        int actorId,
        CancellationToken cancellationToken)
    {
        var productIds = sourceBuckets.Select(s => s.ProductId).Distinct().OrderBy(id => id).ToList();
        if (productIds.Count > 0)
        {
            await context.AcquireInventoryAllocationLocksAsync(warehouseId, productIds, cancellationToken);
        }

        var orderedBuckets = sourceBuckets
            .OrderBy(s => s.TenantId)
            .ThenBy(s => s.WarehouseId)
            .ThenBy(s => s.LocationId ?? -1)
            .ThenBy(s => s.ProductId)
            .ThenBy(s => s.LotId ?? -1)
            .ThenBy(s => s.SerialId ?? -1)
            .ThenBy(s => s.HandlingUnitId ?? -1)
            .ThenBy(s => (int)s.Status)
            .ThenBy(s => s.OwnerId ?? -1)
            .ThenBy(s => s.Id)
            .ToList();

        var movedItems = new List<MovedHandlingUnitBucketDto>();
        decimal totalMovedQty = 0m;

        foreach (var src in orderedBuckets)
        {
            if (src.LocationId is null || src.LocationId.Value == destLocation.Id)
                continue;

            var fromLocId = src.LocationId.Value;
            var fromLocCode = src.Location?.Code ?? $"LOC-{fromLocId}";
            var availableToMove = src.Quantity - Math.Max(src.ReservedQuantity, src.AllocatedQuantity) - src.LockedQuantity;
            if (availableToMove <= 0m)
                continue;

            if (locks != null)
            {
                await locks.EnsureBucketUnlockedAsync(warehouseId, fromLocId, src.ProductId, src.Status, src.LotId, src.SerialId, cancellationToken);
                await locks.EnsureBucketUnlockedAsync(warehouseId, destLocation.Id, src.ProductId, src.Status, src.LotId, src.SerialId, cancellationToken);
            }

            var targetHuId = src.HandlingUnitId ?? hu?.Id;
            src.Quantity -= availableToMove;
            src.LastUpdated = now;
            src.RefreshGrainHash();

            var destBucket = await context.InventoryStocks
                .FirstOrDefaultAsync(
                    s => s.WarehouseId == warehouseId &&
                         s.ProductId == src.ProductId &&
                         s.LocationId == destLocation.Id &&
                         s.Status == src.Status &&
                         s.LotId == src.LotId &&
                         s.SerialId == src.SerialId &&
                         s.HandlingUnitId == targetHuId &&
                         s.OwnerId == src.OwnerId,
                    cancellationToken);

            if (destBucket == null)
            {
                destBucket = new InventoryStock
                {
                    TenantId = src.TenantId,
                    WarehouseId = warehouseId,
                    ProductId = src.ProductId,
                    LocationId = destLocation.Id,
                    Status = src.Status,
                    LotId = src.LotId,
                    SerialId = src.SerialId,
                    HandlingUnitId = targetHuId,
                    OwnerId = src.OwnerId,
                    MovingAverageCost = src.MovingAverageCost,
                    Quantity = availableToMove,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    LastUpdated = now
                };
                destBucket.RefreshGrainHash();
                context.InventoryStocks.Add(destBucket);
            }
            else
            {
                destBucket.Quantity += availableToMove;
                destBucket.LastUpdated = now;
                destBucket.RefreshGrainHash();
            }

            var locMove = new InventoryLocationMovement
            {
                WarehouseId = warehouseId,
                ProductId = src.ProductId,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                BaseQuantity = availableToMove,
                EnteredQuantity = availableToMove,
                EnteredUnitCode = "THÙNG",
                ReferenceType = "HandlingUnitMove",
                CreatedBy = actorId,
                CreatedAt = now
            };
            context.InventoryLocationMovements.Add(locMove);
            await context.SaveChangesAsync(cancellationToken);

            locMove.ReferenceId = locMove.Id;
            var txOut = new InventoryTransaction
            {
                ProductId = src.ProductId,
                WarehouseId = warehouseId,
                LocationId = fromLocId,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                FromInventoryStatus = src.Status,
                ToInventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                HandlingUnitId = targetHuId,
                OwnerId = src.OwnerId,
                TransactionType = TransactionType.Move,
                Quantity = -availableToMove,
                ReferenceId = locMove.Id,
                ReferenceType = "HandlingUnitLpnMove",
                Note = $"{reasonText} ({hu?.HuCode ?? "LPN-AUTO"})",
                TransactionDate = now,
                CreatedBy = actorId
            };
            var txIn = new InventoryTransaction
            {
                ProductId = src.ProductId,
                WarehouseId = warehouseId,
                LocationId = destLocation.Id,
                FromLocationId = fromLocId,
                ToLocationId = destLocation.Id,
                InventoryStatus = src.Status,
                FromInventoryStatus = src.Status,
                ToInventoryStatus = src.Status,
                LotId = src.LotId,
                SerialId = src.SerialId,
                HandlingUnitId = targetHuId,
                OwnerId = src.OwnerId,
                TransactionType = TransactionType.Move,
                Quantity = availableToMove,
                ReferenceId = locMove.Id,
                ReferenceType = "HandlingUnitLpnMove",
                Note = $"{reasonText} ({hu?.HuCode ?? "LPN-AUTO"})",
                TransactionDate = now,
                CreatedBy = actorId
            };
            context.InventoryTransactions.AddRange(txOut, txIn);

            totalMovedQty += availableToMove;
            movedItems.Add(new MovedHandlingUnitBucketDto(
                locMove.Id,
                src.ProductId,
                src.Product?.Code ?? $"SKU-{src.ProductId}",
                src.Product?.Name ?? string.Empty,
                fromLocId,
                fromLocCode,
                destLocation.Id,
                destLocation.Code,
                src.LotId,
                src.SerialId,
                targetHuId,
                availableToMove));
        }

        if (movedItems.Count == 0 && alreadyAtDestBuckets.Count > 0)
        {
            var rcvLoc = await context.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == warehouseId && (l.Code == "RCV-01" || l.Code == "RECEIVING"))
                .OrderByDescending(l => l.Code == "RCV-01")
                .FirstOrDefaultAsync(cancellationToken);
            var fallbackFromId = rcvLoc?.Id ?? destLocation.Id;
            var fallbackFromCode = rcvLoc?.Code ?? "RCV-01";

            foreach (var existingBucket in alreadyAtDestBuckets)
            {
                var qty = existingBucket.Quantity;
                totalMovedQty += qty;
                movedItems.Add(new MovedHandlingUnitBucketDto(
                    existingBucket.Id,
                    existingBucket.ProductId,
                    existingBucket.Product?.Code ?? $"SKU-{existingBucket.ProductId}",
                    existingBucket.Product?.Name ?? string.Empty,
                    fallbackFromId,
                    fallbackFromCode,
                    destLocation.Id,
                    destLocation.Code,
                    existingBucket.LotId,
                    existingBucket.SerialId,
                    existingBucket.HandlingUnitId ?? hu?.Id,
                    qty));
            }
        }

        return (movedItems, totalMovedQty);
    }
}
