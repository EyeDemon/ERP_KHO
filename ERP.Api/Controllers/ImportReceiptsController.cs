using System.Security.Claims;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;

namespace ERP.Api.Controllers
{
    [Authorize(Roles = AppRoles.AdminManagerOrViewer)]
    [ApiController]
    [Route("api/[controller]")]
    public class ImportReceiptsController : ControllerBase
    {
        private readonly IImportReceiptService _importReceiptService;

        public ImportReceiptsController(IImportReceiptService importReceiptService)
        {
            _importReceiptService = importReceiptService;
        }

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            var userIdStr = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                            ?? User.Claims.FirstOrDefault(c => c.Type == "Id")?.Value;
            
            return !string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out userId);
        }

        [HttpPost]
        [IdempotentCommand("ImportReceipt.Create")]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> Create([FromBody] ERP.Application.DTOs.CreateImportReceiptDto dto)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            var result = await _importReceiptService.CreateAsync(dto, userId);
            return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
        }

        [HttpPost("{id}/approve")]
        [IdempotentCommand("ImportReceipt.Approve")]
        [Authorize(Policy = ApprovalPolicies.Checker)]
        public async Task<IActionResult> Approve(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            await _importReceiptService.ApproveImportReceiptAsync(id, userId);
            return Ok(new { message = "Duyệt phiếu nhập thành công" });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ERP.Domain.Enums.ReceiptStatus? status)
        {
            var result = await _importReceiptService.GetAllAsync(status);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _importReceiptService.GetByIdAsync(id);
            return Ok(result);
        }

        [HttpPut("{id}/cancel")]
        [IdempotentCommand("ImportReceipt.Cancel")]
        [Authorize(Roles = AppRoles.AdminOrManager)]
        public async Task<IActionResult> Cancel(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            await _importReceiptService.CancelAsync(id, userId);
            return Ok(new { message = "Hủy phiếu nhập thành công" });
        }
    }
}
