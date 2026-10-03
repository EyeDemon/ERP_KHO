using System;
using System.Collections.Generic;
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
    public class InventoryTransactionQueryService : IInventoryTransactionQueryService
    {
        private readonly ErpKhoDbContext _context;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal InventoryTransactionQueryService(ErpKhoDbContext context)
        {
            _context = context;
        }

        public InventoryTransactionQueryService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _context = context;
            _warehouseAuthorization = warehouseAuthorization;
        }

        public async Task<PagedResult<InventoryTransactionHistoryDto>> GetHistoryAsync(
            DateTime? fromDate,
            DateTime? toDate,
            TransactionType? transactionType,
            int? warehouseId,
            int? productId,
            int? referenceId,
            string? referenceType,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20)
        {
            var query = _context.InventoryTransactions.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(t => allowedWarehouseIds.Contains(t.WarehouseId));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(t => t.TransactionDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(t => t.TransactionDate <= toDate.Value);
            }

            if (transactionType.HasValue)
            {
                query = query.Where(t => t.TransactionType == transactionType.Value);
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            }

            if (productId.HasValue)
            {
                query = query.Where(t => t.ProductId == productId.Value);
            }

            if (referenceId.HasValue)
            {
                query = query.Where(t => t.ReferenceId == referenceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(referenceType))
            {
                var lowerRefType = referenceType.ToLower();
                query = query.Where(t => t.ReferenceType != null && t.ReferenceType.ToLower().Contains(lowerRefType));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lowerKeyword = keyword.ToLower();
                query = query.Where(t => t.Product.Code.ToLower().Contains(lowerKeyword) || 
                                         t.Product.Name.ToLower().Contains(lowerKeyword));
            }

            // Count total
            var totalRecords = await query.CountAsync();

            // Order by date desc, then apply paging
            var pagedQuery = query
                .OrderByDescending(t => t.TransactionDate)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize);

            // Projection
            var projectedQuery = pagedQuery.Select(t => new InventoryTransactionHistoryDto
            {
                Id = t.Id,
                ProductId = t.ProductId,
                ProductCode = t.Product.Code,
                ProductName = t.Product.Name,
                UnitName = t.Product.Unit != null ? t.Product.Unit.Name : string.Empty,
                WarehouseId = t.WarehouseId,
                WarehouseName = t.Warehouse.Name,
                TransactionType = t.TransactionType.ToString(),
                Quantity = t.Quantity,
                ReferenceId = t.ReferenceId,
                ReferenceType = t.ReferenceType,
                TransactionDate = t.TransactionDate,
                CreatedBy = t.CreatedBy,
                CreatedByName = t.CreatedByUser != null ? (t.CreatedByUser.FullName ?? t.CreatedByUser.Username) : string.Empty,
                Note = t.Note
            });

            var items = await projectedQuery.ToListAsync();

            return new PagedResult<InventoryTransactionHistoryDto>
            {
                Items = items,
                TotalRecords = totalRecords,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }
    }
}
