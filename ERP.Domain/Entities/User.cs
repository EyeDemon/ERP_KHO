namespace ERP.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
    public ICollection<UserWarehouse> WarehouseAccesses { get; set; } = new List<UserWarehouse>();
    public ICollection<UserWarehouse> GrantedWarehouseAccesses { get; set; } = new List<UserWarehouse>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}
