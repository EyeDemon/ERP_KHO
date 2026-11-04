using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetOwnerGrossProfitAsync(
        int? warehouseId,
        bool canReadCost,
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
        }

        var skuLines = new List<object>();
        var skuValuations = new List<object>();
        var channelBreakdown = new List<object>();
        decimal totalGrossRevenue = 0m;
        decimal totalMovingAverageCogs = 0m;
        decimal totalPlatformFee = 0m;
        decimal totalShippingFeeDiff = 0m;
        decimal totalReturnRmaFee = 0m;
        decimal totalPackagingCost = 0m;

        var stockValuationQuery = _db.InventoryStocks
            .AsNoTracking()
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Warehouse)
            .Where(s => s.Quantity > 0m && (warehouseId.HasValue || s.Warehouse == null || s.Warehouse.IsActive));

        if (warehouseId.HasValue)
        {
            stockValuationQuery = stockValuationQuery.Where(s => s.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            stockValuationQuery = stockValuationQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
        }

        var positiveStocks = await stockValuationQuery
            .OrderBy(s => s.ProductId)
            .ThenBy(s => s.WarehouseId)
            .ToListAsync(cancellationToken);

        var stockGroups = positiveStocks
            .GroupBy(s => new
            {
                s.ProductId,
                ProductCode = s.Product?.Code ?? $"SKU-{s.ProductId}",
                ProductName = s.Product?.Name ?? $"Sản phẩm #{s.ProductId}",
                UnitName = s.Product?.Unit != null && !string.IsNullOrWhiteSpace(s.Product.Unit.Name)
                    ? s.Product.Unit.Name
                    : "Thùng",
                ProductMovingAverageCost = s.Product?.MovingAverageCost ?? 0m
            })
            .OrderBy(g => g.Key.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sg in stockGroups)
        {
            var onHandQuantity = sg.Sum(s => s.Quantity);
            var availableQuantity = sg.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
            var warehouseNames = sg
                .Select(s => s.Warehouse?.Name?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var warehouseName = warehouseNames.Count > 0
                ? string.Join(", ", warehouseNames)
                : "Kho Trung tâm (WH01)";

            var stocksWithMac = sg.Where(s => s.MovingAverageCost > 0m && s.Quantity > 0m).ToList();
            var weightedMac = stocksWithMac.Count > 0
                ? decimal.Round(
                    stocksWithMac.Sum(s => s.Quantity * s.MovingAverageCost) / stocksWithMac.Sum(s => s.Quantity),
                    4)
                : (sg.Key.ProductMovingAverageCost > 0m ? sg.Key.ProductMovingAverageCost : 0m);

            decimal? movingAverageCost = canReadCost ? weightedMac : null;
            decimal? totalValuationAmount = canReadCost ? Math.Round(onHandQuantity * weightedMac, 0) : null;

            skuValuations.Add(new
            {
                productId = sg.Key.ProductId,
                productCode = sg.Key.ProductCode.Trim(),
                productName = sg.Key.ProductName.Trim(),
                warehouseName,
                unitName = sg.Key.UnitName.Trim(),
                onHandQuantity,
                onHandQty = onHandQuantity,
                availableQuantity,
                availableQty = availableQuantity,
                movingAverageCost,
                movingAverageUnitCost = movingAverageCost,
                totalValuationAmount,
                totalInventoryValue = totalValuationAmount
            });
        }

        var receiptsQuery = _db.ExportReceipts
            .AsNoTracking()
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .Where(r => r.Status == ReceiptStatus.Dispatched);

        if (warehouseId.HasValue)
        {
            receiptsQuery = receiptsQuery.Where(r => r.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            receiptsQuery = receiptsQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
        }

        var receipts = await receiptsQuery.OrderByDescending(r => r.Id).Take(200).ToListAsync(cancellationToken);
        var totalOrderCount = receipts.Count;
        var receiptIds = receipts.Select(r => r.Id).ToHashSet();
        var receiptTrackings = receipts
            .Where(r => !string.IsNullOrWhiteSpace(r.TrackingNumber))
            .Select(r => r.TrackingNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allLedgers = await _db.ShipmentFinancialLedgers.AsNoTracking().Take(200).ToListAsync(cancellationToken);
        var ledgers = (warehouseId.HasValue || accessibleIds != null)
            ? allLedgers.Where(l =>
                (l.ExportReceiptId.HasValue && receiptIds.Contains(l.ExportReceiptId.Value)) ||
                (!l.ExportReceiptId.HasValue && receiptTrackings.Contains(l.TrackingNumber)) ||
                (receiptIds.Count == 0 && (accessibleIds == null || accessibleIds.Count > 0))).ToList()
            : allLedgers;

        if (receipts.Count > 0)
        {
            totalPlatformFee = ledgers.Sum(l => l.PlatformCommissionFee);
            totalReturnRmaFee = ledgers.Sum(l => l.ReturnFee);
            totalShippingFeeDiff = ledgers.Sum(l => Math.Max(0m, l.FeeOverchargeVariance) + l.ReturnFee);
        }

        var grouped = receipts
            .SelectMany(r => r.Details)
            .GroupBy(d => new
            {
                d.ProductId,
                Code = d.Product?.Code ?? $"SKU-{d.ProductId}",
                Name = d.Product?.Name ?? $"Sản phẩm #{d.ProductId}",
                Mac = d.Product?.MovingAverageCost ?? 0m
            })
            .ToList();

        foreach (var g in grouped)
        {
            var qty = g.Sum(x => x.Quantity);
            var revenue = g.Sum(x => decimal.Round(x.Quantity * x.UnitPrice, 0, MidpointRounding.AwayFromZero));
            var cogs = decimal.Round(qty * g.Key.Mac, 0, MidpointRounding.AwayFromZero);
            var skuGrossProfit = revenue - cogs;
            var estPlatformFee = decimal.Round(revenue * 0.07m, 0, MidpointRounding.AwayFromZero);
            var estPackCost = decimal.Round(qty * 2500m, 0, MidpointRounding.AwayFromZero);
            var skuPlatformAndShippingFee = estPlatformFee + estPackCost;
            var netPocket = skuGrossProfit - skuPlatformAndShippingFee;
            var skuVat = Math.Round(revenue * 0.01m, 0, MidpointRounding.AwayFromZero);
            var skuPit = Math.Round(revenue * 0.005m, 0, MidpointRounding.AwayFromZero);
            var skuTaxTotal = skuVat + skuPit;
            var skuFixedFee = 3000m;
            var skuWithholdingTaxAndFixedFee = skuTaxTotal + skuFixedFee;
            var skuNetAfterTax = netPocket - skuWithholdingTaxAndFixedFee;

            totalGrossRevenue += revenue;
            totalMovingAverageCogs += cogs;
            totalPackagingCost += estPackCost;

            skuLines.Add(new
            {
                productId = g.Key.ProductId,
                productCode = g.Key.Code,
                productName = g.Key.Name,
                soldQuantity = qty,
                movingAverageUnitCost = canReadCost ? (decimal?)g.Key.Mac : null,
                revenueVnd = revenue,
                grossRevenue = revenue,
                netRevenue = revenue,
                cogsVnd = canReadCost ? (decimal?)cogs : null,
                movingAverageCogs = canReadCost ? (decimal?)cogs : null,
                totalCogs = canReadCost ? (decimal?)cogs : null,
                grossProfitVnd = canReadCost ? (decimal?)skuGrossProfit : null,
                grossProfit = canReadCost ? (decimal?)skuGrossProfit : null,
                estimatedGrossProfit = canReadCost ? (decimal?)skuGrossProfit : null,
                platformFee = estPlatformFee,
                packagingCost = estPackCost,
                platformAndShippingFeeVnd = skuPlatformAndShippingFee,
                withheldVatAmount = skuVat,
                withheldPitAmount = skuPit,
                withheldTaxTotalAmount = skuTaxTotal,
                fixedInfrastructureFeeTotal = skuFixedFee,
                withholdingTaxAndFixedFeeVnd = skuWithholdingTaxAndFixedFee,
                netPocketProfit = canReadCost ? (decimal?)netPocket : null,
                netProfitAfterWithheldTax = canReadCost ? (decimal?)skuNetAfterTax : null,
                netOperatingProfitVnd = canReadCost ? (decimal?)skuNetAfterTax : null,
                grossMarginPct = canReadCost ? (decimal?)(revenue > 0m ? decimal.Round((skuGrossProfit * 100m) / revenue, 2) : 0m) : null
            });
        }

        if (totalGrossRevenue > 0m && totalPlatformFee == 0m && !allLedgers.Any())
        {
            totalPlatformFee = decimal.Round(totalGrossRevenue * 0.07m, 0, MidpointRounding.AwayFromZero);
        }

        var receiptsByChannel = receipts
            .GroupBy(r => !string.IsNullOrWhiteSpace(r.CarrierCode) ? r.CarrierCode.Trim().ToUpperInvariant() : "SHOPEE")
            .ToList();

        foreach (var chGroup in receiptsByChannel)
        {
            var chOrderCount = chGroup.Count();
            var chDetails = chGroup.SelectMany(r => r.Details).ToList();
            var chRevenue = chDetails.Sum(d => decimal.Round(d.Quantity * d.UnitPrice, 0, MidpointRounding.AwayFromZero));
            var chCogs = chDetails.Sum(d => decimal.Round(d.Quantity * (d.Product?.MovingAverageCost ?? 0m), 0, MidpointRounding.AwayFromZero));
            var chGrossProfit = chRevenue - chCogs;
            var chPlatformFee = decimal.Round(chRevenue * 0.07m, 0, MidpointRounding.AwayFromZero);
            var chPackCost = decimal.Round(chDetails.Sum(d => d.Quantity) * 2500m, 0, MidpointRounding.AwayFromZero);
            var chNetGrossProfit = chGrossProfit - chPlatformFee - chPackCost;
            var chVat = Math.Round(chRevenue * 0.01m, 0, MidpointRounding.AwayFromZero);
            var chPit = Math.Round(chRevenue * 0.005m, 0, MidpointRounding.AwayFromZero);
            var chTaxTotal = chVat + chPit;
            var chFixedFee = chOrderCount * 3000m;
            var chNetAfterTax = chNetGrossProfit - chTaxTotal - chFixedFee;

            channelBreakdown.Add(new
            {
                channelCode = chGroup.Key,
                orderCount = chOrderCount,
                revenueVnd = chRevenue,
                grossRevenue = chRevenue,
                cogsVnd = canReadCost ? (decimal?)chCogs : null,
                movingAverageCogs = canReadCost ? (decimal?)chCogs : null,
                grossProfitVnd = canReadCost ? (decimal?)chGrossProfit : null,
                grossProfit = canReadCost ? (decimal?)chGrossProfit : null,
                platformCommissionFee = chPlatformFee,
                packagingCost = chPackCost,
                netGrossProfit = canReadCost ? (decimal?)chGrossProfit : null,
                netPocketProfit = canReadCost ? (decimal?)chNetGrossProfit : null,
                withheldVatAmount = chVat,
                withheldPitAmount = chPit,
                withheldTaxTotalAmount = chTaxTotal,
                fixedInfrastructureFeeTotal = chFixedFee,
                netProfitAfterWithheldTax = canReadCost ? (decimal?)chNetAfterTax : null
            });
        }

        var trueGrossProfitVnd = totalGrossRevenue - totalMovingAverageCogs;
        var grossMarginPctVal = totalGrossRevenue > 0m ? decimal.Round((trueGrossProfitVnd * 100m) / totalGrossRevenue, 2) : 0m;
        var totalPlatformAndShippingFeeVnd = totalPlatformFee + totalShippingFeeDiff + totalPackagingCost;
        var netPocketProfit = trueGrossProfitVnd - totalPlatformAndShippingFeeVnd;
        var netMarginPct = totalGrossRevenue > 0m ? decimal.Round((netPocketProfit * 100m) / totalGrossRevenue, 2) : 0m;
        var withheldVatAmount = Math.Round(totalGrossRevenue * 0.01m, 0, MidpointRounding.AwayFromZero);
        var withheldPitAmount = Math.Round(totalGrossRevenue * 0.005m, 0, MidpointRounding.AwayFromZero);
        var withheldTaxTotalAmount = withheldVatAmount + withheldPitAmount;
        var fixedInfrastructureFeeTotal = totalOrderCount * 3000m;
        var totalWithholdingTaxAndFixedFeeVnd = withheldTaxTotalAmount + fixedInfrastructureFeeTotal;
        var totalNetOperatingProfitVal = netPocketProfit - totalWithholdingTaxAndFixedFeeVnd;
        decimal? trueGrossProfitNullable = canReadCost ? trueGrossProfitVnd : null;
        decimal? netGrossProfitNullable = canReadCost ? netPocketProfit : null;
        decimal? netProfitAfterWithheldTax = canReadCost ? (decimal?)totalNetOperatingProfitVal : null;
        const string ownerTaxPolicyRef = "Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)";

        if (channelBreakdown.Count == 0)
        {
            channelBreakdown.Add(new
            {
                channelCode = "SHOPEE",
                orderCount = totalOrderCount,
                revenueVnd = totalGrossRevenue,
                grossRevenue = totalGrossRevenue,
                cogsVnd = canReadCost ? (decimal?)totalMovingAverageCogs : null,
                movingAverageCogs = canReadCost ? (decimal?)totalMovingAverageCogs : null,
                grossProfitVnd = trueGrossProfitNullable,
                grossProfit = trueGrossProfitNullable,
                platformCommissionFee = totalPlatformFee,
                packagingCost = totalPackagingCost,
                netGrossProfit = trueGrossProfitNullable,
                netPocketProfit = netGrossProfitNullable,
                withheldVatAmount,
                withheldPitAmount,
                withheldTaxTotalAmount,
                fixedInfrastructureFeeTotal,
                netProfitAfterWithheldTax
            });
        }

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            costHiddenByRbac = !canReadCost,
            orderCount = totalOrderCount,
            totalRevenueVnd = totalGrossRevenue,
            grossRevenue = totalGrossRevenue,
            netRevenue = totalGrossRevenue,
            totalCogsVnd = canReadCost ? (decimal?)totalMovingAverageCogs : null,
            movingAverageCogs = canReadCost ? (decimal?)totalMovingAverageCogs : null,
            totalCogs = canReadCost ? (decimal?)totalMovingAverageCogs : null,
            totalGrossProfitVnd = trueGrossProfitNullable,
            grossProfit = trueGrossProfitNullable,
            estimatedGrossProfit = trueGrossProfitNullable,
            grossMarginPct = canReadCost ? (decimal?)grossMarginPctVal : null,
            grossMarginPercent = canReadCost ? (decimal?)grossMarginPctVal : null,
            totalPlatformAndShippingFeeVnd,
            platformCommissionFee = totalPlatformFee,
            shippingFeeDifferentials = totalShippingFeeDiff,
            returnRmaFee = totalReturnRmaFee,
            packagingCost = totalPackagingCost,
            totalWithholdingTaxAndFixedFeeVnd,
            withheldVatAmount,
            withheldPitAmount,
            withheldTaxTotalAmount,
            fixedInfrastructureFeeTotal,
            netPocketProfit = netGrossProfitNullable,
            totalNetOperatingProfitVnd = netProfitAfterWithheldTax,
            netProfitAfterWithheldTax,
            netMarginPct = canReadCost ? (decimal?)netMarginPct : null,
            taxPolicyReference = ownerTaxPolicyRef,
            marketplaceTaxPolicyLabelVi = ownerTaxPolicyRef,
            formula = "GrossProfit = NetRevenue - MovingAverageCogs; NetOperatingProfit = GrossProfit - PlatformAndShippingFee - WithholdingTaxAndFixedFee",
            summary = new
            {
                warehouseId,
                costHiddenByRbac = !canReadCost,
                orderCount = totalOrderCount,
                totalRevenueVnd = totalGrossRevenue,
                grossRevenue = totalGrossRevenue,
                netRevenue = totalGrossRevenue,
                totalCogsVnd = canReadCost ? (decimal?)totalMovingAverageCogs : null,
                movingAverageCogs = canReadCost ? (decimal?)totalMovingAverageCogs : null,
                totalCogs = canReadCost ? (decimal?)totalMovingAverageCogs : null,
                totalGrossProfitVnd = trueGrossProfitNullable,
                grossProfit = trueGrossProfitNullable,
                estimatedGrossProfit = trueGrossProfitNullable,
                grossMarginPct = canReadCost ? (decimal?)grossMarginPctVal : null,
                totalPlatformAndShippingFeeVnd,
                platformCommissionFee = totalPlatformFee,
                shippingFeeDifferentials = totalShippingFeeDiff,
                packagingCost = totalPackagingCost,
                totalWithholdingTaxAndFixedFeeVnd,
                withheldVatAmount,
                withheldPitAmount,
                withheldTaxTotalAmount,
                fixedInfrastructureFeeTotal,
                netGrossProfit = trueGrossProfitNullable,
                netPocketProfit = netGrossProfitNullable,
                totalNetOperatingProfitVnd = netProfitAfterWithheldTax,
                netProfitAfterWithheldTax,
                taxPolicyReference = ownerTaxPolicyRef
            },
            channels = channelBreakdown,
            skuBreakdown = skuLines,
            skuValuations,
            skuItems = skuValuations,
            items = skuValuations,
            statusLabel = canReadCost
                ? "Đã tổng hợp báo cáo lãi gộp thực bỏ túi cho Chủ doanh nghiệp (đã trừ Giá vốn MAC, Phí sàn, Thuế khấu trừ tại nguồn 1,5% & Phí vận chuyển ẩn)"
                : "Báo cáo doanh thu (đã ẩn Giá vốn MAC & Lãi gộp theo phân quyền RBAC)"
        });
    }
}
