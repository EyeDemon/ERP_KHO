using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class SaasTenant
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string TenantName
    {
        get => CompanyName;
        set => CompanyName = value;
    }
    public string? TaxCode { get; set; }
    public string TaxpayerType { get; set; } = "ENTERPRISE"; // ENTERPRISE | HOUSEHOLD_INDIVIDUAL | INDIVIDUAL_CONSUMER
    public string? OwnerEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? LegalRegisteredAddress { get; set; }
    public string LifecycleStatus { get; set; } = "ACTIVE"; // ACTIVE | TRIAL | SUSPENDED | READ_ONLY | ARCHIVED
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Status
    {
        get => LifecycleStatus;
        set => LifecycleStatus = value;
    }
    public string? SuspensionReason { get; set; }
    public bool AllowSupportAccess { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

public class SaasPlan
{
    public int Id { get; set; }
    public string PlanCode { get; set; } = string.Empty; // FREE_TRIAL | STARTER | PRO | ENTERPRISE
    public string PlanName { get; set; } = string.Empty;
    public int MonthlyOrderQuota { get; set; }
    public int MaxWarehouses { get; set; }
    public int MaxUsers { get; set; }
    public decimal MonthlyPriceVnd { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TenantSubscription : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string PlanCode { get; set; } = "STARTER";
    public string Status { get; set; } = "ACTIVE"; // TRIAL | ACTIVE | PAST_DUE | READ_ONLY
    public int MonthlyOrderQuota { get; set; } = 1000;
    public int CurrentMonthOrderCount { get; set; }
    public int MaxWarehouses { get; set; } = 2;
    public int MaxUsers { get; set; } = 10;
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime CurrentPeriodStartUtc { get; set; } = DateTime.UtcNow;
    public DateTime CurrentPeriodEndUtc { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string EvaluateQuotaTier()
    {
        if (MonthlyOrderQuota <= 0)
        {
            return "NORMAL";
        }

        var pct = (CurrentMonthOrderCount * 100m) / MonthlyOrderQuota;
        if (pct > 115m)
        {
            return "READ_ONLY_OVER_115";
        }

        if (pct > 100m)
        {
            return "SOFT_BURST_100_115";
        }

        if (pct >= 80m)
        {
            return "WARNING_80_100";
        }

        return "NORMAL";
    }
}

public class SaasBillingInvoice : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string InvoiceCode { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public decimal SubTotalVnd { get; set; }
    public decimal VatRatePercent { get; set; } = 10.00m;
    public decimal VatAmountVnd { get; set; }
    public decimal AmountVnd { get; set; }
    public string? BuyerTaxCode { get; set; }
    public string? BuyerLegalName { get; set; }
    public string PaymentProvider { get; set; } = "VIETQR"; // VIETQR | SEPAY | CASSO | VNPAY
    public string Status { get; set; } = "UNPAID"; // UNPAID | PAID
    public string? QrPayload { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
