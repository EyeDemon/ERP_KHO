using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class UserWarehouse : ITenantEntity
{
    public int TenantId { get; set; } = 1;
    public int UserId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public User User { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
