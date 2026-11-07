namespace ERP.Application.Exceptions;

public class ForbiddenException : InvalidOperationException
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(string message, string errorCode) : base(message)
    {
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            Data["ErrorCode"] = errorCode;
        }
    }
    public ForbiddenException() : base("Bạn không có quyền thực hiện thao tác này.") { }
}
