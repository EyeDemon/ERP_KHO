using ERP.Application.DTOs;
using ERP.Application.Inventory;
using ERP.Domain.Exceptions;

namespace ERP.Infrastructure.Services;

public sealed partial class StockTransferService
{
    private static (string Code, string Reason) ValidateReturnReason(ReturnStockTransferDto request)
    {
        var code = request.ReasonCode?.Trim().ToUpperInvariant();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(code) || !StockTransferReturnReasonCatalog.IsAllowed(code))
            throw new BusinessRuleException("Mã lý do hoàn trả điều chuyển không hợp lệ.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 400)
            throw new BusinessRuleException("Diễn giải hoàn trả bắt buộc và không được quá 400 ký tự.");
        return (code, reason);
    }
}
