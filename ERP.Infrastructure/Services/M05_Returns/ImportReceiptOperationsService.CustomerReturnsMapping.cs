using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    internal static string NormalizeVietnameseUnitLabel(string? rawUnit)
    {
        if (string.IsNullOrWhiteSpace(rawUnit))
        {
            return "Cái";
        }

        return rawUnit.Trim().ToUpperInvariant() switch
        {
            "PCS" or "PC" or "EA" or "EACH" or "UNIT" or "ĐVT" or "DVT" or "CAI" or "CÁI" => "Cái",
            "BOX" or "HOP" or "HỘP" => "Hộp",
            "CTN" or "CARTON" or "CASE" or "THUNG" or "THÙNG" => "Thùng",
            "SET" or "BO" or "BỘ" => "Bộ",
            "PAIR" or "CAP" or "CẶP" => "Cặp",
            "PACK" or "PK" or "GOI" or "GÓI" => "Gói",
            "BOTTLE" or "BTL" or "CHAI" => "Chai",
            _ => rawUnit.Trim()
        };
    }

    private object MapCustomerReturnDto(
        ERP.Domain.Entities.CustomerReturn r,
        bool canReadCost,
        IReadOnlyDictionary<int, (string Code, string Name, string UnitName)> productMap,
        IReadOnlyDictionary<int, string> locationMap,
        IReadOnlyDictionary<int, string>? warehouseMap = null,
        string? inspectionVideoUrl = null)
    {
        var now = DateTime.UtcNow;
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
        var rowVersionBase64 = r.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(r.RowVersion)
            : string.Empty;

        var orderedLines = r.Lines.OrderBy(l => l.Id).ToList();
        var mappedLines = orderedLines.Select(l =>
        {
            productMap.TryGetValue(l.ProductId, out var prodInfo);
            var resolvedUnit = NormalizeVietnameseUnitLabel(
                !string.IsNullOrWhiteSpace(prodInfo.UnitName) ? prodInfo.UnitName : (l.TransactionUomCode ?? l.BaseUomCode));
            var qLocId = l.QuarantineLocationId ?? r.QuarantineLocationId;
            var qLocCode = qLocId.HasValue && locationMap.TryGetValue(qLocId.Value, out var qCode) ? qCode : "QUARANTINE";
            var rLocId = l.RestockLocationId ?? l.TargetLocationId;
            var rLocCode = rLocId.HasValue && locationMap.TryGetValue(rLocId.Value, out var tCode) ? tCode : null;

            return new
            {
                id = l.Id,
                lineId = l.Id,
                customerReturnId = l.CustomerReturnId,
                productId = l.ProductId,
                productCode = prodInfo.Code ?? $"SKU-{l.ProductId}",
                productName = prodInfo.Name ?? $"Sản phẩm #{l.ProductId}",
                unitName = resolvedUnit,
                unit = resolvedUnit,
                lotId = l.LotId,
                serialId = l.SerialId,
                expiryDate = l.ExpiryDate,
                baseUomId = l.BaseUomId,
                baseUomCode = l.BaseUomCode,
                transactionUomCode = l.TransactionUomCode,
                conversionRateSnapshot = l.ConversionRateSnapshot,
                quarantineLocationId = qLocId,
                quarantineLocationCode = qLocCode,
                targetLocationId = l.TargetLocationId,
                restockLocationId = rLocId,
                restockLocationCode = rLocCode,
                quantity = l.Quantity,
                unitCost = canReadCost ? (decimal?)l.UnitCost : null,
                condition = l.Condition,
                disposition = l.Disposition,
                packagingProductId = l.PackagingProductId,
                packagingQuantity = l.PackagingQuantity,
                packagingConsumedQty = l.PackagingConsumedQty,
                postedLedgerTransactionId = l.PostedLedgerTransactionId
            };
        }).ToList();

        var firstLine = orderedLines.FirstOrDefault();
        (string Code, string Name, string UnitName) firstProdInfo = default;
        if (firstLine != null)
        {
            productMap.TryGetValue(firstLine.ProductId, out firstProdInfo);
        }
        var primaryUnitName = NormalizeVietnameseUnitLabel(
            !string.IsNullOrWhiteSpace(firstProdInfo.UnitName) ? firstProdInfo.UnitName : (firstLine?.TransactionUomCode ?? firstLine?.BaseUomCode));

        var firstQLocId = firstLine?.QuarantineLocationId ?? r.QuarantineLocationId;
        var primaryQuarantineLocationCode = firstQLocId.HasValue && locationMap.TryGetValue(firstQLocId.Value, out var primaryQCode)
            ? primaryQCode
            : "QUARANTINE";
        var firstRLocId = firstLine?.RestockLocationId ?? firstLine?.TargetLocationId;
        var primaryRestockLocationCode = firstRLocId.HasValue && locationMap.TryGetValue(firstRLocId.Value, out var primaryRCode)
            ? primaryRCode
            : null;
        var warehouseName = warehouseMap != null && warehouseMap.TryGetValue(r.WarehouseId, out var whName) && !string.IsNullOrWhiteSpace(whName)
            ? whName
            : $"Kho #{r.WarehouseId}";
        var salesOrderCode = !string.IsNullOrWhiteSpace(r.OrderNumber)
            ? r.OrderNumber
            : (r.SalesOrderId.HasValue ? $"SO-{r.SalesOrderId.Value}" : null);
        var exportReceiptCode = r.ExportReceiptId.HasValue ? $"EXP-{r.ExportReceiptId.Value}" : null;

        return new
        {
            id = r.Id,
            returnCode = r.ReturnCode,
            warehouseId = r.WarehouseId,
            warehouseName,
            salesOrderId = r.SalesOrderId,
            salesOrderCode,
            exportReceiptId = r.ExportReceiptId,
            exportReceiptCode,
            shipmentId = r.ShipmentId,
            quarantineLocationId = firstQLocId,
            quarantineLocationCode = primaryQuarantineLocationCode,
            restockLocationId = firstRLocId,
            restockLocationCode = primaryRestockLocationCode,
            trackingNumber = r.TrackingNumber,
            orderNumber = r.OrderNumber,
            returnReason = NormalizeVietnameseDiacritics(r.ReturnReason),
            status = r.Status,
            productId = firstLine?.ProductId ?? 0,
            productCode = firstProdInfo.Code ?? (firstLine != null ? $"SKU-{firstLine.ProductId}" : string.Empty),
            productName = firstProdInfo.Name ?? (firstLine != null ? $"Sản phẩm #{firstLine.ProductId}" : string.Empty),
            unitName = primaryUnitName,
            unit = primaryUnitName,
            quantity = orderedLines.Count > 0 ? orderedLines.Sum(l => l.Quantity) : 0m,
            unitCost = canReadCost ? (decimal?)(firstLine?.UnitCost ?? 0m) : null,
            condition = firstLine?.Condition ?? "GOOD",
            disposition = firstLine?.Disposition ?? (string.Equals(r.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ? "RESTOCK_AVAILABLE" : "PENDING"),
            createdBy = r.CreatedBy,
            createdAtUtc = r.CreatedAtUtc,
            createdAt = r.CreatedAtUtc,
            approvedBy = r.ApprovedBy,
            approvedAtUtc = r.ApprovedAtUtc,
            postedAtUtc = r.PostedAtUtc,
            completedAtUtc = r.CompletedAtUtc,
            rowVersion = rowVersionBase64,
            costHiddenByRbac = !canReadCost,
            inspectionVideoUrl,
            returnAgingDays,
            isOverdueInTransit,
            overdueAlertLevel,
            disputeDeadlineUtc,
            disputeRemainingHours,
            disputeClaimStatus,
            lines = mappedLines
        };
    }

    public async Task<WmsEndpointOutcome> InspectCustomerReturnAsync(
        int id,
        CustomerReturnInspectInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var customerReturn = await db.CustomerReturns
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (customerReturn == null)
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
            await effectiveAuth.EnsureWarehouseAccessAsync(customerReturn.WarehouseId, cancellationToken);

        if (string.Equals(customerReturn.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RMA_ALREADY_COMPLETED",
                errorCode = "RMA_ALREADY_COMPLETED",
                message = $"Phiếu hoàn trả '{customerReturn.ReturnCode}' đã hoàn tất định đoạt, không thể kiểm định lại."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        if (request?.Lines is { Count: > 0 })
        {
            foreach (var reqLine in request.Lines)
            {
                var targetLine = reqLine.LineId.HasValue
                    ? customerReturn.Lines.FirstOrDefault(l => l.Id == reqLine.LineId.Value)
                    : (reqLine.ProductId.HasValue
                        ? customerReturn.Lines.FirstOrDefault(l => l.ProductId == reqLine.ProductId.Value)
                        : customerReturn.Lines.FirstOrDefault());
                if (targetLine == null) continue;

                if (!string.IsNullOrWhiteSpace(reqLine.Condition))
                    targetLine.Condition = NormalizeConditionCode(reqLine.Condition);
                if (!string.IsNullOrWhiteSpace(reqLine.Disposition))
                    targetLine.Disposition = NormalizeDispositionCode(reqLine.Disposition);
                if (reqLine.RestockLocationId.HasValue)
                    targetLine.RestockLocationId = reqLine.RestockLocationId.Value;
                if (reqLine.TargetLocationId.HasValue)
                    targetLine.TargetLocationId = reqLine.TargetLocationId.Value;
                if (reqLine.PackagingProductId.HasValue)
                    targetLine.PackagingProductId = reqLine.PackagingProductId.Value;
                if (reqLine.PackagingQuantity.HasValue)
                    targetLine.PackagingQuantity = reqLine.PackagingQuantity.Value;
            }
        }
        else
        {
            foreach (var line in customerReturn.Lines)
            {
                if (!string.IsNullOrWhiteSpace(request?.Condition))
                    line.Condition = NormalizeConditionCode(request.Condition);
                if (!string.IsNullOrWhiteSpace(request?.Disposition))
                    line.Disposition = NormalizeDispositionCode(request.Disposition);
            }
        }

        customerReturn.Status = "INSPECTED";
        if (!db.Database.IsSqlServer())
            customerReturn.RowVersion = Guid.NewGuid().ToByteArray()[..8];

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = customerReturn.WarehouseId,
            EntityName = "CustomerReturn",
            EntityId = customerReturn.Id,
            Action = "CustomerReturn.Inspected",
            OldValues = "QUARANTINED",
            NewValues = "Status: INSPECTED",
            Reason = request?.Note ?? "Hoàn tất kiểm định chất lượng hàng hoàn trả",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
    }
}
