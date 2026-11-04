using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetOrCaptureThreePlStorageSnapshotAsync(
        int? warehouseId,
        int? ownerId,
        bool isPost,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (isPost && accessibleIds.Count == 0)
            {
                return WmsEndpointOutcome.Forbid();
            }
        }

        var now = DateTime.UtcNow;
        var ownerRows = new List<object>();
        decimal totalOnHandQty = 0m;
        int totalOccupiedBins = 0;
        decimal totalVolumeCbm = 0m;
        decimal totalPalletEquivalent = 0m;
        decimal totalDailyFeeVnd = 0m;
        decimal totalMonthlyFeeVnd = 0m;

        var stockQuery = _db.InventoryStocks
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Warehouse)
            .Where(x => x.Quantity > 0m);

        if (warehouseId.HasValue)
        {
            stockQuery = stockQuery.Where(x => x.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            stockQuery = stockQuery.Where(x => accessibleIds.Contains(x.WarehouseId));
        }

        if (ownerId.HasValue)
        {
            stockQuery = stockQuery.Where(x => x.OwnerId == ownerId.Value);
        }

        var stocks = await stockQuery.ToListAsync(cancellationToken);
        var ownerIds = stocks
            .Where(x => x.OwnerId.HasValue && x.OwnerId.Value > 0)
            .Select(x => x.OwnerId!.Value)
            .Distinct()
            .ToList();

        var partnersById = ownerIds.Count > 0
            ? await _db.BusinessPartners
                .AsNoTracking()
                .Where(bp => ownerIds.Contains(bp.Id))
                .ToDictionaryAsync(bp => bp.Id, bp => (bp.Code, bp.Name), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var groupedByOwnerAndWh = stocks
            .GroupBy(x => new { x.OwnerId, x.WarehouseId })
            .OrderBy(g => g.Key.WarehouseId)
            .ThenBy(g => g.Key.OwnerId ?? 0)
            .ToList();

        foreach (var g in groupedByOwnerAndWh)
        {
            string resolvedOwnerCode = "OWN-DEFAULT";
            string resolvedOwnerName = "Chủ hàng Mặc định (Tự doanh & 3PL Chính)";
            if (g.Key.OwnerId.HasValue && partnersById.TryGetValue(g.Key.OwnerId.Value, out var partnerInfo))
            {
                resolvedOwnerCode = partnerInfo.Code;
                resolvedOwnerName = partnerInfo.Name;
            }

            var whName = g.First().Warehouse?.Name?.Trim() ?? $"Kho #{g.Key.WarehouseId}";
            var distinctSkuCount = g.Select(x => x.ProductId).Distinct().Count();
            var occupiedBinCount = g.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().Count();
            var ownerOnHandQty = g.Sum(x => x.Quantity);
            var ownerVolumeCbm = Math.Round(
                g.Sum(x => x.Quantity * (x.Product != null && x.Product.UnitVolumeM3.HasValue && x.Product.UnitVolumeM3.Value > 0m
                    ? x.Product.UnitVolumeM3.Value
                    : 0.002m)),
                3);
            var palletEquivalentCount = Math.Max(1m, Math.Round(ownerVolumeCbm / 1.2m, 2));
            const decimal dailyPalletRateVnd = 18000m;
            const decimal dailyCbmRateVnd = 15000m;
            const decimal dailyBinRateVnd = 5000m;
            var estimatedDailyStorageFeeVnd = Math.Round((palletEquivalentCount * dailyPalletRateVnd) + (occupiedBinCount * dailyBinRateVnd), 0);
            var estimatedMonthlyStorageFeeVnd = estimatedDailyStorageFeeVnd * 30m;

            totalOnHandQty += ownerOnHandQty;
            totalOccupiedBins += occupiedBinCount;
            totalVolumeCbm += ownerVolumeCbm;
            totalPalletEquivalent += palletEquivalentCount;
            totalDailyFeeVnd += estimatedDailyStorageFeeVnd;
            totalMonthlyFeeVnd += estimatedMonthlyStorageFeeVnd;

            ownerRows.Add(new
            {
                ownerId = g.Key.OwnerId,
                ownerCode = resolvedOwnerCode,
                ownerName = resolvedOwnerName,
                warehouseId = g.Key.WarehouseId,
                warehouseName = whName,
                distinctSkuCount,
                occupiedBinCount,
                totalOnHandQty = ownerOnHandQty,
                totalVolumeCbm = ownerVolumeCbm,
                palletEquivalentCount,
                dailyPalletRateVnd,
                dailyCbmRateVnd,
                dailyBinRateVnd,
                estimatedDailyStorageFeeVnd,
                estimatedMonthlyStorageFeeVnd
            });
        }

        if (isPost)
        {
            var snapshotCode = $"3PL-SNAP-{now:yyyyMMdd}";
            var existingSnapshots = await _db.ThreePlStorageSnapshots
                .Where(s => s.SnapshotDateUtc == now.Date &&
                            (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value) &&
                            (warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(s.WarehouseId)) &&
                            (!ownerId.HasValue || s.OwnerId == ownerId.Value))
                .ToListAsync(cancellationToken);

            foreach (var g in groupedByOwnerAndWh)
            {
                string resolvedOwnerCode = "OWN-DEFAULT";
                string resolvedOwnerName = "Chủ hàng Mặc định (Tự doanh & 3PL Chính)";
                if (g.Key.OwnerId.HasValue && partnersById.TryGetValue(g.Key.OwnerId.Value, out var partnerInfo))
                {
                    resolvedOwnerCode = partnerInfo.Code;
                    resolvedOwnerName = partnerInfo.Name;
                }

                var distinctSkuCount = g.Select(x => x.ProductId).Distinct().Count();
                var occupiedBinCount = g.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().Count();
                var ownerOnHandQty = g.Sum(x => x.Quantity);
                var ownerVolumeCbm = Math.Round(
                    g.Sum(x => x.Quantity * (x.Product != null && x.Product.UnitVolumeM3.HasValue && x.Product.UnitVolumeM3.Value > 0m
                        ? x.Product.UnitVolumeM3.Value
                        : 0.002m)),
                    3);
                var palletEquivalentCount = Math.Max(1m, Math.Round(ownerVolumeCbm / 1.2m, 2));
                const decimal dailyPalletRateVnd = 18000m;
                const decimal dailyCbmRateVnd = 15000m;
                const decimal dailyBinRateVnd = 5000m;
                var estimatedDailyStorageFeeVnd = Math.Round((palletEquivalentCount * dailyPalletRateVnd) + (occupiedBinCount * dailyBinRateVnd), 0);
                var estimatedMonthlyStorageFeeVnd = estimatedDailyStorageFeeVnd * 30m;

                var existingSnap = existingSnapshots.FirstOrDefault(s =>
                    s.WarehouseId == g.Key.WarehouseId && s.OwnerId == g.Key.OwnerId);
                if (existingSnap == null)
                {
                    _db.ThreePlStorageSnapshots.Add(new ThreePlStorageSnapshot
                    {
                        WarehouseId = g.Key.WarehouseId,
                        OwnerId = g.Key.OwnerId,
                        OwnerCode = resolvedOwnerCode,
                        OwnerName = resolvedOwnerName,
                        SnapshotCode = snapshotCode,
                        SnapshotDateUtc = now.Date,
                        DistinctSkuCount = distinctSkuCount,
                        OccupiedBinCount = occupiedBinCount,
                        TotalOnHandQty = ownerOnHandQty,
                        TotalVolumeCbm = ownerVolumeCbm,
                        PalletEquivalentCount = palletEquivalentCount,
                        DailyPalletRateVnd = dailyPalletRateVnd,
                        DailyCbmRateVnd = dailyCbmRateVnd,
                        DailyBinRateVnd = dailyBinRateVnd,
                        EstimatedDailyStorageFeeVnd = estimatedDailyStorageFeeVnd,
                        EstimatedMonthlyStorageFeeVnd = estimatedMonthlyStorageFeeVnd,
                        CapturedByUserId = actorUserId,
                        CapturedAtUtc = now
                    });
                }
                else
                {
                    existingSnap.OwnerCode = resolvedOwnerCode;
                    existingSnap.OwnerName = resolvedOwnerName;
                    existingSnap.DistinctSkuCount = distinctSkuCount;
                    existingSnap.OccupiedBinCount = occupiedBinCount;
                    existingSnap.TotalOnHandQty = ownerOnHandQty;
                    existingSnap.TotalVolumeCbm = ownerVolumeCbm;
                    existingSnap.PalletEquivalentCount = palletEquivalentCount;
                    existingSnap.EstimatedDailyStorageFeeVnd = estimatedDailyStorageFeeVnd;
                    existingSnap.EstimatedMonthlyStorageFeeVnd = estimatedMonthlyStorageFeeVnd;
                    existingSnap.CapturedByUserId = actorUserId;
                    existingSnap.CapturedAtUtc = now;
                }
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "CAPTURE_3PL_DAILY_STORAGE_SNAPSHOT",
                EntityName = "ThreePlStorageSnapshot",
                NewValues = $"Owners={ownerRows.Count};Bins={totalOccupiedBins};Pallets={totalPalletEquivalent};DailyFeeVnd={totalDailyFeeVnd}",
                Reason = $"Chốt sản lượng lưu kho 3PL hàng ngày ({now:yyyy-MM-dd}) theo Chủ hàng (OwnerId)",
                Result = "Success",
                Timestamp = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            snapshotId = $"3PL-SNAP-{now:yyyyMMdd}",
            snapshotDateUtc = now.Date,
            capturedAtUtc = now,
            warehouseId,
            ownerCount = ownerRows.Count,
            totalOnHandQty,
            totalOccupiedBins,
            totalVolumeCbm = Math.Round(totalVolumeCbm, 3),
            totalPalletEquivalent = Math.Round(totalPalletEquivalent, 2),
            totalEstimatedDailyStorageFeeVnd = totalDailyFeeVnd,
            totalEstimatedMonthlyStorageFeeVnd = totalMonthlyFeeVnd,
            summary = new
            {
                ownerCount = ownerRows.Count,
                totalOnHandQty,
                totalOccupiedBins,
                totalVolumeCbm = Math.Round(totalVolumeCbm, 3),
                totalPalletEquivalent = Math.Round(totalPalletEquivalent, 2),
                totalEstimatedDailyStorageFeeVnd = totalDailyFeeVnd,
                totalEstimatedMonthlyStorageFeeVnd = totalMonthlyFeeVnd
            },
            owners = ownerRows,
            items = ownerRows,
            statusLabel = isPost
                ? $"Đã chốt & lưu vết kiểm toán sản lượng lưu kho 3PL hàng ngày ({ownerRows.Count} chủ hàng, {totalOccupiedBins} vị trí kệ)"
                : $"Đã tổng hợp bảng tính phí lưu kho 3PL theo Chủ hàng ({ownerRows.Count} chủ hàng, {totalOccupiedBins} vị trí kệ)"
        });
    }

    public async Task<WmsEndpointOutcome> GetOnboardingWizardStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var hasWarehouse = await _db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, cancellationToken);
        var hasLocation = await _db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, cancellationToken);
        var hasProduct = await _db.Products.AsNoTracking().AnyAsync(p => p.IsActive, cancellationToken);
        var hasOpeningStock = await _db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0, cancellationToken);
        var hasFirstAwbOrOrder =
            await _db.ExportReceipts.AsNoTracking().AnyAsync(r => r.AwbPrintCount > 0 || r.Status == ReceiptStatus.Dispatched, cancellationToken) ||
            await _db.SalesOrders.AsNoTracking().AnyAsync(cancellationToken) ||
            await _db.MarketplaceShops.AsNoTracking().AnyAsync(s => s.IsActive, cancellationToken);

        var steps = new object[]
        {
            new { step = 1, code = "CREATE_WAREHOUSE", title = "Khởi tạo Kho hàng", completed = hasWarehouse },
            new { step = 2, code = "CREATE_LOCATIONS", title = "Thiết lập Sơ đồ Kệ / Vị trí", completed = hasLocation },
            new { step = 3, code = "IMPORT_PRODUCTS", title = "Nhập Danh mục Sản phẩm (SKU)", completed = hasProduct },
            new { step = 4, code = "IMPORT_OPENING_STOCK", title = "Chốt Tồn kho Đầu kỳ & Giá vốn MAC", completed = hasOpeningStock },
            new { step = 5, code = "CONNECT_SHOP_AND_FIRST_AWB", title = "Kết nối Sàn TMĐT & In vận đơn đầu tiên", completed = hasFirstAwbOrOrder }
        };
        var completedSteps = new[] { hasWarehouse, hasLocation, hasProduct, hasOpeningStock, hasFirstAwbOrOrder }.Count(x => x);

        return WmsEndpointOutcome.Ok(new
        {
            totalSteps = 5,
            completedSteps,
            completionPct = completedSteps * 20,
            steps,
            statusLabel = $"Tiến độ hướng dẫn thiết lập nhanh 5 bước ({completedSteps}/5 hoàn tất)"
        });
    }
}
