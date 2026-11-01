using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed record EscalateOverdueApprovalsRequest(int? WarehouseId);

[ApiController]
[Route("api/approvals")]
[Route("api/v1/approvals")]
[Authorize]
[ApprovalAuthorize]
public sealed class ApprovalsController(
    IApprovalWorkflowService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("escalate-overdue")]
    [IdempotentCommand("Approval.EscalateOverdue")]
    [PermissionAuthorize(AppPermissions.ReceiptComplete, AppPermissions.ReceiptPost, AppPermissions.ExportReceiptApprove, AppPermissions.DiscrepancyApprove, AppPermissions.QualityApprove, AppPermissions.ApprovalReject)]
    public async Task<IActionResult> EscalateOverdue(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EscalateOverdueApprovalsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.ReceiptComplete, AppPermissions.ReceiptPost, AppPermissions.ExportReceiptApprove, AppPermissions.DiscrepancyApprove, AppPermissions.QualityApprove, AppPermissions.ApprovalReject))
            return Forbid();

        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var (allItems, _) = await FetchAllQueueItemsAsync(warehouseId, cancellationToken);
        var overdueItems = allItems.Where(x => x.SlaStatus == "Overdue").ToList();

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : currentUser is { IsAuthenticated: true, UserId: > 0 } ? currentUser.UserId : (int?)null;

        var outcome = await govOps.EscalateOverdueApprovalsAsync(
            warehouseId,
            overdueItems,
            actorUserId,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] ApprovalQueueQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetQueueAsync(query, cancellationToken));

    [HttpGet("sla-summary")]
    public async Task<IActionResult> SlaSummary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var (allItems, totalRecords) = await FetchAllQueueItemsAsync(warehouseId, cancellationToken);
        return Ok(new
        {
            totalPending = totalRecords,
            normalCount = allItems.Count(x => x.SlaStatus == "Normal"),
            warningCount = allItems.Count(x => x.SlaStatus == "Warning"),
            overdueCount = allItems.Count(x => x.SlaStatus == "Overdue"),
            maxWaitingMinutes = allItems.Any() ? allItems.Max(x => x.WaitingMinutes ?? 0) : 0,
            status = "Đã tải tổng quan SLA hàng đợi phê duyệt",
            statusLabel = "Đã tải tổng quan SLA hàng đợi phê duyệt"
        });
    }

    private async Task<(List<ApprovalQueueItem> Items, int TotalRecords)> FetchAllQueueItemsAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var pageIndex = 1;
        var firstPage = await service.GetQueueAsync(
            new ApprovalQueueQuery { WarehouseId = warehouseId, PageIndex = pageIndex, PageSize = pageSize },
            cancellationToken);
        var items = firstPage.Items.ToList();
        while (items.Count < firstPage.TotalRecords)
        {
            pageIndex++;
            var nextPage = await service.GetQueueAsync(
                new ApprovalQueueQuery { WarehouseId = warehouseId, PageIndex = pageIndex, PageSize = pageSize },
                cancellationToken);
            if (nextPage.Items.Count == 0)
                break;
            items.AddRange(nextPage.Items);
        }

        return (items, firstPage.TotalRecords);
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
