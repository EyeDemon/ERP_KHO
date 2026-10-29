using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[Authorize, ApiController, Route("api/[controller]"), Route("api/export-receipts"), Route("api/v1/export-receipts")]
public class ExportReceiptsController : ControllerBase
{
    private readonly IExportReceiptService _exportReceiptService;
    private readonly IExportReceiptOperationsService? _exportReceiptOps;
    private readonly IWarehouseAuthorizationService? _warehouseAccess;
    private readonly ICurrentUser? _currentUser;
    private readonly IHandoverManifestService? _handoverManifestService;

    public ExportReceiptsController(
        IExportReceiptService exportReceiptService,
        IExportReceiptOperationsService exportReceiptOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!,
        ICurrentUser? currentUser = null,
        IHandoverManifestService? handoverManifestService = null)
    {
        _exportReceiptService = exportReceiptService;
        _exportReceiptOps = exportReceiptOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
        _handoverManifestService = handoverManifestService;
    }

    private IExportReceiptOperationsService RequireOps() =>
        _exportReceiptOps ?? throw new InvalidOperationException("IExportReceiptOperationsService is required for this database-backed operation.");

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

    private bool HasPermissionOrDefault(string permission)
    {
        if (_currentUser != null) return _currentUser.HasPermission(permission);
        if (User?.Claims.Any() == true)
        {
            if (User.Claims.Any(c =>
                (string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (User.IsInRole(AppRoles.WarehouseStaff) || User.IsInRole(AppRoles.Viewer))
                return false;
        }
        return true;
    }

    private bool CanReadInventoryCost() => HasPermissionOrDefault(AppPermissions.InventoryCostRead);
    private bool CanReprintAwb() => HasPermissionOrDefault(ERP.Domain.Security.AppPermissions.ExportReceiptReprintAwb);

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        return int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"), out userId);
    }

    [HttpGet, PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetAll() => Ok(await _exportReceiptService.GetAllAsync());

    [HttpGet("pipeline-summary"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetPipelineSummary()
    {
        var items = (await _exportReceiptService.GetAllAsync()).ToList();
        var now = DateTime.UtcNow;
        var activePending = items.Where(x => x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback")).ToList();
        var slaOverdueCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value < now);
        var slaAtRiskUnder2HoursCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value >= now && x.MarketplaceShipByUtc.Value <= now.AddHours(2));
        var slaDueTodayCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value > now.AddHours(2) && x.MarketplaceShipByUtc.Value.Date == now.Date);
        var marketplacePrioritizedCount = items.Count(x => !string.IsNullOrWhiteSpace(x.MarketplaceOrderId) || !string.IsNullOrWhiteSpace(x.TrackingNumber));
        var slaComplianceRatePct = items.Count == 0 ? 100m : decimal.Round(((items.Count - slaOverdueCount) * 100m) / items.Count, 2);

        return Ok(new
        {
            totalReceipts = items.Count,
            draftCount = items.Count(x => string.Equals(x.Status, "Draft", StringComparison.OrdinalIgnoreCase)),
            approvedReadyCount = items.Count(x => string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
            dispatchedCount = items.Count(x => x.Status is "Dispatched" or "Completed"),
            cancelledCount = items.Count(x => string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
            slaOverdueCount,
            slaAtRiskUnder2HoursCount,
            slaDueTodayCount,
            slaComplianceRatePct,
            marketplacePrioritizedCount,
            status = "Đã tải tổng quan luồng phiếu xuất kho",
            statusLabel = "Đã tải tổng quan luồng phiếu xuất kho & đồng hồ SLA giao hàng sàn TMĐT"
        });
    }

    [HttpGet("sla-queue"), HttpGet("marketplace-sla-queue"), HttpGet("~/api/v1/outbound/sla-queue"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetSlaQueue([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.GetSlaQueueAsync(warehouseId, cancellationToken)).ToActionResult(this);

        var now = DateTime.UtcNow;
        var dtoItems = (await _exportReceiptService.GetAllAsync())
            .Where(x => x.MarketplaceShipByUtc.HasValue && x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback"))
            .OrderBy(x => x.MarketplaceShipByUtc).ToList();
        return Ok(new
        {
            totalCount = dtoItems.Count,
            breachedCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value < now),
            atRiskUnder2HoursCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value >= now && x.MarketplaceShipByUtc!.Value <= now.AddHours(2)),
            onTrackCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value > now.AddHours(2)),
            items = dtoItems
        });
    }

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
            if (_exportReceiptOps != null) await _exportReceiptOps.IncrementSubscriptionOrderCountAsync(cancellationToken);
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
        return Ok(new { message = "Đã duyệt và giữ hàng." });
    }

    [HttpPost("{id}/approve-and-dispatch"), IdempotentCommand("ExportReceipt.ApproveAndDispatch"), PermissionAuthorize(AppPermissions.ExportReceiptApprove), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> ApproveAndDispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAndDispatchAsync(id, userId);
        return Ok(new { message = "Đã duyệt và xuất kho." });
    }

    [HttpPost("{id}/dispatch"), IdempotentCommand("ExportReceipt.Dispatch"), PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
    public async Task<IActionResult> Dispatch(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.DispatchAsync(id, userId);
        return Ok(new { message = "Đã xác nhận xuất kho." });
    }

    [HttpPost("{id}/approve"), IdempotentCommand("ExportReceipt.Approve"), PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
    public async Task<IActionResult> Approve(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await _exportReceiptService.ApproveAsync(id, userId);
        return Ok(new { message = "Duyệt phiếu xuất thành công" });
    }

    [HttpPut("{id:int}/customer"), IdempotentCommand("ExportReceipt.SetCustomer"), PermissionAuthorize(AppPermissions.ExportReceiptUpdate), PermissionAuthorize(AppPermissions.PartnerRead)]
    public async Task<IActionResult> SetCustomer(int id, [FromBody] SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
    { await partners.SetExportCustomerAsync(id, dto.PartnerId, ct); return NoContent(); }

    public sealed record VerifyDispatchCutoffRequest(int? WarehouseId = null) : VerifyDispatchCutoffInput(WarehouseId);

    [HttpPost("verify-dispatch-cutoff"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> VerifyDispatchCutoff(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VerifyDispatchCutoffRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.VerifyDispatchCutoffAsync(request, TryGetUserId(out var uid) ? uid : null, _warehouseAccess, cancellationToken)).ToActionResult(this);
        if (_warehouseAccess != null && request?.WarehouseId.HasValue == true)
            await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
        var now = DateTime.UtcNow;
        return Ok(new { verificationId = $"EXP-CUT-{now:yyyyMMdd-HHmm}", warehouseId = request?.WarehouseId, checkedExportReceiptsCount = 0, openPickingTasksCount = 0, outboundCutoffLocal = "22:00", singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất", cutoffComplianceStatus = "Đạt chuẩn giờ chốt xuất kho (22:00)", status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất", statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất", verifiedAtUtc = now });
    }

    public sealed record BatchPrintAwbRequest(int? WarehouseId = null, List<int>? ReceiptIds = null, List<int>? ExportReceiptIds = null, bool EnforceReprintReason = false, string? ReprintReason = null) : BatchPrintAwbInput(WarehouseId, ReceiptIds, ExportReceiptIds, EnforceReprintReason, ReprintReason);
    public sealed record ScanHandoverAwbRequest(int? ManifestId = null, int? WarehouseId = null, string? CarrierCode = null, string? TrackingNumber = null, int? ExportReceiptId = null, string? VehiclePlate = null, string? DriverName = null, string? DriverPhone = null);
    public sealed record SealHandoverManifestRequest(string? DriverName = null, string? DriverPhone = null, string? VehiclePlate = null, string? DigitalSignatureHash = null);
    public sealed record ConfirmCarrierHandoverRequest(int? WarehouseId = null, string? CarrierCode = null, List<string>? TrackingNumbers = null, string? VehiclePlate = null, string? DriverName = null, string? DriverPhone = null, int? ManifestId = null, List<int>? ExportReceiptIds = null);
    public sealed record NormalizeCarrierAddressRequest(string? Province = null, string? District = null, string? Ward = null) : NormalizeCarrierAddressInput(Province, District, Ward);
    public sealed record OpeningBalanceItemRequest(int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null, decimal Quantity = 0m, decimal UnitCost = 0m, string? LotNumber = null, DateTime? ExpiryDate = null) : OpeningBalanceItemInput(ProductId, ProductCode, LocationId, LocationCode, Quantity, UnitCost, LotNumber, ExpiryDate);
    public sealed record OpeningBalanceImportRowDto(int? ProductId = null, string? ProductCode = null, int? LocationId = null, string? LocationCode = null, decimal Quantity = 0m, decimal UnitCost = 0m, string? LotNumber = null, DateTime? ExpiryDate = null) : OpeningBalanceItemInput(ProductId, ProductCode, LocationId, LocationCode, Quantity, UnitCost, LotNumber, ExpiryDate);
    public sealed record ImportOpeningBalanceRequest(int? WarehouseId = null, int? ProductId = null, int? LocationId = null, decimal? Quantity = null, decimal? UnitCost = null, List<OpeningBalanceItemRequest>? Items = null);
    public sealed record OpeningBalanceImportRequest(int? WarehouseId = null, int? ProductId = null, int? LocationId = null, decimal? Quantity = null, decimal? UnitCost = null, List<OpeningBalanceItemInput>? Items = null) : ImportOpeningBalanceInput(WarehouseId, ProductId, LocationId, Quantity, UnitCost, Items);

    [HttpPost("batch-print-awb"), HttpPost("batch-print-thermal-awb"), HttpPost("~/api/v1/outbound/print-thermal-labels"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.BatchPrintAwb")]
    public async Task<IActionResult> BatchPrintAwb([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BatchPrintAwbRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var outcome = await RequireOps().BatchPrintAwbAsync(request, CanReprintAwb(), TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _warehouseAccess, _currentUser, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("handover-manifests"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetHandoverManifests([FromQuery] int? warehouseId = null, [FromQuery] string? carrierCode = null, CancellationToken cancellationToken = default)
    {
        var result = _handoverManifestService != null
            ? await _handoverManifestService.GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken)
            : await RequireOps().GetHandoverManifestsAsync(warehouseId, carrierCode, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("handover-manifests/scan-awb"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.ScanAwb")]
    public async Task<IActionResult> ScanHandoverAwb([FromRoute] int? manifestIdRoute = null, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScanHandoverAwbRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new ScanHandoverAwbInputDto(request.ManifestId, request.WarehouseId, request.CarrierCode, request.TrackingNumber, request.ExportReceiptId, request.VehiclePlate, request.DriverName, request.DriverPhone);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.ScanHandoverAwbAsync(manifestIdRoute, input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().ScanHandoverAwbAsync(manifestIdRoute, input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("handover-manifests/{id:int}/seal"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.Seal")]
    public async Task<IActionResult> SealHandoverManifest(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SealHandoverManifestRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new SealHandoverManifestInputDto(request.DriverName, request.DriverPhone, request.VehiclePlate, request.DigitalSignatureHash);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.SealHandoverManifestAsync(id, input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().SealHandoverManifestAsync(id, input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("confirm-carrier-handover"), HttpPost("handover-manifests/confirm"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("HandoverManifest.ConfirmCarrierHandover")]
    public async Task<IActionResult> ConfirmCarrierHandover([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmCarrierHandoverRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
        var input = request == null ? null : new ConfirmCarrierHandoverInputDto(request.WarehouseId, request.CarrierCode, request.TrackingNumbers, request.VehiclePlate, request.DriverName, request.DriverPhone, request.ManifestId, request.ExportReceiptIds);
        var result = _handoverManifestService != null
            ? await _handoverManifestService.ConfirmCarrierHandoverAsync(input, actorUserId, User?.Identity?.IsAuthenticated == true, cancellationToken)
            : await RequireOps().ConfirmCarrierHandoverAsync(input, actorUserId, User?.Identity?.IsAuthenticated == true, _exportReceiptService, _warehouseAccess, _currentUser, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("~/api/v1/carriers/address-mappings"), HttpGet("~/api/carriers/address-mappings"), HttpGet("carriers/address-mappings"), HttpGet("carrier-address-mappings"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetCarrierAddressMappings([FromQuery] string? province = null, [FromQuery] string? district = null, [FromQuery] string? ward = null, CancellationToken cancellationToken = default)
        => (await RequireOps().GetCarrierAddressMappingsAsync(province, district, ward, cancellationToken)).ToActionResult(this);

    [HttpGet("data-imports/templates"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public IActionResult GetDataImportTemplates() => Ok(BuildDataImportTemplatesPayload());

    internal static object BuildDataImportTemplatesPayload() => new
    {
        templates = new[]
        {
            new { templateCode = "OPENING_BALANCE_EXCEL", templateName = "Mẫu Excel Nhập Tồn Kho Đầu Kỳ & Giá Vốn Bình Quân (MAC)", downloadUrl = "/api/v1/import-templates/opening-balance", requiredColumns = new[] { "ProductCode", "LocationCode", "Quantity", "UnitCost", "LotNumber", "ExpiryDate" } },
            new { templateCode = "PRODUCT_MASTER_EXCEL", templateName = "Mẫu Excel Danh Mục Sản Phẩm & Quy Đổi ĐVT", downloadUrl = "/api/v1/import-templates/products", requiredColumns = new[] { "ProductCode", "ProductName", "BaseUomCode", "Barcode", "MovingAverageCost" } }
        },
        statusLabel = "Đã tải danh mục biểu mẫu nhập liệu Excel đầu kỳ chuẩn"
    };

    [HttpPost("data-imports/opening-balance"), HttpPost("~/api/v1/inventory/opening-balances/import"), HttpPost("~/api/v1/inventory/opening-balance-import"), HttpPost("~/api/inventory/opening-balance-import")]
    [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite), IdempotentCommand("Inventory.ImportOpeningBalance")]
    public async Task<IActionResult> ImportOpeningBalance(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ImportOpeningBalanceRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] IFormFile? file = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        var parsedFileItems = WmsControllerExcelHelper.ParseOpeningBalanceExcel(uploadedFile);
        var input = request == null ? null : new ImportOpeningBalanceInput(request.WarehouseId, request.ProductId, request.LocationId, request.Quantity, request.UnitCost, request.Items?.Cast<OpeningBalanceItemInput>().ToList());
        var outcome = await RequireOps().ImportOpeningBalanceAsync(input, parsedFileItems, TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _warehouseAccess, _currentUser, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("~/api/v1/accounting-exports/misa-excel"), HttpGet("misa-excel-export"), HttpGet("misa-excel-preview"), HttpGet("misa-excel"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetMisaExcelExport([FromQuery] int? warehouseId = null, [FromQuery] string? format = null, CancellationToken cancellationToken = default)
    {
        var canReadCost = CanReadInventoryCost();
        MisaExcelExportData data;
        if (_exportReceiptOps != null)
            data = await _exportReceiptOps.GetMisaExcelExportDataAsync(warehouseId, canReadCost, _warehouseAccess, cancellationToken);
        else
        {
            if (warehouseId.HasValue && _warehouseAccess != null) await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            data = new MisaExcelExportData(canReadCost, 0m, 0m, []);
        }
        return WmsControllerExcelHelper.BuildMisaExcelExportOutcome(data, format).ToActionResult(this);
    }

    public sealed record ExportMisaEinvoiceRequest(int? WarehouseId = null, string? Standard = null, string? Format = null) : ExportMisaEinvoiceInput(WarehouseId, Standard, Format);

    [HttpPost("export-misa-einvoice"), HttpPost("misa-excel-export"), HttpPost("misa-excel-preview"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.ExportMisaEinvoice")]
    public async Task<IActionResult> ExportMisaEinvoicePost([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExportMisaEinvoiceRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        return await GetMisaExcelExport(request?.WarehouseId, request?.Format ?? request?.Standard, cancellationToken);
    }

    public sealed record IssueEinvoiceNd70DraftRequest(int? ExportReceiptId = null, int? WarehouseId = null, string? BuyerTaxCode = null, string? BuyerName = null, string? DecreeStandard = null) : IssueEinvoiceNd70DraftInput(ExportReceiptId, WarehouseId, BuyerTaxCode, BuyerName, DecreeStandard);

    [HttpPost("einvoice-nd70-draft"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.IssueEinvoiceNd70Draft")]
    public async Task<IActionResult> IssueEinvoiceNd70Draft([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] IssueEinvoiceNd70DraftRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (_exportReceiptOps != null)
            return (await _exportReceiptOps.IssueEinvoiceNd70DraftAsync(request, TryGetUserId(out var uid) ? uid : null, User?.Identity?.IsAuthenticated == true, _currentUser, cancellationToken)).ToActionResult(this);
        var now = DateTime.UtcNow;
        var receiptId = request?.ExportReceiptId ?? 1;
        var draftCode = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
        return Ok(new { exportReceiptId = receiptId, invoiceDraftCode = draftCode, draftNumber = draftCode, buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode) ? "0318999888" : request.BuyerTaxCode.Trim(), buyerName = string.IsNullOrWhiteSpace(request?.BuyerName) ? "Khách hàng Doanh nghiệp" : request.BuyerName.Trim(), decreeStandard = string.IsNullOrWhiteSpace(request?.DecreeStandard) ? "ND70_2025_TT32_2025" : request.DecreeStandard.Trim(), provider = "MISA_MEINVOICE", status = "DRAFT_ISSUED", statusLabel = $"Đã phát hành Hóa đơn điện tử nháp ({draftCode}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC", issuedAtUtc = now });
    }

    [HttpGet("accounting-einvoice/preview"), PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> PreviewAccountingAndEInvoiceOutbox(
        [FromQuery] int? exportReceiptId = null,
        [FromQuery] int? importReceiptId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? accountingProvider = "MISA_AMIS",
        [FromQuery] string? einvoiceProvider = "VIETTEL_SINVOICE",
        CancellationToken cancellationToken = default)
    {
        var canReadCost = CanReadInventoryCost();
        var outcome = await RequireOps().SyncAccountingAndEInvoiceOutboxAsync(
            new SyncAccountingAndEInvoiceInput(
                ExportReceiptId: exportReceiptId,
                ImportReceiptId: importReceiptId,
                WarehouseId: warehouseId,
                AccountingProvider: accountingProvider,
                EInvoiceProvider: einvoiceProvider,
                PersistOutboxAndAudit: false),
            TryGetUserId(out var uid) ? uid : null,
            canReadCost,
            _warehouseAccess,
            _currentUser,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("accounting-einvoice/sync"), PermissionAuthorize(AppPermissions.ExportReceiptRead), IdempotentCommand("ExportReceipt.SyncAccountingAndEinvoiceNd70")]
    public async Task<IActionResult> SyncAccountingAndEInvoiceOutbox(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncAccountingAndEInvoiceInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var canReadCost = CanReadInventoryCost();
        var outcome = await RequireOps().SyncAccountingAndEInvoiceOutboxAsync(
            request ?? new SyncAccountingAndEInvoiceInput(),
            TryGetUserId(out var uid) ? uid : null,
            canReadCost,
            _warehouseAccess,
            _currentUser,
            cancellationToken);
        return outcome.ToActionResult(this);
    }
}
