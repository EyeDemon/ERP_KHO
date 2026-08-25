using ERP.Application.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AllRoles)]
    [ApiController]
    [Route("api/[controller]")]
    public class ExportReceiptsController : ControllerBase
    {
        private readonly IExportReceiptService _exportReceiptService;

        public ExportReceiptsController(IExportReceiptService exportReceiptService)
        {
            _exportReceiptService = exportReceiptService;
        }

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue("Id");
            return int.TryParse(userIdValue, out userId);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var receipts = await _exportReceiptService.GetAllAsync();
            return Ok(receipts);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var receipt = await _exportReceiptService.GetByIdAsync(id);
            return Ok(receipt);
        }

        [HttpPost]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> Create([FromBody] ERP.Application.DTOs.CreateExportReceiptDto dto)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            try
            {
                var receipt = await _exportReceiptService.CreateAsync(dto, userId);
                return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, receipt);
            }
            catch (ERP.Application.Exceptions.BusinessRuleException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> Cancel(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            await _exportReceiptService.CancelAsync(id, userId);
            return Ok(new { message = "Hủy phiếu xuất thành công" });
        }

        [HttpPost("{id}/approve-and-reserve")]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> ApproveAndReserve(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.ApproveAndReserveAsync(id, userId);
            return Ok(new { message = "Đã duyệt và giữ hàng." });
        }

        [HttpPost("{id}/approve-and-dispatch")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> ApproveAndDispatch(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.ApproveAndDispatchAsync(id, userId);
            return Ok(new { message = "Đã duyệt và xuất kho." });
        }

        [HttpPost("{id}/dispatch")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> Dispatch(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.DispatchAsync(id, userId);
            return Ok(new { message = "Đã xác nhận xuất kho." });
        }

        [HttpPost("{id}/approve")]
        [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
        public async Task<IActionResult> Approve(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized();
            }

            await _exportReceiptService.ApproveAsync(id, userId);
            return Ok(new { message = "Duyệt phiếu xuất thành công" });
        }
    }
}
