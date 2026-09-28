using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ERP.Domain.Enums;

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
            var query = _context.InventoryStocks.AsNoTracking().Where(s => s.Status == InventoryStatus.Available && (!s.LocationId.HasValue || (s.Location != null && s.Location.IsActive && !s.Location.IsBlocked && s.Location.IsPickable)));
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

            var projectedQuery = query.GroupBy(s=>new{s.ProductId,s.Product.Code,s.Product.Name,UnitName=s.Product.Unit!=null?s.Product.Unit.Name:string.Empty,s.WarehouseId,WarehouseName=s.Warehouse.Name}).Select(g => new InventoryStockDto
            {
                ProductId = g.Key.ProductId, ProductCode=g.Key.Code, ProductName=g.Key.Name, UnitName=g.Key.UnitName,
                WarehouseId=g.Key.WarehouseId, WarehouseName=g.Key.WarehouseName, Quantity=g.Sum(s=>s.Quantity), OnHandQuantity=g.Sum(s=>s.Quantity),
                ReservedQuantity=g.Sum(s=>s.ReservedQuantity), AvailableQuantity=g.Sum(s=>s.Quantity-s.ReservedQuantity), LastUpdated=g.Max(s=>s.LastUpdated)
            });

            return await projectedQuery.ToListAsync();
        }
    }
}
