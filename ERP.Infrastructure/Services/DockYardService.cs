using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ERP.Infrastructure.Services;

public sealed class DockYardService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDockYardService
{
    private static readonly DockAppointmentStatus[] ReleasedStatuses =
    [
        DockAppointmentStatus.Cancelled,
        DockAppointmentStatus.NoShow
    ];

    public async Task<IReadOnlyList<DockYardWarehouseDto>> GetWarehousesAsync(CancellationToken token = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(token);
        return await context.Warehouses.AsNoTracking()
            .Where(x => accessible.Contains(x.Id))
            .OrderBy(x => x.Code)
            .Select(warehouse => new DockYardWarehouseDto
            {
                Id = warehouse.Id,
                Code = warehouse.Code,
                Name = warehouse.Name,
                TimeZoneId = context.WarehouseCalendars
                    .Where(calendar => calendar.WarehouseId == warehouse.Id)
                    .Select(calendar => calendar.TimeZoneId)
                    .FirstOrDefault() ?? "UTC",
                CalendarConfigured = context.WarehouseCalendars.Any(calendar => calendar.WarehouseId == warehouse.Id)
            })
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<DockDto>> GetDocksAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        var rows = await context.Docks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .OrderBy(x => x.Code)
            .ToListAsync(token);
        return rows.Select(MapDock).ToList();
    }

    public async Task<DockDto> CreateDockAsync(int warehouseId, UpsertDockDto dto, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        ValidateDock(dto);
        var code = NormalizeCode(dto.Code, "Mã dock");
        if (await context.Docks.AnyAsync(x => x.WarehouseId == warehouseId && x.Code == code, token))
            throw new BusinessRuleException("Mã dock đã tồn tại trong kho.");

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        var dock = new Dock
        {
            WarehouseId = warehouseId,
            Code = code,
            Name = Required(dto.Name, "Tên dock"),
            SupportsInbound = dto.SupportsInbound,
            SupportsOutbound = dto.SupportsOutbound,
            AllowedVehicleType = OptionalUpper(dto.AllowedVehicleType),
            IsTemperatureControlled = dto.IsTemperatureControlled,
            HazardAllowed = dto.HazardAllowed,
            IsActive = dto.IsActive
        };
        context.Docks.Add(dock);
        await SaveAsync(token);
        AddAudit("Dock.Created", "Dock", dock.Id, warehouseId, $"Code: {dock.Code}");
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return MapDock(dock);
    }

    public async Task<DockDto> UpdateDockAsync(int warehouseId, int dockId, UpsertDockDto dto, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        ValidateDock(dto);
        var dock = await context.Docks.SingleOrDefaultAsync(x => x.Id == dockId && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy dock hoặc bạn không có quyền truy cập.");
        ApplyVersion(dock, dto.RowVersion);
        var code = NormalizeCode(dto.Code, "Mã dock");
        if (!string.Equals(code, dock.Code, StringComparison.Ordinal))
            throw new BusinessRuleException("Mã dock không được thay đổi sau khi tạo.");

        dock.Name = Required(dto.Name, "Tên dock");
        dock.SupportsInbound = dto.SupportsInbound;
        dock.SupportsOutbound = dto.SupportsOutbound;
        dock.AllowedVehicleType = OptionalUpper(dto.AllowedVehicleType);
        dock.IsTemperatureControlled = dto.IsTemperatureControlled;
        dock.HazardAllowed = dto.HazardAllowed;
        dock.IsActive = dto.IsActive;
        AddAudit("Dock.Updated", "Dock", dock.Id, warehouseId, $"Active: {dock.IsActive}");
        await SaveAsync(token);
        return MapDock(dock);
    }

    public async Task<IReadOnlyList<YardSlotDto>> GetYardSlotsAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        var occupied = await context.DockAppointments.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.YardSlotId != null && x.CheckedOutAtUtc == null
                && !ReleasedStatuses.Contains(x.Status))
            .Select(x => new { YardSlotId = x.YardSlotId!.Value, x.Code })
            .ToListAsync(token);
        var occupiedById = occupied.GroupBy(x => x.YardSlotId).ToDictionary(x => x.Key, x => x.OrderBy(y => y.Code).First().Code);

        var rows = await context.YardSlots.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .OrderBy(x => x.Code)
            .ToListAsync(token);
        return rows.Select(x => new YardSlotDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            Code = x.Code,
            Name = x.Name,
            IsActive = x.IsActive,
            Occupied = occupiedById.ContainsKey(x.Id),
            OccupiedByAppointmentCode = occupiedById.GetValueOrDefault(x.Id),
            RowVersion = Version(x.RowVersion)
        }).ToList();
    }

    public async Task<YardSlotDto> CreateYardSlotAsync(int warehouseId, UpsertYardSlotDto dto, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        var code = NormalizeCode(dto.Code, "Mã yard slot");
        if (await context.YardSlots.AnyAsync(x => x.WarehouseId == warehouseId && x.Code == code, token))
            throw new BusinessRuleException("Mã yard slot đã tồn tại trong kho.");

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        var slot = new YardSlot
        {
            WarehouseId = warehouseId,
            Code = code,
            Name = Required(dto.Name, "Tên yard slot"),
            IsActive = dto.IsActive
        };
        context.YardSlots.Add(slot);
        await SaveAsync(token);
        AddAudit("YardSlot.Created", "YardSlot", slot.Id, warehouseId, $"Code: {slot.Code}");
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return await MapYardSlotAsync(slot, token);
    }

    public async Task<YardSlotDto> UpdateYardSlotAsync(int warehouseId, int yardSlotId, UpsertYardSlotDto dto, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, token);
        var slot = await context.YardSlots.SingleOrDefaultAsync(x => x.Id == yardSlotId && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy yard slot hoặc bạn không có quyền truy cập.");
        ApplyVersion(slot, dto.RowVersion);
        var code = NormalizeCode(dto.Code, "Mã yard slot");
        if (!string.Equals(code, slot.Code, StringComparison.Ordinal))
            throw new BusinessRuleException("Mã yard slot không được thay đổi sau khi tạo.");

        slot.Name = Required(dto.Name, "Tên yard slot");
        slot.IsActive = dto.IsActive;
        AddAudit("YardSlot.Updated", "YardSlot", slot.Id, warehouseId, $"Active: {slot.IsActive}");
        await SaveAsync(token);
        return await MapYardSlotAsync(slot, token);
    }

    public async Task<IReadOnlyList<DockAppointmentDto>> GetAppointmentsAsync(DockAppointmentQueryDto query, CancellationToken token = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(token);
        var source = context.DockAppointments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Dock)
            .Include(x => x.YardSlot)
            .Where(x => accessible.Contains(x.WarehouseId));

        if (query.WarehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(query.WarehouseId.Value, token);
            source = source.Where(x => x.WarehouseId == query.WarehouseId.Value);
        }
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status.Value);
        if (query.Direction.HasValue) source = source.Where(x => x.Direction == query.Direction.Value);
        if (query.From.HasValue)
        {
            var from = query.From.Value.UtcDateTime;
            source = source.Where(x => x.PlannedEndUtc >= from);
        }
        if (query.To.HasValue)
        {
            var to = query.To.Value.UtcDateTime;
            source = source.Where(x => x.PlannedStartUtc <= to);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(x =>
                x.Code.Contains(search) ||
                (x.CarrierName != null && x.CarrierName.Contains(search)) ||
                (x.VehiclePlate != null && x.VehiclePlate.Contains(search)) ||
                (x.DriverName != null && x.DriverName.Contains(search)));
        }

        var rows = await source.OrderBy(x => x.PlannedStartUtc).ThenBy(x => x.Code).Take(500).ToListAsync(token);
        return rows.Select(MapAppointment).ToList();
    }

    public async Task<DockAppointmentDto> GetAppointmentAsync(int id, CancellationToken token = default)
    {
        var appointment = await context.DockAppointments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Dock)
            .Include(x => x.YardSlot)
            .Include(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy lịch xe hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(appointment.WarehouseId, token);
        return MapAppointment(appointment, includeEvents: true);
    }

    public async Task<DockAppointmentDto> CreateAppointmentAsync(UpsertDockAppointmentDto dto, CancellationToken token = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(dto.WarehouseId, token);
        ValidateAppointment(dto);
        var code = NormalizeCode(dto.Code, "Mã appointment");
        if (await context.DockAppointments.AnyAsync(x => x.WarehouseId == dto.WarehouseId && x.Code == code, token))
            throw new BusinessRuleException("Mã appointment đã tồn tại trong kho.");

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var appointment = new DockAppointment
        {
            WarehouseId = dto.WarehouseId,
            Code = code,
            Status = DockAppointmentStatus.Draft,
            CreatedBy = currentUser.UserId,
            CreatedAtUtc = now,
            UpdatedBy = currentUser.UserId,
            UpdatedAtUtc = now
        };
        ApplyAppointmentData(appointment, dto);
        context.DockAppointments.Add(appointment);
        await SaveAsync(token);
        AddEvent(appointment, "Created", dto.Note);
        AddAudit("DockAppointment.Created", "DockAppointment", appointment.Id, appointment.WarehouseId, $"Code: {appointment.Code}");
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return await ReloadAsync(appointment.Id, token);
    }

    public async Task<DockAppointmentDto> UpdateDraftAsync(int id, UpsertDockAppointmentDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status != DockAppointmentStatus.Draft)
            throw new ConcurrencyException("Chỉ appointment ở trạng thái Nháp mới được sửa.");
        if (appointment.WarehouseId != dto.WarehouseId)
            throw new BusinessRuleException("Không được chuyển appointment sang kho khác.");
        ApplyVersion(appointment, dto.RowVersion);
        ValidateAppointment(dto);
        var code = NormalizeCode(dto.Code, "Mã appointment");
        if (!string.Equals(code, appointment.Code, StringComparison.Ordinal))
            throw new BusinessRuleException("Mã appointment không được thay đổi sau khi tạo.");

        ApplyAppointmentData(appointment, dto);
        Touch(appointment);
        AddEvent(appointment, "DraftUpdated", dto.Note);
        AddAudit("DockAppointment.Updated", "DockAppointment", appointment.Id, appointment.WarehouseId, $"Code: {appointment.Code}");
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> ConfirmAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        RequireStatus(appointment, DockAppointmentStatus.Draft, "Chỉ appointment Nháp mới được xác nhận.");
        ApplyVersion(appointment, dto.RowVersion);
        await EnsureCalendarAllowsAsync(appointment, token);
        appointment.Status = DockAppointmentStatus.Confirmed;
        Touch(appointment);
        AddEvent(appointment, "Confirmed", dto.Note);
        AddAudit("DockAppointment.Confirmed", "DockAppointment", appointment.Id, appointment.WarehouseId, null);
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> ArriveAsync(int id, DockAppointmentArrivalDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        RequireStatus(appointment, DockAppointmentStatus.Confirmed, "Appointment phải được xác nhận trước khi ghi nhận xe đến.");
        ApplyVersion(appointment, dto.RowVersion);
        var at = dto.ArrivedAt?.UtcDateTime ?? timeProvider.GetUtcNow().UtcDateTime;
        appointment.ArrivedAtUtc = at;
        appointment.Status = DockAppointmentStatus.Arrived;
        Touch(appointment);
        AddEvent(appointment, "Arrived", dto.Note, at: at);
        AddAudit("DockAppointment.Arrived", "DockAppointment", appointment.Id, appointment.WarehouseId, $"ArrivedAtUtc: {at:o}");
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> CheckInAsync(int id, DockAppointmentCheckInDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        RequireStatus(appointment, DockAppointmentStatus.Arrived, "Xe phải ở trạng thái Đã đến trước khi check-in.");
        ApplyVersion(appointment, dto.RowVersion);

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
            : null;

        var vehicle = OptionalUpper(dto.VehiclePlate) ?? appointment.VehiclePlate;
        if (string.IsNullOrWhiteSpace(vehicle))
            throw new BusinessRuleException("Biển số xe là bắt buộc khi check-in.");
        if (!string.IsNullOrWhiteSpace(appointment.VehiclePlate) && !string.Equals(appointment.VehiclePlate, vehicle, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Biển số xe thực tế không khớp appointment. Hãy ghi nhận exception thay vì tự thay đổi.");
        appointment.VehiclePlate = vehicle;

        var trailer = OptionalUpper(dto.TrailerPlate);
        if (!string.IsNullOrWhiteSpace(appointment.TrailerPlate) && !string.IsNullOrWhiteSpace(trailer)
            && !string.Equals(appointment.TrailerPlate, trailer, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Biển số trailer không khớp appointment.");
        appointment.TrailerPlate ??= trailer;
        appointment.DriverName = Required(dto.DriverName, "Tên tài xế");
        appointment.DriverPhone = Optional(dto.DriverPhone);
        appointment.SealNumber = OptionalUpper(dto.SealNumber);

        if (dto.YardSlotId.HasValue)
        {
            var slot = await context.YardSlots.SingleOrDefaultAsync(x => x.Id == dto.YardSlotId.Value && x.WarehouseId == appointment.WarehouseId && x.IsActive, token)
                ?? throw new BusinessRuleException("Yard slot không tồn tại, khác kho hoặc đã ngừng hoạt động.");
            if (await IsYardSlotOccupiedAsync(slot.Id, appointment.Id, token))
                throw new BusinessRuleException("Yard slot đang được appointment khác sử dụng.");
            appointment.YardSlotId = slot.Id;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        appointment.CheckedInAtUtc = now;
        appointment.Status = DockAppointmentStatus.CheckedIn;
        Touch(appointment);
        AddEvent(appointment, "CheckedIn", dto.Note, yardSlotId: appointment.YardSlotId, at: now);
        AddAudit("DockAppointment.CheckedIn", "DockAppointment", appointment.Id, appointment.WarehouseId, $"Vehicle: {appointment.VehiclePlate}; YardSlotId: {appointment.YardSlotId}");
        await SaveAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> AssignDockAsync(int id, DockAppointmentAssignDockDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status is not (DockAppointmentStatus.CheckedIn or DockAppointmentStatus.DockAssigned))
            throw new ConcurrencyException("Appointment phải check-in trước khi gán dock.");
        ApplyVersion(appointment, dto.RowVersion);

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
            : null;

        var dock = await context.Docks.SingleOrDefaultAsync(x => x.Id == dto.DockId && x.WarehouseId == appointment.WarehouseId && x.IsActive, token)
            ?? throw new BusinessRuleException("Dock không tồn tại, khác kho hoặc đã ngừng hoạt động.");
        ValidateCompatibility(appointment, dock);

        var conflict = await context.DockAppointments.AnyAsync(x =>
            x.Id != appointment.Id &&
            x.DockId == dock.Id &&
            x.CheckedOutAtUtc == null &&
            !ReleasedStatuses.Contains(x.Status) &&
            (x.Status == DockAppointmentStatus.DockAssigned ||
             x.Status == DockAppointmentStatus.InService ||
             x.Status == DockAppointmentStatus.Completed ||
             (x.PlannedStartUtc < appointment.PlannedEndUtc && appointment.PlannedStartUtc < x.PlannedEndUtc)), token);
        if (conflict)
            throw new BusinessRuleException("Dock đang được appointment khác giữ trong khung giờ này.");

        var reassign = appointment.DockId.HasValue && appointment.DockId != dock.Id;
        appointment.DockId = dock.Id;
        appointment.DockAssignedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        appointment.Status = DockAppointmentStatus.DockAssigned;
        Touch(appointment);
        AddEvent(appointment, reassign ? "DockReassigned" : "DockAssigned", dto.Note, dockId: dock.Id);
        AddAudit(reassign ? "DockAppointment.DockReassigned" : "DockAppointment.DockAssigned", "DockAppointment", appointment.Id, appointment.WarehouseId, $"DockId: {dock.Id}");
        await SaveAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return await ReloadAsync(id, token);
    }

    public Task<DockAppointmentDto> StartServiceAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default) =>
        TransitionAsync(id, dto, DockAppointmentStatus.DockAssigned, DockAppointmentStatus.InService, "ServiceStarted", "DockAppointment.ServiceStarted", token);

    public Task<DockAppointmentDto> CompleteAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default) =>
        TransitionAsync(id, dto, DockAppointmentStatus.InService, DockAppointmentStatus.Completed, "ServiceCompleted", "DockAppointment.ServiceCompleted", token);

    public async Task<DockAppointmentDto> CheckoutAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        RequireStatus(appointment, DockAppointmentStatus.Completed, "Chỉ appointment đã hoàn thành loading/unloading mới được checkout.");
        if (appointment.CheckedOutAtUtc.HasValue)
            throw new ConcurrencyException("Appointment đã checkout.");
        ApplyVersion(appointment, dto.RowVersion);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        appointment.CheckedOutAtUtc = now;
        Touch(appointment);
        AddEvent(appointment, "CheckedOut", dto.Note, dockId: appointment.DockId, yardSlotId: appointment.YardSlotId, at: now);
        AddAudit("DockAppointment.CheckedOut", "DockAppointment", appointment.Id, appointment.WarehouseId, null);
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> CancelAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status is not (DockAppointmentStatus.Draft or DockAppointmentStatus.Confirmed))
            throw new ConcurrencyException("Chỉ appointment Nháp hoặc Đã xác nhận mới được hủy.");
        ApplyVersion(appointment, dto.RowVersion);
        appointment.Status = DockAppointmentStatus.Cancelled;
        Touch(appointment);
        AddEvent(appointment, "Cancelled", dto.Note);
        AddAudit("DockAppointment.Cancelled", "DockAppointment", appointment.Id, appointment.WarehouseId, null);
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> MarkNoShowAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status != DockAppointmentStatus.Confirmed)
            throw new ConcurrencyException("Chỉ appointment đã xác nhận nhưng chưa đến mới được đánh dấu No-show.");
        ApplyVersion(appointment, dto.RowVersion);
        appointment.Status = DockAppointmentStatus.NoShow;
        Touch(appointment);
        AddEvent(appointment, "NoShow", dto.Note);
        AddAudit("DockAppointment.NoShow", "DockAppointment", appointment.Id, appointment.WarehouseId, null);
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    public async Task<DockAppointmentDto> MarkExceptionAsync(int id, DockAppointmentExceptionDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status is DockAppointmentStatus.Completed or DockAppointmentStatus.Cancelled or DockAppointmentStatus.NoShow or DockAppointmentStatus.Exception)
            throw new ConcurrencyException("Appointment đã ở trạng thái kết thúc.");
        ApplyVersion(appointment, dto.RowVersion);
        appointment.ExceptionCode = NormalizeCode(dto.ExceptionCode, "Mã exception");
        appointment.Status = DockAppointmentStatus.Exception;
        Touch(appointment);
        AddEvent(appointment, "Exception", dto.Note);
        AddAudit("DockAppointment.Exception", "DockAppointment", appointment.Id, appointment.WarehouseId, $"ExceptionCode: {appointment.ExceptionCode}");
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    private async Task<DockAppointmentDto> TransitionAsync(
        int id,
        DockAppointmentCommandDto dto,
        DockAppointmentStatus expected,
        DockAppointmentStatus next,
        string eventType,
        string auditAction,
        CancellationToken token)
    {
        var appointment = await GetForMutationAsync(id, token);
        RequireStatus(appointment, expected, $"Appointment phải ở trạng thái {expected}.");
        ApplyVersion(appointment, dto.RowVersion);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        appointment.Status = next;
        if (next == DockAppointmentStatus.InService) appointment.ServiceStartedAtUtc = now;
        if (next == DockAppointmentStatus.Completed) appointment.ServiceCompletedAtUtc = now;
        Touch(appointment);
        AddEvent(appointment, eventType, dto.Note, dockId: appointment.DockId, yardSlotId: appointment.YardSlotId, at: now);
        AddAudit(auditAction, "DockAppointment", appointment.Id, appointment.WarehouseId, null);
        await SaveAsync(token);
        return await ReloadAsync(id, token);
    }

    private async Task<DockAppointment> GetForMutationAsync(int id, CancellationToken token)
    {
        var appointment = await context.DockAppointments.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy lịch xe hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(appointment.WarehouseId, token);
        return appointment;
    }

    private async Task<DockAppointmentDto> ReloadAsync(int id, CancellationToken token)
    {
        var appointment = await context.DockAppointments.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Dock)
            .Include(x => x.YardSlot)
            .Include(x => x.Events)
            .SingleAsync(x => x.Id == id, token);
        return MapAppointment(appointment, includeEvents: true);
    }

    private async Task EnsureCalendarAllowsAsync(DockAppointment appointment, CancellationToken token)
    {
        var calendar = await context.WarehouseCalendars.AsNoTracking()
            .Include(x => x.Days)
            .SingleOrDefaultAsync(x => x.WarehouseId == appointment.WarehouseId, token)
            ?? throw new BusinessRuleException("Kho chưa cấu hình lịch vận hành nên chưa thể xác nhận appointment.");

        var zone = ResolveTimeZone(calendar.TimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(appointment.PlannedStartUtc, DateTimeKind.Utc)), zone);
        var localEnd = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(appointment.PlannedEndUtc, DateTimeKind.Utc)), zone);
        var day = calendar.Days.SingleOrDefault(x => x.DayOfWeek == (int)localStart.DayOfWeek);
        if (day?.IsOpen != true || !day.OpensAtLocal.HasValue || !day.ClosesAtLocal.HasValue)
            throw new BusinessRuleException("Appointment nằm trong ngày kho đóng cửa.");

        var open = localStart.Date + day.OpensAtLocal.Value;
        var close = localStart.Date + day.ClosesAtLocal.Value;
        if (day.OpensAtLocal.Value > day.ClosesAtLocal.Value) close = close.AddDays(1);
        if (localStart.DateTime < open || localEnd.DateTime > close)
            throw new BusinessRuleException("Appointment nằm ngoài giờ vận hành của kho.");

        var cutoffValue = appointment.Direction == DockAppointmentDirection.Inbound ? day.InboundCutoffLocal : day.OutboundCutoffLocal;
        if (cutoffValue.HasValue)
        {
            var cutoff = localStart.Date + cutoffValue.Value;
            if (day.OpensAtLocal.Value > day.ClosesAtLocal.Value && cutoffValue.Value < day.OpensAtLocal.Value) cutoff = cutoff.AddDays(1);
            if (localStart.DateTime > cutoff)
                throw new BusinessRuleException("Appointment bắt đầu sau cutoff của ngày vận hành.");
        }
    }

    private static void ValidateCompatibility(DockAppointment appointment, Dock dock)
    {
        if (appointment.Direction == DockAppointmentDirection.Inbound && !dock.SupportsInbound)
            throw new BusinessRuleException("Dock không hỗ trợ inbound.");
        if (appointment.Direction == DockAppointmentDirection.Outbound && !dock.SupportsOutbound)
            throw new BusinessRuleException("Dock không hỗ trợ outbound.");
        if (!string.IsNullOrWhiteSpace(dock.AllowedVehicleType)
            && !string.Equals(dock.AllowedVehicleType, appointment.VehicleType, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Loại xe không tương thích với dock.");
        if (appointment.RequiresTemperatureControl && !dock.IsTemperatureControlled)
            throw new BusinessRuleException("Appointment yêu cầu kiểm soát nhiệt độ nhưng dock không hỗ trợ.");
        if (appointment.Hazardous && !dock.HazardAllowed)
            throw new BusinessRuleException("Dock không cho phép hàng nguy hiểm.");
    }

    private async Task<bool> IsYardSlotOccupiedAsync(int yardSlotId, int appointmentId, CancellationToken token) =>
        await context.DockAppointments.AnyAsync(x =>
            x.Id != appointmentId &&
            x.YardSlotId == yardSlotId &&
            x.CheckedOutAtUtc == null &&
            !ReleasedStatuses.Contains(x.Status), token);

    private static void ValidateDock(UpsertDockDto dto)
    {
        if (!dto.SupportsInbound && !dto.SupportsOutbound)
            throw new BusinessRuleException("Dock phải hỗ trợ ít nhất inbound hoặc outbound.");
    }

    private static void ValidateAppointment(UpsertDockAppointmentDto dto)
    {
        if (dto.WarehouseId <= 0) throw new BusinessRuleException("Warehouse là bắt buộc.");
        if (dto.PlannedStart >= dto.PlannedEnd) throw new BusinessRuleException("Thời gian bắt đầu phải trước thời gian kết thúc.");
        if (dto.PlannedEnd - dto.PlannedStart > TimeSpan.FromDays(2))
            throw new BusinessRuleException("Khung giờ appointment không được dài quá 48 giờ.");
    }

    private static void ApplyAppointmentData(DockAppointment appointment, UpsertDockAppointmentDto dto)
    {
        appointment.Direction = dto.Direction;
        appointment.PlannedStartUtc = dto.PlannedStart.UtcDateTime;
        appointment.PlannedEndUtc = dto.PlannedEnd.UtcDateTime;
        appointment.CarrierCode = OptionalUpper(dto.CarrierCode);
        appointment.CarrierName = Optional(dto.CarrierName);
        appointment.VehiclePlate = OptionalUpper(dto.VehiclePlate);
        appointment.TrailerPlate = OptionalUpper(dto.TrailerPlate);
        appointment.VehicleType = OptionalUpper(dto.VehicleType);
        appointment.RequiresTemperatureControl = dto.RequiresTemperatureControl;
        appointment.Hazardous = dto.Hazardous;
        appointment.Note = Optional(dto.Note);
    }

    private void Touch(DockAppointment appointment)
    {
        appointment.UpdatedBy = currentUser.UserId;
        appointment.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    }

    private void AddEvent(DockAppointment appointment, string eventType, string? note, int? dockId = null, int? yardSlotId = null, DateTime? at = null)
    {
        context.DockAppointmentEvents.Add(new DockAppointmentEvent
        {
            DockAppointmentId = appointment.Id,
            EventType = eventType,
            EventAtUtc = at ?? timeProvider.GetUtcNow().UtcDateTime,
            ActorUserId = currentUser.UserId,
            DockId = dockId,
            YardSlotId = yardSlotId,
            Note = Optional(note)
        });
    }

    private void AddAudit(string action, string entityName, int entityId, int warehouseId, string? values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            WarehouseId = warehouseId,
            NewValues = values,
            Result = "Success",
            Timestamp = timeProvider.GetUtcNow().UtcDateTime
        });

    private async Task SaveAsync(CancellationToken token)
    {
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new BusinessRuleException("Dữ liệu bị trùng với bản ghi vừa được tạo bởi yêu cầu khác. Vui lòng tải lại và thử lại.", ex);
        }
    }

    private void ApplyVersion(Dock entity, string? value) =>
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = ParseVersion(value);

    private void ApplyVersion(YardSlot entity, string? value) =>
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = ParseVersion(value);

    private void ApplyVersion(DockAppointment entity, string? value) =>
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = ParseVersion(value);

    private static byte[] ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        }
    }

    private static void RequireStatus(DockAppointment appointment, DockAppointmentStatus expected, string message)
    {
        if (appointment.Status != expected) throw new ConcurrencyException(message);
    }

    private async Task<YardSlotDto> MapYardSlotAsync(YardSlot slot, CancellationToken token)
    {
        var occupied = await IsYardSlotOccupiedAsync(slot.Id, 0, token);
        var code = occupied
            ? await context.DockAppointments.AsNoTracking()
                .Where(x => x.YardSlotId == slot.Id && x.CheckedOutAtUtc == null && !ReleasedStatuses.Contains(x.Status))
                .OrderBy(x => x.PlannedStartUtc)
                .Select(x => x.Code)
                .FirstOrDefaultAsync(token)
            : null;
        return new YardSlotDto
        {
            Id = slot.Id,
            WarehouseId = slot.WarehouseId,
            Code = slot.Code,
            Name = slot.Name,
            IsActive = slot.IsActive,
            Occupied = occupied,
            OccupiedByAppointmentCode = code,
            RowVersion = Version(slot.RowVersion)
        };
    }

    private static DockDto MapDock(Dock x) => new()
    {
        Id = x.Id,
        WarehouseId = x.WarehouseId,
        Code = x.Code,
        Name = x.Name,
        SupportsInbound = x.SupportsInbound,
        SupportsOutbound = x.SupportsOutbound,
        AllowedVehicleType = x.AllowedVehicleType,
        IsTemperatureControlled = x.IsTemperatureControlled,
        HazardAllowed = x.HazardAllowed,
        IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion)
    };

    private static DockAppointmentDto MapAppointment(DockAppointment x, bool includeEvents = false) => new()
    {
        Id = x.Id,
        WarehouseId = x.WarehouseId,
        WarehouseCode = x.Warehouse.Code,
        WarehouseName = x.Warehouse.Name,
        Code = x.Code,
        Direction = x.Direction,
        Status = x.Status,
        PlannedStartUtc = AsUtc(x.PlannedStartUtc),
        PlannedEndUtc = AsUtc(x.PlannedEndUtc),
        CarrierCode = x.CarrierCode,
        CarrierName = x.CarrierName,
        VehiclePlate = x.VehiclePlate,
        TrailerPlate = x.TrailerPlate,
        VehicleType = x.VehicleType,
        RequiresTemperatureControl = x.RequiresTemperatureControl,
        Hazardous = x.Hazardous,
        DriverName = x.DriverName,
        DriverPhone = x.DriverPhone,
        SealNumber = x.SealNumber,
        YardSlotId = x.YardSlotId,
        YardSlotCode = x.YardSlot?.Code,
        DockId = x.DockId,
        DockCode = x.Dock?.Code,
        ArrivedAtUtc = AsUtc(x.ArrivedAtUtc),
        CheckedInAtUtc = AsUtc(x.CheckedInAtUtc),
        DockAssignedAtUtc = AsUtc(x.DockAssignedAtUtc),
        ServiceStartedAtUtc = AsUtc(x.ServiceStartedAtUtc),
        ServiceCompletedAtUtc = AsUtc(x.ServiceCompletedAtUtc),
        CheckedOutAtUtc = AsUtc(x.CheckedOutAtUtc),
        ExceptionCode = x.ExceptionCode,
        Note = x.Note,
        RowVersion = Version(x.RowVersion),
        Events = includeEvents
            ? x.Events.OrderBy(y => y.EventAtUtc).Select(y => new DockAppointmentEventDto
            {
                Id = y.Id,
                EventType = y.EventType,
                EventAtUtc = AsUtc(y.EventAtUtc),
                ActorUserId = y.ActorUserId,
                DockId = y.DockId,
                YardSlotId = y.YardSlotId,
                Note = y.Note
            }).ToList()
            : []
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value) : null;

    private static string NormalizeCode(string value, string field)
    {
        var result = Required(value, field).ToUpperInvariant();
        if (result.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new BusinessRuleException(field + " chỉ được chứa chữ, số, dấu -, _ hoặc .");
        return result;
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new BusinessRuleException(field + " là bắt buộc.") : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? OptionalUpper(string? value) => Optional(value)?.ToUpperInvariant();
    private static string? Version(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : null;

    private static TimeZoneInfo ResolveTimeZone(string value)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(value); }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out var windows))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(windows); } catch (TimeZoneNotFoundException) { }
            }
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(value, out var iana))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(iana); } catch (TimeZoneNotFoundException) { }
            }
            throw new BusinessRuleException("Timezone của kho không hợp lệ.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new BusinessRuleException("Timezone của kho không hợp lệ.");
        }
    }
}
