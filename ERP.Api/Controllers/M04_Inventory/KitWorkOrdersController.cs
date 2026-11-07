using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/kit-work-orders")]
[Route("api/v1/kit-work-orders")]
public class KitWorkOrdersController : ControllerBase
{
    private readonly IProductOperationsService _productOps;
    private readonly ICurrentUser _currentUser;

    public KitWorkOrdersController(
        IProductOperationsService productOps,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(productOps);
        ArgumentNullException.ThrowIfNull(currentUser);
        _productOps = productOps;
        _currentUser = currentUser;
    }

    [HttpGet]
    [PermissionAuthorize(AppPermissions.ProductRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? kitProductId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
        => (await _productOps.GetKitWorkOrdersAsync(warehouseId, kitProductId, status, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [IdempotentCommand("Product.CreateKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CreateKitWorkOrderAsync(request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:int}/complete")]
    [IdempotentCommand("Product.CompleteKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Complete(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CompleteKitWorkOrderAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:int}/cancel")]
    [IdempotentCommand("Product.CancelKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CancelKitWorkOrderAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    private int? ResolveActorUserId()
        => int.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
