using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
[Route("api/v1/inventory")]
[Route("api/v1/inventory-control")]
public sealed class InventoryControlController : ControllerBase
{
    private readonly IInventoryLockService _lockService;
    private readonly IInventoryMovementService _movementService;
    private readonly IInventoryControlOperationsService _controlOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;

    [ActivatorUtilitiesConstructor]
    public InventoryControlController(
        IInventoryLockService lockService,
        IInventoryMovementService movementService,
        IInventoryControlOperationsService controlOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!)
    {
        _lockService = lockService;
        _movementService = movementService;
        _controlOps = controlOps;
        _warehouseAccess = warehouseAccess;
    }

    public InventoryControlController(
        IInventoryLockService lockService,
        IInventoryControlOperationsService controlOps = null!,
        IWarehouseAuthorizationService warehouseAccess = null!)
        : this(lockService, null!, controlOps, warehouseAccess)
    {
    }

    [HttpGet("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryLockDto>>> Locks(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _lockService.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("locks/{id:int}")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<InventoryLockDto>> Lock(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.GetAsync(id, cancellationToken));

    [HttpPost("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Create")]
    public async Task<ActionResult<InventoryLockDto>> CreateLock(
        CreateInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.CreateAsync(request, cancellationToken));

    [HttpPost("locks/{id:int}/release")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Release")]
    public async Task<ActionResult<InventoryLockDto>> ReleaseLock(
        int id,
        ReleaseInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await _lockService.ReleaseAsync(id, request, cancellationToken));

    [HttpPost("movements")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryMovement.Create")]
    public async Task<ActionResult<InventoryMoveResultDto>> Move(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken) =>
        Ok(await _movementService.MoveAsync(request, cancellationToken));

    [HttpGet("replenishment-suggestions")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ReplenishmentSuggestions(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.ReplenishmentSuggestionsAsync(warehouseId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record ExecuteReplenishmentRequest(
        int? WarehouseId = null,
        int? ProductId = null,
        string? ProductCode = null,
        int? FromLocationId = null,
        string? FromLocationCode = null,
        int? ToLocationId = null,
        string? ToLocationCode = null,
        decimal Quantity = 10m,
        bool ExecuteStockMutation = true,
        string? LotNumber = null,
        string? Note = null,
        int? SourceLocationId = null,
        string? SourceLocationCode = null,
        int? TargetLocationId = null,
        string? TargetLocationCode = null) : ExecuteReplenishmentInput(
            WarehouseId,
            ProductId,
            ProductCode,
            FromLocationId,
            FromLocationCode,
            ToLocationId,
            ToLocationCode,
            Quantity,
            ExecuteStockMutation,
            LotNumber,
            Note,
            SourceLocationId,
            SourceLocationCode,
            TargetLocationId,
            TargetLocationCode);

    [HttpPost("execute-replenishment")]
    [HttpPost("/api/inventory-control/execute-replenishment")]
    [IdempotentCommand("Inventory.ExecuteReplenishment")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteReplenishmentRequest? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.ExecuteReplenishmentAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("replenishment/break-bulk")]
    [HttpPost("/api/inventory-control/replenishment/break-bulk")]
    [IdempotentCommand("Inventory.BreakBulkReplenishment")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteBreakBulkReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteBreakBulkReplenishmentInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.ExecuteBreakBulkReplenishmentAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("advanced-planning-suite")]
    [HttpGet("/api/inventory-control/advanced-planning-suite")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> AdvancedPlanningSuite(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.AdvancedPlanningSuiteAsync(warehouseId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record WhatIfSimulationRequest(
        int? WarehouseId = null,
        decimal DemandMultiplier = 1.5m,
        int HorizonDays = 14,
        decimal? DemandShockPct = null,
        int? LeadTimeDelayDays = null) : WhatIfSimulationInput(
            WarehouseId,
            DemandMultiplier,
            HorizonDays,
            DemandShockPct,
            LeadTimeDelayDays);

    [HttpPost("what-if-simulation")]
    [HttpPost("/api/inventory-control/what-if-simulation")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> RunWhatIfSimulation(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WhatIfSimulationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var actorUserId = GetActorUserId();
        var outcome = await effectiveOps.RunWhatIfSimulationAsync(request, actorUserId, effectiveAuth, cancellationToken);
        return this.ToActionResult(outcome);
    }

    public sealed record EvaluateDecisionPolicyRequest(int? WarehouseId = null)
        : EvaluateDecisionPolicyInput(WarehouseId);

    [HttpPost("evaluate-decision-policy-governance")]
    [HttpPost("audit-decision-policies")]
    [HttpPost("/api/inventory-control/audit-decision-policies")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> EvaluateDecisionPolicyGovernance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateDecisionPolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var actorUserId = GetActorUserId();
        var outcome = await _controlOps.EvaluateDecisionPolicyGovernanceAsync(request, actorUserId, _warehouseAccess, cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("slotting-interleaving/evaluate")]
    [HttpGet("/api/inventory-control/slotting-interleaving/evaluate")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> PreviewAbcSlottingAndTaskInterleaving(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int lookbackDays = 90,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            new EvaluateAbcSlottingInput(warehouseId, lookbackDays, null, PersistAuditAndOutbox: false),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("slotting-interleaving/evaluate")]
    [HttpPost("/api/inventory-control/slotting-interleaving/evaluate")]
    [HttpPost("/api/v1/putaway-tasks/abc-slotting-interleaving")]
    [HttpPost("/api/putaway-tasks/abc-slotting-interleaving")]
    [IdempotentCommand("Inventory.AbcSlottingInterleaving")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> EvaluateAbcSlottingAndTaskInterleaving(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAbcSlottingInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            request ?? new EvaluateAbcSlottingInput(),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpGet("3pl-depositor-portal")]
    [HttpGet("/api/inventory-control/3pl-depositor-portal")]
    [HttpGet("/api/v1/business-partners/3pl-depositor-portal")]
    [HttpGet("/api/business-partners/3pl-depositor-portal")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetThreePlDepositorPortal(
        [FromQuery] int? ownerId = null,
        [FromQuery] int? requestedOwnerId = null,
        [FromQuery] int? authenticatedOwnerId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? billingPeriod = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.GetThreePlDepositorPortalAsync(
            new ThreePlDepositorPortalInput(
                OwnerId: ownerId,
                RequestedOwnerId: requestedOwnerId,
                AuthenticatedOwnerId: authenticatedOwnerId,
                WarehouseId: warehouseId,
                BillingPeriod: billingPeriod,
                PersistSnapshotAndStatement: false),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    [HttpPost("3pl-depositor-portal")]
    [HttpPost("/api/inventory-control/3pl-depositor-portal")]
    [HttpPost("/api/v1/business-partners/3pl-depositor-portal")]
    [HttpPost("/api/business-partners/3pl-depositor-portal")]
    [IdempotentCommand("ThreePlDepositorPortal.IssueBillingStatement")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteThreePlDepositorPortal(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ThreePlDepositorPortalInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var outcome = await effectiveOps.GetThreePlDepositorPortalAsync(
            request ?? new ThreePlDepositorPortalInput(PersistSnapshotAndStatement: true),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken);
        return this.ToActionResult(outcome);
    }

    private int? GetActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
