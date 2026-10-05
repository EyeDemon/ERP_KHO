using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/picking-tasks")]
[Authorize]
public sealed class PickingTasksController(IPickingService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.PickingRead)]
    public async Task<ActionResult<IReadOnlyList<PickingTaskListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

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
