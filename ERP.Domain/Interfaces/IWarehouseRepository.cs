using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IWarehouseRepository : IRepository<Warehouse>
    {
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null);
        Task<bool> HasTransactionsAsync(int warehouseId);
    }
}
