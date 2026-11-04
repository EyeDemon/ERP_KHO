using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class MarketplaceShop : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string ChannelCode { get; set; } = string.Empty; // SHOPEE | TIKTOK | LAZADA | HARAVAN | SAPO
    public string ShopCode { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public int DefaultWarehouseId { get; set; }
    public decimal ReserveRatioPct { get; set; } = 100m;
    public decimal SafetyBufferQty { get; set; } = 0m;
    public decimal LowStockThreshold { get; set; } = 5m;
    public bool IsPrimaryShop { get; set; }
    public string? EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTime? TokenExpiresAtUtc { get; set; }
    public string? WebhookSecret { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Warehouse? DefaultWarehouse { get; set; }
    public ICollection<ChannelSkuMapping> SkuMappings { get; set; } = new List<ChannelSkuMapping>();

    public decimal CalculateChannelPushQty(decimal atpBaseQty, decimal conversionFactor = 1m)
    {
        var effectiveFactor = conversionFactor <= 0m ? 1m : conversionFactor;
        var atp = Math.Max(0m, atpBaseQty);
        if (atp <= 0m)
        {
            return 0m;
        }

        if (atp <= LowStockThreshold && !IsPrimaryShop)
        {
            return 0m;
        }

        var bufferedQty = Math.Max(0m, (atp - SafetyBufferQty) * (ReserveRatioPct / 100m));
        return Math.Max(0m, Math.Floor(bufferedQty / effectiveFactor));
    }
}

public class ChannelSkuMapping : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ShopId { get; set; }
    public string ChannelItemId { get; set; } = string.Empty;
    public string? ChannelVariationId { get; set; }
    public string ChannelSkuCode { get; set; } = string.Empty;
    public int InternalProductId { get; set; }
    public decimal ConversionFactor { get; set; } = 1m;
    public decimal LastPushedQty { get; set; }
    public DateTime? LastPushedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public MarketplaceShop? Shop { get; set; }
    public Product? InternalProduct { get; set; }
}
