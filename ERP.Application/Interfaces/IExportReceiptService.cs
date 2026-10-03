using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IExportReceiptService
    {
        Task<IEnumerable<ExportReceiptDto>> GetAllAsync();
        Task<ExportReceiptDto> GetByIdAsync(int id);
        Task<ExportReceiptDto> CreateAsync(CreateExportReceiptDto dto, int userId);
        Task ApproveAsync(int id, int approvedByUserId);
        Task ApproveAndReserveAsync(int id, int approvedByUserId);
        Task ApproveAndDispatchAsync(int id, int approvedByUserId);
        Task DispatchAsync(int id, int userId);
        Task CancelAsync(int id, int userId);
    }
}
