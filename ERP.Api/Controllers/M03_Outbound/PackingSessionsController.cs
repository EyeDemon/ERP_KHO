using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/packing-sessions")]
[Route("api/v1/packing-sessions")]
[Authorize]
public sealed class PackingSessionsController(
    IPackingService service,
    IPackingSessionOperationsService packingOps,
    IWarehouseAuthorizationService warehouseAccess) : ControllerBase
{
    public sealed record VerifyPackagingProfileRequest(
        int? ProductId = null,
        string? CartonType = null,
        int? WarehouseId = null) : VerifyPackagingProfileInput(ProductId, CartonType, WarehouseId);

    public sealed record ExecuteKittingVasRequest(
        int? WarehouseId = null,
        string? KitSkuCode = null,
        string? VasServiceType = null,
        string? VasServices = null,
        int? ComponentProductId = null,
        string? ComponentProductCode = null,
        int? KitProductId = null,
        int? ParentProductId = null,
        string? KitProductCode = null,
        int? LocationId = null,
        string? LocationCode = null,
        decimal? KitQuantity = null,
        decimal? Quantity = null,
        decimal? ComponentRatio = null,
        string? OperationMode = null,
        string? VasMode = null,
        string? Note = null,
        bool ExecuteStockMutation = false) : ExecuteKittingVasInput(
            WarehouseId, KitSkuCode, VasServiceType, VasServices, ComponentProductId, ComponentProductCode,
            KitProductId, ParentProductId, KitProductCode, LocationId, LocationCode, KitQuantity, Quantity,
            ComponentRatio, OperationMode, VasMode, Note, ExecuteStockMutation);

    public sealed record RecordPackingVideoEvidenceRequest(
        int? PackingSessionId = null,
        int? ExportReceiptId = null,
        string? TrackingNumber = null,
        string? OrderCode = null,
        string? CameraStationId = null,
        DateTime? StartedAtUtc = null,
        DateTime? EndedAtUtc = null,
        string? VideoHashSha256 = null,
        string? StorageUrl = null,
        long? FileSizeBytes = null) : RecordPackingVideoEvidenceInput(
            PackingSessionId, ExportReceiptId, TrackingNumber, OrderCode, CameraStationId,
            StartedAtUtc, EndedAtUtc, VideoHashSha256, StorageUrl, FileSizeBytes);

    public sealed record RecommendCartonRequest(
        decimal? DimDivisor = 6000m,
        string? ServiceType = "ROAD_STANDARD",
        decimal? FragileBubbleWrapAllowancePct = 15m,
        string? PreferredCartonCode = null) : RecommendCartonInput(
            DimDivisor, ServiceType, FragileBubbleWrapAllowancePct, PreferredCartonCode);

    private int? TryGetActorUserId()
    {
        var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
        return int.TryParse(raw, out var uid) && uid > 0 ? uid : null;
    }

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
        Ok(packingOps.GetPackagingProfilesPayload());

    [HttpPost("verify-packaging-profile")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    public async Task<IActionResult> VerifyPackagingProfile(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VerifyPackagingProfileRequest? request = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await packingOps.VerifyPackagingProfileAsync(request, warehouseAccess, cancellationToken));

    [HttpPost("execute-kitting-vas"), IdempotentCommand("Packing.ExecuteKittingVas"), PermissionAuthorize(AppPermissions.PackingExecute)]
    public async Task<IActionResult> ExecuteKittingVas(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExecuteKittingVasRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.PackingExecute))
            return Forbid();

        return this.ToActionResult(await packingOps.ExecuteKittingVasAsync(request, TryGetActorUserId(), warehouseAccess, cancellationToken));
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

    [HttpPost("video-evidence")]
    [HttpPost("video-evidences")]
    [HttpPost("{sessionId:int}/video-evidence")]
    [HttpPost("packing-video-evidence")]
    [RequestSizeLimit(FileUploadSecurityValidator.MaxVideoBytes)]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.RecordVideoEvidence")]
    public async Task<IActionResult> RecordPackingVideoEvidence(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] RecordPackingVideoEvidenceRequest? request = null,
        CancellationToken cancellationToken = default,
        [FromRoute] int? sessionId = null,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? videoFile = null)
    {
        if (!this.HasActionPermission(null, AppPermissions.PackingExecute))
            return Forbid();

        if (videoFile != null)
        {
            var fileValidation = FileUploadSecurityValidator.ValidatePackingVideoFile(videoFile);
            if (!fileValidation.IsValid) return fileValidation.ToActionResult(this);
        }

        await using var videoStream = videoFile is { Length: > 0 } ? videoFile.OpenReadStream() : null;
        return this.ToActionResult(await packingOps.RecordPackingVideoEvidenceAsync(
            request,
            sessionId,
            videoStream,
            videoFile?.Length,
            TryGetActorUserId(),
            cancellationToken));
    }

    [HttpGet("video-evidence")]
    [HttpGet("video-evidences")]
    [HttpGet("{sessionId:int}/video-evidence")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> ListPackingVideoEvidences(
        [FromQuery] string? trackingNumber = null,
        [FromQuery] string? orderCode = null,
        [FromRoute] int? sessionId = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await packingOps.ListPackingVideoEvidencesAsync(trackingNumber, orderCode, sessionId, cancellationToken));

    [HttpGet("video-evidence/{trackingNumber}")]
    [HttpGet("video-evidences/{trackingNumber}")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> GetPackingVideoEvidence(
        string trackingNumber,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await packingOps.GetPackingVideoEvidenceAsync(trackingNumber, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> Cancel(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));

    [HttpGet("{id}/recommend-carton")]
    [HttpGet("recommend-carton")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> GetRecommendCarton(
        [FromRoute] string? id = null,
        [FromQuery] string? packingSessionId = null,
        [FromQuery] decimal? dimDivisor = null,
        [FromQuery] string? serviceType = null,
        [FromQuery] decimal? fragileBubbleWrapAllowancePct = null,
        [FromQuery] string? preferredCartonCode = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = !string.IsNullOrWhiteSpace(id) ? id : packingSessionId;
        return this.ToActionResult(await packingOps.BuildCartonRecommendationAsync(
            resolvedId,
            new RecommendCartonRequest(
                DimDivisor: dimDivisor ?? 6000m,
                ServiceType: serviceType ?? "ROAD_STANDARD",
                FragileBubbleWrapAllowancePct: fragileBubbleWrapAllowancePct ?? 15m,
                PreferredCartonCode: preferredCartonCode),
            warehouseAccess,
            cancellationToken));
    }

    [HttpPost("{id}/recommend-carton")]
    [HttpPost("recommend-carton")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("PackingSession.RecommendCarton")]
    public async Task<IActionResult> RecommendCarton(
        [FromRoute] string? id = null,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RecommendCartonRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.PackingExecute))
            return Forbid();

        return this.ToActionResult(await packingOps.BuildCartonRecommendationAsync(id, request, warehouseAccess, cancellationToken));
    }
}
