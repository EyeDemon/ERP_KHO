namespace ERP.Application.DTOs
{
    public class LoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int? TenantId { get; set; }
        public string? TenantCode { get; set; }
        public string? TotpCode { get; set; }
        public string? RecoveryCode { get; set; }
    }
}
