namespace ERP.Application.DTOs;

public sealed record AccountSecurityDto(bool IsActive, bool IsLocked, DateTime? LockoutEnd, string RowVersion);
public sealed record AccountSecurityCommandDto(string? RowVersion);
