namespace ERP.Application.Common;

public sealed class AuthSecurityOptions
{
    public int MaxFailedAttempts { get; init; }
    public int LockoutMinutes { get; init; }
}
