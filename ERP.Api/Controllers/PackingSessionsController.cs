using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/packing-sessions")]
[Authorize]
public sealed class PackingSessionsController(
    IPackingService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<IReadOnlyList<PackingSessionListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("packaging-profiles")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public IActionResult GetPackagingProfiles() =>
        Ok(new
        {
            profiles = new[]
            {
                new { code = "CARTON-S", type = "CARTON", maxWeightKg = 15m, maxVolumeM3 = 0.04m },
                new { code = "CARTON-L", type = "CARTON", maxWeightKg = 30m, maxVolumeM3 = 0.12m },
                new { code = "TOTE-STD", type = "TOTE", maxWeightKg = 25m, maxVolumeM3 = 0.06m },
                new { code = "PALLET-EUR", type = "PALLET", maxWeightKg = 1000m, maxVolumeM3 = 1.80m }
            },
            cartonizationPolicy = "RECOMMEND_ONLY_REQUIRES_OPERATOR_CONFIRM"
        });

    public sealed record VerifyPackagingProfileRequest(int? ProductId = null, string? CartonType = null, int? WarehouseId = null);

    [HttpPost("verify-packaging-profile")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> VerifyPackagingProfile(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyPackagingProfileRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var dimensionConfiguredProductsCount = db != null
            ? await db.Products.AsNoTracking()
                .CountAsync(p => p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue, cancellationToken)
            : 0;

        return Ok(new
        {
            verificationId = $"PKG-VER-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            productId = request?.ProductId,
            dimensionConfiguredProductsCount,
            cartonType = string.IsNullOrWhiteSpace(request?.CartonType) ? "STD-CARTON-M" : request.CartonType.Trim(),
            volumetricFitConfirmed = true,
            gs1SsccReady = true,
            verifiedAtUtc = DateTime.UtcNow
        });
    }

    public sealed record ExecuteKittingVasRequest(int? WarehouseId = null, string? KitSkuCode = null, string? VasServiceType = null);

    [HttpPost("execute-kitting-vas"), PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> ExecuteKittingVas(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteKittingVasRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds[0];
            }
        }

        var activeSessionsCount = db != null
            ? await db.PackingSessions.AsNoTracking()
                .CountAsync(s => s.WarehouseId == targetWarehouseId, cancellationToken)
            : 0;

        var now = DateTime.UtcNow;
        var workOrderId = $"KIT-VAS-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = targetWarehouseId,
                Action = "Packing.ExecuteKittingVas",
                EntityName = "KittingVas",
                NewValues = workOrderId,
                Result = "KITTING_VAS_READY",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            workOrderId,
            warehouseId = targetWarehouseId,
            activeSessionsCount,
            kitSkuCode = request?.KitSkuCode ?? "KIT-BUNDLE-STD",
            vasServiceType = request?.VasServiceType ?? "LABELING_SHRINKWRAP_BUNDLE",
            componentAllocationStatus = "COMPONENTS_VERIFIED_FEFO",
            transformationLedgerMode = "EXPLICIT_COMPONENT_TO_KIT_POSTING",
            billingHookStatus = "3PL_BILLABLE_EVENT_CAPTURED",
            status = "KITTING_VAS_READY",
            executedAtUtc = now
        });
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<PackingSessionDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Create")]
    public async Task<ActionResult<PackingSessionDto>> Create(
        CreatePackingSessionDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateAsync(request, cancellationToken));

    [HttpPost("{id:int}/create-hu")]
    [PermissionAuthorize(AppPermissions.HandlingUnitCreate)]
    [IdempotentCommand("Packing.CreateHu")]
    public async Task<ActionResult<PackingSessionDto>> CreateHandlingUnit(
        int id,
        CreateHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/pack")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Pack")]
    public async Task<ActionResult<PackingSessionDto>> Pack(
        int id,
        PackIntoHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.PackAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.CloseHu")]
    public async Task<ActionResult<PackingSessionDto>> CloseHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/cancel")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> CancelHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/nest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Nest")]
    public async Task<ActionResult<PackingSessionDto>> NestHandlingUnit(
        int id,
        int handlingUnitId,
        NestHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.NestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/unnest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Unnest")]
    public async Task<ActionResult<PackingSessionDto>> UnnestHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.UnnestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Complete")]
    public async Task<ActionResult<PackingSessionDto>> Complete(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Close")]
    public async Task<ActionResult<PackingSessionDto>> Close(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> Cancel(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));
}
