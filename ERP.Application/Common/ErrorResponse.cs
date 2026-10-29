namespace ERP.Application.Common;

public sealed class ErrorDetailEnvelope
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ErrorResponse
{
    public bool Success { get; set; } = false;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Code { get; set; }
    public int StatusCode { get; set; }
    public ErrorDetailEnvelope? Error { get; set; }
}
