using System;
using System.Linq;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Exceptions;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries
{
    public class InventoryReconciliationQueryService : IInventoryReconciliationQueryService
    {
        private readonly ErpKhoDbContext _context;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal InventoryReconciliationQueryService(ErpKhoDbContext context)
        {
            _context = context;
        }

        public InventoryReconciliationQueryService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _context = context;
            _warehouseAuthorization = warehouseAuthorization;
        }

        // Return only warehouses the current user can reconcile. Unlike the
        // generic warehouse master lookup, this endpoint never exposes other
        // warehouses or requires broader master-data permissions.
        public async Task<IReadOnlyList<InventoryReconciliationWarehouseDto>> GetAccessibleWarehousesAsync()
        {
            if (_warehouseAuthorization is null)
                throw new InvalidOperationException("Thiếu dịch vụ phân quyền kho đối chiếu.");

            var allowed = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
            return await _context.Warehouses.AsNoTracking()
                .Where(x => allowed.Contains(x.Id))
                .OrderBy(x => x.Code).ThenBy(x => x.Id)
                .Select(x => new InventoryReconciliationWarehouseDto
                {
                    Id = x.Id, Code = x.Code, Name = x.Name
                }).ToListAsync();
        }


        public async Task<InventoryReconciliationInvestigationDto> GetInvestigationAsync(
            int warehouseId, int productId, int? eventAnchorId = null, int limit = 50,
            int? eventBeforeId = null, int? bucketAnchorId = null,
            int? bucketAfterId = null, string bucketStatus = "Available",
            string eventStatus = "Available", int? allStatusBucketAnchorId = null,
            string? allStatusBucketAggregateFingerprint = null)
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
            if (!Enum.TryParse<InventoryStatus>(bucketStatus, out var selectedBucketStatus) ||
                !Enum.IsDefined(selectedBucketStatus) ||
                !string.Equals(selectedBucketStatus.ToString(), bucketStatus, StringComparison.Ordinal))
                throw new BusinessRuleException("Trạng thái bucket tồn không hợp lệ.");
            if (!Enum.TryParse<InventoryStatus>(eventStatus, out var selectedEventStatus) ||
                !Enum.IsDefined(selectedEventStatus) ||
                !string.Equals(selectedEventStatus.ToString(), eventStatus, StringComparison.Ordinal))
                throw new BusinessRuleException("Trạng thái lịch sử Ledger không hợp lệ.");
            if (_warehouseAuthorization is null)
                throw new InvalidOperationException("Thiếu dịch vụ phân quyền kho đối chiếu.");

            await using var investigationReadTransaction = _context.Database.IsSqlServer()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            await _warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId);

            var product = await _context.Products.AsNoTracking()
                .Where(x => x.Id == productId)
                .Select(x => new { x.Code, x.Name })
                .SingleOrDefaultAsync();
            if (product is null)
                throw new NotFoundException("Không tìm thấy sản phẩm cần đối chiếu.");

            var warehouseName = await _context.Warehouses.AsNoTracking()
                .Where(x => x.Id == warehouseId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync();
            if (warehouseName is null)
                throw new NotFoundException("Không tìm thấy kho cần đối chiếu.");

            var tenantId = _context.CurrentTenantId;
            var baseStocks = _context.Database.IsSqlServer()
                ? _context.InventoryStocks
                    .FromSqlInterpolated($"SELECT * FROM [InventoryStocks] WITH (INDEX(IX_InventoryStocks_CanonicalBucket)) WHERE [TenantId] = {tenantId}")
                    .AsNoTracking()
                : _context.InventoryStocks.AsNoTracking();
            var allStatusStocks = baseStocks
                .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId);
            var stocks = allStatusStocks
                .Where(x => x.Status == InventoryStatus.Available);
            var selectedStatusStocks = allStatusStocks.Where(x => x.Status == selectedBucketStatus);
            var allStatusLedger = _context.InventoryTransactions.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId);
            var ledger = allStatusLedger.Where(x => x.InventoryStatus == InventoryStatus.Available);

            var latestScopedEventId = await allStatusLedger.MaxAsync(x => (int?)x.Id) ?? 0;
            if (eventAnchorId.HasValue && eventAnchorId.Value > latestScopedEventId)
                throw new BusinessRuleException("Mốc lịch sử sổ cái vượt quá sự kiện hiện có của kho và sản phẩm.");
            var anchor = eventAnchorId ?? latestScopedEventId;
            var anchoredAllStatusLedger = allStatusLedger.Where(x => x.Id <= anchor);
            var anchoredLedger = ledger.Where(x => x.Id <= anchor);
            var hasNewEvents = latestScopedEventId > anchor;

            var hasTwoLegStatusChanges = await anchoredAllStatusLedger.AnyAsync(x =>
                x.TransactionType == TransactionType.StatusChange &&
                x.FromInventoryStatus.HasValue &&
                x.InventoryStatus == x.FromInventoryStatus.Value &&
                x.Quantity < 0m);

            var selectedEventLedger = hasTwoLegStatusChanges
                ? allStatusLedger.Where(x => x.InventoryStatus == selectedEventStatus)
                : allStatusLedger.Where(x =>
                    x.TransactionType == TransactionType.StatusChange
                        ? x.InventoryStatus == selectedEventStatus ||
                          x.FromInventoryStatus == selectedEventStatus ||
                          x.ToInventoryStatus == selectedEventStatus
                        : x.InventoryStatus == selectedEventStatus);

            var latestScopedBucketId = await selectedStatusStocks.MaxAsync(x => (int?)x.Id) ?? 0;
            if (bucketAnchorId.HasValue && bucketAnchorId.Value > latestScopedBucketId)
                throw new BusinessRuleException("Mốc bucket tồn vượt quá bucket hiện có trong phạm vi được chọn.");
            var bucketAnchor = bucketAnchorId ?? latestScopedBucketId;
            var anchoredStocks = selectedStatusStocks.Where(x => x.Id <= bucketAnchor);
            var bucketCount = await anchoredStocks.CountAsync();
            var bucketHasRowsAfterAnchor = latestScopedBucketId > bucketAnchor;
            var latestAllStatusBucketId = await allStatusStocks.MaxAsync(x => (int?)x.Id) ?? 0;
            if (allStatusBucketAnchorId.HasValue &&
                allStatusBucketAnchorId.Value > latestAllStatusBucketId)
                throw new BusinessRuleException("Mốc bucket toàn trạng thái vượt quá bucket hiện có của kho và sản phẩm.");
            var allStatusBucketAnchor = allStatusBucketAnchorId ?? latestAllStatusBucketId;
            var allStatusBucketHasRowsAfterAnchor = latestAllStatusBucketId > allStatusBucketAnchor;
            var eventCount = await selectedEventLedger.CountAsync(x => x.Id <= anchor);

            var stockGroups = await allStatusStocks
                .GroupBy(x => x.Status)
                .Select(g => new
                {
                    Status = g.Key, Quantity = g.Sum(x => x.Quantity),
                    Reserved = g.Sum(x => x.ReservedQuantity), Buckets = g.Count(),
                    InvalidReservationBuckets = g.Count(x =>
                        x.ReservedQuantity < 0m || x.ReservedQuantity > x.Quantity),
                    NegativePhysicalBuckets = g.Count(x => x.Quantity < 0m)
                }).ToListAsync();
            var aggregateText = string.Join(";", stockGroups.OrderBy(x => (int)x.Status)
                .Select(x => FormattableString.Invariant(
                    $"{(int)x.Status}:{x.Quantity}:{x.Reserved}:{x.Buckets}")));
            var observedFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(aggregateText))).ToLowerInvariant();
            var pinnedFingerprint = allStatusBucketAggregateFingerprint ?? observedFingerprint;
            var aggregateChanged = !string.Equals(
                pinnedFingerprint, observedFingerprint, StringComparison.Ordinal);
            var ledgerGroups = await anchoredAllStatusLedger
                .GroupBy(x => new
                {
                    x.InventoryStatus, x.TransactionType,
                    x.FromInventoryStatus, x.ToInventoryStatus,
                    HasCorrectiveTransaction = x.CorrectiveTransactionId != null
                })
                .Select(g => new
                {
                    g.Key.InventoryStatus, g.Key.TransactionType,
                    g.Key.FromInventoryStatus, g.Key.ToInventoryStatus,
                    g.Key.HasCorrectiveTransaction,
                    Quantity = g.Sum(x => x.Quantity), Events = g.Count(),
                    NegativeQuantityEvents = g.Count(x => x.Quantity < 0m),
                    NonPositiveQuantityEvents = g.Count(x => x.Quantity <= 0m),
                    NonNegativeQuantityEvents = g.Count(x => x.Quantity >= 0m),
                    ZeroQuantityEvents = g.Count(x => x.Quantity == 0m)
                }).ToListAsync();
            if (stockGroups.Count == 0 && ledgerGroups.Count == 0)
                throw new NotFoundException("Không có bucket hoặc sự kiện sổ cái cho kho và sản phẩm này.");
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
            var evidenceAnchorsStale = hasNewEvents || bucketHasRowsAfterAnchor ||
                allStatusBucketHasRowsAfterAnchor || aggregateChanged;
            var snapshotComparisonStatus = evidenceAnchorsStale
                ? "Indeterminate" : allStatusStatus;

            var currentQuantity = await stocks.SumAsync(x => (decimal?)x.Quantity) ?? 0m;
            var grouped = await anchoredLedger
                .GroupBy(x => x.TransactionType)
                .Select(x => new
                {
                    Type = x.Key, Quantity = x.Sum(t => t.Quantity),
                    NegativeQuantityEvents = x.Count(t => t.Quantity < 0m)
                })
                .ToListAsync();
            var availableLedgerExpectedIsPartial = !comparable;
            var legacyClassifiedQuantity = grouped
                .Where(x => x.NegativeQuantityEvents == 0 &&
                    x.Type != TransactionType.TransferAdjustment &&
                    x.Type != TransactionType.Move &&
                    x.Type != TransactionType.StatusChange &&
                    Enum.IsDefined(x.Type))
                .Sum(x => x.Type.ApplySign(x.Quantity));
            var expectedQuantity = comparable
                ? byStatus[InventoryStatus.Available].ExpectedQuantity!.Value
                : legacyClassifiedQuantity;

            var bucketPage = bucketAfterId.HasValue
                ? anchoredStocks.Where(x => x.Id > bucketAfterId.Value)
                : anchoredStocks;
            var buckets = await bucketPage
                .OrderBy(x => x.Id)
                .Take(101)
                .Select(x => new InventoryReconciliationEvidenceBucketDto
                {
                    InventoryStockId = x.Id,
                    LocationId = x.LocationId,
                    LocationCode = x.Location != null ? x.Location.Code : null,
                    LotId = x.LotId,
                    LotNumber = x.Lot != null ? x.Lot.LotNumber : null,
                    SerialId = x.SerialId,
                    SerialNumber = x.Serial != null ? x.Serial.SerialNumber : null,
                    Quantity = x.Quantity,
                    ReservedQuantity = x.ReservedQuantity
                }).ToListAsync();

            var selectedAnchoredEvents = selectedEventLedger.Where(x => x.Id <= anchor);
            var pageLedger = eventBeforeId.HasValue
                ? selectedAnchoredEvents.Where(x => x.Id < eventBeforeId.Value)
                : selectedAnchoredEvents;
            var rawEvents = await pageLedger
                .OrderByDescending(x => x.Id)
                .Take(limit + 1)
                .Select(x => new
                {
                    x.Id, x.TransactionType, x.InventoryStatus,
                    x.FromInventoryStatus, x.ToInventoryStatus, x.LocationId,
                    LocationCode = x.Location != null ? x.Location.Code : null,
                    x.LotId, LotNumber = x.Lot != null ? x.Lot.LotNumber : null,
                    x.SerialId, SerialNumber = x.Serial != null ? x.Serial.SerialNumber : null,
                    x.Quantity, x.ReferenceType, x.ReferenceId, x.TransactionDate
                })
                .ToListAsync();

            var hasOlderEvents = rawEvents.Count > limit;
            var page = rawEvents.Take(limit).ToList();

            var result = new InventoryReconciliationInvestigationDto
            {
                WarehouseId = warehouseId,
                WarehouseName = warehouseName,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                EventAnchorId = anchor,
                EventStatus = selectedEventStatus.ToString(),
                BucketStatus = selectedBucketStatus.ToString(),
                BucketAnchorId = bucketAnchor,
                BucketAfterId = bucketAfterId,
                NextBucketAfterId = buckets.Count > 100 ? buckets[99].InventoryStockId : null,
                BucketHasRowsAfterAnchor = bucketHasRowsAfterAnchor,
                AllStatusBucketAnchorId = allStatusBucketAnchor,
                AllStatusBucketHasRowsAfterAnchor = allStatusBucketHasRowsAfterAnchor,
                AllStatusBucketAggregateFingerprint = pinnedFingerprint,
                AllStatusBucketAggregateChanged = aggregateChanged,
                EventBeforeId = eventBeforeId,
                NextEventBeforeId = hasOlderEvents && page.Count > 0 ? page[^1].Id : null,
                LedgerHasEventsAfterAnchor = hasNewEvents,
                EvidenceAnchorsStale = evidenceAnchorsStale,
                SnapshotComparisonStatus = snapshotComparisonStatus,
                SnapshotComparisonExpectedQuantity = evidenceAnchorsStale
                    ? null : allStatusExpected,
                SnapshotComparisonDifference = evidenceAnchorsStale || !allStatusExpected.HasValue
                    ? null : allStatusCurrent - allStatusExpected.Value,
                EventCount = eventCount,
                BucketCount = bucketCount,
                CurrentQuantity = currentQuantity,
                ExpectedQuantity = expectedQuantity,
                Difference = currentQuantity - expectedQuantity,
                AvailableLedgerExpectedIsPartial = availableLedgerExpectedIsPartial,
                HistoryInsufficientForNonAvailableStock = historyInsufficientForNonAvailableStock,
                AllStatusCurrentQuantity = allStatusCurrent,
                AllStatusReservedQuantity = allStatusReserved,
                AllStatusStatus = allStatusStatus,
                AllStatusExpectedQuantity = allStatusExpected,
                AllStatusDifference = allStatusExpected.HasValue
                    ? allStatusCurrent - allStatusExpected.Value
                    : null,
                UnclassifiedLedgerEventCount = unclassifiedCount,
                UnclassifiedStockBucketCount = unclassifiedStockBucketCount,
                InvalidReservationBucketCount = invalidReservationBucketCount,
                NegativePhysicalBucketCount = negativePhysicalBucketCount,
                StatusBreakdown = statusBreakdown,
                EventsTruncated = hasOlderEvents,
                BucketsTruncated = buckets.Count > 100,
                Buckets = buckets.Take(100).ToList(),
                Events = page.Select(x =>
                    new InventoryReconciliationEvidenceEventDto
                    {
                        TransactionId = x.Id,
                        TransactionType = x.TransactionType.ToString(),
                        InventoryStatus = x.InventoryStatus.ToString(),
                        FromInventoryStatus = x.FromInventoryStatus?.ToString(),
                        ToInventoryStatus = x.ToInventoryStatus?.ToString(),
                        LocationId = x.LocationId,
                        LocationCode = x.LocationCode,
                        LotId = x.LotId,
                        LotNumber = x.LotNumber,
                        SerialId = x.SerialId,
                        SerialNumber = x.SerialNumber,
                        Quantity = x.Quantity,
                        SignedQuantity = ComputeEventSignedQuantity(
                            x.TransactionType,
                            x.InventoryStatus,
                            x.FromInventoryStatus,
                            x.ToInventoryStatus,
                            x.Quantity,
                            selectedEventStatus),
                        ReferenceType = x.ReferenceType,
                        ReferenceId = x.ReferenceId,
                        TransactionDate = x.TransactionDate
                    }).ToList()
            };
            if (investigationReadTransaction is not null)
                await investigationReadTransaction.CommitAsync();
            return result;
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

        public async Task<PagedResult<InventoryReconciliationDto>> GetReconciliationsAsync(
            int? warehouseId,
            int? productId,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20)
        {
            if (warehouseId is <= 0)
                throw new BusinessRuleException("ID kho đối chiếu phải là số nguyên dương.");
            if (productId is <= 0)
                throw new BusinessRuleException("ID sản phẩm đối chiếu phải là số nguyên dương.");

            var safePage = Math.Max(1, pageIndex);
            var safeSize = Math.Clamp(pageSize, 1, 100);
            var allStatusStockQuery = _context.InventoryStocks.AsNoTracking();
            var transactionQuery = _context.InventoryTransactions.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                if (warehouseId.HasValue)
                {
                    await _warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value);
                    allStatusStockQuery = allStatusStockQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    transactionQuery = transactionQuery.Where(t => t.WarehouseId == warehouseId.Value);
                }
                else
                {
                    var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                    allStatusStockQuery = allStatusStockQuery.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
                    transactionQuery = transactionQuery.Where(t => allowedWarehouseIds.Contains(t.WarehouseId));
                }
            }
            var stockPairs = allStatusStockQuery.Select(s => new { s.ProductId, s.WarehouseId });
            var transPairs = transactionQuery.Select(t => new { t.ProductId, t.WarehouseId });

            var allPairsQuery = stockPairs.Union(transPairs);

            var query = allPairsQuery
                .Join(_context.Products.AsNoTracking(), p => p.ProductId, pr => pr.Id, (p, pr) => new { p.ProductId, p.WarehouseId, ProductCode = pr.Code, ProductName = pr.Name })
                .Join(_context.Warehouses.AsNoTracking(), p => p.WarehouseId, w => w.Id, (p, w) => new { p.ProductId, p.WarehouseId, p.ProductCode, p.ProductName, WarehouseName = w.Name });

            if (warehouseId.HasValue)
            {
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            }

            if (productId.HasValue)
            {
                query = query.Where(s => s.ProductId == productId.Value);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lowerKeyword = keyword.Trim().ToLowerInvariant();
                query = query.Where(s => 
                    s.ProductCode.ToLower().Contains(lowerKeyword) || 
                    s.ProductName.ToLower().Contains(lowerKeyword));
            }

            var totalRecords = await query.CountAsync();

            var offset = ((long)safePage - 1) * safeSize;
            if (offset >= totalRecords)
            {
                return new PagedResult<InventoryReconciliationDto>
                {
                    Items = [],
                    TotalRecords = totalRecords,
                    PageIndex = safePage,
                    PageSize = safeSize
                };
            }

            var pagedPairs = await query
                .OrderBy(s => s.WarehouseId).ThenBy(s => s.ProductId)
                .Skip(checked((int)offset))
                .Take(safeSize)
                .ToListAsync();

            var productIds = pagedPairs.Select(s => s.ProductId).Distinct().ToList();
            var warehouseIds = pagedPairs.Select(s => s.WarehouseId).Distinct().ToList();

            var stocks = await allStatusStockQuery
                .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
                .GroupBy(s => new { s.ProductId, s.WarehouseId, s.Status })
                .Select(g => new
                {
                    g.Key.ProductId, g.Key.WarehouseId, g.Key.Status,
                    Quantity = g.Sum(s => s.Quantity), Buckets = g.Count(),
                    InvalidReservationBuckets = g.Count(s =>
                        s.ReservedQuantity < 0m || s.ReservedQuantity > s.Quantity),
                    NegativePhysicalBuckets = g.Count(s => s.Quantity < 0m)
                }).ToListAsync();

            var transactions = await transactionQuery
                .Where(t => productIds.Contains(t.ProductId) && warehouseIds.Contains(t.WarehouseId))
                .GroupBy(t => new
                {
                    t.ProductId, t.WarehouseId, t.TransactionType,
                    t.InventoryStatus, t.FromInventoryStatus, t.ToInventoryStatus,
                    HasCorrectiveTransaction = t.CorrectiveTransactionId != null
                })
                .Select(g => new
                {
                    g.Key.ProductId, g.Key.WarehouseId, g.Key.TransactionType,
                    g.Key.InventoryStatus, g.Key.FromInventoryStatus, g.Key.ToInventoryStatus,
                    g.Key.HasCorrectiveTransaction,
                    TotalQuantity = g.Sum(t => t.Quantity),
                    EventCount = g.Count(),
                    NegativeQuantityEvents = g.Count(t => t.Quantity < 0m),
                    NonPositiveQuantityEvents = g.Count(t => t.Quantity <= 0m),
                    NonNegativeQuantityEvents = g.Count(t => t.Quantity >= 0m),
                    ZeroQuantityEvents = g.Count(t => t.Quantity == 0m)
                })
                .ToListAsync();

            var results = pagedPairs.Select(pair => 
            {
                var pairStocks = stocks.Where(s =>
                    s.ProductId == pair.ProductId && s.WarehouseId == pair.WarehouseId).ToList();
                var currentQuantity = pairStocks.Where(s => s.Status == InventoryStatus.Available)
                    .Sum(s => s.Quantity);
                var allStatusCurrent = pairStocks.Sum(s => s.Quantity);
                var unclassifiedStockBucketCount = pairStocks
                    .Where(s => !Enum.IsDefined(s.Status)).Sum(s => s.Buckets);
                var invalidStockStatus = unclassifiedStockBucketCount > 0;
                var invalidReservationBucketCount = pairStocks.Sum(s => s.InvalidReservationBuckets);
                var negativePhysicalBucketCount = pairStocks.Sum(s => s.NegativePhysicalBuckets);

                var stockTransactions = transactions
                    .Where(t => t.ProductId == pair.ProductId && t.WarehouseId == pair.WarehouseId)
                    .ToList();

                decimal Total(TransactionType type) => type == TransactionType.Move
                    ? 0m
                    : stockTransactions
                        .Where(t => t.InventoryStatus == InventoryStatus.Available &&
                                    t.TransactionType != TransactionType.Move &&
                                    t.TransactionType == type)
                        .Sum(t => t.TotalQuantity);

                var importQuantity = Total(TransactionType.Import);
                var exportQuantity = Total(TransactionType.Export) + Total(TransactionType.Ship);
                var transferInQuantity = Total(TransactionType.TransferIn);
                var transferOutQuantity = Total(TransactionType.TransferOut);
                var adjustmentIncreaseQuantity = Total(TransactionType.AdjustmentIncrease);
                var adjustmentDecreaseQuantity = Total(TransactionType.AdjustmentDecrease);
                var statusChangeIn = stockTransactions
                    .Where(t => t.TransactionType == TransactionType.StatusChange &&
                                t.ToInventoryStatus == InventoryStatus.Available &&
                                t.InventoryStatus == InventoryStatus.Available &&
                                t.TotalQuantity > 0m)
                    .Sum(t => t.TotalQuantity);
                var statusChangeOut = stockTransactions
                    .Where(t => t.TransactionType == TransactionType.StatusChange &&
                                t.FromInventoryStatus == InventoryStatus.Available &&
                                t.InventoryStatus == t.ToInventoryStatus &&
                                t.TotalQuantity > 0m)
                    .Sum(t => t.TotalQuantity);
                if (statusChangeOut == 0m)
                {
                    statusChangeOut = stockTransactions
                        .Where(t => t.TransactionType == TransactionType.StatusChange &&
                                    t.FromInventoryStatus == InventoryStatus.Available &&
                                    t.InventoryStatus == InventoryStatus.Available &&
                                    t.TotalQuantity < 0m)
                        .Sum(t => Math.Abs(t.TotalQuantity));
                }

                var unclassifiedCount = stockTransactions.Sum(t =>
                {
                    if (!Enum.IsDefined(t.InventoryStatus) ||
                        !Enum.IsDefined(t.TransactionType) ||
                        t.TransactionType == TransactionType.TransferAdjustment)
                        return t.EventCount;
                    if (t.TransactionType == TransactionType.Move)
                        return t.ZeroQuantityEvents;
                    if (t.TransactionType == TransactionType.Reversal && !t.HasCorrectiveTransaction)
                        return t.ZeroQuantityEvents;
                    if (t.TransactionType != TransactionType.StatusChange)
                        return t.NegativeQuantityEvents;
                    if (t.FromInventoryStatus is not { } from ||
                        t.ToInventoryStatus is not { } to ||
                        from == to ||
                        !Enum.IsDefined(from) || !Enum.IsDefined(to))
                        return t.EventCount;
                    if (t.InventoryStatus == from)
                        return t.NonNegativeQuantityEvents;
                    if (t.InventoryStatus == to)
                        return t.NonPositiveQuantityEvents;
                    return t.EventCount;
                });

                var lacksLedgerForOtherStatusOnly =
                    pairStocks.Any(s => s.Status != InventoryStatus.Available) &&
                    !pairStocks.Any(s => s.Status == InventoryStatus.Available &&
                        s.Quantity != 0m) &&
                    stockTransactions.Count == 0;
                decimal? expectedQuantity =
                    unclassifiedCount > 0 || invalidStockStatus ||
                    invalidReservationBucketCount > 0 || negativePhysicalBucketCount > 0 ||
                    lacksLedgerForOtherStatusOnly ? null :
                    stockTransactions.Sum(t =>
                    {
                        if (t.TransactionType == TransactionType.Move)
                            return 0m;
                        if (t.TransactionType == TransactionType.StatusChange)
                        {
                            if (t.InventoryStatus == t.FromInventoryStatus && t.TotalQuantity < 0m)
                            {
                                return t.FromInventoryStatus == InventoryStatus.Available ? t.TotalQuantity : 0m;
                            }
                            if (t.InventoryStatus == t.ToInventoryStatus && t.TotalQuantity > 0m)
                            {
                                var hasExplicitSourceLeg = stockTransactions.Any(g2 =>
                                    g2.TransactionType == TransactionType.StatusChange &&
                                    g2.FromInventoryStatus == t.FromInventoryStatus &&
                                    g2.ToInventoryStatus == t.ToInventoryStatus &&
                                    g2.InventoryStatus == t.FromInventoryStatus &&
                                    g2.NegativeQuantityEvents > 0);
                                var inDelta = t.ToInventoryStatus == InventoryStatus.Available ? t.TotalQuantity : 0m;
                                var outDelta = !hasExplicitSourceLeg && t.FromInventoryStatus == InventoryStatus.Available ? t.TotalQuantity : 0m;
                                return inDelta - outDelta;
                            }
                            return 0m;
                        }
                        return t.InventoryStatus == InventoryStatus.Available
                            ? InventoryLedgerCalculationRules.ComputeAlgebraicDelta(
                                t.TransactionType,
                                t.TotalQuantity,
                                t.HasCorrectiveTransaction ? 1 : null)
                            : 0m;
                    });
                decimal? difference = expectedQuantity.HasValue
                    ? currentQuantity - expectedQuantity.Value : null;
                string status = !difference.HasValue ? "Indeterminate" :
                    difference.Value == 0m ? "Match" : "Mismatch";

                decimal? allStatusExpected = null;
                decimal? allStatusDifference = null;
                var allStatusStatus = "Indeterminate";
                if (unclassifiedCount == 0 && !invalidStockStatus &&
                    invalidReservationBucketCount == 0 && negativePhysicalBucketCount == 0 &&
                    !lacksLedgerForOtherStatusOnly)
                {
                    var expectedByStatus = Enum.GetValues<InventoryStatus>()
                        .ToDictionary(state => state, _ => 0m);
                    foreach (var entry in stockTransactions)
                    {
                        if (entry.TransactionType == TransactionType.Move)
                        {
                            continue;
                        }
                        if (entry.TransactionType == TransactionType.StatusChange)
                        {
                            if (entry.InventoryStatus == entry.FromInventoryStatus && entry.TotalQuantity < 0m)
                            {
                                expectedByStatus[entry.FromInventoryStatus!.Value] += entry.TotalQuantity;
                            }
                            else if (entry.InventoryStatus == entry.ToInventoryStatus && entry.TotalQuantity > 0m)
                            {
                                expectedByStatus[entry.ToInventoryStatus!.Value] += entry.TotalQuantity;
                                var hasExplicitSourceLeg = stockTransactions.Any(g2 =>
                                    g2.TransactionType == TransactionType.StatusChange &&
                                    g2.FromInventoryStatus == entry.FromInventoryStatus &&
                                    g2.ToInventoryStatus == entry.ToInventoryStatus &&
                                    g2.InventoryStatus == entry.FromInventoryStatus &&
                                    g2.NegativeQuantityEvents > 0);
                                if (!hasExplicitSourceLeg)
                                {
                                    expectedByStatus[entry.FromInventoryStatus!.Value] -= entry.TotalQuantity;
                                }
                            }
                        }
                        else
                        {
                            expectedByStatus[entry.InventoryStatus] +=
                                InventoryLedgerCalculationRules.ComputeAlgebraicDelta(
                                    entry.TransactionType,
                                    entry.TotalQuantity,
                                    entry.HasCorrectiveTransaction ? 1 : null);
                        }
                    }
                    allStatusExpected = expectedByStatus.Values.Sum();
                    allStatusDifference = allStatusCurrent - allStatusExpected.Value;
                    allStatusStatus = expectedByStatus.Any(entry =>
                        entry.Value != pairStocks.Where(s => s.Status == entry.Key)
                            .Sum(s => s.Quantity)) ? "Mismatch" : "Match";
                }

                return new InventoryReconciliationDto
                {
                    ProductId = pair.ProductId,
                    ProductCode = pair.ProductCode,
                    ProductName = pair.ProductName,
                    WarehouseId = pair.WarehouseId,
                    WarehouseName = pair.WarehouseName,
                    CurrentQuantity = currentQuantity,
                    ExpectedQuantity = expectedQuantity,
                    Difference = difference,
                    AllStatusCurrentQuantity = allStatusCurrent,
                    AllStatusExpectedQuantity = allStatusExpected,
                    AllStatusDifference = allStatusDifference,
                    AllStatusStatus = allStatusStatus,
                    HistoryInsufficientForNonAvailableStock = lacksLedgerForOtherStatusOnly,
                    UnclassifiedLedgerEventCount = unclassifiedCount,
                    UnclassifiedStockBucketCount = unclassifiedStockBucketCount,
                    InvalidReservationBucketCount = invalidReservationBucketCount,
                    NegativePhysicalBucketCount = negativePhysicalBucketCount,
                    StatusChangeInQuantity = statusChangeIn,
                    StatusChangeOutQuantity = statusChangeOut,
                    ImportQuantity = importQuantity,
                    ExportQuantity = exportQuantity,
                    TransferInQuantity = transferInQuantity,
                    TransferOutQuantity = transferOutQuantity,
                    AdjustmentIncreaseQuantity = adjustmentIncreaseQuantity,
                    AdjustmentDecreaseQuantity = adjustmentDecreaseQuantity,
                    Status = status
                };
            }).ToList();

            return new PagedResult<InventoryReconciliationDto>
            {
                Items = results,
                TotalRecords = totalRecords,
                PageIndex = safePage,
                PageSize = safeSize
            };
        }
    }
}
