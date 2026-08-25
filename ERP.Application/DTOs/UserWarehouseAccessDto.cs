namespace ERP.Application.DTOs;

public sealed record UserWarehouseAccessDto(int WarehouseId, string WarehouseCode, string WarehouseName, DateTime CreatedAt);

public sealed record GrantWarehouseAccessDto(int WarehouseId);
