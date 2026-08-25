using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff},{AppRoles.Viewer}")]
public sealed class StockTransfersController(IStockTransferService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] StockTransferQueryDto query, CancellationToken cancellationToken) => Ok(await service.GetAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) => Ok(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Create(CreateStockTransferDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Update(int id, UpdateStockTransferDto request, CancellationToken cancellationToken) { await service.UpdateAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken) { await service.ApproveAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/dispatch")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Dispatch(int id, CancellationToken cancellationToken) { await service.DispatchAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/receive")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Receive(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken) { await service.ReceiveAsync(id, request, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken) { await service.CompleteAsync(id, cancellationToken); return NoContent(); }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) { await service.CancelAsync(id, cancellationToken); return NoContent(); }
}
