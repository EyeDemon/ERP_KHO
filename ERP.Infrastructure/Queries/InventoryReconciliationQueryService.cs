using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries
{
    public partial class InventoryReconciliationQueryService : IInventoryReconciliationQueryService
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
