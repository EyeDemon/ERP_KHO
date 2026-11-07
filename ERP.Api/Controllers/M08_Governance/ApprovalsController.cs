using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

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
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ReceiptComplete,
        AppPermissions.ReceiptPost,
        AppPermissions.ExportReceiptApprove,
        AppPermissions.StocktakeManage,
        AppPermissions.DiscrepancyApprove,
        AppPermissions.QualityApprove,
        AppPermissions.ApprovalReject)]
    public async Task<IActionResult> EscalateOverdue(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EscalateOverdueApprovalsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(
                currentUser,
                AppPermissions.ApprovalApprove,
                AppPermissions.ReceiptComplete,
                AppPermissions.ReceiptPost,
                AppPermissions.ExportReceiptApprove,
                AppPermissions.StocktakeManage,
                AppPermissions.DiscrepancyApprove,
                AppPermissions.QualityApprove,
                AppPermissions.ApprovalReject))
            return Forbid();

        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var (allItems, _) = await FetchAllQueueItemsAsync(warehouseId, cancellationToken);
        var overdueItems = allItems.Where(x => x.SlaStatus == "Overdue").ToList();

        var actorUserId = ResolveActorUserId();

        var outcome = await govOps.EscalateOverdueApprovalsAsync(
            warehouseId,
            overdueItems,
            actorUserId,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("queue")]
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptRead,
        AppPermissions.ExportReceiptRead,
        AppPermissions.StocktakeRead,
        AppPermissions.InventoryRead,
        AppPermissions.DiscrepancyRead,
        AppPermissions.QualityPolicyRead)]
    public async Task<IActionResult> Queue([FromQuery] ApprovalQueueQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetQueueAsync(query, cancellationToken));

    [HttpGet("sla-summary")]
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptRead,
        AppPermissions.ExportReceiptRead,
        AppPermissions.StocktakeRead,
        AppPermissions.InventoryRead,
        AppPermissions.DiscrepancyRead,
        AppPermissions.QualityPolicyRead)]
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
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptRead,
        AppPermissions.ExportReceiptRead,
        AppPermissions.StocktakeRead,
        AppPermissions.InventoryRead,
        AppPermissions.DiscrepancyRead,
        AppPermissions.QualityPolicyRead)]
    public async Task<IActionResult> History([FromQuery] ApprovalHistoryQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetHistoryAsync(query, cancellationToken));

    [HttpGet("{documentType}/{id:int}/history")]
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptRead,
        AppPermissions.ExportReceiptRead,
        AppPermissions.StocktakeRead,
        AppPermissions.InventoryRead,
        AppPermissions.DiscrepancyRead,
        AppPermissions.QualityPolicyRead)]
    public async Task<IActionResult> DocumentHistory(string documentType, int id, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await service.GetHistoryAsync(new ApprovalHistoryQuery { DocumentType = documentType, DocumentId = id, PageIndex = pageIndex, PageSize = pageSize }, cancellationToken));

    [HttpGet("{documentType}/{id:int}")]
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptRead,
        AppPermissions.ExportReceiptRead,
        AppPermissions.StocktakeRead,
        AppPermissions.InventoryRead,
        AppPermissions.DiscrepancyRead,
        AppPermissions.QualityPolicyRead)]
    public async Task<IActionResult> Detail(string documentType, int id, CancellationToken cancellationToken) =>
        Ok(await service.GetDetailAsync(documentType, id, cancellationToken));

    [HttpPost("{documentType}/{id:int}/reject")]
    [PermissionAuthorize(
        AppPermissions.ApprovalReject,
        AppPermissions.ReceiptComplete,
        AppPermissions.ExportReceiptApprove,
        AppPermissions.StocktakeManage,
        AppPermissions.DiscrepancyReject,
        AppPermissions.QualityApprove)]
    [IdempotentCommand("Approval.Reject")]
    public async Task<IActionResult> Reject(string documentType, int id, ApprovalRejectRequest request, CancellationToken cancellationToken) =>
        Ok(await service.RejectAsync(documentType, id, request.Reason, cancellationToken));

    [HttpPost("{documentType}/{id:int}/approve")]
    [PermissionAuthorize(
        AppPermissions.ApprovalApprove,
        AppPermissions.ReceiptComplete,
        AppPermissions.ReceiptPost,
        AppPermissions.ExportReceiptApprove,
        AppPermissions.StocktakeManage,
        AppPermissions.InventoryMovementCreate,
        AppPermissions.DiscrepancyApprove,
        AppPermissions.QualityApprove)]
    [IdempotentCommand("Approval.Approve")]
    public async Task<IActionResult> Approve(string documentType, int id, CancellationToken cancellationToken = default)
    {
        var detail = await service.GetDetailAsync(documentType, id, cancellationToken);
        if (!detail.Summary.CanApprove)
        {
            return UnprocessableEntity(new
            {
                code = "APPROVAL_BLOCKED_BY_POLICY",
                errorCode = "APPROVAL_BLOCKED_BY_POLICY",
                reason = detail.Summary.DeniedReasonCode,
                message = detail.Summary.DeniedReasonCode ?? "Chứng từ không đủ điều kiện phê duyệt theo quy tắc kiểm soát nội bộ."
            });
        }

        var actorUserId = ResolveActorUserId() ?? 0;
        var sp = HttpContext?.RequestServices;
        if (sp is not null)
        {
            switch (detail.Summary.DocumentType)
            {
                case "ImportReceipt":
                    var importSvc = sp.GetService<IImportReceiptService>();
                    if (importSvc is not null && actorUserId > 0)
                        await importSvc.ApproveImportReceiptAsync(id, actorUserId);
                    break;
                case "ExportReceipt":
                    var exportSvc = sp.GetService<IExportReceiptService>();
                    if (exportSvc is not null && actorUserId > 0)
                        await exportSvc.ApproveAsync(id, actorUserId);
                    break;
                case "StockTransfer":
                    var transferSvc = sp.GetService<IStockTransferService>();
                    if (transferSvc is not null)
                        await transferSvc.ApproveAsync(id, cancellationToken);
                    break;
                case "Stocktake":
                    var stocktakeSvc = sp.GetService<IStocktakeService>();
                    if (stocktakeSvc is not null && actorUserId > 0)
                        await stocktakeSvc.ApproveStocktakeAsync(id, actorUserId);
                    break;
            }
        }

        var refreshed = sp is not null ? await service.GetDetailAsync(documentType, id, cancellationToken) : detail;
        return Ok(new
        {
            documentType = refreshed.Summary.DocumentType,
            documentId = refreshed.Summary.DocumentId,
            code = refreshed.Summary.DocumentCode,
            status = refreshed.Summary.PendingState,
            approved = true,
            statusLabel = $"Đã thực thi phê duyệt chứng từ {refreshed.Summary.DocumentCode} ({refreshed.Summary.DocumentType})"
        });
    }

    private int? ResolveActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (currentUser is { IsAuthenticated: true, UserId: > 0 } ? currentUser.UserId : null);
}
