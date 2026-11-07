using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    // -------------------------------------------------------------------------
    // BusinessPartnersController
    // -------------------------------------------------------------------------
    public async Task<WmsEndpointOutcome> GetPartnerGovernanceSummaryAsync(CancellationToken ct = default)
    {
        var query = db.BusinessPartners.AsNoTracking();
        return WmsEndpointOutcome.Ok(new
        {
            totalPartners = await query.CountAsync(ct),
            activeSuppliers = await query.CountAsync(x => x.IsActive && x.IsSupplier, ct),
            activeCustomers = await query.CountAsync(x => x.IsActive && x.IsCustomer, ct),
            inactivePartners = await query.CountAsync(x => !x.IsActive, ct),
            dualRolePartners = await query.CountAsync(x => x.IsSupplier && x.IsCustomer, ct),
            missingContactInfoCount = await query.CountAsync(x => (x.Phone == null || x.Phone == "") && (x.Email == null || x.Email == ""), ct)
        });
    }

    public async Task<WmsEndpointOutcome> GetCarriersAndSlaContractsAsync(CancellationToken ct = default)
    {
        var activeQuery = db.BusinessPartners.AsNoTracking().Where(x => x.IsActive);
        var activeSuppliersCount = await activeQuery.CountAsync(x => x.IsSupplier, ct);
        var activeCustomersCount = await activeQuery.CountAsync(x => x.IsCustomer, ct);
        var dualRoleCount = await activeQuery.CountAsync(x => x.IsSupplier && x.IsCustomer, ct);
        var topItems = await activeQuery.OrderBy(x => x.Code).Take(15).ToListAsync(ct);

        return WmsEndpointOutcome.Ok(new
        {
            activeSuppliersCount,
            activeCustomersCount,
            dualRoleCount,
            standardInboundSlaHours = 24,
            standardOutboundDispatchSlaHours = 12,
            targetOnTimeRatePct = 98.5m,
            slaContracts = topItems.Select(x => new
            {
                partnerId = x.Id,
                code = (x.Code ?? string.Empty).Trim(),
                name = (x.Name ?? string.Empty).Trim(),
                partnerRole = (x.IsSupplier && x.IsCustomer) ? "SupplierAndCustomer" : x.IsSupplier ? "Supplier" : "Customer",
                slaTier = (x.IsSupplier && x.IsCustomer) ? "ENTERPRISE_3PL_CARRIER" : x.IsSupplier ? "SUPPLIER_INBOUND_SLA" : "CUSTOMER_FULFILLMENT_SLA",
                targetLeadTimeHours = x.IsSupplier ? 24 : 12,
                onTimeCommitmentPct = 98.5m
            }).ToArray()
        });
    }

    public Task<bool> IsActiveBusinessPartnerAsync(int id, CancellationToken ct = default) =>
        db.BusinessPartners.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive, ct);

    public Task<int> CountActiveBusinessPartnersAsync(CancellationToken ct = default) =>
        db.BusinessPartners.AsNoTracking().CountAsync(x => x.IsActive, ct);

    private sealed record ThreePlStatementLineDto(
        string feeCode,
        string feeName,
        decimal quantity,
        string unitName,
        decimal unitRate,
        decimal amount);

    private sealed record ThreePlBillingComputationResult(
        string statementCode,
        int? partnerId,
        string partnerCode,
        string partnerName,
        int? warehouseId,
        int activePartnersCount,
        string billingPeriod,
        DateTime periodFromUtc,
        DateTime periodToUtc,
        decimal totalOnHandUnits,
        decimal billableStorageUnits,
        decimal inboundUnits,
        decimal outboundUnits,
        decimal storageRatePerUnit,
        decimal inboundRatePerUnit,
        decimal outboundRatePerUnit,
        decimal storageFeeAmount,
        decimal inboundHandlingFeeAmount,
        decimal outboundHandlingFeeAmount,
        decimal subTotalAmount,
        decimal vatRatePercent,
        decimal vatAmount,
        decimal totalBillingAmount,
        string currency,
        ThreePlStatementLineDto[] statementLines);

    private async Task<ThreePlBillingComputationResult> Compute3PlBillingCoreAsync(
        int? partnerId,
        int? warehouseId,
        string? billingPeriod,
        decimal? storageRatePerUnit,
        decimal? inboundRatePerUnit,
        decimal? outboundRatePerUnit,
        DateTime? periodFromUtc,
        DateTime? periodToUtc,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var effectivePeriodFromUtc = periodFromUtc ?? now.Date.AddDays(-30);
        var effectivePeriodToUtc = periodToUtc ?? now;
        if (effectivePeriodToUtc < effectivePeriodFromUtc)
        {
            (effectivePeriodFromUtc, effectivePeriodToUtc) = (effectivePeriodToUtc, effectivePeriodFromUtc);
        }

        var activePartnersCount = await db.BusinessPartners.AsNoTracking().CountAsync(x => x.IsActive, ct);
        int? resolvedPartnerId = null;
        var partnerCode = "BP-3PL-UNASSIGNED";
        var partnerName = "Chưa có chủ hàng ký gửi 3PL hợp lệ";

        BusinessPartner? partner = null;
        if (partnerId.HasValue)
        {
            partner = await db.BusinessPartners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partnerId.Value, ct);
            if (partner != null)
            {
                resolvedPartnerId = partner.Id;
            }
        }
        else
        {
            var defaultConsignmentOwnerId = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.OwnerId != null && s.Quantity > 0m && (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value))
                .OrderByDescending(s => s.Quantity)
                .Select(s => s.OwnerId)
                .FirstOrDefaultAsync(ct);

            if (defaultConsignmentOwnerId.HasValue)
            {
                partner = await db.BusinessPartners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == defaultConsignmentOwnerId.Value, ct);
                if (partner != null)
                {
                    resolvedPartnerId = partner.Id;
                }
            }
        }

        if (partner != null)
        {
            partnerCode = (partner.Code ?? string.Empty).Trim();
            partnerName = (partner.Name ?? string.Empty).Trim();
        }

        var totalOnHandUnits = 0m;
        var inboundUnits = 0m;
        var outboundUnits = 0m;

        if (resolvedPartnerId.HasValue)
        {
            var ownerId = resolvedPartnerId.Value;
            var stockQuery = db.InventoryStocks.AsNoTracking()
                .Where(s => s.OwnerId == ownerId);
            if (warehouseId.HasValue)
            {
                stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
            }

            totalOnHandUnits = await stockQuery.SumAsync(s => (decimal?)s.Quantity, ct) ?? 0m;

            var txQuery = db.InventoryTransactions.AsNoTracking()
                .Where(t => t.OwnerId == ownerId &&
                            t.TransactionDate >= effectivePeriodFromUtc &&
                            t.TransactionDate <= effectivePeriodToUtc);
            if (warehouseId.HasValue)
            {
                txQuery = txQuery.Where(t => t.WarehouseId == warehouseId.Value);
            }

            inboundUnits = await txQuery
                .Where(t => t.TransactionType == TransactionType.Import ||
                            t.TransactionType == TransactionType.TransferIn)
                .SumAsync(t => (decimal?)Math.Abs(t.Quantity), ct) ?? 0m;

            outboundUnits = await txQuery
                .Where(t => t.TransactionType == TransactionType.Export ||
                            t.TransactionType == TransactionType.Ship)
                .SumAsync(t => (decimal?)Math.Abs(t.Quantity), ct) ?? 0m;
        }

        var effectiveStorageRate = storageRatePerUnit is > 0m ? storageRatePerUnit.Value : 15000m;
        var effectiveInboundRate = inboundRatePerUnit is > 0m ? inboundRatePerUnit.Value : 8000m;
        var effectiveOutboundRate = outboundRatePerUnit is > 0m ? outboundRatePerUnit.Value : 9000m;

        var billableStorageUnits = totalOnHandUnits;
        var storageFeeAmount = Math.Round(billableStorageUnits * effectiveStorageRate, 0);
        var inboundHandlingFeeAmount = Math.Round(inboundUnits * effectiveInboundRate, 0);
        var outboundHandlingFeeAmount = Math.Round(outboundUnits * effectiveOutboundRate, 0);
        var subTotalAmount = storageFeeAmount + inboundHandlingFeeAmount + outboundHandlingFeeAmount;
        var vatRatePercent = 10m;
        var vatAmount = Math.Round(subTotalAmount * 0.10m, 0);
        var totalBillingAmount = subTotalAmount + vatAmount;

        var statementLines = new[]
        {
            new ThreePlStatementLineDto(
                "3PL-STORAGE",
                "Phí lưu kho tiêu chuẩn theo kỳ",
                billableStorageUnits,
                "THÙNG",
                effectiveStorageRate,
                storageFeeAmount),
            new ThreePlStatementLineDto(
                "3PL-INBOUND",
                "Phí bốc xếp & tiếp nhận đầu vào",
                inboundUnits,
                "THÙNG",
                effectiveInboundRate,
                inboundHandlingFeeAmount),
            new ThreePlStatementLineDto(
                "3PL-OUTBOUND",
                "Phí soạn hàng, đóng gói & xuất kho",
                outboundUnits,
                "THÙNG",
                effectiveOutboundRate,
                outboundHandlingFeeAmount)
        };

        var resolvedPeriod = !string.IsNullOrWhiteSpace(billingPeriod)
            ? billingPeriod.Trim()
            : now.ToString("yyyy-MM");
        var resolvedStatementCode = resolvedPartnerId.HasValue
            ? $"3PL-STMT-{resolvedPartnerId.Value}-{resolvedPeriod.Replace("-", "")}"
            : $"3PL-STMT-NONE-{resolvedPeriod.Replace("-", "")}";

        return new ThreePlBillingComputationResult(
            resolvedStatementCode,
            resolvedPartnerId,
            partnerCode,
            partnerName,
            warehouseId,
            activePartnersCount,
            resolvedPeriod,
            effectivePeriodFromUtc,
            effectivePeriodToUtc,
            totalOnHandUnits,
            billableStorageUnits,
            inboundUnits,
            outboundUnits,
            effectiveStorageRate,
            effectiveInboundRate,
            effectiveOutboundRate,
            storageFeeAmount,
            inboundHandlingFeeAmount,
            outboundHandlingFeeAmount,
            subTotalAmount,
            vatRatePercent,
            vatAmount,
            totalBillingAmount,
            "VND",
            statementLines);
    }

    public async Task<WmsEndpointOutcome> Get3PlBillingSummaryAsync(
        int? partnerId,
        int? warehouseId,
        string? billingPeriod,
        decimal? storageRatePerUnit,
        decimal? inboundRatePerUnit,
        decimal? outboundRatePerUnit,
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

        return WmsEndpointOutcome.Ok(new
        {
            summary.statementCode,
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
            status = "SUMMARY_READY",
            statusLabel = "Đã tổng hợp bảng tính phí dịch vụ kho 3PL từ sổ cái thực tế",
            calculatedAtUtc = DateTime.UtcNow
        });
    }
}
