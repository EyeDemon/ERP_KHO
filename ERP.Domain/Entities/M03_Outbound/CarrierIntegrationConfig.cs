using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class CarrierIntegrationConfig : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string ShopId { get; set; } = string.Empty;
    public string? EncryptedApiToken { get; set; }
    public bool IsSandbox { get; set; } = true;
    public string WebhookSecretHash { get; set; } = string.Empty;
    public string? SortingHubPrefix { get; set; }
    public decimal BaseShippingFeeVnd { get; set; } = 22000m;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
