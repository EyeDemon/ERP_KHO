using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IUserSessionService
{
    Task<SessionTokenResultDto> CreateAsync(User user, SessionContextDto context, CancellationToken cancellationToken = default);
    Task<SessionTokenResultDto> RefreshAsync(string refreshToken, SessionContextDto context, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, int? actorUserId, CancellationToken cancellationToken = default);
    Task LogoutAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSessionDto>> GetCurrentUserSessionsAsync(string? currentRefreshToken, CancellationToken cancellationToken = default);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task RevokeUserSessionsAsAdminAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> CleanupAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
