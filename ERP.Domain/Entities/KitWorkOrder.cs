using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public enum KitWorkOrderType
{
    Assemble = 1,
    Disassemble = 2
}

public class KitWorkOrder : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public string WorkOrderCode { get; set; } = string.Empty;
    public int KitProductId { get; set; }
    public KitWorkOrderType WorkOrderType { get; set; } = KitWorkOrderType.Assemble;
    public string Status { get; set; } = "Draft";
    public decimal PlannedKitQty { get; set; }
    public decimal CompletedKitQty { get; set; }
    public int? TargetLocationId { get; set; }
    public int? TargetLotId { get; set; }
    public int? TargetHandlingUnitId { get; set; }
    public decimal RolledUpUnitCost { get; set; }
    public DateTime? EarliestComponentExpiryDate { get; set; }
    public int? CreatedByUserId { get; set; }
    public int? CompletedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<KitWorkOrderComponentLine> ComponentLines { get; set; } = [];
}

public class KitWorkOrderComponentLine : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int KitWorkOrderId { get; set; }
    public int ComponentProductId { get; set; }
    public decimal ComponentRatioQty { get; set; }
    public decimal RequiredBaseQty { get; set; }
    public decimal ConsumedBaseQty { get; set; }
    public int? SourceLocationId { get; set; }
    public int? SourceLotId { get; set; }
    public decimal ComponentUnitCost { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public KitWorkOrder KitWorkOrder { get; set; } = null!;
}
