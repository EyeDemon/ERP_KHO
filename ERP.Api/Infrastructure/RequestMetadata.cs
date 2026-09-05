using ERP.Application.Interfaces;

namespace ERP.Api.Infrastructure;

public sealed class RequestMetadata : IRequestMetadata
{
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKeyHash { get; set; }
    public string? RequestFingerprint { get; set; }
}
