using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class WarehouseService : IWarehouseService
    {
        private readonly IWarehouseRepository _warehouseRepository;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal WarehouseService(IWarehouseRepository warehouseRepository)
        {
            _warehouseRepository = warehouseRepository;
        }

        public WarehouseService(IWarehouseRepository warehouseRepository, IWarehouseAuthorizationService warehouseAuthorization)
        {
            _warehouseRepository = warehouseRepository;
            _warehouseAuthorization = warehouseAuthorization;
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
                Code = w.Code,
                Name = w.Name,
                Address = w.Address,
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
                Code = w.Code,
                Name = w.Name,
                Address = w.Address,
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
                Name = dto.Name,
                Address = dto.Address,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _warehouseRepository.AddAsync(warehouse);

            return await GetWarehouseByIdAsync(warehouse.Id);
        }

        public async Task UpdateWarehouseAsync(int id, UpdateWarehouseDto dto, string username)
        {
            if (_warehouseAuthorization is not null)
                await _warehouseAuthorization.EnsureWarehouseAccessAsync(id);
            var warehouse = await _warehouseRepository.GetByIdAsync(id);
            if (warehouse == null) throw new NotFoundException($"Không tìm thấy kho id {id}");

            warehouse.Name = dto.Name;
            warehouse.Address = dto.Address;
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
            if (hasTx) throw new BusinessRuleException("Không thể xóa kho đã có giao dịch");

            await _warehouseRepository.DeleteAsync(warehouse);
        }
    }
}
