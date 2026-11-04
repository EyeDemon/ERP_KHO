using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class DockYardService
{
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
            Code = slot.Code.Trim(),
            Name = slot.Name.Trim(),
            IsActive = slot.IsActive,
            Occupied = occupied,
            OccupiedByAppointmentCode = code?.Trim(),
            RowVersion = Version(slot.RowVersion)
        };
    }

    private static DockDto MapDock(Dock x) => new()
    {
        Id = x.Id,
        WarehouseId = x.WarehouseId,
        Code = x.Code.Trim(),
        Name = x.Name.Trim(),
        SupportsInbound = x.SupportsInbound,
        SupportsOutbound = x.SupportsOutbound,
        AllowedVehicleType = x.AllowedVehicleType?.Trim(),
        IsTemperatureControlled = x.IsTemperatureControlled,
        HazardAllowed = x.HazardAllowed,
        IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion)
    };

    private static DockAppointmentDto MapAppointment(DockAppointment x, bool includeEvents = false)
    {
        var (poId, poCode, asnId, asnCode, grId, grCode) = ParseLatestInboundLink(x.Events);
        return new DockAppointmentDto
        {
            Id = x.Id,
            WarehouseId = x.WarehouseId,
            WarehouseCode = (x.Warehouse?.Code ?? string.Empty).Trim(),
            WarehouseName = (x.Warehouse?.Name ?? string.Empty).Trim(),
            Code = x.Code.Trim(),
            Direction = x.Direction,
            Status = x.Status,
            PlannedStartUtc = AsUtc(x.PlannedStartUtc),
            PlannedEndUtc = AsUtc(x.PlannedEndUtc),
            CarrierCode = x.CarrierCode?.Trim(),
            CarrierName = x.CarrierName?.Trim(),
            VehiclePlate = x.VehiclePlate?.Trim(),
            TrailerPlate = x.TrailerPlate?.Trim(),
            VehicleType = x.VehicleType?.Trim(),
            RequiresTemperatureControl = x.RequiresTemperatureControl,
            Hazardous = x.Hazardous,
            DriverName = x.DriverName?.Trim(),
            DriverPhone = x.DriverPhone?.Trim(),
            SealNumber = x.SealNumber?.Trim(),
            YardSlotId = x.YardSlotId,
            YardSlotCode = x.YardSlot?.Code?.Trim(),
            DockId = x.DockId,
            DockCode = x.Dock?.Code?.Trim(),
            ArrivedAtUtc = AsUtc(x.ArrivedAtUtc),
            CheckedInAtUtc = AsUtc(x.CheckedInAtUtc),
            DockAssignedAtUtc = AsUtc(x.DockAssignedAtUtc),
            ServiceStartedAtUtc = AsUtc(x.ServiceStartedAtUtc),
            ServiceCompletedAtUtc = AsUtc(x.ServiceCompletedAtUtc),
            CheckedOutAtUtc = AsUtc(x.CheckedOutAtUtc),
            ExceptionCode = x.ExceptionCode?.Trim(),
            Note = x.Note?.Trim(),
            PurchaseOrderId = poId,
            PurchaseOrderCode = poCode?.Trim(),
            AsnId = asnId,
            AsnCode = asnCode?.Trim(),
            ImportReceiptId = grId,
            ImportReceiptCode = grCode?.Trim(),
            RowVersion = Version(x.RowVersion),
            Events = includeEvents
                ? x.Events.OrderBy(y => y.EventAtUtc).Select(y => new DockAppointmentEventDto
                {
                    Id = y.Id,
                    EventType = y.EventType.Trim(),
                    EventAtUtc = AsUtc(y.EventAtUtc),
                    ActorUserId = y.ActorUserId,
                    DockId = y.DockId,
                    YardSlotId = y.YardSlotId,
                    Note = string.IsNullOrWhiteSpace(y.Note) ? DefaultDockEventNote(y.EventType) : y.Note.Trim()
                }).ToList()
                : []
        };
    }

    private static (int? PoId, string? PoCode, int? AsnId, string? AsnCode, int? GrId, string? GrCode) ParseLatestInboundLink(IEnumerable<DockAppointmentEvent>? events)
    {
        var linkNote = events?
            .Where(e => string.Equals(e.EventType, "INBOUND_DOC_LINK", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Note))
            .OrderByDescending(e => e.EventAtUtc)
            .ThenByDescending(e => e.Id)
            .Select(e => e.Note!)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(linkNote))
            return default;

        int? poId = null, asnId = null, grId = null;
        string? poCode = null, asnCode = null, grCode = null;
        foreach (var token in linkNote.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':', 3);
            if (parts.Length < 3) continue;
            var key = parts[0].ToUpperInvariant();
            var parsedId = int.TryParse(parts[1], out var idVal) && idVal > 0 ? idVal : (int?)null;
            var codeVal = string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2].Trim();
            switch (key)
            {
                case "PO":
                    poId = parsedId;
                    poCode = codeVal;
                    break;
                case "ASN":
                    asnId = parsedId;
                    asnCode = codeVal;
                    break;
                case "GR":
                    grId = parsedId;
                    grCode = codeVal;
                    break;
            }
        }
        return (poId, poCode, asnId, asnCode, grId, grCode);
    }

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
            if (string.Equals(value, "Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Asia/Saigon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "SE Asia Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
            }
            throw new BusinessRuleException("Timezone của kho không hợp lệ.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new BusinessRuleException("Timezone của kho không hợp lệ.");
        }
    }
}
