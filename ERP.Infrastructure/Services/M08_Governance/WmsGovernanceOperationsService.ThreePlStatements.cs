using System.Data;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    public async Task<WmsEndpointOutcome> Generate3PlStatementAsync(
        int? partnerId,
        int? warehouseId,
        string? billingPeriod,
        decimal? storageRatePerUnit,
        decimal? inboundRatePerUnit,
        decimal? outboundRatePerUnit,
        bool createReceivableInvoice,
        int? actorUserId,
        CancellationToken ct = default,
        DateTime? periodFromUtc = null,
        DateTime? periodToUtc = null)
    {
        var summary = await Compute3PlBillingCoreAsync(
            partnerId,
            warehouseId,
            billingPeriod,
            storageRatePerUnit,
            inboundRatePerUnit,
            outboundRatePerUnit,
            periodFromUtc,
            periodToUtc,
            ct);

        var now = DateTime.UtcNow;
        string? receivableInvoiceCode = null;
        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = summary.warehouseId,
            EntityName = "ThreePlBillingStatement",
            EntityId = summary.partnerId,
            Action = "BusinessPartner.Generate3PlStatement",
            NewValues = $"{summary.statementCode}: Total={summary.totalBillingAmount:0.##} VND",
            Reason = "Phát hành bảng kê phí dịch vụ kho 3PL từ sổ cái thực tế",
            Result = "Success",
            Timestamp = now
        });

        ThreePlBillingStatement? statementRecord = null;
        if (summary.partnerId.HasValue && summary.partnerId.Value > 0)
        {
            statementRecord = await db.ThreePlBillingStatements
                .FirstOrDefaultAsync(x => x.PartnerId == summary.partnerId.Value && x.StatementCode == summary.statementCode, ct);
            if (statementRecord == null)
            {
                statementRecord = new ThreePlBillingStatement
                {
                    WarehouseId = summary.warehouseId,
                    PartnerId = summary.partnerId.Value,
                    StatementCode = summary.statementCode,
                    PeriodFromUtc = summary.periodFromUtc,
                    PeriodToUtc = summary.periodToUtc,
                    TotalOnHandQty = summary.totalOnHandUnits,
                    TotalInboundQty = summary.inboundUnits,
                    TotalOutboundQty = summary.outboundUnits,
                    StorageFeeAmount = summary.storageFeeAmount,
                    InboundHandlingFeeAmount = summary.inboundHandlingFeeAmount,
                    OutboundHandlingFeeAmount = summary.outboundHandlingFeeAmount,
                    TotalBillingAmount = summary.totalBillingAmount,
                    Status = "ISSUED",
                    CreatedByUserId = actorUserId,
                    CreatedAtUtc = now
                };
                db.ThreePlBillingStatements.Add(statementRecord);
            }
            else
            {
                statementRecord.PeriodFromUtc = summary.periodFromUtc;
                statementRecord.PeriodToUtc = summary.periodToUtc;
                statementRecord.TotalOnHandQty = summary.totalOnHandUnits;
                statementRecord.TotalInboundQty = summary.inboundUnits;
                statementRecord.TotalOutboundQty = summary.outboundUnits;
                statementRecord.StorageFeeAmount = summary.storageFeeAmount;
                statementRecord.InboundHandlingFeeAmount = summary.inboundHandlingFeeAmount;
                statementRecord.OutboundHandlingFeeAmount = summary.outboundHandlingFeeAmount;
                statementRecord.TotalBillingAmount = summary.totalBillingAmount;
            }

            await db.SaveChangesAsync(ct);
        }

        if (createReceivableInvoice && summary.partnerId.HasValue && summary.partnerId.Value > 0)
        {
            var targetPartner = await db.BusinessPartners
                .FirstOrDefaultAsync(x => x.Id == summary.partnerId.Value, ct);

            if (targetPartner != null)
            {
                receivableInvoiceCode = $"AR-3PL-{targetPartner.Id}-{now:yyyyMMdd}";
                var invoiceSourceId = statementRecord?.Id is > 0 ? statementRecord.Id : targetPartner.Id;
                if (statementRecord != null)
                {
                    statementRecord.ReceivableInvoiceCode = receivableInvoiceCode;
                }

                var existingInvoice = await db.PartnerDebtInvoices
                    .FirstOrDefaultAsync(x => x.Code == receivableInvoiceCode
                        || (x.DebtType == PartnerDebtType.AR && x.SourceType == "ThreePlBilling" && x.SourceId == invoiceSourceId)
                        || (x.DebtType == PartnerDebtType.AR && x.SourceType == "ThreePlBilling" && x.PartnerId == targetPartner.Id && x.SourceCode == summary.statementCode), ct);

                if (existingInvoice == null)
                {
                    var newInvoice = new PartnerDebtInvoice
                    {
                        TenantId = db.CurrentTenantId,
                        Code = receivableInvoiceCode,
                        PartnerId = targetPartner.Id,
                        DebtType = PartnerDebtType.AR,
                        SourceType = "ThreePlBilling",
                        SourceId = invoiceSourceId,
                        SourceCode = summary.statementCode,
                        Currency = "VND",
                        SubTotalAmount = summary.subTotalAmount,
                        TaxAmount = summary.vatAmount,
                        TotalAmount = summary.totalBillingAmount,
                        PaidAmount = 0m,
                        RemainingAmount = summary.totalBillingAmount,
                        IssuedAtUtc = now,
                        DueDateUtc = now.AddDays(Math.Max(1, targetPartner.PaymentTermDays)),
                        Status = PartnerDebtInvoiceStatus.Open,
                        Note = $"Hóa đơn phải thu phí dịch vụ kho 3PL kỳ {summary.billingPeriod} ({summary.statementCode})",
                        CreatedBy = actorUserId ?? 1,
                        CreatedAtUtc = now
                    };
                    db.PartnerDebtInvoices.Add(newInvoice);

                    targetPartner.ReceivableBalance += summary.totalBillingAmount;
                    targetPartner.UpdatedAt = now;

                    db.PartnerDebtLedgers.Add(new PartnerDebtLedger
                    {
                        TenantId = db.CurrentTenantId,
                        PartnerId = targetPartner.Id,
                        Invoice = newInvoice,
                        DebtType = PartnerDebtType.AR,
                        TransactionType = PartnerDebtTransactionType.InvoiceCreated,
                        AmountDelta = summary.totalBillingAmount,
                        BalanceAfter = targetPartner.ReceivableBalance,
                        ReferenceType = "ThreePlBilling",
                        ReferenceId = invoiceSourceId,
                        ReferenceCode = receivableInvoiceCode,
                        Note = $"Ghi nhận công nợ phải thu từ bảng kê phí dịch vụ kho 3PL {summary.statementCode}",
                        CreatedBy = actorUserId ?? 1,
                        CreatedAtUtc = now
                    });
                }
                else
                {
                    var delta = summary.totalBillingAmount - existingInvoice.TotalAmount;
                    existingInvoice.Code = receivableInvoiceCode;
                    existingInvoice.SourceId = invoiceSourceId;
                    existingInvoice.SubTotalAmount = summary.subTotalAmount;
                    existingInvoice.TaxAmount = summary.vatAmount;
                    existingInvoice.TotalAmount = summary.totalBillingAmount;
                    existingInvoice.RemainingAmount = Math.Max(0m, summary.totalBillingAmount - existingInvoice.PaidAmount);
                    existingInvoice.SourceCode = summary.statementCode;
                    existingInvoice.UpdatedAtUtc = now;
                    if (delta != 0m)
                    {
                        targetPartner.ReceivableBalance += delta;
                        targetPartner.UpdatedAt = now;

                        db.PartnerDebtLedgers.Add(new PartnerDebtLedger
                        {
                            TenantId = db.CurrentTenantId,
                            PartnerId = targetPartner.Id,
                            InvoiceId = existingInvoice.Id,
                            Invoice = existingInvoice,
                            DebtType = PartnerDebtType.AR,
                            TransactionType = PartnerDebtTransactionType.InvoiceCreated,
                            AmountDelta = delta,
                            BalanceAfter = targetPartner.ReceivableBalance,
                            ReferenceType = "ThreePlBilling",
                            ReferenceId = invoiceSourceId,
                            ReferenceCode = receivableInvoiceCode,
                            Note = $"Điều chỉnh công nợ phải thu từ cập nhật bảng kê phí dịch vụ kho 3PL {summary.statementCode} (Chênh lệch: {delta:0.##} VND)",
                            CreatedBy = actorUserId ?? 1,
                            CreatedAtUtc = now
                        });
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        if (tx != null)
        {
            await tx.CommitAsync(ct);
        }

        var arInvoiceCode = createReceivableInvoice && receivableInvoiceCode != null
            ? receivableInvoiceCode
            : null;

        return WmsEndpointOutcome.Ok(new
        {
            summary.statementCode,
            statementId = summary.statementCode,
            summary.partnerId,
            summary.partnerCode,
            summary.partnerName,
            summary.warehouseId,
            summary.activePartnersCount,
            summary.billingPeriod,
            summary.periodFromUtc,
            summary.periodToUtc,
            summary.totalOnHandUnits,
            summary.billableStorageUnits,
            summary.inboundUnits,
            summary.outboundUnits,
            summary.storageRatePerUnit,
            summary.inboundRatePerUnit,
            summary.outboundRatePerUnit,
            summary.storageFeeAmount,
            summary.inboundHandlingFeeAmount,
            summary.outboundHandlingFeeAmount,
            summary.subTotalAmount,
            summary.vatRatePercent,
            summary.vatAmount,
            summary.totalBillingAmount,
            summary.currency,
            summary.statementLines,
            receivableInvoiceCode = arInvoiceCode,
            receivableInvoiceCreated = arInvoiceCode != null,
            status = "ISSUED",
            statusLabel = arInvoiceCode != null
                ? $"Đã phát hành bảng kê {summary.statementCode} và ghi nhận hóa đơn phải thu {arInvoiceCode} ({summary.totalBillingAmount:N0} đ)"
                : "Đã phát hành bảng kê phí dịch vụ 3PL",
            issuedAtUtc = now,
            calculatedAtUtc = now
        });
    }
}
