using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IProductRepository : IRepository<Product>
    {
        Task<bool> HasTransactionsAsync(int productId, CancellationToken cancellationToken = default);
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<Product>> GetProductsWithDetailsAsync(CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<Product> Items, int TotalRecords)> GetPagedAsync(int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken = default);
    }
}
