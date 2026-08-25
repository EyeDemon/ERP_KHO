namespace ERP.Domain.Exceptions;

public sealed class DeadlockException : ConcurrencyException
{
    public DeadlockException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
