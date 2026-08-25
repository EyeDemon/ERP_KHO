using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;

namespace ERP.Application.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _productRepository;

        public ProductService(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<IEnumerable<ProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default)
        {
            var products = await _productRepository.GetProductsWithDetailsAsync(cancellationToken);
            return products.Select(p => new ProductDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                UnitId = p.UnitId,
                UnitName = p.Unit?.Name,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt
            });
        }

        public async Task<PagedResult<ProductDto>> GetPagedProductsAsync(int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var (items, totalRecords) = await _productRepository.GetPagedAsync(pageIndex, pageSize, keyword, cancellationToken);

            var dtos = items.Select(p => new ProductDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                UnitId = p.UnitId,
                UnitName = p.Unit?.Name,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt
            }).ToList();

            return new PagedResult<ProductDto>
            {
                Items = dtos,
                TotalRecords = totalRecords,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public async Task<ProductDto> GetProductByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var p = await _productRepository.GetByIdAsync(id, cancellationToken);
            if (p == null) throw new NotFoundException($"Không tìm thấy sản phẩm id {id}");

            return new ProductDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                UnitId = p.UnitId,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt
            };
        }

        public async Task<ProductDto> CreateProductAsync(CreateProductDto dto, string username, CancellationToken cancellationToken = default)
        {
            dto.Code = dto.Code.Trim();
            var exists = await _productRepository.ExistsByCodeAsync(dto.Code, null, cancellationToken);
            if (exists) throw new BusinessRuleException($"Mã sản phẩm {dto.Code} đã tồn tại");

            var product = new Product
            {
                Code = dto.Code,
                Name = dto.Name,
                Description = dto.Description,
                UnitId = dto.UnitId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _productRepository.AddAsync(product, cancellationToken);

            return await GetProductByIdAsync(product.Id, cancellationToken);
        }

        public async Task UpdateProductAsync(int id, UpdateProductDto dto, string username, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id, cancellationToken);
            if (product == null) throw new NotFoundException($"Không tìm thấy sản phẩm id {id}");

            product.Name = dto.Name;
            product.Description = dto.Description;
            product.UnitId = dto.UnitId;
            product.IsActive = dto.IsActive;
            product.UpdatedAt = DateTime.UtcNow;

            await _productRepository.UpdateAsync(product, cancellationToken);
        }

        public async Task DeleteProductAsync(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetByIdAsync(id, cancellationToken);
            if (product == null) throw new NotFoundException($"Không tìm thấy sản phẩm id {id}");

            var hasTx = await _productRepository.HasTransactionsAsync(id, cancellationToken);
            if (hasTx) throw new BusinessRuleException("Không thể xóa sản phẩm đã có giao dịch kho");

            await _productRepository.DeleteAsync(product, cancellationToken);
        }
    }
}
