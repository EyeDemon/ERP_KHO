using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IUnitRepository : IRepository<Unit>
    {
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null);
        Task<bool> HasProductsAsync(int unitId);
    }
}
