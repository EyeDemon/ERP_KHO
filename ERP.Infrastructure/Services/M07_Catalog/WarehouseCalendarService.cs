using System.Globalization;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WarehouseCalendarService(
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

        var exceptions = await LoadCalendarExceptionsAsync(warehouseId, token);
        return Map(warehouse, calendar, exceptions);
    }

    public async Task<bool> ValidateOperatingWindowAsync(int warehouseId, DateTime? atUtc = null, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var calendar = await context.WarehouseCalendars.AsNoTracking()
            .Include(x => x.Days)
            .SingleOrDefaultAsync(x => x.WarehouseId == warehouseId, token)
            ?? throw new BusinessRuleException("Kho chưa cấu hình lịch vận hành.");

        var zone = ResolveTimeZone(calendar.TimeZoneId);
        var targetUtc = atUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(atUtc.Value, DateTimeKind.Utc))
            : timeProvider.GetUtcNow();
        var localTime = TimeZoneInfo.ConvertTime(targetUtc, zone);
        var exceptions = await LoadCalendarExceptionsAsync(warehouseId, token);
        var overrideState = EvaluateDateException(exceptions, localTime);
        if (overrideState == true)
            return true;
        if (overrideState == false)
            throw new BusinessRuleException("Kho đang đóng cửa hoặc bảo trì theo ngoại lệ lịch vận hành.");

        var days = calendar.Days.OrderBy(x => x.DayOfWeek).ToList();
        if (!IsCalendarOpen(days, localTime.DayOfWeek, localTime.TimeOfDay))
            throw new BusinessRuleException("Kho đang ngoài giờ vận hành.");
        return true;
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

        context.AuditLogs.Add(await AuditAsync("WarehouseCalendar.Updated", warehouseId, warehouseId,
            "Timezone: " + calendar.TimeZoneId + "; OpenDays: " + calendar.Days.Count(x => x.IsOpen), token));

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }

        if (transaction is not null) await transaction.CommitAsync(token);
        var exceptions = await LoadCalendarExceptionsAsync(warehouseId, token);
        return Map(warehouse, calendar, exceptions);
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
        context.AuditLogs.Add(await AuditAsync("WarehouseShift.Created", shift.Id, warehouseId, "Code: " + shift.Code, token));
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
        context.AuditLogs.Add(await AuditAsync("WarehouseShift.Updated", shift.Id, warehouseId, "Code: " + shift.Code + "; Active: " + shift.IsActive, token));

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

    private Task<List<AuditLog>> LoadCalendarExceptionsAsync(int warehouseId, CancellationToken token) =>
        context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "WarehouseCalendarException" &&
                        (a.WarehouseId == warehouseId || a.EntityId == warehouseId))
            .OrderByDescending(a => a.Id)
            .ToListAsync(token);

    private static bool? EvaluateDateException(IReadOnlyList<AuditLog> exceptions, DateTimeOffset localTime)
    {
        var matched = exceptions.FirstOrDefault(a =>
            MatchesExceptionWindow(a.OldValues, localTime) ||
            MatchesExceptionWindow(a.NewValues, localTime));
        if (matched is null) return null;
        if (string.Equals(matched.Action, "EMERGENCY_OPEN_OVERRIDE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(matched.Action, "HOLIDAY_CLOSURE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(matched.Action, "MAINTENANCE_WINDOW", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static bool MatchesExceptionWindow(string? rawWindow, DateTimeOffset localTime)
    {
        if (string.IsNullOrWhiteSpace(rawWindow))
            return false;

        var trimmed = rawWindow.Trim();
        var localDate = localTime.Date;
        var dateKey = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // 1. JSON payload support: {"startUtc":"...","endUtc":"..."} or {"startDate":"...","endDate":"..."}
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("startUtc", out var sUtcEl) &&
                    root.TryGetProperty("endUtc", out var eUtcEl) &&
                    DateTimeOffset.TryParse(sUtcEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sUtc) &&
                    DateTimeOffset.TryParse(eUtcEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var eUtc))
                {
                    var targetUtc = localTime.ToUniversalTime();
                    return sUtc <= targetUtc && targetUtc <= eUtc;
                }
                if (root.TryGetProperty("startDate", out var sDateEl) &&
                    root.TryGetProperty("endDate", out var eDateEl) &&
                    DateTime.TryParseExact(sDateEl.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sDate) &&
                    DateTime.TryParseExact(eDateEl.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eDate))
                {
                    return sDate.Date <= localDate && localDate <= eDate.Date;
                }
            }
            catch
            {
                // Fallback safely on malformed JSON
            }
        }

        // 2. Same-day time window: "yyyy-MM-dd HH:mm..HH:mm" or "yyyy-MM-dd HH:mm-HH:mm" or "yyyy-MM-dd HH:mm - HH:mm"
        if (trimmed.Length >= 21 &&
            DateTime.TryParseExact(trimmed[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var windowDate) &&
            (trimmed[10] == ' ' || trimmed[10] == 'T'))
        {
            var remainder = trimmed[11..].Trim();
            var timeParts = remainder.Split(["..", " - ", "-", " to "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (timeParts.Length == 2 &&
                TimeSpan.TryParseExact(timeParts[0], @"hh\:mm", CultureInfo.InvariantCulture, out var startTod) &&
                TimeSpan.TryParseExact(timeParts[1], @"hh\:mm", CultureInfo.InvariantCulture, out var endTod))
            {
                if (windowDate.Date != localDate)
                    return false;
                return IsWithinWindow(localTime.TimeOfDay, startTod, endTod);
            }
        }

        // 3. Explicit range separator: "start..end", "start - end", "start -> end", "start to end"
        string[]? rangeParts = null;
        foreach (var sep in new[] { "..", " -> ", " - ", " to " })
        {
            if (trimmed.Contains(sep, StringComparison.OrdinalIgnoreCase))
            {
                var split = trimmed.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (split.Length == 2)
                {
                    rangeParts = split;
                    break;
                }
            }
        }

        if (rangeParts is { Length: 2 })
        {
            var left = rangeParts[0];
            var right = rangeParts[1];

            // 3a. Multi-day date-only range: "yyyy-MM-dd..yyyy-MM-dd"
            if (DateTime.TryParseExact(left, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateOnly) &&
                DateTime.TryParseExact(right, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateOnly))
            {
                return startDateOnly.Date <= localDate && localDate <= endDateOnly.Date;
            }

            // 3b. DateTime / DateTimeOffset range: "2026-10-18T08:00..2026-10-18T12:00"
            if (TryParseLocalOrOffsetDateTime(left, localTime.Offset, out var startDt) &&
                TryParseLocalOrOffsetDateTime(right, localTime.Offset, out var endDt))
            {
                return startDt <= localTime && localTime <= endDt;
            }
        }

        // 4. Exact single date "yyyy-MM-dd"
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var singleDate))
        {
            return singleDate.Date == localDate;
        }

        // 5. Safe fallback for legacy free-text strings
        return trimmed.Contains(dateKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLocalOrOffsetDateTime(string input, TimeSpan defaultOffset, out DateTimeOffset result)
    {
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDt))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), defaultOffset);
            return true;
        }
        return DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);
    }
}
