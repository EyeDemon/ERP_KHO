using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class DockYardService
{
    private async Task<string?> BuildInboundLinkNoteIfRequestedAsync(
        int warehouseId,
        DockAppointmentDirection direction,
        int? purchaseOrderId,
        int? asnId,
        int? importReceiptId,
        CancellationToken token)
    {
        if (!purchaseOrderId.HasValue && !asnId.HasValue && !importReceiptId.HasValue)
            return null;
        if (direction != DockAppointmentDirection.Inbound)
            throw new BusinessRuleException("Chỉ appointment luồng Nhập kho (Inbound) mới được liên kết PO / ASN / Phiếu nhập.");

        PurchaseOrder? po = null;
        Asn? asn = null;
        ImportReceipt? receipt = null;

        if (purchaseOrderId.HasValue)
        {
            po = await context.PurchaseOrders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == purchaseOrderId.Value && x.WarehouseId == warehouseId, token)
                ?? throw new BusinessRuleException("Purchase Order không tồn tại hoặc không thuộc kho của appointment.");
        }
        if (asnId.HasValue)
        {
            asn = await context.Asns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == asnId.Value && x.WarehouseId == warehouseId, token)
                ?? throw new BusinessRuleException("ASN không tồn tại hoặc không thuộc kho của appointment.");
            if (po is not null && asn.PurchaseOrderId.HasValue && asn.PurchaseOrderId.Value != po.Id)
                throw new BusinessRuleException("ASN không thuộc Purchase Order đã chọn.");
            if (po is null && asn.PurchaseOrderId.HasValue)
                po = await context.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == asn.PurchaseOrderId.Value, token);
        }
        if (importReceiptId.HasValue)
        {
            receipt = await context.ImportReceipts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == importReceiptId.Value && x.WarehouseId == warehouseId, token)
                ?? throw new BusinessRuleException("Phiếu nhập không tồn tại hoặc không thuộc kho của appointment.");
            if (po is not null && receipt.PurchaseOrderId.HasValue && receipt.PurchaseOrderId.Value != po.Id)
                throw new BusinessRuleException("Phiếu nhập không thuộc Purchase Order đã chọn.");
            if (asn is not null && receipt.AsnId.HasValue && receipt.AsnId.Value != asn.Id)
                throw new BusinessRuleException("Phiếu nhập không thuộc ASN đã chọn.");
            if (asn is null && receipt.AsnId.HasValue)
                asn = await context.Asns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.AsnId.Value, token);
            if (po is null && receipt.PurchaseOrderId.HasValue)
                po = await context.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.PurchaseOrderId.Value, token);
        }

        return $"PO:{po?.Id}:{po?.Code?.Trim()}|ASN:{asn?.Id}:{asn?.Code?.Trim()}|GR:{receipt?.Id}:{receipt?.Code?.Trim()}";
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

        var dateKey = localStart.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var calendarExceptions = await context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "WarehouseCalendarException" &&
                        (a.WarehouseId == appointment.WarehouseId || a.EntityId == appointment.WarehouseId))
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .ToListAsync(token);
        var matchedException = calendarExceptions.FirstOrDefault(a =>
            (a.OldValues != null && a.OldValues.Contains(dateKey, StringComparison.OrdinalIgnoreCase)) ||
            (a.NewValues != null && a.NewValues.Contains(dateKey, StringComparison.OrdinalIgnoreCase)));
        if (matchedException is not null)
        {
            if (string.Equals(matchedException.Action, "EMERGENCY_OPEN_OVERRIDE", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(matchedException.Action, "HOLIDAY_CLOSURE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(matchedException.Action, "MAINTENANCE_WINDOW", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("Appointment nằm trong ngày kho đóng cửa.");
        }

        var currentDayIdx = (int)localStart.DayOfWeek;
        var prevDayIdx = (currentDayIdx + 6) % 7;
        var today = calendar.Days.SingleOrDefault(x => x.DayOfWeek == currentDayIdx);
        var prevDay = calendar.Days.SingleOrDefault(x => x.DayOfWeek == prevDayIdx);

        WarehouseCalendarDay? matchedDay = null;
        var baseDate = localStart.Date;

        if (prevDay?.IsOpen == true &&
            prevDay.OpensAtLocal.HasValue &&
            prevDay.ClosesAtLocal.HasValue &&
            prevDay.OpensAtLocal.Value > prevDay.ClosesAtLocal.Value &&
            localStart.TimeOfDay < prevDay.ClosesAtLocal.Value)
        {
            matchedDay = prevDay;
            baseDate = localStart.Date.AddDays(-1);
        }
        else if (today?.IsOpen == true &&
                 today.OpensAtLocal.HasValue &&
                 today.ClosesAtLocal.HasValue)
        {
            matchedDay = today;
            baseDate = localStart.Date;
        }

        if (matchedDay is null || !matchedDay.OpensAtLocal.HasValue || !matchedDay.ClosesAtLocal.HasValue)
            throw new BusinessRuleException("Appointment nằm trong ngày kho đóng cửa.");

        var open = baseDate + matchedDay.OpensAtLocal.Value;
        var close = baseDate + matchedDay.ClosesAtLocal.Value;
        if (matchedDay.OpensAtLocal.Value > matchedDay.ClosesAtLocal.Value)
            close = close.AddDays(1);
        if (localStart.DateTime < open || localEnd.DateTime > close)
            throw new BusinessRuleException("Appointment nằm ngoài giờ vận hành của kho.");

        var cutoffValue = appointment.Direction == DockAppointmentDirection.Inbound
            ? matchedDay.InboundCutoffLocal
            : matchedDay.OutboundCutoffLocal;
        if (cutoffValue.HasValue)
        {
            var cutoff = baseDate + cutoffValue.Value;
            if (matchedDay.OpensAtLocal.Value > matchedDay.ClosesAtLocal.Value &&
                cutoffValue.Value < matchedDay.OpensAtLocal.Value)
                cutoff = cutoff.AddDays(1);
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
            Note = Optional(note) ?? DefaultDockEventNote(eventType)
        });
    }

    private static string? DefaultDockEventNote(string? eventType) => eventType?.Trim() switch
    {
        "Created" => "Đã khởi tạo lịch hẹn tại cổng kho.",
        "DraftUpdated" => "Đã cập nhật thông tin lịch hẹn nháp.",
        "Confirmed" => "Đã xác nhận khung giờ lịch hẹn.",
        "Arrived" => "Phương tiện đã đến cổng kho.",
        "CheckedIn" => "Đã làm thủ tục check-in tại cổng.",
        "DockAssigned" => "Đã phân bổ cửa xuất nhập (Dock).",
        "DockReassigned" => "Đã điều chuyển cửa xuất nhập (Dock).",
        "ServiceStarted" => "Bắt đầu bốc dỡ hàng hóa tại Dock.",
        "ServiceCompleted" => "Hoàn tất bốc dỡ hàng hóa tại Dock.",
        "CheckedOut" => "Phương tiện đã rời bãi (Check-out).",
        "Cancelled" => "Đã hủy lịch hẹn.",
        "NoShow" => "Phương tiện không đến theo lịch hẹn (No-show).",
        "Exception" => "Ghi nhận sự cố / ngoại lệ vận hành.",
        _ => null
    };

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
}
