using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public async Task<MisaExcelExportData> GetMisaExcelExportDataAsync(
        int? warehouseId,
        bool canReadCost,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var rows = new List<MisaExcelRowData>();
        decimal totalCostAmountValue = 0m;
        decimal totalRevenueAmountValue = 0m;

        var query = _db.ExportReceipts
            .AsNoTracking()
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
        }

        var receipts = await query.OrderByDescending(r => r.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var r in receipts)
        {
            foreach (var d in r.Details)
            {
                var mac = d.Product?.MovingAverageCost is > 0m ? d.Product.MovingAverageCost : 0m;
                var unitPrice = d.UnitPrice > 0m ? d.UnitPrice : 0m;
                var cogsAmount = decimal.Round(d.Quantity * mac, 2);
                var revenueAmount = decimal.Round(d.Quantity * unitPrice, 2);
                totalCostAmountValue += cogsAmount;
                totalRevenueAmountValue += revenueAmount;

                rows.Add(new MisaExcelRowData(
                    SoChungTu: r.Code,
                    NgayHachToan: r.CreatedAt.ToString("yyyy-MM-dd"),
                    MaHang: d.Product?.Code ?? $"SKU-{d.ProductId}",
                    TenHang: d.Product?.Name ?? $"Sản phẩm #{d.ProductId}",
                    TkNoGiaVon: "632",
                    TkCoKho: "1561",
                    SoLuong: d.Quantity,
                    UnitCost: canReadCost ? (decimal?)mac : null,
                    DonGiaVonMac: canReadCost ? (decimal?)mac : null,
                    CostAmount: canReadCost ? (decimal?)cogsAmount : null,
                    ThanhTienGiaVon: canReadCost ? (decimal?)cogsAmount : null,
                    TkNoCongNo: "131",
                    TkCoDoanhThu: "5111",
                    DonGiaBan: unitPrice,
                    DoanhThu: revenueAmount,
                    EInvoiceStatus: r.EInvoiceStatus ?? "PENDING",
                    EInvoiceProvider: r.EInvoiceProvider ?? "MISA_MEINVOICE"));
            }
        }

        return new MisaExcelExportData(canReadCost, totalCostAmountValue, totalRevenueAmountValue, rows);
    }

    public async Task<WmsEndpointOutcome> IssueEinvoiceNd70DraftAsync(
        IssueEinvoiceNd70DraftInput? request,
        int? actorUserId,
        bool isAuthenticated,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var effectiveUser = currentUserOverride ?? _currentUser;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (_warehouseAccess != null)
        {
            if (request?.WarehouseId.HasValue == true)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            }
            else if (request?.ExportReceiptId.HasValue != true &&
                     (effectiveUser == null || (effectiveUser.IsAuthenticated && !effectiveUser.IsGlobalAdmin)))
            {
                accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var actionType = string.IsNullOrWhiteSpace(request?.ActionType)
            ? "ISSUE"
            : request.ActionType.Trim().ToUpperInvariant();

        ExportReceipt? receipt;
        if (request?.ExportReceiptId.HasValue == true)
        {
            receipt = await _db.ExportReceipts
                .Include(r => r.Customer)
                .Include(r => r.Details)
                    .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken);
            if (receipt != null && _warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
            }
        }
        else
        {
            var fallbackQuery = _db.ExportReceipts
                .Include(r => r.Customer)
                .Include(r => r.Details)
                    .ThenInclude(d => d.Product)
                .AsQueryable();
            if (request?.WarehouseId.HasValue == true)
            {
                fallbackQuery = fallbackQuery.Where(r => r.WarehouseId == request.WarehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                fallbackQuery = fallbackQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
            }
            receipt = await fallbackQuery
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.Equals(actionType, "CANCEL", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "EINVOICE_CANNOT_CANCEL_ISSUED_INVOICE",
                errorCode = "EINVOICE_CANNOT_CANCEL_ISSUED_INVOICE",
                message = "Khoản 13 Điều 1 Nghị định 70/2025/NĐ-CP (sửa đổi Điều 19 Nghị định 123/2020/NĐ-CP) nghiêm cấm hủy đơn phương hóa đơn điện tử đã phát hành. Vui lòng lập Hóa đơn điều chỉnh (ADJUSTMENT) hoặc Hóa đơn thay thế (REPLACEMENT) kèm Số biên bản thỏa thuận hai bên."
            });
        }

        if ((string.Equals(actionType, "ADJUSTMENT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actionType, "REPLACEMENT", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(request?.AgreementDocumentNo))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "EINVOICE_AGREEMENT_DOCUMENT_REQUIRED",
                errorCode = "EINVOICE_AGREEMENT_DOCUMENT_REQUIRED",
                message = "Theo Khoản 13 Điều 1 Nghị định 70/2025/NĐ-CP, khi điều chỉnh hoặc thay thế hóa đơn điện tử đã lập sai phải có văn bản thỏa thuận ghi rõ sai sót (AgreementDocumentNo)."
            });
        }

        if (receipt != null &&
            receipt.Details.Any(d => d.UnitPrice <= 0m) &&
            (request?.EnforceRealUnitPrice == true || !string.IsNullOrWhiteSpace(request?.ActionType)))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "EINVOICE_MISSING_SELLING_PRICE",
                errorCode = "EINVOICE_MISSING_SELLING_PRICE",
                message = "Theo Điều 10 Nghị định 123/2020/NĐ-CP & Nghị định 70/2025/NĐ-CP, không được phép phát hành hóa đơn điện tử khi dòng hàng xuất kho thiếu đơn giá bán thực tế (UnitPrice <= 0)."
            });
        }

        if (!string.IsNullOrWhiteSpace(request?.BuyerTaxCode))
        {
            var candidateTaxCode = request.BuyerTaxCode.Trim();
            if (!string.Equals(candidateTaxCode, "KHACH_LE_KHONG_LAY_HOA_DON", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(candidateTaxCode, @"^\d{10}(-?\d{3})?$"))
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "INVALID_BUYER_TAX_CODE",
                    errorCode = "INVALID_BUYER_TAX_CODE",
                    field = "buyerTaxCode",
                    message = "Mã số thuế người mua không hợp lệ (bắt buộc gồm 10 chữ số hoặc 13 chữ số chi nhánh theo chuẩn Tổng cục Thuế)."
                });
            }
        }

        var buyerTaxCode = !string.IsNullOrWhiteSpace(request?.BuyerTaxCode)
            ? request.BuyerTaxCode.Trim()
            : !string.IsNullOrWhiteSpace(receipt?.Customer?.TaxCode)
                ? receipt.Customer.TaxCode.Trim()
                : "KHACH_LE_KHONG_LAY_HOA_DON";
        var buyerName = !string.IsNullOrWhiteSpace(request?.BuyerName)
            ? request.BuyerName.Trim()
            : !string.IsNullOrWhiteSpace(receipt?.Customer?.Name)
                ? receipt.Customer.Name.Trim()
                : !string.IsNullOrWhiteSpace(receipt?.CustomerNameSnapshot)
                    ? receipt.CustomerNameSnapshot.Trim()
                    : "Người mua không lấy hóa đơn";
        var decreeStandard = string.IsNullOrWhiteSpace(request?.DecreeStandard) ? "ND70_2025_TT32_2025" : request.DecreeStandard.Trim();
        var receiptId = receipt?.Id ?? request?.ExportReceiptId ?? 1;

        if (receipt != null)
        {
            receipt.EInvoiceProvider = "MISA_MEINVOICE";
            receipt.EInvoiceStatus = actionType switch
            {
                "ADJUSTMENT" => "ADJUSTED_ND70",
                "REPLACEMENT" => "REPLACED_ND70",
                _ => "DRAFT_ISSUED"
            };
        }

        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, currentUserOverride, cancellationToken);
        var draftCodeDb = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = receipt?.WarehouseId ?? request?.WarehouseId ?? 1,
            EntityName = "ExportReceipt",
            Action = "ExportReceipt.IssueEinvoiceNd70Draft",
            NewValues = $"DraftCode={draftCodeDb}; ActionType={actionType}; AgreementDocumentNo={request?.AgreementDocumentNo}; BuyerTaxCode={buyerTaxCode}; BuyerName={buyerName}; Standard={decreeStandard}",
            Reason = $"Phát hành Hóa đơn điện tử ({actionType}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC ({draftCodeDb})",
            Result = "Success",
            Timestamp = now
        });

        _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            EventId = $"EINV-ND70-{receiptId}-{now.Ticks}",
            EventType = "EinvoiceNd70DraftIssued",
            AggregateType = "ExportReceipt",
            AggregateId = receiptId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                exportReceiptId = receiptId,
                invoiceDraftCode = draftCodeDb,
                actionType,
                agreementDocumentNo = request?.AgreementDocumentNo,
                adjustmentReason = request?.AdjustmentReason,
                buyerTaxCode,
                buyerName,
                decreeStandard,
                issuedAtUtc = now
            }),
            Status = "PENDING",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            exportReceiptId = receiptId,
            invoiceDraftCode = draftCodeDb,
            draftNumber = draftCodeDb,
            actionType,
            agreementDocumentNo = request?.AgreementDocumentNo,
            buyerTaxCode,
            buyerName,
            decreeStandard,
            provider = "MISA_MEINVOICE",
            status = receipt?.EInvoiceStatus ?? "DRAFT_ISSUED",
            statusLabel = $"Đã phát hành Hóa đơn điện tử ({actionType}: {draftCodeDb}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC",
            issuedAtUtc = now
        });
    }
}
