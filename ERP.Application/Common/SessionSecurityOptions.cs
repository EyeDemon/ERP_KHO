namespace ERP.Application.Common;

public sealed class SessionSecurityOptions
{
    public int RefreshTokenDays { get; set; } = 14;
    public int RetentionDays { get; set; } = 30;
    public string SameSite { get; set; } = "Strict";
}
