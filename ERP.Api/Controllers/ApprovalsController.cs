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
    IWarehouseAuthorizationService warehouseAccess = null!,
    ICurrentUser? currentUser = null) : ControllerBase
{
    [HttpPost("escalate-overdue")]
    [IdempotentCommand("Approval.EscalateOverdue")]
    public async Task<IActionResult> EscalateOverdue(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EscalateOverdueApprovalsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var (allItems, _) = await FetchAllQueueItemsAsync(warehouseId, cancellationToken);
        var overdueItems = allItems.Where(x => x.SlaStatus == "Overdue").ToList();
        var overdueCount = overdueItems.Count;
        var recentAuditCount = db != null
            ? await db.AuditLogs.AsNoTracking().CountAsync(x => x.Action.Contains("Approve") || x.Action.Contains("Reject"), cancellationToken)
            : 0;
        var now = DateTime.UtcNow;
        var escalationBatchId = $"ESC-SLA-{now:yyyyMMdd-HHmm}";
        var newlyEscalatedCount = overdueCount;
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : currentUser is { IsAuthenticated: true, UserId: > 0 } ? currentUser.UserId : (int?)null;

            var existingEscalated = await db.AuditLogs.AsNoTracking()
                .Where(x => x.Action == "Approval.SlaEscalated" && x.EntityId.HasValue)
                .Select(x => new { x.EntityName, EntityId = x.EntityId!.Value })
                .ToListAsync(cancellationToken);
            var escalatedSet = existingEscalated
                .Select(x => (x.EntityName, x.EntityId))
                .ToHashSet();

            var unescalatedItems = overdueItems
                .Where(x => !escalatedSet.Contains((x.DocumentType, x.DocumentId)))
                .ToList();
            newlyEscalatedCount = unescalatedItems.Count;

            foreach (var item in unescalatedItems)
            {
                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = actorUserId,
                    WarehouseId = item.WarehouseId,
                    Action = "Approval.SlaEscalated",
                    EntityName = item.DocumentType,
                    EntityId = item.DocumentId,
                    NewValues = escalationBatchId,
                    Result = "Success",
                    Timestamp = now
                });
            }

            if (unescalatedItems.Count > 0 || !await db.AuditLogs.AsNoTracking().AnyAsync(
                x => x.Action == "Approval.EscalateOverdue" && x.WarehouseId == warehouseId && x.NewValues == escalationBatchId,
                cancellationToken))
            {
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
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var statusText = overdueCount > 0
            ? "Đã kích hoạt chuyển cấp phê duyệt các hồ sơ quá hạn SLA"
            : "Không có hồ sơ phê duyệt quá hạn SLA cần chuyển cấp";

        return Ok(new
        {
            escalationBatchId,
            warehouseId = request?.WarehouseId,
            overdueCount,
            newlyEscalatedCount,
            recentAuditCount,
            escalatedStatus = "Đã chuyển cấp quản lý phê duyệt khẩn",
            status = statusText,
            statusLabel = statusText,
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

        var (allItems, totalRecords) = await FetchAllQueueItemsAsync(warehouseId, cancellationToken);
        return Ok(new
        {
            totalPending = totalRecords,
            normalCount = allItems.Count(x => x.SlaStatus == "Normal"),
            warningCount = allItems.Count(x => x.SlaStatus == "Warning"),
            overdueCount = allItems.Count(x => x.SlaStatus == "Overdue"),
            maxWaitingMinutes = allItems.Any() ? allItems.Max(x => x.WaitingMinutes ?? 0) : 0,
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
