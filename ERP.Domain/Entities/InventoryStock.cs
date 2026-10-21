using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class InventoryStock : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int? HandlingUnitId { get; set; }
    public int? OwnerId { get; set; }
    public InventoryStatus Status { get; set; } = InventoryStatus.Available;
    
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public decimal Quantity { get; set; }

    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal LockedQuantity { get; set; }
    public decimal MovingAverageCost { get; set; }
    public byte[]? GrainHash { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public static byte[] ComputeCanonicalGrainHash(
        int tenantId,
        int warehouseId,
        int? locationId,
        int productId,
        int? lotId,
        int? serialId,
        int? handlingUnitId,
        InventoryStatus status,
        int? ownerId)
    {
        var canonicalKey = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{tenantId}|{warehouseId}|{locationId ?? 0}|{productId}|{lotId ?? 0}|{serialId ?? 0}|{handlingUnitId ?? 0}|{(int)status}|{ownerId ?? 0}");
        return System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalKey));
    }

    // Navigation
    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
    public InventoryLot? Lot { get; set; }
    public InventorySerial? Serial { get; set; }
    public InventoryStatusDefinition StatusDefinition { get; set; } = null!;
}
