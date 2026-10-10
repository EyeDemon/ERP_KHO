using System;
using System.Linq;
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
            string eventStatus = "Available")
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
            if (bucketAfterId.HasValue && (bucketAfterId.Value <= 0 ||
                !bucketAnchorId.HasValue || bucketAfterId.Value >= bucketAnchorId.Value))
                throw new BusinessRuleException("Phân trang bucket yêu cầu ID sau hợp lệ và mốc bucket cố định.");
            // Named canonical enum values only: no numeric status injection or
            // silently widened scope before checking warehouse authorization.
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

            await _warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId);

            // Scope every query by the authorized warehouse and exact product,
            // including all second-phase detail reads.
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

            var stocks = _context.InventoryStocks.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId &&
                            x.Status == InventoryStatus.Available);
            var allStatusStocks = _context.InventoryStocks.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId);
            var selectedStatusStocks = allStatusStocks.Where(x => x.Status == selectedBucketStatus);
            var allStatusLedger = _context.InventoryTransactions.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId);
            var ledger = allStatusLedger.Where(x => x.InventoryStatus == InventoryStatus.Available);
            // A validated status transfer contributes in both directions:
            // source (-quantity) and destination (+quantity). Preserve malformed
            // transfers as visible unknown-sign evidence in any recorded scope.
            var selectedEventLedger = allStatusLedger.Where(x =>
                x.TransactionType == TransactionType.StatusChange
                    ? x.InventoryStatus == selectedEventStatus ||
                      x.FromInventoryStatus == selectedEventStatus ||
                      x.ToInventoryStatus == selectedEventStatus
                    : x.InventoryStatus == selectedEventStatus);

            // Use the warehouse/product-wide high-water mark, not just AVAILABLE,
            // so a later QC/Quarantine status event cannot be silently excluded.
            var anchor = eventAnchorId ?? await allStatusLedger.MaxAsync(x => (int?)x.Id) ?? 0;
            var anchoredAllStatusLedger = allStatusLedger.Where(x => x.Id <= anchor);
            var anchoredLedger = ledger.Where(x => x.Id <= anchor);
            // Recheck within the authorized scope on each page request.
            var hasNewEvents = await allStatusLedger.AnyAsync(x => x.Id > anchor);

            // Use a monotonically increasing identity cursor instead of OFFSET.
            // This bounds new bucket INSERTs, but does not freeze mutable stock
            // quantities/statuses across requests. Every page rechecks access.
            var bucketAnchor = bucketAnchorId ?? await selectedStatusStocks.MaxAsync(x => (int?)x.Id) ?? 0;
            var anchoredStocks = selectedStatusStocks.Where(x => x.Id <= bucketAnchor);
            var bucketCount = await anchoredStocks.CountAsync();
            var bucketHasRowsAfterAnchor = await selectedStatusStocks.AnyAsync(x => x.Id > bucketAnchor);
            var eventCount = await selectedEventLedger.CountAsync(x => x.Id <= anchor);

            // SQL aggregation bounds materialization to the status enum, rather
            // than loading all bucket rows or all transaction history.
            var stockGroups = await allStatusStocks
                .GroupBy(x => x.Status)
                .Select(g => new
                {
                    Status = g.Key, Quantity = g.Sum(x => x.Quantity),
                    Reserved = g.Sum(x => x.ReservedQuantity), Buckets = g.Count()
                }).ToListAsync();
            var ledgerGroups = await anchoredAllStatusLedger
                .GroupBy(x => new
                {
                    x.InventoryStatus, x.TransactionType,
                    x.FromInventoryStatus, x.ToInventoryStatus
                })
                .Select(g => new
                {
                    g.Key.InventoryStatus, g.Key.TransactionType,
                    g.Key.FromInventoryStatus, g.Key.ToInventoryStatus,
                    Quantity = g.Sum(x => x.Quantity), Events = g.Count(),
                    // SQL SUM may cancel a malformed negative with a positive
                    // in the same group. Validate individual row signs in SQL.
                    NegativeQuantityEvents = g.Count(x => x.Quantity < 0m),
                    NonPositiveQuantityEvents = g.Count(x => x.Quantity <= 0m)
                }).ToListAsync();
            if (stockGroups.Count == 0 && ledgerGroups.Count == 0)
                throw new NotFoundException("Không có bucket hoặc sự kiện sổ cái cho kho và sản phẩm này.");

            var byStatus = Enum.GetValues<InventoryStatus>()
                .ToDictionary(status => status,
                    status => new InventoryReconciliationStatusEvidenceDto
                    {
                        Status = status.ToString()
                    });
            foreach (var stockGroup in stockGroups)
            {
                if (!byStatus.TryGetValue(stockGroup.Status, out var entry))
                    throw new BusinessRuleException("Tồn kho có trạng thái chưa được hệ thống hỗ trợ.");
                entry.CurrentQuantity = stockGroup.Quantity;
                entry.ReservedQuantity = stockGroup.Reserved;
                entry.BucketCount = stockGroup.Buckets;
            }

            // A StatusChange moves quantity between statuses, but has no effect
            // on total warehouse on-hand. Move/Reversal are zero-sum markers.
            // TransferAdjustment is not signed in the canonical mapper; an
            // incomplete/unknown legacy event poisons expected totals instead
            // of being silently treated as zero.
            var unclassifiedCount = 0;
            foreach (var group in ledgerGroups)
            {
                if (group.TransactionType == TransactionType.StatusChange)
                {
                    if (group.FromInventoryStatus is not { } from ||
                        group.ToInventoryStatus is not { } to ||
                        from == to || group.InventoryStatus != to ||
                        !byStatus.ContainsKey(from) || !byStatus.ContainsKey(to))
                    {
                        unclassifiedCount += group.Events;
                        continue;
                    }
                    // Mixed valid/invalid rows cannot be rebuilt using their
                    // aggregate sum, even if it is strictly positive.
                    if (group.NonPositiveQuantityEvents > 0)
                    {
                        unclassifiedCount += group.NonPositiveQuantityEvents;
                        continue;
                    }
                    byStatus[from].StatusChangeOutQuantity += group.Quantity;
                    byStatus[to].StatusChangeInQuantity += group.Quantity;
                    continue;
                }
                if (!byStatus.TryGetValue(group.InventoryStatus, out var current) ||
                    group.TransactionType == TransactionType.TransferAdjustment ||
                    !Enum.IsDefined(group.TransactionType))
                {
                    unclassifiedCount += group.Events;
                    continue;
                }
                if (group.NegativeQuantityEvents > 0)
                {
                    unclassifiedCount += group.NegativeQuantityEvents;
                    continue;
                }
                current.DirectLedgerNetQuantity +=
                    group.TransactionType.ApplySign(group.Quantity);
            }
            var comparable = unclassifiedCount == 0;
            foreach (var row in byStatus.Values)
            {
                if (!comparable) continue;
                row.ExpectedQuantity = row.DirectLedgerNetQuantity +
                    row.StatusChangeInQuantity - row.StatusChangeOutQuantity;
                row.Difference = row.CurrentQuantity - row.ExpectedQuantity.Value;
            }
            var statusBreakdown = byStatus.Values.ToList();
            var allStatusCurrent = statusBreakdown.Sum(x => x.CurrentQuantity);
            var allStatusReserved = statusBreakdown.Sum(x => x.ReservedQuantity);
            decimal? allStatusExpected = comparable
                ? statusBreakdown.Sum(x => x.ExpectedQuantity!.Value)
                : null;

            var currentQuantity = await stocks.SumAsync(x => (decimal?)x.Quantity) ?? 0m;
            // Compute the signed quantity from a small bounded transaction-type
            // aggregate, not by materializing the entire immutable history.
            var grouped = await anchoredLedger
                .GroupBy(x => x.TransactionType)
                .Select(x => new
                {
                    Type = x.Key, Quantity = x.Sum(t => t.Quantity),
                    NegativeQuantityEvents = x.Count(t => t.Quantity < 0m)
                })
                .ToListAsync();
            // Legacy TransferAdjustment does not have a canonical sign.
            // Never crash the investigation or silently present the partial
            // AVAILABLE sum as complete when such an event is encountered.
            var availableLedgerExpectedIsPartial = grouped.Any(x =>
                x.NegativeQuantityEvents > 0 ||
                x.Type == TransactionType.TransferAdjustment || !Enum.IsDefined(x.Type));
            // A mixed-sign group is partial in its entirety: never let a
            // negative historical row disappear into a positive SUM.
            var expectedQuantity = grouped
                .Where(x => x.NegativeQuantityEvents == 0 &&
                    x.Type != TransactionType.TransferAdjustment &&
                    Enum.IsDefined(x.Type))
                .Sum(x => x.Type.ApplySign(x.Quantity));

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

            // Stable keyset paging: never use TransactionDate (backdatable)
            // or Skip/Take offsets (new rows can shift boundaries).
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

            return new InventoryReconciliationInvestigationDto
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
                EventBeforeId = eventBeforeId,
                NextEventBeforeId = hasOlderEvents && page.Count > 0 ? page[^1].Id : null,
                LedgerHasEventsAfterAnchor = hasNewEvents,
                EventCount = eventCount,
                BucketCount = bucketCount,
                CurrentQuantity = currentQuantity,
                ExpectedQuantity = expectedQuantity,
                Difference = currentQuantity - expectedQuantity,
                AvailableLedgerExpectedIsPartial = availableLedgerExpectedIsPartial,
                AllStatusCurrentQuantity = allStatusCurrent,
                AllStatusReservedQuantity = allStatusReserved,
                AllStatusExpectedQuantity = allStatusExpected,
                AllStatusDifference = allStatusExpected.HasValue
                    ? allStatusCurrent - allStatusExpected.Value
                    : null,
                UnclassifiedLedgerEventCount = unclassifiedCount,
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
                        // An unmapped legacy event is visible as evidence,
                        // but it must never be given a fabricated zero sign.
                        SignedQuantity = x.Quantity < 0m ||
                            x.TransactionType == TransactionType.TransferAdjustment ||
                            !Enum.IsDefined(x.TransactionType)
                            ? null : x.TransactionType == TransactionType.StatusChange
                                ? x.Quantity <= 0m ||
                                  x.FromInventoryStatus is not { } from ||
                                  x.ToInventoryStatus is not { } to ||
                                  from == to || !Enum.IsDefined(from) ||
                                  !Enum.IsDefined(to) || x.InventoryStatus != to
                                    ? null : selectedEventStatus == to
                                        ? x.Quantity : selectedEventStatus == from
                                            ? -x.Quantity : null
                                : x.TransactionType.ApplySign(x.Quantity),
                        ReferenceType = x.ReferenceType,
                        ReferenceId = x.ReferenceId,
                        TransactionDate = x.TransactionDate
                    }).ToList()
            };
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

            // Bound client pagination inputs before translating Skip/Take to SQL.
            var safePage = Math.Max(1, pageIndex);
            var safeSize = Math.Clamp(pageSize, 1, 100);
            // Discover pairs from ALL stock statuses, including legacy QC-only
            // buckets with no Ledger. Restrict AVAILABLE only when computing
            // the legacy summary balance, never when choosing visible pairs.
            var allStatusStockQuery = _context.InventoryStocks.AsNoTracking();
            // Include every status: StatusChange leaving AVAILABLE is
            // recorded against its destination status. Unknown events in
            // this warehouse/product cannot be assumed to be harmless.
            var transactionQuery = _context.InventoryTransactions.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                if (warehouseId.HasValue)
                {
                    // An unknown or unassigned warehouse is a 404, never a
                    // success with an empty result that could hide access errors.
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
            var stockQuery = allStatusStockQuery.Where(s => s.Status == InventoryStatus.Available);
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

            // Count is Int32, so an offset at or beyond count is always an empty
            // page. Avoid Int32 overflow/negative Skip for extreme page numbers.
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

            // Aggregate in SQL instead of materializing every physical bucket.
            // The authorization filter applies before BOTH phases. Grouped
            // results are bounded by the current page's product/warehouse sets.
            var stocks = await allStatusStockQuery
                .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
                .GroupBy(s => new { s.ProductId, s.WarehouseId })
                .Select(g => new
                {
                    g.Key.ProductId, g.Key.WarehouseId,
                    AvailableQuantity = g.Sum(s => s.Status == InventoryStatus.Available ? s.Quantity : 0m),
                    AvailableBuckets = g.Count(s => s.Status == InventoryStatus.Available),
                    OtherStatusBuckets = g.Count(s => s.Status != InventoryStatus.Available)
                }).ToListAsync();

            var transactions = await transactionQuery
                .Where(t => productIds.Contains(t.ProductId) && warehouseIds.Contains(t.WarehouseId))
                .GroupBy(t => new
                {
                    t.ProductId, t.WarehouseId, t.TransactionType,
                    t.InventoryStatus, t.FromInventoryStatus, t.ToInventoryStatus
                })
                .Select(g => new
                {
                    g.Key.ProductId, g.Key.WarehouseId, g.Key.TransactionType,
                    g.Key.InventoryStatus, g.Key.FromInventoryStatus, g.Key.ToInventoryStatus,
                    TotalQuantity = g.Sum(t => t.Quantity),
                    EventCount = g.Count(),
                    NegativeQuantityEvents = g.Count(t => t.Quantity < 0m),
                    NonPositiveQuantityEvents = g.Count(t => t.Quantity <= 0m)
                })
                .ToListAsync();

            var results = pagedPairs.Select(pair => 
            {
                var stock = stocks.SingleOrDefault(s =>
                    s.ProductId == pair.ProductId && s.WarehouseId == pair.WarehouseId);
                var currentQuantity = stock?.AvailableQuantity ?? 0m;

                var stockTransactions = transactions
                    .Where(t => t.ProductId == pair.ProductId && t.WarehouseId == pair.WarehouseId)
                    .ToList();

                decimal Total(TransactionType type) => stockTransactions
                    .Where(t => t.InventoryStatus == InventoryStatus.Available &&
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
                                t.ToInventoryStatus == InventoryStatus.Available)
                    .Sum(t => t.TotalQuantity);
                var statusChangeOut = stockTransactions
                    .Where(t => t.TransactionType == TransactionType.StatusChange &&
                                t.FromInventoryStatus == InventoryStatus.Available)
                    .Sum(t => t.TotalQuantity);

                // Reject incomplete history at pair level rather than silently
                // mapping TransferAdjustment or a malformed StatusChange to zero.
                // Count negative individual rows before grouping: positive and
                // negative rows could otherwise cancel in SQL SUM.
                var unclassifiedCount = stockTransactions.Sum(t =>
                {
                    if (!Enum.IsDefined(t.InventoryStatus) ||
                        !Enum.IsDefined(t.TransactionType) ||
                        t.TransactionType == TransactionType.TransferAdjustment)
                        return t.EventCount;
                    if (t.TransactionType != TransactionType.StatusChange)
                        return t.NegativeQuantityEvents;
                    if (t.FromInventoryStatus is not { } from ||
                        t.ToInventoryStatus is not { } to ||
                        from == to || t.InventoryStatus != to ||
                        !Enum.IsDefined(from) || !Enum.IsDefined(to))
                        return t.EventCount;
                    // Zero/negative status transfers remain invalid even
                    // when other positive rows hide them in an aggregate.
                    return t.NonPositiveQuantityEvents;
                });
                // A non-AVAILABLE-only legacy stock pair without any Ledger
                // cannot be certified as an AVAILABLE Match just because
                // 0 - 0 = 0. Preserve it in the report as Indeterminate,
                // with read-only investigation available for all statuses.
                var lacksLedgerForOtherStatusOnly =
                    (stock?.OtherStatusBuckets ?? 0) > 0 &&
                    (stock?.AvailableBuckets ?? 0) == 0 &&
                    stockTransactions.Count == 0;
                decimal? expectedQuantity =
                    unclassifiedCount > 0 || lacksLedgerForOtherStatusOnly ? null :
                    stockTransactions.Sum(t =>
                    {
                        if (t.TransactionType == TransactionType.StatusChange)
                            return (t.ToInventoryStatus == InventoryStatus.Available
                                    ? t.TotalQuantity : 0m) -
                                   (t.FromInventoryStatus == InventoryStatus.Available
                                    ? t.TotalQuantity : 0m);
                        return t.InventoryStatus == InventoryStatus.Available
                            ? t.TransactionType.ApplySign(t.TotalQuantity) : 0m;
                    });
                decimal? difference = expectedQuantity.HasValue
                    ? currentQuantity - expectedQuantity.Value : null;
                string status = !difference.HasValue ? "Indeterminate" :
                    difference.Value == 0m ? "Match" : "Mismatch";

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
                    UnclassifiedLedgerEventCount = unclassifiedCount,
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
