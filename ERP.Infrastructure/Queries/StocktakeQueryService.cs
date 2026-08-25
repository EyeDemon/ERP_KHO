using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries
{
    public class StocktakeQueryService : IStocktakeQueryService
    {
        private readonly ErpKhoDbContext _context;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal StocktakeQueryService(ErpKhoDbContext context)
        {
            _context = context;
        }

        public StocktakeQueryService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _context = context;
            _warehouseAuthorization = warehouseAuthorization;
        }

        public async Task<IEnumerable<StocktakeSummaryDto>> GetStocktakesAsync()
        {
            var query = _context.Stocktakes.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
            }
            return await query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new StocktakeSummaryDto
                {
                    Id = s.Id,
                    Code = s.Code,
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name,
                    Status = s.Status,
                    Note = s.Note,
                    CreatedBy = s.CreatedBy,
                    CreatedByName = s.CreatedByUser.FullName,
                    ApprovedBy = s.ApprovedBy,
                    ApprovedByName = s.ApprovedByUser != null ? s.ApprovedByUser.FullName : null,
                    CreatedAt = s.CreatedAt,
                    ApprovedAt = s.ApprovedAt,
                    DetailCount = s.Details.Count
                })
                .ToListAsync();
        }

        public async Task<StocktakeResponseDto> GetStocktakeByIdAsync(int id)
        {
            var query = _context.Stocktakes.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(s => allowedWarehouseIds.Contains(s.WarehouseId));
            }
            var stocktake = await query
                .Where(s => s.Id == id)
                .Select(s => new StocktakeResponseDto
                {
                    Id = s.Id,
                    Code = s.Code,
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name,
                    Status = s.Status,
                    Note = s.Note,
                    CreatedBy = s.CreatedBy,
                    CreatedByName = s.CreatedByUser.FullName,
                    ApprovedBy = s.ApprovedBy,
                    ApprovedByName = s.ApprovedByUser != null ? s.ApprovedByUser.FullName : null,
                    CreatedAt = s.CreatedAt,
                    ApprovedAt = s.ApprovedAt,
                    DetailCount = s.Details.Count,
                    Details = s.Details.Select(d => new StocktakeDetailRowDto
                    {
                        Id = d.Id,
                        StocktakeId = d.StocktakeId,
                        ProductId = d.ProductId,
                        ProductCode = d.Product.Code,
                        ProductName = d.Product.Name,
                        UnitName = d.Product.Unit.Name,
                        SystemQuantity = d.SystemQuantity,
                        ActualQuantity = d.ActualQuantity,
                        DifferenceQuantity = d.DifferenceQuantity,
                        Note = d.Note
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (stocktake == null)
            {
                throw new NotFoundException("Stocktake", id);
            }

            return stocktake;
        }
    }
}
