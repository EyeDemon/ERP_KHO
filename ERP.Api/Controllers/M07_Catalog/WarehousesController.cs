using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/v1/warehouses")]
    public class WarehousesController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;
        private readonly IWmsGovernanceOperationsService _govOps;
        private readonly IWarehouseAuthorizationService _warehouseAccess;

        public WarehousesController(
            IWarehouseService warehouseService,
            IWmsGovernanceOperationsService govOps,
            IWarehouseAuthorizationService warehouseAccess)
        {
            _warehouseService = warehouseService;
            _govOps = govOps;
            _warehouseAccess = warehouseAccess;
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.WarehouseRead)]
        public async Task<IActionResult> GetAll()
        {
            var warehouses = await _warehouseService.GetAllWarehousesAsync();
            return Ok(warehouses);
        }

        [HttpGet("governance-summary")]
        [PermissionAuthorize(AppPermissions.WarehouseRead)]
        public async Task<IActionResult> GetGovernanceSummary()
        {
            var list = (await _warehouseService.GetAllWarehousesAsync()).ToList();
            return Ok(new
            {
                totalWarehouses = list.Count,
                activeWarehouses = list.Count(w => w.IsActive),
                inactiveWarehouses = list.Count(w => !w.IsActive)
            });
        }

        [HttpPost("verify-topology-capacity")]
        [HttpPost("/api/v1/warehouse-locations/verify-topology-capacity")]
        [HttpPost("/api/warehouse-locations/verify-topology-capacity")]
        [PermissionAuthorize(AppPermissions.LocationManage)]
        public async Task<IActionResult> VerifyTopologyCapacity(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTopologyCapacityRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var warehouseId = request?.WarehouseId;
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null)
            {
                if (warehouseId.HasValue)
                    await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                else
                    accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            return this.ToActionResult(await _govOps.VerifyTopologyCapacityAsync(warehouseId, accessibleIds, cancellationToken));
        }

        [HttpPost("verify-virtual-locations")]
        [HttpPost("/api/v1/warehouse-locations/verify-virtual-locations")]
        [HttpPost("/api/warehouse-locations/verify-virtual-locations")]
        [PermissionAuthorize(AppPermissions.LocationManage)]
        public async Task<IActionResult> VerifyVirtualLocations(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyVirtualLocationsRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var warehouseId = request?.WarehouseId;
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null)
            {
                if (warehouseId.HasValue)
                    await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                else
                    accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            return this.ToActionResult(await _govOps.VerifyVirtualLocationsAsync(warehouseId, accessibleIds, cancellationToken));
        }

        [HttpPost("evaluate-network-mhe")]
        [PermissionAuthorize(AppPermissions.WarehouseManage)]
        public async Task<IActionResult> EvaluateNetworkMhe(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateNetworkMheRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var warehouseId = request?.WarehouseId;
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null)
            {
                if (warehouseId.HasValue)
                    await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                else
                    accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            return this.ToActionResult(await _govOps.EvaluateNetworkMheAsync(warehouseId, accessibleIds, cancellationToken));
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.WarehouseRead)]
        public async Task<IActionResult> GetById(int id)
        {
            var warehouse = await _warehouseService.GetWarehouseByIdAsync(id);
            return Ok(warehouse);
        }

        [HttpPost]
        [IdempotentCommand("Warehouse.Create")]
        [PermissionAuthorize(AppPermissions.WarehouseManage)]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            var warehouse = await _warehouseService.CreateWarehouseAsync(dto, username);
            return CreatedAtAction(nameof(GetById), new { id = warehouse.Id }, warehouse);
        }

        [HttpPut("{id}")]
        [IdempotentCommand("Warehouse.Update")]
        [PermissionAuthorize(AppPermissions.WarehouseManage)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateWarehouseDto dto)
        {
            var username = User.Identity?.Name ?? "system";
            await _warehouseService.UpdateWarehouseAsync(id, dto, username);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [IdempotentCommand("Warehouse.Delete")]
        [PermissionAuthorize(AppPermissions.WarehouseManage)]
        public async Task<IActionResult> Delete(int id)
        {
            await _warehouseService.DeleteWarehouseAsync(id);
            return NoContent();
        }
    }

    public sealed record VerifyTopologyCapacityRequest(int? WarehouseId);
    public sealed record VerifyVirtualLocationsRequest(int? WarehouseId);
    public sealed record EvaluateNetworkMheRequest(int? WarehouseId);
}
