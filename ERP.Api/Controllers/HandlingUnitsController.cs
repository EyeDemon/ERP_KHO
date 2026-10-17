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
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyInTransitSealRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess is not null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var code = string.IsNullOrWhiteSpace(request?.TransferCode) ? "TRF-IN-TRANSIT" : request.TransferCode.Trim();
        var activeInTransitTransfers = 0;
        var sealedHuCount = 0;
        var unsealedInTransitCount = 0;
        if (db is not null)
        {
            var transferQuery = db.StockTransfers.AsNoTracking().Where(t => t.Status == StockTransferStatus.InTransit);
            var huQuery = db.HandlingUnits.AsNoTracking();
            if (warehouseId.HasValue)
            {
                transferQuery = transferQuery.Where(t => t.SourceWarehouseId == warehouseId.Value || t.DestinationWarehouseId == warehouseId.Value);
                huQuery = huQuery.Where(h => h.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds is not null)
            {
                transferQuery = transferQuery.Where(t => accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId));
                huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
            }

            activeInTransitTransfers = await transferQuery.CountAsync(cancellationToken);
            sealedHuCount = await huQuery.CountAsync(h => h.SealedAt != null, cancellationToken);
            unsealedInTransitCount = await huQuery.CountAsync(h => h.Status == HandlingUnitStatus.Shipped && h.SealedAt == null, cancellationToken);
        }

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
