using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/approvals")]
[Authorize]
[ApprovalAuthorize]
public sealed class ApprovalsController(IApprovalWorkflowService service) : ControllerBase
{
    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] ApprovalQueueQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetQueueAsync(query, cancellationToken));

    [HttpGet("sla-summary")]
    public async Task<IActionResult> SlaSummary(CancellationToken cancellationToken)
    {
        var queue = await service.GetQueueAsync(new ApprovalQueueQuery { PageIndex = 1, PageSize = 100 }, cancellationToken);
        return Ok(new
        {
            totalPending = queue.TotalRecords,
            normalCount = queue.Items.Count(x => x.SlaStatus == "Normal"),
            warningCount = queue.Items.Count(x => x.SlaStatus == "Warning"),
            overdueCount = queue.Items.Count(x => x.SlaStatus == "Overdue"),
            maxWaitingMinutes = queue.Items.Any() ? queue.Items.Max(x => x.WaitingMinutes ?? 0) : 0,
        });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] ApprovalHistoryQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetHistoryAsync(query, cancellationToken));

    [HttpGet("{documentType}/{id:int}/history")]
    public async Task<IActionResult> DocumentHistory(string documentType, int id, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await service.GetHistoryAsync(new ApprovalHistoryQuery { DocumentType = documentType, DocumentId = id, PageIndex = pageIndex, PageSize = pageSize }, cancellationToken));

    [HttpGet("{documentType}/{id:int}")]
    public async Task<IActionResult> Detail(string documentType, int id, CancellationToken cancellationToken) =>
        Ok(await service.GetDetailAsync(documentType, id, cancellationToken));

    [HttpPost("{documentType}/{id:int}/reject")]
    [IdempotentCommand("Approval.Reject")]
    public async Task<IActionResult> Reject(string documentType, int id, ApprovalRejectRequest request, CancellationToken cancellationToken) =>
        Ok(await service.RejectAsync(documentType, id, request.Reason, cancellationToken));
}
