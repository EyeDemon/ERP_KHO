using System.Globalization;
using ClosedXML.Excel;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Infrastructure;

public static class ReportsExcelHelper
{
    public static async Task<IActionResult> ExecuteReportOpAsync(
        ControllerBase controller,
        IReportOperationsService ops,
        Func<IReportOperationsService, Task<WmsEndpointOutcome>> invokeOp)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var outcome = await invokeOp(ops);
        return controller.ToActionResult(outcome);
    }

    public static async Task<List<CodSettlementItemRequestBase>> ParseCodSettlementFileAsync(
        IFormFile file,
        string? defaultCarrierCode,
        CancellationToken cancellationToken = default)
    {
        var inputItems = new List<CodSettlementItemRequestBase>();
        if (file is not { Length: > 0 })
        {
            return inputItems;
        }

        var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        await using var fileStream = file.OpenReadStream();
        if (ext == ".csv" || ext == ".txt")
        {
            using var reader = new StreamReader(fileStream);
            var isFirstRow = true;
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split([',', ';', '\t']);
                if (isFirstRow)
                {
                    isFirstRow = false;
                    if (cols[0].Contains("Tracking", StringComparison.OrdinalIgnoreCase) ||
                        cols[0].Contains("Mã", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                var tracking = cols.ElementAtOrDefault(0)?.Trim();
                if (string.IsNullOrWhiteSpace(tracking)) continue;
                var carrier = cols.ElementAtOrDefault(1)?.Trim();
                decimal.TryParse(cols.ElementAtOrDefault(2)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var actualCod);
                decimal.TryParse(cols.ElementAtOrDefault(3)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var actualShipFee);
                decimal.TryParse(cols.ElementAtOrDefault(4)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var platformFee);
                decimal.TryParse(cols.ElementAtOrDefault(5)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var returnFee);
                decimal.TryParse(cols.ElementAtOrDefault(6)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedCod);
                decimal.TryParse(cols.ElementAtOrDefault(7)?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var estShipFee);

                inputItems.Add(new CodSettlementItemRequestBase(
                    TrackingNumber: tracking,
                    CarrierCode: string.IsNullOrWhiteSpace(carrier) ? (defaultCarrierCode ?? "GHN") : carrier,
                    ExpectedCodAmount: expectedCod,
                    ActualCodCollected: actualCod,
                    EstimatedShippingFee: estShipFee,
                    ActualShippingFee: actualShipFee,
                    PlatformCommissionFee: platformFee,
                    ReturnFee: returnFee));
            }
        }
        else
        {
            using var workbook = new XLWorkbook(fileStream);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws != null)
            {
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (var r = 2; r <= lastRow; r++)
                {
                    var tracking = ws.Cell(r, 1).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(tracking)) continue;
                    var carrier = ws.Cell(r, 2).GetString().Trim();
                    decimal.TryParse(ws.Cell(r, 3).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var actualCod);
                    decimal.TryParse(ws.Cell(r, 4).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var actualShipFee);
                    decimal.TryParse(ws.Cell(r, 5).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var platformFee);
                    decimal.TryParse(ws.Cell(r, 6).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var returnFee);
                    decimal.TryParse(ws.Cell(r, 7).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedCod);
                    decimal.TryParse(ws.Cell(r, 8).GetString().Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var estShipFee);

                    inputItems.Add(new CodSettlementItemRequestBase(
                        TrackingNumber: tracking,
                        CarrierCode: string.IsNullOrWhiteSpace(carrier) ? (defaultCarrierCode ?? "GHN") : carrier,
                        ExpectedCodAmount: expectedCod,
                        ActualCodCollected: actualCod,
                        EstimatedShippingFee: estShipFee,
                        ActualShippingFee: actualShipFee,
                        PlatformCommissionFee: platformFee,
                        ReturnFee: returnFee));
                }
            }
        }

        return inputItems;
    }

    public static byte[] BuildInventoryReportExcel(IEnumerable<InventoryReportDto> report)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Báo cáo Tồn kho");

        worksheet.Cell(1, 1).Value = "Mã SP";
        worksheet.Cell(1, 2).Value = "Tên SP";
        worksheet.Cell(1, 3).Value = "ĐVT";
        worksheet.Cell(1, 4).Value = "Kho";
        worksheet.Cell(1, 5).Value = "Số lượng tồn";
        worksheet.Cell(1, 6).Value = "Cập nhật lần cuối";
        worksheet.Cell(1, 7).Value = "Ngày báo cáo";
        worksheet.Range("A1:G1").Style.Font.Bold = true;

        var row = 2;
        foreach (var item in report)
        {
            worksheet.Cell(row, 1).Value = item.ProductCode;
            worksheet.Cell(row, 2).Value = item.ProductName;
            worksheet.Cell(row, 3).Value = item.UnitName;
            worksheet.Cell(row, 4).Value = item.WarehouseName;
            worksheet.Cell(row, 5).Value = item.Quantity;
            worksheet.Cell(row, 5).Style.NumberFormat.Format = "#,##0.0000";
            worksheet.Cell(row, 6).Value = item.LastUpdated.HasValue ? item.LastUpdated.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
            worksheet.Cell(row, 7).Value = item.ReportDate.ToString("yyyy-MM-dd HH:mm:ss");
            row++;
        }

        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildInventoryInOutReportExcel(IEnumerable<InventoryInOutReportDto> report)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Báo cáo Xuất nhập tồn");

        worksheet.Cell(1, 1).Value = "Mã SP";
        worksheet.Cell(1, 2).Value = "Tên SP";
        worksheet.Cell(1, 3).Value = "ĐVT";
        worksheet.Cell(1, 4).Value = "Kho";
        worksheet.Cell(1, 5).Value = "Tồn đầu kỳ";
        worksheet.Cell(1, 6).Value = "Nhập kho";
        worksheet.Cell(1, 7).Value = "Nhận điều chuyển";
        worksheet.Cell(1, 8).Value = "Điều chỉnh tăng";
        worksheet.Cell(1, 9).Value = "Tổng tăng";
        worksheet.Cell(1, 10).Value = "Xuất kho";
        worksheet.Cell(1, 11).Value = "Xuất điều chuyển";
        worksheet.Cell(1, 12).Value = "Điều chỉnh giảm";
        worksheet.Cell(1, 13).Value = "Tổng giảm";
        worksheet.Cell(1, 14).Value = "Tồn cuối kỳ";
        worksheet.Range("A1:N1").Style.Font.Bold = true;

        var row = 2;
        foreach (var item in report)
        {
            worksheet.Cell(row, 1).Value = item.ProductCode;
            worksheet.Cell(row, 2).Value = item.ProductName;
            worksheet.Cell(row, 3).Value = item.UnitName;
            worksheet.Cell(row, 4).Value = item.WarehouseName;
            worksheet.Cell(row, 5).Value = item.OpeningQuantity;
            worksheet.Cell(row, 6).Value = item.ImportQuantity;
            worksheet.Cell(row, 7).Value = item.TransferInQuantity;
            worksheet.Cell(row, 8).Value = item.AdjustmentIncreaseQuantity;
            worksheet.Cell(row, 9).Value = item.InQuantity;
            worksheet.Cell(row, 10).Value = item.ExportQuantity;
            worksheet.Cell(row, 11).Value = item.TransferOutQuantity;
            worksheet.Cell(row, 12).Value = item.AdjustmentDecreaseQuantity;
            worksheet.Cell(row, 13).Value = item.OutQuantity;
            worksheet.Cell(row, 14).Value = item.ClosingQuantity;

            worksheet.Range($"E{row}:N{row}").Style.NumberFormat.Format = "#,##0.0000";
            row++;
        }

        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildStockCardS12DnExcel(StockCardS12DnReportDto card)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Thẻ kho S12-DN");

        ws.Cell(1, 1).Value = "Đơn vị: Công ty TNHH Thương mại & Kho vận Đại Phát";
        ws.Cell(2, 1).Value = $"Kho: {card.WarehouseName} ({card.WarehouseCode})";
        ws.Cell(1, 8).Value = card.FormCode;
        ws.Cell(2, 8).Value = card.CircularReference;
        ws.Range("H1:J1").Merge().Style.Font.Bold = true;
        ws.Range("H2:J2").Merge().Style.Font.Italic = true;

        ws.Cell(4, 1).Value = card.ReportTitle;
        ws.Range("A4:J4").Merge();
        ws.Cell(4, 1).Style.Font.Bold = true;
        ws.Cell(4, 1).Style.Font.FontSize = 14;
        ws.Cell(4, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(5, 1).Value = $"Tên, nhãn hiệu, quy cách vật tư, hàng hóa: {card.ProductName} | Mã số (SKU): {card.ProductCode} | Đơn vị tính: {card.UnitName}";
        ws.Range("A5:J5").Merge();
        if (!string.IsNullOrWhiteSpace(card.LotNumber))
        {
            ws.Cell(6, 1).Value = $"Số lô (Lot): {card.LotNumber}";
            ws.Range("A6:J6").Merge();
        }

        const int headerRow = 8;
        ws.Cell(headerRow, 1).Value = "STT";
        ws.Cell(headerRow, 2).Value = "Ngày tháng ghi sổ";
        ws.Cell(headerRow, 3).Value = "Số hiệu chứng từ Nhập";
        ws.Cell(headerRow, 4).Value = "Số hiệu chứng từ Xuất";
        ws.Cell(headerRow, 5).Value = "Ngày tháng chứng từ";
        ws.Cell(headerRow, 6).Value = "Diễn giải";
        ws.Cell(headerRow, 7).Value = "Số lượng Nhập";
        ws.Cell(headerRow, 8).Value = "Số lượng Xuất";
        ws.Cell(headerRow, 9).Value = "Số lượng Tồn";
        ws.Cell(headerRow, 10).Value = "Ký xác nhận của Kế toán";
        ws.Range($"A{headerRow}:J{headerRow}").Style.Font.Bold = true;

        var row = headerRow + 1;
        ws.Cell(row, 6).Value = "Số dư đầu kỳ";
        ws.Cell(row, 6).Style.Font.Bold = true;
        ws.Cell(row, 9).Value = card.OpeningBalance;
        ws.Cell(row, 9).Style.Font.Bold = true;
        ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.0000";
        row++;

        foreach (var item in card.Rows)
        {
            ws.Cell(row, 1).Value = item.LineNumber;
            ws.Cell(row, 2).Value = item.EntryDate.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(row, 3).Value = item.InboundVoucherCode;
            ws.Cell(row, 4).Value = item.OutboundVoucherCode;
            ws.Cell(row, 5).Value = item.VoucherDate.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(row, 6).Value = item.Description;
            ws.Cell(row, 7).Value = item.InboundQty;
            ws.Cell(row, 8).Value = item.OutboundQty;
            ws.Cell(row, 9).Value = item.RunningBalance;
            ws.Cell(row, 10).Value = item.SignatureConfirmation;
            ws.Range($"G{row}:I{row}").Style.NumberFormat.Format = "#,##0.0000";
            row++;
        }

        ws.Cell(row, 6).Value = "Cộng phát sinh trong kỳ & Số dư cuối kỳ";
        ws.Cell(row, 6).Style.Font.Bold = true;
        ws.Cell(row, 7).Value = card.TotalInbound;
        ws.Cell(row, 8).Value = card.TotalOutbound;
        ws.Cell(row, 9).Value = card.ClosingBalance;
        ws.Range($"G{row}:I{row}").Style.Font.Bold = true;
        ws.Range($"G{row}:I{row}").Style.NumberFormat.Format = "#,##0.0000";

        var sigRow = row + 2;
        ws.Cell(sigRow, 2).Value = "Người lập biểu";
        ws.Cell(sigRow, 6).Value = "Thủ kho";
        ws.Cell(sigRow, 9).Value = "Kế toán trưởng";
        ws.Range($"A{sigRow}:J{sigRow}").Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
