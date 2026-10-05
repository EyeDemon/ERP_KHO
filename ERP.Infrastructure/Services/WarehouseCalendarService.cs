using System.Globalization;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class WarehouseCalendarService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouses,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IWarehouseCalendarService
{
    private static readonly string[] DayNames = ["Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy"];

    public async Task<WarehouseCalendarDto> GetAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var warehouse = await context.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy kho hoặc bạn không có quyền truy cập.");

        var calendar = await context.WarehouseCalendars.AsNoTracking()
            .Include(x => x.Days)
            .Include(x => x.Shifts)
            .SingleOrDefaultAsync(x => x.WarehouseId == warehouseId, token);

        return Map(warehouse, calendar);
    }

    public async Task<WarehouseCalendarDto> UpdateCalendarAsync(int warehouseId, UpdateWarehouseCalendarDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        _ = ResolveTimeZone(dto.TimeZoneId);
        var days = ValidateDays(dto.Days);

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token)
            : null;

        var warehouse = await context.Warehouses.SingleOrDefaultAsync(x => x.Id == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy kho hoặc bạn không có quyền truy cập.");
        var calendar = await context.WarehouseCalendars
            .Include(x => x.Days)
            .Include(x => x.Shifts)
            .SingleOrDefaultAsync(x => x.WarehouseId == warehouseId, token);

        if (calendar is null)
        {
            if (!string.IsNullOrWhiteSpace(dto.RowVersion))
                throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");

            calendar = new WarehouseCalendar
            {
                WarehouseId = warehouseId,
                TimeZoneId = dto.TimeZoneId.Trim(),
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                UpdatedBy = currentUser.UserId
            };
            foreach (var day in days)
                calendar.Days.Add(day);
            context.WarehouseCalendars.Add(calendar);
        }
        else
        {
            ApplyVersion(calendar, dto.RowVersion);
            calendar.TimeZoneId = dto.TimeZoneId.Trim();
            calendar.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            calendar.UpdatedBy = currentUser.UserId;

            foreach (var desired in days)
            {
                var existing = calendar.Days.SingleOrDefault(x => x.DayOfWeek == desired.DayOfWeek);
                if (existing is null)
                {
                    calendar.Days.Add(desired);
                    continue;
                }
                existing.IsOpen = desired.IsOpen;
                existing.OpensAtLocal = desired.OpensAtLocal;
                existing.ClosesAtLocal = desired.ClosesAtLocal;
                existing.InboundCutoffLocal = desired.InboundCutoffLocal;
                existing.OutboundCutoffLocal = desired.OutboundCutoffLocal;
            }
        }

        context.AuditLogs.Add(Audit("WarehouseCalendar.Updated", warehouseId, warehouseId,
            "Timezone: " + calendar.TimeZoneId + "; OpenDays: " + calendar.Days.Count(x => x.IsOpen)));

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }

        if (transaction is not null) await transaction.CommitAsync(token);
        return Map(warehouse, calendar);
    }

    public async Task<WarehouseShiftDto> CreateShiftAsync(int warehouseId, UpsertWarehouseShiftDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var calendarExists = await context.WarehouseCalendars.AnyAsync(x => x.WarehouseId == warehouseId, token);
        if (!calendarExists) throw new BusinessRuleException("Hãy cấu hình lịch vận hành của kho trước khi tạo ca.");

        var code = Required(dto.Code, "Mã ca").ToUpperInvariant();
        if (await context.WarehouseShifts.AnyAsync(x => x.WarehouseId == warehouseId && x.Code == code, token))
            throw new BusinessRuleException("Mã ca đã tồn tại trong kho.");

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token)
            : null;
        var shift = BuildShift(warehouseId, dto);
        shift.Code = code;
        shift.CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        shift.UpdatedAtUtc = shift.CreatedAtUtc;
        shift.UpdatedBy = currentUser.UserId;
        context.WarehouseShifts.Add(shift);

        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit("WarehouseShift.Created", shift.Id, warehouseId, "Code: " + shift.Code));
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return MapShift(shift);
    }

    public async Task<WarehouseShiftDto> UpdateShiftAsync(int warehouseId, int shiftId, UpsertWarehouseShiftDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var shift = await context.WarehouseShifts.SingleOrDefaultAsync(x => x.Id == shiftId && x.WarehouseId == warehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy ca hoặc bạn không có quyền truy cập.");
        ApplyVersion(shift, dto.RowVersion);

        var code = Required(dto.Code, "Mã ca").ToUpperInvariant();
        if (!string.Equals(code, shift.Code, StringComparison.Ordinal))
            throw new BusinessRuleException("Mã ca không được thay đổi sau khi tạo.");

        ApplyShift(shift, dto);
        shift.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        shift.UpdatedBy = currentUser.UserId;
        context.AuditLogs.Add(Audit("WarehouseShift.Updated", shift.Id, warehouseId, "Code: " + shift.Code + "; Active: " + shift.IsActive));

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        return MapShift(shift);
    }

    private WarehouseCalendarDto Map(Warehouse warehouse, WarehouseCalendar? calendar)
    {
        var timeZoneId = calendar?.TimeZoneId ?? "UTC";
        var zone = ResolveTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var days = calendar?.Days.OrderBy(x => x.DayOfWeek).ToList() ?? [];
        var shifts = calendar?.Shifts.OrderBy(x => x.StartTimeLocal).ThenBy(x => x.Code).ToList() ?? [];
        var openNow = IsCalendarOpen(days, localNow.DayOfWeek, localNow.TimeOfDay);
        var currentShift = shifts.FirstOrDefault(x => x.IsActive && IsWithinWindow(localNow.TimeOfDay, x.StartTimeLocal, x.EndTimeLocal));

        return new WarehouseCalendarDto
        {
            WarehouseId = warehouse.Id,
            WarehouseCode = warehouse.Code,
            WarehouseName = warehouse.Name,
            IsConfigured = calendar is not null,
            TimeZoneId = timeZoneId,
            LocalNow = localNow,
            IsOpenNow = openNow,
            CurrentShiftCode = currentShift?.Code,
            RowVersion = Version(calendar?.RowVersion),
            Days = Enumerable.Range(0, 7).Select(day =>
            {
                var source = days.SingleOrDefault(x => x.DayOfWeek == day);
                return new WarehouseCalendarDayDto
                {
                    DayOfWeek = day,
                    DayName = DayNames[day],
                    IsOpen = source?.IsOpen ?? false,
                    OpensAtLocal = Format(source?.OpensAtLocal),
                    ClosesAtLocal = Format(source?.ClosesAtLocal),
                    InboundCutoffLocal = Format(source?.InboundCutoffLocal),
                    OutboundCutoffLocal = Format(source?.OutboundCutoffLocal),
                    Overnight = source?.IsOpen == true && source.OpensAtLocal > source.ClosesAtLocal
                };
            }).ToList(),
            Shifts = shifts.Select(MapShift).ToList()
        };
    }

    private static List<WarehouseCalendarDay> ValidateDays(IReadOnlyList<UpdateWarehouseCalendarDayDto> input)
    {
        if (input.Count != 7 || input.Select(x => x.DayOfWeek).Distinct().Count() != 7 || input.Any(x => x.DayOfWeek is < 0 or > 6))
            throw new BusinessRuleException("Lịch tuần phải có đúng 7 ngày từ Chủ nhật đến Thứ bảy.");

        return input.OrderBy(x => x.DayOfWeek).Select(day =>
        {
            if (!day.IsOpen)
            {
                if (new[] { day.OpensAtLocal, day.ClosesAtLocal, day.InboundCutoffLocal, day.OutboundCutoffLocal }.Any(x => !string.IsNullOrWhiteSpace(x)))
                    throw new BusinessRuleException(DayNames[day.DayOfWeek] + " đang đóng cửa nên không được có giờ mở/cutoff.");
                return new WarehouseCalendarDay { DayOfWeek = day.DayOfWeek, IsOpen = false };
            }

            var opens = ParseRequiredTime(day.OpensAtLocal, "Giờ mở cửa");
            var closes = ParseRequiredTime(day.ClosesAtLocal, "Giờ đóng cửa");
            if (opens == closes) throw new BusinessRuleException(DayNames[day.DayOfWeek] + " phải có giờ mở và đóng khác nhau.");
            var inbound = ParseOptionalTime(day.InboundCutoffLocal, "Inbound cutoff");
            var outbound = ParseOptionalTime(day.OutboundCutoffLocal, "Outbound cutoff");
            if (inbound.HasValue && !IsWithinWindow(inbound.Value, opens, closes))
                throw new BusinessRuleException("Inbound cutoff phải nằm trong khung giờ vận hành.");
            if (outbound.HasValue && !IsWithinWindow(outbound.Value, opens, closes))
                throw new BusinessRuleException("Outbound cutoff phải nằm trong khung giờ vận hành.");

            return new WarehouseCalendarDay
            {
                DayOfWeek = day.DayOfWeek,
                IsOpen = true,
                OpensAtLocal = opens,
                ClosesAtLocal = closes,
                InboundCutoffLocal = inbound,
                OutboundCutoffLocal = outbound
            };
        }).ToList();
    }

    private WarehouseShift BuildShift(int warehouseId, UpsertWarehouseShiftDto dto)
    {
        var shift = new WarehouseShift { WarehouseId = warehouseId };
        ApplyShift(shift, dto);
        return shift;
    }

    private static void ApplyShift(WarehouseShift shift, UpsertWarehouseShiftDto dto)
    {
        var start = ParseRequiredTime(dto.StartTimeLocal, "Giờ bắt đầu ca");
        var end = ParseRequiredTime(dto.EndTimeLocal, "Giờ kết thúc ca");
        if (start == end) throw new BusinessRuleException("Ca phải có giờ bắt đầu và kết thúc khác nhau.");
        var duration = end > start ? end - start : TimeSpan.FromDays(1) - start + end;
        if (dto.BreakMinutes < 0 || dto.BreakMinutes >= duration.TotalMinutes)
            throw new BusinessRuleException("Thời gian nghỉ phải nhỏ hơn tổng thời lượng ca.");
        if (dto.PlannedHeadcount < 0) throw new BusinessRuleException("Planned headcount không được âm.");
        ValidateNonNegative(dto.InboundPalletsPerHour, "Inbound pallets/hour");
        ValidateNonNegative(dto.OutboundOrdersPerHour, "Outbound orders/hour");
        ValidateNonNegative(dto.DockSlots, "Dock slots");
        ValidateNonNegative(dto.LaborHours, "Labor hours");
        ValidateNonNegative(dto.StagingCapacity, "Staging capacity");
        ValidateNonNegative(dto.PackingStations, "Packing stations");
        ValidateNonNegative(dto.EquipmentAvailable, "Equipment availability");

        shift.Name = Required(dto.Name, "Tên ca");
        shift.StartTimeLocal = start;
        shift.EndTimeLocal = end;
        shift.BreakMinutes = dto.BreakMinutes;
        shift.PlannedHeadcount = dto.PlannedHeadcount;
        shift.InboundPalletsPerHour = dto.InboundPalletsPerHour;
        shift.OutboundOrdersPerHour = dto.OutboundOrdersPerHour;
        shift.DockSlots = dto.DockSlots;
        shift.LaborHours = dto.LaborHours;
        shift.StagingCapacity = dto.StagingCapacity;
        shift.PackingStations = dto.PackingStations;
        shift.EquipmentAvailable = dto.EquipmentAvailable;
        shift.IsActive = dto.IsActive;
    }

    private static WarehouseShiftDto MapShift(WarehouseShift shift) => new()
    {
        Id = shift.Id,
        WarehouseId = shift.WarehouseId,
        Code = shift.Code,
        Name = shift.Name,
        StartTimeLocal = Format(shift.StartTimeLocal)!,
        EndTimeLocal = Format(shift.EndTimeLocal)!,
        Overnight = shift.StartTimeLocal > shift.EndTimeLocal,
        BreakMinutes = shift.BreakMinutes,
        PlannedHeadcount = shift.PlannedHeadcount,
        InboundPalletsPerHour = shift.InboundPalletsPerHour,
        OutboundOrdersPerHour = shift.OutboundOrdersPerHour,
        DockSlots = shift.DockSlots,
        LaborHours = shift.LaborHours,
        StagingCapacity = shift.StagingCapacity,
        PackingStations = shift.PackingStations,
        EquipmentAvailable = shift.EquipmentAvailable,
        IsActive = shift.IsActive,
        RowVersion = Version(shift.RowVersion)
    };

    private static bool IsCalendarOpen(IReadOnlyList<WarehouseCalendarDay> days, DayOfWeek dayOfWeek, TimeSpan now)
    {
        var currentDay = (int)dayOfWeek;
        var today = days.SingleOrDefault(x => x.DayOfWeek == currentDay);
        if (today?.IsOpen == true && today.OpensAtLocal.HasValue && today.ClosesAtLocal.HasValue)
        {
            if (today.OpensAtLocal < today.ClosesAtLocal && now >= today.OpensAtLocal && now < today.ClosesAtLocal) return true;
            if (today.OpensAtLocal > today.ClosesAtLocal && now >= today.OpensAtLocal) return true;
        }

        var previousDay = (currentDay + 6) % 7;
        var previous = days.SingleOrDefault(x => x.DayOfWeek == previousDay);
        return previous?.IsOpen == true
            && previous.OpensAtLocal.HasValue
            && previous.ClosesAtLocal.HasValue
            && previous.OpensAtLocal > previous.ClosesAtLocal
            && now < previous.ClosesAtLocal;
    }

    private static bool IsWithinWindow(TimeSpan value, TimeSpan start, TimeSpan end) =>
        start < end ? value >= start && value <= end : value >= start || value <= end;

    private static TimeSpan ParseRequiredTime(string? value, string field) =>
        ParseOptionalTime(value, field) ?? throw new BusinessRuleException(field + " là bắt buộc.");

    private static TimeSpan? ParseOptionalTime(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var parsed) || parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1))
            throw new BusinessRuleException(field + " phải theo định dạng HH:mm.");
        return parsed;
    }

    private static string? Format(TimeSpan? value) => value?.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    private static string Required(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new BusinessRuleException(field + " là bắt buộc.") : value.Trim();

    private static void ValidateNonNegative(decimal? value, string field)
    {
        if (value < 0) throw new BusinessRuleException(field + " không được âm.");
    }

    private static void ValidateNonNegative(int? value, string field)
    {
        if (value < 0) throw new BusinessRuleException(field + " không được âm.");
    }

    private static TimeZoneInfo ResolveTimeZone(string value)
    {
        var id = Required(value, "Timezone");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(windowsId); } catch (TimeZoneNotFoundException) { }
            }
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); } catch (TimeZoneNotFoundException) { }
            }
            throw new BusinessRuleException("Timezone không hợp lệ trên hệ thống.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new BusinessRuleException("Timezone không hợp lệ trên hệ thống.");
        }
    }

    private void ApplyVersion(WarehouseCalendar entity, string? value)
    {
        var bytes = ParseVersion(value);
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = bytes;
    }

    private void ApplyVersion(WarehouseShift entity, string? value)
    {
        var bytes = ParseVersion(value);
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = bytes;
    }

    private static byte[] ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
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

    private static string? Version(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : null;

    private AuditLog Audit(string action, int entityId, int warehouseId, string values) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = action.StartsWith("WarehouseShift.", StringComparison.Ordinal) ? "WarehouseShift" : "WarehouseCalendar",
        EntityId = entityId,
        WarehouseId = warehouseId,
        NewValues = values,
        Result = "Success",
        Timestamp = timeProvider.GetUtcNow().UtcDateTime
    };
}
