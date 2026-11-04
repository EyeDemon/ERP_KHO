using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public partial class StockReservationService
{
    public async Task<StockReservationPageDto> GetPageAsync(int page, int pageSize, int? warehouseId, int? productId, string? status, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        if (warehouseId.HasValue)
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        var query = ScopedQuery().AsNoTracking();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        if (Enum.TryParse<StockReservationStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var entities = await query.Include(x => x.Product).Include(x => x.Warehouse).OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = entities.Select(Map).ToList();
        return new() { Items = items, TotalRecords = total, PageIndex = page, PageSize = pageSize };
    }

    public async Task<StockReservationDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.StockReservations.AsNoTracking().Include(x => x.Product).Include(x => x.Warehouse).FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Không tìm thấy reservation.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, cancellationToken);
        return Map(entity);
    }

    public async Task<IReadOnlyList<ReservationReconciliationIssueDto>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var issues = new List<ReservationReconciliationIssueDto>();
        var ledger = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                (x.Status == StockReservationStatus.Active ||
                 x.Status == StockReservationStatus.PartiallyConsumed ||
                 x.Status == StockReservationStatus.PartiallyAllocated ||
                 x.Status == StockReservationStatus.Allocated))
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Reserved = g.Sum(x => x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity) })
            .ToListAsync(cancellationToken);
        var stocks = await context.InventoryStocks.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                (x.Status == InventoryStatus.Available || x.ReservedQuantity != 0))
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.WarehouseId,
                BucketCount = g.Count(),
                Reserved = g.Sum(x => x.ReservedQuantity),
                HasNegativeAvailable = g.Any(x => x.Quantity - x.ReservedQuantity < 0)
            })
            .ToListAsync(cancellationToken);
        foreach (var key in ledger.Select(x => (x.ProductId, x.WarehouseId)).Union(stocks.Select(x => (x.ProductId, x.WarehouseId))))
        {
            var l = ledger.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId)?.Reserved ?? 0;
            var stockGroup = stocks.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId);
            var reserved = stockGroup?.Reserved ?? 0;
            if (stockGroup is null || stockGroup.BucketCount == 0 || l != reserved || stockGroup.HasNegativeAvailable)
            {
                issues.Add(new()
                {
                    Issue = stockGroup is null || stockGroup.BucketCount == 0
                        ? "MissingStock"
                        : l != reserved
                            ? "LedgerMismatch"
                            : "NegativeAvailable",
                    ProductId = key.ProductId,
                    WarehouseId = key.WarehouseId,
                    LedgerReserved = l,
                    StockReserved = stockGroup is null || stockGroup.BucketCount == 0 ? null : reserved
                });
            }
        }
        var invalidReservations = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                ((x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed || x.Status == StockReservationStatus.PartiallyAllocated || x.Status == StockReservationStatus.Allocated) &&
                 (x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity <= 0 ||
                  (x.SourceType == "Manual" && x.ExpiresAt <= DateTime.UtcNow &&
                   !context.PickingTaskLines.Any(line =>
                       line.Allocation.ReservationId == x.Id &&
                       line.PickingTask.Status != PickingTaskStatus.Cancelled)))))
            .Select(x => new ReservationReconciliationIssueDto { Issue = x.SourceType == "Manual" && x.ExpiresAt <= DateTime.UtcNow ? "ExpiredStillActive" : "ActiveWithoutRemaining", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidReservations);
        var invalidExportSources = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) && x.SourceType == "ExportReceipt" &&
                (!x.SourceId.HasValue || !context.ExportReceipts.Any(e => e.Id == x.SourceId && e.WarehouseId == x.WarehouseId) ||
                 !context.ExportReceiptDetails.Any(d => d.ExportReceiptId == x.SourceId && d.ProductId == x.ProductId)))
            .Select(x => new ReservationReconciliationIssueDto { Issue = "InvalidSourceReference", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidExportSources);
        return issues;
    }

    private IQueryable<StockReservation> ScopedQuery()
    {
        if (currentUser.IsGlobalAdmin) return context.StockReservations;
        return context.StockReservations.Where(x => context.UserWarehouses.Any(a => a.UserId == currentUser.UserId && a.WarehouseId == x.WarehouseId));
    }

    private static void Validate(CreateStockReservationDto x)
    {
        if (x.ProductId <= 0 || x.WarehouseId <= 0 || x.Quantity <= 0)
            throw new BusinessRuleException("Sản phẩm, kho và số lượng giữ phải hợp lệ.");
    }

    private static StockReservationDto Map(StockReservation x) => new()
    {
        Id = x.Id,
        ReservationCode = x.ReservationCode.Trim(),
        ProductId = x.ProductId,
        ProductCode = (x.Product?.Code ?? string.Empty).Trim(),
        ProductName = (x.Product?.Name ?? string.Empty).Trim(),
        WarehouseId = x.WarehouseId,
        WarehouseName = (x.Warehouse?.Name ?? string.Empty).Trim(),
        Quantity = x.Quantity,
        ConsumedQuantity = x.ConsumedQuantity,
        ReleasedQuantity = x.ReleasedQuantity,
        AllocatedQuantity = x.AllocatedQuantity,
        Status = x.Status.ToString(),
        SourceType = x.SourceType.Trim(),
        SourceId = x.SourceId,
        SourceCode = x.SourceCode?.Trim(),
        CreatedAt = x.CreatedAt,
        ExpiresAt = x.ExpiresAt
    };

    private static AuditLog Audit(int userId, string action, StockReservation x, string values) => new()
    {
        UserId = userId,
        Action = action,
        EntityName = "StockReservation",
        EntityId = x.Id,
        Timestamp = DateTime.UtcNow,
        NewValues = values
    };
}
