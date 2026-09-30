using System.Security.Claims;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ImportReceiptsController : ControllerBase
    {
        private readonly IImportReceiptService _importReceiptService;
        private readonly IReceivingDiscrepancyService? _discrepancies;
        private IReceivingDiscrepancyService Discrepancies => _discrepancies
            ?? throw new InvalidOperationException("Receiving discrepancy service is not configured.");

        public ImportReceiptsController(IImportReceiptService importReceiptService, IReceivingDiscrepancyService? discrepancies = null)
        {
            _importReceiptService = importReceiptService;
            _discrepancies = discrepancies;
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
        [PermissionAuthorize(AppPermissions.ReceiptCreate)]
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
        [PermissionAuthorize(AppPermissions.ReceiptComplete)]
        public async Task<IActionResult> Approve(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            await _importReceiptService.ApproveImportReceiptAsync(id, userId);
            return Ok(new { message = "Phiếu nhập đã sẵn sàng post" });
        }

        [HttpPost("{id}/receive")]
        [IdempotentCommand("ImportReceipt.Receive")]
        [PermissionAuthorize(AppPermissions.ReceiptReceive)]
        public async Task<IActionResult> Receive(int id, [FromBody] ERP.Application.DTOs.ReceiveImportReceiptDto dto)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            await _importReceiptService.ReceiveAsync(id, dto, userId);
            return Ok(new { message = "Hoàn tất nhận hàng; tồn kho chưa thay đổi" });
        }

        [HttpPost("{id}/qc-disposition")]
        [IdempotentCommand("ImportReceipt.QcDisposition")]
        [PermissionAuthorize(AppPermissions.QualityExecute)]
        public async Task<IActionResult> RecordQcDisposition(int id, [FromBody] ERP.Application.DTOs.RecordQcDispositionDto dto)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            await _importReceiptService.RecordQcDispositionAsync(id, dto, userId);
            return Ok(new { message = "Đã ghi nhận disposition QC; tồn kho chưa thay đổi" });
        }

        [HttpPost("{id}/post")]
        [IdempotentCommand("ImportReceipt.Post")]
        [PermissionAuthorize(AppPermissions.ReceiptPost)]
        public async Task<IActionResult> Post(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            await _importReceiptService.PostAsync(id, userId);
            return Ok(new { message = "Post phiếu nhập thành công" });
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> GetAll([FromQuery] ERP.Domain.Enums.ReceiptStatus? status)
        {
            var result = await _importReceiptService.GetAllAsync(status);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _importReceiptService.GetByIdAsync(id);
            return Ok(result);
        }

        [HttpGet("{id:int}/discrepancies"), PermissionAuthorize(AppPermissions.DiscrepancyRead)]
        public async Task<IActionResult> GetDiscrepancies(int id, CancellationToken token) => Ok(await Discrepancies.GetAsync(id, token));

        [HttpGet("discrepancy-reasons"), PermissionAuthorize(AppPermissions.ReasonRead)]
        public async Task<IActionResult> GetDiscrepancyReasons(CancellationToken token) => Ok(await Discrepancies.GetActiveReasonsAsync(token));

        [HttpPost("{id:int}/discrepancies/observe")]
        [IdempotentCommand("ImportReceipt.Discrepancy.Observe")]
        [PermissionAuthorize(AppPermissions.DiscrepancyCreate)]
        public async Task<IActionResult> Observe(int id, [FromBody] ERP.Application.DTOs.ObserveReceivingDto dto, CancellationToken token)
            => Ok(await Discrepancies.ObserveAsync(id, dto, token));

        [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/submit")]
        [IdempotentCommand("ImportReceipt.Discrepancy.Submit")]
        [PermissionAuthorize(AppPermissions.DiscrepancySubmit)]
        public async Task<IActionResult> SubmitDiscrepancy(int id, int discrepancyId, [FromBody] ERP.Application.DTOs.SubmitReceivingDiscrepancyDto dto, CancellationToken token)
            => Ok(await Discrepancies.SubmitAsync(id, discrepancyId, dto, token));

        [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/approve")]
        [IdempotentCommand("ImportReceipt.Discrepancy.Approve")]
        [PermissionAuthorize(AppPermissions.DiscrepancyApprove)]
        public async Task<IActionResult> ApproveDiscrepancy(int id, int discrepancyId, [FromBody] byte[] rowVersion, CancellationToken token)
            => Ok(await Discrepancies.ApproveAsync(id, discrepancyId, rowVersion, token));

        [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/reject")]
        [IdempotentCommand("ImportReceipt.Discrepancy.Reject")]
        [PermissionAuthorize(AppPermissions.DiscrepancyReject)]
        public async Task<IActionResult> RejectDiscrepancy(int id, int discrepancyId, [FromBody] byte[] rowVersion, CancellationToken token)
            => Ok(await Discrepancies.RejectAsync(id, discrepancyId, rowVersion, token));

        [HttpPost("{id:int}/discrepancies/{discrepancyId:int}/recount")]
        [IdempotentCommand("ImportReceipt.Discrepancy.Recount")]
        [PermissionAuthorize(AppPermissions.DiscrepancyResolve)]
        public async Task<IActionResult> RecountDiscrepancy(int id, int discrepancyId, [FromBody] ERP.Application.DTOs.RecountReceivingDto dto, CancellationToken token)
            => Ok(await Discrepancies.RecountAsync(id, discrepancyId, dto, token));

        [HttpPut("{id}/cancel")]
        [IdempotentCommand("ImportReceipt.Cancel")]
        [PermissionAuthorize(AppPermissions.ReceiptCancel)]
        public async Task<IActionResult> Cancel(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            await _importReceiptService.CancelAsync(id, userId);
            return Ok(new { message = "Hủy phiếu nhập thành công" });
        }

        [HttpPut("{id:int}/supplier"), PermissionAuthorize(AppPermissions.ReceiptUpdate)]
        public async Task<IActionResult> SetSupplier(int id, [FromBody] ERP.Application.DTOs.SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
        { await partners.SetImportSupplierAsync(id, dto.PartnerId, ct); return NoContent(); }
    }
}
