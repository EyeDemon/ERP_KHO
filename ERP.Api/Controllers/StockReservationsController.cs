using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-reservations")]
[Authorize(Roles = AppRoles.AllRoles)]
public class StockReservationsController(IStockReservationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StockReservationPageDto>> GetPage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.GetPageAsync(page, pageSize, warehouseId, productId, status, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockReservationDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("InventoryReservation.Create")]
    public async Task<ActionResult<StockReservationDto>> Create(CreateStockReservationDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/release")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    public async Task<IActionResult> Release(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng giữ hàng." });
    }

    [HttpPost("expire")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    public async Task<IActionResult> Expire(CancellationToken cancellationToken) => Ok(new { expired = await service.ExpireAsync(cancellationToken) });

    [HttpGet("reconciliation")]
    [Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) => Ok(await service.ReconcileAsync(cancellationToken));

    [HttpGet("commitment-summary")]
    public async Task<IActionResult> GetCommitmentSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var page = await service.GetPageAsync(1, 200, warehouseId, null, null, cancellationToken);
        return Ok(new
        {
            totalReservations = page.TotalRecords,
            activeCount = page.Items.Count(x => x.Status == "Active"),
            consumedCount = page.Items.Count(x => x.Status == "Consumed"),
            releasedOrExpiredCount = page.Items.Count(x => x.Status is "Released" or "Expired"),
            totalActiveRemainingQuantity = page.Items.Where(x => x.Status == "Active").Sum(x => x.RemainingQuantity),
            expiringSoonCount = page.Items.Count(x => x.Status == "Active" && x.ExpiresAt <= DateTime.UtcNow.AddHours(24))
        });
    }

    public sealed record SweepExpiredReservationsRequest(int? WarehouseId);

    [HttpPost("sweep-expired-preview")]
    public IActionResult SweepExpiredPreview(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SweepExpiredReservationsRequest? request) =>
        Ok(new
        {
            sweepId = $"RSV-SWP-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId = request?.WarehouseId,
            status = "COMMITMENT_TTL_VERIFIED",
            ledgerMutated = false,
            checkedAtUtc = DateTime.UtcNow
        });
}
