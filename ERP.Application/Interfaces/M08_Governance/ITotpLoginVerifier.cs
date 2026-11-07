using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface ITotpLoginVerifier
{
    Task<bool> VerifyLoginSecondFactorAsync(
        User user,
        string? totpCode,
        string? recoveryCode,
        CancellationToken cancellationToken = default);
}
