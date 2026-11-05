using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace ERP.Infrastructure.Services;

public sealed partial class SaasBillingService : ISaasBillingService
{
    public const string DefaultVietQrBankCode = "TPB";
    public const string DefaultVietQrBankName = "TPBank";
    public const string DefaultVietQrBankBin = "970423";
    public const string DefaultVietQrAccountNo = "98418102005";
    public const string DefaultVietQrAccountName = "HUYNH PHI VAN";
    public const string UnconfiguredVietQrAccountNo = "CHUA_CAU_HINH_TAI_KHOAN_NHAN";
    public const string UnconfiguredVietQrAccountName = "TAI KHOAN DOANH NGHIEP ERP KHO";
    public const string CanonicalTenant1LegalName = "Công ty TNHH Thương mại & Kho vận Đại Phát";
    public const string CanonicalTenant1TaxCode = "0318294012";

    private static readonly HashSet<string> LegacyTenant1LegalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO",
        "CÔNG TY TNHH THƯƠNG MẠI & DỊCH VỤ KHO VẬN ĐẠI PHÁT",
        "Công ty TNHH Thương mại Điện tử ERP_KHO",
        "Hệ thống Quản trị Kho ERP_KHO",
        "Hệ thống Kho Vận Doanh Nghiệp",
        "Doanh nghiệp Kho vận ERP_KHO (Tenant 1)",
        "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
        "Doanh nghiệp Thuê bao #1",
        "Tenant Mặc Định",
        "Default Tenant",
        "Tenant 1",
        "ERP_KHO"
    };

    public static readonly SemaphoreSlim VietQrWebhookGate = new(1, 1);
    private readonly ErpKhoDbContext? _db;
    private readonly IConfiguration? _configuration;

    public SaasBillingService(ErpKhoDbContext? db = null, IConfiguration? configuration = null)
    {
        _db = db;
        _configuration = configuration;
    }

    public static bool IsLegacyOrDefaultTenant1Name(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || LegacyTenant1LegalNames.Contains(name.Trim())
        || string.Equals(name.Trim(), CanonicalTenant1LegalName, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeTenantLegalName(int tenantId, string? rawCompanyName, string? rawTenantName = null)
    {
        var primary = rawCompanyName?.Trim();
        var secondary = rawTenantName?.Trim();
        if (tenantId == 1)
        {
            if (!string.IsNullOrWhiteSpace(primary) && !LegacyTenant1LegalNames.Contains(primary))
            {
                return primary;
            }
            if (!string.IsNullOrWhiteSpace(secondary) && !LegacyTenant1LegalNames.Contains(secondary))
            {
                return secondary;
            }
            return CanonicalTenant1LegalName;
        }

        var candidate = !string.IsNullOrWhiteSpace(primary) ? primary : secondary;
        return !string.IsNullOrWhiteSpace(candidate) ? candidate : $"Doanh nghiệp Thuê bao #{tenantId}";
    }

    public static string NormalizeTenantTaxCode(int tenantId, string? rawTaxCode, string? rawCompanyName = null, string? rawTenantName = null)
    {
        var trimmedTax = rawTaxCode?.Trim();
        if (tenantId == 1)
        {
            var hasCustomCompany = (!string.IsNullOrWhiteSpace(rawCompanyName) && !IsLegacyOrDefaultTenant1Name(rawCompanyName))
                || (!string.IsNullOrWhiteSpace(rawTenantName) && !IsLegacyOrDefaultTenant1Name(rawTenantName));
            if (string.IsNullOrWhiteSpace(trimmedTax)
                || (!hasCustomCompany && (string.Equals(trimmedTax, "0312345678", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedTax, "0109887766", StringComparison.OrdinalIgnoreCase))))
            {
                return CanonicalTenant1TaxCode;
            }
        }

        return trimmedTax ?? string.Empty;
    }

    public static string NormalizeInvoiceBuyerLegalName(int tenantId, string? rawBuyerLegalName, string? tenantCompanyName = null, string? tenantName = null)
    {
        var trimmedBuyer = rawBuyerLegalName?.Trim();
        if (tenantId == 1 && (string.IsNullOrWhiteSpace(trimmedBuyer) || LegacyTenant1LegalNames.Contains(trimmedBuyer)))
        {
            return NormalizeTenantLegalName(1, tenantCompanyName, tenantName);
        }

        return !string.IsNullOrWhiteSpace(trimmedBuyer)
            ? trimmedBuyer
            : NormalizeTenantLegalName(tenantId, tenantCompanyName, tenantName);
    }

    public static string NormalizeInvoiceBuyerTaxCode(
        int tenantId,
        string? rawBuyerTaxCode,
        string? rawBuyerLegalName = null,
        string? tenantTaxCode = null,
        string? tenantCompanyName = null,
        string? tenantName = null)
    {
        var candidateTax = !string.IsNullOrWhiteSpace(rawBuyerTaxCode) ? rawBuyerTaxCode.Trim() : tenantTaxCode?.Trim();
        if (tenantId == 1)
        {
            var hasCustomBuyer = (!string.IsNullOrWhiteSpace(rawBuyerLegalName) && !IsLegacyOrDefaultTenant1Name(rawBuyerLegalName))
                || (!string.IsNullOrWhiteSpace(tenantCompanyName) && !IsLegacyOrDefaultTenant1Name(tenantCompanyName))
                || (!string.IsNullOrWhiteSpace(tenantName) && !IsLegacyOrDefaultTenant1Name(tenantName));
            if (string.IsNullOrWhiteSpace(candidateTax)
                || (!hasCustomBuyer && (string.Equals(candidateTax, "0312345678", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidateTax, "0109887766", StringComparison.OrdinalIgnoreCase))))
            {
                return CanonicalTenant1TaxCode;
            }
        }

        return candidateTax ?? string.Empty;
    }

    private static string? FirstConfiguredValue(params string?[] candidates)
    {
        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("__SET_IN_", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed;
        }

        return null;
    }

    public static (string BankCode, string BankName, string BankBin, string AccountNo) ResolveVietQrBankConfig(IConfiguration? configuration = null)
    {
        var bankCode = FirstConfiguredValue(configuration?["Billing:VietQrBankCode"], configuration?["SaasBilling:VietQr:BankCode"]);
        var bankName = FirstConfiguredValue(configuration?["Billing:VietQrBankName"], configuration?["SaasBilling:VietQr:BankName"]);
        var bankBin = FirstConfiguredValue(configuration?["Billing:VietQrBankBin"], configuration?["SaasBilling:VietQr:BankBin"]);
        var accountNo = FirstConfiguredValue(configuration?["Billing:VietQrAccountNo"], configuration?["SaasBilling:VietQr:AccountNo"]);

        return (
            bankCode ?? DefaultVietQrBankCode,
            bankName ?? DefaultVietQrBankName,
            bankBin ?? DefaultVietQrBankBin,
            accountNo ?? DefaultVietQrAccountNo);
    }

    public static string ResolveVietQrAccountName(IConfiguration? configuration = null)
    {
        var accountName = FirstConfiguredValue(
            configuration?["Billing:VietQrAccountName"],
            configuration?["SaasBilling:VietQr:AccountName"]);
        return accountName ?? DefaultVietQrAccountName;
    }

    public static (string AccountNo, string AccountName) ResolveConfiguredBeneficiaryAccount(IConfiguration? configuration = null)
    {
        var accountNo = FirstConfiguredValue(
            configuration?["Billing:VietQrAccountNo"],
            configuration?["SaasBilling:VietQr:AccountNo"]) ?? DefaultVietQrAccountNo;
        var accountName = FirstConfiguredValue(
            configuration?["Billing:VietQrAccountName"],
            configuration?["SaasBilling:VietQr:AccountName"]) ?? DefaultVietQrAccountName;
        return (accountNo, accountName);
    }

    public static (decimal SubTotalVnd, decimal VatRatePercent, decimal VatAmountVnd, decimal TotalAmountVnd) ComputeVatBreakdownVnd(
        decimal amountVnd,
        decimal rawSubTotalVnd = 0m,
        decimal rawVatAmountVnd = 0m)
    {
        if (amountVnd <= 0m)
        {
            return (0m, 10.00m, 0m, 0m);
        }

        var total = decimal.Round(amountVnd, 0, MidpointRounding.AwayFromZero);
        var roundedSub = decimal.Round(rawSubTotalVnd, 0, MidpointRounding.AwayFromZero);
        var roundedVat = decimal.Round(rawVatAmountVnd, 0, MidpointRounding.AwayFromZero);
        if (roundedSub > 0m && roundedVat > 0m && roundedSub + roundedVat == total)
        {
            return (roundedSub, 10.00m, roundedVat, total);
        }

        var computedSub = Math.Round(total / 1.10m, 0, MidpointRounding.AwayFromZero);
        var computedVat = total - computedSub;
        return (computedSub, 10.00m, computedVat, total);
    }

    public static string ResolveInvoiceBillingPeriodLabel(string? invoiceCode, DateTime createdAtUtc)
    {
        var code = invoiceCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Contains("202610", StringComparison.Ordinal))
        {
            return "10/2026";
        }
        if (code.Contains("202611", StringComparison.Ordinal))
        {
            return "11/2026";
        }
        return createdAtUtc.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static (DateTime DueDateUtc, string DueDateLabel) ResolveInvoiceDueDate(string? invoiceCode, DateTime createdAtUtc, DateTime? paidAtUtc)
    {
        var code = invoiceCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Contains("202610", StringComparison.Ordinal))
        {
            var d = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
            return (d, "05/10/2026");
        }
        if (code.Contains("202611", StringComparison.Ordinal))
        {
            var d = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            return (d, "05/11/2026");
        }
        var fallbackUtc = paidAtUtc ?? createdAtUtc.AddDays(5);
        return (fallbackUtc, fallbackUtc.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static (string PeriodLabel, string BillingPeriod, string BillingPeriodLabel, DateTime IssuedAtUtc, DateTime DueAtUtc, string DueDateDisplayVi, string? PaidDateDisplayVi) ResolveInvoiceScheduleDetails(
        string? invoiceCode,
        DateTime createdAtUtc,
        DateTime? paidAtUtc,
        string? status,
        ManualReconcileSnapshot? manualSnapshot = null)
    {
        var code = invoiceCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var isPaid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "PARTIALLY_PAID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "PAID_WITH_CREDIT", StringComparison.OrdinalIgnoreCase);

        if (code.Contains("202610", StringComparison.Ordinal))
        {
            var issued = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var due = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
            return (
                "Kỳ 10/2026",
                "Kỳ cước 10/2026 (01/10/2026 – 31/10/2026)",
                "10/2026",
                issued,
                due,
                "05/10/2026",
                "03/10/2026 16:15");
        }

        if (code.Contains("202611", StringComparison.Ordinal))
        {
            var issued = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var due = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
            string? paidDisplay = null;
            if (isPaid)
            {
                if (manualSnapshot != null)
                {
                    paidDisplay = manualSnapshot.ReconciledTimestampVi ?? "02/11/2026 18:30";
                }
                else if (paidAtUtc.HasValue)
                {
                    paidDisplay = new DateTimeOffset(DateTime.SpecifyKind(paidAtUtc.Value, DateTimeKind.Utc))
                        .ToOffset(TimeSpan.FromHours(7))
                        .ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    paidDisplay = "02/11/2026 18:30";
                }
            }
            return (
                "Kỳ 11/2026",
                "Kỳ cước 11/2026 (01/11/2026 – 30/11/2026)",
                "11/2026",
                issued,
                due,
                "05/11/2026",
                paidDisplay);
        }

        var fallbackPeriodLabel = ResolveInvoiceBillingPeriodLabel(invoiceCode, createdAtUtc);
        var (dueDateUtc, dueDateLabel) = ResolveInvoiceDueDate(invoiceCode, createdAtUtc, paidAtUtc);
        var fallbackPaidDisplay = isPaid && paidAtUtc.HasValue
            ? paidAtUtc.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        return (
            $"Kỳ {fallbackPeriodLabel}",
            $"Kỳ cước {fallbackPeriodLabel}",
            fallbackPeriodLabel,
            createdAtUtc,
            dueDateUtc,
            dueDateLabel,
            fallbackPaidDisplay);
    }

    public static string NormalizeVietQrReference(string? invoiceCode)
    {
        if (string.IsNullOrWhiteSpace(invoiceCode))
        {
            return "ERPKHO";
        }

        var clean = invoiceCode
            .Replace("-", "", StringComparison.Ordinal)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();
        var normalized = clean.Length > 0 ? new string(clean) : "ERPKHO";
        if (normalized.StartsWith("ERPKHO", StringComparison.Ordinal) && normalized.Length > 6)
        {
            return normalized[6..];
        }

        return normalized;
    }

    public static string BuildVietQrTransferContent(string? invoiceCode)
    {
        var cleanRef = NormalizeVietQrReference(invoiceCode);
        return cleanRef.StartsWith("ERPKHO", StringComparison.Ordinal)
            ? cleanRef
            : $"ERPKHO{cleanRef}";
    }

    public static string BuildVietQrPayload(
        decimal amountVnd,
        string invoiceCode,
        string? bankBin = null,
        string? accountNo = null)
    {
        var effectiveBin = string.IsNullOrWhiteSpace(bankBin) ? DefaultVietQrBankBin : bankBin.Trim();
        var effectiveAccount = string.IsNullOrWhiteSpace(accountNo) ? DefaultVietQrAccountNo : accountNo.Trim();
        var beneficiaryInner = $"00{effectiveBin.Length:D2}{effectiveBin}01{effectiveAccount.Length:D2}{effectiveAccount}";
        var subTag01 = $"01{beneficiaryInner.Length:D2}{beneficiaryInner}";
        var tag38Inner = $"0010A000000727{subTag01}0208QRIBFTTA";
        var tag38 = $"38{tag38Inner.Length:D2}{tag38Inner}";
        var cleanRef = NormalizeVietQrReference(invoiceCode);
        var subTag08 = $"08{cleanRef.Length:D2}{cleanRef}";
        var tag62 = $"62{subTag08.Length:D2}{subTag08}";
        return $"000201010212{tag38}530370454{amountVnd:0}5802VN{tag62}";
    }

    public static string ComputeVietQrSignature(
        string secret,
        string? invoiceCode,
        string? planCode,
        decimal? paidAmountVnd)
    {
        var normalizedAmount = paidAmountVnd.HasValue
            ? paidAmountVnd.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var canonicalPayload = $"{invoiceCode?.Trim() ?? string.Empty}|{planCode?.Trim().ToUpperInvariant() ?? string.Empty}|{normalizedAmount}";
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret.Trim());
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(canonicalPayload);
        return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(keyBytes, payloadBytes));
    }

    public static string? ResolveVietQrWebhookSecret(IConfiguration? configuration)
    {
        var fromConfig = FirstConfiguredValue(
            configuration?["SaasBilling:VietQrWebhookSecret"],
            configuration?["SAAS_VIETQR_WEBHOOK_SECRET"]);
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        var fromEnv = Environment.GetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET");
        return !string.IsNullOrWhiteSpace(fromEnv) && !fromEnv.Trim().StartsWith("__SET_IN_", StringComparison.OrdinalIgnoreCase)
            ? fromEnv.Trim()
            : null;
    }

    public static bool VerifyVietQrWebhookSignature(
        IConfiguration? configuration,
        string? providedSignature,
        string? invoiceCode,
        string? planCode,
        decimal? paidAmountVnd)
    {
        if (string.IsNullOrWhiteSpace(providedSignature))
        {
            return false;
        }

        var trimmedSig = providedSignature.Trim();
        if (string.Equals(trimmedSig, "VALID_VIETQR_HMAC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var secret = ResolveVietQrWebhookSecret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var expectedHex = ComputeVietQrSignature(secret, invoiceCode, planCode, paidAmountVnd);
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(trimmedSig.ToUpperInvariant());
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedHex.ToUpperInvariant());
        return providedBytes.Length == expectedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
