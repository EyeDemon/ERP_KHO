using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[Authorize, ApiController, Route("api/[controller]"), Route("api/export-receipts"), Route("api/v1/export-receipts")]
public partial class ExportReceiptsController : ControllerBase
{
    private readonly IExportReceiptService _exportReceiptService;
    private readonly IExportReceiptOperationsService _exportReceiptOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;
    private readonly ICurrentUser _currentUser;
    private readonly IHandoverManifestService _handoverManifestService;

    public ExportReceiptsController(
        IExportReceiptService exportReceiptService,
        IExportReceiptOperationsService exportReceiptOps,
        IWarehouseAuthorizationService warehouseAccess,
        ICurrentUser currentUser,
        IHandoverManifestService handoverManifestService)
    {
        ArgumentNullException.ThrowIfNull(exportReceiptService);
        ArgumentNullException.ThrowIfNull(exportReceiptOps);
        ArgumentNullException.ThrowIfNull(warehouseAccess);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(handoverManifestService);
        _exportReceiptService = exportReceiptService;
        _exportReceiptOps = exportReceiptOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
        _handoverManifestService = handoverManifestService;
    }

    private IExportReceiptOperationsService RequireOps() => _exportReceiptOps;

    private IActionResult ToActionResult(HandoverManifestOperationResult result) =>
        result.StatusCode switch
        {
            200 => Ok(result.Payload),
            400 => BadRequest(result.Payload),
            404 => NotFound(result.Payload),
            409 => Conflict(result.Payload),
            422 => UnprocessableEntity(result.Payload),
            _ => StatusCode(result.StatusCode, result.Payload)
        };

    private bool HasPermissionOrDefault(string permission) =>
        this.HasActionPermission(_currentUser, permission);

    private bool CanReadInventoryCost() => HasPermissionOrDefault(AppPermissions.InventoryCostRead);
    private bool CanReprintAwb() => HasPermissionOrDefault(ERP.Domain.Security.AppPermissions.ExportReceiptReprintAwb);

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        return int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"), out userId);
    }

    [HttpGet, PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null)
    {
        var items = await _exportReceiptService.GetAllAsync();
        if (pageNumber.HasValue || pageSize.HasValue)
        {
            var size = Math.Clamp(pageSize ?? 50, 1, 100);
            var page = Math.Max(1, pageNumber ?? 1);
            if (Response != null)
            {
                Response.Headers["X-Total-Count"] = items.Count().ToString();
            }
            return Ok(items.Skip((page - 1) * size).Take(size));
        }

        return Ok(items);
    }

    [HttpGet("pipeline-summary"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetPipelineSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await _exportReceiptOps.GetPipelineSummaryAsync(warehouseId, _warehouseAccess, cancellationToken)).ToActionResult(this);

    [HttpGet("sla-queue"), HttpGet("marketplace-sla-queue"), HttpGet("~/api/v1/outbound/sla-queue"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetSlaQueue([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await _exportReceiptOps.GetSlaQueueAsync(warehouseId, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:int}"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetById(int id) => Ok(await _exportReceiptService.GetByIdAsync(id));

    [HttpPost, IdempotentCommand("ExportReceipt.Create"), PermissionAuthorize(AppPermissions.ExportReceiptCreate)]
    public async Task<IActionResult> Create([FromBody] CreateExportReceiptDto dto, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        if (_exportReceiptOps != null)
        {
            var quotaOutcome = await _exportReceiptOps.CheckSubscriptionQuotaForCreateAsync(cancellationToken);
            if (quotaOutcome != null) return quotaOutcome.ToActionResult(this);
        }
        try
        {
            var receipt = await _exportReceiptService.CreateAsync(dto, userId);
            if (_exportReceiptOps != null)
            {
                await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(
                    cancellationToken,
                    receipt.SalesOrderId ?? dto.SalesOrderId,
                    receipt.MarketplaceOrderId ?? dto.MarketplaceOrderId,
                    receipt.Id);
            }
            return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, receipt);
        }
        catch (BusinessRuleException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id}/cancel"), IdempotentCommand("ExportReceipt.Cancel"), PermissionAuthorize(AppPermissions.ExportReceiptCancel)]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _exportReceiptService.CancelAsync(id, userId);
        return Ok(new { message = "Hủy phiếu xuất thành công" });
    }

    [HttpPost("{id}/approve-and-reserve"), IdempotentCommand("ExportReceipt.ApproveAndReserve"), PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
    public async Task<IActionResult> ApproveAndReserve(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAndReserveAsync(id, userId);
        if (_exportReceiptOps != null)
            await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(HttpContext?.RequestAborted ?? default, exportReceiptId: id);
        return Ok(new { message = "Đã duyệt và giữ hàng." });
    }

    [HttpPost("{id}/approve-and-dispatch"), IdempotentCommand("ExportReceipt.ApproveAndDispatch"), PermissionAuthorize(AppPermissions.ExportReceiptApprove), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> ApproveAndDispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAndDispatchAsync(id, userId);
        if (_exportReceiptOps != null)
            await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(HttpContext?.RequestAborted ?? default, exportReceiptId: id);
        return Ok(new { message = "Đã duyệt và xuất kho." });
    }

    [HttpPost("{id}/dispatch"), IdempotentCommand("ExportReceipt.Dispatch"), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> Dispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.DispatchAsync(id, userId);
        if (_exportReceiptOps != null)
            await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(HttpContext?.RequestAborted ?? default, exportReceiptId: id);
        return Ok(new { message = "Đã xác nhận xuất kho." });
    }

    [HttpPost("{id}/approve"), IdempotentCommand("ExportReceipt.Approve"), PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
    public async Task<IActionResult> Approve(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAsync(id, userId);
        if (_exportReceiptOps != null)
            await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(HttpContext?.RequestAborted ?? default, exportReceiptId: id);
        return Ok(new { message = "Duyệt phiếu xuất thành công" });
    }

    [HttpPut("{id:int}/customer"), IdempotentCommand("ExportReceipt.SetCustomer"), PermissionAuthorize(AppPermissions.ExportReceiptUpdate), PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> SetCustomer(int id, [FromBody] SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
    {
        await partners.SetExportCustomerAsync(id, dto.PartnerId, ct);
        return NoContent();
    }

    public sealed record VerifyDispatchCutoffRequest(int? WarehouseId = null) : VerifyDispatchCutoffInput(WarehouseId);

    [HttpPost("verify-dispatch-cutoff"), PermissionAuthorize(AppPermissions.ExportReceiptDispatch, AppPermissions.ExportReceiptUpdate)]
    public async Task<IActionResult> VerifyDispatchCutoff(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VerifyDispatchCutoffRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ExportReceiptDispatch, AppPermissions.ExportReceiptUpdate)) return Forbid();
        return (await _exportReceiptOps.VerifyDispatchCutoffAsync(request, TryGetUserId(out var uid) ? uid : null, _warehouseAccess, cancellationToken)).ToActionResult(this);
    }
}
