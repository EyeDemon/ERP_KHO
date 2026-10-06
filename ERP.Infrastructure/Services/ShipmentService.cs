using System.Data;
using System.Text.Json;
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
    IStockReservationService? stockReservationService = null) : IShipmentService, IShipmentDispatchReadiness
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
            if (stockReservationService is null)
                throw new InvalidOperationException("Stock reservation service is required for Shipment dispatch.");

            if (shipment.HandlingUnits.Count == 0 || shipment.HandlingUnits.Any(x => !x.LoadedAt.HasValue))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Shipment chưa load đủ Handling Unit.");

            var activeHus = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, activeHus);
            if (activeHus.Any(x => x.Status != HandlingUnitStatus.Loaded))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Handling Unit hierarchy chưa ở trạng thái LOADED.");

            if (shipment.PackingSession.Status is not PackingSessionStatus.Packed and not PackingSessionStatus.Closed)
                throw Conflict("SHIPMENT_PACKING_NOT_READY", "Packing session không còn ở trạng thái sẵn sàng dispatch.");

            var pickingTaskId = shipment.PackingSession.PickingTaskId;
            var task = await context.PickingTasks.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == pickingTaskId, token)
                ?? throw Conflict("SHIPMENT_PICKING_INVALID", "Không tìm thấy Picking task nguồn của Shipment.");
            if (task.Status != PickingTaskStatus.Completed)
                throw Conflict("SHIPMENT_PICKING_INVALID", "Picking task chưa hoàn tất.");

            var lines = await context.PickingTaskLines.AsNoTracking()
                .Include(x => x.ShortPicks)
                .Include(x => x.Allocation)
                .Where(x => x.PickingTaskId == pickingTaskId)
                .OrderBy(x => x.Sequence)
                .ToListAsync(token);
            if (lines.Count == 0)
                throw Conflict("SHIPMENT_PICKING_INVALID", "Shipment không có dòng Picking để dispatch.");

            if (lines.SelectMany(x => x.ShortPicks).Any(x =>
                    x.Status == ShortPickExceptionStatus.Resolved &&
                    x.ResolutionType is ShortPickResolutionType.Backorder
                        or ShortPickResolutionType.CancelRemainder
                        or ShortPickResolutionType.SupervisorOverride))
                throw Conflict("SHIPMENT_BACKORDER_NOT_SUPPORTED", "Shipment còn Short Pick cần Backorder/Cancel/Override; OUT-09 chưa hoàn tất nên chưa thể dispatch.");

            if (lines.Any(x =>
                    x.PickedQuantity <= 0 ||
                    x.Allocation.Status != StockAllocationStatus.Picked ||
                    x.Allocation.Quantity != x.PickedQuantity))
                throw Conflict("SHIPMENT_ALLOCATION_INVALID", "Allocation/Picked quantity không còn khớp để dispatch.");

            var contents = shipment.PackingSession.HandlingUnits
                .Where(x => x.Status != HandlingUnitStatus.Cancelled)
                .SelectMany(x => x.Contents)
                .ToList();
            foreach (var line in lines)
            {
                var packed = contents.Where(x => x.PickingTaskLineId == line.Id).Sum(x => x.Quantity);
                if (packed != line.PickedQuantity)
                    throw Conflict("SHIPMENT_PACKING_MISMATCH", "Packing content không còn khớp số lượng đã Picking.");
            }

            if (await context.InventoryTransactions.AsNoTracking().AnyAsync(
                    x => x.ReferenceType == "Shipment" &&
                         x.ReferenceId == shipment.Id &&
                         x.TransactionType == TransactionType.Ship,
                    token))
                throw Conflict("SHIPMENT_LEDGER_EXISTS", "Shipment đã có SHIP ledger và cần đối soát trước khi retry.");

            ExportReceipt? sourceReceipt = null;
            if (string.Equals(shipment.SourceType, "ExportReceipt", StringComparison.OrdinalIgnoreCase))
            {
                if (!shipment.SourceId.HasValue)
                    throw Conflict("SHIPMENT_SOURCE_INVALID", "Shipment ExportReceipt thiếu SourceId.");
                sourceReceipt = await context.ExportReceipts.SingleOrDefaultAsync(
                    x => x.Id == shipment.SourceId.Value,
                    token)
                    ?? throw Conflict("SHIPMENT_SOURCE_INVALID", "Không tìm thấy phiếu xuất nguồn của Shipment.");
                if (sourceReceipt.Status != ReceiptStatus.Approved)
                    throw Conflict("SHIPMENT_SOURCE_STATE_INVALID", "Phiếu xuất nguồn không còn ở trạng thái APPROVED.");
            }

            var reservationIds = lines.Select(x => x.Allocation.ReservationId).Distinct().Order().ToArray();
            var ledgerRows = new List<InventoryTransaction>();
            foreach (var reservationId in reservationIds)
            {
                var reservation = await context.StockReservations.SingleOrDefaultAsync(x => x.Id == reservationId, token)
                    ?? throw Conflict("SHIPMENT_RESERVATION_INVALID", "Không tìm thấy reservation của Shipment.");
                if (!string.Equals(reservation.SourceType, shipment.SourceType, StringComparison.OrdinalIgnoreCase) ||
                    reservation.SourceId != shipment.SourceId)
                    throw Conflict("SHIPMENT_RESERVATION_INVALID", "Reservation không thuộc source của Shipment.");

                var taskAllocationIds = lines
                    .Where(x => x.Allocation.ReservationId == reservationId)
                    .Select(x => x.AllocationId)
                    .Order()
                    .ToArray();
                var activeAllocationIds = await context.StockAllocations.AsNoTracking()
                    .Where(x => x.ReservationId == reservationId &&
                                (x.Status == StockAllocationStatus.Active ||
                                 x.Status == StockAllocationStatus.Picking ||
                                 x.Status == StockAllocationStatus.Picked))
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .ToArrayAsync(token);
                if (!taskAllocationIds.SequenceEqual(activeAllocationIds))
                    throw Conflict("SHIPMENT_ALLOCATION_INVALID", "Reservation còn Allocation ngoài Shipment hoặc thiếu Allocation đã Picking.");

                var consumptions = await stockReservationService.ConsumeAsync(
                    reservation,
                    currentUser.UserId,
                    token);
                foreach (var group in consumptions.GroupBy(x => x.LocationId))
                {
                    ledgerRows.Add(new InventoryTransaction
                    {
                        ProductId = reservation.ProductId,
                        WarehouseId = shipment.WarehouseId,
                        LocationId = group.Key,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Ship,
                        Quantity = group.Sum(x => x.Quantity),
                        ReferenceId = shipment.Id,
                        ReferenceType = "Shipment",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = currentUser.UserId,
                        Note = $"Shipment {shipment.ShipmentCode} dispatched"
                    });
                }
            }

            if (ledgerRows.Count == 0)
                throw Conflict("SHIPMENT_LEDGER_INVALID", "Không tạo được SHIP ledger từ inventory consumption.");
            context.InventoryTransactions.AddRange(ledgerRows);

            var now = DateTime.UtcNow;
            foreach (var hu in activeHus) hu.Status = HandlingUnitStatus.Shipped;
            shipment.Status = ShipmentStatus.Dispatched;
            shipment.DispatchedAt = now;
            shipment.DispatchedBy = currentUser.UserId;

            if (sourceReceipt is not null)
            {
                sourceReceipt.Status = ReceiptStatus.Dispatched;
                sourceReceipt.DispatchedAt = now;
                sourceReceipt.DispatchedBy = currentUser.UserId;
            }

            context.OutboxMessages.Add(new OutboxMessage
            {
                EventKey = $"ShipmentDispatched:{shipment.Id}",
                EventType = "ShipmentDispatched",
                AggregateType = "Shipment",
                AggregateId = shipment.Id,
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(new
                {
                    shipmentId = shipment.Id,
                    shipmentCode = shipment.ShipmentCode,
                    warehouseId = shipment.WarehouseId,
                    dispatchedAt = now,
                    dispatchedBy = currentUser.UserId,
                    quantity = ledgerRows.Sum(x => x.Quantity)
                })
            });
            AddAudit(
                "Shipment.Dispatched",
                shipment,
                $"LedgerRows: {ledgerRows.Count}; Quantity: {ledgerRows.Sum(x => x.Quantity)}; HandlingUnits: {activeHus.Count}");
        }, cancellationToken);

    public async Task EnsureSourceReadyAsync(
        string sourceType,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await Query().AsNoTracking().SingleOrDefaultAsync(
            x => x.SourceType == sourceType && x.SourceId == sourceId,
            cancellationToken);
        if (shipment is null) return;

        throw new ConcurrencyException(
            "Chứng từ đã đi vào canonical Shipment workflow. Hãy xác nhận xuất kho tại Shipment thay vì ExportReceipt.");
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
            .Include(x => x.DispatchedByUser)
            .Include(x => x.StagingLocation)
            .Include(x => x.DockAppointment)
            .Include(x => x.Dock)
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
        DispatchedByName = shipment.DispatchedByUser?.FullName ?? shipment.DispatchedByUser?.Username
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
            RowVersion = Convert.ToBase64String(shipment.RowVersion),
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
