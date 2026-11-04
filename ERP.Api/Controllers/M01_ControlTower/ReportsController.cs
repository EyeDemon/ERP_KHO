using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[Authorize]
[PermissionAuthorize(AppPermissions.InventoryRead)]
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/reports")]
public partial class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IWarehouseAuthorizationService _warehouseAuth;
    private readonly IReportOperationsService _reportOps;
    private readonly ICurrentUser _currentUser;

    public ReportsController(
        IReportService reportService,
        IReportOperationsService reportOps,
        IWarehouseAuthorizationService warehouseAuth,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(reportService);
        ArgumentNullException.ThrowIfNull(reportOps);
        ArgumentNullException.ThrowIfNull(warehouseAuth);
        ArgumentNullException.ThrowIfNull(currentUser);
        _reportService = reportService;
        _reportOps = reportOps;
        _warehouseAuth = warehouseAuth;
        _currentUser = currentUser;
    }

    private bool CanReadInventoryCost() =>
        (HttpContext is null && !_currentUser.IsAuthenticated && _currentUser.UserId == 0 && string.IsNullOrWhiteSpace(_currentUser.Role)) ||
        (HttpContext is null && _currentUser.IsGlobalAdmin) ||
        ReportOperationsTextAndFallbackHelper.CanReadInventoryCost(_currentUser, User);
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
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOperationalDashboard(CancellationToken cancellationToken = default) =>
        GetOperationalDashboard(_reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalDashboard(IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        GetOperationalDashboard(_reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalDashboard(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetOperationalDashboardAsync(warehouseAuth ?? _warehouseAuth, cancellationToken));

    [HttpGet("multi-warehouse-control-tower")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetMultiWarehouseControlTower(CancellationToken cancellationToken = default) =>
        GetMultiWarehouseControlTower(_reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetMultiWarehouseControlTower(IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        GetMultiWarehouseControlTower(_reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetMultiWarehouseControlTower(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetMultiWarehouseControlTowerAsync(warehouseAuth ?? _warehouseAuth, cancellationToken));

    [HttpGet("productivity-and-bi-snapshot")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetProductivityAndBiSnapshot([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetProductivityAndBiSnapshot(_reportOps, _warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetProductivityAndBiSnapshot(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetProductivityAndBiSnapshot(_reportOps, warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetProductivityAndBiSnapshot(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetProductivityAndBiSnapshotAsync(warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken));

    [HttpPost("sync-bi-snapshot")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> SyncBiSnapshot([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncBiSnapshotRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReportExport, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.SyncBiSnapshotAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken));
    }

    [HttpPost("export-gl-journal")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.ExportReceiptUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> ExportGlJournal([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExportGlJournalRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReportExport, AppPermissions.ExportReceiptUpdate, AppPermissions.InventoryMovementCreate)
            || !CanReadInventoryCost())
        {
            return Forbid();
        }
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.ExportGlJournalAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken));
    }

    [HttpPost("compliance-evidence-bundle")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> ComplianceEvidenceBundle([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ComplianceEvidenceBundleRequest? request = null, [FromServices] IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReportExport, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.ComplianceEvidenceBundleAsync(request?.WarehouseId, auth, GetActorUserId(), cancellationToken));
    }

    [HttpGet("cod-reconciliation")]
    [HttpGet("cod-shipping-reconciliation")]
    [HttpGet("cod-and-settlement-reconciliation")]
    [HttpGet("~/api/v1/finance/cod-reconciliation")]
    [HttpGet("~/api/finance/cod-reconciliation")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetCodAndSettlementReconciliation([FromQuery] int? warehouseId = null, [FromQuery] string? carrierCode = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.GetCodAndSettlementReconciliationAsync(warehouseId, carrierCode, _warehouseAuth, cancellationToken));

    [HttpPost("cod-reconciliation/verify")]
    [HttpPost("cod-shipping-reconciliation")]
    [HttpPost("cod-and-settlement-reconciliation/verify")]
    [HttpPost("reconcile-cod-settlement")]
    [HttpPost("cod-reconciliation/import")]
    [HttpPost("~/api/v1/finance/cod-reconciliation/verify")]
    [HttpPost("~/api/v1/finance/cod-reconciliation/import")]
    [HttpPost("~/api/finance/cod-reconciliation/verify")]
    [RequestSizeLimit(FileUploadSecurityValidator.MaxDocumentBytes)]
    [PermissionAuthorize(AppPermissions.CodReconciliationManage, AppPermissions.ExportReceiptUpdate, AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("Report.VerifyCodAndSettlementReconciliation")]
    public async Task<IActionResult> VerifyCodAndSettlementReconciliation([ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] VerifyCodSettlementRequest? request = null, [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] IFormFile? file = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.CodReconciliationManage, AppPermissions.ExportReceiptUpdate, AppPermissions.InventoryMovementCreate)) return Forbid();
        var fileValidation = FileUploadSecurityValidator.ValidateSpreadsheetOrCsvFile(file);
        if (!fileValidation.IsValid)
        {
            return StatusCode(fileValidation.StatusCode, new { code = fileValidation.ErrorCode, errorCode = fileValidation.ErrorCode, message = fileValidation.Message });
        }
        IReadOnlyList<CodSettlementItemRequestBase>? items = file is { Length: > 0 } ? await ReportsExcelHelper.ParseCodSettlementFileAsync(file, request?.CarrierCode, cancellationToken) : request?.Items?.Cast<CodSettlementItemRequestBase>().ToList();
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps, ops => ops.VerifyCodAndSettlementReconciliationAsync(request?.WarehouseId, request?.CarrierCode, request?.ReconcileMode, items, _warehouseAuth, cancellationToken));
    }

    [HttpGet("owner-gross-profit")]
    [HttpGet("owner-pnl-summary")]
    [HttpGet("gross-profit-by-sku")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOwnerGrossProfit([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOwnerGrossProfitAsync(warehouseId, CanReadInventoryCost(), _warehouseAuth, cancellationToken));

    [HttpGet("3pl-storage-snapshot")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetThreePlStorageSnapshot([FromQuery] int? warehouseId = null, [FromQuery] int? ownerId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOrCaptureThreePlStorageSnapshotAsync(warehouseId, ownerId, false, GetActorUserId(), _warehouseAuth, cancellationToken));

    [HttpPost("3pl-storage-snapshot")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> CaptureThreePlStorageSnapshot([FromQuery] int? warehouseId = null, [FromQuery] int? ownerId = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOrCaptureThreePlStorageSnapshotAsync(warehouseId, ownerId, true, GetActorUserId(), _warehouseAuth, cancellationToken));
    }

    [HttpGet("onboarding-wizard/status")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOnboardingWizardStatus(CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOnboardingWizardStatusAsync(cancellationToken));

    [HttpPost("onboarding-wizard/quick-start")]
    [PermissionAuthorize(AppPermissions.WarehouseManage, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    public async Task<IActionResult> QuickStartOnboardingWizard(CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.WarehouseManage, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetOnboardingWizardStatusAsync(cancellationToken));
    }
}
