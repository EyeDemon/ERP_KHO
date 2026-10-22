using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class SupportImpersonationSession : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string SessionCode { get; set; } = string.Empty;
    public string SupportTicketCode { get; set; } = string.Empty;
    public int GrantedByUserId { get; set; }
    public int SupportAgentUserId { get; set; }
    public bool ReadOnlyScope { get; set; } = true;
    public string Status { get; set; } = "ACTIVE"; // ACTIVE | REVOKED | EXPIRED
    public string TokenHash { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(30);
    public DateTime? RevokedAtUtc { get; set; }
}
