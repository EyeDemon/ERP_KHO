namespace ERP.Application.DTOs;

public sealed record UserWarehouseAccessDto(int WarehouseId, string WarehouseCode, string WarehouseName, DateTime CreatedAt);

public sealed record UserWarehouseAccessSetDto(IReadOnlyList<UserWarehouseAccessDto> Memberships,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RowVersion);

public sealed record GrantWarehouseAccessDto(int WarehouseId, string? RowVersion = null);
public sealed record RevokeWarehouseAccessDto(string? RowVersion = null);
