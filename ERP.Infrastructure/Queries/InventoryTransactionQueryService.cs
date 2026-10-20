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
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);

            var query = _context.InventoryTransactions.AsNoTracking();
            if (_warehouseAuthorization is not null)
            {
                if (warehouseId.HasValue)
                {
                    await _warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value);
                }
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
            var offset = ((long)pageIndex - 1) * pageSize;
            if (offset >= totalRecords)
            {
                return new PagedResult<InventoryTransactionHistoryDto>
                {
                    Items = [],
                    TotalRecords = totalRecords,
                    PageIndex = pageIndex,
                    PageSize = pageSize
                };
            }

            // Order by date desc, then Id desc for deterministic pagination
            var pagedQuery = query
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.Id)
                .Skip(checked((int)offset))
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
                InventoryStatus = t.InventoryStatus.ToString(),
                FromInventoryStatus = t.FromInventoryStatus.HasValue ? t.FromInventoryStatus.Value.ToString() : null,
                ToInventoryStatus = t.ToInventoryStatus.HasValue ? t.ToInventoryStatus.Value.ToString() : null,
                LotId = t.LotId,
                LotNumber = t.Lot != null ? t.Lot.LotNumber : null,
                ExpiryDate = t.Lot != null ? t.Lot.ExpiryDate : null,
                SerialId = t.SerialId,
                SerialNumber = t.Serial != null ? t.Serial.SerialNumber : null,
                Quantity = t.Quantity,
                ReferenceId = t.ReferenceId,
                ReferenceType = t.ReferenceType,
                TransactionDate = t.TransactionDate,
                CreatedBy = t.CreatedBy,
                CreatedByName = t.CreatedByUser != null ? (t.CreatedByUser.FullName ?? t.CreatedByUser.Username) : string.Empty,
                Note = t.Note
            });

            var items = await projectedQuery.ToListAsync();
            foreach (var item in items)
            {
                item.ProductCode = (item.ProductCode ?? string.Empty).Trim();
                item.ProductName = (item.ProductName ?? string.Empty).Trim();
                item.WarehouseName = (item.WarehouseName ?? string.Empty).Trim();
                var rawUnit = (item.UnitName ?? string.Empty).Trim();
                item.UnitName = string.IsNullOrWhiteSpace(rawUnit) ||
                                string.Equals(rawUnit, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rawUnit, "QA-PCS", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rawUnit, "QA-BOX", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(rawUnit, "QA_BOX", StringComparison.OrdinalIgnoreCase)
                    ? "THÙNG"
                    : rawUnit;
                item.LotNumber = item.LotNumber?.Trim();
                item.SerialNumber = item.SerialNumber?.Trim();
                item.ReferenceType = item.ReferenceType?.Trim();
                item.TransactionType = (item.TransactionType ?? string.Empty).Trim();
                item.InventoryStatus = (item.InventoryStatus ?? string.Empty).Trim();
                item.FromInventoryStatus = item.FromInventoryStatus?.Trim();
                item.ToInventoryStatus = item.ToInventoryStatus?.Trim();
                item.CreatedByName = NormalizeActorDisplayName(item.CreatedByName) ?? string.Empty;
                item.Note = item.Note?.Trim();
            }

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
