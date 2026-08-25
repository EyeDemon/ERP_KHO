using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IWarehouseService
    {
        Task<IEnumerable<WarehouseDto>> GetAllWarehousesAsync();
        Task<WarehouseDto> GetWarehouseByIdAsync(int id);
        Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto, string username);
        Task UpdateWarehouseAsync(int id, UpdateWarehouseDto dto, string username);
        Task DeleteWarehouseAsync(int id);
    }
}
