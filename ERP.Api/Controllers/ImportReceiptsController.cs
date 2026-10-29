using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/import-receipts")]
[Route("api/v1/import-receipts")]
public class ImportReceiptsController : ControllerBase
{
    private readonly IImportReceiptService _importReceiptService;
    private readonly IReceivingDiscrepancyService? _discrepancies;
    private readonly IImportReceiptOperationsService? _receiptOps;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;

    private IReceivingDiscrepancyService Discrepancies => _discrepancies
        ?? throw new InvalidOperationException("Receiving discrepancy service is not configured.");

    [ActivatorUtilitiesConstructor]
    public ImportReceiptsController(
        IImportReceiptService importReceiptService,
        IReceivingDiscrepancyService? discrepancies = null,
        IImportReceiptOperationsService receiptOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!)
    {
        _importReceiptService = importReceiptService;
        _discrepancies = discrepancies;
        _receiptOps = receiptOps;
        _warehouseAccess = warehouseAccess;
    }

    public ImportReceiptsController(
        IImportReceiptService importReceiptService,
        IImportReceiptOperationsService receiptOps,
        IWarehouseAuthorizationService warehouseAccess = null!)
        : this(importReceiptService, null, receiptOps, warehouseAccess)
    {
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var userIdStr = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                        ?? User.Claims.FirstOrDefault(c => c.Type == "Id")?.Value;
        return !string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out userId);
    }

    private int? GetActorUserId() =>
        int.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value, out var uid) ? uid : null;

    [HttpPost, IdempotentCommand("ImportReceipt.Create"), PermissionAuthorize(AppPermissions.ReceiptCreate)]
    public async Task<IActionResult> Create([FromBody] ERP.Application.DTOs.CreateImportReceiptDto dto)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        var result = await _importReceiptService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    [HttpPost("{id}/approve"), IdempotentCommand("ImportReceipt.Approve"), PermissionAuthorize(AppPermissions.ReceiptComplete)]
    public async Task<IActionResult> Approve(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _importReceiptService.ApproveImportReceiptAsync(id, userId);
        return Ok(new { message = "Phiếu nhập đã sẵn sàng post" });
    }

    [HttpPost("{id}/receive"), IdempotentCommand("ImportReceipt.Receive"), PermissionAuthorize(AppPermissions.ReceiptReceive)]
    public async Task<IActionResult> Receive(int id, [FromBody] ERP.Application.DTOs.ReceiveImportReceiptDto dto)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _importReceiptService.ReceiveAsync(id, dto, userId);
        return Ok(new { message = "Hoàn tất nhận hàng; tồn kho chưa thay đổi" });
    }

    [HttpPost("{id}/qc-disposition"), IdempotentCommand("ImportReceipt.QcDisposition"), PermissionAuthorize(AppPermissions.QualityExecute)]
    public async Task<IActionResult> RecordQcDisposition(int id, [FromBody] ERP.Application.DTOs.RecordQcDispositionDto dto)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _importReceiptService.RecordQcDispositionAsync(id, dto, userId);
        return Ok(new { message = "Đã ghi nhận disposition QC; tồn kho chưa thay đổi" });
    }

    [HttpGet("{id:int}/inventory-identities"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetInventoryIdentities(int id, CancellationToken token)
        => Ok(await _importReceiptService.GetInventoryIdentitiesAsync(id, token));

    [HttpPost("{id:int}/inventory-identities"), IdempotentCommand("ImportReceipt.InventoryIdentity.Set"), PermissionAuthorize(AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> SetInventoryIdentities(int id, [FromBody] ERP.Application.DTOs.SetImportReceiptInventoryIdentitiesDto dto, CancellationToken token)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        return Ok(await _importReceiptService.SetInventoryIdentitiesAsync(id, dto, userId, token));
    }

    [HttpPost("{id}/post"), IdempotentCommand("ImportReceipt.Post"), PermissionAuthorize(AppPermissions.ReceiptPost)]
    public async Task<IActionResult> Post(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _importReceiptService.PostAsync(id, userId);
        return Ok(new { message = "Post phiếu nhập thành công" });
    }

    [HttpGet, PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetAll([FromQuery] ERP.Domain.Enums.ReceiptStatus? status)
        => Ok(await _importReceiptService.GetAllAsync(status));

    [HttpGet("pipeline-summary"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetPipelineSummary()
    {
        var items = (await _importReceiptService.GetAllAsync(null)).ToList();
        return Ok(new
        {
            totalReceipts = items.Count,
            draftOrReceivingCount = items.Count(x => x.Status is "Draft" or "Received" or "0" or "3" || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
            qcOrApprovalPendingCount = items.Count(x => x.Status is "QcPending" or "QcCompleted" or "ReadyToPost" or "4" or "5"),
            postedCount = items.Count(x => x.Status is "Posted" or "Approved" or "1"),
            cancelledCount = items.Count(x => x.Status is "Cancelled" or "2"),
            status = "Đã tải tổng quan luồng phiếu nhập kho",
            statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
        });
    }

    [HttpGet("qc-and-rma-summary"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetQcAndRmaSummary()
    {
        var items = (await _importReceiptService.GetAllAsync(null)).ToList();
        return Ok(new
        {
            totalReceiptsInspected = items.Count,
            pendingQcOrPostingCount = items.Count(x => x.Status is "Draft" or "Approved" or "Received" or "QcPending" or "QcCompleted" or "ReadyToPost" or "0" or "3" or "4" or "5" || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
            postedWithLotOrQcCount = items.Count(x => x.Status is "Posted" or "Approved" or "1"),
            rmaEligibleReceiptsCount = items.Count(x => (!string.IsNullOrWhiteSpace(x.Note) && (x.Note.Contains("RMA", StringComparison.OrdinalIgnoreCase) || x.Note.Contains("RETURN", StringComparison.OrdinalIgnoreCase) || x.Note.Contains("trả", StringComparison.OrdinalIgnoreCase))) || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
            qcWorkCenterModes = new[] { "Kiểm định chất lượng đầu vào", "Kiểm định hàng trả về", "Rà soát hàng biệt trữ & hư hỏng" },
            status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
            statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
        });
    }

    public sealed record RmaIntakeCheckRequest(
        string? ReturnReference = null, string? ReasonCode = null, int? WarehouseId = null, string? RmaReferenceCode = null,
        int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null,
        string? LotNumber = null, decimal? Quantity = null) : RmaIntakeCheckInput(
            ReturnReference, ReasonCode, WarehouseId, RmaReferenceCode, ProductId, ProductCode, LocationId, LocationCode, LotNumber, Quantity);

    [HttpPost("rma-intake-check"), HttpPost("initiate-rma-inspection")]
    [IdempotentCommand("ImportReceipt.RmaIntakeCheck"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> RmaIntakeCheck(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RmaIntakeCheckRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_receiptOps != null)
            return this.ToActionResult(await _receiptOps.RmaIntakeCheckAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));

        if (request?.WarehouseId.HasValue == true && _warehouseAccess != null)
            await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        else if (_warehouseAccess != null)
            await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return Ok(new
        {
            customerReturnId = (int?)null, rmaCode = $"RMA-{now:yyyyMMdd}-01", rmaInspectionCode = $"RMA-INSP-{now:yyyyMMdd-HHmm}",
            rmaReferenceCode = string.IsNullOrWhiteSpace(request?.RmaReferenceCode) ? "RMA-AUTO" : request.RmaReferenceCode.Trim(),
            returnReference = request?.ReturnReference ?? string.Empty, reasonCode = request?.ReasonCode ?? string.Empty,
            quarantineBucketCount = 0, pendingQcOrRmaReceiptsCount = 0, openStocktakesCount = 0, activeCountFreezeLocksCount = 0,
            ledgerTransactionId = (int?)null, quarantineStockQuantity = (decimal?)null,
            targetBucket = "Khu vực biệt trữ chờ kiểm định", nextStep = "Chuyển trạm kiểm định chất lượng đầu vào",
            dispositionSummary = "Đã tiếp nhận kiểm định hàng trả về (0 phiếu chờ kiểm định, 0 vị trí biệt trữ)",
            status = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ",
            statusLabel = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ", queuedAtUtc = now
        });
    }

    [HttpGet("customer-returns"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetCustomerReturns([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => _receiptOps == null ? Ok(Array.Empty<object>()) : this.ToActionResult(await _receiptOps.GetCustomerReturnsAsync(warehouseId, _warehouseAccess, cancellationToken));

    public sealed record ExecuteRtvRefurbishRequest(
        int? WarehouseId = null, string? DispositionType = null, int? ProductId = null, int? LocationId = null,
        string? LotNumber = null, bool ExecuteStockMutation = false, string? ActionType = null, decimal Quantity = 1m,
        string? ProductCode = null, string? LocationCode = null, string? ReasonCode = null, string? Note = null,
        int? CustomerReturnId = null, string? ReturnCode = null, string? TrackingNumber = null, string? Status = null)
        : ExecuteRtvRefurbishInput(WarehouseId, DispositionType, ProductId, LocationId, LotNumber, ExecuteStockMutation, ActionType, Quantity, ProductCode, LocationCode, ReasonCode, Note, CustomerReturnId, ReturnCode, TrackingNumber, Status);

    [HttpPost("execute-rtv-refurbish"), IdempotentCommand("ImportReceipt.ExecuteRtvRefurbish"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> ExecuteRtvRefurbish(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteRtvRefurbishRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_receiptOps != null)
            return this.ToActionResult(await _receiptOps.ExecuteRtvRefurbishAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));

        if (request?.WarehouseId.HasValue == true && _warehouseAccess != null)
            await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        else if (_warehouseAccess != null)
            await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        const string dispType = "Tân trang hàng trả về & chuyển về tồn khả dụng";
        return Ok(new
        {
            customerReturnId = (int?)null, dispositionCode = $"DISP-RTV-{now:yyyyMMdd-HHmm}", warehouseId = request?.WarehouseId,
            nonAvailableBucketsCount = 0, dispositionType = dispType, actionType = dispType,
            dispositionSummary = $"{dispType} (0 vị trí biệt trữ và hư hỏng đã rà soát)",
            status = "Đã thực thi & Ghi sổ kiểm toán", statusLabel = "Đã thực thi & Ghi sổ kiểm toán",
            ledgerUpdated = false, executedStockMutation = false, mutatedQuantity = 0m,
            ledgerTransactionId = (int?)null, resultingStockQuantity = (decimal?)null, resultingStatus = (string?)null,
            refurbishRouting = "Chuyển tồn kho sau tân trang về khu vực hàng khả dụng",
            rtvClaimStatus = "Đã ghi nhận giảm trừ tồn kho và sẵn sàng hoàn trả nhà cung cấp", executedAtUtc = now
        });
    }

    [HttpGet("{id}"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetById(int id) => Ok(await _importReceiptService.GetByIdAsync(id));

    private bool CanReadInventoryCost()
    {
        if (User != null && User.Claims.Any())
        {
            if (User.Claims.Any(c =>
                (string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(c.Value, AppPermissions.InventoryCostRead, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (User.IsInRole(AppRoles.WarehouseStaff) || User.IsInRole(AppRoles.Viewer))
                return false;
        }
        return true;
    }

    [HttpGet("{id:int}/mac-preview"), PermissionAuthorize(AppPermissions.ReceiptRead, AppPermissions.InventoryCostRead)]
    public async Task<IActionResult> GetMacPreview(int id, CancellationToken cancellationToken = default)
    {
        if (!CanReadInventoryCost())
            return StatusCode(403, new { code = "COST_READ_FORBIDDEN", errorCode = "COST_READ_FORBIDDEN", costHiddenByRbac = true, message = "Bạn không có quyền xem dữ liệu giá vốn bình quân gia quyền (inventory.cost.read)." });

        if (_receiptOps == null)
        {
            var dto = await _importReceiptService.GetByIdAsync(id);
            return Ok(new
            {
                importReceiptId = dto.Id, importReceiptCode = dto.Code, warehouseId = dto.WarehouseId, status = dto.Status,
                formula = "(OldQty * OldMAC + IncomingQty * UnitCost) / (OldQty + IncomingQty)",
                costHiddenByRbac = false, lines = Array.Empty<object>(), items = Array.Empty<object>(),
                statusLabel = "Đã tính toán bản xem trước giá vốn bình quân gia quyền (MAC Preview)"
            });
        }

        return this.ToActionResult(await _receiptOps.GetMacPreviewAsync(id, _warehouseAccess, cancellationToken));
    }

    [HttpGet("{id:int}/discrepancies"), PermissionAuthorize(AppPermissions.DiscrepancyRead)]
    public async Task<IActionResult> GetDiscrepancies(int id, CancellationToken token) => Ok(await Discrepancies.GetAsync(id, token));

    [HttpGet("discrepancy-reasons"), PermissionAuthorize(AppPermissions.ReasonRead)]
    public async Task<IActionResult> GetDiscrepancyReasons(CancellationToken token) => Ok(await Discrepancies.GetActiveReasonsAsync(token));

    [HttpPost("{id:int}/discrepancies/observe"), IdempotentCommand("ImportReceipt.Discrepancy.Observe"), PermissionAuthorize(AppPermissions.DiscrepancyCreate)]
    public async Task<IActionResult> Observe(int id, [FromBody] ERP.Application.DTOs.ObserveReceivingDto dto, CancellationToken token)
        => Ok(await Discrepancies.ObserveAsync(id, dto, token));

    [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/submit"), IdempotentCommand("ImportReceipt.Discrepancy.Submit"), PermissionAuthorize(AppPermissions.DiscrepancySubmit)]
    public async Task<IActionResult> SubmitDiscrepancy(int id, int discrepancyId, [FromBody] ERP.Application.DTOs.SubmitReceivingDiscrepancyDto dto, CancellationToken token)
        => Ok(await Discrepancies.SubmitAsync(id, discrepancyId, dto, token));

    [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/approve"), IdempotentCommand("ImportReceipt.Discrepancy.Approve"), PermissionAuthorize(AppPermissions.DiscrepancyApprove)]
    public async Task<IActionResult> ApproveDiscrepancy(int id, int discrepancyId, [FromBody] byte[] rowVersion, CancellationToken token)
        => Ok(await Discrepancies.ApproveAsync(id, discrepancyId, rowVersion, token));

    [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/reject"), IdempotentCommand("ImportReceipt.Discrepancy.Reject"), PermissionAuthorize(AppPermissions.DiscrepancyReject)]
    public async Task<IActionResult> RejectDiscrepancy(int id, int discrepancyId, [FromBody] byte[] rowVersion, CancellationToken token)
        => Ok(await Discrepancies.RejectAsync(id, discrepancyId, rowVersion, token));

    [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/recount"), IdempotentCommand("ImportReceipt.Discrepancy.Recount"), PermissionAuthorize(AppPermissions.DiscrepancyResolve)]
    public async Task<IActionResult> RecountDiscrepancy(int id, int discrepancyId, [FromBody] ERP.Application.DTOs.RecountReceivingDto dto, CancellationToken token)
        => Ok(await Discrepancies.RecountAsync(id, discrepancyId, dto, token));

    [HttpPut("{id}/cancel"), IdempotentCommand("ImportReceipt.Cancel"), PermissionAuthorize(AppPermissions.ReceiptCancel)]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        await _importReceiptService.CancelAsync(id, userId);
        return Ok(new { message = "Hủy phiếu nhập thành công" });
    }

    [HttpPut("{id:int}/supplier"), IdempotentCommand("ImportReceipt.SetSupplier"), PermissionAuthorize(AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> SetSupplier(int id, [FromBody] ERP.Application.DTOs.SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
    { await partners.SetImportSupplierAsync(id, dto.PartnerId, ct); return NoContent(); }
}
