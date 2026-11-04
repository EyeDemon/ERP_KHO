using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> GetCustomerReturnsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return WmsEndpointOutcome.Ok(Array.Empty<object>());

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (warehouseId.HasValue && effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var query = db.CustomerReturns.AsNoTracking().Include(r => r.Lines).AsQueryable();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);

        var rawReturns = await query
            .OrderByDescending(r => r.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var trackingNumbers = rawReturns
            .Where(r => !string.IsNullOrWhiteSpace(r.TrackingNumber))
            .Select(r => r.TrackingNumber!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exportReceiptIds = rawReturns
            .Where(r => r.ExportReceiptId.HasValue)
            .Select(r => r.ExportReceiptId!.Value)
            .Distinct()
            .ToList();

        var videoEvidences = (trackingNumbers.Count > 0 || exportReceiptIds.Count > 0)
            ? await db.PackingVideoEvidences
                .AsNoTracking()
                .Where(v => (v.ExportReceiptId.HasValue && exportReceiptIds.Contains(v.ExportReceiptId.Value)) ||
                            trackingNumbers.Contains(v.TrackingNumber))
                .ToListAsync(cancellationToken)
            : [];

        var now = DateTime.UtcNow;
        var items = rawReturns.Select(r =>
        {
            var matchedVideo = videoEvidences.FirstOrDefault(v =>
                (r.ExportReceiptId.HasValue && v.ExportReceiptId == r.ExportReceiptId.Value) ||
                (!string.IsNullOrWhiteSpace(r.TrackingNumber) && string.Equals(v.TrackingNumber, r.TrackingNumber, StringComparison.OrdinalIgnoreCase)));
            var inspectionVideoUrl = matchedVideo?.StorageUrl;

            var returnAgingDays = Math.Max(0, (int)Math.Floor((now - r.CreatedAtUtc).TotalDays));
            var isOverdueInTransit = !string.Equals(r.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(r.Status, "PUTAWAY_COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                                     returnAgingDays > 7;
            var overdueAlertLevel = isOverdueInTransit
                ? "CRITICAL_OVERDUE_7D"
                : (returnAgingDays >= 5 ? "WARNING_AGING" : "NORMAL");
            var disputeDeadlineUtc = r.CreatedAtUtc.AddHours(48);
            var disputeRemainingHours = Math.Round(Math.Max(0, (disputeDeadlineUtc - now).TotalHours), 1);
            var disputeClaimStatus = !string.IsNullOrWhiteSpace(inspectionVideoUrl)
                ? "EVIDENCE_ATTACHED_READY"
                : (disputeRemainingHours <= 0 ? "DISPUTE_WINDOW_EXPIRED" : "PENDING_VIDEO_EVIDENCE");

            return new
            {
                r.Id,
                r.ReturnCode,
                r.WarehouseId,
                r.SalesOrderId,
                r.ExportReceiptId,
                r.TrackingNumber,
                r.OrderNumber,
                ReturnReason = NormalizeVietnameseDiacritics(r.ReturnReason),
                r.Status,
                r.CreatedBy,
                r.CreatedAtUtc,
                createdAt = r.CreatedAtUtc,
                r.CompletedAtUtc,
                inspectionVideoUrl,
                returnAgingDays,
                isOverdueInTransit,
                overdueAlertLevel,
                disputeDeadlineUtc,
                disputeRemainingHours,
                disputeClaimStatus,
                lines = r.Lines.Select(l => new
                {
                    l.Id,
                    l.ProductId,
                    l.QuarantineLocationId,
                    l.TargetLocationId,
                    l.Quantity,
                    l.UnitCost,
                    l.Condition,
                    l.Disposition,
                    l.PackagingProductId,
                    l.PackagingQuantity
                }).ToList()
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(items);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int TenantId, int ReturnId), SemaphoreSlim> _dispositionLocks = new();

    private static string NormalizeDispositionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "RESTOCK_AVAILABLE";
        var upper = raw.Trim().ToUpperInvariant();
        if (upper.Contains("RESTOCK") || upper.Contains("AVAILABLE"))
            return "RESTOCK_AVAILABLE";
        if (upper.Contains("REFURBISH") || upper.Contains("REPACK"))
            return "REFURBISH_REPACK";
        if (upper.Contains("RTV") || upper.Contains("VENDOR"))
            return "RTV_VENDOR";
        if (upper.Contains("SCRAP") || upper.Contains("DISPOSAL"))
            return "SCRAP_DISPOSAL";
        return upper;
    }

    private static string NormalizeConditionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "GOOD";
        var upper = raw.Trim().ToUpperInvariant();
        if (upper is "GOOD" or "DAMAGED_BOX" or "DEFECTIVE")
            return upper;
        if (upper.Contains("DAMAGE") || upper.Contains("BOX"))
            return "DAMAGED_BOX";
        if (upper.Contains("DEFECT") || upper.Contains("BROKEN") || upper.Contains("BAD"))
            return "DEFECTIVE";
        return "GOOD";
    }

    public async Task<WmsEndpointOutcome> QueryCustomerReturnsAsync(
        CustomerReturnQueryFilter filter,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                items = Array.Empty<object>(),
                totalCount = 0,
                pageNumber = filter.PageNumber,
                pageSize = filter.PageSize,
                summary = new { quarantinedCount = 0, inspectedCount = 0, restockedCount = 0, refurbishRtvScrapCount = 0 },
                costHiddenByRbac = !filter.CanReadCost
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (filter.WarehouseId.HasValue && effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(filter.WarehouseId.Value, cancellationToken);

        var query = db.CustomerReturns.AsNoTracking().Include(r => r.Lines).AsQueryable();
        if (filter.WarehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == filter.WarehouseId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim().ToUpperInvariant();
            query = query.Where(r => r.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(filter.Condition))
        {
            var cond = filter.Condition.Trim().ToUpperInvariant();
            query = query.Where(r => r.Lines.Any(l => l.Condition == cond));
        }
        if (!string.IsNullOrWhiteSpace(filter.Disposition))
        {
            var disp = filter.Disposition.Trim().ToUpperInvariant();
            query = query.Where(r => r.Lines.Any(l => l.Disposition == disp));
        }
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim();
            query = query.Where(r =>
                r.ReturnCode.Contains(kw) ||
                (r.TrackingNumber != null && r.TrackingNumber.Contains(kw)) ||
                (r.OrderNumber != null && r.OrderNumber.Contains(kw)) ||
                r.ReturnReason.Contains(kw));
        }

        var pageNumber = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var totalCount = await query.CountAsync(cancellationToken);

        var rawReturns = await query
            .OrderByDescending(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var allReturnsForSummary = await db.CustomerReturns.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => !filter.WarehouseId.HasValue || r.WarehouseId == filter.WarehouseId.Value)
            .ToListAsync(cancellationToken);

        var quarantinedCount = allReturnsForSummary.Count(r => string.Equals(r.Status, "QUARANTINED", StringComparison.OrdinalIgnoreCase));
        var inspectedCount = allReturnsForSummary.Count(r => string.Equals(r.Status, "INSPECTED", StringComparison.OrdinalIgnoreCase));
        var restockedCount = allReturnsForSummary.SelectMany(r => r.Lines).Count(l => string.Equals(l.Disposition, "RESTOCK_AVAILABLE", StringComparison.OrdinalIgnoreCase));
        var refurbishRtvScrapCount = allReturnsForSummary.SelectMany(r => r.Lines).Count(l =>
            string.Equals(l.Disposition, "REFURBISH_REPACK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Disposition, "RTV_VENDOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Disposition, "SCRAP_DISPOSAL", StringComparison.OrdinalIgnoreCase));

        var productIds = rawReturns.SelectMany(r => r.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var productMap = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.Code.Trim(), p.Name.Trim()), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var locIds = rawReturns
            .SelectMany(r => r.Lines.SelectMany(l => new[] { l.QuarantineLocationId, l.TargetLocationId, l.RestockLocationId }).Append(r.QuarantineLocationId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var locationMap = locIds.Count > 0
            ? await db.WarehouseLocations.AsNoTracking()
                .Where(l => locIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Code.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var whIds = rawReturns.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouseMap = whIds.Count > 0
            ? await db.Warehouses.AsNoTracking()
                .Where(w => whIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var items = rawReturns
            .Select(r => MapCustomerReturnDto(r, filter.CanReadCost, productMap, locationMap, warehouseMap))
            .ToList();

        return WmsEndpointOutcome.Ok(new
        {
            items,
            totalCount,
            pageNumber,
            pageSize,
            summary = new
            {
                quarantinedCount,
                inspectedCount,
                restockedCount,
                refurbishRtvScrapCount
            },
            costHiddenByRbac = !filter.CanReadCost
        });
    }

    public async Task<WmsEndpointOutcome> GetCustomerReturnByIdAsync(
        int id,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var r = await db.CustomerReturns.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RMA_NOT_FOUND",
                errorCode = "RMA_NOT_FOUND",
                message = $"Không tìm thấy phiếu hoàn trả #{id}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(r.WarehouseId, cancellationToken);

        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var productMap = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.Code.Trim(), p.Name.Trim()), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var locIds = r.Lines
            .SelectMany(l => new[] { l.QuarantineLocationId, l.TargetLocationId, l.RestockLocationId })
            .Append(r.QuarantineLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var locationMap = locIds.Count > 0
            ? await db.WarehouseLocations.AsNoTracking()
                .Where(l => locIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Code.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var warehouseMap = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == r.WarehouseId)
            .ToDictionaryAsync(w => w.Id, w => w.Name.Trim(), cancellationToken);

        return WmsEndpointOutcome.Ok(MapCustomerReturnDto(r, canReadCost, productMap, locationMap, warehouseMap));
    }
}
