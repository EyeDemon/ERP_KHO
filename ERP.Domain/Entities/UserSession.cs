namespace ERP.Domain.Entities;

public class UserSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public string RefreshTokenHash { get; set; } = string.Empty;
    public Guid RefreshTokenFamilyId { get; set; }
    public string AccessTokenJti { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public User User { get; set; } = null!;
    public UserSession? ReplacedBySession { get; set; }
}
