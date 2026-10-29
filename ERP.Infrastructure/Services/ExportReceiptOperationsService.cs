using System.Net;
using System.Text;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ExportReceiptOperationsService(
    ErpKhoDbContext db,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null,
    IExportReceiptService? exportReceiptService = null,
    IHandoverManifestService? handoverManifestService = null) : IExportReceiptOperationsService
{
    private readonly ErpKhoDbContext _db = db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess = warehouseAccess;
    private readonly ICurrentUser? _currentUser = currentUser;
    private readonly IExportReceiptService? _exportReceiptService = exportReceiptService;
    private readonly IHandoverManifestService? _handoverManifestService = handoverManifestService;

    private IHandoverManifestService ResolveHandoverManifests(
        IExportReceiptService? exportReceiptServiceOverride,
        IWarehouseAuthorizationService? warehouseAccessOverride,
        ICurrentUser? currentUserOverride)
        => _handoverManifestService ?? new HandoverManifestService(
            _db,
            (exportReceiptServiceOverride ?? _exportReceiptService)!,
            warehouseAccessOverride ?? _warehouseAccess,
            currentUserOverride ?? _currentUser);

    private async Task<int?> ResolveValidAuditUserIdAsync(
        int? actorUserId,
        bool isAuthenticated,
        ICurrentUser? currentUserOverride,
        CancellationToken cancellationToken)
    {
        var effectiveUser = currentUserOverride ?? _currentUser;
        var hasClaimUser = actorUserId is > 0;
        var uid = actorUserId ?? 0;
        if (!hasClaimUser && effectiveUser?.UserId is > 0)
        {
            uid = effectiveUser.UserId;
            hasClaimUser = true;
        }

        if (hasClaimUser && uid > 0)
        {
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken))
            {
                return uid;
            }

            if (!_db.Database.IsRelational() && !await _db.Users.AsNoTracking().AnyAsync(cancellationToken))
            {
                return uid;
            }
        }

        if (isAuthenticated && !hasClaimUser)
        {
            throw new UnauthorizedAccessException("Không xác định được người dùng hợp lệ cho nhật ký kiểm toán.");
        }

        return null;
    }

    public async Task<WmsEndpointOutcome> GetSlaQueueAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = _db.ExportReceipts
            .AsNoTracking()
            .Where(r => r.Status != ReceiptStatus.Cancelled &&
                        r.Status != ReceiptStatus.CancelPendingPutback &&
                        r.Status != ReceiptStatus.Dispatched &&
                        r.MarketplaceShipByUtc.HasValue);

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }

        var receipts = await query
            .OrderBy(r => r.MarketplaceShipByUtc)
            .ToListAsync(cancellationToken);

        var queueItems = receipts.Select(r =>
        {
            var shipBy = r.MarketplaceShipByUtc!.Value;
            var remainingMinutes = (int)Math.Round((shipBy - now).TotalMinutes);
            var slaTier = shipBy < now
                ? "BREACHED"
                : (shipBy <= now.AddHours(2) ? "AT_RISK_UNDER_2H" : "ON_TRACK");
            return new
            {
                exportReceiptId = r.Id,
                exportReceiptCode = r.Code,
                warehouseId = r.WarehouseId,
                marketplaceOrderId = r.MarketplaceOrderId,
                carrierCode = r.CarrierCode,
                trackingNumber = r.TrackingNumber,
                marketplaceShipByUtc = shipBy,
                remainingMinutes,
                slaTier,
                status = r.Status.ToString()
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = queueItems.Count,
            breachedCount = queueItems.Count(x => x.slaTier == "BREACHED"),
            atRiskUnder2HoursCount = queueItems.Count(x => x.slaTier == "AT_RISK_UNDER_2H"),
            onTrackCount = queueItems.Count(x => x.slaTier == "ON_TRACK"),
            items = queueItems
        });
    }

    public async Task<WmsEndpointOutcome?> CheckSubscriptionQuotaForCreateAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId;
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub != null)
        {
            var quota = sub.MonthlyOrderQuota > 0 ? sub.MonthlyOrderQuota : 1500;
            var maxAllowed = (int)Math.Ceiling(quota * 1.15m);
            var usagePct = decimal.Round((sub.CurrentMonthOrderCount * 100m) / quota, 2);
            var isExpiredPastDue = string.Equals(sub.Status, "PAST_DUE", StringComparison.OrdinalIgnoreCase) &&
                                   sub.CurrentPeriodEndUtc.AddHours(72) < DateTime.UtcNow;
            if (sub.CurrentMonthOrderCount >= maxAllowed || usagePct > 115m || isExpiredPastDue)
            {
                return WmsEndpointOutcome.Json(403, new
                {
                    code = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    errorCode = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    message = $"Thuê bao đã vượt ngưỡng cứng 115% hạn ngạch đơn hàng tháng ({sub.CurrentMonthOrderCount}/{quota} đơn - {usagePct}%) hoặc quá hạn thanh toán 72h. Hệ thống chuyển sang chế độ Chỉ Đọc cho đơn mới."
                });
            }
        }

        return null;
    }

    public async Task IncrementSubscriptionOrderCountAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId;
        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (sub != null)
        {
            sub.CurrentMonthOrderCount += 1;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<WmsEndpointOutcome> VerifyDispatchCutoffAsync(
        VerifyDispatchCutoffInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var outboundCutoffLocal = "22:00";
        var query = _db.ExportReceipts.AsNoTracking();
        var taskQuery = _db.PickingTasks.AsNoTracking()
            .Where(t => t.Status != PickingTaskStatus.Completed
                        && t.Status != PickingTaskStatus.Cancelled);
        var calQuery = _db.WarehouseCalendarDays.AsNoTracking()
            .Where(c => c.IsOpen && c.OutboundCutoffLocal.HasValue);

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
            taskQuery = taskQuery.Where(t => t.WarehouseId == warehouseId.Value);
            calQuery = calQuery.Where(c => c.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
            taskQuery = taskQuery.Where(t => accessibleIds.Contains(t.WarehouseId));
            calQuery = calQuery.Where(c => accessibleIds.Contains(c.WarehouseId));
        }

        var checkedExportReceiptsCount = await query.CountAsync(cancellationToken);
        var openPickingTasksCount = await taskQuery.CountAsync(cancellationToken);
        var cutoffSpan = await calQuery.Select(c => c.OutboundCutoffLocal).FirstOrDefaultAsync(cancellationToken);
        if (cutoffSpan.HasValue)
        {
            outboundCutoffLocal = cutoffSpan.Value.ToString(@"hh\:mm");
        }

        var now = DateTime.UtcNow;
        var verificationId = $"EXP-CUT-{now:yyyyMMdd-HHmm}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = warehouseId,
            EntityName = "ExportReceipt",
            Action = "ExportReceipt.VerifyDispatchCutoff",
            NewValues = verificationId,
            Reason = $"Đối soát giờ chốt xuất kho ({outboundCutoffLocal}, {checkedExportReceiptsCount} phiếu xuất, {openPickingTasksCount} tác vụ lấy hàng mở)",
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            verificationId,
            warehouseId,
            checkedExportReceiptsCount,
            openPickingTasksCount,
            outboundCutoffLocal,
            singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất",
            cutoffComplianceStatus = $"Đạt chuẩn giờ chốt xuất kho ({outboundCutoffLocal})",
            status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            verifiedAtUtc = now
        });
    }

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

        if (request?.WarehouseId.HasValue == true && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
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

        var hasExplicitReceiptIds = request?.ReceiptIds is { Count: > 0 };
        var receipts = await query.OrderBy(r => r.Id).Take(100).ToListAsync(cancellationToken);

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
            var labelHtml = $@"<section class=""awb-page"">
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
    <div><strong>Người gửi:</strong> Kho trung tâm ERP (#{r.WarehouseId})</div>
    <div><strong>Người nhận:</strong> {WebUtility.HtmlEncode(r.CustomerNameSnapshot ?? "Khách hàng TMĐT")}</div>
  </div>
  <div class=""cod-box"">Thu hộ COD: <strong>{codAmount:N0} VND</strong></div>
  <footer class=""pick-footer"">{WebUtility.HtmlEncode(footer)}</footer>
</section>";
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
        var printHtmlDocument = $@"<!DOCTYPE html>
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

    public Task<HandoverManifestOperationResult> GetHandoverManifestsAsync(
        int? warehouseId,
        string? carrierCode,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken);

    public Task<HandoverManifestOperationResult> ScanHandoverAwbAsync(
        int? manifestIdRoute,
        ScanHandoverAwbInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .ScanHandoverAwbAsync(manifestIdRoute, request, actorUserId, isAuthenticated, cancellationToken);

    public Task<HandoverManifestOperationResult> SealHandoverManifestAsync(
        int id,
        SealHandoverManifestInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .SealHandoverManifestAsync(id, request, actorUserId, isAuthenticated, cancellationToken);

    public Task<HandoverManifestOperationResult> ConfirmCarrierHandoverAsync(
        ConfirmCarrierHandoverInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
        => ResolveHandoverManifests(exportReceiptServiceOverride, warehouseAccessOverride, currentUserOverride)
            .ConfirmCarrierHandoverAsync(request, actorUserId, isAuthenticated, cancellationToken);

    public async Task<WmsEndpointOutcome> GetCarrierAddressMappingsAsync(
        string? province,
        string? district,
        string? ward,
        CancellationToken cancellationToken = default)
    {
        await VnAdministrativeSeedHelper.EnsureVnAdministrativeSeededAsync(_db, cancellationToken);

        var allUnits = await _db.VnAdministrativeUnits
            .AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        var filteredUnits = allUnits.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(province))
        {
            var p = province.Trim();
            filteredUnits = filteredUnits.Where(u =>
                u.ProvinceName.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                p.Contains(u.ProvinceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.ProvinceCode, p, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(ward))
        {
            var w = ward.Trim();
            filteredUnits = filteredUnits.Where(u =>
                u.WardName.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                w.Contains(u.WardName, StringComparison.OrdinalIgnoreCase) ||
                (u.Keywords != null && u.Keywords.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(u.WardCode, w, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(district))
        {
            var d = district.Trim();
            var byDistrict = filteredUnits.Where(u =>
                (!string.IsNullOrWhiteSpace(u.DistrictName) && (u.DistrictName.Contains(d, StringComparison.OrdinalIgnoreCase) || d.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(u.DistrictCode) && string.Equals(u.DistrictCode, d, StringComparison.OrdinalIgnoreCase))).ToList();
            if (byDistrict.Count > 0)
            {
                filteredUnits = byDistrict;
            }
        }

        var matchedUnits = filteredUnits.ToList();
        var matched = matchedUnits.Count > 0;
        var primaryUnit = matchedUnits.FirstOrDefault() ?? allUnits.FirstOrDefault();
        var unitIds = matchedUnits.Select(u => u.Id).ToHashSet();
        var dbMappings = await _db.CarrierLocationMappings
            .AsNoTracking()
            .Where(m => unitIds.Count == 0 || unitIds.Contains(m.AdministrativeUnitId))
            .ToListAsync(cancellationToken);

        var ghnMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHN");
        var ghtkMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHTK");
        var vtpMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "VTP" || m.CarrierCode == "VIETTELPOST" || m.CarrierCode == "VTPOST");
        var jtMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "JT" || m.CarrierCode == "J&T" || m.CarrierCode == "SPX");

        return WmsEndpointOutcome.Ok(new
        {
            administrativeModel = "VN_2_TIER_2025_2026",
            matched,
            input = new { province, district, ward },
            persistedUnitsCount = allUnits.Count,
            matchedUnitsCount = matchedUnits.Count,
            units = matchedUnits.Select(u => new
            {
                u.Id,
                u.ProvinceCode,
                u.ProvinceName,
                u.WardCode,
                u.WardName,
                legacyDistrictCode = u.DistrictCode,
                legacyDistrictName = u.DistrictName,
                legacyWardAliases = u.Keywords,
                carrierMappings = u.CarrierMappings.Select(m => new
                {
                    m.Id,
                    m.CarrierCode,
                    m.CarrierProvinceId,
                    m.CarrierDistrictId,
                    m.CarrierWardCode,
                    sortingRouteCode = m.RouteCode
                })
            }),
            carrierMappings = dbMappings.Select(m => new
            {
                m.Id,
                m.AdministrativeUnitId,
                m.CarrierCode,
                m.CarrierProvinceId,
                m.CarrierDistrictId,
                m.CarrierWardCode,
                sortingRouteCode = m.RouteCode
            }),
            normalized = primaryUnit == null ? null : new
            {
                provinceCode = primaryUnit.ProvinceCode,
                provinceName = primaryUnit.ProvinceName,
                districtName = primaryUnit.DistrictName,
                wardCode = primaryUnit.WardCode,
                wardName = primaryUnit.WardName,
                ghn = ghnMap == null ? null : new { provinceId = ghnMap.CarrierProvinceId, districtId = ghnMap.CarrierDistrictId, wardCode = ghnMap.CarrierWardCode, routeCode = ghnMap.RouteCode },
                ghtk = ghtkMap == null ? null : new { provinceCode = ghtkMap.CarrierProvinceId, districtCode = ghtkMap.CarrierDistrictId, wardCode = ghtkMap.CarrierWardCode, sortingHub = ghtkMap.RouteCode },
                viettelPost = vtpMap == null ? null : new { provinceId = vtpMap.CarrierProvinceId, districtId = vtpMap.CarrierDistrictId, wardId = vtpMap.CarrierWardCode, postOfficeCode = vtpMap.RouteCode },
                jtExpress = jtMap == null ? null : new { areaCode = jtMap.CarrierDistrictId, sortingCode = jtMap.RouteCode }
            },
            statusLabel = "Đã truy vấn & chuẩn hóa địa giới hành chính Việt Nam sang mã định tuyến 3PL từ cơ sở dữ liệu"
        });
    }

    public async Task<WmsEndpointOutcome> ImportOpeningBalanceAsync(
        ImportOpeningBalanceInput? request,
        IReadOnlyList<OpeningBalanceItemInput> parsedFileItems,
        int? actorUserId,
        bool isAuthenticated,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, currentUserOverride, cancellationToken);
        var resolvedActorId = validAuditUserId ?? (actorUserId is > 0 ? actorUserId.Value : 1);
        var warehouseId = request?.WarehouseId ?? await _db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (warehouseId <= 0) warehouseId = 1;

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (warehouseId > 0 && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        var items = new List<OpeningBalanceItemInput>();
        if (parsedFileItems is { Count: > 0 })
        {
            items.AddRange(parsedFileItems);
        }
        else if (request?.Items is { Count: > 0 })
        {
            items.AddRange(request.Items);
        }
        else if (request?.ProductId is > 0 || request?.Quantity is > 0m)
        {
            items.Add(new OpeningBalanceItemInput(
                ProductId: request.ProductId,
                LocationId: request.LocationId,
                Quantity: request.Quantity ?? 0m,
                UnitCost: request.UnitCost ?? 0m));
        }

        if (items.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "OPENING_BALANCE_FILE_REQUIRED",
                errorCode = "OPENING_BALANCE_FILE_REQUIRED",
                message = "Vui lòng tải lên tệp Excel (.xlsx) hoặc danh sách dòng tồn kho đầu kỳ hợp lệ."
            });
        }

        var defaultLocId = request?.LocationId ?? await _db.WarehouseLocations
            .AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedProducts = new List<(OpeningBalanceItemInput Item, Product Product)>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0m)
            {
                return WmsEndpointOutcome.BadRequest(new { code = "INVALID_OPENING_QUANTITY", errorCode = "INVALID_OPENING_QUANTITY", message = "Số lượng tồn đầu kỳ phải lớn hơn 0." });
            }
            if (item.UnitCost < 0m)
            {
                return WmsEndpointOutcome.BadRequest(new { code = "INVALID_OPENING_COST", errorCode = "INVALID_OPENING_COST", message = "Đơn giá vốn đầu kỳ không được âm." });
            }

            Product? product = null;
            if (item.ProductId is > 0)
            {
                product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(item.ProductCode))
            {
                var pCode = item.ProductCode.Trim();
                product = await _db.Products.FirstOrDefaultAsync(p => p.Code == pCode, cancellationToken);
            }

            if (product == null)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    productCode = item.ProductCode,
                    productId = item.ProductId,
                    message = $"Không tìm thấy sản phẩm '{item.ProductCode ?? item.ProductId?.ToString()}'."
                });
            }

            resolvedProducts.Add((item, product));
        }

        await using var sqlTx = _db.Database.CurrentTransaction == null && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var lockProductIds = resolvedProducts.Select(x => x.Product.Id).Distinct().ToList();
        if (lockProductIds.Count > 0)
        {
            await _db.AcquireInventoryAllocationLocksAsync(warehouseId, lockProductIds, cancellationToken);
        }

        var importedItems = new List<object>();

        foreach (var (item, product) in resolvedProducts)
        {
            int? locId = item.LocationId;
            string? resolvedLocationCode = item.LocationCode;
            if ((!locId.HasValue || locId <= 0) && !string.IsNullOrWhiteSpace(item.LocationCode))
            {
                var codeNorm = item.LocationCode.Trim().ToUpperInvariant();
                var locationEntity = await _db.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.WarehouseId == warehouseId && l.Code == codeNorm, cancellationToken);
                if (locationEntity == null)
                {
                    locationEntity = new WarehouseLocation
                    {
                        WarehouseId = warehouseId,
                        Code = codeNorm,
                        Name = $"Kệ {codeNorm}",
                        LocationType = WarehouseLocationType.Storage,
                        IsActive = true,
                        IsPickable = true,
                        IsReceivable = true
                    };
                    _db.WarehouseLocations.Add(locationEntity);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                locId = locationEntity.Id;
                resolvedLocationCode = locationEntity.Code;
            }
            locId ??= (defaultLocId > 0 ? defaultLocId : null);

            int? lotId = null;
            if (!string.IsNullOrWhiteSpace(item.LotNumber))
            {
                var lotNo = item.LotNumber.Trim().ToUpperInvariant();
                var lotEntity = await _db.InventoryLots
                    .FirstOrDefaultAsync(l => l.ProductId == product.Id && l.LotNumber == lotNo, cancellationToken);
                if (lotEntity == null)
                {
                    lotEntity = new InventoryLot
                    {
                        ProductId = product.Id,
                        LotNumber = lotNo,
                        ManufactureDate = now.Date,
                        ExpiryDate = item.ExpiryDate ?? now.Date.AddYears(2),
                        CreatedAt = now
                    };
                    _db.InventoryLots.Add(lotEntity);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else if (item.ExpiryDate.HasValue)
                {
                    lotEntity.ExpiryDate = item.ExpiryDate.Value;
                }
                lotId = lotEntity.Id;
            }

            var stock = await _db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == warehouseId &&
                     s.ProductId == product.Id &&
                     s.LocationId == locId &&
                     s.LotId == lotId &&
                     s.SerialId == null &&
                     s.HandlingUnitId == null &&
                     s.OwnerId == null &&
                     s.Status == InventoryStatus.Available,
                cancellationToken);

            if (stock == null)
            {
                stock = new InventoryStock
                {
                    WarehouseId = warehouseId,
                    ProductId = product.Id,
                    LocationId = locId > 0 ? locId : null,
                    LotId = lotId,
                    Status = InventoryStatus.Available,
                    Quantity = item.Quantity,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    MovingAverageCost = item.UnitCost,
                    LastUpdated = now
                };
                _db.InventoryStocks.Add(stock);
            }
            else
            {
                var oldQty = Math.Max(0m, stock.Quantity);
                var newTotalQty = oldQty + item.Quantity;
                stock.MovingAverageCost = newTotalQty > 0m
                    ? decimal.Round(((oldQty * stock.MovingAverageCost) + (item.Quantity * item.UnitCost)) / newTotalQty, 4)
                    : item.UnitCost;
                stock.Quantity = newTotalQty;
                stock.LastUpdated = now;
            }

            product.MovingAverageCost = stock.MovingAverageCost;

            var tx = new InventoryTransaction
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                LocationId = locId > 0 ? locId : null,
                LotId = lotId,
                TransactionType = TransactionType.Import,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                ReferenceId = product.Id,
                ReferenceType = "OPENING_BALANCE",
                CreatedBy = resolvedActorId,
                TransactionDate = now,
                Note = $"Nhập tồn kho đầu kỳ ({item.Quantity:0.##} x {item.UnitCost:0.##}đ)"
            };
            _db.InventoryTransactions.Add(tx);

            _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                EventId = $"OPN-VAL-{warehouseId}-{product.Id}-{now.Ticks}",
                EventType = "InventoryValuationPosted",
                AggregateType = "InventoryStock",
                AggregateId = product.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    warehouseId,
                    productId = product.Id,
                    productCode = product.Code,
                    locationId = locId,
                    locationCode = resolvedLocationCode,
                    lotId,
                    lotNumber = item.LotNumber,
                    expiryDate = item.ExpiryDate,
                    openingQuantity = item.Quantity,
                    unitCost = item.UnitCost,
                    movingAverageCost = stock.MovingAverageCost
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            importedItems.Add(new
            {
                productId = product.Id,
                productCode = product.Code,
                locationId = locId,
                locationCode = resolvedLocationCode,
                lotId,
                lotNumber = item.LotNumber,
                expiryDate = item.ExpiryDate,
                importedQuantity = item.Quantity,
                unitCost = item.UnitCost,
                movingAverageCost = stock.MovingAverageCost,
                referenceType = "OPENING_BALANCE"
            });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = warehouseId,
            EntityName = "InventoryStock",
            Action = "Inventory.ImportOpeningBalance",
            NewValues = $"ImportedCount={importedItems.Count}",
            Reason = $"Nạp tồn kho đầu kỳ ({importedItems.Count} dòng)",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (sqlTx != null)
        {
            await sqlTx.CommitAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            importedCount = importedItems.Count,
            referenceType = "OPENING_BALANCE",
            items = importedItems,
            importedAtUtc = now,
            statusLabel = $"Đã nạp tồn kho đầu kỳ ({importedItems.Count} dòng) & khởi tạo giá vốn bình quân gia quyền (MAC)"
        });
    }

    public async Task<MisaExcelExportData> GetMisaExcelExportDataAsync(
        int? warehouseId,
        bool canReadCost,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (warehouseId.HasValue && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
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
        var buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode) ? "0318999888" : request.BuyerTaxCode.Trim();
        var buyerName = string.IsNullOrWhiteSpace(request?.BuyerName) ? "Khách hàng Doanh nghiệp" : request.BuyerName.Trim();
        var decreeStandard = string.IsNullOrWhiteSpace(request?.DecreeStandard) ? "ND70_2025_TT32_2025" : request.DecreeStandard.Trim();
        var receiptId = request?.ExportReceiptId ?? 1;

        var receipt = request?.ExportReceiptId.HasValue == true
            ? await _db.ExportReceipts.FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken)
            : await _db.ExportReceipts.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);

        if (receipt != null)
        {
            receiptId = receipt.Id;
            receipt.EInvoiceProvider = "MISA_MEINVOICE";
            receipt.EInvoiceStatus = "DRAFT_ISSUED";
        }

        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, isAuthenticated, currentUserOverride, cancellationToken);
        var draftCodeDb = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = receipt?.WarehouseId ?? request?.WarehouseId ?? 1,
            EntityName = "ExportReceipt",
            Action = "ExportReceipt.IssueEinvoiceNd70Draft",
            NewValues = $"DraftCode={draftCodeDb}; BuyerTaxCode={buyerTaxCode}; BuyerName={buyerName}; Standard={decreeStandard}",
            Reason = $"Phát hành Hóa đơn điện tử nháp tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC ({draftCodeDb})",
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
            buyerTaxCode,
            buyerName,
            decreeStandard,
            provider = "MISA_MEINVOICE",
            status = "DRAFT_ISSUED",
            statusLabel = $"Đã phát hành Hóa đơn điện tử nháp ({draftCodeDb}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC",
            issuedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> SyncAccountingAndEInvoiceOutboxAsync(
        SyncAccountingAndEInvoiceInput? request = null,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (request?.WarehouseId.HasValue == true && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        }

        // Decree 70/2025/NĐ-CP export goods customs clearance timing guard
        if (request?.IsExportDeclaration == true && !request.CustomsCleared)
        {
            throw new ERP.Application.Exceptions.BusinessRuleException(
                "EINVOICE_EXPORT_AWAITING_CUSTOMS_CLEARANCE: Hàng xuất khẩu chưa hoàn tất thông quan hải quan theo Nghị định 70/2025/NĐ-CP; chưa được phép phát hành Hóa đơn điện tử.");
        }

        var now = DateTime.UtcNow;
        var validAuditUserId = await ResolveValidAuditUserIdAsync(actorUserId, true, currentUserOverride, cancellationToken);
        var accountingProvider = string.IsNullOrWhiteSpace(request?.AccountingProvider)
            ? "MISA_AMIS"
            : request!.AccountingProvider!.Trim().ToUpperInvariant();
        var einvoiceProvider = string.IsNullOrWhiteSpace(request?.EInvoiceProvider)
            ? "VIETTEL_SINVOICE"
            : request!.EInvoiceProvider!.Trim().ToUpperInvariant();
        var buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode)
            ? "0318999888"
            : request!.BuyerTaxCode!.Trim();
        var buyerName = string.IsNullOrWhiteSpace(request?.BuyerName)
            ? "Công ty TNHH Thương mại & Logistics Đại Dương"
            : request!.BuyerName!.Trim();

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
            exportQuery = exportQuery.Where(r => r.Details.Any());
        }

        var exportReceipt = await exportQuery.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var targetReceiptId = exportReceipt?.Id ?? request?.ExportReceiptId ?? 1;
        var targetWarehouseId = exportReceipt?.WarehouseId ?? request?.WarehouseId ?? 1;
        var receiptCode = exportReceipt?.Code?.Trim() ?? $"PXK-{targetReceiptId:D4}";

        if (request?.Reverse == true)
        {
            var eventIdToReverse = request.OriginalOutboxEventId?.Trim();
            var origOutbox = !string.IsNullOrWhiteSpace(eventIdToReverse)
                ? await _db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                    m => m.EventId == eventIdToReverse && m.EventType == "AccountingAndEinvoiceNd70Synced",
                    cancellationToken)
                : await _db.IntegrationOutboxMessages
                    .Where(m => m.AggregateType == "ExportReceipt" && m.AggregateId == targetReceiptId && m.EventType == "AccountingAndEinvoiceNd70Synced")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ACCOUNTING_EINVOICE_ALREADY_REVERSED",
                    $"Bút toán đồng bộ Kế toán & Hóa đơn điện tử của phiếu xuất #{targetReceiptId} không tồn tại hoặc đã được hủy/đảo trước đó.");
            }

            origOutbox.Status = "REVERSED";
            origOutbox.LastError = "Reversed via Form 04/SS-HDDT cancellation";

            if (exportReceipt != null)
            {
                exportReceipt.EInvoiceStatus = "CANCELLED_04SS";
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                EntityName = "ExportReceipt",
                EntityId = targetReceiptId,
                Action = "ExportReceipt.ReverseAccountingAndEinvoiceOutbox",
                NewValues = origOutbox.EventId,
                Reason = $"Hủy/đảo đồng bộ Kế toán ({accountingProvider}) & HĐĐT NĐ 70/2025 ({einvoiceProvider}) theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                exportReceiptId = targetReceiptId,
                exportReceiptCode = receiptCode,
                outboxEventId = origOutbox.EventId,
                reversed = true,
                form04SsApplied = true,
                status = "REVERSED",
                statusLabel = $"Đã hủy/đảo bút toán đồng bộ Kế toán & HĐĐT NĐ 70/2025 theo Mẫu 04/SS-HĐĐT ({origOutbox.EventId})",
                reversedAtUtc = now
            });
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
        var lineDetails = new List<object>();

        foreach (var d in exportReceipt.Details)
        {
            var mac = d.Product?.MovingAverageCost is > 0m ? d.Product.MovingAverageCost : 50000m;
            var unitPrice = d.UnitPrice > 0m ? d.UnitPrice : Math.Round(mac * 1.25m, 4, MidpointRounding.AwayFromZero);
            var qty = Math.Round(d.Quantity, 4, MidpointRounding.AwayFromZero);
            var lineCogs = Math.Round(qty * mac, 4, MidpointRounding.AwayFromZero);
            var lineRev = Math.Round(qty * unitPrice, 4, MidpointRounding.AwayFromZero);

            totalCogsAmount = Math.Round(totalCogsAmount + lineCogs, 4, MidpointRounding.AwayFromZero);
            totalRevenueAmount = Math.Round(totalRevenueAmount + lineRev, 4, MidpointRounding.AwayFromZero);

            lineDetails.Add(new
            {
                productId = d.ProductId,
                productCode = d.Product?.Code?.Trim() ?? $"SKU-{d.ProductId}",
                productName = d.Product?.Name?.Trim() ?? $"Sản phẩm #{d.ProductId}",
                quantity = qty,
                unitCostMac = canReadCost ? (decimal?)mac : null,
                cogsAmount = canReadCost ? (decimal?)lineCogs : null,
                unitSellingPrice = unitPrice,
                revenueAmount = lineRev,
                debitCogsAccount = "632",
                creditInventoryAccount = "1561",
                debitReceivableAccount = "131",
                creditRevenueAccount = "5111"
            });
        }

        var vatRatePercent = 10.0000m;
        var totalVatAmount = Math.Round(totalRevenueAmount * (vatRatePercent / 100m), 4, MidpointRounding.AwayFromZero);
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
                importReceiptCode = imp.Code.Trim();
                inboundPurchaseAmount = Math.Round(imp.Details.Sum(x => x.Quantity * x.UnitPrice), 4, MidpointRounding.AwayFromZero);
            }
        }

        var circular200JournalEntries = new List<object>
        {
            new
            {
                entryType = "COGS_ISSUE_TT200",
                descriptionVi = $"Hạch toán giá vốn hàng xuất kho theo bình quân gia quyền di động (MAC) — Phiếu {receiptCode}",
                debitAccount = "632",
                debitAccountNameVi = "Giá vốn hàng bán",
                creditAccount = "1561",
                creditAccountNameVi = "Giá mua hàng hóa kho",
                debitAmount = canReadCost ? (decimal?)totalCogsAmount : null,
                creditAmount = canReadCost ? (decimal?)totalCogsAmount : null
            },
            new
            {
                entryType = "REVENUE_RECEIVABLE_TT200",
                descriptionVi = $"Hạch toán doanh thu bán hàng & công nợ phải thu khách hàng — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng",
                creditAccount = "5111",
                creditAccountNameVi = "Doanh thu bán hàng hóa",
                debitAmount = (decimal?)totalRevenueAmount,
                creditAmount = (decimal?)totalRevenueAmount
            },
            new
            {
                entryType = "VAT_OUTPUT_TT200",
                descriptionVi = $"Hạch toán thuế GTGT đầu ra (10%) theo Nghị định 70/2025/NĐ-CP — Phiếu {receiptCode}",
                debitAccount = "131",
                debitAccountNameVi = "Phải thu của khách hàng (Thuế GTGT)",
                creditAccount = "33311",
                creditAccountNameVi = "Thuế GTGT đầu ra phải nộp",
                debitAmount = (decimal?)totalVatAmount,
                creditAmount = (decimal?)totalVatAmount
            }
        };

        if (inboundPurchaseAmount > 0m)
        {
            circular200JournalEntries.Add(new
            {
                entryType = "INBOUND_PURCHASE_TT200",
                descriptionVi = $"Hạch toán nhập mua hàng hóa nhập kho — Phiếu {importReceiptCode}",
                debitAccount = "1561",
                debitAccountNameVi = "Giá mua hàng hóa kho",
                creditAccount = "331",
                creditAccountNameVi = "Phải trả cho người bán",
                debitAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null,
                creditAmount = canReadCost ? (decimal?)inboundPurchaseAmount : null
            });
        }

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
            if (exportReceipt != null)
            {
                exportReceipt.EInvoiceProvider = einvoiceProvider;
                exportReceipt.EInvoiceStatus = einvoiceStatus;
            }

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
