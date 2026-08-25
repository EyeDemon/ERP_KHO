using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IExportReceiptRepository : IRepository<ExportReceipt>
    {
        Task<IEnumerable<ExportReceipt>> GetListAsync();
        Task<ExportReceipt?> GetByIdWithDetailsAsync(int id);
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null);
    }
}
