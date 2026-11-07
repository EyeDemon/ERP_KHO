using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> SyncAccountingAndEInvoiceOutboxAsync(
        SyncAccountingAndEInvoiceInput? request = null,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        var effectiveUser = currentUserOverride ?? _currentUser;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (request?.WarehouseId.HasValue == true)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            }
            else if (request?.ExportReceiptId.HasValue != true &&
                     (effectiveUser == null || (effectiveUser.IsAuthenticated && !effectiveUser.IsGlobalAdmin)))
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        // Decree 70/2025/NĐ-CP export goods customs clearance timing guard
        if (request?.IsExportDeclaration == true && !request.CustomsCleared)
        {
            throw new ERP.Application.Exceptions.BusinessRuleException(
                "EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE: Hàng xuất khẩu chưa hoàn tất thông quan hải quan theo Nghị định 70/2025/NĐ-CP; chưa được phép phát hành Hóa đơn điện tử.");
        }

        if (string.Equals(request?.ActionType, "CANCEL", StringComparison.OrdinalIgnoreCase))
        {
            throw new ERP.Application.Exceptions.BusinessRuleException(
                "EINVOICE_CANNOT_CANCEL_ISSUED_INVOICE: Khoản 13 Điều 1 Nghị định 70/2025/NĐ-CP nghiêm cấm hủy đơn phương hóa đơn điện tử đã phát hành. Vui lòng lập Hóa đơn điều chỉnh (ADJUSTMENT) hoặc Hóa đơn thay thế (REPLACEMENT) kèm Số biên bản thỏa thuận hai bên.");
        }

        if ((string.Equals(request?.ActionType, "ADJUSTMENT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(request?.ActionType, "REPLACEMENT", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(request?.AgreementDocumentNo))
        {
            throw new ERP.Application.Exceptions.BusinessRuleException(
                "EINVOICE_AGREEMENT_DOCUMENT_REQUIRED: Theo Khoản 13 Điều 1 Nghị định 70/2025/NĐ-CP, khi điều chỉnh hoặc thay thế hóa đơn điện tử đã lập sai phải có văn bản thỏa thuận ghi rõ sai sót (AgreementDocumentNo).");
        }

        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, true, currentUserOverride, cancellationToken);
        var accountingProvider = string.IsNullOrWhiteSpace(request?.AccountingProvider)
            ? "MISA_AMIS"
            : request!.AccountingProvider!.Trim().ToUpperInvariant();
        var einvoiceProvider = string.IsNullOrWhiteSpace(request?.EInvoiceProvider)
            ? "VIETTEL_SINVOICE"
            : request!.EInvoiceProvider!.Trim().ToUpperInvariant();

        var exportQuery = _db.ExportReceipts
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .Include(r => r.Customer)
            .AsQueryable();

        if (request?.ExportReceiptId.HasValue == true)
        {
            exportQuery = exportQuery.Where(r => r.Id == request.ExportReceiptId.Value);
        }
        else
        {
            if (request?.WarehouseId.HasValue == true)
            {
                exportQuery = exportQuery.Where(r => r.WarehouseId == request.WarehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                exportQuery = exportQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
            }
            exportQuery = exportQuery.Where(r => r.Details.Any());
        }

        var exportReceipt = await exportQuery.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (request?.ExportReceiptId.HasValue == true && exportReceipt != null && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(exportReceipt.WarehouseId, cancellationToken);
        }

        var buyerTaxCode = !string.IsNullOrWhiteSpace(request?.BuyerTaxCode)
            ? request!.BuyerTaxCode!.Trim()
            : !string.IsNullOrWhiteSpace(exportReceipt?.Customer?.TaxCode)
                ? exportReceipt.Customer.TaxCode.Trim()
                : "KHACH_LE_KHONG_LAY_HOA_DON";
        var buyerName = !string.IsNullOrWhiteSpace(request?.BuyerName)
            ? request!.BuyerName!.Trim()
            : !string.IsNullOrWhiteSpace(exportReceipt?.Customer?.Name)
                ? exportReceipt.Customer.Name.Trim()
                : !string.IsNullOrWhiteSpace(exportReceipt?.CustomerNameSnapshot)
                    ? exportReceipt.CustomerNameSnapshot.Trim()
                    : "Người mua không lấy hóa đơn";
        var targetReceiptId = exportReceipt?.Id ?? request?.ExportReceiptId ?? 1;
        var targetWarehouseId = exportReceipt?.WarehouseId ?? request?.WarehouseId ?? 1;
        var receiptCode = exportReceipt?.Code?.Trim() ?? $"PXK-{targetReceiptId:D4}";

        if (request?.Reverse == true)
        {
            return await ReverseAccountingOutboxEntryAsync(
                request,
                exportReceipt,
                targetReceiptId,
                targetWarehouseId,
                receiptCode,
                accountingProvider,
                einvoiceProvider,
                validAuditUserId,
                now,
                cancellationToken);
        }

        if (exportReceipt == null)
        {
            throw new KeyNotFoundException(
                $"EXPORT_RECEIPT_NOT_FOUND: Không tìm thấy phiếu xuất kho #{request?.ExportReceiptId ?? 0} để đồng bộ kế toán & phát hành hóa đơn điện tử.");
        }

        if (exportReceipt.Details == null || exportReceipt.Details.Count == 0)
        {
            throw new ERP.Domain.Exceptions.ConflictException(
                "EXPORT_RECEIPT_HAS_NO_LINES_FOR_ACCOUNTING_SYNC",
                $"Phiếu xuất {receiptCode} (#{targetReceiptId}) không có dòng hàng chi tiết để hạch toán kế toán & phát hành HĐĐT.");
        }

        if (request?.PersistOutboxAndAudit != false && request?.IssueAdjustmentForm04SS != true)
        {
            var isRelational = _db.Database.IsRelational();
            var lockProductIds = exportReceipt.Details.Select(d => d.ProductId).Distinct().OrderBy(x => x).ToList();
            if (isRelational && lockProductIds.Count > 0)
            {
                await _db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);
            }

            var existingActiveOutbox = await _db.IntegrationOutboxMessages.AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.AggregateType == "ExportReceipt" &&
                         m.AggregateId == targetReceiptId &&
                         m.EventType == "AccountingAndEinvoiceNd70Synced" &&
                         m.Status != "REVERSED",
                    cancellationToken);

            if (existingActiveOutbox != null)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ACCOUNTING_EINVOICE_ALREADY_SYNCED",
                    $"Phiếu xuất {receiptCode} (#{targetReceiptId}) đã được đồng bộ sang {accountingProvider} / {einvoiceProvider} ({existingActiveOutbox.EventId}). Sử dụng Mẫu 04/SS-HĐĐT nếu cần điều chỉnh.");
            }
        }

        decimal totalCogsAmount = 0m;
        decimal totalRevenueAmount = 0m;
        decimal totalVatAmount = 0m;
        var lineDetails = new List<object>();

        foreach (var d in exportReceipt.Details)
        {
            if (d.UnitPrice <= 0m && (request?.EnforceRealUnitPrice == true || !string.IsNullOrWhiteSpace(request?.ActionType)))
            {
                throw new ERP.Application.Exceptions.BusinessRuleException(
                    "EINVOICE_MISSING_SELLING_PRICE: Dòng hàng xuất kho thiếu đơn giá bán thực tế (UnitPrice <= 0); nghiêm cấm tự bịa đặt giá bán khi phát hành HĐĐT theo Nghị định 70/2025/NĐ-CP.");
            }

            var mac = d.Product?.MovingAverageCost is > 0m ? d.Product.MovingAverageCost : 0m;
            var unitPrice = d.UnitPrice > 0m ? d.UnitPrice : 0m;
            var lineVatRate = d.TaxPercent > 0m
                ? d.TaxPercent
                : (d.Product?.DefaultVatRatePercent is >= 0m ? d.Product.DefaultVatRatePercent : 10.0000m);
            var qty = Math.Round(d.Quantity, 4, MidpointRounding.AwayFromZero);
            var lineCogs = Math.Round(qty * mac, 4, MidpointRounding.AwayFromZero);
            var lineRev = Math.Round(qty * unitPrice, 4, MidpointRounding.AwayFromZero);
            var lineVat = Math.Round(lineRev * (lineVatRate / 100m), 4, MidpointRounding.AwayFromZero);

            totalCogsAmount = Math.Round(totalCogsAmount + lineCogs, 4, MidpointRounding.AwayFromZero);
            totalRevenueAmount = Math.Round(totalRevenueAmount + lineRev, 4, MidpointRounding.AwayFromZero);
            totalVatAmount = Math.Round(totalVatAmount + lineVat, 4, MidpointRounding.AwayFromZero);

            lineDetails.Add(new
            {
                productId = d.ProductId,
                productCode = d.Product?.Code?.Trim() ?? $"SKU-{d.ProductId}",
                productName = d.Product?.Name?.Trim() ?? $"Sản phẩm #{d.ProductId}",
                quantity = qty,
                unitCostMac = canReadCost ? (decimal?)mac : null,
                cogsAmount = canReadCost ? (decimal?)lineCogs : null,
                unitSellingPrice = unitPrice,
                vatRatePercent = lineVatRate,
                vatAmount = lineVat,
                revenueAmount = lineRev,
                debitCogsAccount = "632",
                creditInventoryAccount = "1561",
                debitReceivableAccount = "131",
                creditRevenueAccount = "5111"
            });
        }

        var vatRatePercent = totalRevenueAmount > 0m
            ? Math.Round((totalVatAmount / totalRevenueAmount) * 100m, 4, MidpointRounding.AwayFromZero)
            : 10.0000m;
        var totalReceivableAmount = Math.Round(totalRevenueAmount + totalVatAmount, 4, MidpointRounding.AwayFromZero);

        decimal inboundPurchaseAmount = 0m;
        string? importReceiptCode = null;
        if (request?.ImportReceiptId.HasValue == true)
        {
            var imp = await _db.ImportReceipts.AsNoTracking()
                .Include(i => i.Details)
                .FirstOrDefaultAsync(i => i.Id == request.ImportReceiptId.Value, cancellationToken);
            if (imp != null)
            {
                if (effectiveWhAccess != null)
                {
                    await effectiveWhAccess.EnsureWarehouseAccessAsync(imp.WarehouseId, cancellationToken);
                }
                importReceiptCode = imp.Code.Trim();
                inboundPurchaseAmount = Math.Round(imp.Details.Sum(x => x.Quantity * x.UnitPrice), 4, MidpointRounding.AwayFromZero);
            }
        }

        var circular200JournalEntries = BuildCircular200JournalEntries(
            receiptCode,
            canReadCost,
            totalCogsAmount,
            totalRevenueAmount,
            totalVatAmount,
            inboundPurchaseAmount,
            importReceiptCode);

        var totalDebitAmount = Math.Round(totalCogsAmount + totalReceivableAmount + inboundPurchaseAmount, 4, MidpointRounding.AwayFromZero);
        var totalCreditAmount = Math.Round(totalCogsAmount + totalRevenueAmount + totalVatAmount + inboundPurchaseAmount, 4, MidpointRounding.AwayFromZero);
        var journalImbalanceDelta = Math.Round(totalDebitAmount - totalCreditAmount, 4, MidpointRounding.AwayFromZero);

        var invoiceSeries = "C26TAA";
        var invoiceTemplateCode = "1/001";
        var invoiceNumber = $"{now:yyyyMMdd}{targetReceiptId:D4}";
        var isForm04Ss = request?.IssueAdjustmentForm04SS == true;
        var form04SsCode = isForm04Ss ? $"04SS-ND70-{now:yyyyMMdd}-{targetReceiptId:D4}" : null;
        var outboxEventId = !string.IsNullOrWhiteSpace(request?.OriginalOutboxEventId)
            ? request!.OriginalOutboxEventId!.Trim()
            : isForm04Ss
                ? $"OUTBOX-04SS-{targetReceiptId}-{now.Ticks}"
                : $"OUTBOX-ACC-EINV-{targetReceiptId}-{now.Ticks}";

        var einvoiceStatus = isForm04Ss ? "ADJUSTED_04SS_ISSUED" : "ND70_ISSUED";

        if (request?.PersistOutboxAndAudit != false)
        {
            exportReceipt.EInvoiceProvider = einvoiceProvider;
            exportReceipt.EInvoiceStatus = einvoiceStatus;

            _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                EventId = outboxEventId,
                EventType = isForm04Ss ? "EinvoiceForm04SsAdjustmentIssued" : "AccountingAndEinvoiceNd70Synced",
                AggregateType = "ExportReceipt",
                AggregateId = targetReceiptId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    outboxEventId,
                    exportReceiptId = targetReceiptId,
                    exportReceiptCode = receiptCode,
                    accountingProvider,
                    einvoiceProvider,
                    decreeStandard = "ND70_2025_TT200_2014",
                    invoiceTemplateCode,
                    invoiceSeries,
                    invoiceNumber,
                    isForm04Ss,
                    form04SsCode,
                    adjustmentReason = request?.AdjustmentReason,
                    buyerTaxCode,
                    buyerName,
                    totalCogsAmount,
                    totalRevenueAmount,
                    totalVatAmount,
                    totalReceivableAmount,
                    totalDebitAmount,
                    totalCreditAmount,
                    journalImbalanceDelta,
                    syncedAtUtc = now
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                EntityName = "ExportReceipt",
                EntityId = targetReceiptId,
                Action = isForm04Ss ? "ExportReceipt.IssueEinvoiceForm04Ss" : "ExportReceipt.SyncAccountingAndEinvoiceNd70",
                NewValues = $"{outboxEventId}:{accountingProvider}+{einvoiceProvider};Debit={totalDebitAmount:0.####};Credit={totalCreditAmount:0.####}",
                Reason = isForm04Ss
                    ? $"Phát hành Thông báo điều chỉnh/thay thế HĐĐT Mẫu 04/SS-HĐĐT ({form04SsCode}) theo Nghị định 70/2025/NĐ-CP cho phiếu {receiptCode}"
                    : $"Đồng bộ bút toán Thông tư 200 ({accountingProvider}) & phát hành HĐĐT Nghị định 70/2025/NĐ-CP ({einvoiceProvider} • {invoiceSeries} • {invoiceNumber}) cho phiếu {receiptCode}",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            outboxEventId,
            exportReceiptId = targetReceiptId,
            exportReceiptCode = receiptCode,
            importReceiptId = request?.ImportReceiptId,
            importReceiptCode,
            warehouseId = targetWarehouseId,
            accountingProvider,
            einvoiceProvider,
            decreeStandard = "ND70_2025_TT200_2014",
            circularStandard = "TT200_2014_TT_BTC",
            invoiceTemplateCode,
            invoiceSeries,
            invoiceNumber,
            buyerTaxCode,
            buyerName,
            isExportDeclaration = request?.IsExportDeclaration ?? false,
            customsCleared = request?.CustomsCleared ?? true,
            isForm04Ss,
            form04SsCode,
            adjustmentReason = request?.AdjustmentReason,
            canReadCost,
            totalCogsAmount = canReadCost ? (decimal?)totalCogsAmount : null,
            totalRevenueAmount,
            vatRatePercent,
            totalVatAmount,
            totalReceivableAmount,
            inboundPurchaseAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null,
            totalDebitAmount = canReadCost ? (decimal?)totalDebitAmount : null,
            totalCreditAmount = canReadCost ? (decimal?)totalCreditAmount : null,
            journalImbalanceDelta,
            doubleEntryBalanced = journalImbalanceDelta == 0.0000m,
            circular200JournalEntries,
            lineDetails,
            status = einvoiceStatus,
            statusLabel = isForm04Ss
                ? $"Đã phát hành Mẫu 04/SS-HĐĐT ({form04SsCode}) điều chỉnh HĐĐT NĐ 70/2025 & đồng bộ {accountingProvider} (Cân đối Nợ/Có Δ = 0.0000)"
                : $"Đã đồng bộ bút toán Thông tư 200 ({accountingProvider}: Nợ 632/Có 1561, Nợ 131/Có 5111, 33311) & phát hành HĐĐT NĐ 70/2025 ({einvoiceProvider} • {invoiceTemplateCode} • {invoiceSeries} #{invoiceNumber})",
            syncedAtUtc = now
        });
    }
}
