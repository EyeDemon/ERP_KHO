namespace ERP.Application.Interfaces;

public interface ICurrentUser
{
    int UserId { get; }
    bool IsAuthenticated { get; }
    bool IsGlobalAdmin { get; }
    string Role => string.Empty;
}
