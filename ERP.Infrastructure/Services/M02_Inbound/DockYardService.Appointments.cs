using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class DockYardService
{
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
        var inboundLinkNote = await BuildInboundLinkNoteIfRequestedAsync(appointment.WarehouseId, appointment.Direction, dto.PurchaseOrderId, dto.AsnId, dto.ImportReceiptId, token);
        context.DockAppointments.Add(appointment);
        await SaveAsync(token);
        AddEvent(appointment, "Created", dto.Note);
        if (inboundLinkNote is not null) AddEvent(appointment, "INBOUND_DOC_LINK", inboundLinkNote);
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
        var inboundLinkNote = await BuildInboundLinkNoteIfRequestedAsync(appointment.WarehouseId, appointment.Direction, dto.PurchaseOrderId, dto.AsnId, dto.ImportReceiptId, token);
        Touch(appointment);
        AddEvent(appointment, "DraftUpdated", dto.Note);
        if (inboundLinkNote is not null) AddEvent(appointment, "INBOUND_DOC_LINK", inboundLinkNote);
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
             x.Status == DockAppointmentStatus.Exception ||
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
        var canCheckout = appointment.Status == DockAppointmentStatus.Completed
            || (appointment.Status == DockAppointmentStatus.Exception && appointment.CheckedInAtUtc.HasValue);
        if (!canCheckout)
            throw new ConcurrencyException("Chỉ appointment đã hoàn thành hoặc exception sau check-in mới được checkout.");
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

    public async Task<DockAppointmentDto> LinkInboundDocumentsAsync(int id, DockAppointmentLinkInboundDto dto, CancellationToken token = default)
    {
        var appointment = await GetForMutationAsync(id, token);
        if (appointment.Status is DockAppointmentStatus.Cancelled or DockAppointmentStatus.NoShow)
            throw new ConcurrencyException("Không thể liên kết chứng từ cho appointment đã hủy hoặc không đến.");
        ApplyVersion(appointment, dto.RowVersion);
        if (!dto.PurchaseOrderId.HasValue && !dto.AsnId.HasValue && !dto.ImportReceiptId.HasValue)
            throw new BusinessRuleException("Cần chọn ít nhất một chứng từ PO, ASN hoặc Phiếu nhập để liên kết.");

        var inboundLinkNote = await BuildInboundLinkNoteIfRequestedAsync(
            appointment.WarehouseId,
            appointment.Direction,
            dto.PurchaseOrderId,
            dto.AsnId,
            dto.ImportReceiptId,
            token);

        Touch(appointment);
        AddEvent(appointment, "INBOUND_DOC_LINK", inboundLinkNote);
        AddAudit("DockAppointment.InboundLinked", "DockAppointment", appointment.Id, appointment.WarehouseId, inboundLinkNote);
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
}
