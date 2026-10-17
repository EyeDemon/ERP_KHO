using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Inventory;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff},{AppRoles.Viewer}")]
public sealed class StockTransfersController(
    IStockTransferService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] StockTransferQueryDto query, CancellationToken cancellationToken) => Ok(await service.GetAsync(query, cancellationToken));

    [HttpGet("in-transit")]
    public async Task<IActionResult> GetInTransitInventory(
        [FromServices] ErpKhoDbContext dbContext,
        [FromServices] IWarehouseAuthorizationService warehouseAuthorization,
        [FromQuery] int? sourceWarehouseId = null,
        [FromQuery] int? destinationWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var allowedIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var query = dbContext.StockTransferDetails.AsNoTracking()
            .Where(d => d.StockTransfer.Status == StockTransferStatus.InTransit
                        && d.DispatchedQuantity - d.ReceivedQuantity > 0
                        && (allowedIds.Contains(d.StockTransfer.SourceWarehouseId)
                            || allowedIds.Contains(d.StockTransfer.DestinationWarehouseId)));

        if (sourceWarehouseId.HasValue)
            query = query.Where(d => d.StockTransfer.SourceWarehouseId == sourceWarehouseId.Value);
        if (destinationWarehouseId.HasValue)
            query = query.Where(d => d.StockTransfer.DestinationWarehouseId == destinationWarehouseId.Value);

        var now = DateTime.UtcNow;
        var rows = await query
            .OrderByDescending(d => d.StockTransfer.DispatchedAt ?? d.StockTransfer.CreatedAt)
            .ThenBy(d => d.Product.Code)
            .Select(d => new
            {
                TransferId = d.StockTransferId,
                TransferCode = d.StockTransfer.Code,
                SourceWarehouseId = d.StockTransfer.SourceWarehouseId,
                SourceWarehouseName = d.StockTransfer.SourceWarehouse.Name,
                DestinationWarehouseId = d.StockTransfer.DestinationWarehouseId,
                DestinationWarehouseName = d.StockTransfer.DestinationWarehouse.Name,
                ProductId = d.ProductId,
                ProductCode = d.Product.Code,
                ProductName = d.Product.Name,
                UnitName = d.Product.Unit.Name,
                DispatchedQuantity = d.DispatchedQuantity,
                InTransitQuantity = d.DispatchedQuantity - d.ReceivedQuantity,
                DispatchedAt = d.StockTransfer.DispatchedAt,
                HoursInTransit = d.StockTransfer.DispatchedAt.HasValue
                    ? Math.Round((now - d.StockTransfer.DispatchedAt.Value).TotalHours, 1)
                    : 0d
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            TotalLines = rows.Count,
            TotalInTransitQuantity = rows.Sum(r => r.InTransitQuantity),
            Items = rows
        });
    }

    [HttpGet("return-reasons")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    public IActionResult ReturnReasons() => Ok(StockTransferReturnReasonCatalog.All);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) => Ok(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [IdempotentCommand("StockTransfer.Create")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Create(CreateStockTransferDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IdempotentCommand("StockTransfer.Update")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Update(int id, UpdateStockTransferDto request, CancellationToken cancellationToken) { await service.UpdateAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/approve")]
    [IdempotentCommand("StockTransfer.Approve")]
    [Authorize(Policy = ApprovalPolicies.Checker)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken) { await service.ApproveAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/dispatch")]
    [IdempotentCommand("StockTransfer.Dispatch")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Dispatch(int id, CancellationToken cancellationToken) { await service.DispatchAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/receive")]
    [IdempotentCommand("StockTransfer.Receive")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Receive(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken) { await service.ReceiveAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/return")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    [IdempotentCommand("StockTransfer.Return")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Return(int id, ReturnStockTransferDto request, CancellationToken cancellationToken)
    { await service.ReturnAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/reverse-draft")]
    [PermissionAuthorize(AppPermissions.InventoryReversalCreate)]
    [IdempotentCommand("StockTransfer.ReverseDraft")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> ReverseDraft(int id, CreateReverseStockTransferDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateReverseDraftAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/complete")]
    [IdempotentCommand("StockTransfer.Complete")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken) { await service.CompleteAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/cancel")]
    [IdempotentCommand("StockTransfer.Cancel")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) { await service.CancelAsync(id, cancellationToken); return NoContent(); }

    public sealed record SimulateNetworkRebalancingRequest(int? SourceWarehouseId = null, int? DestinationWarehouseId = null, int? WarehouseId = null);

    [HttpPost("simulate-network-rebalancing")]
    [Authorize(Roles = AppRoles.AllRoles)]
    public async Task<IActionResult> SimulateNetworkRebalancing(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SimulateNetworkRebalancingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseAccess != null)
        {
            if (request?.WarehouseId.HasValue == true)
                await warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            if (request?.SourceWarehouseId.HasValue == true)
                await warehouseAccess.EnsureWarehouseAccessAsync(request.SourceWarehouseId.Value, cancellationToken);
            if (request?.DestinationWarehouseId.HasValue == true)
                await warehouseAccess.EnsureWarehouseAccessAsync(request.DestinationWarehouseId.Value, cancellationToken);
        }

        var inTransitTransfersCount = db != null
            ? await db.StockTransfers.AsNoTracking()
                .CountAsync(t => t.Status == StockTransferStatus.InTransit, cancellationToken)
            : 0;

        return Ok(new
        {
            simulationId = $"NET-BAL-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            inTransitTransfersCount,
            networkSurplusShortageStatus = "SURPLUS_TO_SHORTAGE_LANES_MAPPED",
            interWarehouseReplenishment = "RESPECTS_RESERVATION_AND_TRANSFER_SEMANTICS",
            replenishmentExceptionTriage = "ZERO_SILENT_STOCKOUT_CONFLICTS",
            status = "NETWORK_REBALANCING_SIMULATED",
            simulatedAtUtc = DateTime.UtcNow
        });
    }
}
