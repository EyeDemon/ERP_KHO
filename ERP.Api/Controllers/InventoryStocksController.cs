using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryStocksController : ControllerBase
    {
        private readonly IInventoryQueryService _inventoryQueryService;
        private readonly IStockReservationService? _reservationService;

        public InventoryStocksController(IInventoryQueryService inventoryQueryService, IStockReservationService? reservationService = null)
        {
            _inventoryQueryService = inventoryQueryService;
            _reservationService = reservationService;
        }

        [HttpGet("current")]
        public async Task<ActionResult<IEnumerable<InventoryStockDto>>> GetCurrentStock(
            [FromQuery] int? warehouseId, 
            [FromQuery] int? productId, 
            [FromQuery] string? keyword, 
            [FromQuery] decimal? lowStockThreshold)
        {
            if (_reservationService is not null && (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Manager) || User.IsInRole(AppRoles.WarehouseStaff)))
                await _reservationService.ExpireAsync(HttpContext.RequestAborted);
            var stocks = await _inventoryQueryService.GetCurrentStockAsync(warehouseId, productId, keyword, lowStockThreshold);
            return Ok(stocks);
        }
    }
}
