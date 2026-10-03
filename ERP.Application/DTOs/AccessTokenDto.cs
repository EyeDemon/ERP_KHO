namespace ERP.Application.DTOs;

public sealed record AccessTokenDto(string Token, string Jti, DateTime ExpiresAtUtc);
