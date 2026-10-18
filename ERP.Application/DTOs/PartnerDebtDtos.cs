namespace ERP.Application.DTOs;

public sealed class PartnerDebtInvoiceDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int PartnerId { get; set; }
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string DebtType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public decimal SubTotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public string? Note { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PartnerDebtLedgerDto
{
    public int Id { get; set; }
    public int PartnerId { get; set; }
    public string PartnerCode { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public int InvoiceId { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public string DebtType { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public decimal AmountDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public int? ReferenceId { get; set; }
    public string? ReferenceCode { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PayPartnerDebtDto
{
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class PartnerDebtSummaryDto
{
    public decimal TotalPayableBalance { get; set; }
    public decimal TotalReceivableBalance { get; set; }
    public int OpenApInvoiceCount { get; set; }
    public int OpenArInvoiceCount { get; set; }
    public int OverdueInvoiceCount { get; set; }
}
