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
            int warehouseId, int productId, int? eventAnchorId = null, int limit = 50)
        {
            if (warehouseId <= 0 || productId <= 0)
                throw new BusinessRuleException("ID kho và ID sản phẩm phải là số nguyên dương.");
            if (eventAnchorId is < 0)
                throw new BusinessRuleException("Mốc lịch sử sổ cái không hợp lệ.");
            if (limit is < 1 or > 100)
                throw new BusinessRuleException("Giới hạn sự kiện phải nằm từ 1 đến 100.");
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
            var ledger = _context.InventoryTransactions.AsNoTracking()
                .Where(x => x.WarehouseId == warehouseId && x.ProductId == productId &&
                            x.InventoryStatus == InventoryStatus.Available);

            // Immutable monotonically increasing transaction IDs: a returned
            // anchor never expands because newer events were appended.
            var anchor = eventAnchorId ?? await ledger.MaxAsync(x => (int?)x.Id) ?? 0;
            var anchoredLedger = ledger.Where(x => x.Id <= anchor);

            var bucketCount = await stocks.CountAsync();
            var eventCount = await anchoredLedger.CountAsync();
            if (bucketCount == 0 && eventCount == 0)
                throw new NotFoundException("Không có bucket hoặc sự kiện sổ cái cho kho và sản phẩm này.");

            var currentQuantity = await stocks.SumAsync(x => (decimal?)x.Quantity) ?? 0m;
            // Compute the signed quantity from a small bounded transaction-type
            // aggregate, not by materializing the entire immutable history.
            var grouped = await anchoredLedger
                .GroupBy(x => x.TransactionType)
                .Select(x => new { Type = x.Key, Quantity = x.Sum(t => t.Quantity) })
                .ToListAsync();
            var expectedQuantity = grouped.Sum(x => x.Type.ApplySign(x.Quantity));

            var buckets = await stocks
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

            var rawEvents = await anchoredLedger
                .OrderByDescending(x => x.Id)
                .Take(limit + 1)
                .Select(x => new
                {
                    x.Id, x.TransactionType, x.LocationId,
                    LocationCode = x.Location != null ? x.Location.Code : null,
                    x.LotId, LotNumber = x.Lot != null ? x.Lot.LotNumber : null,
                    x.SerialId, SerialNumber = x.Serial != null ? x.Serial.SerialNumber : null,
                    x.Quantity, x.ReferenceType, x.ReferenceId, x.TransactionDate
                })
                .ToListAsync();

            return new InventoryReconciliationInvestigationDto
            {
                WarehouseId = warehouseId,
                WarehouseName = warehouseName,
                ProductId = productId,
                ProductCode = product.Code,
                ProductName = product.Name,
                EventAnchorId = anchor,
                EventCount = eventCount,
                BucketCount = bucketCount,
                CurrentQuantity = currentQuantity,
                ExpectedQuantity = expectedQuantity,
                Difference = currentQuantity - expectedQuantity,
                EventsTruncated = rawEvents.Count > limit,
                BucketsTruncated = buckets.Count > 100,
                Buckets = buckets.Take(100).ToList(),
                Events = rawEvents.Take(limit).Select(x =>
                    new InventoryReconciliationEvidenceEventDto
                    {
                        TransactionId = x.Id,
                        TransactionType = x.TransactionType.ToString(),
                        LocationId = x.LocationId,
                        LocationCode = x.LocationCode,
                        LotId = x.LotId,
                        LotNumber = x.LotNumber,
                        SerialId = x.SerialId,
                        SerialNumber = x.SerialNumber,
                        Quantity = x.Quantity,
                        SignedQuantity = x.TransactionType.ApplySign(x.Quantity),
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
            var stockQuery = _context.InventoryStocks.AsNoTracking().Where(s => s.Status == InventoryStatus.Available);
            var transactionQuery = _context.InventoryTransactions.AsNoTracking().Where(t => t.InventoryStatus == InventoryStatus.Available);
            if (_warehouseAuthorization is not null)
            {
                if (warehouseId.HasValue)
                {
                    // An unknown or unassigned warehouse is a 404, never a
                    // success with an empty result that could hide access errors.
                    await _warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value);
                    stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    transactionQuery = transactionQuery.Where(t => t.WarehouseId == warehouseId.Value);
                }
                else
                {
                    var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                    stockQuery = stockQuery.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
                    transactionQuery = transactionQuery.Where(t => allowedWarehouseIds.Contains(t.WarehouseId));
                }
            }
            var stockPairs = stockQuery.Select(s => new { s.ProductId, s.WarehouseId });
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

            // Reuse the already-authorized base query for both second-phase
            // reads. The paged product/warehouse cross-product must not cause
            // raw inventory from an unrelated warehouse to be materialized.
            var stocks = await stockQuery
                .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
                .ToListAsync();

            var transactions = await transactionQuery
                .Where(t => productIds.Contains(t.ProductId) && warehouseIds.Contains(t.WarehouseId))
                .GroupBy(t => new { t.ProductId, t.WarehouseId, t.TransactionType })
                .Select(g => new 
                {
                    g.Key.ProductId,
                    g.Key.WarehouseId,
                    g.Key.TransactionType,
                    TotalQuantity = g.Sum(t => t.Quantity)
                })
                .ToListAsync();

            var results = pagedPairs.Select(pair => 
            {
                var currentQuantity = stocks
                    .Where(s => s.ProductId == pair.ProductId && s.WarehouseId == pair.WarehouseId)
                    .Sum(s => s.Quantity);

                var stockTransactions = transactions
                    .Where(t => t.ProductId == pair.ProductId && t.WarehouseId == pair.WarehouseId)
                    .ToList();

                decimal Total(TransactionType type) => stockTransactions
                    .Where(t => t.TransactionType == type)
                    .Sum(t => t.TotalQuantity);

                var importQuantity = Total(TransactionType.Import);
                var exportQuantity = Total(TransactionType.Export) + Total(TransactionType.Ship);
                var transferInQuantity = Total(TransactionType.TransferIn);
                var transferOutQuantity = Total(TransactionType.TransferOut);
                var adjustmentIncreaseQuantity = Total(TransactionType.AdjustmentIncrease);
                var adjustmentDecreaseQuantity = Total(TransactionType.AdjustmentDecrease);

                var expectedQuantity = stockTransactions.Sum(t =>
                    t.TransactionType.ApplySign(t.TotalQuantity));

                decimal difference = currentQuantity - expectedQuantity;
                string status = difference == 0 ? "Match" : "Mismatch";

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
