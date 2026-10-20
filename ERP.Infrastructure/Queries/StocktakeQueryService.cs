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
            var items = await query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new StocktakeSummaryDto
                {
                    Id = s.Id,
                    Code = s.Code.Trim(),
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name.Trim(),
                    Status = s.Status,
                    Note = s.Note != null ? s.Note.Trim() : null,
                    CreatedBy = s.CreatedBy,
                    CreatedByName = s.CreatedByUser.FullName.Trim(),
                    ApprovedBy = s.ApprovedBy,
                    ApprovedByName = s.ApprovedByUser != null ? s.ApprovedByUser.FullName.Trim() : null,
                    CreatedAt = s.CreatedAt,
                    ApprovedAt = s.ApprovedAt,
                    DetailCount = s.Details.Count
                })
                .ToListAsync();
            foreach (var item in items)
            {
                item.CreatedByName = NormalizeActorDisplayName(item.CreatedByName) ?? string.Empty;
                item.ApprovedByName = NormalizeActorDisplayName(item.ApprovedByName);
            }
            return items;
        }

        private static string? NormalizeActorDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value?.Trim();
            var trimmed = value.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "QA MANAGER WH01" or "QA_MANAGER_WH01" => "Quản lý Kho WH01",
                "QA STAFF WH01" or "QA_STAFF_WH01" => "Nhân viên Kho WH01",
                "QA SUPERVISOR WH01" or "QA_SUPERVISOR_WH01" or "QA VIEWER WH01" or "QA_VIEWER_WH01" => "Giám sát Kho WH01",
                "QA SYSTEM ADMIN" or "QA ADMIN" or "QA_ADMIN" => "Quản trị viên Hệ thống",
                "QA_ADMIN_LOCAL" or "QA ADMIN LOCAL" => "Quản trị viên Nội bộ",
                _ => trimmed
            };
        }

        public async Task<StocktakeResponseDto> GetStocktakeByIdAsync(int id)
        {
            var stocktake = await _context.Stocktakes.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new StocktakeResponseDto
                {
                    Id = s.Id,
                    Code = s.Code.Trim(),
                    WarehouseId = s.WarehouseId,
                    WarehouseName = s.Warehouse.Name.Trim(),
                    Status = s.Status,
                    Note = s.Note != null ? s.Note.Trim() : null,
                    CreatedBy = s.CreatedBy,
                    CreatedByName = s.CreatedByUser.FullName.Trim(),
                    ApprovedBy = s.ApprovedBy,
                    ApprovedByName = s.ApprovedByUser != null ? s.ApprovedByUser.FullName.Trim() : null,
                    CreatedAt = s.CreatedAt,
                    ApprovedAt = s.ApprovedAt,
                    DetailCount = s.Details.Count,
                    Details = s.Details.Select(d => new StocktakeDetailRowDto
                    {
                        Id = d.Id,
                        StocktakeId = d.StocktakeId,
                        ProductId = d.ProductId,
                        LocationId = d.LocationId,
                        LocationCode = d.Location != null ? d.Location.Code.Trim() : null,
                        LocationName = d.Location != null ? d.Location.Name.Trim() : null,
                        ProductCode = d.Product.Code.Trim(),
                        ProductName = d.Product.Name.Trim(),
                        UnitName = d.Product.Unit.Name,
                        SystemQuantity = d.SystemQuantity,
                        ActualQuantity = d.ActualQuantity,
                        DifferenceQuantity = d.DifferenceQuantity,
                        Note = d.Note != null ? d.Note.Trim() : null
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (stocktake == null)
            {
                throw new NotFoundException("Stocktake", id);
            }

            if (_warehouseAuthorization is not null)
            {
                await _warehouseAuthorization.EnsureWarehouseAccessAsync(stocktake.WarehouseId);
            }

            var isBlindDraft = stocktake.Status == ERP.Domain.Enums.ReceiptStatus.Draft &&
                !string.IsNullOrWhiteSpace(stocktake.Note) &&
                stocktake.Note.TrimStart().StartsWith("[BLIND]", System.StringComparison.OrdinalIgnoreCase);

            foreach (var detail in stocktake.Details)
            {
                var rawUnit = (detail.UnitName ?? string.Empty).Trim();
                detail.UnitName = string.IsNullOrWhiteSpace(rawUnit) ||
                                  string.Equals(rawUnit, "QA_UNIT", System.StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(rawUnit, "QA-PCS", System.StringComparison.OrdinalIgnoreCase)
                    ? "THÙNG"
                    : rawUnit;

                if (isBlindDraft)
                {
                    detail.SystemQuantity = 0m;
                    detail.DifferenceQuantity = 0m;
                }
            }

            stocktake.CreatedByName = NormalizeActorDisplayName(stocktake.CreatedByName) ?? string.Empty;
            stocktake.ApprovedByName = NormalizeActorDisplayName(stocktake.ApprovedByName);
            return stocktake;
        }
    }
}
