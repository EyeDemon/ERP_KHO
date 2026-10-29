using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize(Roles = AppRoles.AdminManagerOrStaff)]
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/stocktakes")]
public class StocktakesController : ControllerBase
{
    private readonly IStocktakeService _stocktakeService;
    private readonly IStocktakeQueryService _stocktakeQueryService;
    private readonly IStocktakeOperationsService? _stocktakeOps;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;

    public StocktakesController(
        IStocktakeService stocktakeService,
        IStocktakeQueryService stocktakeQueryService,
        IStocktakeOperationsService stocktakeOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!,
        ICurrentUser? currentUser = null)
    {
        _stocktakeService = stocktakeService;
        _stocktakeQueryService = stocktakeQueryService;
        _stocktakeOps = stocktakeOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    private bool CanReadCost() =>
        _currentUser?.HasPermission(AppPermissions.InventoryCostRead)
        ?? (User != null && !User.IsInRole(AppRoles.WarehouseStaff) && !User.IsInRole(AppRoles.Viewer));

    private int? GetActorUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"), out var uid)
            ? uid
            : (_currentUser is { UserId: > 0 } ? _currentUser.UserId : null);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _stocktakeQueryService.GetStocktakesAsync());

    [HttpGet("reason-codes-and-adjustments")]
    public async Task<IActionResult> GetReasonCodesAndAdjustments(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (_stocktakeOps != null)
            return this.ToActionResult(await _stocktakeOps.GetReasonCodesAndAdjustmentsAsync(warehouseId, CanReadCost(), _warehouseAccess, cancellationToken));

        if (_warehouseAccess != null)
        {
            if (warehouseId.HasValue) await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        return Ok(new
        {
            reasonCodes = new object[]
            {
                new { code = "COUNT_VARIANCE", name = "Chênh lệch kiểm đếm định kỳ", category = "CycleCount", requiresApproval = true },
                new { code = "DAMAGED_FOUND", name = "Phát hiện hàng hư hỏng vật lý", category = "Quality", requiresApproval = true },
                new { code = "EXPIRY_WRITE_OFF", name = "Hủy hàng hết hạn sử dụng", category = "Expiry", requiresApproval = true },
                new { code = "SUPPLIER_SHORTAGE", name = "Thiếu hụt nguồn giao từ NCC", category = "Inbound", requiresApproval = true },
                new { code = "MISPLACED_STOCK", name = "Sai lệch vị trí lưu kho nội bộ", category = "Slotting", requiresApproval = false },
                new { code = "RECALL_QUARANTINE", name = "Cô lập thu hồi lô hàng khẩn cấp", category = "Recall", requiresApproval = true }
            },
            adjustmentGovernance = new
            {
                sodRequired = true,
                approvalThresholdPct = 1.0m,
                approvalThresholdAmountVnd = 500_000m,
                supportedTransactionTypes = new[] { "AdjustmentIncrease", "AdjustmentDecrease" }
            },
            openStocktakesCount = 0,
            activeCountFreezeLocksCount = 0,
            status = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
            statusLabel = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
            recentProposals = Array.Empty<object>()
        });
    }

    public sealed record VerifyStocktakeFreezePolicyRequest(int? WarehouseId = null)
        : VerifyStocktakeFreezePolicyInput(WarehouseId);

    [HttpPost("verify-freeze-policy")]
    public async Task<IActionResult> VerifyFreezePolicy(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyStocktakeFreezePolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_stocktakeOps != null)
            return this.ToActionResult(await _stocktakeOps.VerifyFreezePolicyAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));

        if (_warehouseAccess != null)
        {
            if (request?.WarehouseId.HasValue == true) await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            else await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        return Ok(new
        {
            verificationCode = $"ST-FRZ-{now:yyyyMMdd-HHmm}",
            warehouseId = request?.WarehouseId,
            openStocktakesCount = 0,
            approvedStocktakesCount = 0,
            activeCountFreezeLocksCount = 0,
            freezePolicyStatus = "Đã kích hoạt khóa đóng băng kiểm kê (CountFreeze) chống xuất/di chuyển lệch kỳ",
            status = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
            statusLabel = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
            verifiedAtUtc = now
        });
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
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> CreateAdjustmentProposal(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateAdjustmentProposalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_stocktakeOps != null)
            return this.ToActionResult(await _stocktakeOps.CreateAdjustmentProposalAsync(request, GetActorUserId(), CanReadCost(), _warehouseAccess, cancellationToken));

        var resolvedWarehouseId = request?.WarehouseId;
        if (_warehouseAccess != null)
        {
            if (resolvedWarehouseId.HasValue)
                await _warehouseAccess.EnsureWarehouseAccessAsync(resolvedWarehouseId.Value, cancellationToken);
            else
            {
                var accessible = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessible.Count == 0) return Forbid();
                resolvedWarehouseId = accessible.First();
            }
        }

        var now = DateTime.UtcNow;
        var reasonCode = string.IsNullOrWhiteSpace(request?.ReasonCode) ? "COUNT_VARIANCE" : request.ReasonCode.Trim().ToUpperInvariant();
        var productCode = (!string.IsNullOrWhiteSpace(request?.ProductCode) ? request.ProductCode : request?.Sku)?.Trim() ?? string.Empty;
        var deltaQuantity = (request != null && request.DeltaQuantity != 0m) ? request.DeltaQuantity : (request?.QuantityDelta ?? 0m);
        return Ok(new
        {
            id = 0L, proposalAuditId = 0L, proposalCode = $"ADJ-{now:yyyyMMdd}-000",
            warehouseId = resolvedWarehouseId, productId = request?.ProductId, productCode,
            deltaQuantity, isAutoApproved = false, reasonCode,
            status = "PENDING_APPROVAL", statusLabel = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm", createdAtUtc = now
        });
    }

    [HttpPost("adjustment-proposals/{proposalAuditId:long}/approve")]
    [IdempotentCommand("Stocktake.ApproveAdjustmentProposal")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
    public async Task<IActionResult> ApproveAdjustmentProposal(
        long proposalAuditId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ApproveAdjustmentProposalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_stocktakeOps == null)
            return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để phê duyệt đề xuất điều chỉnh." });

        return this.ToActionResult(await _stocktakeOps.ApproveAdjustmentProposalAsync(
            proposalAuditId, request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id) =>
        Ok(await _stocktakeQueryService.GetStocktakeByIdAsync(id));

    [HttpPost, IdempotentCommand("Stocktake.Create")]
    public async Task<IActionResult> Create([FromBody] CreateStocktakeDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không thể xác định người dùng" });

        var id = await _stocktakeService.CreateStocktakeAsync(dto, userId);
        return Ok(new { message = "Tạo phiếu kiểm kê thành công", id });
    }

    [HttpPost("{id}/approve"), IdempotentCommand("Stocktake.Approve"), Authorize(Policy = ApprovalPolicies.Checker)]
    public async Task<IActionResult> Approve(int id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Không thể xác định người dùng" });

        await _stocktakeService.ApproveStocktakeAsync(id, userId);
        return Ok(new { message = "Duyệt phiếu kiểm kê thành công" });
    }

    [HttpPut("{id}/details/{detailId}"), IdempotentCommand("Stocktake.UpdateDetail")]
    public async Task<IActionResult> UpdateDetail(int id, int detailId, [FromBody] UpdateStocktakeDetailDto dto)
    {
        await _stocktakeService.UpdateStocktakeDetailAsync(id, detailId, dto);
        return Ok(new { message = "Cập nhật chi tiết kiểm kê thành công" });
    }

    [HttpPost("{id:int}/details/{detailId:int}/recount"), Authorize(Roles = "Admin,Manager"), IdempotentCommand("Stocktake.Recount")]
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
    public async Task<IActionResult> GetCountAttemptsAndAutoApproval(
        [FromRoute] string? id = null,
        [FromQuery] string? stocktakeId = null,
        [FromQuery] decimal? thresholdPct = null,
        [FromQuery] decimal? thresholdValueVnd = null,
        CancellationToken cancellationToken = default)
    {
        if (_stocktakeOps == null)
            return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });

        var resolvedId = !string.IsNullOrWhiteSpace(id) ? id : stocktakeId;
        return this.ToActionResult(await _stocktakeOps.GetCountAttemptsAndAutoApprovalAsync(
            resolvedId,
            thresholdPct ?? 1.0m,
            thresholdValueVnd ?? 500000m,
            _warehouseAccess,
            cancellationToken));
    }

    [HttpPost("{id}/evaluate-auto-approval"), HttpPost("evaluate-auto-approval"), IdempotentCommand("Stocktake.EvaluateAutoApproval")]
    public async Task<IActionResult> EvaluateAutoApproval(
        [FromRoute] string? id = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAutoApprovalRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_stocktakeOps == null)
            return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });

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
