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
        private readonly IProductCatalogService _catalogService;

        public ProductService(IProductRepository productRepository, IProductCatalogService catalogService)
        {
            _productRepository = productRepository;
            _catalogService = catalogService;
        }

        public async Task<IEnumerable<ProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default)
        {
            var products = await _productRepository.GetProductsWithDetailsAsync(cancellationToken);
            return products.Select(Map);
        }

        public async Task<PagedResult<ProductDto>> GetPagedProductsAsync(int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var (items, totalRecords) = await _productRepository.GetPagedAsync(pageIndex, pageSize, keyword, cancellationToken);

            var dtos = items.Select(Map).ToList();

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

            return Map(p);
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
                CategoryId = dto.CategoryId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _catalogService.AddProductAsync(product, cancellationToken);

            return await GetProductByIdAsync(product.Id, cancellationToken);
        }

        public static ProductDto Map(Product p) => new()
        {
            Id = p.Id, Code = p.Code, Name = p.Name, Description = p.Description,
            UnitId = p.UnitId, UnitName = p.Unit?.Name, UnitCode = p.Unit?.Code, UnitDecimalPlaces = p.Unit?.DecimalPlaces ?? 4,
            Uoms = new[] { new ProductUomDto { UnitId = p.UnitId, UnitCode = p.Unit?.Code ?? string.Empty, UnitName = p.Unit?.Name ?? string.Empty, DecimalPlaces = p.Unit?.DecimalPlaces ?? 4, ConversionFactor = 1, Version = 1 } }
                .Concat(p.Uoms.Where(x => x.IsActive && x.EffectiveFromUtc <= DateTime.UtcNow && x.UnitId != p.UnitId)
                    .GroupBy(x => x.UnitId).Select(group => group.OrderByDescending(x => x.Version).First())
                    .Select(x => new ProductUomDto { UnitId = x.UnitId, UnitCode = x.Unit.Code, UnitName = x.Unit.Name, DecimalPlaces = x.Unit.DecimalPlaces, ConversionFactor = x.ConversionFactor, Version = x.Version })).ToList(),
            CategoryId = p.CategoryId,
            CategoryCode = p.Category?.Code, CategoryName = p.Category?.Name,
            Barcodes = p.Barcodes.Select(x => new ProductBarcodeDto { Id = x.Id, ProductId = x.ProductId, Value = x.Value }).ToList(),
            IsActive = p.IsActive, CreatedAt = p.CreatedAt
        };

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
