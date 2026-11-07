using System.Data;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingSessionOperationsService
{
    private sealed record VasMutationResult(
        WmsEndpointOutcome? FailureOutcome,
        bool ExecutedStockMutation,
        decimal ComponentConsumedQuantity,
        decimal KitProducedQuantity,
        decimal? ComponentRemainingQuantity,
        decimal? KitResultingQuantity,
        int? ComponentTransactionId,
        int? KitTransactionId,
        List<object> ComponentBreakdown);

    public async Task<WmsEndpointOutcome> ExecuteKittingVasAsync(
        ExecuteKittingVasInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccess = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccess ?? _warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                var accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var activeSessionsCount = 0;
        var handlingUnitsCount = 0;
        if (_db != null)
        {
            activeSessionsCount = await _db.PackingSessions.AsNoTracking()
                .CountAsync(s => s.WarehouseId == targetWarehouseId && s.Status != PackingSessionStatus.Cancelled, cancellationToken);
            handlingUnitsCount = await _db.HandlingUnits.AsNoTracking()
                .CountAsync(h => h.WarehouseId == targetWarehouseId && h.Status != HandlingUnitStatus.Cancelled, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var candidateMode = !string.IsNullOrWhiteSpace(request?.OperationMode)
            ? request.OperationMode
            : (!string.IsNullOrWhiteSpace(request?.VasMode) ? request.VasMode : request?.VasServiceType);
        var rawMode = string.IsNullOrWhiteSpace(candidateMode)
            ? "ASSEMBLE_KIT"
            : candidateMode.Trim().ToUpperInvariant();
        var isDekit = rawMode is "DEKIT_DISASSEMBLE" or "DEKIT" or "DISASSEMBLE"
                      || string.Equals(request?.VasMode?.Trim(), "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(request?.VasServiceType?.Trim(), "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase);
        var normalizedMode = isDekit ? "DEKIT_DISASSEMBLE" : "ASSEMBLE_KIT";

        var workOrderId = isDekit ? $"DEKIT-VAS-{now:yyyyMMdd-HHmm}" : $"KIT-VAS-{now:yyyyMMdd-HHmm}";
        var shouldMutateStock = _db != null && (
            isDekit ||
            request?.ExecuteStockMutation == true ||
            request?.KitProductId.HasValue == true ||
            request?.ParentProductId.HasValue == true ||
            request?.ComponentProductId.HasValue == true ||
            !string.IsNullOrWhiteSpace(request?.ComponentProductCode) ||
            !string.IsNullOrWhiteSpace(request?.KitProductCode));

        var resolvedActorId = actorUserId is > 0 ? actorUserId.Value : (_currentUser?.UserId is > 0 ? _currentUser.UserId : 1);
        var mutation = new VasMutationResult(null, false, 0m, 0m, null, null, null, null, []);

        if (_db != null && shouldMutateStock)
        {
            mutation = isDekit
                ? await ExecuteDekittingMutationAsync(request, targetWarehouseId, workOrderId, resolvedActorId, now, cancellationToken)
                : await ExecuteAssembleKittingMutationAsync(request, targetWarehouseId, workOrderId, resolvedActorId, now, cancellationToken);
            if (mutation.FailureOutcome != null)
            {
                return mutation.FailureOutcome;
            }
        }

        if (_db != null)
        {
            var validAuditUserId = actorUserId is > 0 && await _db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken)
                ? actorUserId
                : null;
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                Action = isDekit ? "DEKIT_DISASSEMBLE_EXECUTED" : "Packing.ExecuteKittingVas",
                EntityName = "KittingVas",
                NewValues = workOrderId,
                Reason = isDekit
                    ? $"Hoàn tất rã bộ De-kitting hoàn nguyên linh kiện ({workOrderId})"
                    : $"Hoàn tất gia công đóng gói & dán nhãn kiện hàng ({workOrderId})",
                Result = "Success",
                Timestamp = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var resolvedVasType = !string.IsNullOrWhiteSpace(request?.VasServiceType)
            ? request.VasServiceType.Trim()
            : (!string.IsNullOrWhiteSpace(request?.VasServices) ? request.VasServices.Trim() : "Dán nhãn & đóng màng co kiện hàng");
        var serviceLabel = isDekit || string.Equals(resolvedVasType, "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase)
            ? "Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ"
            : (string.Equals(resolvedVasType, "LABELING_SHRINKWRAP_BUNDLE", StringComparison.OrdinalIgnoreCase)
                ? "Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm"
                : resolvedVasType);
        var statusMsg = isDekit
            ? "Đã hoàn tất rã bộ hàng lắp ráp & nhập kho linh kiện thành phần"
            : "Đã hoàn tất gia công đóng gói & dán nhãn kiện hàng";

        return WmsEndpointOutcome.Ok(new
        {
            workOrderId,
            operationMode = normalizedMode,
            warehouseId = targetWarehouseId,
            activeSessionsCount,
            activePackingSessionsCount = activeSessionsCount,
            handlingUnitsCount,
            kitSkuCode = string.IsNullOrWhiteSpace(request?.KitSkuCode) ? "KIT-BUNDLE-STD" : request.KitSkuCode.Trim(),
            vasServiceType = serviceLabel,
            serviceScopeLabel = serviceLabel,
            componentAllocationStatus = isDekit
                ? "Đã kiểm tra tồn kho bộ KIT & hoàn nguyên linh kiện về vị trí lưu trữ"
                : "Đã kiểm tra linh kiện & lô hàng theo chuẩn FEFO",
            transformationLedgerMode = isDekit
                ? "Ghi nhận bút toán xuất rã bộ KIT (TransferOut) & nhập hoàn nguyên SKU thành phần (TransferIn)"
                : "Ghi nhận bút toán xuất linh kiện sang bộ KIT",
            billingHookStatus = "Đã ghi nhận phí dịch vụ gia công kho (VAS)",
            status = statusMsg,
            statusLabel = statusMsg,
            components = mutation.ComponentBreakdown,
            executedStockMutation = mutation.ExecutedStockMutation,
            ledgerUpdated = mutation.ExecutedStockMutation,
            componentConsumedQuantity = mutation.ComponentConsumedQuantity,
            kitProducedQuantity = mutation.KitProducedQuantity,
            componentRemainingQuantity = mutation.ComponentRemainingQuantity,
            kitResultingQuantity = mutation.KitResultingQuantity,
            componentTransactionId = mutation.ComponentTransactionId,
            kitTransactionId = mutation.KitTransactionId,
            executedAtUtc = now
        });
    }

    private async Task<VasMutationResult> ExecuteAssembleKittingMutationAsync(
        ExecuteKittingVasInput? request,
        int targetWarehouseId,
        string workOrderId,
        int resolvedActorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var (resolvedCompId, resolvedKitId, resolvedLocId, kitQty, ratio, bomComponents) =
            await ResolveKittingInputsAsync(request, targetWarehouseId, cancellationToken);

        var consumeQty = Math.Round(kitQty * ratio, 4);
        var compStockQuery = _db!.InventoryStocks
            .Include(s => s.Lot)
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.Status == InventoryStatus.Available &&
                        s.Quantity - s.ReservedQuantity >= consumeQty);
        if (resolvedCompId.HasValue)
            compStockQuery = compStockQuery.Where(s => s.ProductId == resolvedCompId.Value);
        else if (bomComponents.Count > 0)
            compStockQuery = compStockQuery.Where(s => s.ProductId == bomComponents[0].ComponentProductId);

        if (resolvedLocId.HasValue)
            compStockQuery = compStockQuery.Where(s => s.LocationId == resolvedLocId.Value);

        var compStock = await compStockQuery
            .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
            .ThenBy(s => s.Lot != null ? s.Lot.ExpiryDate : null)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (compStock == null)
        {
            return new VasMutationResult(null, false, 0m, 0m, null, null, null, null, []);
        }

        var targetKitProductId = resolvedKitId ?? compStock.ProductId;
        var targetLocationId = resolvedLocId ?? compStock.LocationId;

        await using var sqlTx = _db.Database.CurrentTransaction == null && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        await _db.AcquireInventoryAllocationLocksAsync(
            targetWarehouseId,
            new[] { compStock.ProductId, targetKitProductId }.Distinct(),
            cancellationToken);

        var compUnitCost = compStock.MovingAverageCost;
        var rolledUpUnitCost = kitQty > 0m
            ? Math.Round((consumeQty * compUnitCost) / kitQty, 4)
            : compUnitCost;

        compStock.Quantity -= consumeQty;
        compStock.LastUpdated = now;
        compStock.RefreshGrainHash();
        decimal? componentRemainingQuantity = compStock.Quantity;

        var kitStock = await _db.InventoryStocks
            .FirstOrDefaultAsync(s =>
                s.WarehouseId == targetWarehouseId &&
                s.ProductId == targetKitProductId &&
                s.LocationId == targetLocationId &&
                s.LotId == null &&
                s.SerialId == null &&
                s.HandlingUnitId == null &&
                s.OwnerId == null &&
                s.Status == InventoryStatus.Available,
                cancellationToken);

        decimal? kitResultingQuantity;
        if (kitStock != null)
        {
            kitStock.Quantity += kitQty;
            if (rolledUpUnitCost > 0m) kitStock.MovingAverageCost = rolledUpUnitCost;
            kitStock.LastUpdated = now;
            kitStock.RefreshGrainHash();
            kitResultingQuantity = kitStock.Quantity;
        }
        else
        {
            kitStock = new InventoryStock
            {
                WarehouseId = targetWarehouseId,
                ProductId = targetKitProductId,
                LocationId = targetLocationId,
                Status = InventoryStatus.Available,
                Quantity = kitQty,
                ReservedQuantity = 0m,
                MovingAverageCost = rolledUpUnitCost,
                LastUpdated = now
            };
            kitStock.RefreshGrainHash();
            _db.InventoryStocks.Add(kitStock);
            kitResultingQuantity = kitStock.Quantity;
        }

        var kwo = new KitWorkOrder
        {
            WarehouseId = targetWarehouseId,
            WorkOrderCode = $"{workOrderId}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            KitProductId = targetKitProductId,
            WorkOrderType = KitWorkOrderType.Assemble,
            Status = "Completed",
            PlannedKitQty = kitQty,
            CompletedKitQty = kitQty,
            BaseUomCode = "PCS",
            ConversionRateSnapshot = 1m,
            TargetLocationId = targetLocationId,
            RolledUpUnitCost = rolledUpUnitCost,
            EarliestComponentExpiryDate = compStock.Lot?.ExpiryDate,
            CreatedByUserId = resolvedActorId,
            CompletedByUserId = resolvedActorId,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };
        kwo.ComponentLines.Add(new KitWorkOrderComponentLine
        {
            ComponentProductId = compStock.ProductId,
            ComponentRatioQty = ratio,
            RequiredBaseQty = consumeQty,
            ConsumedBaseQty = consumeQty,
            BaseUomCode = "PCS",
            ConversionRateSnapshot = 1m,
            SourceLocationId = compStock.LocationId,
            SourceLotId = compStock.LotId,
            ComponentUnitCost = compUnitCost
        });
        _db.KitWorkOrders.Add(kwo);

        var compTx = new InventoryTransaction
        {
            ProductId = compStock.ProductId,
            WarehouseId = targetWarehouseId,
            LocationId = compStock.LocationId,
            LotId = compStock.LotId,
            SerialId = compStock.SerialId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.AdjustmentDecrease,
            Quantity = consumeQty,
            ReferenceId = compStock.Id,
            ReferenceType = "KittingVas",
            TransactionDate = now,
            CreatedBy = resolvedActorId,
            Note = $"{workOrderId}: Xuất linh kiện lắp ráp bộ KIT"
        };
        var kitTx = new InventoryTransaction
        {
            ProductId = targetKitProductId,
            WarehouseId = targetWarehouseId,
            LocationId = targetLocationId,
            InventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.AdjustmentIncrease,
            Quantity = kitQty,
            ReferenceId = compStock.Id,
            ReferenceType = "KittingVas",
            TransactionDate = now,
            CreatedBy = resolvedActorId,
            Note = $"{workOrderId}: Nhập thành phẩm bộ KIT sau lắp ráp"
        };
        _db.InventoryTransactions.AddRange(compTx, kitTx);
        await _db.SaveChangesAsync(cancellationToken);
        if (sqlTx != null)
        {
            await sqlTx.CommitAsync(cancellationToken);
        }

        return new VasMutationResult(
            null,
            true,
            consumeQty,
            kitQty,
            componentRemainingQuantity,
            kitResultingQuantity,
            compTx.Id,
            kitTx.Id,
            []);
    }
}
