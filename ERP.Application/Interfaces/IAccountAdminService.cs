namespace ERP.Application.Interfaces;

public interface IAccountAdminService
{
    Task UnlockAsync(int userId, CancellationToken cancellationToken = default);
}
