using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetWarehouseTasksAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        var accessibleIds = await GetAccessibleIdsAsync(effectiveAuth, cancellationToken);
        if (warehouseId.HasValue)
        {
            if (effectiveAuth != null)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            if (!accessibleIds.Contains(warehouseId.Value))
                return WmsEndpointOutcome.Forbid();
        }

        var scopedIds = warehouseId.HasValue ? new[] { warehouseId.Value } : accessibleIds.ToArray();
        var tasks = new List<object>();

        var putaways = await _db.PutawayTasks.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) &&
                        (t.Status == PutawayTaskStatus.Open || t.Status == PutawayTaskStatus.Assigned || t.Status == PutawayTaskStatus.InProgress))
            .Select(t => new
            {
                taskType = "PUTAWAY",
                code = t.Receipt.Code.Trim(),
                warehouseId = t.WarehouseId,
                warehouseName = t.Warehouse.Name.Trim(),
                status = t.Status.ToString(),
                priority = t.Status == PutawayTaskStatus.InProgress ? "HIGH" : "NORMAL",
                createdAt = t.CreatedAt,
                route = "/putaway-tasks"
            })
            .Take(25)
            .ToListAsync(cancellationToken);
        tasks.AddRange(putaways);

        var pickings = await _db.PickingTasks.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) &&
                        (t.Status == PickingTaskStatus.Open || t.Status == PickingTaskStatus.Assigned || t.Status == PickingTaskStatus.InProgress))
            .Select(t => new
            {
                taskType = "PICKING",
                code = t.TaskCode.Trim(),
                warehouseId = t.WarehouseId,
                warehouseName = t.Warehouse.Name.Trim(),
                status = t.Status.ToString(),
                priority = t.Priority >= 2 ? "HIGH" : "NORMAL",
                createdAt = t.CreatedAt,
                route = "/picking-tasks"
            })
            .Take(25)
            .ToListAsync(cancellationToken);
        tasks.AddRange(pickings);

        var packings = await _db.PackingSessions.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) &&
                        (s.Status == PackingSessionStatus.Open || s.Status == PackingSessionStatus.InProgress))
            .Select(s => new
            {
                taskType = "PACKING",
                code = s.SessionCode.Trim(),
                warehouseId = s.WarehouseId,
                warehouseName = s.Warehouse.Name.Trim(),
                status = s.Status.ToString(),
                priority = "NORMAL",
                createdAt = s.CreatedAt,
                route = "/packing-sessions"
            })
            .Take(20)
            .ToListAsync(cancellationToken);
        tasks.AddRange(packings);

        var draftImports = await _db.ImportReceipts.AsNoTracking()
            .Where(r => scopedIds.Contains(r.WarehouseId) && r.Status == ReceiptStatus.Draft)
            .Select(r => new
            {
                taskType = "INBOUND_RECEIPT",
                code = r.Code.Trim(),
                warehouseId = r.WarehouseId,
                warehouseName = r.Warehouse.Name.Trim(),
                status = r.Status.ToString(),
                priority = "NORMAL",
                createdAt = r.CreatedAt,
                route = "/import-receipts"
            })
            .Take(20)
            .ToListAsync(cancellationToken);
        tasks.AddRange(draftImports);

        var draftStocktakes = await _db.Stocktakes.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) && s.Status == StocktakeStatus.Draft)
            .Select(s => new
            {
                taskType = "STOCKTAKE",
                code = s.Code.Trim(),
                warehouseId = s.WarehouseId,
                warehouseName = s.Warehouse.Name.Trim(),
                status = s.Status.ToString(),
                priority = "NORMAL",
                createdAt = s.CreatedAt,
                route = "/stocktakes"
            })
            .Take(20)
            .ToListAsync(cancellationToken);
        tasks.AddRange(draftStocktakes);

        var dockAppointments = await _db.DockAppointments.AsNoTracking()
            .Where(a => scopedIds.Contains(a.WarehouseId) &&
                        (a.Status == DockAppointmentStatus.Confirmed ||
                         a.Status == DockAppointmentStatus.Arrived ||
                         a.Status == DockAppointmentStatus.CheckedIn ||
                         a.Status == DockAppointmentStatus.DockAssigned ||
                         a.Status == DockAppointmentStatus.InService))
            .Select(a => new
            {
                taskType = "DOCK_YARD",
                code = a.Code.Trim(),
                warehouseId = a.WarehouseId,
                warehouseName = a.Warehouse.Name.Trim(),
                status = a.Status.ToString(),
                priority = a.Status == DockAppointmentStatus.Arrived || a.Status == DockAppointmentStatus.InService ? "HIGH" : "NORMAL",
                createdAt = a.CreatedAtUtc,
                route = "/dock-yard"
            })
            .Take(20)
            .ToListAsync(cancellationToken);
        tasks.AddRange(dockAppointments);

        return WmsEndpointOutcome.Ok(tasks.Take(50).ToList());
    }

    public async Task<WmsEndpointOutcome> GlobalSearchAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        string? q = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return WmsEndpointOutcome.Ok(Array.Empty<object>());

        var term = q.Trim();
        var accessibleIds = await GetAccessibleIdsAsync(warehouseAuth, cancellationToken);
        var results = new List<object>();

        var barcodes = await _db.ProductBarcodes.AsNoTracking()
            .Where(b => b.Value.Contains(term))
            .OrderBy(b => b.Value == term ? 0 : 1)
            .Take(8)
            .Select(b => new
            {
                entityType = "BARCODE",
                code = b.Value.Trim(),
                title = b.Product.Code.Trim() + " — " + b.Product.Name.Trim(),
                subtitle = "Barcode • " + b.Value.Trim(),
                route = "/products"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(barcodes);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.Code.Contains(term) || p.Name.Contains(term))
            .Take(10)
            .Select(p => new
            {
                entityType = "PRODUCT",
                code = p.Code.Trim(),
                title = p.Name.Trim(),
                subtitle = "Sản phẩm • " + p.Code.Trim(),
                route = "/products"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(products);

        var locations = await _db.WarehouseLocations.AsNoTracking()
            .Where(l => accessibleIds.Contains(l.WarehouseId) && (l.Code.Contains(term) || l.Name.Contains(term)))
            .Take(8)
            .Select(l => new
            {
                entityType = "LOCATION",
                code = l.Code.Trim(),
                title = l.Name.Trim(),
                subtitle = "Vị trí • " + l.Warehouse.Name.Trim(),
                route = "/warehouse-structure"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(locations);

        var lots = await _db.InventoryLots.AsNoTracking()
            .Where(l => l.LotNumber.Contains(term) &&
                        _db.InventoryStocks.Any(st => st.LotId == l.Id && accessibleIds.Contains(st.WarehouseId)))
            .Take(8)
            .Select(l => new
            {
                entityType = "LOT",
                code = l.LotNumber.Trim(),
                title = l.Product.Code.Trim() + " — " + l.Product.Name.Trim(),
                subtitle = "Lô hàng",
                route = "/inventory"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(lots);

        var serials = await _db.InventorySerials.AsNoTracking()
            .Where(s => s.SerialNumber.Contains(term) &&
                        _db.InventoryStocks.Any(st => st.SerialId == s.Id && accessibleIds.Contains(st.WarehouseId)))
            .Take(8)
            .Select(s => new
            {
                entityType = "SERIAL",
                code = s.SerialNumber.Trim(),
                title = s.Product.Code.Trim() + " — " + s.Product.Name.Trim(),
                subtitle = "Sê-ri tồn kho",
                route = "/inventory"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(serials);

        var imports = await _db.ImportReceipts.AsNoTracking()
            .Where(r => accessibleIds.Contains(r.WarehouseId) && r.Code.Contains(term))
            .Take(6)
            .Select(r => new
            {
                entityType = "IMPORT_RECEIPT",
                code = r.Code.Trim(),
                title = "Phiếu nhập " + r.Code.Trim(),
                subtitle = r.Warehouse.Name.Trim() + " • " + r.Status,
                route = "/import-receipts"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(imports);

        var exports = await _db.ExportReceipts.AsNoTracking()
            .Where(r => accessibleIds.Contains(r.WarehouseId) && r.Code.Contains(term))
            .Take(6)
            .Select(r => new
            {
                entityType = "EXPORT_RECEIPT",
                code = r.Code.Trim(),
                title = "Phiếu xuất " + r.Code.Trim(),
                subtitle = r.Warehouse.Name.Trim() + " • " + r.Status,
                route = "/export-receipts"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(exports);

        var pos = await _db.PurchaseOrders.AsNoTracking()
            .Where(p => accessibleIds.Contains(p.WarehouseId) && p.Code.Contains(term))
            .Take(6)
            .Select(p => new
            {
                entityType = "PURCHASE_ORDER",
                code = p.Code.Trim(),
                title = "Đơn mua " + p.Code.Trim(),
                subtitle = p.Warehouse.Name.Trim() + " • " + p.Status,
                route = "/purchase-orders"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(pos);

        var asns = await _db.Asns.AsNoTracking()
            .Where(a => accessibleIds.Contains(a.WarehouseId) && a.Code.Contains(term))
            .Take(6)
            .Select(a => new
            {
                entityType = "ASN",
                code = a.Code.Trim(),
                title = "ASN " + a.Code.Trim(),
                subtitle = a.Warehouse.Name.Trim() + " • " + a.Status,
                route = "/asns"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(asns);

        var transfers = await _db.StockTransfers.AsNoTracking()
            .Where(t => (accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId)) && t.Code.Contains(term))
            .Take(6)
            .Select(t => new
            {
                entityType = "STOCK_TRANSFER",
                code = t.Code.Trim(),
                title = "Điều chuyển " + t.Code.Trim(),
                subtitle = t.SourceWarehouse.Name.Trim() + " → " + t.DestinationWarehouse.Name.Trim(),
                route = "/stock-transfers"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(transfers);

        var shipments = await _db.Shipments.AsNoTracking()
            .Where(s => accessibleIds.Contains(s.WarehouseId) && s.ShipmentCode.Contains(term))
            .Take(6)
            .Select(s => new
            {
                entityType = "SHIPMENT",
                code = s.ShipmentCode.Trim(),
                title = "Lô giao " + s.ShipmentCode.Trim(),
                subtitle = s.Warehouse.Name.Trim() + " • " + s.Status,
                route = "/shipments"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(shipments);

        var stocktakes = await _db.Stocktakes.AsNoTracking()
            .Where(s => accessibleIds.Contains(s.WarehouseId) && s.Code.Contains(term))
            .Take(6)
            .Select(s => new
            {
                entityType = "STOCKTAKE",
                code = s.Code.Trim(),
                title = "Kiểm kê " + s.Code.Trim(),
                subtitle = s.Warehouse.Name.Trim() + " • " + s.Status,
                route = "/stocktakes"
            })
            .ToListAsync(cancellationToken);
        results.AddRange(stocktakes);

        return WmsEndpointOutcome.Ok(results.Take(40).ToList());
    }
}
