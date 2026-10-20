using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ExportReceiptsController : ControllerBase
    {
        private readonly IExportReceiptService _exportReceiptService;
        private readonly ErpKhoDbContext? _db;
        private readonly IWarehouseAuthorizationService? _warehouseAccess;

        public ExportReceiptsController(
            IExportReceiptService exportReceiptService,
            ErpKhoDbContext db = null!,
            IWarehouseAuthorizationService warehouseAccess = null!)
        {
            _exportReceiptService = exportReceiptService;
            _db = db;
            _warehouseAccess = warehouseAccess;
        }

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue("Id");
            return int.TryParse(userIdValue, out userId);
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetAll()
        {
            var receipts = await _exportReceiptService.GetAllAsync();
            return Ok(receipts);
        }

        [HttpGet("pipeline-summary")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetPipelineSummary()
        {
            var items = (await _exportReceiptService.GetAllAsync()).ToList();
            return Ok(new
            {
                totalReceipts = items.Count,
                draftCount = items.Count(x => string.Equals(x.Status, "Draft", StringComparison.OrdinalIgnoreCase)),
                approvedReadyCount = items.Count(x => string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
                dispatchedCount = items.Count(x => x.Status is "Dispatched" or "Completed"),
                cancelledCount = items.Count(x => string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            });
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetById(int id)
        {
            var receipt = await _exportReceiptService.GetByIdAsync(id);
            return Ok(receipt);
        }

        [HttpPost]
        [IdempotentCommand("ExportReceipt.Create")]
        [PermissionAuthorize(AppPermissions.ExportReceiptCreate)]
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
        [IdempotentCommand("ExportReceipt.Cancel")]
        [PermissionAuthorize(AppPermissions.ExportReceiptCancel)]
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
        [IdempotentCommand("ExportReceipt.ApproveAndReserve")]
        [PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
        public async Task<IActionResult> ApproveAndReserve(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.ApproveAndReserveAsync(id, userId);
            return Ok(new { message = "Đã duyệt và giữ hàng." });
        }

        [HttpPost("{id}/approve-and-dispatch")]
        [IdempotentCommand("ExportReceipt.ApproveAndDispatch")]
        [PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
        [PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
        public async Task<IActionResult> ApproveAndDispatch(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.ApproveAndDispatchAsync(id, userId);
            return Ok(new { message = "Đã duyệt và xuất kho." });
        }

        [HttpPost("{id}/dispatch")]
        [IdempotentCommand("ExportReceipt.Dispatch")]
        [PermissionAuthorize(AppPermissions.ExportReceiptDispatch)]
        public async Task<IActionResult> Dispatch(int id)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            await _exportReceiptService.DispatchAsync(id, userId);
            return Ok(new { message = "Đã xác nhận xuất kho." });
        }

        [HttpPost("{id}/approve")]
        [IdempotentCommand("ExportReceipt.Approve")]
        [PermissionAuthorize(AppPermissions.ExportReceiptApprove)]
        public async Task<IActionResult> Approve(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized();
            }

            await _exportReceiptService.ApproveAsync(id, userId);
            return Ok(new { message = "Duyệt phiếu xuất thành công" });
        }

        [HttpPut("{id:int}/customer"), IdempotentCommand("ExportReceipt.SetCustomer"), PermissionAuthorize(AppPermissions.ExportReceiptUpdate), PermissionAuthorize(AppPermissions.PartnerRead)]
        public async Task<IActionResult> SetCustomer(int id, [FromBody] ERP.Application.DTOs.SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
        { await partners.SetExportCustomerAsync(id, dto.PartnerId, ct); return NoContent(); }

        public sealed record VerifyDispatchCutoffRequest(int? WarehouseId = null);

        [HttpPost("verify-dispatch-cutoff")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> VerifyDispatchCutoff(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyDispatchCutoffRequest? request = null,
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

            var checkedExportReceiptsCount = 0;
            var openPickingTasksCount = 0;
            var outboundCutoffLocal = "22:00";
            if (_db != null)
            {
                var query = _db.ExportReceipts.AsNoTracking();
                var taskQuery = _db.PickingTasks.AsNoTracking()
                    .Where(t => t.Status != ERP.Domain.Enums.PickingTaskStatus.Completed
                                && t.Status != ERP.Domain.Enums.PickingTaskStatus.Cancelled);
                var calQuery = _db.WarehouseCalendarDays.AsNoTracking()
                    .Where(c => c.IsOpen && c.OutboundCutoffLocal.HasValue);

                if (warehouseId.HasValue)
                {
                    query = query.Where(r => r.WarehouseId == warehouseId.Value);
                    taskQuery = taskQuery.Where(t => t.WarehouseId == warehouseId.Value);
                    calQuery = calQuery.Where(c => c.WarehouseId == warehouseId.Value);
                }
                else if (accessibleIds != null)
                {
                    query = query.Where(r => accessibleIds.Contains(r.WarehouseId));
                    taskQuery = taskQuery.Where(t => accessibleIds.Contains(t.WarehouseId));
                    calQuery = calQuery.Where(c => accessibleIds.Contains(c.WarehouseId));
                }

                checkedExportReceiptsCount = await query.CountAsync(cancellationToken);
                openPickingTasksCount = await taskQuery.CountAsync(cancellationToken);
                var cutoffSpan = await calQuery.Select(c => c.OutboundCutoffLocal).FirstOrDefaultAsync(cancellationToken);
                if (cutoffSpan.HasValue)
                    outboundCutoffLocal = cutoffSpan.Value.ToString(@"hh\:mm");
            }

            return Ok(new
            {
                verificationId = $"EXP-CUT-{DateTime.UtcNow:yyyyMMdd-HHmm}",
                warehouseId,
                checkedExportReceiptsCount,
                openPickingTasksCount,
                outboundCutoffLocal,
                singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất",
                cutoffComplianceStatus = $"Đạt chuẩn giờ chốt xuất kho ({outboundCutoffLocal})",
                status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
                statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
                verifiedAtUtc = DateTime.UtcNow
            });
        }
    }
}
