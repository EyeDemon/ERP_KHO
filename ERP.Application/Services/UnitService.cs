using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class UnitService : IUnitService
    {
        private readonly IUnitRepository _unitRepository;

        public UnitService(IUnitRepository unitRepository)
        {
            _unitRepository = unitRepository;
        }

        public async Task<IEnumerable<UnitDto>> GetAllUnitsAsync()
        {
            var units = await _unitRepository.GetAllAsync();
            return units.OrderByDescending(u => u.CreatedAt).Select(u => new UnitDto
            {
                Id = u.Id,
                Code = u.Code,
                Name = u.Name,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            });
        }

        public async Task<UnitDto> GetUnitByIdAsync(int id)
        {
            var u = await _unitRepository.GetByIdAsync(id);
            if (u == null) throw new NotFoundException($"Không tìm thấy đơn vị tính id {id}");

            return new UnitDto
            {
                Id = u.Id,
                Code = u.Code,
                Name = u.Name,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            };
        }

        public async Task<UnitDto> CreateUnitAsync(CreateUnitDto dto, string username)
        {
            dto.Code = dto.Code.Trim();
            var exists = await _unitRepository.ExistsByCodeAsync(dto.Code);
            if (exists) throw new BusinessRuleException($"Mã đơn vị tính {dto.Code} đã tồn tại");

            var unit = new Unit
            {
                Code = dto.Code,
                Name = dto.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitRepository.AddAsync(unit);

            return await GetUnitByIdAsync(unit.Id);
        }

        public async Task UpdateUnitAsync(int id, UpdateUnitDto dto, string username)
        {
            var unit = await _unitRepository.GetByIdAsync(id);
            if (unit == null) throw new NotFoundException($"Không tìm thấy đơn vị tính id {id}");

            unit.Name = dto.Name;
            unit.IsActive = dto.IsActive;
            unit.UpdatedAt = DateTime.UtcNow;

            await _unitRepository.UpdateAsync(unit);
        }

        public async Task DeleteUnitAsync(int id)
        {
            var unit = await _unitRepository.GetByIdAsync(id);
            if (unit == null) throw new NotFoundException($"Không tìm thấy đơn vị tính id {id}");

            var hasProducts = await _unitRepository.HasProductsAsync(id);
            if (hasProducts) throw new BusinessRuleException("Không thể xóa đơn vị tính đang được sử dụng cho sản phẩm");

            await _unitRepository.DeleteAsync(unit);
        }
    }
}
