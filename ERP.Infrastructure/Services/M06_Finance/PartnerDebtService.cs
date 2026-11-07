using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PartnerDebtService(
    ErpKhoDbContext context,
    ICurrentUser currentUser) : IPartnerDebtService
{
    public async Task<IReadOnlyList<PartnerDebtInvoiceDto>> ListInvoicesAsync(
        string? debtType = null,
        int? partnerId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = context.PartnerDebtInvoices.AsNoTracking()
            .Include(x => x.Partner)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(debtType) && Enum.TryParse<PartnerDebtType>(debtType.Trim(), true, out var parsedType))
            query = query.Where(x => x.DebtType == parsedType);

        if (partnerId.HasValue && partnerId.Value > 0)
            query = query.Where(x => x.PartnerId == partnerId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PartnerDebtInvoiceStatus>(status.Trim(), true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        var items = await query
            .OrderByDescending(x => x.IssuedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        return items.Select(MapInvoice).ToList();
    }

    public async Task<PartnerDebtInvoiceDto> GetInvoiceAsync(int id, CancellationToken ct = default)
    {
        var invoice = await context.PartnerDebtInvoices.AsNoTracking()
            .Include(x => x.Partner)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException($"Không tìm thấy hóa đơn công nợ id {id}.");

        return MapInvoice(invoice);
    }

    public async Task<IReadOnlyList<PartnerDebtLedgerDto>> ListLedgerAsync(
        int? partnerId = null,
        int? invoiceId = null,
        string? debtType = null,
        CancellationToken ct = default)
    {
        var query = context.PartnerDebtLedgers.AsNoTracking()
            .Include(x => x.Partner)
            .Include(x => x.Invoice)
            .AsQueryable();

        if (partnerId.HasValue && partnerId.Value > 0)
            query = query.Where(x => x.PartnerId == partnerId.Value);

        if (invoiceId.HasValue && invoiceId.Value > 0)
            query = query.Where(x => x.InvoiceId == invoiceId.Value);

        if (!string.IsNullOrWhiteSpace(debtType) && Enum.TryParse<PartnerDebtType>(debtType.Trim(), true, out var parsedType))
            query = query.Where(x => x.DebtType == parsedType);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        return items.Select(MapLedger).ToList();
    }

    public async Task<PartnerDebtSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var partners = await context.BusinessPartners.AsNoTracking().ToListAsync(ct);
        var openInvoices = await context.PartnerDebtInvoices.AsNoTracking()
            .Where(x => x.Status == PartnerDebtInvoiceStatus.Open || x.Status == PartnerDebtInvoiceStatus.PartiallyPaid)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;

        return new PartnerDebtSummaryDto
        {
            TotalPayableBalance = partners.Sum(x => x.PayableBalance),
            TotalReceivableBalance = partners.Sum(x => x.ReceivableBalance),
            OpenApInvoiceCount = openInvoices.Count(x => x.DebtType == PartnerDebtType.AP),
            OpenArInvoiceCount = openInvoices.Count(x => x.DebtType == PartnerDebtType.AR),
            OverdueInvoiceCount = openInvoices.Count(x => x.DueDateUtc.HasValue && x.DueDateUtc.Value < now && x.RemainingAmount > 0)
        };
    }

    public async Task<PartnerDebtInvoiceDto> PayInvoiceAsync(int invoiceId, PayPartnerDebtDto dto, CancellationToken ct = default)
    {
        var amount = decimal.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new BusinessRuleException("Số tiền thanh toán phải lớn hơn 0.");

        var own = context.Database.CurrentTransaction is null;
        var maxAttempts = own && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = own
                ? await context.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                var tenantId = context.CurrentTenantId;
                PartnerDebtInvoice? invoice;
                if (context.Database.IsSqlServer())
                {
                    invoice = await context.PartnerDebtInvoices
                        .FromSqlInterpolated(
                            $"SELECT * FROM dbo.PartnerDebtInvoices WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {invoiceId}")
                        .SingleOrDefaultAsync(ct);
                    if (invoice is not null)
                        await context.Entry(invoice).ReloadAsync(ct);
                }
                else
                {
                    invoice = await context.PartnerDebtInvoices
                        .SingleOrDefaultAsync(x => x.Id == invoiceId, ct);
                }

                if (invoice is null)
                    throw new NotFoundException($"Không tìm thấy hóa đơn công nợ id {invoiceId}.");

                if (invoice.Status is PartnerDebtInvoiceStatus.Paid or PartnerDebtInvoiceStatus.Cancelled)
                    throw Conflict("DEBT_INVOICE_ALREADY_SETTLED", "Hóa đơn công nợ đã tất toán hoặc đã hủy.");

                if (!string.IsNullOrWhiteSpace(dto.RowVersion))
                {
                    byte[] expectedVersion;
                    try
                    {
                        expectedVersion = Convert.FromBase64String(dto.RowVersion);
                    }
                    catch (FormatException)
                    {
                        throw new BusinessRuleException("RowVersion không hợp lệ.");
                    }

                    if (!invoice.RowVersion.SequenceEqual(expectedVersion))
                        throw new ConcurrencyException("Hóa đơn công nợ đã được cập nhật bởi yêu cầu khác. Vui lòng tải lại.");

                    context.Entry(invoice).Property(x => x.RowVersion).OriginalValue = expectedVersion;
                }

                if (amount > invoice.RemainingAmount)
                    throw Conflict("DEBT_PAYMENT_EXCEEDS_REMAINING", $"Số tiền thanh toán ({amount:N2}) vượt quá dư nợ còn lại ({invoice.RemainingAmount:N2}).");

                await context.AcquireBusinessPartnerLockAsync(invoice.PartnerId, ct);
                var partner = await context.BusinessPartners.SingleAsync(x => x.Id == invoice.PartnerId, ct);
                if (context.Database.IsSqlServer())
                    await context.Entry(partner).ReloadAsync(ct);

                invoice.PaidAmount = decimal.Round(invoice.PaidAmount + amount, 2, MidpointRounding.AwayFromZero);
                invoice.RemainingAmount = decimal.Round(invoice.TotalAmount - invoice.PaidAmount, 2, MidpointRounding.AwayFromZero);
                invoice.Status = invoice.RemainingAmount == 0m
                    ? PartnerDebtInvoiceStatus.Paid
                    : PartnerDebtInvoiceStatus.PartiallyPaid;
                invoice.UpdatedAtUtc = DateTime.UtcNow;

                decimal balanceAfter;
                if (invoice.DebtType == PartnerDebtType.AP)
                {
                    partner.PayableBalance = Math.Max(0m, decimal.Round(partner.PayableBalance - amount, 2, MidpointRounding.AwayFromZero));
                    balanceAfter = partner.PayableBalance;
                }
                else
                {
                    partner.ReceivableBalance = Math.Max(0m, decimal.Round(partner.ReceivableBalance - amount, 2, MidpointRounding.AwayFromZero));
                    balanceAfter = partner.ReceivableBalance;
                }
                partner.UpdatedAt = DateTime.UtcNow;

                var paymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "BANK_TRANSFER" : dto.PaymentMethod.Trim().ToUpperInvariant();
                var refCode = string.IsNullOrWhiteSpace(dto.ReferenceCode) ? $"PAY-{invoice.Code}-{DateTime.UtcNow:HHmmss}" : dto.ReferenceCode.Trim();
                var isCashAbove5M = string.Equals(paymentMethod, "CASH", StringComparison.OrdinalIgnoreCase) &&
                                    (invoice.TotalAmount >= 5_000_000m || amount >= 5_000_000m);
                var ledgerNote = string.IsNullOrWhiteSpace(dto.Note) ? $"Thanh toán công nợ {invoice.Code}" : dto.Note.Trim();
                if (isCashAbove5M && !ledgerNote.Contains("CANH_BAO_THUE_ND181_2025", StringComparison.Ordinal))
                {
                    ledgerNote = $"{ledgerNote} [CANH_BAO_THUE_ND181_2025: Thanh toán tiền mặt >= 5.000.000 VNĐ không đủ điều kiện khấu trừ thuế GTGT đầu vào theo Luật Thuế GTGT 48/2024/QH15 & Điều 26 NĐ 181/2025/NĐ-CP]";
                    invoice.Note = string.IsNullOrWhiteSpace(invoice.Note)
                        ? ledgerNote
                        : $"{invoice.Note} | {ledgerNote}";
                }

                context.PartnerDebtLedgers.Add(new PartnerDebtLedger
                {
                    PartnerId = partner.Id,
                    InvoiceId = invoice.Id,
                    DebtType = invoice.DebtType,
                    TransactionType = PartnerDebtTransactionType.Payment,
                    AmountDelta = -amount,
                    BalanceAfter = balanceAfter,
                    ReferenceType = "DebtPayment",
                    ReferenceId = invoice.Id,
                    ReferenceCode = refCode.Length > 80 ? refCode[..80] : refCode,
                    PaymentMethod = paymentMethod.Length > 40 ? paymentMethod[..40] : paymentMethod,
                    Note = ledgerNote,
                    CreatedBy = currentUser.UserId > 0 ? currentUser.UserId : invoice.CreatedBy,
                    CreatedAtUtc = DateTime.UtcNow
                });

                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId > 0 ? currentUser.UserId : invoice.CreatedBy,
                    Action = invoice.DebtType == PartnerDebtType.AP ? "PartnerDebt.ApPaid" : "PartnerDebt.ArCollected",
                    EntityName = "PartnerDebtInvoice",
                    EntityId = invoice.Id,
                    NewValues = $"Code: {invoice.Code}; Amount: {amount}; Remaining: {invoice.RemainingAmount}; Status: {invoice.Status}",
                    Result = "Success",
                    Severity = "Information",
                    Timestamp = DateTime.UtcNow
                });

                await context.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);

                invoice.Partner = partner;
                return MapInvoice(invoice);
            }
            catch (Exception ex) when (own && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                throw new ConcurrencyException("Hóa đơn công nợ đã được cập nhật bởi yêu cầu khác. Vui lòng tải lại.", ex);
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                throw;
            }
        }
    }

    public async Task<PartnerDebtInvoice?> LockAndValidateSourceDebtForReversalAsync(
        string? referenceType,
        int? referenceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenceType) || !referenceId.HasValue || referenceId.Value <= 0)
            return null;

        var normalizedReferenceType = NormalizeSourceOrReferenceType(referenceType);
        int? candidateInvoiceId = null;
        if (normalizedReferenceType is "ImportReceipt" or "ExportReceipt")
        {
            candidateInvoiceId = await context.PartnerDebtInvoices.AsNoTracking()
                .Where(x => x.SourceType == normalizedReferenceType && x.SourceId == referenceId.Value)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
        }
        else if (normalizedReferenceType == "Shipment")
        {
            var exportReceiptId = await context.ExportReceipts.AsNoTracking()
                .Where(x => x.ShipmentId == referenceId.Value)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);

            candidateInvoiceId = await context.PartnerDebtInvoices.AsNoTracking()
                .Where(x =>
                    (x.SourceType == "Shipment" && x.SourceId == referenceId.Value) ||
                    (exportReceiptId.HasValue && x.SourceType == "ExportReceipt" && x.SourceId == exportReceiptId.Value))
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (!candidateInvoiceId.HasValue)
            return null;

        var tenantId = context.CurrentTenantId;
        PartnerDebtInvoice invoice;
        if (context.Database.IsSqlServer())
        {
            invoice = await context.PartnerDebtInvoices
                .FromSqlInterpolated(
                    $"SELECT * FROM dbo.PartnerDebtInvoices WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {candidateInvoiceId.Value}")
                .SingleAsync(ct);
            await context.Entry(invoice).ReloadAsync(ct);
        }
        else
        {
            invoice = await context.PartnerDebtInvoices
                .SingleAsync(x => x.Id == candidateInvoiceId.Value, ct);
        }

        if (invoice.PaidAmount > 0m)
        {
            throw Conflict(
                "DEBT_INVOICE_ALREADY_PAID",
                $"Không thể đảo giao dịch kho vì hóa đơn công nợ {invoice.Code} đã phát sinh thanh toán ({invoice.PaidAmount:N2}).");
        }

        return invoice;
    }

    public async Task CancelLockedInvoiceForInventoryReversalAsync(
        PartnerDebtInvoice invoice,
        int originalTransactionId,
        int reversalTransactionId,
        string reason,
        CancellationToken ct = default)
    {
        if (invoice.PaidAmount > 0m)
        {
            throw Conflict(
                "DEBT_INVOICE_ALREADY_PAID",
                $"Không thể đảo giao dịch kho vì hóa đơn công nợ {invoice.Code} đã phát sinh thanh toán ({invoice.PaidAmount:N2}).");
        }

        if (invoice.Status == PartnerDebtInvoiceStatus.Cancelled)
            return;

        await context.AcquireBusinessPartnerLockAsync(invoice.PartnerId, ct);
        var partner = await context.BusinessPartners.SingleAsync(x => x.Id == invoice.PartnerId, ct);
        if (context.Database.IsSqlServer())
            await context.Entry(partner).ReloadAsync(ct);

        var reversalAmount = invoice.RemainingAmount > 0m ? invoice.RemainingAmount : invoice.TotalAmount;
        var now = DateTime.UtcNow;

        invoice.RemainingAmount = 0m;
        invoice.Status = PartnerDebtInvoiceStatus.Cancelled;
        invoice.UpdatedAtUtc = now;
        invoice.Note = string.IsNullOrWhiteSpace(invoice.Note)
            ? $"Hủy công nợ do đảo giao dịch kho #{originalTransactionId}: {reason}"
            : $"{invoice.Note} | Hủy do đảo kho #{originalTransactionId}: {reason}";

        decimal balanceAfter;
        if (invoice.DebtType == PartnerDebtType.AP)
        {
            partner.PayableBalance = Math.Max(0m, decimal.Round(partner.PayableBalance - reversalAmount, 2, MidpointRounding.AwayFromZero));
            balanceAfter = partner.PayableBalance;
        }
        else
        {
            partner.ReceivableBalance = Math.Max(0m, decimal.Round(partner.ReceivableBalance - reversalAmount, 2, MidpointRounding.AwayFromZero));
            balanceAfter = partner.ReceivableBalance;
        }
        partner.UpdatedAt = now;

        var actorId = currentUser.UserId > 0 ? currentUser.UserId : invoice.CreatedBy;
        var refCode = $"REV-{invoice.Code}";
        context.PartnerDebtLedgers.Add(new PartnerDebtLedger
        {
            PartnerId = partner.Id,
            InvoiceId = invoice.Id,
            DebtType = invoice.DebtType,
            TransactionType = PartnerDebtTransactionType.Adjustment,
            AmountDelta = -reversalAmount,
            BalanceAfter = balanceAfter,
            ReferenceType = "InventoryReversal",
            ReferenceId = reversalTransactionId,
            ReferenceCode = refCode.Length > 80 ? refCode[..80] : refCode,
            Note = $"Đảo công nợ tự động do đảo giao dịch kho #{originalTransactionId}: {reason}",
            CreatedBy = actorId,
            CreatedAtUtc = now
        });

        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            Action = "PartnerDebt.ReversedByInventory",
            EntityName = "PartnerDebtInvoice",
            EntityId = invoice.Id,
            NewValues = $"Code: {invoice.Code}; ReversedAmount: {reversalAmount}; BalanceAfter: {balanceAfter}; Status: {invoice.Status}",
            Reason = reason,
            Result = "Success",
            Severity = "Warning",
            Timestamp = now
        });
    }
}
