using System.Threading.Tasks;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Manager,Viewer")]
    public class InventoryReconciliationController : ControllerBase
    {
        private readonly IInventoryReconciliationQueryService _queryService;

        public InventoryReconciliationController(IInventoryReconciliationQueryService queryService)
        {
            _queryService = queryService;
        }

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
