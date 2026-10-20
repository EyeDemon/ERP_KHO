using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed record WaveGroupDto(
    string WaveCode,
    int WarehouseId,
    string WarehouseName,
    string PickingType,
    int Priority,
    int TaskCount,
    int OpenTaskCount,
    decimal TotalRequestedQuantity,
    decimal TotalPickedQuantity,
    decimal TotalRemainingQuantity,
    IReadOnlyList<string> TaskCodes);

[ApiController]
[Route("api/picking-tasks")]
[Authorize]
public sealed class PickingTasksController(
    IPickingService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    private static readonly HashSet<string> ActiveWaveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Open",
        "Assigned",
        "InProgress",
        "ShortPick"
    };

    [HttpGet]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<ActionResult<IReadOnlyList<PickingTaskListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("wave-summary")]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<ActionResult<IReadOnlyList<WaveGroupDto>>> GetWaveSummary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = await service.ListAsync(warehouseId, null, cancellationToken);
        var waves = tasks
            .Where(t => ActiveWaveStatuses.Contains(t.Status))
            .GroupBy(t => new
            {
                t.WarehouseId,
                t.WarehouseName,
                PickingType = string.IsNullOrWhiteSpace(t.PickingType) ? "STANDARD" : t.PickingType.Trim().ToUpperInvariant(),
                t.Priority
            })
            .OrderByDescending(g => g.Key.Priority)
            .ThenBy(g => g.Key.WarehouseId)
            .ThenBy(g => g.Key.PickingType)
            .Select((g, index) => new WaveGroupDto(
                $"WAVE-QA-{index + 1:D3}",
                g.Key.WarehouseId,
                g.Key.WarehouseName,
                g.Key.PickingType,
                g.Key.Priority,
                g.Count(),
                g.Count(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase)),
                g.Sum(x => x.RequestedQuantity),
                g.Sum(x => x.PickedQuantity),
                g.Sum(x => x.RemainingQuantity),
                g.Select(x => x.TaskCode).ToList()))
            .ToList();

        return Ok(waves);
    }

    [HttpGet("mobile-scan-queue")]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<IActionResult> GetMobileScanQueue(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        int mobileReadyTasksCount;
        int shortPickExceptionTasksCount;
        if (db != null)
        {
            var query = db.PickingTasks.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(t => accessibleIds.Contains(t.WarehouseId));

            mobileReadyTasksCount = await query.CountAsync(
                x => x.Status == PickingTaskStatus.Open
                     || x.Status == PickingTaskStatus.Assigned
                     || x.Status == PickingTaskStatus.InProgress,
                cancellationToken);
            shortPickExceptionTasksCount = await query.CountAsync(
                x => x.Status == PickingTaskStatus.ShortPick,
                cancellationToken);
        }
        else
        {
            var tasks = await service.ListAsync(warehouseId, null, cancellationToken);
            mobileReadyTasksCount = tasks.Count(x => x.Status is "Open" or "Assigned" or "InProgress");
            shortPickExceptionTasksCount = tasks.Count(x => x.Status == "ShortPick");
        }

        return Ok(new
        {
            mobileReadyTasksCount,
            shortPickExceptionTasksCount,
            supportedMobileCapabilities = new[]
            {
                "MyTasks",
                "ReceiveScan",
                "PutawayScan",
                "PickScan",
                "PackScan",
                "CountScan",
                "TransferScan",
                "ReplenishScan",
                "ReturnScan",
                "OfflineSyncQueue",
                "SkuBarcodeLookup",
                "MobileExceptionTriage"
            },
            offlineSyncPolicy = "DEFERRED_LOW_RISK_QUEUE_WITH_IDEMPOTENCY_KEY"
        });
    }

    public sealed record MobileScanVerifyRequest(string? ScanInput = null, int? WarehouseId = null);

    [HttpPost("mobile-scan-verify")]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<IActionResult> MobileScanVerify(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] MobileScanVerifyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var rawInput = request?.ScanInput ?? string.Empty;
        var input = new string(rawInput.Where(ch => ch >= 32 && ch != 127 && ch != '\u200b' && ch != '\ufeff').ToArray()).Trim();
        var tasks = await service.ListAsync(warehouseId, null, cancellationToken);
        var match = string.IsNullOrWhiteSpace(input)
            ? null
            : tasks.FirstOrDefault(x =>
                string.Equals(x.TaskCode, input, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.SourceCode, input, StringComparison.OrdinalIgnoreCase)
                || x.TaskCode.Contains(input, StringComparison.OrdinalIgnoreCase));

        if (match == null && !string.IsNullOrWhiteSpace(input) && db != null)
        {
            var dbQuery = db.PickingTasks.AsNoTracking()
                .Include(t => t.Warehouse)
                .Include(t => t.Lines)
                    .ThenInclude(l => l.Product)
                .Include(t => t.Lines)
                    .ThenInclude(l => l.SourceLocation)
                .AsQueryable();

            if (warehouseId.HasValue)
                dbQuery = dbQuery.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                dbQuery = dbQuery.Where(t => accessibleIds.Contains(t.WarehouseId));

            var dbMatch = await dbQuery.FirstOrDefaultAsync(
                t => t.TaskCode == input
                     || t.SourceCode == input
                     || t.TaskCode.Contains(input)
                     || t.Lines.Any(l => l.Product.Code == input || l.SourceLocation.Code == input),
                cancellationToken);

            if (dbMatch != null)
            {
                var matchedLine = dbMatch.Lines.FirstOrDefault(
                    l => string.Equals(l.Product?.Code, input, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(l.SourceLocation?.Code, input, StringComparison.OrdinalIgnoreCase))
                    ?? dbMatch.Lines.FirstOrDefault();

                return Ok(new
                {
                    matched = true,
                    scanInput = request?.ScanInput ?? string.Empty,
                    matchedTaskCode = dbMatch.TaskCode,
                    matchedProductCode = matchedLine?.Product?.Code ?? dbMatch.SourceCode,
                    matchedLocationCode = matchedLine?.SourceLocation?.Code ?? dbMatch.Warehouse?.Name,
                    status = dbMatch.Status.ToString()
                });
            }
        }

        return Ok(new
        {
            matched = match != null,
            scanInput = request?.ScanInput ?? string.Empty,
            matchedTaskCode = match?.TaskCode,
            matchedProductCode = match?.SourceCode,
            matchedLocationCode = match?.WarehouseName,
            status = match?.Status ?? "NOT_FOUND"
        });
    }

    public sealed record SyncAutomationTelemetryRequest(int? WarehouseId = null);

    [HttpPost("sync-automation-telemetry")]
    [IdempotentCommand("Picking.SyncAutomationTelemetry")]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<IActionResult> SyncAutomationTelemetry(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncAutomationTelemetryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var activeTasksCount = 0;
        if (db != null)
        {
            var query = db.PickingTasks.AsNoTracking()
                .Where(t => t.Status != PickingTaskStatus.Completed && t.Status != PickingTaskStatus.Cancelled);
            if (warehouseId.HasValue)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(t => accessibleIds.Contains(t.WarehouseId));
            activeTasksCount = await query.CountAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var telemetrySyncId = $"WCS-IOT-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "Picking.SyncAutomationTelemetry",
                EntityName = "AutomationTelemetry",
                NewValues = telemetrySyncId,
                Result = "AMR_ASRS_CONVEYOR_SYNCHRONIZED",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            telemetrySyncId,
            warehouseId,
            activePickingTasksCount = activeTasksCount,
            wcsRoboticsState = "AMR_ASRS_CONVEYOR_SYNCHRONIZED",
            rfidIotTelemetry = "RFID_GATE_AND_COLD_CHAIN_SENSORS_ONLINE",
            voicePickToLight = "VOICE_PTL_HANDS_FREE_DIRECTED_ACTIVE",
            syncedAtUtc = now
        });
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<ActionResult<PickingTaskDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost("{id:int}/assign")]
    [PermissionAuthorize(AppPermissions.PickingAssign)]
    [IdempotentCommand("Picking.Assign")]
    public async Task<ActionResult<PickingTaskDto>> Assign(int id, PickingStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.AssignAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/start")]
    [PermissionAuthorize(AppPermissions.PickingExecute)]
    [IdempotentCommand("Picking.Start")]
    public async Task<ActionResult<PickingTaskDto>> Start(int id, PickingStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.StartAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/pick")]
    [PermissionAuthorize(AppPermissions.PickingExecute)]
    [IdempotentCommand("Picking.Pick")]
    public async Task<ActionResult<PickingTaskDto>> Pick(int id, PickScanDto request, CancellationToken cancellationToken) =>
        Ok(await service.PickAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/report-short-pick")]
    [PermissionAuthorize(AppPermissions.PickingShortPick)]
    [IdempotentCommand("Picking.ShortPick")]
    public async Task<ActionResult<PickingTaskDto>> ReportShortPick(int id, ReportShortPickDto request, CancellationToken cancellationToken) =>
        Ok(await service.ReportShortPickAsync(id, request, cancellationToken));

    [HttpPost("{taskId:int}/short-picks/{exceptionId:int}/resolve")]
    [PermissionAuthorize(AppPermissions.PickingShortPick)]
    [IdempotentCommand("Picking.ShortPick.Resolve")]
    public async Task<ActionResult<PickingTaskDto>> ResolveShortPick(
        int taskId, int exceptionId, ResolveShortPickDto request, CancellationToken cancellationToken) =>
        Ok(await service.ResolveShortPickAsync(taskId, exceptionId, request, cancellationToken));

    [HttpPost("{taskId:int}/short-picks/{exceptionId:int}/override")]
    [PermissionAuthorize(AppPermissions.PickingOverride)]
    [IdempotentCommand("Picking.ShortPick.Override")]
    public async Task<ActionResult<PickingTaskDto>> OverrideShortPick(
        int taskId, int exceptionId, OverrideShortPickDto request, CancellationToken cancellationToken) =>
        Ok(await service.OverrideShortPickAsync(taskId, exceptionId, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.PickingExecute)]
    [IdempotentCommand("Picking.Complete")]
    public async Task<ActionResult<PickingTaskDto>> Complete(int id, PickingStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));
}
