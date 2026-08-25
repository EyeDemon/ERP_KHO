using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
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

        public async Task<PagedResult<InventoryReconciliationDto>> GetReconciliationsAsync(
            int? warehouseId,
            int? productId,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20)
        {
            var stockQuery = _context.InventoryStocks.AsNoTracking();
            var transactionQuery = _context.InventoryTransactions.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                stockQuery = stockQuery.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
                transactionQuery = transactionQuery.Where(t => allowedWarehouseIds.Contains(t.WarehouseId));
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
                var lowerKeyword = keyword.ToLower();
                query = query.Where(s => 
                    s.ProductCode.ToLower().Contains(lowerKeyword) || 
                    s.ProductName.ToLower().Contains(lowerKeyword));
            }

            var totalRecords = await query.CountAsync();

            var pagedPairs = await query
                .OrderBy(s => s.WarehouseId).ThenBy(s => s.ProductId)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var productIds = pagedPairs.Select(s => s.ProductId).Distinct().ToList();
            var warehouseIds = pagedPairs.Select(s => s.WarehouseId).Distinct().ToList();

            var stocks = await _context.InventoryStocks
                .AsNoTracking()
                .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
                .ToListAsync();

            var transactions = await _context.InventoryTransactions
                .AsNoTracking()
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
                var currentStock = stocks.FirstOrDefault(s => s.ProductId == pair.ProductId && s.WarehouseId == pair.WarehouseId);
                var currentQuantity = currentStock?.Quantity ?? 0;

                var stockTransactions = transactions
                    .Where(t => t.ProductId == pair.ProductId && t.WarehouseId == pair.WarehouseId)
                    .ToList();

                decimal Total(TransactionType type) => stockTransactions
                    .Where(t => t.TransactionType == type)
                    .Sum(t => t.TotalQuantity);

                var importQuantity = Total(TransactionType.Import);
                var exportQuantity = Total(TransactionType.Export);
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
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }
    }
}
