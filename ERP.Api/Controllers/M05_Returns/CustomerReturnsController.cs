using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/customer-returns")]
[Route("api/v1/customer-returns")]
public class CustomerReturnsController : ControllerBase
{
    private readonly IImportReceiptOperationsService _receiptOps;
    private readonly IWarehouseAuthorizationService _warehouseAccess;
    private readonly ICurrentUser _currentUser;

    public CustomerReturnsController(
        IImportReceiptOperationsService receiptOps,
        IWarehouseAuthorizationService warehouseAccess,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(receiptOps);
        ArgumentNullException.ThrowIfNull(warehouseAccess);
        ArgumentNullException.ThrowIfNull(currentUser);
        _receiptOps = receiptOps;
        _warehouseAccess = warehouseAccess;
        _currentUser = currentUser;
    }

    private int? GetActorUserId() =>
        _currentUser.UserId > 0
            ? _currentUser.UserId
            : int.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value, out var uid)
                ? uid
                : null;

    private bool CanReadInventoryCost() =>
        this.HasActionPermission(_currentUser, AppPermissions.InventoryCostRead);

    [HttpGet]
    [PermissionAuthorize(AppPermissions.ReturnRead, AppPermissions.ReceiptRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? condition = null,
        [FromQuery] string? disposition = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReturnRead, AppPermissions.ReceiptRead, AppPermissions.InventoryRead))
            return Forbid();

        var filter = new CustomerReturnQueryFilter(
            warehouseId,
            status,
            condition,
            disposition,
            keyword,
            pageNumber <= 0 ? 1 : pageNumber,
            pageSize <= 0 ? 20 : Math.Min(pageSize, 100),
            CanReadInventoryCost());

        return this.ToActionResult(await _receiptOps.QueryCustomerReturnsAsync(filter, _warehouseAccess, cancellationToken));
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.ReturnRead, AppPermissions.ReceiptRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReturnRead, AppPermissions.ReceiptRead, AppPermissions.InventoryRead))
            return Forbid();

        return this.ToActionResult(await _receiptOps.GetCustomerReturnByIdAsync(id, CanReadInventoryCost(), _warehouseAccess, cancellationToken));
    }

    [HttpPost]
    [HttpPost("intake")]
    [IdempotentCommand("CustomerReturn.Intake")]
    [PermissionAuthorize(AppPermissions.ReturnCreate, AppPermissions.ReturnReceive, AppPermissions.ReceiptReceive)]
    public async Task<IActionResult> Intake(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CustomerReturnIntakeInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReturnCreate, AppPermissions.ReturnReceive, AppPermissions.ReceiptReceive))
            return Forbid();

        return this.ToActionResult(await _receiptOps.IntakeCustomerReturnAsync(
            request,
            GetActorUserId(),
            CanReadInventoryCost(),
            _warehouseAccess,
            cancellationToken));
    }

    [HttpPost("{id:int}/inspect")]
    [IdempotentCommand("CustomerReturn.Inspect")]
    [PermissionAuthorize(AppPermissions.ReturnInspect, AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> Inspect(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CustomerReturnInspectInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReturnInspect, AppPermissions.ReceiptUpdate))
            return Forbid();

        return this.ToActionResult(await _receiptOps.InspectCustomerReturnAsync(
            id,
            request,
            GetActorUserId(),
            CanReadInventoryCost(),
            _warehouseAccess,
            cancellationToken));
    }

    [HttpPost("{id:int}/dispose")]
    [HttpPost("{id:int}/post-disposition")]
    [IdempotentCommand("CustomerReturn.Dispose")]
    [PermissionAuthorize(AppPermissions.ReturnDisposition, AppPermissions.ReturnPost, AppPermissions.ReceiptUpdate)]
    public async Task<IActionResult> DisposeReturn(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CustomerReturnDisposeInput? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ReturnDisposition, AppPermissions.ReturnPost, AppPermissions.ReceiptUpdate))
            return Forbid();

        return this.ToActionResult(await _receiptOps.DisposeCustomerReturnAsync(
            id,
            request,
            GetActorUserId(),
            CanReadInventoryCost(),
            _warehouseAccess,
            cancellationToken));
    }
}
