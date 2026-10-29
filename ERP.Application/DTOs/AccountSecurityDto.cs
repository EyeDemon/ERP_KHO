namespace ERP.Application.DTOs;

public sealed record AccountSecurityDto(
    bool IsActive,
    bool IsLocked,
    DateTime? LockoutEnd,
    string RowVersion,
    int UserId = 0,
    string? Username = null,
    string? RoleName = null,
    int FailedLoginCount = 0,
    long SecurityStateVersion = 1,
    int ActiveSessionCount = 0);
public sealed record AccountSecurityCommandDto(string? RowVersion);
