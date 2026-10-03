namespace ERP.Application.Interfaces;

public interface IRequestMetadata
{
    string CorrelationId { get; set; }
    string? IdempotencyKeyHash { get; set; }
    string? RequestFingerprint { get; set; }
}
