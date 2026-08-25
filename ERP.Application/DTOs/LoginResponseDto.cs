namespace ERP.Application.DTOs
{
    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAtUtc { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string RefreshToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime RefreshTokenExpiresAtUtc { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
