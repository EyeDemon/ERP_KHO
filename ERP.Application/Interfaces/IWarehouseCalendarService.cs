using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IWarehouseCalendarService
{
    Task<WarehouseCalendarDto> GetAsync(int warehouseId, CancellationToken token = default);
    Task<WarehouseCalendarDto> UpdateCalendarAsync(int warehouseId, UpdateWarehouseCalendarDto dto, CancellationToken token = default);
    Task<WarehouseShiftDto> CreateShiftAsync(int warehouseId, UpsertWarehouseShiftDto dto, CancellationToken token = default);
    Task<WarehouseShiftDto> UpdateShiftAsync(int warehouseId, int shiftId, UpsertWarehouseShiftDto dto, CancellationToken token = default);
}
