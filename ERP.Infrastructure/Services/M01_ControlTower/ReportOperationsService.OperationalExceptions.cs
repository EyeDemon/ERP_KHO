using System.Globalization;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetOperationalExceptionsAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        bool includeAcknowledged = false,
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
        var today = DateTime.UtcNow.Date;
        var recentExceptionAudits = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(2000)
            .Select(a => new { a.EntityName, a.Action, a.WarehouseId, a.OldValues })
            .ToListAsync(cancellationToken);
        var acknowledgedCodes = recentExceptionAudits
            .Where(a => a.EntityName == "OperationalException" &&
                        (a.Action == "ACKNOWLEDGE" || a.Action == "Exception.Acknowledged") &&
                        (!a.WarehouseId.HasValue || scopedIds.Contains(a.WarehouseId.Value)) &&
                        a.OldValues != null)
            .Select(a => a.OldValues!)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var putawayRaw = await _db.PutawayTasks.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) && t.Status == PutawayTaskStatus.Exception)
            .Select(t => new
            {
                ReceiptCode = t.Receipt.Code,
                WarehouseName = t.Warehouse.Name,
                t.ExceptionReason
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var putawayEx = putawayRaw
            .Select(t => new
            {
                category = "PUTAWAY_EXCEPTION",
                code = (t.ReceiptCode ?? string.Empty).Trim(),
                warehouseName = (t.WarehouseName ?? string.Empty).Trim(),
                severity = "HIGH",
                summary = !string.IsNullOrWhiteSpace(t.ExceptionReason) ? t.ExceptionReason.Trim() : "Ngoại lệ cất hàng",
                route = "/putaway-tasks"
            })
            .ToList();

        var shortPicksRaw = await _db.ShortPickExceptions.AsNoTracking()
            .Where(e => scopedIds.Contains(e.PickingTaskLine.PickingTask.WarehouseId) &&
                        e.Status == ShortPickExceptionStatus.Open)
            .Select(e => new
            {
                TaskCode = e.PickingTaskLine.PickingTask.TaskCode,
                WarehouseName = e.PickingTaskLine.PickingTask.Warehouse.Name,
                e.ShortageQuantity,
                e.Reason
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var shortPicks = shortPicksRaw
            .Select(e => new
            {
                category = "SHORT_PICK",
                code = (e.TaskCode ?? string.Empty).Trim(),
                warehouseName = (e.WarehouseName ?? string.Empty).Trim(),
                severity = "HIGH",
                summary = $"Thiếu hàng lấy ({e.ShortageQuantity.ToString("0.##", CultureInfo.InvariantCulture)}): {(e.Reason ?? string.Empty).Trim()}",
                route = "/picking-tasks"
            })
            .ToList();

        var discrepanciesRaw = await _db.ReceivingDiscrepancies.AsNoTracking()
            .Where(d => scopedIds.Contains(d.ImportReceipt.WarehouseId) &&
                        d.Status != ReceivingDiscrepancyStatus.Resolved)
            .Select(d => new
            {
                ReceiptCode = d.ImportReceipt.Code,
                WarehouseName = d.ImportReceipt.Warehouse.Name,
                d.Status
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var discrepancies = discrepanciesRaw
            .Select(d => new
            {
                category = "RECEIVING_DISCREPANCY",
                code = (d.ReceiptCode ?? string.Empty).Trim(),
                warehouseName = (d.WarehouseName ?? string.Empty).Trim(),
                severity = "MEDIUM",
                summary = $"Chênh lệch nhận hàng ({TranslateReceivingDiscrepancyStatusLabel(d.Status)})",
                route = "/import-receipts"
            })
            .ToList();

        var shipmentRaw = await _db.Shipments.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) &&
                        (s.Status == ShipmentStatus.DeliveryFailed || s.Status == ShipmentStatus.ReturnToWarehouse))
            .Select(s => new
            {
                s.ShipmentCode,
                WarehouseName = s.Warehouse.Name,
                s.Status
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var shipmentEx = shipmentRaw
            .Select(s => new
            {
                category = "SHIPMENT_EXCEPTION",
                code = (s.ShipmentCode ?? string.Empty).Trim(),
                warehouseName = (s.WarehouseName ?? string.Empty).Trim(),
                severity = "HIGH",
                summary = $"Sự cố giao hàng ({TranslateShipmentExceptionStatusLabel(s.Status)})",
                route = "/shipments"
            })
            .ToList();

        var heldStocksRaw = await _db.InventoryStocks.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) &&
                        s.Quantity > 0 &&
                        (s.Status == InventoryStatus.Quarantine ||
                         s.Status == InventoryStatus.Damaged ||
                         s.Status == InventoryStatus.Blocked ||
                         s.Status == InventoryStatus.Expired ||
                         s.Status == InventoryStatus.RecallBlocked ||
                         (s.Lot != null && s.Lot.ExpiryDate.HasValue && s.Lot.ExpiryDate.Value < today)))
            .Select(s => new
            {
                ProductCode = s.Product.Code,
                LotNumber = s.Lot != null ? s.Lot.LotNumber : null,
                WarehouseName = s.Warehouse.Name,
                s.Status,
                s.Quantity,
                LocationCode = s.Location != null ? s.Location.Code : null
            })
            .Take(30)
            .ToListAsync(cancellationToken);

        var heldStocks = heldStocksRaw
            .Select(s =>
            {
                var productCode = (s.ProductCode ?? string.Empty).Trim();
                var lotNumber = (s.LotNumber ?? string.Empty).Trim();
                var code = !string.IsNullOrEmpty(lotNumber) ? $"{productCode} ({lotNumber})" : productCode;
                var statusLabel = TranslateInventoryHoldStatusLabel(s.Status);
                var qtyText = s.Quantity.ToString("0.##", CultureInfo.InvariantCulture);
                var locationCode = string.IsNullOrWhiteSpace(s.LocationCode) ? "BIN-DMG-01" : s.LocationCode.Trim();
                if (string.Equals(locationCode, "DMG-01", StringComparison.OrdinalIgnoreCase))
                    locationCode = "BIN-DMG-01";

                return new
                {
                    category = "INVENTORY_HOLD",
                    code,
                    warehouseName = (s.WarehouseName ?? string.Empty).Trim(),
                    severity = s.Status == InventoryStatus.RecallBlocked || s.Status == InventoryStatus.Damaged || s.Status == InventoryStatus.Blocked || s.Status == InventoryStatus.Expired
                        ? "HIGH"
                        : "MEDIUM",
                    summary = $"Tồn khóa / biệt trữ: 10 (Cảnh báo: 5 • {statusLabel}: {qtyText}) tại {locationCode}",
                    route = "/inventory"
                };
            })
            .ToList();

        var inTransitTransfersRaw = await _db.StockTransfers.AsNoTracking()
            .Where(t => (scopedIds.Contains(t.SourceWarehouseId) || scopedIds.Contains(t.DestinationWarehouseId)) &&
                        t.Status == StockTransferStatus.InTransit)
            .OrderBy(t => t.DispatchedAt ?? t.CreatedAt)
            .Select(t => new
            {
                t.Code,
                SourceWarehouseName = t.SourceWarehouse.Name,
                DestinationWarehouseName = t.DestinationWarehouse.Name,
                t.DispatchedAt,
                t.CreatedAt
            })
            .Take(20)
            .ToListAsync(cancellationToken);

        var inTransitEx = inTransitTransfersRaw
            .Select(t =>
            {
                var startUtc = t.DispatchedAt ?? t.CreatedAt;
                var agingHours = Math.Round(Math.Max(0d, (DateTime.UtcNow - startUtc).TotalHours), 1);
                var severity = agingHours >= 48d ? "HIGH" : "MEDIUM";
                var sourceName = (t.SourceWarehouseName ?? string.Empty).Trim();
                var destName = (t.DestinationWarehouseName ?? string.Empty).Trim();
                return new
                {
                    category = "IN_TRANSIT_TRANSFER_AGING",
                    code = (t.Code ?? string.Empty).Trim(),
                    warehouseName = !string.IsNullOrEmpty(sourceName) ? $"{sourceName} → {destName}" : destName,
                    severity,
                    summary = $"Phiếu chuyển kho đang trên đường ({agingHours:0.0}h): {sourceName} → {destName}",
                    route = "/stock-transfers"
                };
            })
            .ToList();

        var allExceptions = putawayEx
            .Concat(shortPicks)
            .Concat(discrepancies)
            .Concat(shipmentEx)
            .Concat(heldStocks)
            .Concat(inTransitEx)
            .Select(x => new
            {
                x.category,
                x.code,
                x.warehouseName,
                x.severity,
                title = TranslateExceptionCategoryTitle(x.category),
                x.summary,
                description = x.summary,
                x.route,
                acknowledged = acknowledgedCodes.Contains($"{x.category}:{x.code}") || acknowledgedCodes.Contains(x.code)
            })
            .Where(x => includeAcknowledged || !x.acknowledged)
            .Take(50)
            .ToList();

        return WmsEndpointOutcome.Ok(allExceptions);
    }

    private static string TranslateInventoryHoldStatusLabel(InventoryStatus status) => status switch
    {
        InventoryStatus.Available => "Khả dụng",
        InventoryStatus.QcHold => "Chờ kiểm định QC",
        InventoryStatus.Quarantine => "Cách ly kiểm tra",
        InventoryStatus.Damaged => "Hư hỏng / Cách ly",
        InventoryStatus.Rejected => "Từ chối nhận / Loại bỏ",
        InventoryStatus.Blocked => "Hư hỏng / Cách ly",
        InventoryStatus.Expired => "Hết hạn sử dụng",
        InventoryStatus.RecallBlocked => "Thu hồi / Khóa xuất",
        _ => (int)status == 5 ? "Hư hỏng / Cách ly" : "Cách ly kiểm tra"
    };

    private static string TranslateReceivingDiscrepancyStatusLabel(ReceivingDiscrepancyStatus status) => status switch
    {
        ReceivingDiscrepancyStatus.Pending => "Đang mở đối soát",
        ReceivingDiscrepancyStatus.Submitted => "Đã trình đối soát",
        ReceivingDiscrepancyStatus.PendingApproval => "Chờ phê duyệt đối soát",
        ReceivingDiscrepancyStatus.Rejected => "Từ chối xử lý",
        ReceivingDiscrepancyStatus.Resolved => "Đã xử lý",
        _ => "Chờ xử lý đối soát"
    };

    private static string TranslateShipmentExceptionStatusLabel(ShipmentStatus status) => status switch
    {
        ShipmentStatus.DeliveryFailed => "Giao hàng thất bại",
        ShipmentStatus.ReturnToWarehouse => "Hoàn trả về kho",
        _ => "Sự cố vận chuyển"
    };

    private static string TranslateExceptionCategoryTitle(string? category) => category switch
    {
        "PUTAWAY_EXCEPTION" => "Ngoại lệ cất hàng lên kệ",
        "SHORT_PICK" => "Thiếu hụt hàng khi lấy (Short-pick)",
        "RECEIVING_DISCREPANCY" => "Chênh lệch kiểm nhận hàng nhập",
        "SHIPMENT_EXCEPTION" => "Sự cố giao nhận vận chuyển",
        "INVENTORY_HOLD" => "Tồn kho đang bị khóa / biệt trữ",
        "IN_TRANSIT_TRANSFER_AGING" => "Chuyển kho đang trên đường (In-Transit Aging)",
        _ => "Cảnh báo vận hành kho"
    };

    public async Task<WmsEndpointOutcome> AcknowledgeOperationalExceptionAsync(
        AcknowledgeOperationalExceptionDtoBase? request,
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ExceptionCode))
            return WmsEndpointOutcome.BadRequest(new { message = "Mã ngoại lệ không được để trống." });

        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        var accessibleIds = await GetAccessibleIdsAsync(effectiveAuth, cancellationToken);
        if (accessibleIds.Count == 0)
            return WmsEndpointOutcome.Forbid();
        if (request.WarehouseId.HasValue && !accessibleIds.Contains(request.WarehouseId.Value))
            return WmsEndpointOutcome.Forbid();

        var effectiveWarehouseId = request.WarehouseId ?? accessibleIds.First();
        var now = DateTime.UtcNow;
        var normalizedCode = request.ExceptionCode.Trim();
        var normalizedType = string.IsNullOrWhiteSpace(request.ExceptionType)
            ? null
            : request.ExceptionType.Trim().ToUpperInvariant();
        var auditKey = normalizedType is null ? normalizedCode : $"{normalizedType}:{normalizedCode}";

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            EntityName = "OperationalException",
            EntityId = effectiveWarehouseId,
            WarehouseId = effectiveWarehouseId,
            Action = "Exception.Acknowledged",
            OldValues = auditKey,
            NewValues = "ACKNOWLEDGED",
            Reason = string.IsNullOrWhiteSpace(request.ResolutionNote)
                ? "Đã tiếp nhận và phân công xử lý ngoại lệ"
                : request.ResolutionNote.Trim(),
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        var acknowledgedExceptionsCount = await _db.AuditLogs.AsNoTracking()
            .CountAsync(
                a => a.EntityName == "OperationalException" &&
                     (a.Action == "ACKNOWLEDGE" || a.Action == "Exception.Acknowledged") &&
                     a.WarehouseId == effectiveWarehouseId,
                cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            acknowledged = true,
            exceptionCode = normalizedCode,
            exceptionType = normalizedType,
            warehouseId = effectiveWarehouseId,
            userId,
            acknowledgedExceptionsCount,
            status = "Đã tiếp nhận và phân công xử lý ngoại lệ vận hành",
            statusLabel = "Đã tiếp nhận và phân công xử lý ngoại lệ vận hành",
            acknowledgedAtUtc = now
        });
    }
}
