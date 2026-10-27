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
        IReportOperationsService? ops,
        Func<IReportOperationsService, Task<WmsEndpointOutcome>> invokeOp,
        Func<Task<WmsEndpointOutcome>> invokeFallback)
    {
        var outcome = ops != null ? await invokeOp(ops) : await invokeFallback();
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
}
