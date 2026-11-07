using System.Globalization;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WarehouseCalendarService
{
    private WarehouseCalendarDto Map(Warehouse warehouse, WarehouseCalendar? calendar, IReadOnlyList<AuditLog>? exceptions = null)
    {
        var timeZoneId = (calendar?.TimeZoneId ?? "UTC").Trim();
        var zone = ResolveTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var days = calendar?.Days.OrderBy(x => x.DayOfWeek).ToList() ?? [];
        var shifts = calendar?.Shifts.OrderBy(x => x.StartTimeLocal).ThenBy(x => x.Code).ToList() ?? [];
        var openNow = IsCalendarOpen(days, localNow.DayOfWeek, localNow.TimeOfDay);
        var overrideState = exceptions is not null ? EvaluateDateException(exceptions, localNow) : null;
        if (overrideState.HasValue)
            openNow = overrideState.Value;
        var currentShift = openNow
            ? shifts.FirstOrDefault(x => x.IsActive && IsWithinShiftWindow(localNow.TimeOfDay, x.StartTimeLocal, x.EndTimeLocal))
            : null;

        return new WarehouseCalendarDto
        {
            WarehouseId = warehouse.Id,
            WarehouseCode = (warehouse.Code ?? string.Empty).Trim(),
            WarehouseName = (warehouse.Name ?? string.Empty).Trim(),
            IsConfigured = calendar is not null,
            TimeZoneId = timeZoneId,
            LocalNow = localNow,
            IsOpenNow = openNow,
            CurrentShiftCode = currentShift?.Code?.Trim(),
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
        Code = (shift.Code ?? string.Empty).Trim(),
        Name = (shift.Name ?? string.Empty).Trim(),
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

    private static bool IsWithinShiftWindow(TimeSpan value, TimeSpan start, TimeSpan end) =>
        start < end ? value >= start && value < end : value >= start || value < end;

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
            if (string.Equals(id, "Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "Asia/Saigon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "SE Asia Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
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

    private async Task<AuditLog> AuditAsync(string action, int entityId, int warehouseId, string values, CancellationToken token)
    {
        int? validUserId = currentUser.UserId > 0 && await context.Users.AsNoTracking().AnyAsync(u => u.Id == currentUser.UserId, token)
            ? currentUser.UserId
            : null;
        return new AuditLog
        {
            UserId = validUserId,
            Action = action,
            EntityName = action.StartsWith("WarehouseShift.", StringComparison.Ordinal) ? "WarehouseShift" : "WarehouseCalendar",
            EntityId = entityId,
            WarehouseId = warehouseId,
            NewValues = values,
            Result = "Success",
            Timestamp = timeProvider.GetUtcNow().UtcDateTime
        };
    }
}
