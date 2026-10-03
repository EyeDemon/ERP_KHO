using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request, SessionContextDto context, CancellationToken cancellationToken = default) =>
        LoginAsync(request, cancellationToken);
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
}
