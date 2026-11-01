using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed record CreateWarehouseCalendarExceptionRequest(
    string ExceptionType,
    string WindowDateOrRange,
    string Reason);

public sealed record WarehouseCalendarExceptionDto(
    int Id,
    int WarehouseId,
    string ExceptionType,
    string WindowSummary,
    string Reason,
    int? CreatedBy,
    DateTime CreatedAtUtc);

[Authorize]
[ApiController]
[Route("api/warehouses/{warehouseId:int}/calendar")]
[Route("api/v1/warehouses/{warehouseId:int}/calendar")]
[Route("api/v1/warehouse-calendars/{warehouseId:int}")]
public sealed class WarehouseCalendarsController(
    IWarehouseCalendarService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.WarehouseRead)]
    public async Task<IActionResult> Get(int warehouseId, CancellationToken token) =>
        Ok(await service.GetAsync(warehouseId, token));

    [HttpPut]
    [IdempotentCommand("WarehouseCalendar.Update")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> Update(int warehouseId, UpdateWarehouseCalendarDto dto, CancellationToken token) =>
        Ok(await service.UpdateCalendarAsync(warehouseId, dto, token));

    [HttpPost("shifts")]
    [IdempotentCommand("WarehouseCalendar.CreateShift")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> CreateShift(int warehouseId, UpsertWarehouseShiftDto dto, CancellationToken token) =>
        Ok(await service.CreateShiftAsync(warehouseId, dto, token));

    [HttpPut("shifts/{shiftId:int}")]
    [IdempotentCommand("WarehouseCalendar.UpdateShift")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> UpdateShift(int warehouseId, int shiftId, UpsertWarehouseShiftDto dto, CancellationToken token) =>
        Ok(await service.UpdateShiftAsync(warehouseId, shiftId, dto, token));

    [HttpPost("verify-shift-coverage")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> VerifyShiftCoverage(int warehouseId, CancellationToken token = default)
    {
        if (warehouseAccess is not null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, token);

        return this.ToActionResult(await govOps.VerifyShiftCoverageAsync(warehouseId, token));
    }

    [HttpPost("optimize-labor-interleaving")]
    [IdempotentCommand("WarehouseCalendar.OptimizeLaborInterleaving")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> OptimizeLaborInterleaving(int warehouseId, CancellationToken token = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.WarehouseCalendarManage))
            return Forbid();

        if (warehouseAccess is not null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, token);

        var actorUserId = int.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        return this.ToActionResult(await govOps.OptimizeLaborInterleavingAsync(warehouseId, actorUserId, token));
    }

    [HttpGet("exceptions")]
    [PermissionAuthorize(AppPermissions.WarehouseRead)]
    public async Task<IActionResult> GetExceptions(int warehouseId, CancellationToken token)
    {
        if (warehouseAccess is not null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, token);

        var items = await govOps.GetWarehouseCalendarExceptionsAsync(warehouseId, token);
        return Ok(items
            .Select(a => new WarehouseCalendarExceptionDto(
                a.Id,
                a.WarehouseId,
                a.ExceptionType,
                a.WindowSummary,
                a.Reason,
                a.CreatedBy,
                a.CreatedAtUtc))
            .ToList());
    }

    [HttpPost("exceptions")]
    [IdempotentCommand("WarehouseCalendar.CreateException")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> CreateException(
        int warehouseId,
        [FromBody] CreateWarehouseCalendarExceptionRequest request,
        CancellationToken token)
    {
        if (warehouseAccess is not null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, token);

        var exceptionType = (request.ExceptionType ?? string.Empty).Trim().ToUpperInvariant();
        if (exceptionType is not ("HOLIDAY_CLOSURE" or "MAINTENANCE_WINDOW" or "EMERGENCY_OPEN_OVERRIDE"))
            throw new BusinessRuleException("Loại ngoại lệ lịch kho không hợp lệ (HOLIDAY_CLOSURE, MAINTENANCE_WINDOW, EMERGENCY_OPEN_OVERRIDE).");

        var windowSummary = (request.WindowDateOrRange ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(windowSummary))
            throw new BusinessRuleException("Khung thời gian hoặc ngày áp dụng ngoại lệ là bắt buộc.");

        var reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Lý do ghi nhận ngoại lệ lịch vận hành là bắt buộc.");

        var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value;
        int? userId = int.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

        var created = await govOps.CreateWarehouseCalendarExceptionAsync(
            warehouseId,
            exceptionType,
            windowSummary,
            reason,
            userId,
            token);

        return Ok(new WarehouseCalendarExceptionDto(
            created.Id,
            created.WarehouseId,
            created.ExceptionType,
            created.WindowSummary,
            created.Reason,
            created.CreatedBy,
            created.CreatedAtUtc));
    }
}
