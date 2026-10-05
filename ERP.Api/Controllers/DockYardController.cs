using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/dock-yard")]
public sealed class DockYardController(IDockYardService service) : ControllerBase
{
    [HttpGet("warehouses")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetWarehouses(CancellationToken token) =>
        Ok(await service.GetWarehousesAsync(token));

    [HttpGet("docks")]
    [PermissionAuthorize(AppPermissions.DockRead)]
    public async Task<IActionResult> GetDocks([FromQuery] int warehouseId, CancellationToken token) =>
        Ok(await service.GetDocksAsync(warehouseId, token));

    [HttpPost("docks")]
    [IdempotentCommand("DockYard.Dock.Create")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> CreateDock([FromQuery] int warehouseId, UpsertDockDto dto, CancellationToken token) =>
        Ok(await service.CreateDockAsync(warehouseId, dto, token));

    [HttpPut("docks/{dockId:int}")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> UpdateDock([FromQuery] int warehouseId, int dockId, UpsertDockDto dto, CancellationToken token) =>
        Ok(await service.UpdateDockAsync(warehouseId, dockId, dto, token));

    [HttpGet("yard-slots")]
    [PermissionAuthorize(AppPermissions.YardRead)]
    public async Task<IActionResult> GetYardSlots([FromQuery] int warehouseId, CancellationToken token) =>
        Ok(await service.GetYardSlotsAsync(warehouseId, token));

    [HttpPost("yard-slots")]
    [IdempotentCommand("DockYard.YardSlot.Create")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> CreateYardSlot([FromQuery] int warehouseId, UpsertYardSlotDto dto, CancellationToken token) =>
        Ok(await service.CreateYardSlotAsync(warehouseId, dto, token));

    [HttpPut("yard-slots/{yardSlotId:int}")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> UpdateYardSlot([FromQuery] int warehouseId, int yardSlotId, UpsertYardSlotDto dto, CancellationToken token) =>
        Ok(await service.UpdateYardSlotAsync(warehouseId, yardSlotId, dto, token));

    [HttpGet("appointments")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetAppointments([FromQuery] DockAppointmentQueryDto query, CancellationToken token) =>
        Ok(await service.GetAppointmentsAsync(query, token));

    [HttpGet("appointments/{id:int}")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetAppointment(int id, CancellationToken token) =>
        Ok(await service.GetAppointmentAsync(id, token));

    [HttpPost("appointments")]
    [IdempotentCommand("DockYard.Appointment.Create")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> CreateAppointment(UpsertDockAppointmentDto dto, CancellationToken token) =>
        Ok(await service.CreateAppointmentAsync(dto, token));

    [HttpPut("appointments/{id:int}")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> UpdateAppointment(int id, UpsertDockAppointmentDto dto, CancellationToken token) =>
        Ok(await service.UpdateDraftAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/confirm")]
    [IdempotentCommand("DockYard.Appointment.Confirm")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Confirm(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.ConfirmAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/arrive")]
    [IdempotentCommand("DockYard.Appointment.Arrive")]
    [PermissionAuthorize(AppPermissions.YardCheckIn)]
    public async Task<IActionResult> Arrive(int id, DockAppointmentArrivalDto dto, CancellationToken token) =>
        Ok(await service.ArriveAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/check-in")]
    [IdempotentCommand("DockYard.Appointment.CheckIn")]
    [PermissionAuthorize(AppPermissions.YardCheckIn)]
    public async Task<IActionResult> CheckIn(int id, DockAppointmentCheckInDto dto, CancellationToken token) =>
        Ok(await service.CheckInAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/assign-dock")]
    [IdempotentCommand("DockYard.Appointment.AssignDock")]
    [PermissionAuthorize(AppPermissions.YardAssignDock)]
    public async Task<IActionResult> AssignDock(int id, DockAppointmentAssignDockDto dto, CancellationToken token) =>
        Ok(await service.AssignDockAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/start-service")]
    [IdempotentCommand("DockYard.Appointment.StartService")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> StartService(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.StartServiceAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/complete")]
    [IdempotentCommand("DockYard.Appointment.Complete")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Complete(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CompleteAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/checkout")]
    [IdempotentCommand("DockYard.Appointment.Checkout")]
    [PermissionAuthorize(AppPermissions.YardCheckout)]
    public async Task<IActionResult> Checkout(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CheckoutAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/cancel")]
    [IdempotentCommand("DockYard.Appointment.Cancel")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Cancel(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CancelAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/no-show")]
    [IdempotentCommand("DockYard.Appointment.NoShow")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> MarkNoShow(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.MarkNoShowAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/exception")]
    [IdempotentCommand("DockYard.Appointment.Exception")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> MarkException(int id, DockAppointmentExceptionDto dto, CancellationToken token) =>
        Ok(await service.MarkExceptionAsync(id, dto, token));
}
