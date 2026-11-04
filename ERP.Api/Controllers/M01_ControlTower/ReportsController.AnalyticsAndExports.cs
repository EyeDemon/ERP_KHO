using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public partial class ReportsController
{
    [HttpGet("warehouse-tasks")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetWarehouseTasks([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetWarehouseTasks(_reportOps, _warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetWarehouseTasks(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        GetWarehouseTasks(_reportOps, warehouseAuth, warehouseId, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetWarehouseTasks(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetWarehouseTasksAsync(warehouseAuth ?? _warehouseAuth, warehouseId, cancellationToken));

    [HttpGet("operational-exceptions")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetOperationalExceptions([FromQuery] int? warehouseId = null, [FromQuery] bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        GetOperationalExceptions(_reportOps, _warehouseAuth, warehouseId, includeAcknowledged, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalExceptions(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        GetOperationalExceptions(_reportOps, warehouseAuth, warehouseId, includeAcknowledged, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetOperationalExceptions(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, bool includeAcknowledged = false, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetOperationalExceptionsAsync(warehouseAuth ?? _warehouseAuth, warehouseId, includeAcknowledged, cancellationToken));

    [HttpPost("operational-dashboard/exceptions/{code}/acknowledge")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    [IdempotentCommand("OperationalException.Acknowledge")]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode([FromRoute] string code, [FromBody] AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalDashboardExceptionByCode(code, _reportOps, _warehouseAuth, body, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode(string code, IWarehouseAuthorizationService warehouseAuth, AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalDashboardExceptionByCode(code, _reportOps, warehouseAuth, body, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalDashboardExceptionByCode(string code, IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, AcknowledgeOperationalExceptionDto? body = null, CancellationToken cancellationToken = default)
    {
        var req = new AcknowledgeOperationalExceptionDto(!string.IsNullOrWhiteSpace(body?.ExceptionCode) ? body!.ExceptionCode : code, body?.WarehouseId, body?.ResolutionNote, body?.ExceptionType);
        return AcknowledgeOperationalException(req, reportOps, warehouseAuth, cancellationToken);
    }

    [HttpPost("operational-exceptions/acknowledge")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)]
    [IdempotentCommand("OperationalException.Acknowledge")]
    public Task<IActionResult> AcknowledgeOperationalException([FromBody] AcknowledgeOperationalExceptionDto request, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalException(request, _reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public Task<IActionResult> AcknowledgeOperationalException(AcknowledgeOperationalExceptionDto request, IWarehouseAuthorizationService warehouseAuth, CancellationToken cancellationToken = default) =>
        AcknowledgeOperationalException(request, _reportOps, warehouseAuth, cancellationToken);

    [NonAction]
    public async Task<IActionResult> AcknowledgeOperationalException(AcknowledgeOperationalExceptionDto request, IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryWrite)) return Forbid();
        var auth = warehouseAuth ?? _warehouseAuth;
        return await ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.AcknowledgeOperationalExceptionAsync(request, auth, GetActorUserId(), cancellationToken));
    }

    [HttpGet("global-search")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GlobalSearch([FromQuery] string? q = null, CancellationToken cancellationToken = default) =>
        GlobalSearch(_reportOps, _warehouseAuth, q, cancellationToken);

    [NonAction]
    public Task<IActionResult> GlobalSearch(IWarehouseAuthorizationService warehouseAuth, string? q = null, CancellationToken cancellationToken = default) =>
        GlobalSearch(_reportOps, warehouseAuth, q, cancellationToken);

    [NonAction]
    public Task<IActionResult> GlobalSearch(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, string? q = null, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GlobalSearchAsync(warehouseAuth ?? _warehouseAuth, q, cancellationToken));

    [HttpGet("analytics-summary")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetAnalyticsSummary([FromQuery] int? warehouseId = null, [FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        GetAnalyticsSummary(_reportOps, _warehouseAuth, warehouseId, days, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetAnalyticsSummary(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, int days = 30, CancellationToken cancellationToken = default) =>
        GetAnalyticsSummary(_reportOps, warehouseAuth, warehouseId, days, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetAnalyticsSummary(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, int days = 30, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetAnalyticsSummaryAsync(warehouseAuth ?? _warehouseAuth, warehouseId, days, cancellationToken));

    [HttpGet("expiry-aging")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetExpiryAgingReport([FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] int nearExpiryDays = 30, [FromQuery] bool onlyLots = false, CancellationToken cancellationToken = default) =>
        GetExpiryAgingReport(_reportOps, _warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetExpiryAgingReport(IWarehouseAuthorizationService warehouseAuth, int? warehouseId = null, int? productId = null, int nearExpiryDays = 30, bool onlyLots = false, CancellationToken cancellationToken = default) =>
        GetExpiryAgingReport(_reportOps, warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken);

    [NonAction]
    public Task<IActionResult> GetExpiryAgingReport(IReportOperationsService reportOps, IWarehouseAuthorizationService? warehouseAuth = null, int? warehouseId = null, int? productId = null, int nearExpiryDays = 30, bool onlyLots = false, CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, reportOps ?? _reportOps,
            ops => ops.GetExpiryAgingReportAsync(warehouseAuth ?? _warehouseAuth, warehouseId, productId, nearExpiryDays, onlyLots, cancellationToken));

    [HttpGet("stock-card")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetStockCard(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? lotId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default) =>
        ReportsExcelHelper.ExecuteReportOpAsync(this, _reportOps,
            ops => ops.GetStockCardS12DnAsync(warehouseId, productId, lotId, fromDate, toDate, _warehouseAuth, cancellationToken));

    [HttpGet("stock-card/export")]
    [HttpGet("stock-card/export-excel")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExportStockCard(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? lotId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _reportOps.GetStockCardS12DnAsync(warehouseId, productId, lotId, fromDate, toDate, _warehouseAuth, cancellationToken);
        if (outcome.StatusCode != StatusCodes.Status200OK || outcome.Payload is not StockCardS12DnReportDto card)
        {
            return this.ToActionResult(outcome);
        }

        var bytes = ReportsExcelHelper.BuildStockCardS12DnExcel(card);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"TheKho_S12DN_WH{card.WarehouseId}_SP{card.ProductId}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpGet("inventory")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetInventoryReport([FromQuery] DateTime? asOfDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default) =>
        Ok(await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken));

    [HttpGet("inventory/export")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExportInventoryReport([FromQuery] DateTime? asOfDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default)
    {
        var report = await _reportService.GetInventoryReportAsync(asOfDate, warehouseId, productId, cancellationToken);
        return File(ReportsExcelHelper.BuildInventoryReportExcel(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoTonKho_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpGet("inventory-in-out-stock")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public Task<IActionResult> GetInventoryInOutReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default) =>
        GetInventoryInOutStock(fromDate, toDate, warehouseId, productId, _reportOps, _warehouseAuth, cancellationToken);

    [NonAction]
    public async Task<IActionResult> GetInventoryInOutStock(DateTime? fromDate = null, DateTime? toDate = null, int? warehouseId = null, int? productId = null, IReportOperationsService? reportOps = null, IWarehouseAuthorizationService? warehouseAuth = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var ops = reportOps ?? _reportOps;
            var report = await ops.ComputeInventoryInOutStockAsync(warehouseAuth ?? _warehouseAuth, fromDate, toDate, warehouseId, productId, cancellationToken)
                ?? await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
            return Ok(report);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("inventory-in-out-stock/export")]
    [PermissionAuthorize(AppPermissions.ReportExport, AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExportInventoryInOutReport([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int? warehouseId, [FromQuery] int? productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportOps.ComputeInventoryInOutStockAsync(_warehouseAuth, fromDate, toDate, warehouseId, productId, cancellationToken)
                ?? await _reportService.GetInventoryInOutReportAsync(fromDate, toDate, warehouseId, productId, cancellationToken);
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
    public sealed record CodSettlementItemRequest(string? TrackingNumber = null, string? CarrierCode = null, int? ExportReceiptId = null, decimal ExpectedCodAmount = 0m, decimal ActualCodCollected = 0m, decimal EstimatedShippingFee = 0m, decimal ActualShippingFee = 0m, decimal PlatformCommissionFee = 0m, decimal ReturnFee = 0m, string? TaxpayerType = null) : CodSettlementItemRequestBase(TrackingNumber, CarrierCode, ExportReceiptId, ExpectedCodAmount, ActualCodCollected, EstimatedShippingFee, ActualShippingFee, PlatformCommissionFee, ReturnFee, TaxpayerType);
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
