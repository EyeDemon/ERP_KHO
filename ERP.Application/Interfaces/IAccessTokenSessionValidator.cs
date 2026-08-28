namespace ERP.Application.Interfaces;

public interface IAccessTokenSessionValidator
{
    Task<bool> IsActiveAsync(int userId, string accessTokenJti, CancellationToken cancellationToken = default);
}
