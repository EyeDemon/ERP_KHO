using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/handling-units")]
[Authorize]
[PermissionAuthorize(AppPermissions.HandlingUnitRead)]
public sealed class HandlingUnitsController(
    IPackingService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HandlingUnitDto>>> List(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListHandlingUnitsAsync(warehouseId, cancellationToken));

    [HttpGet("summary")]
    [PermissionAuthorize(AppPermissions.HandlingUnitRead)]
    public async Task<IActionResult> Summary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseAccess is not null && warehouseId.HasValue)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var units = await service.ListHandlingUnitsAsync(warehouseId, cancellationToken);
        return Ok(new
        {
            totalHandlingUnits = units.Count,
            openOrInUseCount = units.Count(x => x.Status is "Open" or "InUse" or "Packing"),
            sealedCount = units.Count(x => x.Status is "Closed" or "Sealed" || x.SealedAt != null),
            stagedOrLoadedCount = units.Count(x => x.Status is "Staged" or "Loaded"),
            shippedCount = units.Count(x => x.Status == "Shipped"),
            nestedHuCount = units.Count(x => x.ParentHandlingUnitId != null),
            ssccAssignedCount = units.Count(x => !string.IsNullOrWhiteSpace(x.Sscc)),
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HandlingUnitDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetHandlingUnitAsync(id, cancellationToken));

    public sealed record VerifyInTransitSealRequest(string? TransferCode = null, int? WarehouseId = null);

    [HttpPost("verify-intransit-seal")]
    [PermissionAuthorize(AppPermissions.HandlingUnitRead)]
    public async Task<IActionResult> VerifyInTransitSeal(
        [FromBody] VerifyInTransitSealRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseAccess is not null && request?.WarehouseId is int warehouseId)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);

        var code = string.IsNullOrWhiteSpace(request?.TransferCode) ? "TRF-IN-TRANSIT" : request.TransferCode.Trim();
        var activeInTransitTransfers = db is not null
            ? await db.StockTransfers.AsNoTracking().CountAsync(
                t => t.Status == StockTransferStatus.InTransit &&
                     (!request!.WarehouseId.HasValue || t.SourceWarehouseId == request.WarehouseId.Value || t.DestinationWarehouseId == request.WarehouseId.Value),
                cancellationToken)
            : 0;
        var sealedHuCount = db is not null
            ? await db.HandlingUnits.AsNoTracking().CountAsync(
                h => h.SealedAt != null && (!request!.WarehouseId.HasValue || h.WarehouseId == request.WarehouseId.Value),
                cancellationToken)
            : 0;
        var unsealedInTransitCount = db is not null
            ? await db.HandlingUnits.AsNoTracking().CountAsync(
                h => h.Status == HandlingUnitStatus.Shipped && h.SealedAt == null &&
                     (!request!.WarehouseId.HasValue || h.WarehouseId == request.WarehouseId.Value),
                cancellationToken)
            : 0;

        return Ok(new
        {
            sealVerificationId = $"HU-SEAL-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            transferCode = code,
            activeInTransitTransfers,
            sealedHuCount,
            inTransitBalanceMatched = unsealedInTransitCount == 0,
            sealStatus = unsealedInTransitCount == 0 ? "SEAL_INTACT_VERIFIED" : "UNSEALED_HU_DETECTED",
            verifiedAtUtc = DateTime.UtcNow
        });
    }
}
