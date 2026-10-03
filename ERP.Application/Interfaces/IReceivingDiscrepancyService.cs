using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IReceivingDiscrepancyService
{
    Task<IReadOnlyList<ReceivingDiscrepancyDto>> GetAsync(int receiptId, CancellationToken token);
    Task<IReadOnlyList<ReceivingReasonCodeDto>> GetActiveReasonsAsync(CancellationToken token);
    Task<IReadOnlyList<ReceivingDiscrepancyDto>> ObserveAsync(int receiptId, ObserveReceivingDto dto, CancellationToken token);
    Task<ReceivingDiscrepancyDto> SubmitAsync(int receiptId, int discrepancyId, SubmitReceivingDiscrepancyDto dto, CancellationToken token);
    Task<ReceivingDiscrepancyDto> ApproveAsync(int receiptId, int discrepancyId, byte[] rowVersion, CancellationToken token);
    Task<ReceivingDiscrepancyDto> RejectAsync(int receiptId, int discrepancyId, byte[] rowVersion, CancellationToken token);
    Task<ReceivingDiscrepancyDto> RecountAsync(int receiptId, int discrepancyId, RecountReceivingDto dto, CancellationToken token);
}
