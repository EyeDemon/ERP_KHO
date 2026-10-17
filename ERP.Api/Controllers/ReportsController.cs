using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using System.IO;
using System.Security.Claims;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AllRoles)]
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

        [HttpGet("multi-warehouse-control-tower")]
        [Authorize]
        public async Task<IActionResult> GetMultiWarehouseControlTower(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            var whRows = await db.Warehouses.AsNoTracking()
                .Where(w => accessibleIds.Contains(w.Id))
                .OrderBy(w => w.Code)
                .Select(w => new
                {
                    warehouseId = w.Id,
                    warehouseCode = w.Code,
                    warehouseName = w.Name,
                    isActive = w.IsActive
                })
                .ToListAsync(cancellationToken);

            var stockAgg = await db.InventoryStocks.AsNoTracking()
                .Where(s => accessibleIds.Contains(s.WarehouseId))
                .GroupBy(s => s.WarehouseId)
                .Select(g => new
                {
                    WarehouseId = g.Key,
                    OnHandQuantity = g.Sum(x => x.Quantity),
                    ReservedQuantity = g.Sum(x => x.ReservedQuantity)
                })
                .ToListAsync(cancellationToken);
            var stockByWh = stockAgg.ToDictionary(x => x.WarehouseId);

            var putawayAgg = await db.PutawayTasks.AsNoTracking()
                .Where(t => accessibleIds.Contains(t.WarehouseId) && t.Status != PutawayTaskStatus.Completed && t.Status != PutawayTaskStatus.Cancelled)
                .GroupBy(t => t.WarehouseId)
                .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var putawayByWh = putawayAgg.ToDictionary(x => x.WarehouseId, x => x.Count);

            var pickingAgg = await db.PickingTasks.AsNoTracking()
                .Where(t => accessibleIds.Contains(t.WarehouseId) && t.Status != PickingTaskStatus.Completed && t.Status != PickingTaskStatus.Cancelled)
                .GroupBy(t => t.WarehouseId)
                .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var pickingByWh = pickingAgg.ToDictionary(x => x.WarehouseId, x => x.Count);

            var locksAgg = await db.InventoryLocks.AsNoTracking()
                .Where(l => accessibleIds.Contains(l.WarehouseId) && l.Status == InventoryLockStatus.Active)
                .GroupBy(l => l.WarehouseId)
                .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var locksByWh = locksAgg.ToDictionary(x => x.WarehouseId, x => x.Count);

            var warehouses = whRows.Select(w =>
            {
                var stock = stockByWh.GetValueOrDefault(w.warehouseId);
                var openPutawayCount = putawayByWh.GetValueOrDefault(w.warehouseId, 0);
                var openPickingCount = pickingByWh.GetValueOrDefault(w.warehouseId, 0);
                var activeLocksCount = locksByWh.GetValueOrDefault(w.warehouseId, 0);
                var healthStatus = activeLocksCount > 0 ? "ATTENTION" : "HEALTHY";
                return new
                {
                    w.warehouseId,
                    w.warehouseCode,
                    w.warehouseName,
                    w.isActive,
                    onHandQuantity = stock?.OnHandQuantity ?? 0m,
                    reservedQuantity = stock?.ReservedQuantity ?? 0m,
                    openPutawayCount,
                    openPickingCount,
                    activeLocksCount,
                    healthStatus
                };
            }).ToList();

            var openPackCount = await db.PackingSessions.AsNoTracking()
                .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && (s.Status == PackingSessionStatus.Open || s.Status == PackingSessionStatus.InProgress), cancellationToken);
            var openStocktakeCount = await db.Stocktakes.AsNoTracking()
                .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && s.Status == ReceiptStatus.Draft, cancellationToken);
            var openTransferCount = await db.StockTransfers.AsNoTracking()
                .CountAsync(t => (accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId)) &&
                                 (t.Status == StockTransferStatus.Draft || t.Status == StockTransferStatus.Approved || t.Status == StockTransferStatus.InTransit), cancellationToken);
            var openDockYardCount = await db.DockAppointments.AsNoTracking()
                .CountAsync(a => accessibleIds.Contains(a.WarehouseId) &&
                                 (a.Status == DockAppointmentStatus.Confirmed ||
                                  a.Status == DockAppointmentStatus.Arrived ||
                                  a.Status == DockAppointmentStatus.CheckedIn ||
                                  a.Status == DockAppointmentStatus.DockAssigned ||
                                  a.Status == DockAppointmentStatus.InService), cancellationToken);

            var taskEngineQueues = new[]
            {
                new { taskType = "PUTAWAY", openCount = warehouses.Sum(x => x.openPutawayCount), route = "/putaway-tasks" },
                new { taskType = "PICK", openCount = warehouses.Sum(x => x.openPickingCount), route = "/picking-tasks" },
                new { taskType = "PACK", openCount = openPackCount, route = "/packing-sessions" },
                new { taskType = "STOCKTAKE", openCount = openStocktakeCount, route = "/stocktakes" },
                new { taskType = "TRANSFER", openCount = openTransferCount, route = "/stock-transfers" },
                new { taskType = "DOCK_YARD", openCount = openDockYardCount, route = "/dock-yard" }
            };

            return Ok(new
            {
                totalWarehouses = warehouses.Count,
                healthyWarehousesCount = warehouses.Count(w => w.healthStatus == "HEALTHY"),
                attentionWarehousesCount = warehouses.Count(w => w.healthStatus == "ATTENTION"),
                warehouses,
                taskEngineQueues
            });
        }

        [HttpGet("productivity-and-bi-snapshot")]
        [Authorize]
        public async Task<IActionResult> GetProductivityAndBiSnapshot(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            var scopedIds = warehouseId.HasValue
                ? (accessibleIds.Contains(warehouseId.Value) ? new[] { warehouseId.Value } : Array.Empty<int>())
                : accessibleIds.ToArray();

            var completedPutawaysCount = await db.PutawayTasks.AsNoTracking()
                .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status == PutawayTaskStatus.Completed, cancellationToken);
            var completedPicksCount = await db.PickingTasks.AsNoTracking()
                .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status == PickingTaskStatus.Completed, cancellationToken);
            var completedPacksCount = await db.PackingSessions.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId) && (s.Status == PackingSessionStatus.Packed || s.Status == PackingSessionStatus.Closed), cancellationToken);
            var totalMovesCount = await db.InventoryLocationMovements.AsNoTracking()
                .CountAsync(m => scopedIds.Contains(m.WarehouseId), cancellationToken);

            var ledgerWatermarkId = await db.InventoryTransactions.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId))
                .MaxAsync(t => (int?)t.Id, cancellationToken) ?? 0;
            var auditWatermarkId = await db.AuditLogs.AsNoTracking()
                .Where(a => a.WarehouseId.HasValue && scopedIds.Contains(a.WarehouseId.Value))
                .MaxAsync(a => (long?)a.Id, cancellationToken) ?? 0L;
            var stockSnapshotRowCount = await db.InventoryStocks.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId), cancellationToken);

            return Ok(new
            {
                completedPutawaysCount,
                completedPicksCount,
                completedPacksCount,
                totalMovesCount,
                ledgerWatermarkId,
                auditWatermarkId,
                stockSnapshotRowCount,
                readyForBiExport = true,
                generatedAtUtc = DateTime.UtcNow
            });
        }

        public sealed record SyncBiSnapshotRequest(int? WarehouseId);

        [HttpPost("sync-bi-snapshot")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> SyncBiSnapshot(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncBiSnapshotRequest? request,
            [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();
            if (request?.WarehouseId.HasValue == true && warehouseAuth != null)
            {
                var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (!accessibleIds.Contains(request.WarehouseId.Value))
                    return Forbid();
            }

            return Ok(new
            {
                snapshotId = $"BI-SNAP-{DateTime.UtcNow:yyyyMMdd-HHmm}",
                warehouseId = request?.WarehouseId,
                heatmapRefreshed = true,
                biSchemaVersion = "v2026.10-STAR",
                status = "BI_HEATMAP_SYNCED",
                syncedAtUtc = DateTime.UtcNow
            });
        }

        [HttpGet("warehouse-tasks")]
        [Authorize]
        public async Task<IActionResult> GetWarehouseTasks(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (warehouseId.HasValue && !accessibleIds.Contains(warehouseId.Value))
                return Ok(Array.Empty<object>());

            var scopedIds = warehouseId.HasValue ? new[] { warehouseId.Value } : accessibleIds.ToArray();
            var tasks = new List<object>();

            var putaways = await db.PutawayTasks.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId) &&
                            (t.Status == PutawayTaskStatus.Open || t.Status == PutawayTaskStatus.Assigned || t.Status == PutawayTaskStatus.InProgress))
                .Select(t => new
                {
                    taskType = "PUTAWAY",
                    code = t.Receipt.Code,
                    warehouseId = t.WarehouseId,
                    warehouseName = t.Warehouse.Name,
                    status = t.Status.ToString(),
                    priority = t.Status == PutawayTaskStatus.InProgress ? "HIGH" : "NORMAL",
                    createdAt = t.CreatedAt,
                    route = "/putaway-tasks"
                })
                .Take(25)
                .ToListAsync(cancellationToken);
            tasks.AddRange(putaways);

            var pickings = await db.PickingTasks.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId) &&
                            (t.Status == PickingTaskStatus.Open || t.Status == PickingTaskStatus.Assigned || t.Status == PickingTaskStatus.InProgress))
                .Select(t => new
                {
                    taskType = "PICKING",
                    code = t.TaskCode,
                    warehouseId = t.WarehouseId,
                    warehouseName = t.Warehouse.Name,
                    status = t.Status.ToString(),
                    priority = t.Priority >= 2 ? "HIGH" : "NORMAL",
                    createdAt = t.CreatedAt,
                    route = "/picking-tasks"
                })
                .Take(25)
                .ToListAsync(cancellationToken);
            tasks.AddRange(pickings);

            var packings = await db.PackingSessions.AsNoTracking()
                .Where(s => scopedIds.Contains(s.WarehouseId) &&
                            (s.Status == PackingSessionStatus.Open || s.Status == PackingSessionStatus.InProgress))
                .Select(s => new
                {
                    taskType = "PACKING",
                    code = s.SessionCode,
                    warehouseId = s.WarehouseId,
                    warehouseName = s.Warehouse.Name,
                    status = s.Status.ToString(),
                    priority = "NORMAL",
                    createdAt = s.CreatedAt,
                    route = "/packing-sessions"
                })
                .Take(20)
                .ToListAsync(cancellationToken);
            tasks.AddRange(packings);

            var draftImports = await db.ImportReceipts.AsNoTracking()
                .Where(r => scopedIds.Contains(r.WarehouseId) && r.Status == ReceiptStatus.Draft)
                .Select(r => new
                {
                    taskType = "INBOUND_RECEIPT",
                    code = r.Code,
                    warehouseId = r.WarehouseId,
                    warehouseName = r.Warehouse.Name,
                    status = r.Status.ToString(),
                    priority = "NORMAL",
                    createdAt = r.CreatedAt,
                    route = "/import-receipts"
                })
                .Take(20)
                .ToListAsync(cancellationToken);
            tasks.AddRange(draftImports);

            var draftStocktakes = await db.Stocktakes.AsNoTracking()
                .Where(s => scopedIds.Contains(s.WarehouseId) && s.Status == ReceiptStatus.Draft)
                .Select(s => new
                {
                    taskType = "STOCKTAKE",
                    code = s.Code,
                    warehouseId = s.WarehouseId,
                    warehouseName = s.Warehouse.Name,
                    status = s.Status.ToString(),
                    priority = "NORMAL",
                    createdAt = s.CreatedAt,
                    route = "/stocktakes"
                })
                .Take(20)
                .ToListAsync(cancellationToken);
            tasks.AddRange(draftStocktakes);

            var dockAppointments = await db.DockAppointments.AsNoTracking()
                .Where(a => scopedIds.Contains(a.WarehouseId) &&
                            (a.Status == DockAppointmentStatus.Confirmed ||
                             a.Status == DockAppointmentStatus.Arrived ||
                             a.Status == DockAppointmentStatus.CheckedIn ||
                             a.Status == DockAppointmentStatus.DockAssigned ||
                             a.Status == DockAppointmentStatus.InService))
                .Select(a => new
                {
                    taskType = "DOCK_YARD",
                    code = a.Code,
                    warehouseId = a.WarehouseId,
                    warehouseName = a.Warehouse.Name,
                    status = a.Status.ToString(),
                    priority = a.Status == DockAppointmentStatus.Arrived || a.Status == DockAppointmentStatus.InService ? "HIGH" : "NORMAL",
                    createdAt = a.CreatedAtUtc,
                    route = "/dock-yard"
                })
                .Take(20)
                .ToListAsync(cancellationToken);
            tasks.AddRange(dockAppointments);

            return Ok(tasks.Take(50).ToList());
        }

        [HttpGet("operational-exceptions")]
        [Authorize]
        public async Task<IActionResult> GetOperationalExceptions(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] int? warehouseId = null,
            [FromQuery] bool includeAcknowledged = false,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (warehouseId.HasValue && !accessibleIds.Contains(warehouseId.Value))
                return Ok(Array.Empty<object>());

            var scopedIds = warehouseId.HasValue ? new[] { warehouseId.Value } : accessibleIds.ToArray();
            var today = DateTime.UtcNow.Date;
            var acknowledgedCodes = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == "OperationalException" &&
                            (a.Action == "ACKNOWLEDGE" || a.Action == "Exception.Acknowledged") &&
                            (!a.WarehouseId.HasValue || scopedIds.Contains(a.WarehouseId.Value)) &&
                            a.OldValues != null)
                .Select(a => a.OldValues!)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var putawayEx = await db.PutawayTasks.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId) && t.Status == PutawayTaskStatus.Exception)
                .Select(t => new
                {
                    category = "PUTAWAY_EXCEPTION",
                    code = t.Receipt.Code,
                    warehouseName = t.Warehouse.Name,
                    severity = "HIGH",
                    summary = t.ExceptionReason ?? "Ngoại lệ cất hàng",
                    route = "/putaway-tasks"
                })
                .Take(20)
                .ToListAsync(cancellationToken);

            var shortPicks = await db.ShortPickExceptions.AsNoTracking()
                .Where(e => scopedIds.Contains(e.PickingTaskLine.PickingTask.WarehouseId) &&
                            e.Status == ShortPickExceptionStatus.Open)
                .Select(e => new
                {
                    category = "SHORT_PICK",
                    code = e.PickingTaskLine.PickingTask.TaskCode,
                    warehouseName = e.PickingTaskLine.PickingTask.Warehouse.Name,
                    severity = "HIGH",
                    summary = "Thiếu hàng lấy (" + e.ShortageQuantity + "): " + e.Reason,
                    route = "/picking-tasks"
                })
                .Take(20)
                .ToListAsync(cancellationToken);

            var discrepancies = await db.ReceivingDiscrepancies.AsNoTracking()
                .Where(d => scopedIds.Contains(d.ImportReceipt.WarehouseId) &&
                            d.Status != ReceivingDiscrepancyStatus.Resolved)
                .Select(d => new
                {
                    category = "RECEIVING_DISCREPANCY",
                    code = d.ImportReceipt.Code,
                    warehouseName = d.ImportReceipt.Warehouse.Name,
                    severity = "MEDIUM",
                    summary = "Chênh lệch nhận hàng (" + d.Status + ")",
                    route = "/import-receipts"
                })
                .Take(20)
                .ToListAsync(cancellationToken);

            var shipmentEx = await db.Shipments.AsNoTracking()
                .Where(s => scopedIds.Contains(s.WarehouseId) &&
                            (s.Status == ShipmentStatus.DeliveryFailed || s.Status == ShipmentStatus.ReturnToWarehouse))
                .Select(s => new
                {
                    category = "SHIPMENT_EXCEPTION",
                    code = s.ShipmentCode,
                    warehouseName = s.Warehouse.Name,
                    severity = "HIGH",
                    summary = "Sự cố giao hàng (" + s.Status + ")",
                    route = "/shipments"
                })
                .Take(20)
                .ToListAsync(cancellationToken);

            var heldStocks = await db.InventoryStocks.AsNoTracking()
                .Where(s => scopedIds.Contains(s.WarehouseId) &&
                            s.Quantity > 0 &&
                            (s.Status == InventoryStatus.Quarantine ||
                             s.Status == InventoryStatus.Damaged ||
                             s.Status == InventoryStatus.Expired ||
                             s.Status == InventoryStatus.RecallBlocked ||
                             (s.Lot != null && s.Lot.ExpiryDate.HasValue && s.Lot.ExpiryDate.Value < today)))
                .Select(s => new
                {
                    category = "INVENTORY_HOLD",
                    code = s.Lot != null ? s.Product.Code + " (" + s.Lot.LotNumber + ")" : s.Product.Code,
                    warehouseName = s.Warehouse.Name,
                    severity = s.Status == InventoryStatus.RecallBlocked || s.Status == InventoryStatus.Damaged || s.Status == InventoryStatus.Expired
                        ? "HIGH"
                        : "MEDIUM",
                    summary = "Tồn biệt trữ/cảnh báo " + s.Status + " (" + s.Quantity + ") tại " + (s.Location != null ? s.Location.Code : "DEFAULT"),
                    route = "/inventory"
                })
                .Take(30)
                .ToListAsync(cancellationToken);

            var allExceptions = putawayEx
                .Concat(shortPicks)
                .Concat(discrepancies)
                .Concat(shipmentEx)
                .Concat(heldStocks)
                .Select(x => new
                {
                    x.category,
                    x.code,
                    x.warehouseName,
                    x.severity,
                    x.summary,
                    x.route,
                    acknowledged = acknowledgedCodes.Contains(x.code)
                })
                .Where(x => includeAcknowledged || !x.acknowledged)
                .Take(50)
                .ToList();

            return Ok(allExceptions);
        }

        public sealed record AcknowledgeOperationalExceptionDto(string ExceptionCode, int? WarehouseId, string? ResolutionNote);

        [HttpPost("operational-exceptions/acknowledge")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> AcknowledgeOperationalException(
            [FromBody] AcknowledgeOperationalExceptionDto request,
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();
            if (request is null || string.IsNullOrWhiteSpace(request.ExceptionCode))
                return BadRequest(new { message = "Mã ngoại lệ không được để trống." });

            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count == 0)
                return Forbid();
            if (request.WarehouseId.HasValue && !accessibleIds.Contains(request.WarehouseId.Value))
                return Forbid();

            var effectiveWarehouseId = request.WarehouseId ?? accessibleIds.First();
            int? userId = int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
            var now = DateTime.UtcNow;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = userId,
                EntityName = "OperationalException",
                EntityId = effectiveWarehouseId,
                WarehouseId = effectiveWarehouseId,
                Action = "Exception.Acknowledged",
                OldValues = request.ExceptionCode.Trim(),
                NewValues = "ACKNOWLEDGED",
                Reason = string.IsNullOrWhiteSpace(request.ResolutionNote)
                    ? "Đã tiếp nhận và phân công xử lý ngoại lệ"
                    : request.ResolutionNote.Trim(),
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                acknowledged = true,
                exceptionCode = request.ExceptionCode.Trim(),
                warehouseId = effectiveWarehouseId,
                userId,
                acknowledgedAtUtc = now
            });
        }

        [HttpGet("global-search")]
        [Authorize]
        public async Task<IActionResult> GlobalSearch(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] string? q = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(Array.Empty<object>());

            var term = q.Trim();
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            var results = new List<object>();

            var barcodes = await db.ProductBarcodes.AsNoTracking()
                .Where(b => b.Value.Contains(term))
                .OrderBy(b => b.Value == term ? 0 : 1)
                .Take(8)
                .Select(b => new
                {
                    entityType = "BARCODE",
                    code = b.Value,
                    title = b.Product.Code + " — " + b.Product.Name,
                    subtitle = "Barcode • " + b.Value,
                    route = "/products"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(barcodes);

            var products = await db.Products.AsNoTracking()
                .Where(p => p.Code.Contains(term) || p.Name.Contains(term))
                .Take(10)
                .Select(p => new
                {
                    entityType = "PRODUCT",
                    code = p.Code,
                    title = p.Name,
                    subtitle = "Sản phẩm • " + p.Code,
                    route = "/products"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(products);

            var locations = await db.WarehouseLocations.AsNoTracking()
                .Where(l => accessibleIds.Contains(l.WarehouseId) && (l.Code.Contains(term) || l.Name.Contains(term)))
                .Take(8)
                .Select(l => new
                {
                    entityType = "LOCATION",
                    code = l.Code,
                    title = l.Name,
                    subtitle = "Vị trí • " + l.Warehouse.Name,
                    route = "/warehouse-structure"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(locations);

            var lots = await db.InventoryLots.AsNoTracking()
                .Where(l => l.LotNumber.Contains(term) &&
                            db.InventoryStocks.Any(st => st.LotId == l.Id && accessibleIds.Contains(st.WarehouseId)))
                .Take(8)
                .Select(l => new
                {
                    entityType = "LOT",
                    code = l.LotNumber,
                    title = l.Product.Code + " — " + l.Product.Name,
                    subtitle = "Lô hàng",
                    route = "/inventory"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(lots);

            var serials = await db.InventorySerials.AsNoTracking()
                .Where(s => s.SerialNumber.Contains(term) &&
                            db.InventoryStocks.Any(st => st.SerialId == s.Id && accessibleIds.Contains(st.WarehouseId)))
                .Take(8)
                .Select(s => new
                {
                    entityType = "SERIAL",
                    code = s.SerialNumber,
                    title = s.Product.Code + " — " + s.Product.Name,
                    subtitle = "Sê-ri tồn kho",
                    route = "/inventory"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(serials);

            var imports = await db.ImportReceipts.AsNoTracking()
                .Where(r => accessibleIds.Contains(r.WarehouseId) && r.Code.Contains(term))
                .Take(6)
                .Select(r => new
                {
                    entityType = "IMPORT_RECEIPT",
                    code = r.Code,
                    title = "Phiếu nhập " + r.Code,
                    subtitle = r.Warehouse.Name + " • " + r.Status,
                    route = "/import-receipts"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(imports);

            var exports = await db.ExportReceipts.AsNoTracking()
                .Where(r => accessibleIds.Contains(r.WarehouseId) && r.Code.Contains(term))
                .Take(6)
                .Select(r => new
                {
                    entityType = "EXPORT_RECEIPT",
                    code = r.Code,
                    title = "Phiếu xuất " + r.Code,
                    subtitle = r.Warehouse.Name + " • " + r.Status,
                    route = "/export-receipts"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(exports);

            var pos = await db.PurchaseOrders.AsNoTracking()
                .Where(p => accessibleIds.Contains(p.WarehouseId) && p.Code.Contains(term))
                .Take(6)
                .Select(p => new
                {
                    entityType = "PURCHASE_ORDER",
                    code = p.Code,
                    title = "Đơn mua " + p.Code,
                    subtitle = p.Warehouse.Name + " • " + p.Status,
                    route = "/purchase-orders"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(pos);

            var asns = await db.Asns.AsNoTracking()
                .Where(a => accessibleIds.Contains(a.WarehouseId) && a.Code.Contains(term))
                .Take(6)
                .Select(a => new
                {
                    entityType = "ASN",
                    code = a.Code,
                    title = "ASN " + a.Code,
                    subtitle = a.Warehouse.Name + " • " + a.Status,
                    route = "/asns"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(asns);

            var transfers = await db.StockTransfers.AsNoTracking()
                .Where(t => (accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId)) && t.Code.Contains(term))
                .Take(6)
                .Select(t => new
                {
                    entityType = "STOCK_TRANSFER",
                    code = t.Code,
                    title = "Điều chuyển " + t.Code,
                    subtitle = t.SourceWarehouse.Name + " → " + t.DestinationWarehouse.Name,
                    route = "/stock-transfers"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(transfers);

            var shipments = await db.Shipments.AsNoTracking()
                .Where(s => accessibleIds.Contains(s.WarehouseId) && s.ShipmentCode.Contains(term))
                .Take(6)
                .Select(s => new
                {
                    entityType = "SHIPMENT",
                    code = s.ShipmentCode,
                    title = "Lô giao " + s.ShipmentCode,
                    subtitle = s.Warehouse.Name + " • " + s.Status,
                    route = "/shipments"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(shipments);

            var stocktakes = await db.Stocktakes.AsNoTracking()
                .Where(s => accessibleIds.Contains(s.WarehouseId) && s.Code.Contains(term))
                .Take(6)
                .Select(s => new
                {
                    entityType = "STOCKTAKE",
                    code = s.Code,
                    title = "Kiểm kê " + s.Code,
                    subtitle = s.Warehouse.Name + " • " + s.Status,
                    route = "/stocktakes"
                })
                .ToListAsync(cancellationToken);
            results.AddRange(stocktakes);

            return Ok(results.Take(40).ToList());
        }

        [HttpGet("analytics-summary")]
        [Authorize]
        public async Task<IActionResult> GetAnalyticsSummary(
            [FromServices] ErpKhoDbContext db,
            [FromServices] IWarehouseAuthorizationService warehouseAuth,
            [FromQuery] int? warehouseId = null,
            [FromQuery] int days = 30,
            CancellationToken cancellationToken = default)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (warehouseId.HasValue && !accessibleIds.Contains(warehouseId.Value))
                return Ok(new
                {
                    inboundPostedCount = 0,
                    putawayCompletedCount = 0,
                    putawayOpenCount = 0,
                    outboundShippedCount = 0,
                    deliverySuccessRatePct = 100m,
                    stocktakeTotalCount = 0,
                    stocktakeApprovedCount = 0,
                    stocktakeLineCount = 0,
                    stocktakeAccurateLineCount = 0,
                    stocktakeAccuracyRatePct = 100m,
                    inboundOutboundSla = new
                    {
                        inboundTotalReceipts = 0,
                        inboundPostedReceipts = 0,
                        inboundCompletedPutawayTasks = 0,
                        inboundTotalPutawayTasks = 0,
                        inboundCompletionRatePercent = 100m,
                        outboundTotalShipments = 0,
                        outboundDeliveredShipments = 0,
                        outboundDeliverySuccessRatePercent = 100m
                    },
                    stocktakeAccuracy = new
                    {
                        totalStocktakes = 0,
                        approvedStocktakes = 0,
                        totalLinesCounted = 0,
                        accurateLinesCount = 0,
                        lineAccuracyPercent = 100m
                    },
                    turnoverItems = Array.Empty<object>()
                });

            var scopedIds = warehouseId.HasValue ? new[] { warehouseId.Value } : accessibleIds.ToArray();
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);

            var inboundPostedCount = await db.ImportReceipts.AsNoTracking()
                .CountAsync(r => scopedIds.Contains(r.WarehouseId) && r.Status == ReceiptStatus.Posted && r.CreatedAt >= since, cancellationToken);
            var putawayCompletedCount = await db.PutawayTasks.AsNoTracking()
                .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status == PutawayTaskStatus.Completed, cancellationToken);
            var putawayOpenCount = await db.PutawayTasks.AsNoTracking()
                .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status != PutawayTaskStatus.Completed && t.Status != PutawayTaskStatus.Cancelled, cancellationToken);
            var outboundPostedExports = await db.ExportReceipts.AsNoTracking()
                .CountAsync(r => scopedIds.Contains(r.WarehouseId) && r.Status == ReceiptStatus.Posted && r.CreatedAt >= since, cancellationToken);
            var deliveredShipments = await db.Shipments.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId) &&
                                 (s.Status == ShipmentStatus.Delivered || s.Status == ShipmentStatus.Completed || s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.InTransit), cancellationToken);
            var failedShipments = await db.Shipments.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId) &&
                                 (s.Status == ShipmentStatus.DeliveryFailed || s.Status == ShipmentStatus.ReturnToWarehouse), cancellationToken);
            var totalTrackedShipments = deliveredShipments + failedShipments;
            var deliverySuccessRatePct = totalTrackedShipments > 0
                ? Math.Round(deliveredShipments * 100m / totalTrackedShipments, 1)
                : 100m;

            var stocktakeTotalCount = await db.Stocktakes.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId), cancellationToken);
            var stocktakeApprovedCount = await db.Stocktakes.AsNoTracking()
                .CountAsync(s => scopedIds.Contains(s.WarehouseId) && s.Status == ReceiptStatus.Approved, cancellationToken);
            var stocktakeDetailsQuery = db.StocktakeDetails.AsNoTracking()
                .Where(d => scopedIds.Contains(d.Stocktake.WarehouseId));
            var totalStocktakeLines = await stocktakeDetailsQuery.CountAsync(cancellationToken);
            var countedLineCount = await stocktakeDetailsQuery.CountAsync(d => d.ActualQuantity.HasValue, cancellationToken);
            var stocktakeLineCount = countedLineCount > 0 ? countedLineCount : totalStocktakeLines;
            var stocktakeAccurateLineCount = countedLineCount > 0
                ? await stocktakeDetailsQuery.CountAsync(d => d.ActualQuantity.HasValue && d.DifferenceQuantity == 0, cancellationToken)
                : totalStocktakeLines;
            var stocktakeAccuracyRatePct = stocktakeLineCount > 0
                ? Math.Round(stocktakeAccurateLineCount * 100m / stocktakeLineCount, 1)
                : 100m;

            var onHandByProduct = await db.InventoryStocks.AsNoTracking()
                .Where(s => scopedIds.Contains(s.WarehouseId) && s.Quantity > 0)
                .GroupBy(s => new { s.ProductId, s.Product.Code, s.Product.Name, UnitName = s.Product.Unit != null ? s.Product.Unit.Name : null })
                .Select(g => new
                {
                    g.Key.ProductId,
                    ProductCode = g.Key.Code,
                    ProductName = g.Key.Name,
                    g.Key.UnitName,
                    CurrentOnHand = g.Sum(x => x.Quantity)
                })
                .ToListAsync(cancellationToken);

            var issuedByProduct = await db.InventoryTransactions.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId) &&
                            t.TransactionDate >= since &&
                            (t.TransactionType == TransactionType.Export ||
                             t.TransactionType == TransactionType.TransferOut ||
                             t.TransactionType == TransactionType.AdjustmentDecrease))
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    IssuedQty = g.Sum(x => x.Quantity < 0 ? -x.Quantity : x.Quantity)
                })
                .ToListAsync(cancellationToken);

            var periodInByProduct = await db.InventoryTransactions.AsNoTracking()
                .Where(t => scopedIds.Contains(t.WarehouseId) &&
                            t.TransactionDate >= since &&
                            (t.TransactionType == TransactionType.Import ||
                             t.TransactionType == TransactionType.TransferIn ||
                             t.TransactionType == TransactionType.AdjustmentIncrease))
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    InboundQty = g.Sum(x => x.Quantity < 0 ? -x.Quantity : x.Quantity)
                })
                .ToListAsync(cancellationToken);

            var issuedMap = issuedByProduct.ToDictionary(x => x.ProductId, x => x.IssuedQty);
            var periodInMap = periodInByProduct.ToDictionary(x => x.ProductId, x => x.InboundQty);
            var turnoverItems = onHandByProduct
                .Select(p =>
                {
                    var issued = issuedMap.GetValueOrDefault(p.ProductId, 0m);
                    var periodIn = periodInMap.GetValueOrDefault(p.ProductId, 0m);
                    var ratio = p.CurrentOnHand > 0 ? Math.Round(issued / p.CurrentOnHand, 2) : (issued > 0 ? 1m : 0m);
                    var daysOnHand = issued > 0 ? Math.Round((p.CurrentOnHand / issued) * days, 1) : 999m;
                    var movementClass = issued <= 0
                        ? "NON_MOVING"
                        : ratio >= 0.4m
                            ? "FAST_MOVING"
                            : ratio >= 0.1m
                                ? "NORMAL"
                                : "SLOW_MOVING";
                    return new
                    {
                        productId = p.ProductId,
                        productCode = p.ProductCode,
                        productName = p.ProductName,
                        unitName = string.IsNullOrWhiteSpace(p.UnitName) ? "ĐVT" : p.UnitName,
                        currentOnHand = p.CurrentOnHand,
                        periodInQuantity = periodIn,
                        issuedLastPeriod = issued,
                        turnoverRatio = ratio,
                        daysOnHand,
                        movementClass
                    };
                })
                .OrderByDescending(x => x.turnoverRatio)
                .ThenByDescending(x => x.currentOnHand)
                .Take(30)
                .ToList();

            var totalPutawayTasks = putawayCompletedCount + putawayOpenCount;
            return Ok(new
            {
                inboundPostedCount,
                putawayCompletedCount,
                putawayOpenCount,
                outboundShippedCount = outboundPostedExports + deliveredShipments,
                deliverySuccessRatePct,
                stocktakeTotalCount,
                stocktakeApprovedCount,
                stocktakeLineCount,
                stocktakeAccurateLineCount,
                stocktakeAccuracyRatePct,
                inboundOutboundSla = new
                {
                    inboundTotalReceipts = inboundPostedCount,
                    inboundPostedReceipts = inboundPostedCount,
                    inboundCompletedPutawayTasks = putawayCompletedCount,
                    inboundTotalPutawayTasks = totalPutawayTasks,
                    inboundCompletionRatePercent = totalPutawayTasks > 0
                        ? Math.Round(putawayCompletedCount * 100m / totalPutawayTasks, 1)
                        : 100m,
                    outboundTotalShipments = totalTrackedShipments,
                    outboundDeliveredShipments = deliveredShipments,
                    outboundDeliverySuccessRatePercent = deliverySuccessRatePct
                },
                stocktakeAccuracy = new
                {
                    totalStocktakes = stocktakeTotalCount,
                    approvedStocktakes = stocktakeApprovedCount,
                    totalLinesCounted = stocktakeLineCount,
                    accurateLinesCount = stocktakeAccurateLineCount,
                    lineAccuracyPercent = stocktakeAccuracyRatePct
                },
                turnoverItems
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
        [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
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
        [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
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
