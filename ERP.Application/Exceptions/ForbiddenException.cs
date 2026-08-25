namespace ERP.Application.Exceptions;

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException() : base("Bạn không có quyền thực hiện thao tác này.") { }
}
