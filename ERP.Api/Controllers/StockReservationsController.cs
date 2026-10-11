using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-reservations")]
[Authorize]
public class StockReservationsController(IStockReservationService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.ReservationRead)]
    public async Task<ActionResult<StockReservationPageDto>> GetPage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.GetPageAsync(page, pageSize, warehouseId, productId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.ReservationRead)]
    public async Task<ActionResult<StockReservationDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.ReservationCreate)]
    [IdempotentCommand("StockReservation.Create")]
    public async Task<ActionResult<StockReservationDto>> Create(CreateStockReservationDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.ReservationRelease)]
    [IdempotentCommand("StockReservation.Release")]
    public async Task<IActionResult> Release(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng giữ hàng." });
    }

    [HttpPost("expire")]
    [PermissionAuthorize(AppPermissions.ReservationRelease)]
    [IdempotentCommand("StockReservation.Expire")]
    public async Task<IActionResult> Expire(CancellationToken cancellationToken) => Ok(new { expired = await service.ExpireAsync(cancellationToken) });

    [HttpGet("reconciliation")]
    [PermissionAuthorize(AppPermissions.ReservationRead)]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) => Ok(await service.ReconcileAsync(cancellationToken));
}
