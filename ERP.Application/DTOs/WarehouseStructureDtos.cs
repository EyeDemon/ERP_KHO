namespace ERP.Application.DTOs;

public sealed class WarehouseStructureDto
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public IReadOnlyList<WarehouseZoneDto> Zones { get; set; } = [];
    public IReadOnlyList<WarehouseLocationDto> SystemLocations { get; set; } = [];
}

public sealed class WarehouseZoneDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ZoneType { get; set; } = "";
    public int? PickPriority { get; set; }
    public int? PutawayPriority { get; set; }
    public bool IsActive { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<WarehouseAisleDto> Aisles { get; set; } = [];
    public IReadOnlyList<WarehouseLocationDto> Locations { get; set; } = [];
}

public sealed class WarehouseAisleDto
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<WarehouseRackDto> Racks { get; set; } = [];
}

public sealed class WarehouseRackDto
{
    public int Id { get; set; }
    public int AisleId { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<WarehouseRackLevelDto> Levels { get; set; } = [];
}

public sealed class WarehouseRackLevelDto
{
    public int Id { get; set; }
    public int RackId { get; set; }
    public int LevelNo { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<WarehouseLocationDto> Locations { get; set; } = [];
}

public sealed class CreateWarehouseZoneDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ZoneType { get; set; } = "STORAGE";
    public int? PickPriority { get; set; }
    public int? PutawayPriority { get; set; }
}

public sealed class UpdateWarehouseZoneDto
{
    public string Name { get; set; } = "";
    public string ZoneType { get; set; } = "STORAGE";
    public int? PickPriority { get; set; }
    public int? PutawayPriority { get; set; }
    public bool IsActive { get; set; } = true;
    public string RowVersion { get; set; } = "";
}

public sealed class CreateWarehouseAisleDto
{
    public string Code { get; set; } = "";
    public string? Name { get; set; }
}

public sealed class UpdateWarehouseAisleDto
{
    public string? Name { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class CreateWarehouseRackDto
{
    public string Code { get; set; } = "";
    public string? Name { get; set; }
}

public sealed class UpdateWarehouseRackDto
{
    public string? Name { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class CreateWarehouseRackLevelDto
{
    public int LevelNo { get; set; }
}
