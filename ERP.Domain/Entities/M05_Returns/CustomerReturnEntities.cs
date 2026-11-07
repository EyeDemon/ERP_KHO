using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class CustomerReturn : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string ReturnCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int? SalesOrderId { get; set; }
    public int? ExportReceiptId { get; set; }
    public int? ShipmentId { get; set; }
    public int? QuarantineLocationId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? OrderNumber { get; set; }
    public string ReturnReason { get; set; } = string.Empty;
    public string Status { get; set; } = "QUARANTINED"; // QUARANTINED | INSPECTED | COMPLETED

    [NotMapped]
    [JsonIgnore]
    public CustomerReturnStatus StatusEnum
    {
        get => CustomerReturnEnumExtensions.ParseStatus(Status);
        set => Status = value.ToCode();
    }

    public static CustomerReturnStatus ParseStatus(string? value) => CustomerReturnEnumExtensions.ParseStatus(value);
    public static string FormatStatus(CustomerReturnStatus status) => status.ToCode();

    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<CustomerReturnLine> Lines { get; set; } = new List<CustomerReturnLine>();
}

public class CustomerReturnLine : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int CustomerReturnId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? BaseUomId { get; set; }
    public string BaseUomCode { get; set; } = "PCS";
    public string TransactionUomCode { get; set; } = "PCS";
    public decimal ConversionRateSnapshot { get; set; } = 1m;
    public int? QuarantineLocationId { get; set; }
    public int? TargetLocationId { get; set; }
    public int? RestockLocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string Condition { get; set; } = "GOOD"; // GOOD | DAMAGED_BOX | DEFECTIVE
    public string Disposition { get; set; } = "PENDING"; // PENDING | RESTOCK_AVAILABLE | REFURBISH_REPACK | RTV_VENDOR | SCRAP_DISPOSAL

    [NotMapped]
    [JsonIgnore]
    public ReturnItemCondition ConditionEnum
    {
        get => CustomerReturnEnumExtensions.ParseCondition(Condition);
        set => Condition = value.ToCode();
    }

    [NotMapped]
    [JsonIgnore]
    public ReturnDisposition DispositionEnum
    {
        get => CustomerReturnEnumExtensions.ParseDisposition(Disposition);
        set => Disposition = value.ToCode();
    }

    public static ReturnItemCondition ParseCondition(string? value) => CustomerReturnEnumExtensions.ParseCondition(value);
    public static string FormatCondition(ReturnItemCondition condition) => condition.ToCode();
    public static ReturnDisposition ParseDisposition(string? value) => CustomerReturnEnumExtensions.ParseDisposition(value);
    public static string FormatDisposition(ReturnDisposition disposition) => disposition.ToCode();

    public int? PackagingProductId { get; set; }
    public decimal PackagingQuantity { get; set; }
    public decimal PackagingConsumedQty { get; set; }
    public int? PostedLedgerTransactionId { get; set; }

    public CustomerReturn? CustomerReturn { get; set; }
}
