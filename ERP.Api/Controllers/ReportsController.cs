using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ClosedXML.Excel;
using System.IO;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("inventory")]
        public async Task<IActionResult> GetInventoryReport(
            [FromQuery] DateTime? asOfDate, 
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId,
            CancellationToken cancellationToken = default)
        {
            var report = await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken);
            return Ok(report);
        }

        [HttpGet("inventory/export")]
        public async Task<IActionResult> ExportInventoryReport(
            [FromQuery] DateTime? asOfDate, 
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId,
            CancellationToken cancellationToken = default)
        {
            var report = await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Báo cáo Tồn kho");
            
            // Header
            worksheet.Cell(1, 1).Value = "Mã SP";
            worksheet.Cell(1, 2).Value = "Tên SP";
            worksheet.Cell(1, 3).Value = "ĐVT";
            worksheet.Cell(1, 4).Value = "Kho";
            worksheet.Cell(1, 5).Value = "Số lượng tồn";
            worksheet.Cell(1, 6).Value = "Cập nhật lần cuối";
            worksheet.Cell(1, 7).Value = "Ngày báo cáo";
            worksheet.Range("A1:G1").Style.Font.Bold = true;

            int row = 2;
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
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoTonKho_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        [HttpGet("inventory-in-out-stock")]
        public async Task<IActionResult> GetInventoryInOutReport(
            [FromQuery] DateTime? fromDate, 
            [FromQuery] DateTime? toDate, 
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var report = await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
                return Ok(report);
            }
            catch (ERP.Application.Exceptions.BusinessRuleException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("inventory-in-out-stock/export")]
        public async Task<IActionResult> ExportInventoryInOutReport(
            [FromQuery] DateTime? fromDate, 
            [FromQuery] DateTime? toDate, 
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var report = await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
                
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Báo cáo Xuất nhập tồn");
                
                // Header
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

                int row = 2;
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
                var content = stream.ToArray();

                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoXuatNhapTon_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            }
            catch (ERP.Application.Exceptions.BusinessRuleException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
