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
    private async Task<Stocktake?> ResolveStocktakeEntityAsync(
        string? id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        if (db == null)
            return null;

        IQueryable<Stocktake> query = db.Stocktakes.Include(s => s.Details);
        if (asNoTracking)
            query = query.AsNoTracking();

        if (string.IsNullOrWhiteSpace(id))
            return await query.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);

        var trimmed = id.Trim();
        if (int.TryParse(trimmed, out var numericId))
            return await query.FirstOrDefaultAsync(s => s.Id == numericId, cancellationToken);

        var byCode = await query.FirstOrDefaultAsync(s => s.Code == trimmed, cancellationToken);
        if (byCode != null)
            return byCode;

        if (Guid.TryParse(trimmed, out _))
            return await query.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);

        return null;
    }

    private async Task<List<StocktakeLineAutoApprovalDto>> BuildStocktakeAutoApprovalLinesAsync(
        Stocktake stocktake,
        decimal thresholdPct,
        decimal thresholdValueVnd,
        CancellationToken cancellationToken)
    {
        var details = stocktake.Details.OrderBy(d => d.Id).ToList();
        if (details.Count == 0 || db == null)
            return [];

        var productIds = details.Select(d => d.ProductId).Distinct().ToList();
        var locationIds = details.Where(d => d.LocationId.HasValue).Select(d => d.LocationId!.Value).Distinct().ToList();

        var productsById = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var locationsById = locationIds.Count > 0
            ? await db.WarehouseLocations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken)
            : new Dictionary<int, WarehouseLocation>();

        var result = new List<StocktakeLineAutoApprovalDto>(details.Count);

        foreach (var detail in details)
        {
            productsById.TryGetValue(detail.ProductId, out var product);
            WarehouseLocation? location = null;
            if (detail.LocationId.HasValue)
                locationsById.TryGetValue(detail.LocationId.Value, out location);

            var systemQty = detail.SystemQuantity;
            var countedQty = detail.ActualQuantity ?? detail.SystemQuantity;
            var varianceQty = detail.ActualQuantity.HasValue
                ? detail.ActualQuantity.Value - systemQty
                : detail.DifferenceQuantity;
            var variancePct = systemQty > 0m
                ? Math.Round(Math.Abs(varianceQty) / systemQty * 100m, 2)
                : (varianceQty != 0m ? 100m : 0m);

            var unitCost = product?.MovingAverageCost is > 0m ? product.MovingAverageCost : 0m;
            var varianceValueVnd = Math.Round(Math.Abs(varianceQty) * unitCost, 0);

            var attemptHistory = new List<CountAttemptItemDto>();
            var counterLabel = (detail.LastCountedByUserId ?? stocktake.CreatedBy).ToString();

            if (detail.CountAttempt1Qty.HasValue)
            {
                attemptHistory.Add(new CountAttemptItemDto(
                    attemptNo: 1,
                    countedQty: detail.CountAttempt1Qty.Value,
                    counterUserId: stocktake.CreatedBy.ToString(),
                    countedAtUtc: stocktake.CreatedAt,
                    varianceQty: detail.CountAttempt1Qty.Value - systemQty,
                    note: "Đếm lần 1 (Blind Count)"));
            }

            if (detail.CountAttempt2Qty.HasValue)
            {
                attemptHistory.Add(new CountAttemptItemDto(
                    attemptNo: 2,
                    countedQty: detail.CountAttempt2Qty.Value,
                    counterUserId: counterLabel,
                    countedAtUtc: DateTime.UtcNow,
                    varianceQty: detail.CountAttempt2Qty.Value - systemQty,
                    note: detail.RecountReason ?? detail.Note ?? "Đếm lại lần #2"));
            }

            if (detail.CountAttempt3Qty.HasValue)
            {
                attemptHistory.Add(new CountAttemptItemDto(
                    attemptNo: 3,
                    countedQty: detail.CountAttempt3Qty.Value,
                    counterUserId: counterLabel,
                    countedAtUtc: DateTime.UtcNow,
                    varianceQty: detail.CountAttempt3Qty.Value - systemQty,
                    note: detail.RecountReason ?? detail.Note ?? "Đếm lại lần #3"));
            }

            if (attemptHistory.Count == 0 && detail.ActualQuantity.HasValue)
            {
                attemptHistory.Add(new CountAttemptItemDto(
                    attemptNo: 1,
                    countedQty: countedQty,
                    counterUserId: stocktake.CreatedBy.ToString(),
                    countedAtUtc: stocktake.CreatedAt,
                    varianceQty: varianceQty,
                    note: detail.Note ?? "Đếm lần 1 (Blind Count)"));

                if (detail.CountAttemptCount >= 2 ||
                    !string.IsNullOrWhiteSpace(detail.RecountReason) ||
                    (!string.IsNullOrWhiteSpace(detail.Note) &&
                     detail.Note.Contains("[RECOUNT_REQUESTED]", StringComparison.OrdinalIgnoreCase)))
                {
                    attemptHistory.Add(new CountAttemptItemDto(
                        attemptNo: 2,
                        countedQty: countedQty,
                        counterUserId: counterLabel,
                        countedAtUtc: DateTime.UtcNow,
                        varianceQty: varianceQty,
                        note: detail.RecountReason ?? detail.Note));
                }
            }

            var attemptCount = Math.Max(detail.CountAttemptCount, attemptHistory.Count);
            var autoApproveEligible = Math.Abs(variancePct) <= thresholdPct && varianceValueVnd <= thresholdValueVnd;
            var requiresManagerApproval = !autoApproveEligible && varianceQty != 0m;
            var requiresRecount = varianceQty != 0m && attemptCount < 2;

            string approvalStatus;
            if (varianceQty == 0m)
                approvalStatus = "MATCHED_NO_VARIANCE";
            else if (autoApproveEligible)
                approvalStatus = "AUTO_APPROVED_WITHIN_THRESHOLD";
            else
                approvalStatus = "ESCALATED_PENDING_MANAGER_APPROVAL";

            result.Add(new StocktakeLineAutoApprovalDto(
                stocktakeDetailId: detail.Id,
                productId: detail.ProductId,
                productCode: product?.Code ?? $"SKU-{detail.ProductId}",
                productName: product?.Name ?? $"Sản phẩm #{detail.ProductId}",
                locationId: detail.LocationId,
                locationCode: location?.Code ?? (detail.LocationId.HasValue ? $"LOC-{detail.LocationId.Value}" : "DEFAULT"),
                lotNumber: null,
                systemQty: systemQty,
                countedQty: countedQty,
                varianceQty: varianceQty,
                variancePct: variancePct,
                unitCost: unitCost,
                varianceValueVnd: varianceValueVnd,
                attemptCount: attemptCount,
                maxAllowedAttempts: 3,
                attemptHistory: attemptHistory,
                requiresRecount: requiresRecount,
                autoApproveEligible: autoApproveEligible,
                requiresManagerApproval: requiresManagerApproval,
                approvalStatus: approvalStatus));
        }

        return result;
    }

    private static int? ParseDetailIdFromJsonElement(JsonElement? element)
    {
        if (!element.HasValue)
            return null;

        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var numId))
            return numId;

        if (el.ValueKind == JsonValueKind.String)
        {
            var str = el.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(str) && int.TryParse(str, out var parsedInt))
                return parsedInt;
        }

        return null;
    }
}
