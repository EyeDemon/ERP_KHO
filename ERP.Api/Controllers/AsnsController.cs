using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController, Route("api/asns"), Route("api/v1/asns"), Authorize]
public sealed class AsnsController(
    IInboundPlanningService service,
    IWmsGovernanceOperationsService govOps = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> List([FromQuery] AsnStatus? status, CancellationToken ct) => Ok(await service.ListAsnsAsync(status, ct));

    [HttpGet("arrival-summary"), PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> ArrivalSummary(CancellationToken ct)
    {
        var items = await service.ListAsnsAsync(null, ct);
        return Ok(new
        {
            totalAsnCount = items.Count,
            inTransitCount = items.Count(x => x.Status == "InTransit"),
            arrivedWaitingCount = items.Count(x => x.Status == "Arrived"),
            receivingCount = items.Count(x => x.Status == "Receiving"),
            overdueArrivalCount = items.Count(x => (x.Status == "Confirmed" || x.Status == "InTransit") && x.ExpectedArrivalAtUtc.HasValue && x.ExpectedArrivalAtUtc.Value < DateTime.UtcNow),
            unlinkedPoAsnCount = items.Count(x => x.PurchaseOrderId == null)
        });
    }

    public sealed record PreAllocateAsnStagingRequest(int? WarehouseId = null, string? AsnNumber = null);

    [HttpPost("pre-allocate-staging"), PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> PreAllocateStaging(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PreAllocateAsnStagingRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        IReadOnlyList<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, ct);
            }
            else
            {
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(ct);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds[0];
            }
        }

        var asns = (await service.ListAsnsAsync(null, ct)) ?? [];
        var scopedAsns = asns
            .Where(a => !warehouseId.HasValue || a.WarehouseId == warehouseId.Value)
            .Where(a => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(a.WarehouseId))
            .ToList();
        var activeAsns = scopedAsns
            .Where(a => a.Status is "Confirmed" or "InTransit" or "Arrived" or "Receiving")
            .ToList();
        var preAllocatedAsnCount = activeAsns.Count > 0 ? activeAsns.Count : scopedAsns.Count;

        var resolvedAsnNumber = !string.IsNullOrWhiteSpace(request?.AsnNumber)
            ? request.AsnNumber.Trim()
            : (activeAsns.FirstOrDefault()?.Code?.Trim()
                ?? scopedAsns.FirstOrDefault()?.Code?.Trim()
                ?? "ASN-QA-001");

        if (govOps != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            var outcome = await govOps.PreAllocateAsnStagingAsync(
                targetWarehouseId,
                preAllocatedAsnCount,
                resolvedAsnNumber,
                actorUserId,
                ct);
            return outcome.ToActionResult(this);
        }

        var now = DateTime.UtcNow;
        var stagingReservationId = $"ASN-STG-{now:yyyyMMdd-HHmm}";
        return Ok(new
        {
            stagingReservationId,
            warehouseId = targetWarehouseId,
            asnNumber = resolvedAsnNumber,
            preAllocatedAsnCount,
            stagingLaneCode = "RCV-01",
            stagingLocationName = "RCV-01 — Khu vực tiếp nhận & tập kết hàng nhập",
            stagingStatus = "Đã phân bổ trước khu vực tiếp nhận & tập kết hàng nhập",
            status = "Đã phân bổ trước khu vực tập kết hàng nhập",
            statusLabel = "Đã phân bổ trước khu vực tập kết hàng nhập",
            dockPreAssigned = true,
            reservedAtUtc = now
        });
    }

    [HttpGet("{id:int}"), PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> Detail(int id, CancellationToken ct) => Ok(await service.GetAsnAsync(id, ct));

    [HttpPost, IdempotentCommand("Asn.Create"), PermissionAuthorize(AppPermissions.AsnCreate)]
    public async Task<IActionResult> Create(CreateAsnDto dto, CancellationToken ct)
    {
        var result = await service.CreateAsnAsync(dto, ct);
        return CreatedAtAction(nameof(Detail), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}"), IdempotentCommand("Asn.Update"), PermissionAuthorize(AppPermissions.AsnUpdate)]
    public async Task<IActionResult> Update(int id, UpdateAsnDto dto, CancellationToken ct) => Ok(await service.UpdateAsnAsync(id, dto, ct));

    [HttpPost("{id:int}/confirm"), IdempotentCommand("Asn.Confirm"), PermissionAuthorize(AppPermissions.AsnConfirm)]
    public async Task<IActionResult> Confirm(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.ConfirmAsnAsync(id, dto, ct));

    [HttpPost("{id:int}/mark-in-transit"), IdempotentCommand("Asn.MarkInTransit"), PermissionAuthorize(AppPermissions.AsnUpdate)]
    public async Task<IActionResult> MarkInTransit(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.MarkAsnInTransitAsync(id, dto, ct));

    [HttpPost("{id:int}/arrive"), IdempotentCommand("Asn.Arrive"), PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> Arrive(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.ArriveAsnAsync(id, dto, ct));

    [HttpPost("{id:int}/start-receiving"), IdempotentCommand("Asn.StartReceiving"), PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> StartReceiving(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.StartReceivingAsnAsync(id, dto, ct));

    [HttpPost("{id:int}/complete"), IdempotentCommand("Asn.Complete"), PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> Complete(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.CompleteAsnAsync(id, dto, ct));

    [HttpPost("{id:int}/cancel"), IdempotentCommand("Asn.Cancel"), PermissionAuthorize(AppPermissions.AsnCancel)]
    public async Task<IActionResult> Cancel(int id, InboundStateCommandDto dto, CancellationToken ct) => Ok(await service.CancelAsnAsync(id, dto, ct));
}
