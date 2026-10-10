namespace ERP.Application.DTOs;

public class PurchaseOrderListDto
{
    public int Id { get; set; }
    public string ExternalPoId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public decimal BaseOrderedQuantity { get; set; }
}

public sealed class PurchaseOrderDto : PurchaseOrderListDto
{
    public string SourceSystem { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? ExternalVersion { get; set; }
    public string? RowVersion { get; set; }
    public IReadOnlyList<PurchaseOrderLineDto> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLineDto
{
    public int Id { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public string OperationUnitCode { get; set; } = string.Empty;
    public decimal BaseOrderedQuantity { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal AllowedOverReceiptPct { get; set; }
    public decimal AllowedUnderReceiptPct { get; set; }
}

public class CreatePurchaseOrderDto
{
    public string ExternalPoId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = "MANUAL";
    public string Code { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Currency { get; set; }
    public string? ExternalVersion { get; set; }
    public List<CreatePurchaseOrderLineDto> Lines { get; set; } = [];
}

public sealed class UpdatePurchaseOrderDto : CreatePurchaseOrderDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreatePurchaseOrderLineDto
{
    public string ExternalLineId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int OperationUnitId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal AllowedOverReceiptPct { get; set; }
    public decimal AllowedUnderReceiptPct { get; set; }
}

public sealed class InboundStateCommandDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public class AsnListDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int? PurchaseOrderId { get; set; }
    public string? PurchaseOrderCode { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpectedArrivalAtUtc { get; set; }
    public decimal BaseExpectedQuantity { get; set; }
}

public sealed class AsnDto : AsnListDto
{
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
    public IReadOnlyList<AsnLineDto> Lines { get; set; } = [];
}

public sealed class AsnLineDto
{
    public int Id { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public string OperationUnitCode { get; set; } = string.Empty;
    public decimal BaseExpectedQuantity { get; set; }
    public string BaseUnitCode { get; set; } = string.Empty;
}

public class CreateAsnDto
{
    public string Code { get; set; } = string.Empty;
    public int? PurchaseOrderId { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? ExpectedArrivalAtUtc { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Note { get; set; }
    public List<CreateAsnLineDto> Lines { get; set; } = [];
}

public sealed class UpdateAsnDto : CreateAsnDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateAsnLineDto
{
    public int? PurchaseOrderLineId { get; set; }
    public int ProductId { get; set; }
    public int OperationUnitId { get; set; }
    public decimal ExpectedQuantity { get; set; }
}
