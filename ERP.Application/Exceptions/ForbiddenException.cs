namespace ERP.Application.Exceptions;

public class ForbiddenException : InvalidOperationException
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException() : base("Bạn không có quyền thực hiện thao tác này.") { }
}
