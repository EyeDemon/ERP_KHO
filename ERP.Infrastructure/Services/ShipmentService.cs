using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ShipmentService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IStockReservationService stockReservationService) : IShipmentService, IShipmentDispatchReadiness
{
    public async Task<IReadOnlyList<ShipmentListDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = Query().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (Enum.TryParse<ShipmentStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        return rows.Select(MapList).ToList();
    }

    public async Task<ShipmentDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Map(await LoadAsync(id, false, cancellationToken));

    public Task<ShipmentDto> StageAsync(
        int id,
        StageShipmentDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Ready, "Chỉ Shipment READY mới có thể đưa vào staging.");
            if (shipment.PackingSession.Status is not PackingSessionStatus.Packed and not PackingSessionStatus.Closed)
                throw Conflict("SHIPMENT_PACKING_NOT_READY", "Packing session chưa hoàn tất.");

            var locationCode = request.StagingLocationCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(locationCode))
                throw new BusinessRuleException("Staging location là bắt buộc.");
            var stagingLocation = await context.WarehouseLocations.SingleOrDefaultAsync(
                x => x.WarehouseId == shipment.WarehouseId &&
                     x.Code == locationCode &&
                     x.LocationType == WarehouseLocationType.Staging &&
                     x.IsActive &&
                     !x.IsBlocked,
                token);
            if (stagingLocation is null)
                throw Conflict("SHIPMENT_STAGING_LOCATION_INVALID", "Không tìm thấy staging location hợp lệ trong kho.");

            var active = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, active);
            if (active.Any(x => x.Status != HandlingUnitStatus.Closed))
                throw Conflict("SHIPMENT_HU_NOT_CLOSED", "Tất cả Handling Unit phải CLOSED trước khi staging.");

            var now = DateTime.UtcNow;
            foreach (var hu in active) hu.Status = HandlingUnitStatus.Staged;
            foreach (var link in shipment.HandlingUnits) link.StagedAt = now;
            shipment.StagingLocationId = stagingLocation.Id;
            shipment.StagingLocation = stagingLocation;
            shipment.Status = ShipmentStatus.Staging;
            shipment.StagedAt = now;
            AddAudit("Shipment.Staged", shipment, $"HandlingUnits: {shipment.HandlingUnits.Count}; StagingLocationId: {stagingLocation.Id}; Code: {stagingLocation.Code}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> StartLoadingAsync(
        int id,
        StartShipmentLoadingDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Staging, "Shipment phải ở STAGING trước khi bắt đầu loading.");
            var appointment = await context.DockAppointments
                .Include(x => x.Dock)
                .SingleOrDefaultAsync(x => x.Id == request.DockAppointmentId, token)
                ?? throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Không tìm thấy outbound dock appointment.");

            if (appointment.WarehouseId != shipment.WarehouseId ||
                appointment.Direction != DockAppointmentDirection.Outbound ||
                appointment.Status != DockAppointmentStatus.InService ||
                appointment.DockId is null ||
                appointment.Dock is null ||
                !appointment.Dock.IsActive ||
                !appointment.Dock.SupportsOutbound)
                throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Appointment phải là outbound, cùng kho, IN_SERVICE và có dock outbound đang hoạt động.");

            if (string.IsNullOrWhiteSpace(appointment.VehiclePlate))
                throw Conflict("SHIPMENT_VEHICLE_REQUIRED", "Appointment chưa có biển số xe thực tế.");

            var active = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, active);
            if (active.Any(x => x.Status != HandlingUnitStatus.Staged))
                throw Conflict("SHIPMENT_HU_NOT_STAGED", "Tất cả Handling Unit phải ở STAGED trước khi loading.");

            shipment.DockAppointmentId = appointment.Id;
            shipment.DockAppointment = appointment;
            shipment.DockId = appointment.DockId;
            shipment.Dock = appointment.Dock;
            shipment.VehiclePlate = appointment.VehiclePlate;
            shipment.TrailerPlate = appointment.TrailerPlate;
            shipment.SealNumber = NormalizeSeal(appointment.SealNumber);
            shipment.Status = ShipmentStatus.Loading;
            shipment.LoadingStartedAt = DateTime.UtcNow;
            AddAudit(
                "Shipment.LoadingStarted",
                shipment,
                $"DockAppointmentId: {appointment.Id}; DockId: {appointment.DockId}; Vehicle: {appointment.VehiclePlate}");
        }, cancellationToken);

    public Task<ShipmentDto> LoadHandlingUnitAsync(
        int id,
        LoadShipmentHandlingUnitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loading, "Shipment phải ở LOADING trước khi quét HU.");
            var scan = request.HandlingUnitBarcode?.Trim();
            if (string.IsNullOrWhiteSpace(scan))
                throw new BusinessRuleException("Barcode/HU code là bắt buộc.");

            var all = ActivePackingHus(shipment);
            var scanned = all.SingleOrDefault(x =>
                string.Equals(x.Barcode, scan, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.HuCode, scan, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.Sscc) && string.Equals(x.Sscc, scan, StringComparison.OrdinalIgnoreCase)));

            if (scanned is null)
            {
                var existsElsewhere = await context.HandlingUnits.AsNoTracking().AnyAsync(x =>
                    x.WarehouseId == shipment.WarehouseId &&
                    (x.Barcode == scan || x.HuCode == scan || x.Sscc == scan), token);
                throw existsElsewhere
                    ? Conflict("SHIPMENT_HU_MISMATCH", "Handling Unit không thuộc Shipment này.")
                    : Conflict("HU_NOT_FOUND", "Không tìm thấy Handling Unit theo mã đã quét.");
            }

            var link = shipment.HandlingUnits.SingleOrDefault(x => x.HandlingUnitId == scanned.Id);
            if (link is null)
                throw Conflict("SHIPMENT_HU_MISMATCH", "Hãy quét Handling Unit gốc đã được gắn vào Shipment, không quét HU con.");

            if (link.LoadedAt.HasValue || scanned.Status == HandlingUnitStatus.Loaded)
                throw Conflict("HU_ALREADY_LOADED", "Handling Unit đã được load lên xe.");

            var tree = Hierarchy(all, scanned.Id);
            if (tree.Any(x => x.Status != HandlingUnitStatus.Staged))
                throw Conflict("SHIPMENT_HU_NOT_STAGED", "Handling Unit hoặc HU con chưa ở trạng thái STAGED.");

            var now = DateTime.UtcNow;
            foreach (var hu in tree) hu.Status = HandlingUnitStatus.Loaded;
            link.LoadedAt = now;
            link.LoadedBy = currentUser.UserId;
            AddAudit("Shipment.HandlingUnitLoaded", shipment, $"HandlingUnitId: {scanned.Id}; HuCode: {scanned.HuCode}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> CompleteLoadingAsync(
        int id,
        CompleteShipmentLoadingDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loading, "Shipment phải ở LOADING trước khi xác nhận LOADED.");
            if (shipment.DockAppointmentId is null || shipment.DockId is null || string.IsNullOrWhiteSpace(shipment.VehiclePlate))
                throw Conflict("SHIPMENT_LOADING_CONTEXT_REQUIRED", "Shipment thiếu dock/vehicle context.");

            var appointment = await context.DockAppointments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == shipment.DockAppointmentId.Value, token);
            if (appointment is null ||
                appointment.WarehouseId != shipment.WarehouseId ||
                appointment.Direction != DockAppointmentDirection.Outbound ||
                appointment.Status != DockAppointmentStatus.InService)
                throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Outbound appointment không còn IN_SERVICE.");

            if (shipment.HandlingUnits.Count == 0 || shipment.HandlingUnits.Any(x => !x.LoadedAt.HasValue))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Chưa load đủ Handling Unit của Shipment.");

            var active = ActivePackingHus(shipment);
            if (active.Any(x => x.Status != HandlingUnitStatus.Loaded))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Handling Unit hierarchy chưa được load đầy đủ.");

            var seal = NormalizeSeal(request.SealNumber);
            shipment.SealNumber = seal ?? shipment.SealNumber;
            shipment.Status = ShipmentStatus.Loaded;
            shipment.LoadedAt = DateTime.UtcNow;
            AddAudit(
                "Shipment.Loaded",
                shipment,
                $"HandlingUnits: {shipment.HandlingUnits.Count}; DockId: {shipment.DockId}; Vehicle: {shipment.VehiclePlate}; Seal: {shipment.SealNumber}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> DispatchAsync(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loaded, "Chỉ Shipment LOADED mới có thể dispatch.");
            if (shipment.HandlingUnits.Count == 0 || shipment.HandlingUnits.Any(x => !x.LoadedAt.HasValue))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Shipment chưa load đủ Handling Unit.");

            var activeHus = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, activeHus);
            if (activeHus.Any(x => x.Status != HandlingUnitStatus.Loaded))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Handling Unit hierarchy chưa ở trạng thái LOADED.");

            var picking = shipment.PackingSession.PickingTask;
            if (picking.Status != PickingTaskStatus.Completed)
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Picking task chưa hoàn tất.");
            if (picking.Lines.Any(x => x.Status is not PickingTaskLineStatus.Picked and not PickingTaskLineStatus.Resolved))
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Picking line chưa hoàn tất.");
            if (picking.Lines.SelectMany(x => x.ShortPicks).Any(x =>
                    x.Status == ShortPickExceptionStatus.Resolved &&
                    x.ResolutionType is ShortPickResolutionType.Backorder
                        or ShortPickResolutionType.CancelRemainder
                        or ShortPickResolutionType.SupervisorOverride))
                throw Conflict("SHIPMENT_PICKING_NOT_READY", "Shipment còn thiếu hàng sau Picking.");

            var reservationIds = picking.Lines
                .Select(x => x.Allocation.ReservationId)
                .Distinct()
                .Order()
                .ToArray();
            if (reservationIds.Length == 0)
                throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Shipment không có reservation/allocation để dispatch.");

            var reservations = await context.StockReservations
                .Where(x => reservationIds.Contains(x.Id))
                .OrderBy(x => x.Id)
                .ToListAsync(token);
            if (reservations.Count != reservationIds.Length)
                throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Reservation của Shipment không còn đầy đủ.");

            var ledgerBuckets = new Dictionary<(int ProductId, int LocationId, InventoryStatus InventoryStatus), decimal>();
            foreach (var reservation in reservations)
            {
                if (reservation.WarehouseId != shipment.WarehouseId)
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Reservation không cùng kho với Shipment.");

                var lines = picking.Lines
                    .Where(x => x.Allocation.ReservationId == reservation.Id)
                    .OrderBy(x => x.Allocation.LocationId)
                    .ThenBy(x => x.Id)
                    .ToList();
                var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
                if (remaining <= 0 ||
                    lines.Sum(x => x.Allocation.Quantity) != remaining ||
                    reservation.AllocatedQuantity != remaining ||
                    lines.Any(x => x.Allocation.Status != StockAllocationStatus.Picked ||
                                   x.PickedQuantity != x.Allocation.Quantity))
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Allocation/Picked quantity không còn khớp reservation.");

                var expectedByLocation = lines
                    .GroupBy(x => x.Allocation.LocationId)
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Allocation.Quantity));

                var consumptions = await stockReservationService.ConsumeAsync(
                    reservation,
                    currentUser.UserId,
                    token);
                if (consumptions.Sum(x => x.Quantity) != remaining ||
                    consumptions.Count != expectedByLocation.Count ||
                    consumptions.Any(x => !expectedByLocation.TryGetValue(x.LocationId, out var expected) || expected != x.Quantity))
                    throw Conflict("SHIPMENT_ALLOCATION_MISMATCH", "Inventory consumption không khớp Allocation location.");

                foreach (var consumption in consumptions.OrderBy(x => x.LocationId))
                {
                    var statuses = lines
                        .Where(x => x.Allocation.LocationId == consumption.LocationId)
                        .Select(x => x.Allocation.InventoryStatus)
                        .Distinct()
                        .ToArray();
                    if (statuses.Length != 1)
                        throw Conflict("SHIPMENT_INVENTORY_MISMATCH", "Allocation cùng Location có nhiều InventoryStatus, không thể tạo SHIP ledger chuẩn.");

                    var key = (reservation.ProductId, consumption.LocationId, statuses[0]);
                    ledgerBuckets[key] = ledgerBuckets.GetValueOrDefault(key) + consumption.Quantity;
                }
            }

            var dispatchedAt = DateTime.UtcNow;
            foreach (var bucket in ledgerBuckets.OrderBy(x => x.Key.ProductId).ThenBy(x => x.Key.LocationId).ThenBy(x => x.Key.InventoryStatus))
            {
                context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = bucket.Key.ProductId,
                    WarehouseId = shipment.WarehouseId,
                    LocationId = bucket.Key.LocationId,
                    InventoryStatus = bucket.Key.InventoryStatus,
                    TransactionType = TransactionType.Ship,
                    Quantity = bucket.Value,
                    ReferenceId = shipment.Id,
                    ReferenceType = "Shipment",
                    TransactionDate = dispatchedAt,
                    CreatedBy = currentUser.UserId,
                    Note = $"Shipment dispatch {shipment.ShipmentCode}"
                });
            }

            foreach (var hu in activeHus) hu.Status = HandlingUnitStatus.Shipped;
            var previousStatus = shipment.Status;
            shipment.Status = ShipmentStatus.Dispatched;
            shipment.DispatchedAt = dispatchedAt;
            shipment.DispatchedBy = currentUser.UserId;
            AddTrackingEvent(
                shipment,
                "ShipmentDispatched",
                previousStatus,
                ShipmentStatus.Dispatched,
                dispatchedAt,
                null,
                "Inventory SHIP boundary committed.");

            if (shipment.SourceType == "ExportReceipt" && shipment.SourceId.HasValue)
            {
                var receipt = await context.ExportReceipts.SingleOrDefaultAsync(
                    x => x.Id == shipment.SourceId.Value,
                    token);
                if (receipt is null)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Không tìm thấy phiếu xuất nguồn của Shipment.");
                if (receipt.Status != ReceiptStatus.Approved)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Phiếu xuất nguồn không còn ở trạng thái APPROVED.");

                receipt.Status = ReceiptStatus.Dispatched;
                receipt.DispatchedAt = dispatchedAt;
                receipt.DispatchedBy = currentUser.UserId;
            }

            AddAudit(
                "Shipment.Dispatched",
                shipment,
                $"Reservations: {reservations.Count}; LedgerRows: {ledgerBuckets.Count}; Quantity: {ledgerBuckets.Values.Sum()}");
        }, cancellationToken);

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
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            if (shipment.Status is not ShipmentStatus.InTransit and not ShipmentStatus.DeliveryFailed)
                throw Conflict("SHIPMENT_STATE_INVALID", "Chỉ Shipment IN_TRANSIT hoặc DELIVERY_FAILED mới có thể khởi tạo return-to-warehouse.");
            var reason = NormalizeRequired(request.ReasonCode, 80, "Mã lý do return").ToUpperInvariant();
            var occurredAt = ResolveTrackingTime(shipment, request.OccurredAt, "RETURN_TO_WAREHOUSE");
            var from = shipment.Status;
            shipment.Status = ShipmentStatus.ReturnToWarehouse;
            shipment.ReturnInitiatedAt = occurredAt;
            AddTrackingEvent(shipment, "ReturnToWarehouseInitiated", from, shipment.Status, occurredAt, reason, NormalizeNote(request.Note));
            AddAudit("Shipment.ReturnInitiated", shipment, $"OccurredAt: {occurredAt:O}; Reason: {reason}; Note: {NormalizeNote(request.Note)}");
            await Task.CompletedTask;
        }, cancellationToken);

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
            x => x.SourceType == sourceType && x.SourceId == sourceId,
            cancellationToken);
        if (!shipmentExists) return;

        throw new ConcurrencyException(
            "Chứng từ đã đi vào canonical Shipment workflow. Hãy dispatch tại Shipment để tránh double inventory movement.");
    }

    private async Task<ShipmentDto> MutateAsync(
        int id,
        string encodedRowVersion,
        Func<Shipment, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var shipment = await LoadAsync(id, true, cancellationToken);
            ApplyVersion(shipment, encodedRowVersion);
            await mutation(shipment, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(shipment);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Dữ liệu Shipment đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql &&
                                           sql.Message.Contains("IX_InventoryTransactions_ShipmentReference", StringComparison.OrdinalIgnoreCase))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Shipment đã được dispatch bởi yêu cầu đồng thời khác.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Shipment bị deadlock.", ex);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new DeadlockException("Giao dịch Shipment bị deadlock.", ex);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Shipment> LoadAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<Shipment> query = Query();
        if (!tracking) query = query.AsNoTracking();
        var shipment = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Shipment hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(shipment.WarehouseId, token);
        return shipment;
    }

    private IQueryable<Shipment> Query() =>
        context.Shipments
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.PackingSession).ThenInclude(x => x.HandlingUnits).ThenInclude(x => x.Contents)
            .Include(x => x.PackingSession).ThenInclude(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.Allocation)
            .Include(x => x.PackingSession).ThenInclude(x => x.PickingTask).ThenInclude(x => x.Lines).ThenInclude(x => x.ShortPicks)
            .Include(x => x.StagingLocation)
            .Include(x => x.DockAppointment)
            .Include(x => x.Dock)
            .Include(x => x.DispatchedByUser)
            .Include(x => x.ProofOfDelivery)
            .Include(x => x.TrackingEvents)
            .Include(x => x.HandlingUnits).ThenInclude(x => x.HandlingUnit);

    private static List<HandlingUnit> ActivePackingHus(Shipment shipment) =>
        shipment.PackingSession.HandlingUnits
            .Where(x => x.Status != HandlingUnitStatus.Cancelled)
            .OrderBy(x => x.Id)
            .ToList();

    private static void ValidateAssignedRoots(Shipment shipment, List<HandlingUnit> active)
    {
        var roots = active.Where(x => x.ParentHandlingUnitId is null).Select(x => x.Id).Order().ToArray();
        var assigned = shipment.HandlingUnits.Select(x => x.HandlingUnitId).Order().ToArray();
        if (roots.Length == 0 || !roots.SequenceEqual(assigned))
            throw Conflict("SHIPMENT_HU_MISMATCH", "Danh sách Handling Unit gốc không còn khớp Packing session.");
    }

    private static List<HandlingUnit> Hierarchy(List<HandlingUnit> all, int rootId)
    {
        var result = new List<HandlingUnit>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var current = all.SingleOrDefault(x => x.Id == id)
                ?? throw Conflict("SHIPMENT_HU_MISMATCH", "Handling Unit hierarchy không hợp lệ.");
            result.Add(current);
            foreach (var child in all.Where(x => x.ParentHandlingUnitId == id))
                queue.Enqueue(child.Id);
        }
        return result;
    }

    private void ApplyVersion(Shipment shipment, string encoded)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản dữ liệu Shipment không hợp lệ."); }
        if (!shipment.RowVersion.SequenceEqual(expected))
            throw Conflict("SHIPMENT_VERSION_CONFLICT", "Dữ liệu Shipment đã thay đổi. Vui lòng tải lại.");
        context.Entry(shipment).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static void RequireStatus(Shipment shipment, ShipmentStatus expected, string message)
    {
        if (shipment.Status != expected)
            throw Conflict("SHIPMENT_STATE_INVALID", message);
    }

    private void AddAudit(string action, Shipment shipment, string? values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "Shipment",
            EntityId = shipment.Id,
            WarehouseId = shipment.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private DateTime ResolveTrackingTime(Shipment shipment, DateTime? requested, string transition)
    {
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

    private static ShipmentTrackingEventDto MapTrackingEvent(ShipmentTrackingEvent item) => new()
    {
        Id = item.Id,
        EventType = item.EventType,
        FromStatus = item.FromStatus.ToString(),
        ToStatus = item.ToStatus.ToString(),
        OccurredAt = item.OccurredAt,
        RecordedAt = item.RecordedAt,
        Source = item.Source,
        SourceEventId = item.SourceEventId,
        ReasonCode = item.ReasonCode,
        Note = item.Note
    };

    private static ShipmentProofOfDeliveryDto? MapProofOfDelivery(ShipmentProofOfDelivery? pod) =>
        pod is null ? null : new ShipmentProofOfDeliveryDto
        {
            DeliveredAt = pod.DeliveredAt,
            ReceiverName = pod.ReceiverName,
            EvidenceReference = pod.EvidenceReference,
            Latitude = pod.Latitude,
            Longitude = pod.Longitude,
            CarrierReference = pod.CarrierReference,
            DeliveryNote = pod.DeliveryNote,
            CreatedAt = pod.CreatedAt
        };

    private static ShipmentTrackingDto MapTracking(Shipment shipment) => new()
    {
        ShipmentId = shipment.Id,
        ShipmentCode = shipment.ShipmentCode,
        Status = shipment.Status.ToString(),
        DispatchedAt = shipment.DispatchedAt,
        InTransitAt = shipment.InTransitAt,
        DeliveryFailedAt = shipment.DeliveryFailedAt,
        ReturnInitiatedAt = shipment.ReturnInitiatedAt,
        DeliveredAt = shipment.ProofOfDelivery?.DeliveredAt,
        CompletedAt = shipment.CompletedAt,
        ProofOfDelivery = MapProofOfDelivery(shipment.ProofOfDelivery),
        Events = shipment.TrackingEvents.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(MapTrackingEvent).ToList()
    };

    private static string? NormalizeSeal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 80)
            throw new BusinessRuleException("Seal number không được vượt quá 80 ký tự.");
        return normalized;
    }

    private static ShipmentListDto MapList(Shipment shipment) => new()
    {
        Id = shipment.Id,
        ShipmentCode = shipment.ShipmentCode,
        PackingSessionId = shipment.PackingSessionId,
        PackingSessionCode = shipment.PackingSession.SessionCode,
        WarehouseId = shipment.WarehouseId,
        WarehouseName = shipment.Warehouse.Name,
        SourceType = shipment.SourceType,
        SourceId = shipment.SourceId,
        SourceCode = shipment.SourceCode,
        Status = shipment.Status.ToString(),
        StagingLocationId = shipment.StagingLocationId,
        StagingLocationCode = shipment.StagingLocation?.Code,
        HandlingUnitCount = shipment.HandlingUnits.Count,
        LoadedHandlingUnitCount = shipment.HandlingUnits.Count(x => x.LoadedAt.HasValue),
        DockAppointmentId = shipment.DockAppointmentId,
        DockAppointmentCode = shipment.DockAppointment?.Code,
        DockId = shipment.DockId,
        DockCode = shipment.Dock?.Code,
        VehiclePlate = shipment.VehiclePlate,
        TrailerPlate = shipment.TrailerPlate,
        SealNumber = shipment.SealNumber,
        CreatedAt = shipment.CreatedAt,
        StagedAt = shipment.StagedAt,
        LoadingStartedAt = shipment.LoadingStartedAt,
        LoadedAt = shipment.LoadedAt,
        DispatchedAt = shipment.DispatchedAt,
        DispatchedBy = shipment.DispatchedBy,
        DispatchedByName = shipment.DispatchedByUser?.FullName,
        InTransitAt = shipment.InTransitAt,
        DeliveryFailedAt = shipment.DeliveryFailedAt,
        ReturnInitiatedAt = shipment.ReturnInitiatedAt,
        DeliveredAt = shipment.ProofOfDelivery?.DeliveredAt,
        CompletedAt = shipment.CompletedAt
    };

    private static ShipmentDto Map(Shipment shipment)
    {
        var summary = MapList(shipment);
        var all = shipment.PackingSession.HandlingUnits.ToList();
        return new ShipmentDto
        {
            Id = summary.Id,
            ShipmentCode = summary.ShipmentCode,
            PackingSessionId = summary.PackingSessionId,
            PackingSessionCode = summary.PackingSessionCode,
            WarehouseId = summary.WarehouseId,
            WarehouseName = summary.WarehouseName,
            SourceType = summary.SourceType,
            SourceId = summary.SourceId,
            SourceCode = summary.SourceCode,
            Status = summary.Status,
            StagingLocationId = summary.StagingLocationId,
            StagingLocationCode = summary.StagingLocationCode,
            HandlingUnitCount = summary.HandlingUnitCount,
            LoadedHandlingUnitCount = summary.LoadedHandlingUnitCount,
            DockAppointmentId = summary.DockAppointmentId,
            DockAppointmentCode = summary.DockAppointmentCode,
            DockId = summary.DockId,
            DockCode = summary.DockCode,
            VehiclePlate = summary.VehiclePlate,
            TrailerPlate = summary.TrailerPlate,
            SealNumber = summary.SealNumber,
            CreatedAt = summary.CreatedAt,
            StagedAt = summary.StagedAt,
            LoadingStartedAt = summary.LoadingStartedAt,
            LoadedAt = summary.LoadedAt,
            DispatchedAt = summary.DispatchedAt,
            DispatchedBy = summary.DispatchedBy,
            DispatchedByName = summary.DispatchedByName,
            InTransitAt = summary.InTransitAt,
            DeliveryFailedAt = summary.DeliveryFailedAt,
            ReturnInitiatedAt = summary.ReturnInitiatedAt,
            DeliveredAt = summary.DeliveredAt,
            CompletedAt = summary.CompletedAt,
            RowVersion = Convert.ToBase64String(shipment.RowVersion),
            ProofOfDelivery = MapProofOfDelivery(shipment.ProofOfDelivery),
            TrackingEvents = shipment.TrackingEvents.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(MapTrackingEvent).ToList(),
            HandlingUnits = shipment.HandlingUnits.OrderBy(x => x.Sequence).Select(link =>
            {
                var tree = Hierarchy(all, link.HandlingUnitId);
                return new ShipmentHandlingUnitDto
                {
                    Id = link.Id,
                    HandlingUnitId = link.HandlingUnitId,
                    HuCode = link.HandlingUnit.HuCode,
                    Barcode = link.HandlingUnit.Barcode,
                    Sscc = link.HandlingUnit.Sscc,
                    Type = link.HandlingUnit.Type.ToString(),
                    Status = link.HandlingUnit.Status.ToString(),
                    Sequence = link.Sequence,
                    ContentQuantity = tree.SelectMany(x => x.Contents).Sum(x => x.Quantity),
                    AssignedAt = link.AssignedAt,
                    StagedAt = link.StagedAt,
                    LoadedAt = link.LoadedAt
                };
            }).ToList()
        };
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
