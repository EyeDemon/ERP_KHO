namespace ERP.Domain.Enums;

public enum PartnerDebtType
{
    AP = 1,
    AR = 2
}

public enum PartnerDebtInvoiceStatus
{
    Open = 0,
    PartiallyPaid = 1,
    Paid = 2,
    Cancelled = 3
}

public enum PartnerDebtTransactionType
{
    InvoiceCreated = 1,
    InvoiceIssued = 1,
    Payment = 2,
    Adjustment = 3
}
