using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetStockCardS12DnAsync(
        int? warehouseId = null,
        int? productId = null,
        int? lotId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "INVALID_DATE_RANGE",
                errorCode = "INVALID_DATE_RANGE",
                message = "Từ ngày không được lớn hơn đến ngày."
            });
        }

        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        var accessibleIds = await GetAccessibleIdsAsync(effectiveAuth, cancellationToken);
        if (warehouseId.HasValue)
        {
            if (effectiveAuth != null)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            if (!accessibleIds.Contains(warehouseId.Value))
                return WmsEndpointOutcome.Forbid();
        }
        else if (accessibleIds.Count == 0)
        {
            return WmsEndpointOutcome.Forbid();
        }

        var effectiveWarehouseId = warehouseId ?? accessibleIds[0];
        var warehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Id == effectiveWarehouseId)
            .Select(w => new { w.Id, w.Code, w.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var effectiveProductId = productId ?? await _db.InventoryStocks.AsNoTracking()
            .Where(s => s.WarehouseId == effectiveWarehouseId)
            .OrderByDescending(s => s.Quantity)
            .ThenBy(s => s.ProductId)
            .Select(s => (int?)s.ProductId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.Products.AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken)
            ?? 1;

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == effectiveProductId)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                UnitName = p.Unit != null ? p.Unit.Name : "CÁI"
            })
            .FirstOrDefaultAsync(cancellationToken);

        string? resolvedLotNumber = null;
        if (lotId.HasValue)
        {
            resolvedLotNumber = await _db.InventoryLots.AsNoTracking()
                .Where(l => l.Id == lotId.Value)
                .Select(l => l.LotNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var stockQuery = _db.InventoryStocks.AsNoTracking()
            .Where(s => s.WarehouseId == effectiveWarehouseId && s.ProductId == effectiveProductId);
        if (lotId.HasValue)
            stockQuery = stockQuery.Where(s => s.LotId == lotId.Value);

        var currentOnHand = decimal.Round(
            await stockQuery.SumAsync(s => (decimal?)s.Quantity, cancellationToken) ?? 0m,
            4);

        var txQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == effectiveWarehouseId &&
                        t.ProductId == effectiveProductId &&
                        t.TransactionType != TransactionType.Move &&
                        t.TransactionType != TransactionType.StatusChange);
        if (lotId.HasValue)
            txQuery = txQuery.Where(t => t.LotId == lotId.Value);

        var rawTxs = await txQuery
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id,
                t.TransactionDate,
                t.PostedAtUtc,
                t.TransactionType,
                t.Quantity,
                t.ReferenceId,
                t.ReferenceType,
                t.InventoryStatus,
                t.Note,
                LocationCode = t.Location != null ? t.Location.Code : null,
                LotNumber = t.Lot != null ? t.Lot.LotNumber : null
            })
            .ToListAsync(cancellationToken);

        var classifiedTxs = rawTxs.Select(t =>
        {
            decimal inbound = 0m;
            decimal outbound = 0m;
            switch (t.TransactionType)
            {
                case TransactionType.Import:
                case TransactionType.TransferIn:
                case TransactionType.AdjustmentIncrease:
                    if (t.Quantity >= 0m) inbound = t.Quantity;
                    else outbound = Math.Abs(t.Quantity);
                    break;
                case TransactionType.Export:
                case TransactionType.Ship:
                case TransactionType.TransferOut:
                case TransactionType.AdjustmentDecrease:
                    outbound = Math.Abs(t.Quantity);
                    break;
                default:
                    if (t.Quantity >= 0m) inbound = t.Quantity;
                    else outbound = Math.Abs(t.Quantity);
                    break;
            }

            return new
            {
                t.Id,
                t.TransactionDate,
                t.PostedAtUtc,
                t.TransactionType,
                t.ReferenceId,
                t.ReferenceType,
                t.InventoryStatus,
                t.Note,
                t.LocationCode,
                t.LotNumber,
                InboundQty = decimal.Round(inbound, 4),
                OutboundQty = decimal.Round(outbound, 4)
            };
        }).ToList();

        var postPeriodTxs = toDate.HasValue
            ? classifiedTxs.Where(t => t.TransactionDate > toDate.Value).ToList()
            : [];
        var periodTxs = classifiedTxs
            .Where(t => (!fromDate.HasValue || t.TransactionDate >= fromDate.Value) &&
                        (!toDate.HasValue || t.TransactionDate <= toDate.Value))
            .ToList();

        var totalInbound = decimal.Round(periodTxs.Sum(x => x.InboundQty), 4);
        var totalOutbound = decimal.Round(periodTxs.Sum(x => x.OutboundQty), 4);
        var postPeriodNet = decimal.Round(postPeriodTxs.Sum(x => x.InboundQty - x.OutboundQty), 4);
        var closingBalance = decimal.Round(currentOnHand - postPeriodNet, 4);
        var openingBalance = decimal.Round(closingBalance - (totalInbound - totalOutbound), 4);

        var runningBalance = openingBalance;
        var cardRows = new List<StockCardS12DnRowDto>(periodTxs.Count);
        for (var i = 0; i < periodTxs.Count; i++)
        {
            var tx = periodTxs[i];
            runningBalance = decimal.Round(runningBalance + tx.InboundQty - tx.OutboundQty, 4);

            var refType = (tx.ReferenceType ?? tx.TransactionType.ToString()).Trim();
            var refTypeLabel = ReportOperationsTextAndFallbackHelper.TranslateReferenceTypeLabel(refType) ?? refType;
            var prefix = refType.ToUpperInvariant() switch
            {
                "IMPORTRECEIPT" or "GOODSRECEIPT" or "RECEIPT" => "PNK",
                "EXPORTRECEIPT" => "PXK",
                "STOCKTRANSFER" => "PDC",
                "STOCKTAKE" or "STOCKTAKEADJUSTMENT" => "PKK",
                "CUSTOMERRETURN" or "RETURN" => "RMA",
                "SHIPMENT" => "SHP",
                _ => tx.InboundQty > 0m ? "PNK" : "PXK"
            };
            var voucherCode = tx.ReferenceId.HasValue
                ? $"{prefix}-{tx.ReferenceId.Value:D4}"
                : $"{prefix}-{tx.Id:D4}";
            var inboundVoucher = tx.InboundQty > 0m ? voucherCode : string.Empty;
            var outboundVoucher = tx.OutboundQty > 0m ? voucherCode : string.Empty;
            var description = !string.IsNullOrWhiteSpace(tx.Note)
                ? ReportOperationsTextAndFallbackHelper.NormalizeTransactionNote(tx.Note) ?? refTypeLabel
                : $"{refTypeLabel} ({voucherCode})";

            cardRows.Add(new StockCardS12DnRowDto(
                LineNumber: i + 1,
                TransactionId: tx.Id,
                EntryDate: tx.PostedAtUtc != default ? tx.PostedAtUtc : tx.TransactionDate,
                VoucherDate: tx.TransactionDate,
                InboundVoucherCode: inboundVoucher,
                OutboundVoucherCode: outboundVoucher,
                VoucherCode: voucherCode,
                ReferenceType: refType,
                ReferenceTypeLabel: refTypeLabel,
                Description: description,
                LocationCode: tx.LocationCode,
                LotNumber: tx.LotNumber ?? resolvedLotNumber,
                InventoryStatus: ReportOperationsTextAndFallbackHelper.TranslateInventoryStatusLabel(tx.InventoryStatus.ToString()),
                InboundQty: tx.InboundQty,
                OutboundQty: tx.OutboundQty,
                RunningBalance: runningBalance,
                SignatureConfirmation: "Đã ký xác nhận"));
        }

        var reportDto = new StockCardS12DnReportDto(
            FormCode: "Mẫu số S12-DN",
            CircularReference: "Ban hành theo Thông tư số 200/2014/TT-BTC ngày 22/12/2014 của Bộ Tài chính",
            ReportTitle: "THẺ KHO (SỔ KHO) - MẪU S12-DN",
            WarehouseId: effectiveWarehouseId,
            WarehouseCode: (warehouse?.Code ?? $"WH-{effectiveWarehouseId:D2}").Trim(),
            WarehouseName: (warehouse?.Name ?? $"Kho #{effectiveWarehouseId}").Trim(),
            ProductId: effectiveProductId,
            ProductCode: (product?.Code ?? $"SKU-{effectiveProductId:D4}").Trim(),
            ProductName: (product?.Name ?? $"Sản phẩm #{effectiveProductId}").Trim(),
            UnitName: ReportOperationsTextAndFallbackHelper.NormalizeUom(product?.UnitName, "CÁI"),
            LotId: lotId,
            LotNumber: resolvedLotNumber,
            FromDate: fromDate,
            ToDate: toDate,
            OpeningBalance: openingBalance,
            TotalInbound: totalInbound,
            TotalOutbound: totalOutbound,
            ClosingBalance: closingBalance,
            CurrentOnHand: currentOnHand,
            ReconciledWithStock: decimal.Round(openingBalance + totalInbound - totalOutbound, 4) == closingBalance,
            Rows: cardRows);

        return WmsEndpointOutcome.Ok(reportDto);
    }
}
