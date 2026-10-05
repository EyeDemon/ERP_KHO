using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IDockYardService
{
    Task<IReadOnlyList<DockYardWarehouseDto>> GetWarehousesAsync(CancellationToken token = default);
    Task<IReadOnlyList<DockDto>> GetDocksAsync(int warehouseId, CancellationToken token = default);
    Task<DockDto> CreateDockAsync(int warehouseId, UpsertDockDto dto, CancellationToken token = default);
    Task<DockDto> UpdateDockAsync(int warehouseId, int dockId, UpsertDockDto dto, CancellationToken token = default);

    Task<IReadOnlyList<YardSlotDto>> GetYardSlotsAsync(int warehouseId, CancellationToken token = default);
    Task<YardSlotDto> CreateYardSlotAsync(int warehouseId, UpsertYardSlotDto dto, CancellationToken token = default);
    Task<YardSlotDto> UpdateYardSlotAsync(int warehouseId, int yardSlotId, UpsertYardSlotDto dto, CancellationToken token = default);

    Task<IReadOnlyList<DockAppointmentDto>> GetAppointmentsAsync(DockAppointmentQueryDto query, CancellationToken token = default);
    Task<DockAppointmentDto> GetAppointmentAsync(int id, CancellationToken token = default);
    Task<DockAppointmentDto> CreateAppointmentAsync(UpsertDockAppointmentDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> UpdateDraftAsync(int id, UpsertDockAppointmentDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> ConfirmAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> ArriveAsync(int id, DockAppointmentArrivalDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> CheckInAsync(int id, DockAppointmentCheckInDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> AssignDockAsync(int id, DockAppointmentAssignDockDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> StartServiceAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> CompleteAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> CheckoutAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> CancelAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> MarkNoShowAsync(int id, DockAppointmentCommandDto dto, CancellationToken token = default);
    Task<DockAppointmentDto> MarkExceptionAsync(int id, DockAppointmentExceptionDto dto, CancellationToken token = default);
}
