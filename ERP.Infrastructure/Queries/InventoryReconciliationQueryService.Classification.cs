using System;
using System.Collections.Generic;
using System.Linq;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Enums;

namespace ERP.Infrastructure.Queries
{
    public partial class InventoryReconciliationQueryService
    {
        private sealed record InvestigationStockGroup(
            InventoryStatus Status,
            decimal Quantity,
            decimal Reserved,
            int Buckets,
            int InvalidReservationBuckets,
            int NegativePhysicalBuckets);

        private sealed record InvestigationLedgerGroup(
            InventoryStatus InventoryStatus,
            TransactionType TransactionType,
            InventoryStatus? FromInventoryStatus,
            InventoryStatus? ToInventoryStatus,
            bool HasCorrectiveTransaction,
            decimal Quantity,
            int Events,
            int NegativeQuantityEvents,
            int NonPositiveQuantityEvents,
            int NonNegativeQuantityEvents,
            int ZeroQuantityEvents);

        private sealed record InvestigationBreakdownResult(
            Dictionary<InventoryStatus, InventoryReconciliationStatusEvidenceDto> ByStatus,
            List<InventoryReconciliationStatusEvidenceDto> StatusBreakdown,
            bool Comparable,
            bool HistoryInsufficientForNonAvailableStock,
            decimal AllStatusCurrent,
            decimal AllStatusReserved,
            decimal? AllStatusExpected,
            string AllStatusStatus,
            int UnclassifiedCount,
            int UnclassifiedStockBucketCount,
            int InvalidReservationBucketCount,
            int NegativePhysicalBucketCount);

        private static void ValidateInvestigationParameters(
            int warehouseId,
            int productId,
            int? eventAnchorId,
            int limit,
            int? eventBeforeId,
            int? bucketAnchorId,
            int? bucketAfterId,
            string bucketStatus,
            string eventStatus,
            int? allStatusBucketAnchorId,
            string? allStatusBucketAggregateFingerprint,
            out InventoryStatus selectedBucketStatus,
            out InventoryStatus selectedEventStatus)
        {
            if (warehouseId <= 0 || productId <= 0)
                throw new BusinessRuleException("ID kho và ID sản phẩm phải là số nguyên dương.");
            if (eventAnchorId is < 0)
                throw new BusinessRuleException("Mốc lịch sử sổ cái không hợp lệ.");
            if (limit is < 1 or > 100)
                throw new BusinessRuleException("Giới hạn sự kiện phải nằm từ 1 đến 100.");
            if (eventBeforeId.HasValue && (eventBeforeId.Value <= 0 ||
                !eventAnchorId.HasValue || eventBeforeId.Value > eventAnchorId.Value))
                throw new BusinessRuleException("Phân trang sự kiện yêu cầu ID trước hợp lệ và mốc sổ cái cố định.");
            if (bucketAnchorId is < 0)
                throw new BusinessRuleException("Mốc ID bucket tồn không hợp lệ.");
            if (allStatusBucketAnchorId is < 0)
                throw new BusinessRuleException("Mốc bucket toàn trạng thái không hợp lệ.");
            if (allStatusBucketAggregateFingerprint is not null &&
                (allStatusBucketAggregateFingerprint.Length != 64 ||
                 allStatusBucketAggregateFingerprint.Any(c =>
                     !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))))
                throw new BusinessRuleException("Dấu kiểm tra tổng hợp bucket không hợp lệ.");
            if (bucketAfterId.HasValue && (bucketAfterId.Value <= 0 ||
                !bucketAnchorId.HasValue || bucketAfterId.Value >= bucketAnchorId.Value))
                throw new BusinessRuleException("Phân trang bucket yêu cầu ID sau hợp lệ và mốc bucket cố định.");
            if (!Enum.TryParse<InventoryStatus>(bucketStatus, out selectedBucketStatus) ||
                !Enum.IsDefined(selectedBucketStatus) ||
                !string.Equals(selectedBucketStatus.ToString(), bucketStatus, StringComparison.Ordinal))
                throw new BusinessRuleException("Trạng thái bucket tồn không hợp lệ.");
            if (!Enum.TryParse<InventoryStatus>(eventStatus, out selectedEventStatus) ||
                !Enum.IsDefined(selectedEventStatus) ||
                !string.Equals(selectedEventStatus.ToString(), eventStatus, StringComparison.Ordinal))
                throw new BusinessRuleException("Trạng thái lịch sử Ledger không hợp lệ.");
        }

        private static InvestigationBreakdownResult BuildInvestigationBreakdown(
            List<InvestigationStockGroup> stockGroups,
            List<InvestigationLedgerGroup> ledgerGroups)
        {
            var unclassifiedStockBucketCount = stockGroups
                .Where(group => !Enum.IsDefined(group.Status))
                .Sum(group => group.Buckets);
            var invalidReservationBucketCount = stockGroups.Sum(group => group.InvalidReservationBuckets);
            var negativePhysicalBucketCount = stockGroups.Sum(group => group.NegativePhysicalBuckets);

            var byStatus = Enum.GetValues<InventoryStatus>()
                .ToDictionary(status => status,
                    status => new InventoryReconciliationStatusEvidenceDto
                    {
                        Status = status.ToString()
                    });
            foreach (var stockGroup in stockGroups)
            {
                if (!byStatus.TryGetValue(stockGroup.Status, out var entry))
                    continue;
                entry.CurrentQuantity = stockGroup.Quantity;
                entry.ReservedQuantity = stockGroup.Reserved;
                entry.BucketCount = stockGroup.Buckets;
            }

            var unclassifiedCount = 0;
            foreach (var group in ledgerGroups)
            {
                if (group.TransactionType == TransactionType.StatusChange)
                {
                    if (group.FromInventoryStatus is not { } from ||
                        group.ToInventoryStatus is not { } to ||
                        from == to ||
                        !byStatus.ContainsKey(from) || !byStatus.ContainsKey(to))
                    {
                        unclassifiedCount += group.Events;
                        continue;
                    }

                    // Source leg of 2-row StatusChange (-Q on FromInventoryStatus)
                    if (group.InventoryStatus == from)
                    {
                        if (group.NonNegativeQuantityEvents > 0)
                        {
                            unclassifiedCount += group.NonNegativeQuantityEvents;
                            continue;
                        }
                        byStatus[from].StatusChangeOutQuantity += Math.Abs(group.Quantity);
                        continue;
                    }

                    // Destination leg of StatusChange (+Q on ToInventoryStatus)
                    if (group.InventoryStatus == to)
                    {
                        if (group.NonPositiveQuantityEvents > 0)
                        {
                            unclassifiedCount += group.NonPositiveQuantityEvents;
                            continue;
                        }
                        byStatus[to].StatusChangeInQuantity += group.Quantity;
                        var hasExplicitSourceLeg = ledgerGroups.Any(g2 =>
                            g2.TransactionType == TransactionType.StatusChange &&
                            g2.FromInventoryStatus == from &&
                            g2.ToInventoryStatus == to &&
                            g2.InventoryStatus == from &&
                            g2.NegativeQuantityEvents > 0);
                        if (!hasExplicitSourceLeg)
                        {
                            byStatus[from].StatusChangeOutQuantity += group.Quantity;
                        }
                        continue;
                    }

                    unclassifiedCount += group.Events;
                    continue;
                }
                if (!byStatus.TryGetValue(group.InventoryStatus, out var current) ||
                    group.TransactionType == TransactionType.TransferAdjustment ||
                    !Enum.IsDefined(group.TransactionType))
                {
                    unclassifiedCount += group.Events;
                    continue;
                }
                if (group.TransactionType == TransactionType.Move)
                {
                    if (group.ZeroQuantityEvents > 0)
                    {
                        unclassifiedCount += group.ZeroQuantityEvents;
                    }
                    continue;
                }
                if (group.TransactionType == TransactionType.Reversal && !group.HasCorrectiveTransaction)
                {
                    if (group.ZeroQuantityEvents > 0)
                    {
                        unclassifiedCount += group.ZeroQuantityEvents;
                        continue;
                    }
                    current.DirectLedgerNetQuantity +=
                        InventoryLedgerCalculationRules.ComputeAlgebraicDelta(group.TransactionType, group.Quantity, null);
                    continue;
                }
                if (group.NegativeQuantityEvents > 0)
                {
                    unclassifiedCount += group.NegativeQuantityEvents;
                    continue;
                }
                current.DirectLedgerNetQuantity +=
                    InventoryLedgerCalculationRules.ComputeAlgebraicDelta(
                        group.TransactionType,
                        group.Quantity,
                        group.HasCorrectiveTransaction ? 1 : null);
            }

            var historyInsufficientForNonAvailableStock =
                ledgerGroups.Count == 0 && stockGroups.Any(x =>
                    x.Status != InventoryStatus.Available && x.Buckets > 0) &&
                !stockGroups.Any(x => x.Status == InventoryStatus.Available && x.Quantity != 0m);
            var comparable = unclassifiedCount == 0 &&
                unclassifiedStockBucketCount == 0 &&
                invalidReservationBucketCount == 0 && negativePhysicalBucketCount == 0 &&
                !historyInsufficientForNonAvailableStock;
            foreach (var row in byStatus.Values)
            {
                if (!comparable) continue;
                row.ExpectedQuantity = row.DirectLedgerNetQuantity +
                    row.StatusChangeInQuantity - row.StatusChangeOutQuantity;
                row.Difference = row.CurrentQuantity - row.ExpectedQuantity.Value;
            }
            var statusBreakdown = byStatus.Values.ToList();
            var allStatusCurrent = stockGroups.Sum(x => x.Quantity);
            var allStatusReserved = stockGroups.Sum(x => x.Reserved);
            decimal? allStatusExpected = comparable
                ? statusBreakdown.Sum(x => x.ExpectedQuantity!.Value)
                : null;
            var allStatusStatus = !comparable ? "Indeterminate" :
                statusBreakdown.Any(x => x.Difference != 0m) ? "Mismatch" : "Match";

            return new InvestigationBreakdownResult(
                byStatus, statusBreakdown, comparable, historyInsufficientForNonAvailableStock,
                allStatusCurrent, allStatusReserved, allStatusExpected, allStatusStatus,
                unclassifiedCount, unclassifiedStockBucketCount, invalidReservationBucketCount, negativePhysicalBucketCount);
        }

        private static decimal? ComputeEventSignedQuantity(
            TransactionType txType,
            InventoryStatus inventoryStatus,
            InventoryStatus? fromStatus,
            InventoryStatus? toStatus,
            decimal quantity,
            InventoryStatus selectedEventStatus)
        {
            if (txType == TransactionType.TransferAdjustment || !Enum.IsDefined(txType))
                return null;

            if (txType == TransactionType.StatusChange)
            {
                if (quantity == 0m ||
                    fromStatus is not { } from ||
                    toStatus is not { } to ||
                    from == to ||
                    !Enum.IsDefined(from) ||
                    !Enum.IsDefined(to))
                {
                    return null;
                }

                if (inventoryStatus == from && quantity < 0m)
                {
                    return selectedEventStatus == from ? quantity : null;
                }

                if (inventoryStatus == to && quantity > 0m)
                {
                    return selectedEventStatus == to
                        ? quantity
                        : selectedEventStatus == from
                            ? -quantity
                            : null;
                }

                return null;
            }

            if (txType == TransactionType.Move)
            {
                return quantity == 0m ? null : quantity;
            }

            if (quantity < 0m)
                return null;

            return txType.ApplySign(quantity);
        }
    }
}
