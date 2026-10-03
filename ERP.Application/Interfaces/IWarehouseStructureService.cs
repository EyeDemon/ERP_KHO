using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IWarehouseStructureService
{
    Task<WarehouseStructureDto> GetAsync(int warehouseId, CancellationToken token = default);
    Task<WarehouseZoneDto> CreateZoneAsync(int warehouseId, CreateWarehouseZoneDto dto, CancellationToken token = default);
    Task<WarehouseZoneDto> UpdateZoneAsync(int warehouseId, int zoneId, UpdateWarehouseZoneDto dto, CancellationToken token = default);
    Task<WarehouseAisleDto> CreateAisleAsync(int warehouseId, int zoneId, CreateWarehouseAisleDto dto, CancellationToken token = default);
    Task<WarehouseAisleDto> UpdateAisleAsync(int warehouseId, int zoneId, int aisleId, UpdateWarehouseAisleDto dto, CancellationToken token = default);
    Task<WarehouseRackDto> CreateRackAsync(int warehouseId, int aisleId, CreateWarehouseRackDto dto, CancellationToken token = default);
    Task<WarehouseRackDto> UpdateRackAsync(int warehouseId, int aisleId, int rackId, UpdateWarehouseRackDto dto, CancellationToken token = default);
    Task<WarehouseRackLevelDto> CreateLevelAsync(int warehouseId, int rackId, CreateWarehouseRackLevelDto dto, CancellationToken token = default);
}
