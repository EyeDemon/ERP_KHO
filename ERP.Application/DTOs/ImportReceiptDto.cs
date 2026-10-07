using ERP.Domain.Enums;

namespace ERP.Application.DTOs
{
    public class ImportReceiptDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public int? ApprovedBy { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int? SupplierId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string? PurchaseOrderCode { get; set; }
        public int? AsnId { get; set; }
        public string? AsnCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public bool RequiresQc { get; set; }
        public List<ImportReceiptDetailDto> Details { get; set; } = new();
    }

    public class ImportReceiptDetailDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public int? AsnLineId { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? UnitName { get; set; }
        public int OperationUnitId { get; set; }
        public string OperationUnitCode { get; set; } = string.Empty;
        public int OperationUnitDecimalPlaces { get; set; }
        public int BaseUnitId { get; set; }
        public string BaseUnitCode { get; set; } = string.Empty;
        public int BaseUnitDecimalPlaces { get; set; }
        public decimal ConversionFactor { get; set; }
        public int ConversionVersion { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal DamagedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public decimal PostedQuantity { get; set; }
        public decimal BaseExpectedQuantity { get; set; }
        public decimal BaseReceivedQuantity { get; set; }
        public decimal BaseAcceptedQuantity { get; set; }
        public decimal BaseDamagedQuantity { get; set; }
        public decimal BaseRejectedQuantity { get; set; }
        public decimal BasePostedQuantity { get; set; }
        public string TrackingType { get; set; } = "None";
        public bool ExpiryControl { get; set; }
        public int? ShelfLifeDays { get; set; }
        public decimal ObservedQuantity { get; set; }
        public decimal DoorRejectedQuantity { get; set; }
        public decimal FinalReceivedQuantity { get; set; }
        public decimal BaseFinalReceivedQuantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Note { get; set; }
        public bool RequiresQc { get; set; }
        public string QcState { get; set; } = string.Empty;
        public int? QcPolicyId { get; set; }
        public int? QcPolicyVersion { get; set; }
        public string? QcPolicySource { get; set; }
        public DateTime? QcPolicyEffectiveAtUtc { get; set; }
        public string? QcDispositionReasonCode { get; set; }
        public string? QcDispositionNote { get; set; }
    }
    
    public class CreateImportReceiptDto
    {
        public string Code { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string? Note { get; set; }
        public int? SupplierId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public int? AsnId { get; set; }
        public List<CreateImportReceiptDetailDto> Details { get; set; } = new();
    }

    public class CreateImportReceiptDetailDto
    {
        public int ProductId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public int? AsnLineId { get; set; }
        public int OperationUnitId { get; set; }
        public decimal ExpectedQuantity { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal Quantity { set => ExpectedQuantity = value; }
        public decimal UnitPrice { get; set; }
        public string? Note { get; set; }
    }

    public class ReceiveImportReceiptDto
    {
        public List<ReceiveImportReceiptLineDto> Lines { get; set; } = new();
    }

    public class ReceiveImportReceiptLineDto
    {
        public int LineId { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal DamagedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
    }

    public sealed class SetImportReceiptInventoryIdentitiesDto
    {
        public List<SetImportReceiptInventoryIdentityLineDto> Lines { get; set; } = [];
    }

    public sealed class SetImportReceiptInventoryIdentityLineDto
    {
        public int LineId { get; set; }
        public string TargetStatus { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? SerialNumber { get; set; }
    }

    public sealed class ImportReceiptInventoryIdentityDto
    {
        public int Id { get; set; }
        public int LineId { get; set; }
        public int ProductId { get; set; }
        public string TargetStatus { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? SerialNumber { get; set; }
    }

    public class RecordQcDispositionDto
    {
        public List<RecordQcDispositionLineDto> Lines { get; set; } = new();
    }

    public class RecordQcDispositionLineDto
    {
        public int LineId { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal DamagedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public string? ReasonCode { get; set; }
        public string? Note { get; set; }
    }
}
