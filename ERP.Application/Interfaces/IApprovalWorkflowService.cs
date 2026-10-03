using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IApprovalWorkflowService
{
    Task<PagedResult<ApprovalQueueItem>> GetQueueAsync(ApprovalQueueQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<ApprovalHistoryItem>> GetHistoryAsync(ApprovalHistoryQuery query, CancellationToken cancellationToken = default);
    Task<ApprovalDetail> GetDetailAsync(string documentType, int id, CancellationToken cancellationToken = default);
    Task<ApprovalActionResult> RejectAsync(string documentType, int id, string reason, CancellationToken cancellationToken = default);
}
