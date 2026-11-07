using System.Data;
using System.Globalization;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StocktakeOperationsService
{
    public async Task<WmsEndpointOutcome> CreateAdjustmentProposalAsync(
        CreateAdjustmentProposalInput? request = null,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var resolvedWarehouseId = request?.WarehouseId;
        if (effectiveAuth != null)
        {
            if (resolvedWarehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(resolvedWarehouseId.Value, cancellationToken);
            }
            else
            {
                var accessible = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessible.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                resolvedWarehouseId = accessible.First();
            }
        }

        var reasonCode = string.IsNullOrWhiteSpace(request?.ReasonCode)
            ? "COUNT_VARIANCE"
            : request.ReasonCode.Trim().ToUpperInvariant();
        var productCode = (!string.IsNullOrWhiteSpace(request?.ProductCode) ? request.ProductCode : request?.Sku)?.Trim() ?? string.Empty;
        var resolvedProductId = request?.ProductId;
        var deltaQuantity = (request != null && request.DeltaQuantity != 0m)
            ? request.DeltaQuantity
            : (request?.QuantityDelta ?? 0m);

        if (db != null)
        {
            if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(productCode))
            {
                resolvedProductId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == productCode)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else if (resolvedProductId.HasValue && string.IsNullOrWhiteSpace(productCode))
            {
                productCode = await db.Products.AsNoTracking()
                    .Where(p => p.Id == resolvedProductId.Value)
                    .Select(p => p.Code)
                    .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            }
        }

        var now = DateTime.UtcNow;
        var seedKey = !string.IsNullOrWhiteSpace(productCode)
            ? productCode
            : (resolvedProductId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        var proposalCode = $"ADJ-{now:yyyyMMdd}-{Math.Abs(seedKey.GetHashCode()) % 1000:D3}";
        var userId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

        long proposalAuditId = 0;
        if (db != null)
        {
            if (!resolvedWarehouseId.HasValue)
            {
                resolvedWarehouseId = await db.Warehouses.AsNoTracking()
                    .OrderBy(w => w.Id)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? 1;
            }

            if (!resolvedProductId.HasValue)
            {
                resolvedProductId = await db.InventoryStocks.AsNoTracking()
                    .Where(s => s.WarehouseId == resolvedWarehouseId.Value)
                    .Select(s => (int?)s.ProductId)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? await db.Products.AsNoTracking()
                        .OrderBy(p => p.Id)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync(cancellationToken) ?? 1;
            }

            var log = new AuditLog
            {
                UserId = userId,
                EntityName = "StocktakeAdjustmentProposal",
                EntityId = resolvedProductId,
                WarehouseId = resolvedWarehouseId,
                Action = reasonCode,
                OldValues = deltaQuantity.ToString(CultureInfo.InvariantCulture),
                NewValues = proposalCode,
                Reason = request?.Note?.Trim() ?? reasonCode,
                CorrelationId = string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                Result = "Success",
                Timestamp = now
            };
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync(cancellationToken);
            proposalAuditId = log.Id;

            var stocktakeId = await db.Stocktakes.AsNoTracking()
                .Where(s => !resolvedWarehouseId.HasValue || s.WarehouseId == resolvedWarehouseId.Value)
                .OrderByDescending(s => s.Id)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

            var systemBookQty = (resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
                ? await db.InventoryStocks.AsNoTracking()
                    .Where(s => s.WarehouseId == resolvedWarehouseId.Value && s.ProductId == resolvedProductId.Value)
                    .SumAsync(s => (decimal?)s.Quantity, cancellationToken) ?? 0m
                : 0m;
            var unitCost = resolvedProductId.HasValue
                ? await db.Products.AsNoTracking()
                    .Where(p => p.Id == resolvedProductId.Value)
                    .Select(p => p.MovingAverageCost)
                    .FirstOrDefaultAsync(cancellationToken)
                : 0m;
            var variancePct = systemBookQty > 0m
                ? decimal.Round(Math.Abs(deltaQuantity) / systemBookQty * 100m, 4)
                : (Math.Abs(deltaQuantity) <= 1m ? 0.5m : 100m);
            var varianceValue = decimal.Round(Math.Abs(deltaQuantity) * unitCost, 2);
            var isAutoApproveEligible = (variancePct < 1.0m || Math.Abs(deltaQuantity) <= 1m) && varianceValue <= 500_000m;
            var autoApproved = isAutoApproveEligible;
            var approvalTier = autoApproved
                ? "AUTO_APPROVE_UNDER_1PCT_500K"
                : (variancePct <= 5.0m && varianceValue <= 10_000_000m ? "WAREHOUSE_MANAGER" : "DIRECTOR_CFO");

            StocktakeAdjustmentProposal? dbProposal = null;
            if (resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
            {
                dbProposal = new StocktakeAdjustmentProposal
                {
                    ProposalCode = $"{proposalCode}-{log.Id}",
                    StocktakeId = stocktakeId > 0 ? stocktakeId : null,
                    WarehouseId = resolvedWarehouseId.Value,
                    ProductId = resolvedProductId.Value,
                    LocationId = request?.LocationId,
                    ReasonCode = reasonCode,
                    SystemQuantity = systemBookQty,
                    CountedQuantity = systemBookQty + deltaQuantity,
                    DeltaQuantity = deltaQuantity,
                    VariancePct = variancePct,
                    UnitCost = unitCost,
                    VarianceValue = varianceValue,
                    IsAutoApproved = autoApproved,
                    Status = autoApproved ? "APPROVED" : "PENDING_APPROVAL",
                    CreatedBy = userId ?? 1,
                    ApprovedBy = autoApproved ? userId : null,
                    Note = $"{approvalTier} | {request?.Note?.Trim() ?? reasonCode}",
                    CreatedAtUtc = now,
                    ApprovedAtUtc = autoApproved ? now : null
                };
                db.StocktakeAdjustmentProposals.Add(dbProposal);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (autoApproved && deltaQuantity != 0m && resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
            {
                var execResult = await ExecuteProposalAdjustmentCoreAsync(
                    log.Id,
                    dbProposal?.ProposalCode ?? proposalCode,
                    reasonCode,
                    request?.Note?.Trim() ?? reasonCode,
                    string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                    resolvedWarehouseId.Value,
                    resolvedProductId.Value,
                    request?.LocationId ?? (!string.IsNullOrWhiteSpace(request?.LocationCode)
                        ? await db.WarehouseLocations.AsNoTracking()
                            .Where(l => l.WarehouseId == resolvedWarehouseId.Value && l.Code == request.LocationCode.Trim())
                            .Select(l => (int?)l.Id)
                            .FirstOrDefaultAsync(cancellationToken)
                        : null),
                    request?.LotNumber,
                    deltaQuantity,
                    userId,
                    cancellationToken);

                if (!execResult.Success)
                {
                    if (dbProposal != null)
                    {
                        dbProposal.IsAutoApproved = false;
                        dbProposal.Status = "PENDING_APPROVAL";
                        dbProposal.ApprovedBy = null;
                        dbProposal.ApprovedAtUtc = null;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    return WmsEndpointOutcome.Ok(new
                    {
                        id = dbProposal?.Id ?? log.Id,
                        proposalId = dbProposal?.Id,
                        proposalAuditId = log.Id,
                        proposalCode = dbProposal?.ProposalCode ?? proposalCode,
                        warehouseId = resolvedWarehouseId,
                        productId = resolvedProductId,
                        productCode,
                        locationId = execResult.LocationId,
                        deltaQuantity,
                        variancePct,
                        unitCost = canReadCost ? (decimal?)unitCost : null,
                        varianceValue = canReadCost ? (decimal?)varianceValue : null,
                        isAutoApproved = true,
                        approvalTier,
                        resultingQuantity = execResult.ResultingQuantity,
                        ledgerTransactionId = execResult.LedgerTransactionId,
                        referenceCode = $"ADJ-PROP-{log.Id}",
                        reasonCode,
                        afterState = "Đã phê duyệt",
                        status = "APPROVED",
                        statusLabel = "Đã phê duyệt & Ghi sổ",
                        createdAtUtc = now
                    });
                }
            }

            return WmsEndpointOutcome.Ok(new
            {
                id = dbProposal?.Id ?? proposalAuditId,
                proposalId = dbProposal?.Id,
                proposalAuditId,
                proposalCode = dbProposal?.ProposalCode ?? proposalCode,
                warehouseId = resolvedWarehouseId,
                productId = resolvedProductId,
                productCode,
                deltaQuantity,
                variancePct,
                unitCost = canReadCost ? (decimal?)unitCost : null,
                varianceValue = canReadCost ? (decimal?)varianceValue : null,
                isAutoApproved = false,
                approvalTier,
                reasonCode,
                status = "PENDING_APPROVAL",
                statusLabel = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                createdAtUtc = now
            });
        }

        return WmsEndpointOutcome.Ok(new
        {
            id = proposalAuditId,
            proposalAuditId,
            proposalCode,
            warehouseId = resolvedWarehouseId,
            productId = resolvedProductId,
            productCode,
            deltaQuantity,
            isAutoApproved = false,
            reasonCode,
            status = "PENDING_APPROVAL",
            statusLabel = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
            createdAtUtc = now
        });
    }
}
