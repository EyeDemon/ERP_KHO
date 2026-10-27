using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/handover-manifests")]
[Route("api/handover-manifests")]
public sealed class HandoverManifestsController(IHandoverManifestService handoverManifestService) : ControllerBase
{
    private readonly IHandoverManifestService _handoverManifestService = handoverManifestService;

    private int? TryGetUserId()
    {
        if (User is null)
        {
            return null;
        }
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
        return int.TryParse(userIdValue, out var uid) ? uid : null;
    }

    private IActionResult ToActionResult(HandoverManifestOperationResult result) =>
        result.StatusCode switch
        {
            200 => Ok(result.Payload),
            400 => BadRequest(result.Payload),
            404 => NotFound(result.Payload),
            409 => Conflict(result.Payload),
            422 => UnprocessableEntity(result.Payload),
            _ => StatusCode(result.StatusCode, result.Payload)
        };

    [HttpGet]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    public async Task<IActionResult> GetHandoverManifests(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? carrierCode = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _handoverManifestService.GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("scan-awb")]
    [HttpPost("~/api/v1/integrations/handover-manifests/{manifestIdRoute:int}/scan")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    [IdempotentCommand("HandoverManifest.ScanAwb")]
    public async Task<IActionResult> ScanHandoverAwb(
        [FromRoute] int? manifestIdRoute = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExportReceiptsController.ScanHandoverAwbRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var result = await _handoverManifestService.ScanHandoverAwbAsync(
            manifestIdRoute,
            request == null
                ? null
                : new ScanHandoverAwbInputDto(
                    request.ManifestId,
                    request.WarehouseId,
                    request.CarrierCode,
                    request.TrackingNumber,
                    request.ExportReceiptId,
                    request.VehiclePlate,
                    request.DriverName,
                    request.DriverPhone),
            TryGetUserId(),
            User?.Identity?.IsAuthenticated == true,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:int}/seal")]
    [HttpPost("~/api/v1/integrations/handover-manifests/{id:int}/seal")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    [IdempotentCommand("HandoverManifest.Seal")]
    public async Task<IActionResult> SealHandoverManifest(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExportReceiptsController.SealHandoverManifestRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var result = await _handoverManifestService.SealHandoverManifestAsync(
            id,
            request == null
                ? null
                : new SealHandoverManifestInputDto(
                    request.DriverName,
                    request.DriverPhone,
                    request.VehiclePlate,
                    request.DigitalSignatureHash),
            TryGetUserId(),
            User?.Identity?.IsAuthenticated == true,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("confirm")]
    [HttpPost("confirm-carrier-handover")]
    [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
    [IdempotentCommand("HandoverManifest.ConfirmCarrierHandover")]
    public async Task<IActionResult> ConfirmCarrierHandover(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExportReceiptsController.ConfirmCarrierHandoverRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var result = await _handoverManifestService.ConfirmCarrierHandoverAsync(
            request == null
                ? null
                : new ConfirmCarrierHandoverInputDto(
                    request.WarehouseId,
                    request.CarrierCode,
                    request.TrackingNumbers,
                    request.VehiclePlate,
                    request.DriverName,
                    request.DriverPhone,
                    request.ManifestId,
                    request.ExportReceiptIds),
            TryGetUserId(),
            User?.Identity?.IsAuthenticated == true,
            cancellationToken);
        return ToActionResult(result);
    }
}
