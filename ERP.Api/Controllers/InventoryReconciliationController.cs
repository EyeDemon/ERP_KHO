using System.Threading.Tasks;
using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [PermissionAuthorize(AppPermissions.InventoryLedgerRead)]
    public class InventoryReconciliationController : ControllerBase
    {
        private readonly IInventoryReconciliationQueryService _queryService;

        public InventoryReconciliationController(IInventoryReconciliationQueryService queryService)
        {
            _queryService = queryService;
        }

        [HttpGet("warehouses")]
        public async Task<ActionResult<IReadOnlyList<InventoryReconciliationWarehouseDto>>> Warehouses()
            => Ok(await _queryService.GetAccessibleWarehousesAsync());

        // Explicit warehouse and product identity is mandatory. A broad
        // unscoped evidence download is intentionally not exposed.
        [HttpGet("investigation")]
        public async Task<ActionResult<InventoryReconciliationInvestigationDto>> Investigation(
            [FromQuery] int warehouseId, [FromQuery] int productId,
            [FromQuery] int? eventAnchorId = null, [FromQuery] int limit = 50,
            [FromQuery] int? eventBeforeId = null, [FromQuery] int? bucketAnchorId = null,
            [FromQuery] int? bucketAfterId = null, [FromQuery] string bucketStatus = "Available")
            => Ok(await _queryService.GetInvestigationAsync(warehouseId, productId, eventAnchorId, limit,
                eventBeforeId, bucketAnchorId, bucketAfterId, bucketStatus));

        [HttpGet]
        public async Task<IActionResult> GetReconciliations(
            [FromQuery] int? warehouseId,
            [FromQuery] int? productId,
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _queryService.GetReconciliationsAsync(
                warehouseId,
                productId,
                keyword,
                page,
                pageSize);

            return Ok(result);
        }
    }
}
