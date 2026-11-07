using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PartnerDebtService
{
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

        if (!receipt.SupplierId.HasValue || receipt.Details.Count == 0 || totalAmount <= 0m)
            return null;

        // 3PL Bailment / Consignment guard (TK 002 Thông tư 200/2014/TT-BTC):
        // Hàng nhận giữ hộ/gia công 3PL không thuộc quyền sở hữu của kho, tuyệt đối không ghi nhận công nợ phải trả tiền hàng (AP).
        if (!string.IsNullOrWhiteSpace(receipt.Note) &&
            (receipt.Note.Contains("BAILMENT", StringComparison.OrdinalIgnoreCase) ||
             receipt.Note.Contains("3PL_CONSIGNMENT", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

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

        context.PartnerDebtLedgers.Add(new PartnerDebtLedger
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
        });
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

        context.PartnerDebtLedgers.Add(new PartnerDebtLedger
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
        });
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

    private static PartnerDebtInvoiceDto MapInvoice(PartnerDebtInvoice x)
    {
        var hasCashAbove5MWarning = !string.IsNullOrWhiteSpace(x.Note) &&
                                    x.Note.Contains("CANH_BAO_THUE_ND181_2025", StringComparison.Ordinal);
        return new()
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
            IsInputVatDeductible = !hasCashAbove5MWarning,
            TaxComplianceWarning = hasCashAbove5MWarning
                ? "Cảnh báo Luật Thuế GTGT 48/2024/QH15 & Điều 26 NĐ 181/2025/NĐ-CP: Hóa đơn từ 5.000.000 VNĐ thanh toán bằng tiền mặt (CASH) không đủ điều kiện khấu trừ thuế GTGT đầu vào."
                : null,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        };
    }

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
