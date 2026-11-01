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
    private readonly IReceivingDiscrepancyService _discrepancies;
    private readonly IImportReceiptOperationsService _receiptOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;

    private IReceivingDiscrepancyService Discrepancies => _discrepancies;

    public ImportReceiptsController(
        IImportReceiptService importReceiptService,
        IReceivingDiscrepancyService discrepancies,
        IImportReceiptOperationsService receiptOps,
        IWarehouseAuthorizationService warehouseAccess)
    {
        ArgumentNullException.ThrowIfNull(importReceiptService);
        ArgumentNullException.ThrowIfNull(discrepancies);
        ArgumentNullException.ThrowIfNull(receiptOps);
        ArgumentNullException.ThrowIfNull(warehouseAccess);
        _importReceiptService = importReceiptService;
        _discrepancies = discrepancies;
        _receiptOps = receiptOps;
        _warehouseAccess = warehouseAccess;
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

    public sealed record ExpressPostImportReceiptRequest(
        int? TargetLocationId = null,
        string? TargetLocationCode = null,
        string? Note = null) : ExpressPostImportReceiptInput(TargetLocationId, TargetLocationCode, Note);

    [HttpPost("{id:int}/express-post"), IdempotentCommand("ImportReceipt.ExpressPost"), PermissionAuthorize(AppPermissions.ReceiptReceive, AppPermissions.ReceiptPost)]
    public async Task<IActionResult> ExpressPost(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExpressPostImportReceiptRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ReceiptReceive, AppPermissions.ReceiptPost))
            return Forbid();
        return this.ToActionResult(await _receiptOps.ExpressPostAsync(id, request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet, PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] ERP.Domain.Enums.ReceiptStatus? status = null,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null)
    {
        var items = await _importReceiptService.GetAllAsync(status);
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

    [HttpGet("pipeline-summary"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetPipelineSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => this.ToActionResult(await _receiptOps.GetPipelineSummaryAsync(warehouseId, _warehouseAccess, cancellationToken));

    [HttpGet("qc-and-rma-summary"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetQcAndRmaSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => this.ToActionResult(await _receiptOps.GetQcAndRmaSummaryAsync(warehouseId, _warehouseAccess, cancellationToken));

    public sealed record RmaIntakeCheckRequest(
        string? ReturnReference = null, string? ReasonCode = null, int? WarehouseId = null, string? RmaReferenceCode = null,
        int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null,
        string? LotNumber = null, decimal? Quantity = null) : RmaIntakeCheckInput(
            ReturnReference, ReasonCode, WarehouseId, RmaReferenceCode, ProductId, ProductCode, LocationId, LocationCode, LotNumber, Quantity);

    [HttpPost("rma-intake-check"), HttpPost("initiate-rma-inspection")]
    [IdempotentCommand("ImportReceipt.RmaIntakeCheck"), PermissionAuthorize(AppPermissions.ReturnCreate, AppPermissions.ReturnReceive, AppPermissions.ReceiptReceive, AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> RmaIntakeCheck(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RmaIntakeCheckRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ReturnCreate, AppPermissions.ReturnReceive, AppPermissions.ReceiptReceive, AppPermissions.ReceiptUpdate)) return Forbid();
        return this.ToActionResult(await _receiptOps.RmaIntakeCheckAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet("customer-returns"), PermissionAuthorize(AppPermissions.ReturnRead, AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetCustomerReturns([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => this.ToActionResult(await _receiptOps.GetCustomerReturnsAsync(warehouseId, _warehouseAccess, cancellationToken));

    public sealed record ExecuteRtvRefurbishRequest(
        int? WarehouseId = null, string? DispositionType = null, int? ProductId = null, int? LocationId = null,
        string? LotNumber = null, bool ExecuteStockMutation = false, string? ActionType = null, decimal Quantity = 1m,
        string? ProductCode = null, string? LocationCode = null, string? ReasonCode = null, string? Note = null,
        int? CustomerReturnId = null, string? ReturnCode = null, string? TrackingNumber = null, string? Status = null)
        : ExecuteRtvRefurbishInput(WarehouseId, DispositionType, ProductId, LocationId, LotNumber, ExecuteStockMutation, ActionType, Quantity, ProductCode, LocationCode, ReasonCode, Note, CustomerReturnId, ReturnCode, TrackingNumber, Status);

    [HttpPost("execute-rtv-refurbish"), IdempotentCommand("ImportReceipt.ExecuteRtvRefurbish"), PermissionAuthorize(AppPermissions.ReturnDisposition, AppPermissions.ReturnPost, AppPermissions.ReceiptReceive, AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> ExecuteRtvRefurbish(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteRtvRefurbishRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ReturnDisposition, AppPermissions.ReturnPost, AppPermissions.ReceiptReceive, AppPermissions.ReceiptUpdate)) return Forbid();
        return this.ToActionResult(await _receiptOps.ExecuteRtvRefurbishAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet("{id}"), PermissionAuthorize(AppPermissions.ReceiptRead)]
    public async Task<IActionResult> GetById(int id) => Ok(await _importReceiptService.GetByIdAsync(id));

    private bool CanReadInventoryCost() =>
        this.HasActionPermission(null, AppPermissions.InventoryCostRead);

    [HttpGet("{id:int}/mac-preview"), PermissionAuthorize(AppPermissions.ReceiptRead, AppPermissions.InventoryCostRead)]
    public async Task<IActionResult> GetMacPreview(int id, CancellationToken cancellationToken = default)
    {
        if (!CanReadInventoryCost())
            return StatusCode(403, new { code = "COST_READ_FORBIDDEN", errorCode = "COST_READ_FORBIDDEN", costHiddenByRbac = true, message = "Bạn không có quyền xem dữ liệu giá vốn bình quân gia quyền (inventory.cost.read)." });

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
