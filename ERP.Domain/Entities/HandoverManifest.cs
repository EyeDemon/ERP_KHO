using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class HandoverManifest : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string ManifestCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN | SEALED
    public int TotalPackages { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? SealedBy { get; set; }
    public DateTime? SealedAtUtc { get; set; }

    public Warehouse? Warehouse { get; set; }
    public ICollection<HandoverManifestLine> Lines { get; set; } = new List<HandoverManifestLine>();
}

public class HandoverManifestLine : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ManifestId { get; set; }
    public int ExportReceiptId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string? MarketplaceOrderId { get; set; }
    public int ScannedBy { get; set; }
    public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    public HandoverManifest? Manifest { get; set; }
    public ExportReceipt? ExportReceipt { get; set; }
}
