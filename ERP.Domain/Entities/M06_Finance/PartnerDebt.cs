using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class PartnerDebtInvoice : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public int PartnerId { get; set; }
    public PartnerDebtType DebtType { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public decimal SubTotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DueDateUtc { get; set; } = DateTime.UtcNow;
    public PartnerDebtInvoiceStatus Status { get; set; } = PartnerDebtInvoiceStatus.Open;
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public BusinessPartner Partner { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<PartnerDebtLedger> LedgerEntries { get; set; } = [];
}

public sealed class PartnerDebtLedger : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int PartnerId { get; set; }
    public int InvoiceId { get; set; }
    public PartnerDebtType DebtType { get; set; }
    public PartnerDebtTransactionType TransactionType { get; set; }
    public decimal AmountDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public int? ReferenceId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BusinessPartner Partner { get; set; } = null!;
    public PartnerDebtInvoice Invoice { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
