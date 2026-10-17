using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using System.Security.Claims;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [ApiController]
    [Route("api/[controller]")]
    public class StocktakesController : ControllerBase
    {
        private readonly IStocktakeService _stocktakeService;
        private readonly IStocktakeQueryService _stocktakeQueryService;

        public StocktakesController(IStocktakeService stocktakeService, IStocktakeQueryService stocktakeQueryService)
        {
            _stocktakeService = stocktakeService;
            _stocktakeQueryService = stocktakeQueryService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var stocktakes = await _stocktakeQueryService.GetStocktakesAsync();
            return Ok(stocktakes);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var stocktake = await _stocktakeQueryService.GetStocktakeByIdAsync(id);
            return Ok(stocktake);
        }

        [HttpPost]
        [IdempotentCommand("Stocktake.Create")]
        public async Task<IActionResult> Create([FromBody] CreateStocktakeDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng" });

            var id = await _stocktakeService.CreateStocktakeAsync(dto, userId);
            return Ok(new { message = "Tạo phiếu kiểm kê thành công", id });
        }
        [HttpPost("{id}/approve")]
        [IdempotentCommand("Stocktake.Approve")]
        [Authorize(Policy = ApprovalPolicies.Checker)]
        public async Task<IActionResult> Approve(int id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng" });

            await _stocktakeService.ApproveStocktakeAsync(id, userId);
            return Ok(new { message = "Duyệt phiếu kiểm kê thành công" });
        }
        [HttpPut("{id}/details/{detailId}")]
        [IdempotentCommand("Stocktake.UpdateDetail")]
        public async Task<IActionResult> UpdateDetail(int id, int detailId, [FromBody] UpdateStocktakeDetailDto dto)
        {
            await _stocktakeService.UpdateStocktakeDetailAsync(id, detailId, dto);
            return Ok(new { message = "Cập nhật chi tiết kiểm kê thành công" });
        }

        [HttpPost("{id:int}/details/{detailId:int}/recount")]
        [Authorize(Roles = "Admin,Manager")]
        [IdempotentCommand("Stocktake.Recount")]
        public async Task<IActionResult> Recount(
            int id,
            int detailId,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RecountStocktakeDetailDto? dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Id");
            int.TryParse(userIdStr, out var userId);

            await _stocktakeService.RecountStocktakeDetailAsync(id, detailId, dto?.Reason, userId);
            return Ok(new { message = "Đã yêu cầu kiểm đếm lại dòng kiểm kê" });
        }
    }
}




