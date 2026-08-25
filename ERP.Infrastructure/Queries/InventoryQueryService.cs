using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries
{
    public class InventoryQueryService : IInventoryQueryService
    {
        private readonly ErpKhoDbContext _context;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal InventoryQueryService(ErpKhoDbContext context)
        {
            _context = context;
        }

        public InventoryQueryService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _context = context;
            _warehouseAuthorization = warehouseAuthorization;
        }

        public async Task<IEnumerable<InventoryStockDto>> GetCurrentStockAsync(int? warehouseId, int? productId, string? keyword, decimal? lowStockThreshold)
        {
            var query = _context.InventoryStocks.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
            }

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
                query = query.Where(s => s.Product.Code.ToLower().Contains(lowerKeyword) || 
                                         s.Product.Name.ToLower().Contains(lowerKeyword));
            }

            if (lowStockThreshold.HasValue)
            {
                query = query.Where(s => s.Quantity - s.ReservedQuantity <= lowStockThreshold.Value);
            }

            var projectedQuery = query.Select(s => new InventoryStockDto
            {
                ProductId = s.ProductId,
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                UnitName = s.Product.Unit != null ? s.Product.Unit.Name : string.Empty,
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                Quantity = s.Quantity,
                OnHandQuantity = s.Quantity,
                ReservedQuantity = s.ReservedQuantity,
                AvailableQuantity = s.Quantity - s.ReservedQuantity,
                LastUpdated = s.LastUpdated
            });

            return await projectedQuery.ToListAsync();
        }
    }
}
