using System.ComponentModel.DataAnnotations;

namespace ERP.Domain.Entities;

public sealed class BusinessPartner
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSupplier { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<ImportReceipt> ImportReceipts { get; set; } = [];
    public ICollection<ExportReceipt> ExportReceipts { get; set; } = [];
}
