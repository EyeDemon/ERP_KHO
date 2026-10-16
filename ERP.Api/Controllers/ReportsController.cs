using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

        [HttpGet("operational-dashboard")]
        [Authorize]
        public async Task<IActionResult> GetOperationalDashboard(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);

            var warehouseCount = await db.Warehouses.AsNoTracking()
                .CountAsync(w => accessibleIds.Contains(w.Id) && w.IsActive, cancellationToken);
            var productCount = await db.Products.AsNoTracking()
                .CountAsync(cancellationToken);
            var inventoryBucketCount = await db.InventoryStocks.AsNoTracking()
                .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && s.Quantity > 0, cancellationToken);
            var invalidStockCount = await db.InventoryStocks.AsNoTracking()
                .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && (s.Quantity < 0 || s.ReservedQuantity < 0 || s.ReservedQuantity > s.Quantity), cancellationToken);

            var openInboundCount = await db.ImportReceipts.AsNoTracking()
                .CountAsync(r => accessibleIds.Contains(r.WarehouseId) && r.Status != ReceiptStatus.Posted && r.Status != ReceiptStatus.Cancelled, cancellationToken);
            var openPutawayCount = await db.PutawayTasks.AsNoTracking()
                .CountAsync(t => accessibleIds.Contains(t.WarehouseId) && t.Status != PutawayTaskStatus.Completed && t.Status != PutawayTaskStatus.Cancelled, cancellationToken);
            var openOutboundCount = await db.ExportReceipts.AsNoTracking()
                .CountAsync(r => accessibleIds.Contains(r.WarehouseId) && (r.Status == ReceiptStatus.Draft || r.Status == ReceiptStatus.Approved), cancellationToken);
            var openTransferCount = await db.StockTransfers.AsNoTracking()
                .CountAsync(t => (accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId)) &&
                                 (t.Status == StockTransferStatus.Draft || t.Status == StockTransferStatus.Approved || t.Status == StockTransferStatus.InTransit), cancellationToken);

            var recentAudits = await db.AuditLogs.AsNoTracking()
                .Where(a => !a.WarehouseId.HasValue || accessibleIds.Contains(a.WarehouseId.Value))
                .OrderByDescending(a => a.Timestamp)
                .Take(10)
                .Select(a => new
                {
                    a.Id,
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.WarehouseId,
                    a.Result,
                    a.Timestamp
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                warehouseCount,
                productCount,
                inventoryBucketCount,
                invalidStockCount,
                openTaskCount = openInboundCount + openPutawayCount + openOutboundCount + openTransferCount,
                openInboundCount,
                openPutawayCount,
                openOutboundCount,
                openTransferCount,
                recentActivities = recentAudits
            });
        }

        [HttpGet("expiry-aging")]
        [Authorize]
        public async Task<IActionResult> GetExpiryAgingReport(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] int? warehouseId = null,
            [FromQuery] int? productId = null,
            [FromQuery] int nearExpiryDays = 30,
            [FromQuery] bool onlyLots = false,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (warehouseId.HasValue && !accessibleIds.Contains(warehouseId.Value))
                return Ok(Array.Empty<object>());

            if (nearExpiryDays < 1) nearExpiryDays = 30;
            var today = DateTime.UtcNow.Date;
            var nearThreshold = today.AddDays(nearExpiryDays);

            var query = db.InventoryStocks.AsNoTracking()
                .Where(s => accessibleIds.Contains(s.WarehouseId) && s.Quantity > 0);

            if (warehouseId.HasValue)
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            if (productId.HasValue)
                query = query.Where(s => s.ProductId == productId.Value);
            if (onlyLots)
                query = query.Where(s => s.LotId.HasValue);

            var rows = await query
                .Select(s => new
                {
                    InventoryStockId = s.Id,
                    s.WarehouseId,
                    WarehouseName = s.Warehouse.Name,
                    s.LocationId,
                    LocationCode = s.Location != null ? s.Location.Code : "DEFAULT",
                    s.ProductId,
                    ProductCode = s.Product.Code,
                    ProductName = s.Product.Name,
                    UnitName = s.Product.Unit.Name,
                    Status = s.Status.ToString(),
                    s.LotId,
                    LotNumber = s.Lot != null ? s.Lot.LotNumber : null,
                    ManufactureDate = s.Lot != null ? s.Lot.ManufactureDate : null,
                    ExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : null,
                    LotCreatedAt = s.Lot != null ? (DateTime?)s.Lot.CreatedAt : null,
                    s.Quantity,
                    s.ReservedQuantity,
                    AvailableQuantity = s.Quantity - s.ReservedQuantity,
                    s.LastUpdated
                })
                .ToListAsync(cancellationToken);

            var result = rows
                .Select(r =>
                {
                    var daysToExpiry = r.ExpiryDate.HasValue
                        ? (int?)(r.ExpiryDate.Value.Date - today).TotalDays
                        : null;
                    var anchorDate = (r.ManufactureDate ?? r.LotCreatedAt ?? r.LastUpdated).Date;
                    var ageDays = Math.Max(0, (int)(today - anchorDate).TotalDays);
                    var expiryStatus = !r.ExpiryDate.HasValue
                        ? "NO_EXPIRY"
                        : r.ExpiryDate.Value.Date < today
                            ? "EXPIRED"
                            : r.ExpiryDate.Value.Date <= nearThreshold
                                ? "NEAR_EXPIRY"
                                : "GOOD";

                    return new
                    {
                        r.InventoryStockId,
                        r.WarehouseId,
                        r.WarehouseName,
                        r.LocationId,
                        r.LocationCode,
                        r.ProductId,
                        r.ProductCode,
                        r.ProductName,
                        r.UnitName,
                        r.Status,
                        r.LotId,
                        r.LotNumber,
                        r.ManufactureDate,
                        r.ExpiryDate,
                        r.Quantity,
                        r.ReservedQuantity,
                        r.AvailableQuantity,
                        DaysToExpiry = daysToExpiry,
                        AgeDays = ageDays,
                        ExpiryStatus = expiryStatus
                    };
                })
                .OrderBy(r => r.ExpiryStatus == "EXPIRED" ? 0 : r.ExpiryStatus == "NEAR_EXPIRY" ? 1 : r.ExpiryStatus == "GOOD" ? 2 : 3)
                .ThenBy(r => r.DaysToExpiry ?? int.MaxValue)
                .ThenByDescending(r => r.AgeDays)
                .ThenBy(r => r.ProductCode)
                .ToList();

            return Ok(result);
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
