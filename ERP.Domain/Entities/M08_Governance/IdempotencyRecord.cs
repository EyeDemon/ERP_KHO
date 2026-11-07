using System.ComponentModel.DataAnnotations.Schema;
using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public enum IdempotencyStatus { Processing = 0, Completed = 1 }

public class IdempotencyRecord : ITenantEntity
{
    public long Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int UserId { get; set; }
    public string CommandScope { get; set; } = string.Empty;
    [NotMapped]
    public string ScopeKey => $"{TenantId}:{UserId}:{CommandScope}";
    public string KeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public int? SourceWarehouseId { get; set; }
    public int? DestinationWarehouseId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public User User { get; set; } = null!;
}
