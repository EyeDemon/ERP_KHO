using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class PromotionGiftRule : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public int? TriggerProductId { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal MinTriggerQuantity { get; set; }
    public int GiftProductId { get; set; }
    public decimal GiftQuantity { get; set; } = 1m;
    public decimal GiftUnitPrice { get; set; } = 0m;
    public bool AutoSkipWhenOutOfStock { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
