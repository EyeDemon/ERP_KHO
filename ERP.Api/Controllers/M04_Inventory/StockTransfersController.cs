using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Route("api/v1/stock-transfers")]
[Authorize]
public sealed class StockTransfersController(
    IStockTransferService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> Get([FromQuery] StockTransferQueryDto query, CancellationToken cancellationToken) => Ok(await service.GetAsync(query, cancellationToken));

    [HttpGet("in-transit")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetInTransitInventory(
        [FromQuery] int? sourceWarehouseId = null,
        [FromQuery] int? destinationWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var allowedIds = warehouseAccess != null
            ? await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken)
            : Array.Empty<int>();
        var outcome = await govOps.GetInTransitInventoryAsync(
            allowedIds,
            sourceWarehouseId,
            destinationWarehouseId,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("return-reasons")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    public IActionResult ReturnReasons() => Ok(StockTransferReturnReasonCatalog.All);

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) => Ok(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [IdempotentCommand("StockTransfer.Create")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Create(CreateStockTransferDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IdempotentCommand("StockTransfer.Update")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Update(int id, UpdateStockTransferDto request, CancellationToken cancellationToken) { await service.UpdateAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/approve")]
    [IdempotentCommand("StockTransfer.Approve")]
    [Authorize(Policy = ApprovalPolicies.Checker)]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken) { await service.ApproveAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/dispatch")]
    [IdempotentCommand("StockTransfer.Dispatch")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Dispatch(int id, CancellationToken cancellationToken) { await service.DispatchAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/receive")]
    [IdempotentCommand("StockTransfer.Receive")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Receive(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken) { await service.ReceiveAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/return")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    [IdempotentCommand("StockTransfer.Return")]
    public async Task<IActionResult> Return(int id, ReturnStockTransferDto request, CancellationToken cancellationToken)
    { await service.ReturnAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/reverse-draft")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    [IdempotentCommand("StockTransfer.ReverseDraft")]
    public async Task<IActionResult> ReverseDraft(int id, CreateReverseStockTransferDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateReverseDraftAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/complete")]
    [IdempotentCommand("StockTransfer.Complete")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken) { await service.CompleteAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/cancel")]
    [IdempotentCommand("StockTransfer.Cancel")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) { await service.CancelAsync(id, cancellationToken); return NoContent(); }

    public sealed record SimulateNetworkRebalancingRequest(int? SourceWarehouseId = null, int? DestinationWarehouseId = null, int? WarehouseId = null);

    [HttpPost("simulate-network-rebalancing")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> SimulateNetworkRebalancing(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SimulateNetworkRebalancingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var sourceWarehouseId = request?.SourceWarehouseId;
        var destinationWarehouseId = request?.DestinationWarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            if (sourceWarehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(sourceWarehouseId.Value, cancellationToken);
            if (destinationWarehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(destinationWarehouseId.Value, cancellationToken);
            if (!warehouseId.HasValue && !sourceWarehouseId.HasValue && !destinationWarehouseId.HasValue)
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var outcome = await govOps.SimulateNetworkRebalancingAsync(
            warehouseId,
            sourceWarehouseId,
            destinationWarehouseId,
            accessibleIds,
            cancellationToken);
        return outcome.ToActionResult(this);
    }
}
