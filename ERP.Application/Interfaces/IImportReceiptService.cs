using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IImportReceiptService
    {
        Task<ImportReceiptDto> CreateAsync(CreateImportReceiptDto dto, int userId);
        Task ApproveImportReceiptAsync(int id, int approvedByUserId);
        Task ReceiveAsync(int id, ReceiveImportReceiptDto dto, int receivedByUserId);
        Task RecordQcDispositionAsync(int id, RecordQcDispositionDto dto, int completedByUserId);
        Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> SetInventoryIdentitiesAsync(
            int id,
            SetImportReceiptInventoryIdentitiesDto dto,
            int updatedByUserId,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> GetInventoryIdentitiesAsync(
            int id,
            CancellationToken cancellationToken = default);
        Task PostAsync(int id, int postedByUserId);
        Task<IEnumerable<ImportReceiptDto>> GetAllAsync(Domain.Enums.ReceiptStatus? status = null);
        Task<ImportReceiptDto> GetByIdAsync(int id);
        Task CancelAsync(int id, int cancelledByUserId);
    }
}
