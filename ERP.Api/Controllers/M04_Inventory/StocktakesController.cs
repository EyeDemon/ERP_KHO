using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/stocktakes")]
public class StocktakesController : ControllerBase
{
    private readonly IStocktakeService _stocktakeService;
    private readonly IStocktakeQueryService _stocktakeQueryService;
    private readonly IStocktakeOperationsService _stocktakeOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;
    private readonly ICurrentUser _currentUser;

    public StocktakesController(
        IStocktakeService stocktakeService,
        IStocktakeQueryService stocktakeQueryService,
        IStocktakeOperationsService stocktakeOps,
        IWarehouseAuthorizationService warehouseAccess,
        ICurrentUser currentUser)
    {
        _stocktakeService = stocktakeService;
        _stocktakeQueryService = stocktakeQueryService;
        _stocktakeOps = stocktakeOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    private bool CanReadCost() =>
        this.HasActionPermission(_currentUser, AppPermissions.InventoryCostRead);

    private int? GetActorUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"), out var uid)
            ? uid
            : (_currentUser is { UserId: > 0 } ? _currentUser.UserId : null);

    [HttpGet]
    [PermissionAuthorize(AppPermissions.StocktakeRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetAll() =>
        Ok(await _stocktakeQueryService.GetStocktakesAsync());

    [HttpGet("reason-codes-and-adjustments")]
    [PermissionAuthorize(AppPermissions.StocktakeRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetReasonCodesAndAdjustments(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await _stocktakeOps.GetReasonCodesAndAdjustmentsAsync(warehouseId, CanReadCost(), _warehouseAccess, cancellationToken));

    public sealed record VerifyStocktakeFreezePolicyRequest(int? WarehouseId = null)
        : VerifyStocktakeFreezePolicyInput(WarehouseId);

    [HttpPost("verify-freeze-policy")]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> VerifyFreezePolicy(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyStocktakeFreezePolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.StocktakeManage, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        return this.ToActionResult(await _stocktakeOps.VerifyFreezePolicyAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    public sealed record CreateAdjustmentProposalRequest(
        int? WarehouseId = null, string? ProductCode = null, decimal DeltaQuantity = 0m,
        string ReasonCode = "COUNT_VARIANCE", string? Note = null, int? ProductId = null,
        int? LocationId = null, string? LotNumber = null, bool ExecuteImmediately = false,
        decimal? QuantityDelta = null, string? Sku = null, string? LocationCode = null)
        : CreateAdjustmentProposalInput(
            WarehouseId, ProductCode, DeltaQuantity, ReasonCode, Note,
            ProductId, LocationId, LotNumber, ExecuteImmediately, QuantityDelta, Sku, LocationCode);

    public sealed record ApproveAdjustmentProposalRequest(
        int? LocationId = null, string? LotNumber = null, string? Note = null,
        string? LocationCode = null, decimal? DeltaQuantity = null, decimal? QuantityDelta = null)
        : ApproveAdjustmentProposalInput(LocationId, LotNumber, Note, LocationCode, DeltaQuantity, QuantityDelta);

    [HttpPost("adjustment-proposals")]
    [IdempotentCommand("Stocktake.CreateAdjustmentProposal")]
    [PermissionAuthorize(AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> CreateAdjustmentProposal(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateAdjustmentProposalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)) return Forbid();
        return this.ToActionResult(await _stocktakeOps.CreateAdjustmentProposalAsync(request, GetActorUserId(), CanReadCost(), _warehouseAccess, cancellationToken));
    }

    [HttpPost("adjustment-proposals/{proposalAuditId:long}/approve")]
    [IdempotentCommand("Stocktake.ApproveAdjustmentProposal")]
    [PermissionAuthorize(AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> ApproveAdjustmentProposal(
        long proposalAuditId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ApproveAdjustmentProposalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)) return Forbid();
        return this.ToActionResult(await _stocktakeOps.ApproveAdjustmentProposalAsync(
            proposalAuditId, request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet("{id}")]
    [PermissionAuthorize(AppPermissions.StocktakeRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetById(int id) =>
        Ok(await _stocktakeQueryService.GetStocktakeByIdAsync(id));

    [HttpPost, IdempotentCommand("Stocktake.Create")]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Create([FromBody] CreateStocktakeDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không thể xác định người dùng" });

        var id = await _stocktakeService.CreateStocktakeAsync(dto, userId);
        return Ok(new { message = "Tạo phiếu kiểm kê thành công", id });
    }

    [HttpPost("{id}/approve"), IdempotentCommand("Stocktake.Approve"), Authorize(Policy = ApprovalPolicies.Checker)]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.ApprovalApprove, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> Approve(int id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không thể xác định người dùng" });

        await _stocktakeService.ApproveStocktakeAsync(id, userId);
        return Ok(new { message = "Duyệt phiếu kiểm kê thành công" });
    }

    public sealed record CancelStocktakeRequest(string? Reason = null);

    [HttpPost("{id}/cancel"), IdempotentCommand("Stocktake.Cancel"), Authorize(Policy = ApprovalPolicies.Checker)]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.ApprovalReject, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelStocktakeRequest? request = null)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không thể xác định người dùng" });

        await _stocktakeService.CancelStocktakeAsync(id, userId, request?.Reason);
        return Ok(new { message = "Hủy phiếu kiểm kê thành công" });
    }

    [HttpPut("{id}/details/{detailId}"), IdempotentCommand("Stocktake.UpdateDetail")]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> UpdateDetail(int id, int detailId, [FromBody] UpdateStocktakeDetailDto dto)
    {
        await _stocktakeService.UpdateStocktakeDetailAsync(id, detailId, dto);
        return Ok(new { message = "Cập nhật chi tiết kiểm kê thành công" });
    }

    [HttpPost("{id:int}/details/{detailId:int}/recount"), IdempotentCommand("Stocktake.Recount")]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> Recount(
        int id,
        int detailId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RecountStocktakeDetailDto? dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        int.TryParse(userIdStr, out var userId);

        await _stocktakeService.RecountStocktakeDetailAsync(id, detailId, dto?.Reason, userId);
        return Ok(new { message = "Đã yêu cầu kiểm đếm lại dòng kiểm kê" });
    }

    public sealed record RecountSubmissionDto(
        JsonElement? StocktakeDetailId = null,
        int? DetailId = null,
        int? ProductId = null,
        decimal? CountedQuantity = null,
        string? Note = null) : RecountSubmissionInput(StocktakeDetailId, DetailId, ProductId, CountedQuantity, Note);

    public sealed record EvaluateAutoApprovalRequest(
        decimal? ThresholdPct = 1.0m,
        decimal? ThresholdValueVnd = 500000m,
        List<RecountSubmissionDto>? RecountSubmissions = null);

    [HttpGet("{id}/count-attempts-and-auto-approval"), HttpGet("count-attempts-and-auto-approval")]
    [PermissionAuthorize(AppPermissions.StocktakeRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetCountAttemptsAndAutoApproval(
        [FromRoute] string? id = null,
        [FromQuery] string? stocktakeId = null,
        [FromQuery] decimal? thresholdPct = null,
        [FromQuery] decimal? thresholdValueVnd = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = !string.IsNullOrWhiteSpace(id) ? id : stocktakeId;
        return this.ToActionResult(await _stocktakeOps.GetCountAttemptsAndAutoApprovalAsync(
            resolvedId,
            thresholdPct ?? 1.0m,
            thresholdValueVnd ?? 500000m,
            _warehouseAccess,
            cancellationToken));
    }

    [HttpPost("{id}/evaluate-auto-approval"), HttpPost("evaluate-auto-approval"), IdempotentCommand("Stocktake.EvaluateAutoApproval")]
    [PermissionAuthorize(AppPermissions.StocktakeManage, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> EvaluateAutoApproval(
        [FromRoute] string? id = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAutoApprovalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.StocktakeManage, AppPermissions.InventoryWrite, AppPermissions.InventoryMovementCreate)) return Forbid();

        var counterLabel = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.Identity?.Name ?? "system";
        return this.ToActionResult(await _stocktakeOps.EvaluateAutoApprovalAsync(
            id,
            request?.ThresholdPct ?? 1.0m,
            request?.ThresholdValueVnd ?? 500000m,
            request?.RecountSubmissions,
            GetActorUserId(),
            counterLabel,
            _warehouseAccess,
            cancellationToken));
    }
}
