using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed record EscalateOverdueApprovalsRequest(int? WarehouseId);

[ApiController]
[Route("api/approvals")]
[Authorize]
[ApprovalAuthorize]
public sealed class ApprovalsController(
    IApprovalWorkflowService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpPost("escalate-overdue")]
    public async Task<IActionResult> EscalateOverdue(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EscalateOverdueApprovalsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var queue = await service.GetQueueAsync(
            new ApprovalQueueQuery { WarehouseId = request?.WarehouseId, PageIndex = 1, PageSize = 100 },
            cancellationToken);
        var overdueCount = queue.Items.Count(x => x.SlaStatus == "Overdue");
        var recentAuditCount = db != null
            ? await db.AuditLogs.AsNoTracking().CountAsync(x => x.Action.Contains("Approve") || x.Action.Contains("Reject"), cancellationToken)
            : 0;
        var now = DateTime.UtcNow;
        var escalationBatchId = $"ESC-SLA-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "Approval.EscalateOverdue",
                EntityName = "ApprovalQueue",
                NewValues = escalationBatchId,
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            escalationBatchId,
            warehouseId = request?.WarehouseId,
            overdueCount,
            recentAuditCount,
            escalatedStatus = "ESCALATED_TO_SUPERVISOR",
            targetSlaMinutes = 30,
            escalatedAtUtc = now
        });
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

        var queue = await service.GetQueueAsync(
            new ApprovalQueueQuery { WarehouseId = warehouseId, PageIndex = 1, PageSize = 100 },
            cancellationToken);
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
