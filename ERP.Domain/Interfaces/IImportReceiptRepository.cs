using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IImportReceiptRepository : IRepository<ImportReceipt>
    {
        Task<ImportReceipt?> GetByIdWithDetailsAsync(int id);
        Task<IEnumerable<ImportReceipt>> GetAllWithDetailsAsync(ERP.Domain.Enums.ReceiptStatus? status);
        Task<bool> ExistsByCodeAsync(string code, int? excludeId = null);
    }
}

