using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
public sealed class PickingTasksController(IPickingService service) : ControllerBase
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
            .Select(g => new WaveGroupDto(
                $"WAVE-W{g.Key.WarehouseId}-P{g.Key.Priority}-{g.Key.PickingType}",
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
