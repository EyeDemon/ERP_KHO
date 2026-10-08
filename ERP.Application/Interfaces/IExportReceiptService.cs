using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IExportReceiptService
    {
        Task<IEnumerable<ExportReceiptDto>> GetAllAsync();
        Task<ExportReceiptDto> GetByIdAsync(int id);
        Task<ExportReceiptDto> CreateAsync(CreateExportReceiptDto dto, int userId);
        Task ApproveAsync(int id, int approvedByUserId, string? rowVersion = null);
        Task ApproveAndReserveAsync(int id, int approvedByUserId, string? rowVersion = null);
        Task ApproveAndDispatchAsync(int id, int approvedByUserId, string? rowVersion = null);
        Task DispatchAsync(int id, int userId, string? rowVersion = null);
        Task CancelAsync(int id, int userId, string? rowVersion = null);
    }
}
