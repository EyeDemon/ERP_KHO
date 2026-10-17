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

        [HttpGet("reason-codes-and-adjustments")]
        public IActionResult GetReasonCodesAndAdjustments()
        {
            return Ok(new
            {
                reasonCodes = new object[]
                {
                    new { code = "COUNT_VARIANCE", name = "Chênh lệch kiểm đếm định kỳ", category = "CycleCount", requiresApproval = true },
                    new { code = "DAMAGED_FOUND", name = "Phát hiện hàng hư hỏng vật lý", category = "Quality", requiresApproval = true },
                    new { code = "EXPIRY_WRITE_OFF", name = "Hủy hàng hết hạn sử dụng", category = "Expiry", requiresApproval = true },
                    new { code = "SUPPLIER_SHORTAGE", name = "Thiếu hụt nguồn giao từ NCC", category = "Inbound", requiresApproval = true },
                    new { code = "MISPLACED_STOCK", name = "Sai lệch vị trí lưu kho nội bộ", category = "Slotting", requiresApproval = false },
                    new { code = "RECALL_QUARANTINE", name = "Cô lập thu hồi lô hàng khẩn cấp", category = "Recall", requiresApproval = true }
                },
                adjustmentGovernance = new
                {
                    sodRequired = true,
                    approvalThresholdPct = 2.0m,
                    supportedTransactionTypes = new[] { "AdjustmentIncrease", "AdjustmentDecrease" }
                }
            });
        }

        public sealed record CreateAdjustmentProposalRequest(
            int WarehouseId,
            string ProductCode,
            decimal DeltaQuantity,
            string ReasonCode,
            string? Note);

        [HttpPost("adjustment-proposals")]
        public IActionResult CreateAdjustmentProposal([FromBody] CreateAdjustmentProposalRequest request)
        {
            return Ok(new
            {
                proposalCode = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Math.Abs((request.ProductCode ?? string.Empty).GetHashCode()) % 1000:D3}",
                warehouseId = request.WarehouseId,
                productCode = request.ProductCode,
                deltaQuantity = request.DeltaQuantity,
                reasonCode = request.ReasonCode,
                status = "PENDING_SOD_APPROVAL",
                createdAtUtc = DateTime.UtcNow
            });
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




