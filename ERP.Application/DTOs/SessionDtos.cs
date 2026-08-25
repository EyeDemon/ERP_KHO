using System.Text.Json.Serialization;

namespace ERP.Application.DTOs;

public sealed class SessionContextDto
{
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

public sealed class SessionTokenResultDto
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; init; }
    [JsonIgnore]
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}

public sealed record UserSessionDto(
    Guid Id,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent);
