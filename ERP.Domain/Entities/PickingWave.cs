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
    public DateTime ReleasedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
