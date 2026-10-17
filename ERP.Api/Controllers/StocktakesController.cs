using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using System.Globalization;
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
        private readonly ErpKhoDbContext? _db;
        private readonly IWarehouseAuthorizationService? _warehouseAccess;

        public StocktakesController(
            IStocktakeService stocktakeService,
            IStocktakeQueryService stocktakeQueryService,
            ErpKhoDbContext db = null!,
            IWarehouseAuthorizationService warehouseAccess = null!)
        {
            _stocktakeService = stocktakeService;
            _stocktakeQueryService = stocktakeQueryService;
            _db = db;
            _warehouseAccess = warehouseAccess;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var stocktakes = await _stocktakeQueryService.GetStocktakesAsync();
            return Ok(stocktakes);
        }

        [HttpGet("reason-codes-and-adjustments")]
        public async Task<IActionResult> GetReasonCodesAndAdjustments(
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null)
            {
                if (warehouseId.HasValue)
                    await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                else
                    accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            var recentProposals = new List<object>();
            if (_db != null)
            {
                var query = _db.AuditLogs.AsNoTracking()
                    .Where(a => a.EntityName == "StocktakeAdjustmentProposal");
                if (warehouseId.HasValue)
                    query = query.Where(a => a.WarehouseId == warehouseId.Value);
                else if (accessibleIds != null)
                    query = query.Where(a => a.WarehouseId.HasValue && accessibleIds.Contains(a.WarehouseId.Value));

                var logs = await query
                    .OrderByDescending(a => a.Timestamp)
                    .ThenByDescending(a => a.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken);

                foreach (var a in logs)
                {
                    decimal.TryParse(a.OldValues, NumberStyles.Any, CultureInfo.InvariantCulture, out var deltaQty);
                    recentProposals.Add(new
                    {
                        id = a.Id,
                        proposalCode = a.NewValues ?? $"ADJ-{a.Timestamp:yyyyMMdd}-{a.Id:D3}",
                        warehouseId = a.WarehouseId ?? 0,
                        productId = a.EntityId,
                        productCode = !string.IsNullOrWhiteSpace(a.CorrelationId)
                            ? a.CorrelationId
                            : (a.EntityId.HasValue ? $"SKU-{a.EntityId.Value}" : "SKU-CYCLE-01"),
                        deltaQuantity = deltaQty,
                        reasonCode = a.Action,
                        note = a.Reason,
                        status = string.IsNullOrWhiteSpace(a.Result) ? "PENDING_SOD_APPROVAL" : a.Result,
                        createdAtUtc = a.Timestamp
                    });
                }
            }

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
                },
                recentProposals
            });
        }

        public sealed record CreateAdjustmentProposalRequest(
            int WarehouseId,
            string? ProductCode = null,
            decimal DeltaQuantity = 0m,
            string ReasonCode = "COUNT_VARIANCE",
            string? Note = null,
            int? ProductId = null);

        [HttpPost("adjustment-proposals")]
        [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
        public async Task<IActionResult> CreateAdjustmentProposal(
            [FromBody] CreateAdjustmentProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_warehouseAccess != null)
                await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);

            var reasonCode = string.IsNullOrWhiteSpace(request.ReasonCode)
                ? "COUNT_VARIANCE"
                : request.ReasonCode.Trim().ToUpperInvariant();
            var productCode = request.ProductCode?.Trim() ?? string.Empty;
            var resolvedProductId = request.ProductId;

            if (_db != null)
            {
                if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(productCode))
                {
                    resolvedProductId = await _db.Products.AsNoTracking()
                        .Where(p => p.Code == productCode)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }
                else if (resolvedProductId.HasValue && string.IsNullOrWhiteSpace(productCode))
                {
                    productCode = await _db.Products.AsNoTracking()
                        .Where(p => p.Id == resolvedProductId.Value)
                        .Select(p => p.Code)
                        .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
                }
            }

            var now = DateTime.UtcNow;
            var seedKey = !string.IsNullOrWhiteSpace(productCode)
                ? productCode
                : (resolvedProductId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            var proposalCode = $"ADJ-{now:yyyyMMdd}-{Math.Abs(seedKey.GetHashCode()) % 1000:D3}";
            var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
            int? userId = int.TryParse(userIdStr, out var parsedUserId) ? parsedUserId : null;

            if (_db != null)
            {
                var log = new AuditLog
                {
                    UserId = userId,
                    EntityName = "StocktakeAdjustmentProposal",
                    EntityId = resolvedProductId,
                    WarehouseId = request.WarehouseId,
                    Action = reasonCode,
                    OldValues = request.DeltaQuantity.ToString(CultureInfo.InvariantCulture),
                    NewValues = proposalCode,
                    Reason = request.Note?.Trim() ?? reasonCode,
                    CorrelationId = string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                    Result = "PENDING_SOD_APPROVAL",
                    Timestamp = now
                };
                _db.AuditLogs.Add(log);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                proposalCode,
                warehouseId = request.WarehouseId,
                productId = resolvedProductId,
                productCode,
                deltaQuantity = request.DeltaQuantity,
                reasonCode,
                status = "PENDING_SOD_APPROVAL",
                createdAtUtc = now
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




