using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IImportReceiptService
    {
        Task<ImportReceiptDto> CreateAsync(CreateImportReceiptDto dto, int userId);
        Task ApproveImportReceiptAsync(int id, int approvedByUserId);
        Task<IEnumerable<ImportReceiptDto>> GetAllAsync(Domain.Enums.ReceiptStatus? status = null);
        Task<ImportReceiptDto> GetByIdAsync(int id);
        Task CancelAsync(int id, int cancelledByUserId);
    }
}
