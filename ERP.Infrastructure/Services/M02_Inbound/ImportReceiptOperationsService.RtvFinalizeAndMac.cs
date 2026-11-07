using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    private async Task<WmsEndpointOutcome> FinalizeRtvRefurbishDispositionAsync(
        ExecuteRtvRefurbishInput? request,
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        string rawAction,
        string dispositionCode,
        int? resolvedProductId,
        int? resolvedLocationId,
        int? effectiveActorId,
        DateTime now,
        bool ledgerUpdated,
        int? ledgerTransactionId,
        decimal? resultingStockQuantity,
        string? resultingStatus,
        CancellationToken cancellationToken)
    {
        var nonAvailableBucketsCount = 0;
        int? dispositionReturnId = null;
        if (db != null)
        {
            var query = db.InventoryStocks.AsNoTracking()
                .Where(s => s.Status != ERP.Domain.Enums.InventoryStatus.Available);
            if (warehouseId.HasValue)
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
            nonAvailableBucketsCount = await query.CountAsync(cancellationToken);

            var isRtv = rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal);
            var isScrap = rawAction.StartsWith("SCRAP", StringComparison.Ordinal);
            var resolvedReturnStatus = !string.IsNullOrWhiteSpace(request?.Status)
                ? request.Status.Trim().ToUpperInvariant()
                : (isRtv || isScrap ? "QC_FAILED" : "COMPLETED");

            ERP.Domain.Entities.CustomerReturn? dispReturn = null;
            if (request?.CustomerReturnId.HasValue == true)
            {
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == request.CustomerReturnId.Value, cancellationToken);
            }
            if (dispReturn == null && !string.IsNullOrWhiteSpace(request?.ReturnCode))
            {
                var rc = request.ReturnCode.Trim();
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.ReturnCode == rc, cancellationToken);
            }
            if (dispReturn == null && !string.IsNullOrWhiteSpace(request?.TrackingNumber))
            {
                var tn = request.TrackingNumber.Trim();
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.TrackingNumber == tn, cancellationToken);
            }
            if (dispReturn == null)
            {
                var openQuery = db.CustomerReturns
                    .Include(r => r.Lines)
                    .Where(r => r.Status != "COMPLETED" && r.Status != "REFUNDED");
                if (warehouseId.HasValue)
                    openQuery = openQuery.Where(r => r.WarehouseId == warehouseId.Value);
                if (resolvedProductId.HasValue)
                    openQuery = openQuery.Where(r => r.Lines.Any(l => l.ProductId == resolvedProductId.Value) || !r.Lines.Any());
                dispReturn = await openQuery.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            }

            var mutatedQty = request?.Quantity > 0m ? request.Quantity : 1m;
            var lineCondition = isRtv || isScrap ? "DEFECTIVE" : "DAMAGED_BOX";
            var lineDisposition = isRtv ? "RTV_VENDOR" : (isScrap ? "SCRAP_DISPOSAL" : "REFURBISH_REPACK");

            if (dispReturn == null)
            {
                dispReturn = new ERP.Domain.Entities.CustomerReturn
                {
                    ReturnCode = dispositionCode,
                    WarehouseId = warehouseId ?? accessibleIds?.FirstOrDefault() ?? 1,
                    ReturnReason = string.IsNullOrWhiteSpace(request?.ReasonCode) ? rawAction : NormalizeVietnameseDiacritics(request.ReasonCode.Trim()),
                    Status = resolvedReturnStatus,
                    CreatedBy = effectiveActorId ?? 1,
                    CreatedAtUtc = now,
                    CompletedAtUtc = now
                };
                db.CustomerReturns.Add(dispReturn);
                await db.SaveChangesAsync(cancellationToken);

                if (resolvedProductId.HasValue)
                {
                    db.CustomerReturnLines.Add(new ERP.Domain.Entities.CustomerReturnLine
                    {
                        CustomerReturnId = dispReturn.Id,
                        ProductId = resolvedProductId.Value,
                        TargetLocationId = resolvedLocationId,
                        Quantity = mutatedQty,
                        Condition = lineCondition,
                        Disposition = lineDisposition
                    });
                }
            }
            else
            {
                dispReturn.Status = resolvedReturnStatus;
                dispReturn.CompletedAtUtc = now;
                if (!string.IsNullOrWhiteSpace(request?.ReasonCode))
                    dispReturn.ReturnReason = NormalizeVietnameseDiacritics(request.ReasonCode.Trim());
                else if (!string.IsNullOrWhiteSpace(dispReturn.ReturnReason))
                    dispReturn.ReturnReason = NormalizeVietnameseDiacritics(dispReturn.ReturnReason);

                var existingLine = resolvedProductId.HasValue
                    ? dispReturn.Lines.FirstOrDefault(l => l.ProductId == resolvedProductId.Value) ?? dispReturn.Lines.FirstOrDefault()
                    : dispReturn.Lines.FirstOrDefault();

                if (existingLine != null)
                {
                    existingLine.Condition = lineCondition;
                    existingLine.Disposition = lineDisposition;
                    if (resolvedLocationId.HasValue)
                        existingLine.TargetLocationId = resolvedLocationId.Value;
                }
                else if (resolvedProductId.HasValue)
                {
                    db.CustomerReturnLines.Add(new ERP.Domain.Entities.CustomerReturnLine
                    {
                        CustomerReturnId = dispReturn.Id,
                        ProductId = resolvedProductId.Value,
                        TargetLocationId = resolvedLocationId,
                        Quantity = mutatedQty,
                        Condition = lineCondition,
                        Disposition = lineDisposition
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            dispositionReturnId = dispReturn.Id;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                Action = "ImportReceipt.ExecuteRtvRefurbish",
                EntityName = "RtvRefurbish",
                EntityId = dispositionReturnId,
                NewValues = dispositionCode,
                Reason = $"Thực thi xử lý hàng biệt trữ ({rawAction})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var vietnameseDispositionType = (rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal))
            ? "Hoàn trả nhà cung cấp & giảm trừ tồn kho"
            : (rawAction.StartsWith("SCRAP", StringComparison.Ordinal)
                ? "Hủy bỏ hàng lỗi & xuất toán tồn kho"
                : "Tân trang hàng trả về & chuyển về tồn khả dụng");

        return WmsEndpointOutcome.Ok(new
        {
            customerReturnId = dispositionReturnId,
            dispositionCode,
            warehouseId,
            nonAvailableBucketsCount,
            dispositionType = vietnameseDispositionType,
            actionType = vietnameseDispositionType,
            dispositionSummary = $"{vietnameseDispositionType} ({nonAvailableBucketsCount} vị trí biệt trữ và hư hỏng đã rà soát)",
            status = "Đã thực thi & Ghi sổ kiểm toán",
            statusLabel = "Đã thực thi & Ghi sổ kiểm toán",
            ledgerUpdated,
            executedStockMutation = ledgerUpdated,
            mutatedQuantity = ledgerUpdated ? (request?.Quantity > 0m ? request.Quantity : 1m) : 0m,
            ledgerTransactionId,
            resultingStockQuantity,
            resultingStatus,
            refurbishRouting = "Chuyển tồn kho sau tân trang về khu vực hàng khả dụng",
            rtvClaimStatus = "Đã ghi nhận giảm trừ tồn kho và sẵn sàng hoàn trả nhà cung cấp",
            executedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetMacPreviewAsync(
        int id,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var receipt = await db.ImportReceipts
            .AsNoTracking()
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (receipt == null)
        {
            return WmsEndpointOutcome.NotFound(new { code = "NOT_FOUND", errorCode = "NOT_FOUND", message = $"Không tìm thấy phiếu nhập kho #{id}." });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
        }

        var productIds = receipt.Details.Select(d => d.ProductId).Distinct().ToList();
        var existingStocks = await db.InventoryStocks
            .AsNoTracking()
            .Where(s => s.WarehouseId == receipt.WarehouseId && productIds.Contains(s.ProductId))
            .ToListAsync(cancellationToken);

        var lines = receipt.Details.OrderBy(d => d.Id).Select(d =>
        {
            var productStocks = existingStocks.Where(s => s.ProductId == d.ProductId).ToList();
            var oldQty = Math.Max(0m, productStocks.Sum(s => s.Quantity));
            var oldMac = productStocks.FirstOrDefault(s => s.MovingAverageCost > 0m)?.MovingAverageCost
                         ?? d.Product?.MovingAverageCost
                         ?? 0m;
            var incomingBaseQty = d.BaseAcceptedQuantity > 0m
                ? d.BaseAcceptedQuantity
                : (d.BaseReceivedQuantity > 0m
                    ? d.BaseReceivedQuantity
                    : (d.BaseExpectedQuantity > 0m ? d.BaseExpectedQuantity : d.Quantity * (d.ConversionFactor > 0m ? d.ConversionFactor : 1m)));
            if (incomingBaseQty <= 0m)
            {
                incomingBaseQty = d.Quantity;
            }
            var conversionFactor = d.ConversionFactor > 0m ? d.ConversionFactor : 1m;
            var receiptBaseUnitCost = d.UnitPrice > 0m
                ? decimal.Round(d.UnitPrice / conversionFactor, 4)
                : oldMac;
            var projectedTotalQty = oldQty + incomingBaseQty;
            var projectedMac = projectedTotalQty > 0m
                ? decimal.Round(((oldQty * oldMac) + (incomingBaseQty * receiptBaseUnitCost)) / projectedTotalQty, 4)
                : receiptBaseUnitCost;
            var macDelta = decimal.Round(projectedMac - oldMac, 4);
            var macDeltaPct = oldMac > 0m
                ? decimal.Round((macDelta * 100m) / oldMac, 2)
                : 0m;

            return new
            {
                detailId = d.Id,
                productId = d.ProductId,
                productCode = d.Product?.Code ?? $"SKU-{d.ProductId}",
                productName = d.Product?.Name ?? string.Empty,
                oldQuantity = oldQty,
                oldMovingAverageCost = oldMac,
                incomingQuantity = incomingBaseQty,
                receiptUnitCost = receiptBaseUnitCost,
                projectedTotalQuantity = projectedTotalQty,
                projectedMovingAverageCost = projectedMac,
                macDelta,
                macDeltaPct,
                projectedInventoryValuation = decimal.Round(projectedTotalQty * projectedMac, 2)
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            importReceiptId = receipt.Id,
            importReceiptCode = receipt.Code,
            warehouseId = receipt.WarehouseId,
            status = receipt.Status.ToString(),
            formula = "(OldQty * OldMAC + IncomingQty * UnitCost) / (OldQty + IncomingQty)",
            costHiddenByRbac = false,
            totalIncomingLines = lines.Count,
            totalProjectedValuation = lines.Sum(x => x.projectedInventoryValuation),
            lines,
            items = lines,
            statusLabel = $"Đã tính toán bản xem trước giá vốn bình quân gia quyền (MAC) cho {lines.Count} dòng hàng"
        });
    }
}
