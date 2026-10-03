using ERP.Application.Exceptions;
using System.Text.RegularExpressions;

namespace ERP.Application.Security;

public static class ApprovalRejectReasonPolicy
{
    public static string Normalize(string? reason)
    {
        var value = reason?.Trim() ?? string.Empty;
        if (value.Length is < 3 or > 500)
            throw new BusinessRuleException("Lý do từ chối phải có từ 3 đến 500 ký tự.");
        if (value.Any(char.IsControl) || Regex.IsMatch(value, "<[^>]*>", RegexOptions.CultureInvariant))
            throw new BusinessRuleException("Lý do từ chối chứa ký tự không hợp lệ.");
        return value;
    }
}
