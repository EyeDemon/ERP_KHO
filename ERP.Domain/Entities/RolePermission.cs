namespace ERP.Domain.Entities;

public sealed class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public int? GrantedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
    public User? GrantedByUser { get; set; }
}
