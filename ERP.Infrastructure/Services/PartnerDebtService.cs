using System.Data;
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

public sealed class PartnerDebtService(
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

                var ledger = new PartnerDebtLedger
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
                    Note = string.IsNullOrWhiteSpace(dto.Note) ? $"Thanh toán công nợ {invoice.Code}" : dto.Note.Trim(),
                    CreatedBy = currentUser.UserId > 0 ? currentUser.UserId : invoice.CreatedBy,
                    CreatedAtUtc = DateTime.UtcNow
                };
                context.PartnerDebtLedgers.Add(ledger);

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

    public async Task EnqueueValuationOutboxEventAsync(
        string aggregateType,
        int aggregateId,
        string referenceCode,
        int warehouseId,
        decimal totalAmount = 0m,
        CancellationToken ct = default)
    {
        var tenantId = context.CurrentTenantId > 0 ? context.CurrentTenantId : 1;
        var isImportReceipt = string.Equals(aggregateType, "ImportReceipt", StringComparison.OrdinalIgnoreCase);
        var eventId = isImportReceipt
            ? $"VALUATION-IMP-{tenantId}-{aggregateId}"
            : $"OUTBOX-VAL-{tenantId}-{aggregateType.ToUpperInvariant()}-{aggregateId}";
        var legacyImportKey = $"VALUATION-IMP-{aggregateId}";
        var legacyOutboxKey = $"OUTBOX-VAL-{aggregateType.ToUpperInvariant()}-{aggregateId}";

        var existing = context.IntegrationOutboxMessages.Local
            .FirstOrDefault(x => x.TenantId == tenantId &&
                (x.EventId == eventId || x.EventId == legacyImportKey || x.EventId == legacyOutboxKey))
            ?? await context.IntegrationOutboxMessages
                .FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                    (x.EventId == eventId || x.EventId == legacyImportKey || x.EventId == legacyOutboxKey), ct);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            receiptId = isImportReceipt ? aggregateId : (int?)null,
            aggregateType,
            aggregateId,
            code = referenceCode,
            referenceCode,
            warehouseId,
            totalAmount,
            targetSystem = "ACCOUNTING_ERP_GL",
            occurredAtUtc = DateTime.UtcNow
        });

        if (existing is not null)
        {
            existing.EventId = eventId;
            if (totalAmount > 0m)
            {
                existing.PayloadJson = payloadJson;
            }
            return;
        }

        context.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            TenantId = tenantId,
            EventId = eventId,
            EventType = "InventoryValuationPosted",
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = payloadJson,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(ct);
    }

    public async Task<PartnerDebtInvoiceDto?> RecordImportReceiptApDebtAsync(
        ImportReceipt receipt,
        int actorUserId,
        CancellationToken ct = default)
    {
        decimal subTotal = 0m;
        decimal taxAmount = 0m;
        var outboxLines = new List<object>();
        foreach (var line in receipt.Details)
        {
            var factor = line.ConversionFactor > 0 ? line.ConversionFactor : 1m;
            var acceptedQty = line.AcceptedQuantity > 0
                ? line.AcceptedQuantity
                : (line.BaseAcceptedQuantity > 0 ? line.BaseAcceptedQuantity / factor : 0m);
            var lineSub = acceptedQty > 0 && line.UnitPrice > 0
                ? decimal.Round(acceptedQty * line.UnitPrice, 2, MidpointRounding.AwayFromZero)
                : 0m;
            var lineTax = lineSub > 0 && line.TaxPercent > 0
                ? decimal.Round(lineSub * (line.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero)
                : 0m;
            subTotal += lineSub;
            taxAmount += lineTax;
            outboxLines.Add(new
            {
                detailId = line.Id,
                productId = line.ProductId,
                acceptedQuantity = acceptedQty,
                baseAcceptedQuantity = line.BaseAcceptedQuantity,
                unitPrice = line.UnitPrice,
                taxPercent = line.TaxPercent,
                lineSubTotal = lineSub,
                lineTaxAmount = lineTax,
                lineTotal = lineSub + lineTax
            });
        }

        var totalAmount = subTotal + taxAmount;
        var tenantId = receipt.TenantId > 0 ? receipt.TenantId : (context.CurrentTenantId > 0 ? context.CurrentTenantId : 1);
        var idempotencyKey = $"VALUATION-IMP-{tenantId}-{receipt.Id}";
        var legacyKey1 = $"VALUATION-IMP-{receipt.Id}";
        var legacyKey2 = $"OUTBOX-VAL-IMPORTRECEIPT-{receipt.Id}";
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            receiptId = receipt.Id,
            aggregateType = "ImportReceipt",
            aggregateId = receipt.Id,
            code = receipt.Code,
            referenceCode = receipt.Code,
            warehouseId = receipt.WarehouseId,
            subTotalAmount = subTotal,
            taxAmount,
            totalAmount,
            lines = outboxLines,
            targetSystem = "ACCOUNTING_ERP_GL",
            occurredAtUtc = DateTime.UtcNow
        });

        var existingOutbox = context.IntegrationOutboxMessages.Local
            .FirstOrDefault(x => x.TenantId == tenantId &&
                (x.EventId == idempotencyKey || x.EventId == legacyKey1 || x.EventId == legacyKey2))
            ?? await context.IntegrationOutboxMessages
                .FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                    (x.EventId == idempotencyKey || x.EventId == legacyKey1 || x.EventId == legacyKey2), ct);

        if (existingOutbox is null)
        {
            context.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                TenantId = tenantId,
                EventId = idempotencyKey,
                EventType = "InventoryValuationPosted",
                AggregateType = "ImportReceipt",
                AggregateId = receipt.Id,
                PayloadJson = payloadJson,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(ct);
        }
        else
        {
            existingOutbox.EventId = idempotencyKey;
            existingOutbox.PayloadJson = payloadJson;
            await context.SaveChangesAsync(ct);
        }

        if (!receipt.SupplierId.HasValue || receipt.Details.Count == 0)
            return null;
        if (totalAmount <= 0m)
            return null;

        var existing = await context.PartnerDebtInvoices
            .Include(x => x.Partner)
            .SingleOrDefaultAsync(x =>
                x.DebtType == PartnerDebtType.AP &&
                x.SourceType == "ImportReceipt" &&
                x.SourceId == receipt.Id, ct);
        if (existing is not null)
            return MapInvoice(existing);

        await context.AcquireBusinessPartnerLockAsync(receipt.SupplierId.Value, ct);
        var supplier = await context.BusinessPartners.SingleOrDefaultAsync(x => x.Id == receipt.SupplierId.Value, ct);
        if (supplier is null)
            return null;

        var currency = "VND";
        if (receipt.PurchaseOrderId.HasValue)
        {
            var poCurrency = await context.PurchaseOrders.AsNoTracking()
                .Where(x => x.Id == receipt.PurchaseOrderId.Value)
                .Select(x => x.Currency)
                .SingleOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(poCurrency))
                currency = poCurrency.Trim().ToUpperInvariant();
        }

        var now = DateTime.UtcNow;
        var invoice = new PartnerDebtInvoice
        {
            Code = $"AP-IR-{receipt.Id:D6}",
            PartnerId = supplier.Id,
            DebtType = PartnerDebtType.AP,
            SourceType = "ImportReceipt",
            SourceId = receipt.Id,
            SourceCode = receipt.Code,
            Currency = currency,
            SubTotalAmount = subTotal,
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            PaidAmount = 0m,
            RemainingAmount = totalAmount,
            Status = PartnerDebtInvoiceStatus.Open,
            IssuedAtUtc = now,
            DueDateUtc = now.AddDays(Math.Max(0, supplier.PaymentTermDays)),
            Note = $"Công nợ phải trả từ phiếu nhập {receipt.Code}",
            CreatedBy = actorUserId,
            CreatedAtUtc = now
        };

        supplier.PayableBalance = decimal.Round(supplier.PayableBalance + totalAmount, 2, MidpointRounding.AwayFromZero);
        supplier.UpdatedAt = now;

        context.PartnerDebtInvoices.Add(invoice);
        await context.SaveChangesAsync(ct);

        var ledger = new PartnerDebtLedger
        {
            PartnerId = supplier.Id,
            InvoiceId = invoice.Id,
            DebtType = PartnerDebtType.AP,
            TransactionType = PartnerDebtTransactionType.InvoiceCreated,
            AmountDelta = totalAmount,
            BalanceAfter = supplier.PayableBalance,
            ReferenceType = "ImportReceipt",
            ReferenceId = receipt.Id,
            ReferenceCode = receipt.Code,
            Note = $"Ghi nhận công nợ phải trả từ phiếu nhập {receipt.Code}",
            CreatedBy = actorUserId,
            CreatedAtUtc = now
        };
        context.PartnerDebtLedgers.Add(ledger);
        await context.SaveChangesAsync(ct);

        invoice.Partner = supplier;
        return MapInvoice(invoice);
    }

    public async Task<PartnerDebtInvoiceDto?> RecordOutboundArDebtAsync(
        int customerId,
        string sourceType,
        int sourceId,
        string sourceCode,
        decimal subTotalAmount,
        decimal taxAmount,
        int actorUserId,
        string? currency = "VND",
        CancellationToken ct = default)
    {
        var normalizedSourceType = NormalizeSourceOrReferenceType(sourceType);
        var subTotal = decimal.Round(subTotalAmount, 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round(taxAmount, 2, MidpointRounding.AwayFromZero);
        var totalAmount = subTotal + tax;
        if (customerId <= 0 || totalAmount <= 0m)
            return null;

        var existing = await context.PartnerDebtInvoices
            .Include(x => x.Partner)
            .SingleOrDefaultAsync(x =>
                x.DebtType == PartnerDebtType.AR &&
                x.SourceType == normalizedSourceType &&
                x.SourceId == sourceId, ct);
        if (existing is not null)
            return MapInvoice(existing);

        await context.AcquireBusinessPartnerLockAsync(customerId, ct);
        var customer = await context.BusinessPartners.SingleOrDefaultAsync(x => x.Id == customerId, ct);
        if (customer is null)
            return null;

        var now = DateTime.UtcNow;
        var prefix = normalizedSourceType == "ExportReceipt" ? "AR-ER" : "AR-SHP";
        var invoice = new PartnerDebtInvoice
        {
            Code = $"{prefix}-{sourceId:D6}",
            PartnerId = customer.Id,
            DebtType = PartnerDebtType.AR,
            SourceType = normalizedSourceType,
            SourceId = sourceId,
            SourceCode = sourceCode,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            SubTotalAmount = subTotal,
            TaxAmount = tax,
            TotalAmount = totalAmount,
            PaidAmount = 0m,
            RemainingAmount = totalAmount,
            Status = PartnerDebtInvoiceStatus.Open,
            IssuedAtUtc = now,
            DueDateUtc = now.AddDays(Math.Max(0, customer.PaymentTermDays)),
            Note = $"Công nợ phải thu từ chứng từ xuất {sourceCode}",
            CreatedBy = actorUserId,
            CreatedAtUtc = now
        };

        customer.ReceivableBalance = decimal.Round(customer.ReceivableBalance + totalAmount, 2, MidpointRounding.AwayFromZero);
        customer.UpdatedAt = now;

        context.PartnerDebtInvoices.Add(invoice);
        await context.SaveChangesAsync(ct);

        var ledger = new PartnerDebtLedger
        {
            PartnerId = customer.Id,
            InvoiceId = invoice.Id,
            DebtType = PartnerDebtType.AR,
            TransactionType = PartnerDebtTransactionType.InvoiceCreated,
            AmountDelta = totalAmount,
            BalanceAfter = customer.ReceivableBalance,
            ReferenceType = normalizedSourceType,
            ReferenceId = sourceId,
            ReferenceCode = sourceCode,
            Note = $"Ghi nhận công nợ phải thu từ chứng từ xuất {sourceCode}",
            CreatedBy = actorUserId,
            CreatedAtUtc = now
        };
        context.PartnerDebtLedgers.Add(ledger);
        await context.SaveChangesAsync(ct);

        invoice.Partner = customer;
        return MapInvoice(invoice);
    }

    private static string NormalizeSourceOrReferenceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "IMPORTRECEIPT" or "IMPORT_RECEIPT" => "ImportReceipt",
            "EXPORTRECEIPT" or "EXPORT_RECEIPT" => "ExportReceipt",
            "SHIPMENT" => "Shipment",
            "DEBTPAYMENT" or "DEBT_PAYMENT" => "DebtPayment",
            "INVENTORYREVERSAL" or "INVENTORY_REVERSAL" => "InventoryReversal",
            _ => trimmed
        };
    }

    private static PartnerDebtInvoiceDto MapInvoice(PartnerDebtInvoice x) => new()
    {
        Id = x.Id,
        Code = (x.Code ?? string.Empty).Trim(),
        PartnerId = x.PartnerId,
        PartnerCode = (x.Partner?.Code ?? string.Empty).Trim(),
        PartnerName = (x.Partner?.Name ?? string.Empty).Trim(),
        DebtType = x.DebtType.ToString(),
        SourceType = NormalizeSourceOrReferenceType(x.SourceType),
        SourceId = x.SourceId,
        SourceCode = (x.SourceCode ?? string.Empty).Trim(),
        Currency = string.IsNullOrWhiteSpace(x.Currency) ? "VND" : x.Currency.Trim().ToUpperInvariant(),
        SubTotalAmount = x.SubTotalAmount,
        TaxAmount = x.TaxAmount,
        TotalAmount = x.TotalAmount,
        PaidAmount = x.PaidAmount,
        RemainingAmount = x.RemainingAmount,
        Status = x.Status.ToString(),
        IssuedAtUtc = x.IssuedAtUtc,
        DueDateUtc = x.DueDateUtc,
        Note = x.Note?.Trim(),
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static PartnerDebtLedgerDto MapLedger(PartnerDebtLedger x) => new()
    {
        Id = x.Id,
        PartnerId = x.PartnerId,
        PartnerCode = (x.Partner?.Code ?? string.Empty).Trim(),
        PartnerName = (x.Partner?.Name ?? string.Empty).Trim(),
        InvoiceId = x.InvoiceId,
        InvoiceCode = (x.Invoice?.Code ?? string.Empty).Trim(),
        DebtType = x.DebtType.ToString(),
        TransactionType = x.TransactionType.ToString(),
        AmountDelta = x.AmountDelta,
        BalanceAfter = x.BalanceAfter,
        ReferenceType = NormalizeSourceOrReferenceType(x.ReferenceType),
        ReferenceId = x.ReferenceId,
        ReferenceCode = (x.ReferenceCode ?? string.Empty).Trim(),
        PaymentMethod = string.IsNullOrWhiteSpace(x.PaymentMethod) ? null : x.PaymentMethod.Trim().ToUpperInvariant(),
        Note = x.Note?.Trim(),
        CreatedBy = x.CreatedBy,
        CreatedAtUtc = x.CreatedAtUtc
    };

    private static BusinessRuleException Conflict(string errorCode, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = errorCode;
        return ex;
    }
}
