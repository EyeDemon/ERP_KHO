using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class DockYardService(
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
                Code = warehouse.Code.Trim(),
                Name = warehouse.Name.Trim(),
                TimeZoneId = (context.WarehouseCalendars
                    .Where(calendar => calendar.WarehouseId == warehouse.Id)
                    .Select(calendar => calendar.TimeZoneId)
                    .FirstOrDefault() ?? "UTC").Trim(),
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
        var occupiedById = occupied.GroupBy(x => x.YardSlotId).ToDictionary(x => x.Key, x => x.OrderBy(y => y.Code).First().Code.Trim());

        var rows = await context.YardSlots.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .OrderBy(x => x.Code)
            .ToListAsync(token);
        return rows.Select(x => new YardSlotDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            Code = x.Code.Trim(),
            Name = x.Name.Trim(),
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
            .Include(x => x.Events)
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
        return rows.Select(x => MapAppointment(x)).ToList();
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
}
