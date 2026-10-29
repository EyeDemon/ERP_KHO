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

        public static string? TranslateInventoryStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return status?.Trim();
            var trimmed = status.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "AVAILABLE" => "Khả dụng",
                "QUARANTINE" => "Cách ly",
                "DAMAGED" => "Hư hỏng",
                "REJECTED" => "Từ chối",
                "EXPIRED" => "Đã hết hạn",
                "QCHOLD" or "QC_HOLD" => "Tạm giữ chờ kiểm định",
                "RECALLBLOCKED" or "RECALL_BLOCKED" => "Phong tỏa thu hồi",
                "BLOCKED" => "Phong tỏa",
                _ => trimmed
            };
        }

        public static string? TranslateReferenceType(string? referenceType)
        {
            if (string.IsNullOrWhiteSpace(referenceType)) return referenceType?.Trim();
            var trimmed = referenceType.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "INTERNALMOVE" or "INVENTORYMOVE" => "Di chuyển nội bộ",
                "QASEEDRECONCILE" or "RECONCILIATION" => "Đối soát sổ cái",
                "IMPORTRECEIPT" or "GOODSRECEIPT" or "RECEIPT" => "Phiếu nhập kho",
                "EXPORTRECEIPT" => "Phiếu xuất kho",
                "STOCKTRANSFER" => "Phiếu điều chuyển",
                "STOCKTRANSFERDAMAGE" => "Hư hỏng điều chuyển",
                "STOCKTAKE" or "STOCKTAKEADJUSTMENT" => "Phiếu kiểm kê",
                "SHIPMENT" => "Chuyến giao hàng",
                "INVENTORYREVERSAL" => "Phiếu đảo giao dịch",
                "INVENTORYADJUSTMENT" => "Phiếu điều chỉnh",
                "PUTAWAYTASK" => "Tác vụ cất hàng",
                "PICKTASK" or "PICKINGTASK" => "Tác vụ lấy hàng",
                "PACKINGTASK" or "PACKINGSESSION" => "Tác vụ đóng gói",
                "INVENTORYSTATUSCHANGE" => "Đổi trạng thái tồn kho",
                "QUALITYDISPOSITIONRELEASE" => "Giải tỏa kiểm định chất lượng",
                "QUALITYDISPOSITIONSCRAP" => "Xuất hủy hàng lỗi",
                "QUALITYDISPOSITIONRTV" => "Hoàn trả nhà cung cấp",
                "RECALLCAMPAIGNBLOCK" => "Phong tỏa thu hồi lô hàng",
                "RETURN" => "Phiếu trả hàng",
                "DEBT_PAYMENT" or "DEBTPAYMENT" => "Thanh toán công nợ",
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
                item.ReferenceType = TranslateReferenceType(item.ReferenceType);
                item.TransactionType = (item.TransactionType ?? string.Empty).Trim();
                item.InventoryStatus = TranslateInventoryStatus(item.InventoryStatus) ?? string.Empty;
                item.FromInventoryStatus = TranslateInventoryStatus(item.FromInventoryStatus);
                item.ToInventoryStatus = TranslateInventoryStatus(item.ToInventoryStatus);
                item.CreatedByName = NormalizeActorDisplayName(item.CreatedByName) ?? string.Empty;
                item.Note = item.Note?.Trim().Replace("(100% Match)", "(Khớp 100%)", StringComparison.OrdinalIgnoreCase).Replace("chờ QC", "chờ kiểm định chất lượng", StringComparison.OrdinalIgnoreCase);
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
