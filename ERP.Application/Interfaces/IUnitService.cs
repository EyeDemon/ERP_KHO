using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IUnitService
    {
        Task<IEnumerable<UnitDto>> GetAllUnitsAsync();
        Task<UnitDto> GetUnitByIdAsync(int id);
        Task<UnitDto> CreateUnitAsync(CreateUnitDto dto, string username);
        Task UpdateUnitAsync(int id, UpdateUnitDto dto, string username);
        Task DeleteUnitAsync(int id);
    }
}
