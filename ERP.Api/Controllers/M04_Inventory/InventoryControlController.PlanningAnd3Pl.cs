using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed partial class InventoryControlController
{
    [HttpPost("replenishment/break-bulk")]
    [HttpPost("/api/v1/inventory-control/replenishment/break-bulk")]
    [HttpPost("/api/inventory-control/replenishment/break-bulk")]
    [IdempotentCommand("Inventory.BreakBulkReplenishment")]
    [PermissionAuthorize(AppPermissions.ReplenishmentExecute, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> ExecuteBreakBulkReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteBreakBulkReplenishmentInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ReplenishmentExecute, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        return this.ToActionResult(await effectiveOps.ExecuteBreakBulkReplenishmentAsync(request, GetActorUserId(), effectiveAuth, cancellationToken));
    }

    [HttpGet("advanced-planning-suite")]
    [HttpGet("/api/v1/inventory-control/advanced-planning-suite")]
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
        return this.ToActionResult(await effectiveOps.AdvancedPlanningSuiteAsync(warehouseId, effectiveAuth, cancellationToken));
    }

    public sealed record WhatIfSimulationRequest(
        int? WarehouseId = null,
        decimal DemandMultiplier = 1.5m,
        int HorizonDays = 14,
        decimal? DemandShockPct = null,
        int? LeadTimeDelayDays = null) : WhatIfSimulationInput(
            WarehouseId, DemandMultiplier, HorizonDays, DemandShockPct, LeadTimeDelayDays);

    [HttpPost("what-if-simulation")]
    [HttpPost("/api/v1/inventory-control/what-if-simulation")]
    [HttpPost("/api/inventory-control/what-if-simulation")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> RunWhatIfSimulation(
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WhatIfSimulationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        return this.ToActionResult(await effectiveOps.RunWhatIfSimulationAsync(request, GetActorUserId(), effectiveAuth, cancellationToken));
    }

    public sealed record EvaluateDecisionPolicyRequest(int? WarehouseId = null)
        : EvaluateDecisionPolicyInput(WarehouseId);

    [HttpPost("evaluate-decision-policy-governance")]
    [HttpPost("audit-decision-policies")]
    [HttpPost("/api/v1/inventory-control/audit-decision-policies")]
    [HttpPost("/api/inventory-control/audit-decision-policies")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> EvaluateDecisionPolicyGovernance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateDecisionPolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        return this.ToActionResult(await _controlOps.EvaluateDecisionPolicyGovernanceAsync(request, GetActorUserId(), _warehouseAccess, cancellationToken));
    }

    [HttpGet("slotting-interleaving/evaluate")]
    [HttpGet("/api/v1/inventory-control/slotting-interleaving/evaluate")]
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
        return this.ToActionResult(await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            new EvaluateAbcSlottingInput(warehouseId, lookbackDays, null, PersistAuditAndOutbox: false),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken));
    }

    [HttpPost("slotting-interleaving/evaluate")]
    [HttpPost("/api/v1/inventory-control/slotting-interleaving/evaluate")]
    [HttpPost("/api/inventory-control/slotting-interleaving/evaluate")]
    [HttpPost("/api/v1/putaway-tasks/abc-slotting-interleaving")]
    [HttpPost("/api/putaway-tasks/abc-slotting-interleaving")]
    [IdempotentCommand("Inventory.AbcSlottingInterleaving")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> EvaluateAbcSlottingAndTaskInterleaving(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAbcSlottingInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        return this.ToActionResult(await effectiveOps.EvaluateAbcSlottingAndTaskInterleavingAsync(
            request ?? new EvaluateAbcSlottingInput(),
            GetActorUserId(),
            effectiveAuth,
            cancellationToken));
    }

    private int? ResolveServerBoundOwnerId()
    {
        var raw = User?.FindFirst("owner_id")?.Value
            ?? User?.FindFirst("OwnerId")?.Value
            ?? User?.FindFirst("depositor_owner_id")?.Value;
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : null;
    }

    [HttpGet("3pl-depositor-portal")]
    [HttpGet("/api/v1/inventory-control/3pl-depositor-portal")]
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
        var boundOwnerId = ResolveServerBoundOwnerId();
#pragma warning disable CS0618
        var input = new ThreePlDepositorPortalInput(
            OwnerId: ownerId,
            RequestedOwnerId: requestedOwnerId,
            AuthenticatedOwnerId: authenticatedOwnerId,
            WarehouseId: warehouseId,
            BillingPeriod: billingPeriod,
            PersistSnapshotAndStatement: false);
#pragma warning restore CS0618
        return this.ToActionResult(await effectiveOps.GetThreePlDepositorPortalAsync(
            input,
            GetActorUserId(),
            effectiveAuth,
            boundOwnerId,
            cancellationToken));
    }

    [HttpPost("3pl-depositor-portal")]
    [HttpPost("/api/v1/inventory-control/3pl-depositor-portal")]
    [HttpPost("/api/inventory-control/3pl-depositor-portal")]
    [HttpPost("/api/v1/business-partners/3pl-depositor-portal")]
    [HttpPost("/api/business-partners/3pl-depositor-portal")]
    [IdempotentCommand("ThreePlDepositorPortal.IssueBillingStatement")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> ExecuteThreePlDepositorPortal(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ThreePlDepositorPortalInput? request = null,
        [FromServices] IInventoryControlOperationsService? opsSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.InventoryMovementCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var effectiveOps = opsSvc ?? _controlOps;
        var effectiveAuth = warehouseAuth ?? _warehouseAccess;
        var boundOwnerId = ResolveServerBoundOwnerId();
        var effectiveRequest = request ?? new ThreePlDepositorPortalInput(PersistSnapshotAndStatement: true);
        return this.ToActionResult(await effectiveOps.GetThreePlDepositorPortalAsync(
            effectiveRequest,
            GetActorUserId(),
            effectiveAuth,
            boundOwnerId,
            cancellationToken));
    }
}
