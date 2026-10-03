namespace ERP.Application.Interfaces;

public interface IAccountAdminService
{
    Task<ERP.Application.DTOs.AccountSecurityDto> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task UnlockAsync(int userId, string? rowVersion, CancellationToken cancellationToken = default);
}
