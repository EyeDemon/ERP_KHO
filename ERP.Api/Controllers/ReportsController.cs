using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[Authorize(Roles = AppRoles.AllRoles)]
[PermissionAuthorize(AppPermissions.InventoryRead)]
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IWarehouseAuthorizationService? _warehouseAuth;
    private readonly IReportOperationsService? _reportOps;
    private readonly ICurrentUser? _currentUser;

    public ReportsController(
        IReportService reportService,
        IReportOperationsService? reportOps = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        ICurrentUser? currentUser = null)
    {
        _reportService = reportService;
        _reportOps = reportOps;
        _warehouseAuth = warehouseAuth;
        _currentUser = currentUser;
    }

    private bool CanReadInventoryCost() => ReportOperationsTextAndFallbackHelper.CanReadInventoryCost(_currentUser, User);
    private int? GetActorUserId() => ReportOperationsTextAndFallbackHelper.GetActorUserId(User, _currentUser);

    public static string? NormalizeActorDisplayName(string? value) =>
        ReportOperationsTextAndFallbackHelper.NormalizeActorDisplayName(value);

    public static string TranslateInventoryStatusLabel(string? status) =>
        ReportOperationsTextAndFallbackHelper.TranslateInventoryStatusLabel(status);

    public static string? TranslateReferenceTypeLabel(string? referenceType) =>
        ReportOperationsTextAndFallbackHelper.TranslateReferenceTypeLabel(referenceType);

    public static string? NormalizeTransactionNote(string? note) =>
        ReportOperationsTextAndFallbackHelper.NormalizeTransactionNote(note);

    [HttpGet("operational-dashboard")]
    [Authorize]
    public Task<IActionResult> GetOperationalDashboard(CancellationToken cancellationToken = default) =>
        GetOperationalDashboard(_reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalDashboard(IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        GetOperationalDashboard(_reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalDashboard(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetOperationalDashboardAsync(warehouseAuth ?? _warehouseAuth, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("OperationalDashboard", warehouseAuth ?? _warehouseAuth, cancellationToken: cancellationToken));

    [HttpGet("multi-warehouse-control-tower")]
    [Authorize]
    public Task<IActionResult> GetMultiWarehouseControlTower(CancellationToken cancellationToken = default) =>
        GetMultiWarehouseControlTower(_reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetMultiWarehouseControlTower(IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        GetMultiWarehouseControlTower(_reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetMultiWarehouseControlTower(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetMultiWarehouseControlTowerAsync(warehouseAuth ?? _warehouseAuth, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("MultiWarehouseControlTower", warehouseAuth ?? _warehouseAuth, cancellationToken: cancellationToken));

    [HttpGet("productivity-and-bi-snapshot")]
    [Authorize]
    public Task<IActionResult> GetProductivityAndBiSnapshot([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetProductivityAndBiSnapshot(_reportOps, _warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetProductivityAndBiSnapshot(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetProductivityAndBiSnapshot(_reportOps, warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetProductivityAndBiSnapshot(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetProductivityAndBiSnapshotAsync(warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("ProductivityAndBiSnapshot", warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken: cancellationToken));

    [HttpPost("sync-bi-snapshot")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    public async Task<IActionResult> SyncBiSnapshot([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncBiSnapshotRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.SyncBiSnapshotAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken), () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("SyncBiSnapshot", auth, request?.WarehouseId, cancellationToken: cancellationToken));
    }

    [HttpPost("export-gl-journal")]
    [Authorize]
    public async Task<IActionResult> ExportGlJournal([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExportGlJournalRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.ExportGlJournalAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken), () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("ExportGlJournal", auth, request?.WarehouseId, cancellationToken: cancellationToken));
    }

    [HttpPost("compliance-evidence-bundle")]
    [Authorize]
    public async Task<IActionResult> ComplianceEvidenceBundle([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ComplianceEvidenceBundleRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.ComplianceEvidenceBundleAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken), () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("ComplianceEvidenceBundle", auth, request?.WarehouseId, cancellationToken: cancellationToken));
    }

    [HttpGet("cod-reconciliation")]
    [HttpGet("cod-shipping-reconciliation")]
    [HttpGet("cod-and-settlement-reconciliation")]
    [HttpGet("~/api/v1/finance/cod-reconciliation")]
    [HttpGet("~/api/finance/cod-reconciliation")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetCodAndSettlementReconciliation([FromQuery] int? warehouseId = null, [FromQuery] string? carrierCode = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.GetCodAndSettlementReconciliationAsync(warehouseId, carrierCode, _warehouseAuth, cancellationToken), () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("GetCodAndSettlementReconciliation", _warehouseAuth, warehouseId, carrierCode, cancellationToken: cancellationToken));

    [HttpPost("cod-reconciliation/verify")]
    [HttpPost("cod-shipping-reconciliation")]
    [HttpPost("cod-and-settlement-reconciliation/verify")]
    [HttpPost("reconcile-cod-settlement")]
    [HttpPost("cod-reconciliation/import")]
    [HttpPost("~/api/v1/finance/cod-reconciliation/import")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    [IdempotentCommand("Report.VerifyCodAndSettlementReconciliation")]
    public async Task<IActionResult> VerifyCodAndSettlementReconciliation([ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] VerifyCodSettlementRequest? request = null, [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] IFormFile? file = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        IReadOnlyList<CodSettlementItemRequestBase>? items = file is { Length: > 0 } ? await ReportsExcelHelper.ParseCodSettlementFileAsync(file, request?.CarrierCode, cancellationToken) : request?.Items?.Cast<CodSettlementItemRequestBase>().ToList();
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.VerifyCodAndSettlementReconciliationAsync(request?.WarehouseId, request?.CarrierCode, request?.ReconcileMode, items, _warehouseAuth, cancellationToken), () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("VerifyCodAndSettlementReconciliation", _warehouseAuth, request?.WarehouseId, request?.CarrierCode, codItems: items, cancellationToken: cancellationToken));
    }

    [HttpGet("owner-gross-profit")]
    [HttpGet("owner-pnl-summary")]
    [HttpGet("gross-profit-by-sku")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOwnerGrossProfit([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOwnerGrossProfitAsync(warehouseId, CanReadInventoryCost(), _warehouseAuth, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("GetOwnerGrossProfit", _warehouseAuth, warehouseId, canReadCost: CanReadInventoryCost(), cancellationToken: cancellationToken));

    [HttpGet("3pl-storage-snapshot")]
    [HttpPost("3pl-storage-snapshot")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetOrCaptureThreePlStorageSnapshot([FromQuery] int? warehouseId = null, [FromQuery] int? ownerId = null, CancellationToken cancellationToken = default)
    {
        var isPost = HttpMethods.IsPost(Request?.Method ?? "GET");
        if (isPost && User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOrCaptureThreePlStorageSnapshotAsync(warehouseId, ownerId, isPost, GetActorUserId(), _warehouseAuth, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("GetOrCaptureThreePlStorageSnapshot", _warehouseAuth, warehouseId, isPost: isPost, actorUserId: GetActorUserId(), cancellationToken: cancellationToken));
    }

    [HttpGet("onboarding-wizard/status")]
    [HttpPost("onboarding-wizard/quick-start")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOnboardingWizardStatus(CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOnboardingWizardStatusAsync(cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("GetOnboardingWizardStatus", _warehouseAuth, cancellationToken: cancellationToken));

    [HttpGet("warehouse-tasks")]
    [Authorize]
    public Task<IActionResult> GetWarehouseTasks([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetWarehouseTasks(_reportOps, _warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetWarehouseTasks(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetWarehouseTasks(_reportOps, warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetWarehouseTasks(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetWarehouseTasksAsync(warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("WarehouseTasks", warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken: cancellationToken));

    [HttpGet("operational-exceptions")]
    [Authorize]
    public Task<IActionResult> GetOperationalExceptions([FromQuery] int? warehouseId = null, [FromQuery] bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        GetOperationalExceptions(_reportOps, _warehouseAuth, warehouseId, includeAcknowledged, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalExceptions(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        GetOperationalExceptions(_reportOps, warehouseAuth, warehouseId, includeAcknowledged, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalExceptions(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetOperationalExceptionsAsync(warehouseAuth ?? _warehouseAuth, warehouseId, includeAcknowledged, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("OperationalExceptions", warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken: cancellationToken));

    [HttpPost("operational-dashboard/exceptions/{code}/acknowledge")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("OperationalException.Acknowledge")]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode([FromRoute] string code, [FromBody] AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalDashboardExceptionByCode(code, _reportOps, _warehouseAuth, body, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode(string code, IWarehouseAuthorizationService warehouseAuth, AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalDashboardExceptionByCode(code, _reportOps, warehouseAuth, body, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode(string code, IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default)
    {
        var req = new AcknowledgeOperationalExceptionDto(!string.IsNullOrWhiteSpace(body?.ExceptionCode) ? body!.ExceptionCode : code, body?.WarehouseId, body?.ResolutionNote, body?.ExceptionType);
        return AcknowledgeOperationalException(req, reportOps, warehouseAuth, cancellationToken);
    }

    [HttpPost("operational-exceptions/acknowledge")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("OperationalException.Acknowledge")]
    public Task<IActionResult> AcknowledgeOperationalException([FromBody] AcknowledgeOperationalExceptionDto request, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalException(request, _reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalException(AcknowledgeOperationalExceptionDto request, IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalException(request, _reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public async Task<IActionResult> AcknowledgeOperationalException(AcknowledgeOperationalExceptionDto request, IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.AcknowledgeOperationalExceptionAsync(request, auth, GetActorUserId(), cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("AcknowledgeOperationalException", auth, actorUserId: GetActorUserId(), ackRequest: request, cancellationToken: cancellationToken));
    }

    [HttpGet("global-search")]
    [Authorize]
    public Task<IActionResult> GlobalSearch([FromQuery] string? q = null, CancellationToken cancellationToken = default) =>
        GlobalSearch(_reportOps, _warehouseAuth, q, cancellationToken);

    [NonAction]
    public Task<IActionResult> GlobalSearch(IWarehouseAuthorizationService warehouseAuth, string? q = null, CancellationToken cancellationToken = default) =>
        GlobalSearch(_reportOps, warehouseAuth, q, cancellationToken);

    [NonAction]
    public Task<IActionResult> GlobalSearch(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, string? q = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GlobalSearchAsync(warehouseAuth ?? _warehouseAuth, q, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("GlobalSearch", warehouseAuth ?? _warehouseAuth, cancellationToken: cancellationToken));

    [HttpGet("analytics-summary")]
    [Authorize]
    public Task<IActionResult> GetAnalyticsSummary([FromQuery] int? warehouseId = null, [FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        GetAnalyticsSummary(_reportOps, _warehouseAuth, warehouseId, days, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetAnalyticsSummary(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, int days = 30, CancellationToken cancellationToken = default) =>
        GetAnalyticsSummary(_reportOps, warehouseAuth, warehouseId, days, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetAnalyticsSummary(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, int days = 30, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetAnalyticsSummaryAsync(warehouseAuth ?? _warehouseAuth, warehouseId, days, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("AnalyticsSummary", warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken: cancellationToken));

    [HttpGet("expiry-aging")]
    [Authorize]
    public Task<IActionResult> GetExpiryAgingReport([FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] int nearExpiryDays = 30, [FromQuery] bool onlyLots = false, CancellationToken cancellationToken = default) =>
        GetExpiryAgingReport(_reportOps, _warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetExpiryAgingReport(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, int? productId = null, int nearExpiryDays = 30, bool onlyLots = false, CancellationToken cancellationToken = default) =>
        GetExpiryAgingReport(_reportOps, warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetExpiryAgingReport(IReportOperationsService? reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, int? productId = null, int nearExpiryDays = 30, bool onlyLots = false, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetExpiryAgingReportAsync(warehouseAuth ?? _warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken),
            () => ReportOperationsTextAndFallbackHelper.FallbackWhenNoOpsAsync("ExpiryAging", warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken: cancellationToken));

    [HttpGet("inventory")]
    public async Task<IActionResult> GetInventoryReport([FromQuery] DateTime? asOfDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default) =>
        Ok(await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken));

    [HttpGet("inventory/export")]
    public async Task<IActionResult> ExportInventoryReport([FromQuery] DateTime? asOfDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default)
    {
        var report = await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken);
        return File(ReportsExcelHelper.BuildInventoryReportExcel(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoTonKho_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpGet("inventory-in-out-stock")]
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    public Task<IActionResult> GetInventoryInOutReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default) =>
        GetInventoryInOutStock(fromDate, toDate, warehouseId, productId, _reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public async Task<IActionResult> GetInventoryInOutStock(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, int? productId = null, IReportOperationsService? reportOps = null, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var ops = reportOps ?? _reportOps;
            var report = ops is not null
                ? await ops.ComputeInventoryInOutStockAsync(warehouseAuth ?? _warehouseAuth, fromDate, toDate, warehouseId, productId, cancellationToken)
                : await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
            return Ok(report);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("inventory-in-out-stock/export")]
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    public async Task<IActionResult> ExportInventoryInOutReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = _reportOps is not null
                ? await _reportOps.ComputeInventoryInOutStockAsync(_warehouseAuth, fromDate, toDate, warehouseId, productId, cancellationToken)
                : await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
            return File(ReportsExcelHelper.BuildInventoryInOutReportExcel(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoXuatNhapTon_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public sealed record SyncBiSnapshotRequest(int? WarehouseId = null) : SyncBiSnapshotRequestBase(WarehouseId);
    public sealed record ExportGlJournalRequest(int? WarehouseId = null) : ExportGlJournalRequestBase(WarehouseId);
    public sealed record ComplianceEvidenceBundleRequest(int? WarehouseId = null) : ComplianceEvidenceBundleRequestBase(WarehouseId);
    public sealed record CodSettlementItemRequest(string? TrackingNumber = null, string? CarrierCode = null, int? ExportReceiptId = null, decimal ExpectedCodAmount = 0m, decimal ActualCodCollected = 0m, decimal EstimatedShippingFee = 0m, decimal ActualShippingFee = 0m, decimal PlatformCommissionFee = 0m, decimal ReturnFee = 0m) : CodSettlementItemRequestBase(TrackingNumber, CarrierCode, ExportReceiptId, ExpectedCodAmount, ActualCodCollected, EstimatedShippingFee, ActualShippingFee, PlatformCommissionFee, ReturnFee);
    public sealed record VerifyCodSettlementRequest(int? WarehouseId = null, string? CarrierCode = null, List<CodSettlementItemRequest>? Items = null, string? ReconcileMode = null);
    public sealed record AcknowledgeOperationalExceptionDto(string ExceptionCode, int? WarehouseId, string? ResolutionNote, string? ExceptionType = null) : AcknowledgeOperationalExceptionDtoBase(ExceptionCode, WarehouseId, ResolutionNote, ExceptionType);
    public sealed record CreateRecallCampaignDto(string? CampaignCode = null, int? ProductId = null, string? LotNumber = null, int? WarehouseId = null, string? Reason = null) : CreateRecallCampaignDtoBase(CampaignCode, ProductId, LotNumber, WarehouseId, Reason);
    public sealed record ExecuteRecallIsolationDto(string? CampaignCode = null, int? WarehouseId = null, string? QuarantineLocationCode = null, string? Note = null) : ExecuteRecallIsolationDtoBase(CampaignCode, WarehouseId, QuarantineLocationCode, Note);
    public sealed record CreateCustomReportTemplateDto(string? TemplateCode = null, string? TemplateName = null, string? DatasetType = null, int? WarehouseId = null) : CreateCustomReportTemplateDtoBase(TemplateCode, TemplateName, DatasetType, WarehouseId);
    public sealed record ExecuteCustomReportRunDto(string? TemplateCode = null, int? WarehouseId = null, string? OutputFormat = null) : ExecuteCustomReportRunDtoBase(TemplateCode, WarehouseId, OutputFormat);
    public sealed record UpsertLaborStandardDto(string? TaskType = null, decimal TargetUnitsPerHour = 0m, int? WarehouseId = null) : UpsertLaborStandardDtoBase(TaskType, TargetUnitsPerHour, WarehouseId);
    public sealed record UpsertThreePlContractRuleDto(int? OwnerId = null, int? WarehouseId = null, decimal DailyPalletRateVnd = 18000m, decimal DailyCbmRateVnd = 15000m, decimal DailyBinRateVnd = 5000m) : UpsertThreePlContractRuleDtoBase(OwnerId, WarehouseId, DailyPalletRateVnd, DailyCbmRateVnd, DailyBinRateVnd);
    public sealed record GenerateThreePlInvoiceDto(int? OwnerId = null, int? WarehouseId = null, string? BillingPeriod = null) : GenerateThreePlInvoiceDtoBase(OwnerId, WarehouseId, BillingPeriod);
    public sealed record ReconcileCodRemittanceBatchDto(int? WarehouseId = null, string? CarrierCode = null, string? BatchCode = null) : ReconcileCodRemittanceBatchDtoBase(WarehouseId, CarrierCode, BatchCode);
    public sealed record ConfirmInterWarehouseReceiptDto(int? TransferId = null, int? DestinationWarehouseId = null, string? Note = null) : ConfirmInterWarehouseReceiptDtoBase(TransferId, DestinationWarehouseId, Note);
}
