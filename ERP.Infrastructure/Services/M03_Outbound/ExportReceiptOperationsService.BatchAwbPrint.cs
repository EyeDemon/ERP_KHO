using System.Net;
using System.Text;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ExportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> BatchPrintAwbAsync(
        BatchPrintAwbInput? request,
        bool canReprintAwb,
        int? actorUserId,
        bool isAuthenticated,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        var effectiveUser = currentUserOverride ?? _currentUser;
        IReadOnlyCollection<int>? accessibleIds = null;

        if (effectiveWhAccess != null)
        {
            if (request?.WarehouseId.HasValue == true)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            }
            else if (effectiveUser == null || (effectiveUser.IsAuthenticated && !effectiveUser.IsGlobalAdmin))
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var query = _db.ExportReceipts
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .Where(r => r.Status != ReceiptStatus.Cancelled &&
                        r.Status != ReceiptStatus.CancelPendingPutback);

        var explicitReceiptIds = request?.ReceiptIds is { Count: > 0 }
            ? request.ReceiptIds
            : (request?.ExportReceiptIds is { Count: > 0 } ? request.ExportReceiptIds : null);

        if (explicitReceiptIds is { Count: > 0 })
        {
            query = query.Where(r => explicitReceiptIds.Contains(r.Id));
        }
        else if (request?.WarehouseId.HasValue == true)
        {
            query = query.Where(r => r.WarehouseId == request.WarehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
        }

        var hasExplicitReceiptIds = request?.ReceiptIds is { Count: > 0 };
        var receipts = await query.OrderBy(r => r.Id).Take(100).ToListAsync(cancellationToken);

        if (explicitReceiptIds is { Count: > 0 } && effectiveWhAccess != null)
        {
            foreach (var whId in receipts.Select(r => r.WarehouseId).Distinct())
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(whId, cancellationToken);
            }
        }

        var alreadyPrintedReceipts = receipts.Where(r => r.AwbPrintCount >= 1).ToList();
        if (alreadyPrintedReceipts.Count > 0)
        {
            if (!canReprintAwb)
            {
                return WmsEndpointOutcome.Json(403, new
                {
                    code = "AWB_REPRINT_PERMISSION_DENIED",
                    errorCode = "AWB_REPRINT_PERMISSION_DENIED",
                    message = "Bạn không có quyền in lại tem vận đơn (export_receipt.reprint_awb).",
                    reprintReceiptIds = alreadyPrintedReceipts.Select(r => r.Id).ToList()
                });
            }

            var enforceReprintReason = hasExplicitReceiptIds && (request?.EnforceReprintReason ?? true);
            if (enforceReprintReason && string.IsNullOrWhiteSpace(request?.ReprintReason))
            {
                return WmsEndpointOutcome.Conflict(new
                {
                    code = "AWB_REPRINT_REASON_REQUIRED",
                    errorCode = "REPRINT_REASON_REQUIRED",
                    reprintErrorCode = "AWB_REPRINT_REASON_REQUIRED",
                    message = "Phiếu đã từng in tem vận đơn; bắt buộc nhập lý do in lại (ReprintReason).",
                    reprintReceiptIds = alreadyPrintedReceipts.Select(r => r.Id).ToList()
                });
            }
        }

        var warehouseIds = receipts.Select(r => r.WarehouseId).Distinct().ToList();
        var locationsByWh = await _db.WarehouseLocations
            .AsNoTracking()
            .Where(l => warehouseIds.Contains(l.WarehouseId) && l.IsActive)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);

        var enriched = receipts.Select(r =>
        {
            var firstDetail = r.Details.OrderBy(d => d.Product?.Code ?? string.Empty).FirstOrDefault();
            var primaryLocCode = locationsByWh
                .Where(l => l.WarehouseId == r.WarehouseId)
                .Select(l => l.Code)
                .FirstOrDefault() ?? "A-01-01";
            var primarySku = firstDetail?.Product?.Code ?? $"SKU-{firstDetail?.ProductId ?? 1}";
            var totalQty = r.Details.Sum(d => d.Quantity);
            if (totalQty <= 0m) totalQty = 1m;
            r.TrackingNumber ??= $"VN{now:yyMMdd}{r.Id:D6}";
            r.CarrierCode ??= "GHN";
            r.AwbLabelUrl ??= $"/api/v1/export-receipts/{r.Id}/thermal-awb-100x150.pdf";
            return new
            {
                Receipt = r,
                PrimaryPickLocationCode = primaryLocCode,
                PrimaryProductCode = primarySku,
                TotalQuantity = totalQty
            };
        })
        .OrderBy(x => x.PrimaryPickLocationCode, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.PrimaryProductCode, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Receipt.Id)
        .ToList();

        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, currentUserOverride, cancellationToken);
        var labels = new List<object>();
        var htmlPages = new List<string>();
        var pdfTextLines = new List<string>();
        var seq = 1;
        foreach (var item in enriched)
        {
            var r = item.Receipt;
            var wasReprint = r.AwbPrintCount > 0;
            r.AwbPrintCount += 1;
            r.AwbFirstPrintedAtUtc ??= now;

            var footer = $"[Kệ: {item.PrimaryPickLocationCode} | SKU: {item.PrimaryProductCode} x {item.TotalQuantity:0.##}]";
            var watermarkText = r.AwbPrintCount > 1 ? "REPRINT - BẢN SAO" : null;
            var watermarkHtml = wasReprint
                ? $"<div class=\"watermark\">REPRINT - BẢN SAO LẦN {r.AwbPrintCount} ({WebUtility.HtmlEncode(request?.ReprintReason ?? "In lại")})</div>"
                : string.Empty;
            var codAmount = r.Details.Sum(d => decimal.Round(d.Quantity * (d.UnitPrice > 0m ? d.UnitPrice : 125000m), 0));
            var labelHtml = BuildSingleAwbLabelHtml(r, watermarkHtml, codAmount, footer);
            htmlPages.Add(labelHtml);
            pdfTextLines.Add($"AWB:{r.TrackingNumber} | CARRIER:{r.CarrierCode} | {footer}{(wasReprint ? $" | REPRINT - BAN SAO ({request?.ReprintReason})" : string.Empty)}");

            labels.Add(new
            {
                sequence = seq++,
                receiptId = r.Id,
                exportReceiptId = r.Id,
                receiptCode = r.Code,
                exportReceiptCode = r.Code,
                trackingNumber = r.TrackingNumber,
                awbCode = r.TrackingNumber,
                carrierCode = r.CarrierCode,
                carrierName = r.CarrierCode,
                marketplaceOrderId = r.MarketplaceOrderId ?? $"MP-{r.Code}",
                paperSize = "100x150mm",
                primaryPickLocationCode = item.PrimaryPickLocationCode,
                primaryProductCode = item.PrimaryProductCode,
                totalQuantity = item.TotalQuantity,
                pickingSummaryFooter = footer,
                pickSequenceFooter = footer,
                awbPrintCount = r.AwbPrintCount,
                printCount = r.AwbPrintCount,
                awbFirstPrintedAtUtc = r.AwbFirstPrintedAtUtc,
                isReprint = wasReprint,
                isReprintWatermark = wasReprint,
                watermarkText,
                reprintReason = wasReprint ? request?.ReprintReason : null,
                labelHtml
            });

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = r.WarehouseId,
                EntityName = "ExportReceipt",
                EntityId = r.Id,
                Action = wasReprint ? "ExportReceipt.ReprintAwb" : "ExportReceipt.BatchPrintAwb",
                NewValues = $"AwbPrintCount={r.AwbPrintCount}; Tracking={r.TrackingNumber}; Loc={item.PrimaryPickLocationCode}",
                Reason = wasReprint
                    ? $"In lại nhãn vận đơn nhiệt 100x150mm: {request?.ReprintReason}"
                    : "In gộp nhãn vận đơn nhiệt 100x150mm kèm dòng tóm tắt nhặt hàng",
                Result = "Success",
                Timestamp = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        var batchCode = $"AWB-BATCH-{now:yyyyMMdd-HHmmss}";
        var printHtmlDocument = BuildBatchAwbPrintDocumentHtml(batchCode, htmlPages);
        var pdfBytes = BuildThermalAwbPdf14Bytes(batchCode, pdfTextLines);
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        return WmsEndpointOutcome.Ok(new
        {
            awbBatchCode = batchCode,
            paperSize = "100x150mm",
            sortStrategy = "PrimaryPickLocationCode ASC, ProductCode ASC",
            printedCount = labels.Count,
            duplicatePrintBlocked = true,
            printHtmlDocument,
            thermalPrintHtml = printHtmlDocument,
            pdfBase64,
            labels,
            items = labels,
            printedAtUtc = now,
            statusLabel = $"Đã kết xuất lô in nhiệt {labels.Count} vận đơn 100x150mm theo thứ tự tuyến kệ nhặt"
        });
    }

    private static string BuildSingleAwbLabelHtml(ExportReceipt r, string watermarkHtml, decimal codAmount, string footer)
    {
        var senderName = !string.IsNullOrWhiteSpace(r.Warehouse?.Name)
            ? r.Warehouse.Name
            : (r.WarehouseId == 1 ? "Kho Trung tâm (WH01)" : $"Kho trung tâm ERP (#{r.WarehouseId})");
        return $@"<section class=""awb-page"">
  {watermarkHtml}
  <header class=""awb-header"">
    <div><strong>{r.CarrierCode} EXPRESS</strong><br/><span>Mã đơn sàn: {r.MarketplaceOrderId ?? $"MP-{r.Code}"}</span></div>
    <div class=""hub-box"">HUB-{r.CarrierCode}-01</div>
  </header>
  <div class=""barcode-block"">
    <svg width=""260"" height=""48"" viewBox=""0 0 260 48"" xmlns=""http://www.w3.org/2000/svg""><rect x=""4"" y=""4"" width=""6"" height=""40"" fill=""#000""/><rect x=""14"" y=""4"" width=""3"" height=""40"" fill=""#000""/><rect x=""22"" y=""4"" width=""9"" height=""40"" fill=""#000""/><rect x=""36"" y=""4"" width=""4"" height=""40"" fill=""#000""/><rect x=""46"" y=""4"" width=""8"" height=""40"" fill=""#000""/><rect x=""60"" y=""4"" width=""3"" height=""40"" fill=""#000""/><rect x=""70"" y=""4"" width=""10"" height=""40"" fill=""#000""/><rect x=""86"" y=""4"" width=""5"" height=""40"" fill=""#000""/><rect x=""98"" y=""4"" width=""7"" height=""40"" fill=""#000""/><rect x=""112"" y=""4"" width=""4"" height=""40"" fill=""#000""/><rect x=""122"" y=""4"" width=""9"" height=""40"" fill=""#000""/></svg>
    <div class=""tracking-code"">{r.TrackingNumber}</div>
  </div>
  <div class=""addr-grid"">
    <div><strong>Người gửi:</strong> {WebUtility.HtmlEncode(senderName)}</div>
    <div><strong>Người nhận:</strong> {WebUtility.HtmlEncode(r.CustomerNameSnapshot ?? "Khách hàng TMĐT")}</div>
  </div>
  <div class=""cod-box"">Thu hộ COD: <strong>{codAmount:N0} VND</strong></div>
  <footer class=""pick-footer"">{WebUtility.HtmlEncode(footer)}</footer>
</section>";
    }

    private static string BuildBatchAwbPrintDocumentHtml(string batchCode, IReadOnlyList<string> htmlPages) =>
        $@"<!DOCTYPE html>
<html lang=""vi""><head><meta charset=""utf-8""/><title>{batchCode} - Thermal AWB 100x150mm</title>
<style>
@page {{ size: 100mm 150mm; margin: 0; }}
body {{ margin: 0; font-family: Arial, sans-serif; font-size: 11px; }}
.awb-page {{ width: 100mm; height: 150mm; box-sizing: border-box; padding: 4mm; border-bottom: 1px dashed #000; page-break-after: always; position: relative; }}
.watermark {{ position: absolute; top: 45%; left: 8%; transform: rotate(-28deg); font-size: 16px; font-weight: bold; color: rgba(200,0,0,0.35); border: 2px solid rgba(200,0,0,0.35); padding: 4px 8px; }}
.awb-header {{ display: flex; justify-content: space-between; border-bottom: 2px solid #000; padding-bottom: 4px; }}
.hub-box {{ font-size: 16px; font-weight: bold; border: 2px solid #000; padding: 2px 6px; }}
.barcode-block {{ text-align: center; margin: 6px 0; }}
.tracking-code {{ font-size: 14px; font-weight: bold; letter-spacing: 1px; }}
.cod-box {{ margin: 8px 0; padding: 6px; border: 2px solid #000; font-size: 14px; }}
.pick-footer {{ margin-top: 10px; padding-top: 6px; border-top: 1px dashed #000; font-weight: bold; font-size: 11px; }}
</style></head><body>{string.Join("\n", htmlPages)}</body></html>";

    private static byte[] BuildThermalAwbPdf14Bytes(string batchCode, IReadOnlyList<string> lines)
    {
        var sanitizedLines = lines.Count > 0 ? lines : new[] { $"Batch {batchCode} - Thermal AWB 100x150mm" };
        var streamBuilder = new StringBuilder();
        streamBuilder.Append("BT\n/F1 9 Tf\n14 405 Td\n");
        streamBuilder.Append($"(Thermal AWB 100x150mm - {EscapePdfText(batchCode)}) Tj\n");
        foreach (var line in sanitizedLines)
        {
            streamBuilder.Append("0 -16 Td\n");
            streamBuilder.Append($"({EscapePdfText(line)}) Tj\n");
        }
        streamBuilder.Append("ET\n");
        var contentStream = streamBuilder.ToString();
        var contentByteLength = Encoding.UTF8.GetByteCount(contentStream);

        var pdf = new StringBuilder();
        pdf.Append("%PDF-1.4\n");
        var obj1Offset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var obj2Offset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        var obj3Offset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 283.46 425.20] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        var obj4Offset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append($"4 0 obj\n<< /Length {contentByteLength} >>\nstream\n{contentStream}endstream\nendobj\n");
        var obj5Offset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        var xrefOffset = Encoding.UTF8.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        pdf.Append($"{obj1Offset:D10} 00000 n \n");
        pdf.Append($"{obj2Offset:D10} 00000 n \n");
        pdf.Append($"{obj3Offset:D10} 00000 n \n");
        pdf.Append($"{obj4Offset:D10} 00000 n \n");
        pdf.Append($"{obj5Offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return Encoding.UTF8.GetBytes(pdf.ToString());
    }

    private static string EscapePdfText(string input)
    {
        return (input ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }
}
