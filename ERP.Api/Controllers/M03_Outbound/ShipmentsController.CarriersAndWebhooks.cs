using System.Text;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed partial class ShipmentsController
{
    [HttpGet("vn-administrative-units"), PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetVnAdministrativeUnits(
        [FromQuery] string? provinceCode = null,
        [FromQuery] string? districtCode = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await shipmentOps.GetVnAdministrativeUnitsAsync(provinceCode, districtCode, search, cancellationToken));

    [HttpPost("normalize-address"), HttpPost("normalize-vn-address"), PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> NormalizeVietnameseAddress(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NormalizeVnAddressRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.NormalizeVietnameseAddressAsync(request, cancellationToken));
    }

    [HttpGet("carrier-configs"), PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetCarrierConfigs(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await shipmentOps.GetCarrierConfigsAsync(warehouseId, warehouseAccess, cancellationToken));

    [HttpPost("carrier-configs"), IdempotentCommand("Shipment.UpsertCarrierConfig")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> UpsertCarrierConfig(
        [FromBody] UpsertCarrierConfigInput? request,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.UpsertCarrierConfigAsync(request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("carrier-configs/ghn/verify-and-sync"), IdempotentCommand("Shipment.VerifyAndSyncGhnConfig")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> VerifyAndSyncGhnConfig(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyAndSyncGhnInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.VerifyAndSyncGhnConfigAsync(request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("carrier-configs/{carrierCode}/verify-and-sync"), IdempotentCommand("Shipment.VerifyAndSyncCarrierConfig")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> VerifyAndSyncCarrierConfig(
        [FromRoute] string carrierCode,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyAndSyncGhnInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.VerifyAndSyncCarrierConfigAsync(carrierCode, request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("{id:int}/ghn/sync-detail")]
    [HttpPost("{id:int}/carrier/sync-detail")]
    [IdempotentCommand("Shipment.SyncCarrierWaybillDetail")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentStage)]
    public async Task<IActionResult> SyncCarrierWaybillDetail(
        [FromRoute] int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncGhnOrderDetailInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentStage)) return Forbid();
        return this.ToActionResult(await shipmentOps.SyncCarrierWaybillDetailAsync(id, request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("{id:int}/ghn/cancel-waybill")]
    [HttpPost("{id:int}/carrier/cancel-waybill")]
    [IdempotentCommand("Shipment.CancelCarrierWaybill")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> CancelCarrierWaybill(
        [FromRoute] int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelGhnWaybillInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.CancelCarrierWaybillAsync(id, request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("calculate-carrier-fee"), IdempotentCommand("Shipment.CalculateCarrierFee")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentStage)]
    public async Task<IActionResult> CalculateCarrierFee(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CalculateCarrierFeeInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentStage)) return Forbid();
        return this.ToActionResult(await shipmentOps.CalculateCarrierFeeAsync(request, warehouseAccess, cancellationToken));
    }

    [HttpPost("book-carrier-waybill"), IdempotentCommand("Shipment.BookCarrierWaybill")]
    [PermissionAuthorize(AppPermissions.ShipmentDispatch, AppPermissions.ShipmentUpdate)]
    public async Task<IActionResult> BookCarrierWaybill(
        [FromBody] CarrierWaybillBookingRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentUpdate)) return Forbid();
        return this.ToActionResult(await shipmentOps.BookCarrierWaybillAsync(request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpPost("webhooks/carrier/{carrierCode}"), AllowAnonymous, IdempotentCommand("Shipment.CarrierWebhook")]
    public async Task<IActionResult> CarrierWebhook(
        [FromRoute] string carrierCode,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CarrierWebhookEventInput? request = null,
        [FromHeader(Name = "X-Carrier-Signature")] string? signatureHeader = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSignature = !string.IsNullOrWhiteSpace(signatureHeader)
            ? signatureHeader
            : (HttpContext?.Request?.Headers["X-Carrier-Signature"].FirstOrDefault()
               ?? HttpContext?.Request?.Headers["X-Hub-Signature-256"].FirstOrDefault());

        string? rawBody = null;
        if (HttpContext?.Request?.Body is { CanSeek: true } seekableBody)
        {
            seekableBody.Position = 0;
            using var reader = new StreamReader(seekableBody, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            rawBody = await reader.ReadToEndAsync(cancellationToken);
            seekableBody.Position = 0;
        }

        return this.ToActionResult(await shipmentOps.IngestCarrierWebhookAsync(
            carrierCode,
            request,
            rawBody: string.IsNullOrWhiteSpace(rawBody) ? null : rawBody,
            signatureHeader: effectiveSignature,
            actorUserId: GetActorUserId(),
            cancellationToken: cancellationToken));
    }
}

public sealed record NormalizeVnAddressRequest(
    string? RawAddress = null, string? Province = null, string? District = null, string? Ward = null, string? CarrierCode = null)
    : NormalizeVnAddressInput(RawAddress, Province, District, Ward, CarrierCode);
