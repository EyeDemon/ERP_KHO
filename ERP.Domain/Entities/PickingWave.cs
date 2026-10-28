using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class PickingWave : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public string WaveCode { get; set; } = string.Empty;
    public string PickingType { get; set; } = "STANDARD";
    public int? Priority { get; set; }
    public int ReleasedTasksCount { get; set; }
    public int TotalLinesCount { get; set; }
    public decimal TotalRequestedQuantity { get; set; }
    public int? AssignedUserId { get; set; }
    public int? ReleasedByUserId { get; set; }
    public string Status { get; set; } = "RELEASED";
    public string? Note { get; set; }
    public string? TemplateCode { get; set; }
    public string? CarrierCutoffWindow { get; set; }
    public string? SlaTierFilter { get; set; }
    public string? ZoneCode { get; set; }
    public int MaxOrdersPerClusterTote { get; set; } = 8;
    public string? ClusterToteBarcodesCsv { get; set; }
    public DateTime ReleasedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

public class PickingWaveTemplate : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string? CarrierCutoffWindow { get; set; }
    public string? SlaTierFilter { get; set; }
    public string? ZoneCode { get; set; }
    public int MaxOrdersPerClusterTote { get; set; } = 8;
    public string PickingType { get; set; } = "CLUSTER_TOTE";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
