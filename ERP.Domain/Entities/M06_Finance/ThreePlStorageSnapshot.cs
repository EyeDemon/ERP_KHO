using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ThreePlStorageSnapshot : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public int? OwnerId { get; set; }
    public string OwnerCode { get; set; } = "OWN-DEFAULT";
    public string OwnerName { get; set; } = string.Empty;
    public string SnapshotCode { get; set; } = string.Empty;
    public DateTime SnapshotDateUtc { get; set; } = DateTime.UtcNow.Date;
    public int DistinctSkuCount { get; set; }
    public int OccupiedBinCount { get; set; }
    public decimal TotalOnHandQty { get; set; }
    public decimal TotalVolumeCbm { get; set; }
    public decimal PalletEquivalentCount { get; set; }
    public decimal DailyPalletRateVnd { get; set; }
    public decimal DailyCbmRateVnd { get; set; }
    public decimal DailyBinRateVnd { get; set; }
    public decimal EstimatedDailyStorageFeeVnd { get; set; }
    public decimal EstimatedMonthlyStorageFeeVnd { get; set; }
    public int? CapturedByUserId { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
