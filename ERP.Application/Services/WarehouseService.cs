using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Domain.Enums;

namespace ERP.Application.Services
{
    public class WarehouseService : IWarehouseService
    {
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;
        private readonly IInventoryStockRepository? _stockRepository;
        private readonly ICurrentUser? _currentUser;

        internal WarehouseService(
            IWarehouseRepository warehouseRepository,
            IInventoryStockRepository? stockRepository = null,
            ICurrentUser? currentUser = null)
        {
            _warehouseRepository = warehouseRepository;
            _stockRepository = stockRepository;
            _currentUser = currentUser;
        }

        public WarehouseService(
            IWarehouseRepository warehouseRepository,
            IWarehouseAuthorizationService warehouseAuthorization,
            IInventoryStockRepository? stockRepository = null,
            ICurrentUser? currentUser = null)
        {
            _warehouseRepository = warehouseRepository;
            _warehouseAuthorization = warehouseAuthorization;
            _stockRepository = stockRepository;
            _currentUser = currentUser;
        }

        public async Task<IEnumerable<WarehouseDto>> GetAllWarehousesAsync()
        {
            IEnumerable<Warehouse> warehouses;
            if (_warehouseAuthorization is null)
            {
                warehouses = await _warehouseRepository.GetAllAsync();
            }
            else
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                warehouses = await _warehouseRepository.FindAsync(w => allowedWarehouseIds.Contains(w.Id));
            }
            return warehouses.OrderByDescending(w => w.CreatedAt).Select(w => new WarehouseDto
            {
                Id = w.Id,
                Code = (w.Code ?? string.Empty).Trim(),
                Name = (w.Name ?? string.Empty).Trim(),
                Address = w.Address?.Trim(),
                IsActive = w.IsActive,
                CreatedAt = w.CreatedAt
            });
        }

        public async Task<WarehouseDto> GetWarehouseByIdAsync(int id)
        {
            if (_warehouseAuthorization is not null)
                await _warehouseAuthorization.EnsureWarehouseAccessAsync(id);
            var w = await _warehouseRepository.GetByIdAsync(id);
            if (w == null) throw new NotFoundException($"Không tìm thấy kho id {id}");

            return new WarehouseDto
            {
                Id = w.Id,
                Code = (w.Code ?? string.Empty).Trim(),
                Name = (w.Name ?? string.Empty).Trim(),
                Address = w.Address?.Trim(),
                IsActive = w.IsActive,
                CreatedAt = w.CreatedAt
            };
        }

        public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto, string username)
        {
            dto.Code = dto.Code.Trim();
            var exists = await _warehouseRepository.ExistsByCodeAsync(dto.Code);
            if (exists) throw new BusinessRuleException($"Mã kho {dto.Code} đã tồn tại");

            var warehouse = new Warehouse
            {
                Code = dto.Code,
                Name = (dto.Name ?? string.Empty).Trim(),
                Address = dto.Address?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            warehouse.Locations.Add(new WarehouseLocation { Code = "RECEIVING", Name = "Vị trí nhận hàng", LocationType = WarehouseLocationType.Receiving, IsActive = true, IsPickable = false, IsReceivable = true, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "STAGING", Name = "Vị trí tập kết trung chuyển", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "QC_HOLD", Name = "Vị trí chờ kiểm định chất lượng", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "QUARANTINE", Name = "Vị trí biệt trữ cách ly", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "RETURNS_STAGE", Name = "Vị trí tập kết hàng trả về", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "PACKING_STAGE", Name = "Vị trí tập kết đóng gói", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "SHIPPING_STAGE", Name = "Vị trí tập kết xuất hàng", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code = "SCRAP", Name = "Vị trí chờ thanh lý hủy bỏ", LocationType = WarehouseLocationType.Staging, IsActive = true, IsPickable = false, IsReceivable = false, IsSystemManaged = true });
            warehouse.Locations.Add(new WarehouseLocation { Code="LEGACY", Name="Vị trí tương thích nghiệp vụ kho", LocationType=WarehouseLocationType.Legacy, IsActive=true, IsPickable=true, IsSystemManaged=true });
            warehouse.Locations.Add(new WarehouseLocation { Code="DAMAGED", Name="Vị trí cách ly hàng hỏng", LocationType=WarehouseLocationType.Damaged, IsActive=true, IsPickable=false, IsReceivable=false, IsSystemManaged=true });

            var creatorUserId = _currentUser is { IsAuthenticated: true, UserId: > 0 }
                ? _currentUser.UserId
                : _warehouseAuthorization?.CurrentUserId;

            if (creatorUserId is > 0)
            {
                warehouse.UserAccesses.Add(new UserWarehouse
                {
                    UserId = creatorUserId.Value,
                    CreatedBy = creatorUserId.Value,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _warehouseRepository.AddAsync(warehouse);
            _warehouseAuthorization?.InvalidateWarehouseAccessCache(creatorUserId);

            return new WarehouseDto
            {
                Id = warehouse.Id,
                Code = warehouse.Code.Trim(),
                Name = warehouse.Name.Trim(),
                Address = warehouse.Address?.Trim(),
                IsActive = warehouse.IsActive,
                CreatedAt = warehouse.CreatedAt
            };
        }

        public async Task UpdateWarehouseAsync(int id, UpdateWarehouseDto dto, string username)
        {
            if (_warehouseAuthorization is not null)
                await _warehouseAuthorization.EnsureWarehouseAccessAsync(id);
            var warehouse = await _warehouseRepository.GetByIdAsync(id);
            if (warehouse == null) throw new NotFoundException($"Không tìm thấy kho id {id}");

            if (warehouse.IsActive && !dto.IsActive)
            {
                var hasActiveStock = _stockRepository is not null
                    ? (await _stockRepository.FindAsync(s => s.WarehouseId == id && (s.Quantity > 0 || s.ReservedQuantity > 0)))
                        ?.Any(s => s.Quantity > 0 || s.ReservedQuantity > 0) == true
                    : await _warehouseRepository.HasTransactionsAsync(id);
                if (hasActiveStock)
                    throw new BusinessRuleException("Không thể ngừng hoạt động kho khi vẫn còn tồn kho hoặc hàng đang giữ chỗ.");
            }

            warehouse.Name = (dto.Name ?? string.Empty).Trim();
            warehouse.Address = dto.Address?.Trim();
            warehouse.IsActive = dto.IsActive;
            warehouse.UpdatedAt = DateTime.UtcNow;

            await _warehouseRepository.UpdateAsync(warehouse);
        }

        public async Task DeleteWarehouseAsync(int id)
        {
            if (_warehouseAuthorization is not null)
                await _warehouseAuthorization.EnsureWarehouseAccessAsync(id);
            var warehouse = await _warehouseRepository.GetByIdAsync(id);
            if (warehouse == null) throw new NotFoundException($"Không tìm thấy kho id {id}");

            var hasTx = await _warehouseRepository.HasTransactionsAsync(id);
            if (hasTx) throw new BusinessRuleException("Không thể xóa kho đã có giao dịch hoặc dữ liệu nghiệp vụ liên quan");

            await _warehouseRepository.DeleteAsync(warehouse);
        }
    }
}
