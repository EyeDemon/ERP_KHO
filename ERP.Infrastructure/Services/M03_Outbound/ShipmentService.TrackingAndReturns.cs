using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentService
{
    public async Task<ShipmentTrackingDto> GetTrackingAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadAsync(id, false, cancellationToken);
        return MapTracking(shipment);
    }

    public Task<ShipmentDto> MarkInTransitAsync(
        int id,
        MarkShipmentInTransitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Dispatched, "Chỉ Shipment DISPATCHED mới có thể chuyển IN_TRANSIT.");
            var occurredAt = ResolveTrackingTime(shipment, request.OccurredAt, "IN_TRANSIT");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.InTransit;
            shipment.InTransitAt = occurredAt;
            AddTrackingEvent(shipment, "ShipmentInTransit", from, shipment.Status, occurredAt, null, NormalizeNote(request.Note));
            AddAudit("Shipment.InTransit", shipment, $"OccurredAt: {occurredAt:O}; Note: {NormalizeNote(request.Note)}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> ConfirmDeliveryAsync(
        int id,
        ConfirmShipmentDeliveryDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.InTransit, "Chỉ Shipment IN_TRANSIT mới có thể xác nhận giao hàng.");
            if (shipment.ProofOfDelivery is not null)
                throw Conflict("SHIPMENT_POD_EXISTS", "Shipment đã có Proof of Delivery.");

            var receiver = NormalizeRequired(request.ReceiverName, 200, "Tên người nhận");
            var evidenceReference = NormalizeOptional(request.EvidenceReference, 500);
            var carrierReference = NormalizeOptional(request.CarrierReference, 120);
            if (evidenceReference is null && carrierReference is null)
                throw new BusinessRuleException("POD cần EvidenceReference hoặc CarrierReference.");
            ValidateCoordinates(request.Latitude, request.Longitude);
            var deliveredAt = ResolveTrackingTime(shipment, request.DeliveredAt, "DELIVERED");
            var pod = new ShipmentProofOfDelivery
            {
                ShipmentId = shipment.Id,
                DeliveredAt = deliveredAt,
                ReceiverName = receiver,
                EvidenceReference = evidenceReference,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                CarrierReference = carrierReference,
                DeliveryNote = NormalizeOptional(request.DeliveryNote, 500),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser.UserId
            };
            shipment.ProofOfDelivery = pod;
            context.ShipmentProofOfDeliveries.Add(pod);

            var from = shipment.Status;
            shipment.Status = ShipmentStatus.Delivered;
            AddTrackingEvent(
                shipment,
                "DeliveryConfirmed",
                from,
                shipment.Status,
                deliveredAt,
                null,
                NormalizeOptional(request.DeliveryNote, 500));
            AddAudit(
                "Shipment.DeliveryConfirmed",
                shipment,
                $"DeliveredAt: {deliveredAt:O}; Receiver: {receiver}; Evidence: {pod.EvidenceReference}; CarrierReference: {pod.CarrierReference}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> FailDeliveryAsync(
        int id,
        FailShipmentDeliveryDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.InTransit, "Chỉ Shipment IN_TRANSIT mới có thể ghi nhận giao hàng thất bại.");
            var reason = NormalizeRequired(request.ReasonCode, 80, "Mã lý do giao thất bại").ToUpperInvariant();
            var occurredAt = ResolveTrackingTime(shipment, request.OccurredAt, "DELIVERY_FAILED");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.DeliveryFailed;
            shipment.DeliveryFailedAt = occurredAt;
            AddTrackingEvent(shipment, "DeliveryFailed", from, shipment.Status, occurredAt, reason, NormalizeNote(request.Note));
            AddAudit("Shipment.DeliveryFailed", shipment, $"OccurredAt: {occurredAt:O}; Reason: {reason}; Note: {NormalizeNote(request.Note)}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> RetryDeliveryAsync(
        int id,
        RetryShipmentDeliveryDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.DeliveryFailed, "Chỉ Shipment DELIVERY_FAILED mới có thể retry delivery.");
            var occurredAt = ResolveTrackingTime(shipment, request.OccurredAt, "RETRY_DELIVERY");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.InTransit;
            shipment.InTransitAt = occurredAt;
            AddTrackingEvent(shipment, "DeliveryRetryStarted", from, shipment.Status, occurredAt, null, NormalizeNote(request.Note));
            AddAudit("Shipment.DeliveryRetryStarted", shipment, $"OccurredAt: {occurredAt:O}; Note: {NormalizeNote(request.Note)}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> InitiateReturnAsync(
        int id,
        InitiateShipmentReturnDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request?.RowVersion ?? string.Empty, async (shipment, token) =>
        {
            if (shipment.Status is not ShipmentStatus.Dispatched and not ShipmentStatus.InTransit and not ShipmentStatus.DeliveryFailed)
                throw Conflict("SHIPMENT_STATE_INVALID", "Chỉ Shipment DISPATCHED, IN_TRANSIT hoặc DELIVERY_FAILED mới có thể khởi tạo return-to-warehouse.");
            var rawReason = string.IsNullOrWhiteSpace(request?.ReasonCode) ? "DELIVERY_FAILED_RTS" : request.ReasonCode;
            var reason = NormalizeRequired(rawReason, 80, "Mã lý do return").ToUpperInvariant();
            var occurredAt = ResolveTrackingTime(shipment, request?.OccurredAt, "RETURN_TO_WAREHOUSE");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.ReturnToWarehouse;
            shipment.ReturnInitiatedAt = occurredAt;
            AddTrackingEvent(shipment, "ReturnToWarehouseInitiated", from, shipment.Status, occurredAt, reason, NormalizeNote(request?.Note));
            AddAudit("Shipment.ReturnInitiated", shipment, $"OccurredAt: {occurredAt:O}; Reason: {reason}; Note: {NormalizeNote(request?.Note)}");
            await EnsureCustomerReturnForShipmentAsync(shipment, reason, token);
        }, cancellationToken);

    private async Task EnsureCustomerReturnForShipmentAsync(
        Shipment shipment,
        string reason,
        CancellationToken token)
    {
        var trackingNumber = !string.IsNullOrWhiteSpace(shipment.SealNumber)
            ? shipment.SealNumber.Trim()
            : shipment.ShipmentCode.Trim();

        int? exportReceiptId = string.Equals(shipment.SourceType, "ExportReceipt", StringComparison.OrdinalIgnoreCase)
            ? shipment.SourceId
            : null;
        int? salesOrderId = string.Equals(shipment.SourceType, "SalesOrder", StringComparison.OrdinalIgnoreCase)
            ? shipment.SourceId
            : null;

        var linkedExport = await context.ExportReceipts
            .AsNoTracking()
            .Include(x => x.Details)
            .FirstOrDefaultAsync(
                x => x.ShipmentId == shipment.Id ||
                     (exportReceiptId.HasValue && x.Id == exportReceiptId.Value),
                token);

        if (linkedExport is not null)
        {
            exportReceiptId ??= linkedExport.Id;
            salesOrderId ??= linkedExport.SalesOrderId;
        }

        var existingReturn = await context.CustomerReturns
            .FirstOrDefaultAsync(
                r => r.ShipmentId == shipment.Id ||
                     (!string.IsNullOrWhiteSpace(trackingNumber) && r.TrackingNumber == trackingNumber) ||
                     (exportReceiptId.HasValue && r.ExportReceiptId == exportReceiptId.Value),
                token);

        if (existingReturn is not null)
        {
            existingReturn.ShipmentId ??= shipment.Id;
            existingReturn.ExportReceiptId ??= exportReceiptId;
            existingReturn.SalesOrderId ??= salesOrderId;
            if (string.IsNullOrWhiteSpace(existingReturn.TrackingNumber))
                existingReturn.TrackingNumber = trackingNumber;
            return;
        }

        var now = DateTime.UtcNow;
        var customerReturn = new CustomerReturn
        {
            ReturnCode = $"RMA-RTS-{shipment.Id}-{now:yyyyMMddHHmmss}",
            WarehouseId = shipment.WarehouseId,
            ShipmentId = shipment.Id,
            ExportReceiptId = exportReceiptId,
            SalesOrderId = salesOrderId,
            TrackingNumber = trackingNumber,
            OrderNumber = shipment.SourceCode?.Trim() ?? linkedExport?.Code?.Trim(),
            ReturnReason = reason,
            Status = "QUARANTINED",
            CreatedBy = currentUser.UserId > 0 ? currentUser.UserId : 1,
            CreatedAtUtc = now
        };

        if (linkedExport is not null && linkedExport.Details.Count > 0)
        {
            foreach (var detail in linkedExport.Details.OrderBy(d => d.Id))
            {
                customerReturn.Lines.Add(new CustomerReturnLine
                {
                    ProductId = detail.ProductId,
                    BaseUomId = detail.BaseUomIdSnapshot,
                    BaseUomCode = string.IsNullOrWhiteSpace(detail.BaseUomCodeSnapshot) ? "PCS" : detail.BaseUomCodeSnapshot.Trim(),
                    TransactionUomCode = string.IsNullOrWhiteSpace(detail.BaseUomCodeSnapshot) ? "PCS" : detail.BaseUomCodeSnapshot.Trim(),
                    ConversionRateSnapshot = 1m,
                    Quantity = detail.Quantity,
                    UnitCost = detail.UnitPrice,
                    Condition = "GOOD",
                    Disposition = "PENDING"
                });
            }
        }
        else if (shipment.PackingSession?.HandlingUnits is { Count: > 0 })
        {
            var grouped = shipment.PackingSession.HandlingUnits
                .Where(hu => hu.Status != HandlingUnitStatus.Cancelled)
                .SelectMany(hu => hu.Contents)
                .GroupBy(c => c.ProductId)
                .ToList();

            foreach (var g in grouped)
            {
                customerReturn.Lines.Add(new CustomerReturnLine
                {
                    ProductId = g.Key,
                    BaseUomCode = "PCS",
                    TransactionUomCode = "PCS",
                    ConversionRateSnapshot = 1m,
                    Quantity = g.Sum(x => x.Quantity),
                    UnitCost = 0m,
                    Condition = "GOOD",
                    Disposition = "PENDING"
                });
            }
        }

        context.CustomerReturns.Add(customerReturn);
    }

    public Task<ShipmentDto> CompleteAsync(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Delivered, "Chỉ Shipment DELIVERED mới có thể hoàn tất.");
            if (shipment.ProofOfDelivery is null)
                throw Conflict("SHIPMENT_POD_REQUIRED", "Shipment DELIVERED phải có Proof of Delivery trước khi hoàn tất.");
            var occurredAt = ResolveTrackingTime(shipment, null, "COMPLETED");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.Completed;
            shipment.CompletedAt = occurredAt;
            AddTrackingEvent(shipment, "ShipmentCompleted", from, shipment.Status, occurredAt, null, null);
            AddAudit("Shipment.Completed", shipment, $"CompletedAt: {occurredAt:O}");
            await Task.CompletedTask;
        }, cancellationToken);

    public async Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var shipmentExists = await context.Shipments.AsNoTracking().AnyAsync(
            x => x.SourceType == sourceType && x.SourceId == sourceId && x.Status != ShipmentStatus.Cancelled,
            cancellationToken);
        if (!shipmentExists && string.Equals(sourceType, "ExportReceipt", StringComparison.OrdinalIgnoreCase))
        {
            var receiptInfo = await context.ExportReceipts.AsNoTracking()
                .Where(x => x.Id == sourceId)
                .Select(x => new { x.ShipmentId, x.SalesOrderId })
                .FirstOrDefaultAsync(cancellationToken);
            if (receiptInfo is not null)
            {
                if (receiptInfo.ShipmentId.HasValue)
                {
                    shipmentExists = true;
                }
                else if (receiptInfo.SalesOrderId.HasValue)
                {
                    shipmentExists = await context.Shipments.AsNoTracking().AnyAsync(
                        x => x.SourceType == "SalesOrder" &&
                             x.SourceId == receiptInfo.SalesOrderId.Value &&
                             x.Status != ShipmentStatus.Cancelled,
                        cancellationToken);
                }
            }
        }
        if (!shipmentExists) return;

        throw new ConcurrencyException(
            "Chứng từ đã đi vào canonical Shipment workflow. Hãy dispatch tại Shipment để tránh double inventory movement.");
    }

    private DateTime ResolveTrackingTime(Shipment shipment, DateTime? requested, string transition)
    {
        if (!shipment.DispatchedAt.HasValue &&
            shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.InTransit or ShipmentStatus.DeliveryFailed)
        {
            shipment.DispatchedAt = shipment.CreatedAt != default ? shipment.CreatedAt : DateTime.UtcNow.AddSeconds(-1);
        }
        if (!shipment.DispatchedAt.HasValue)
            throw Conflict("SHIPMENT_NOT_DISPATCHED", "Shipment chưa có inventory dispatch boundary.");
        var occurredAt = requested?.ToUniversalTime() ?? DateTime.UtcNow;
        var now = DateTime.UtcNow;
        if (occurredAt > now.AddMinutes(5))
            throw new BusinessRuleException($"Thời điểm {transition} không được ở tương lai.");
        var latest = shipment.TrackingEvents.Count == 0
            ? shipment.DispatchedAt.Value
            : shipment.TrackingEvents.Max(x => x.OccurredAt);
        if (occurredAt < shipment.DispatchedAt.Value || occurredAt < latest)
            throw Conflict("SHIPMENT_TRACKING_TIME_INVALID", $"Thời điểm {transition} không được trước event tracking gần nhất.");
        return occurredAt;
    }

    private void AddTrackingEvent(
        Shipment shipment,
        string eventType,
        ShipmentStatus fromStatus,
        ShipmentStatus toStatus,
        DateTime occurredAt,
        string? reasonCode,
        string? note)
    {
        shipment.TrackingEvents.Add(new ShipmentTrackingEvent
        {
            EventType = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            OccurredAt = occurredAt,
            RecordedAt = DateTime.UtcNow,
            Source = "Internal",
            ReasonCode = reasonCode,
            Note = note,
            RecordedBy = currentUser.UserId
        });
    }

    private static string NormalizeRequired(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new BusinessRuleException($"{label} là bắt buộc.");
        if (normalized.Length > maxLength)
            throw new BusinessRuleException($"{label} không được vượt quá {maxLength} ký tự.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new BusinessRuleException($"Giá trị không được vượt quá {maxLength} ký tự.");
        return normalized;
    }

    private static string? NormalizeNote(string? value) => NormalizeOptional(value, 500);

    private static void ValidateCoordinates(decimal? latitude, decimal? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
            throw new BusinessRuleException("Latitude và Longitude phải được cung cấp cùng nhau.");
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new BusinessRuleException("Tọa độ POD không hợp lệ.");
    }
}
