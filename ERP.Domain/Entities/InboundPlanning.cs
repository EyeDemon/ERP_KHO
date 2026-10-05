using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class PurchaseOrder
{
    public int Id { get; set; }
    public string ExternalPoId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = "MANUAL";
    public string Code { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Currency { get; set; }
    public string? ExternalVersion { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public BusinessPartner Supplier { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];
    public ICollection<Asn> Asns { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public int OperationUnitId { get; set; }
    public string OperationUnitCodeSnapshot { get; set; } = string.Empty;
    public int OperationUnitDecimalPlaces { get; set; }
    public int BaseUnitId { get; set; }
    public string BaseUnitCodeSnapshot { get; set; } = string.Empty;
    public int BaseUnitDecimalPlaces { get; set; }
    public decimal ConversionFactorSnapshot { get; set; } = 1m;
    public int ConversionVersionSnapshot { get; set; } = 1;
    public decimal BaseOrderedQuantity { get; set; }
    public decimal AllowedOverReceiptPct { get; set; }
    public decimal AllowedUnderReceiptPct { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ICollection<AsnLine> AsnLines { get; set; } = [];
}

public sealed class Asn
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int? PurchaseOrderId { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? ExpectedArrivalAtUtc { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Note { get; set; }
    public AsnStatus Status { get; set; } = AsnStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public PurchaseOrder? PurchaseOrder { get; set; }
    public BusinessPartner Supplier { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<AsnLine> Lines { get; set; } = [];
}

public sealed class AsnLine
{
    public int Id { get; set; }
    public int AsnId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public int OperationUnitId { get; set; }
    public string OperationUnitCodeSnapshot { get; set; } = string.Empty;
    public int OperationUnitDecimalPlaces { get; set; }
    public int BaseUnitId { get; set; }
    public string BaseUnitCodeSnapshot { get; set; } = string.Empty;
    public int BaseUnitDecimalPlaces { get; set; }
    public decimal ConversionFactorSnapshot { get; set; } = 1m;
    public int ConversionVersionSnapshot { get; set; } = 1;
    public decimal BaseExpectedQuantity { get; set; }
    public Asn Asn { get; set; } = null!;
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Product Product { get; set; } = null!;
}
