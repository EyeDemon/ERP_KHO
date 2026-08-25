using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface ITokenService
{
    DTOs.AccessTokenDto GenerateAccessToken(User user);
    string GenerateToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
    bool FixedTimeHashEquals(string leftHash, string rightHash);
}
