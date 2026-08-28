using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AllRoles)]
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryStocksController : ControllerBase
    {
        private readonly IInventoryQueryService _inventoryQueryService;

        public InventoryStocksController(IInventoryQueryService inventoryQueryService)
        {
            _inventoryQueryService = inventoryQueryService;
        }

        [HttpGet("current")]
        public async Task<ActionResult<IEnumerable<InventoryStockDto>>> GetCurrentStock(
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId, 
            [FromQuery] string? keyword, 
            [FromQuery] decimal? lowStockThreshold)
        {
            var stocks = await _inventoryQueryService.GetCurrentStockAsync(warehouseId, productId, keyword, lowStockThreshold);
            return Ok(stocks);
        }
    }
}
