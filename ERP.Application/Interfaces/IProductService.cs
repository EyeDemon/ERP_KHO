using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IProductService
    {
        Task<IEnumerable<ProductDto>> GetAllProductsAsync(CancellationToken cancellationToken = default);
        Task<PagedResult<ProductDto>> GetPagedProductsAsync(int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken = default);
        Task<ProductDto> GetProductByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<ProductDto> CreateProductAsync(CreateProductDto dto, string username, CancellationToken cancellationToken = default);
        Task UpdateProductAsync(int id, UpdateProductDto dto, string username, CancellationToken cancellationToken = default);
        Task DeleteProductAsync(int id, CancellationToken cancellationToken = default);
    }
}
