using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/sales-orders")]
[Authorize]
public sealed class SalesOrdersController(ISalesOrderBackorderService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<IReadOnlyList<SalesOrderListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListSalesOrdersAsync(warehouseId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<SalesOrderDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetSalesOrderAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("SalesOrder.Create")]
    public async Task<ActionResult<SalesOrderDto>> Create(
        CreateSalesOrderDto request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateSalesOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/hold")]
    [PermissionAuthorize(AppPermissions.SalesOrderHold)]
    [IdempotentCommand("SalesOrder.Hold")]
    public async Task<ActionResult<SalesOrderDto>> Hold(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.HoldSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.SalesOrderRelease)]
    [IdempotentCommand("SalesOrder.Release")]
    public async Task<ActionResult<SalesOrderDto>> Release(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReleaseSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.Cancel")]
    public async Task<ActionResult<SalesOrderDto>> Cancel(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelSalesOrderAsync(id, request, cancellationToken));
}
