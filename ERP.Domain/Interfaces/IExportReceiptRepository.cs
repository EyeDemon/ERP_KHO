using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IExportReceiptRepository : IRepository<ExportReceipt>
    {
        Task<ExportReceipt?> GetForMutationAsync(int id) => GetByIdWithDetailsAsync(id);
        Task<IEnumerable<ExportReceipt>> GetListAsync();
        Task<ExportReceipt?> GetByIdWithDetailsAsync(int id);
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null);
    }
}
