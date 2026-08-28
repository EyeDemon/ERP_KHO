namespace ERP.Application.Exceptions;

public sealed class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message) : base(message) { }
}
