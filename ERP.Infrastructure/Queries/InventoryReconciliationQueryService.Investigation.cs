using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries
{
    public partial class InventoryReconciliationQueryService
    {
        public async Task<InventoryReconciliationInvestigationDto> GetInvestigationAsync(
            int warehouseId, int productId, int? eventAnchorId = null, int limit = 50,
            int? eventBeforeId = null, int? bucketAnchorId = null,
            int? bucketAfterId = null, string bucketStatus = "Available",
            string eventStatus = "Available", int? allStatusBucketAnchorId = null,
            string? allStatusBucketAggregateFingerprint = null)
        {
            ValidateInvestigationParameters(
                warehouseId, productId, eventAnchorId, limit, eventBeforeId,
                bucketAnchorId, bucketAfterId, bucketStatus, eventStatus,
                allStatusBucketAnchorId, allStatusBucketAggregateFingerprint,
                out var selectedBucketStatus, out var selectedEventStatus);
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
                .Select(g => new InvestigationStockGroup(
                    g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.ReservedQuantity), g.Count(),
                    g.Count(x => x.ReservedQuantity < 0m || x.ReservedQuantity > x.Quantity),
                    g.Count(x => x.Quantity < 0m))).ToListAsync();
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
                .Select(g => new InvestigationLedgerGroup(
                    g.Key.InventoryStatus, g.Key.TransactionType,
                    g.Key.FromInventoryStatus, g.Key.ToInventoryStatus,
                    g.Key.HasCorrectiveTransaction,
                    g.Sum(x => x.Quantity), g.Count(),
                    g.Count(x => x.Quantity < 0m),
                    g.Count(x => x.Quantity <= 0m),
                    g.Count(x => x.Quantity >= 0m),
                    g.Count(x => x.Quantity == 0m))).ToListAsync();
            if (stockGroups.Count == 0 && ledgerGroups.Count == 0)
                throw new NotFoundException("Không có bucket hoặc sự kiện sổ cái cho kho và sản phẩm này.");

            var breakdownInfo = BuildInvestigationBreakdown(stockGroups, ledgerGroups);
            var evidenceAnchorsStale = hasNewEvents || bucketHasRowsAfterAnchor ||
                allStatusBucketHasRowsAfterAnchor || aggregateChanged;
            var snapshotComparisonStatus = evidenceAnchorsStale
                ? "Indeterminate" : breakdownInfo.AllStatusStatus;

            var currentQuantity = await stocks.SumAsync(x => (decimal?)x.Quantity) ?? 0m;
            var grouped = await anchoredLedger
                .GroupBy(x => x.TransactionType)
                .Select(x => new
                {
                    Type = x.Key, Quantity = x.Sum(t => t.Quantity),
                    NegativeQuantityEvents = x.Count(t => t.Quantity < 0m)
                })
                .ToListAsync();
            var availableLedgerExpectedIsPartial = !breakdownInfo.Comparable;
            var legacyClassifiedQuantity = grouped
                .Where(x => x.NegativeQuantityEvents == 0 &&
                    x.Type != TransactionType.TransferAdjustment &&
                    x.Type != TransactionType.Move &&
                    x.Type != TransactionType.StatusChange &&
                    Enum.IsDefined(x.Type))
                .Sum(x => x.Type.ApplySign(x.Quantity));
            var expectedQuantity = breakdownInfo.Comparable
                ? breakdownInfo.ByStatus[InventoryStatus.Available].ExpectedQuantity!.Value
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
                    ? null : breakdownInfo.AllStatusExpected,
                SnapshotComparisonDifference = evidenceAnchorsStale || !breakdownInfo.AllStatusExpected.HasValue
                    ? null : breakdownInfo.AllStatusCurrent - breakdownInfo.AllStatusExpected.Value,
                EventCount = eventCount,
                BucketCount = bucketCount,
                CurrentQuantity = currentQuantity,
                ExpectedQuantity = expectedQuantity,
                Difference = currentQuantity - expectedQuantity,
                AvailableLedgerExpectedIsPartial = availableLedgerExpectedIsPartial,
                HistoryInsufficientForNonAvailableStock = breakdownInfo.HistoryInsufficientForNonAvailableStock,
                AllStatusCurrentQuantity = breakdownInfo.AllStatusCurrent,
                AllStatusReservedQuantity = breakdownInfo.AllStatusReserved,
                AllStatusStatus = breakdownInfo.AllStatusStatus,
                AllStatusExpectedQuantity = breakdownInfo.AllStatusExpected,
                AllStatusDifference = breakdownInfo.AllStatusExpected.HasValue
                    ? breakdownInfo.AllStatusCurrent - breakdownInfo.AllStatusExpected.Value
                    : null,
                UnclassifiedLedgerEventCount = breakdownInfo.UnclassifiedCount,
                UnclassifiedStockBucketCount = breakdownInfo.UnclassifiedStockBucketCount,
                InvalidReservationBucketCount = breakdownInfo.InvalidReservationBucketCount,
                NegativePhysicalBucketCount = breakdownInfo.NegativePhysicalBucketCount,
                StatusBreakdown = breakdownInfo.StatusBreakdown,
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
    }
}
