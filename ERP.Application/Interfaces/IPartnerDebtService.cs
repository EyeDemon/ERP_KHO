using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IPartnerDebtService
{
    Task<IReadOnlyList<PartnerDebtInvoiceDto>> ListInvoicesAsync(
        string? debtType = null,
        int? partnerId = null,
        string? status = null,
        CancellationToken ct = default);

    Task<PartnerDebtInvoiceDto> GetInvoiceAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PartnerDebtLedgerDto>> ListLedgerAsync(
        int? partnerId = null,
        int? invoiceId = null,
        string? debtType = null,
        CancellationToken ct = default);

    Task<PartnerDebtSummaryDto> GetSummaryAsync(CancellationToken ct = default);

    Task<PartnerDebtInvoiceDto> PayInvoiceAsync(int invoiceId, PayPartnerDebtDto dto, CancellationToken ct = default);

    Task<PartnerDebtInvoiceDto?> RecordImportReceiptApDebtAsync(
        ImportReceipt receipt,
        int actorUserId,
        CancellationToken ct = default);

    Task<PartnerDebtInvoiceDto?> RecordOutboundArDebtAsync(
        int customerId,
        string sourceType,
        int sourceId,
        string sourceCode,
        decimal subTotalAmount,
        decimal taxAmount,
        int actorUserId,
        string? currency = "VND",
        CancellationToken ct = default);
}
