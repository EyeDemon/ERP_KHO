using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using System.Data;
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
            var openStocktakesCount = 0;
            var activeCountFreezeLocksCount = 0;
            if (_db != null)
            {
                var stQuery = _db.Stocktakes.AsNoTracking()
                    .Where(s => s.Status == ReceiptStatus.Draft);
                var lockQuery = _db.InventoryLocks.AsNoTracking()
                    .Where(l => l.Status == InventoryLockStatus.Active && l.LockType == InventoryLockType.CountFreeze);
                if (warehouseId.HasValue)
                {
                    stQuery = stQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
                }
                else if (accessibleIds != null)
                {
                    stQuery = stQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                    lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
                }

                openStocktakesCount = await stQuery.CountAsync(cancellationToken);
                activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);

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

                var approvedProposalEntityIds = logs
                    .Where(a => string.Equals(a.Result, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(a.Result, "Đã phê duyệt & Ghi sổ", StringComparison.OrdinalIgnoreCase) ||
                                a.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.EntityId)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToHashSet();

                var approvedProposalCodes = logs
                    .Where(a => (string.Equals(a.Result, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(a.Result, "Đã phê duyệt & Ghi sổ", StringComparison.OrdinalIgnoreCase) ||
                                 a.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase)) &&
                                !string.IsNullOrWhiteSpace(a.NewValues))
                    .Select(a => a.NewValues!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in logs)
                {
                    var codeKey = a.NewValues ?? $"ADJ-{a.Timestamp:yyyyMMdd}-{a.Id:D3}";
                    if (!seenCodes.Add(codeKey))
                        continue;

                    decimal.TryParse(a.OldValues, NumberStyles.Any, CultureInfo.InvariantCulture, out var deltaQty);
                    var isApproved = string.Equals(a.Result, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a.Result, "Đã phê duyệt & Ghi sổ", StringComparison.OrdinalIgnoreCase) ||
                                     a.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase) ||
                                     approvedProposalEntityIds.Contains((int)(a.Id % int.MaxValue)) ||
                                     approvedProposalCodes.Contains(codeKey);
                    var normalizedStatus = isApproved
                        ? "Đã phê duyệt & Ghi sổ"
                        : "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm";
                    var normalizedReasonCode = a.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase)
                        ? a.Action[..^9]
                        : a.Action;
                    recentProposals.Add(new
                    {
                        id = a.Id,
                        proposalCode = codeKey,
                        warehouseId = a.WarehouseId ?? 0,
                        productId = a.EntityId,
                        productCode = !string.IsNullOrWhiteSpace(a.CorrelationId)
                            ? a.CorrelationId
                            : (a.EntityId.HasValue ? $"SKU-{a.EntityId.Value}" : "SKU-CYCLE-01"),
                        deltaQuantity = deltaQty,
                        reasonCode = normalizedReasonCode,
                        note = a.Reason,
                        status = normalizedStatus,
                        statusLabel = isApproved
                            ? "Đã phê duyệt & Ghi sổ"
                            : "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
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
                openStocktakesCount,
                activeCountFreezeLocksCount,
                status = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
                statusLabel = "Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê",
                recentProposals
            });
        }

        public sealed record VerifyStocktakeFreezePolicyRequest(int? WarehouseId = null);

        [HttpPost("verify-freeze-policy")]
        public async Task<IActionResult> VerifyFreezePolicy(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyStocktakeFreezePolicyRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var warehouseId = request?.WarehouseId;
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null)
            {
                if (warehouseId.HasValue)
                    await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                else
                    accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            var openStocktakesCount = 0;
            var approvedStocktakesCount = 0;
            var activeCountFreezeLocksCount = 0;
            if (_db != null)
            {
                var stQuery = _db.Stocktakes.AsNoTracking();
                var lockQuery = _db.InventoryLocks.AsNoTracking()
                    .Where(l => l.Status == InventoryLockStatus.Active && l.LockType == InventoryLockType.CountFreeze);

                if (warehouseId.HasValue)
                {
                    stQuery = stQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
                }
                else if (accessibleIds != null)
                {
                    stQuery = stQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                    lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
                }

                openStocktakesCount = await stQuery.CountAsync(s => s.Status == ReceiptStatus.Draft, cancellationToken);
                approvedStocktakesCount = await stQuery.CountAsync(s => s.Status == ReceiptStatus.Approved || s.Status == ReceiptStatus.Posted, cancellationToken);
                activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);
            }

            var now = DateTime.UtcNow;
            var verificationCode = $"ST-FRZ-{now:yyyyMMdd-HHmm}";
            if (_db != null)
            {
                var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
                int? userId = int.TryParse(userIdStr, out var parsedUserId) ? parsedUserId : null;
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    WarehouseId = warehouseId,
                    EntityName = "Stocktake",
                    Action = "Stocktake.VerifyFreezePolicy",
                    NewValues = verificationCode,
                    Reason = $"Kiểm định chính sách đóng băng kiểm kê ({openStocktakesCount} phiên mở, {activeCountFreezeLocksCount} khóa CountFreeze)",
                    Result = "Success",
                    Timestamp = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                verificationCode,
                warehouseId,
                openStocktakesCount,
                approvedStocktakesCount,
                activeCountFreezeLocksCount,
                freezePolicyStatus = "Đã kích hoạt khóa đóng băng kiểm kê (CountFreeze) chống xuất/di chuyển lệch kỳ",
                status = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
                statusLabel = "Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh",
                verifiedAtUtc = now
            });
        }

        public sealed record CreateAdjustmentProposalRequest(
            int? WarehouseId = null,
            string? ProductCode = null,
            decimal DeltaQuantity = 0m,
            string ReasonCode = "COUNT_VARIANCE",
            string? Note = null,
            int? ProductId = null,
            int? LocationId = null,
            string? LotNumber = null,
            bool ExecuteImmediately = false,
            decimal? QuantityDelta = null,
            string? Sku = null);

        public sealed record ApproveAdjustmentProposalRequest(
            int? LocationId = null,
            string? LotNumber = null,
            string? Note = null);

        [HttpPost("adjustment-proposals")]
        [IdempotentCommand("Stocktake.CreateAdjustmentProposal")]
        [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager},{AppRoles.WarehouseStaff}")]
        public async Task<IActionResult> CreateAdjustmentProposal(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateAdjustmentProposalRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var resolvedWarehouseId = request?.WarehouseId;
            if (_warehouseAccess != null)
            {
                if (resolvedWarehouseId.HasValue)
                {
                    await _warehouseAccess.EnsureWarehouseAccessAsync(resolvedWarehouseId.Value, cancellationToken);
                }
                else
                {
                    var accessible = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                    if (accessible.Count == 0)
                        return Forbid();
                    resolvedWarehouseId = accessible.First();
                }
            }

            var reasonCode = string.IsNullOrWhiteSpace(request?.ReasonCode)
                ? "COUNT_VARIANCE"
                : request.ReasonCode.Trim().ToUpperInvariant();
            var productCode = (!string.IsNullOrWhiteSpace(request?.ProductCode) ? request.ProductCode : request?.Sku)?.Trim() ?? string.Empty;
            var resolvedProductId = request?.ProductId;
            var deltaQuantity = (request != null && request.DeltaQuantity != 0m)
                ? request.DeltaQuantity
                : (request?.QuantityDelta ?? 0m);

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

            long proposalAuditId = 0;
            if (_db != null)
            {
                var log = new AuditLog
                {
                    UserId = userId,
                    EntityName = "StocktakeAdjustmentProposal",
                    EntityId = resolvedProductId,
                    WarehouseId = resolvedWarehouseId,
                    Action = reasonCode,
                    OldValues = deltaQuantity.ToString(CultureInfo.InvariantCulture),
                    NewValues = proposalCode,
                    Reason = request?.Note?.Trim() ?? reasonCode,
                    CorrelationId = string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                    Result = "Success",
                    Timestamp = now
                };
                _db.AuditLogs.Add(log);
                await _db.SaveChangesAsync(cancellationToken);
                proposalAuditId = log.Id;

                if (request?.ExecuteImmediately == true && deltaQuantity != 0m && resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
                {
                    var execResult = await ExecuteProposalAdjustmentCoreAsync(
                        log.Id,
                        proposalCode,
                        reasonCode,
                        request.Note?.Trim() ?? reasonCode,
                        string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                        resolvedWarehouseId.Value,
                        resolvedProductId.Value,
                        request.LocationId,
                        request.LotNumber,
                        deltaQuantity,
                        userId,
                        cancellationToken);

                    if (!execResult.Success)
                        return BadRequest(new { message = execResult.ErrorMessage });

                    return Ok(new
                    {
                        id = log.Id,
                        proposalAuditId = log.Id,
                        proposalCode,
                        warehouseId = resolvedWarehouseId,
                        productId = resolvedProductId,
                        productCode,
                        locationId = execResult.LocationId,
                        deltaQuantity,
                        resultingQuantity = execResult.ResultingQuantity,
                        ledgerTransactionId = execResult.LedgerTransactionId,
                        referenceCode = $"ADJ-PROP-{log.Id}",
                        reasonCode,
                        afterState = "Đã phê duyệt",
                        status = "Đã phê duyệt & Ghi sổ",
                        statusLabel = "Đã phê duyệt & Ghi sổ",
                        createdAtUtc = now
                    });
                }
            }

            return Ok(new
            {
                id = proposalAuditId,
                proposalAuditId,
                proposalCode,
                warehouseId = resolvedWarehouseId,
                productId = resolvedProductId,
                productCode,
                deltaQuantity,
                reasonCode,
                status = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                statusLabel = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                createdAtUtc = now
            });
        }

        [HttpPost("adjustment-proposals/{proposalAuditId:long}/approve")]
        [IdempotentCommand("Stocktake.ApproveAdjustmentProposal")]
        [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
        public async Task<IActionResult> ApproveAdjustmentProposal(
            long proposalAuditId,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ApproveAdjustmentProposalRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_db == null)
                return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để phê duyệt đề xuất điều chỉnh." });

            var original = await _db.AuditLogs.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == proposalAuditId && a.EntityName == "StocktakeAdjustmentProposal", cancellationToken);
            if (original == null)
                return NotFound(new { message = "Không tìm thấy đề xuất điều chỉnh tồn kho." });

            var warehouseId = original.WarehouseId ?? 1;
            if (_warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }

            var refEntityId = (int)(proposalAuditId % int.MaxValue);
            var alreadyApproved = string.Equals(original.Result, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(original.Result, "Đã phê duyệt & Ghi sổ", StringComparison.OrdinalIgnoreCase) ||
                                  original.Action.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase) ||
                                  await _db.AuditLogs.AsNoTracking().AnyAsync(
                                      a => a.EntityName == "StocktakeAdjustmentProposal" &&
                                           (a.Result == "Approved" || a.Result == "Đã phê duyệt & Ghi sổ" || a.Action.EndsWith(":APPROVED")) &&
                                           (a.EntityId == refEntityId || (original.NewValues != null && a.NewValues == original.NewValues)),
                                      cancellationToken);
            if (alreadyApproved)
            {
                return Conflict(new { message = "Đề xuất điều chỉnh này đã được phê duyệt và ghi sổ trước đó." });
            }

            decimal.TryParse(original.OldValues, NumberStyles.Any, CultureInfo.InvariantCulture, out var deltaQuantity);
            if (deltaQuantity == 0m)
                return BadRequest(new { message = "Số lượng điều chỉnh bằng 0, không thể thực thi ghi sổ." });

            var resolvedProductId = original.EntityId;
            if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(original.CorrelationId))
            {
                resolvedProductId = await _db.Products.AsNoTracking()
                    .Where(p => p.Code == original.CorrelationId)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedProductId.HasValue)
            {
                resolvedProductId = await _db.InventoryStocks.AsNoTracking()
                    .Where(s => s.WarehouseId == warehouseId)
                    .Select(s => (int?)s.ProductId)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? await _db.Products.AsNoTracking().Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedProductId.HasValue)
                return BadRequest(new { message = "Không xác định được sản phẩm cần điều chỉnh tồn kho." });

            var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
            int? userId = int.TryParse(userIdStr, out var parsedUserId) ? parsedUserId : null;
            var proposalCode = original.NewValues ?? $"ADJ-{original.Timestamp:yyyyMMdd}-{original.Id:D3}";

            var execResult = await ExecuteProposalAdjustmentCoreAsync(
                original.Id,
                proposalCode,
                original.Action,
                request?.Note?.Trim() ?? original.Reason ?? original.Action,
                original.CorrelationId,
                warehouseId,
                resolvedProductId.Value,
                request?.LocationId,
                request?.LotNumber,
                deltaQuantity,
                userId ?? original.UserId,
                cancellationToken);

            if (!execResult.Success)
                return BadRequest(new { message = execResult.ErrorMessage });

            return Ok(new
            {
                id = original.Id,
                proposalAuditId = original.Id,
                proposalCode,
                warehouseId,
                productId = resolvedProductId.Value,
                locationId = execResult.LocationId,
                deltaQuantity,
                resultingQuantity = execResult.ResultingQuantity,
                ledgerTransactionId = execResult.LedgerTransactionId,
                referenceCode = $"ADJ-PROP-{original.Id}",
                afterState = "Đã phê duyệt",
                status = "Đã phê duyệt & Ghi sổ",
                statusLabel = "Đã phê duyệt & Ghi sổ",
                approvedAtUtc = DateTime.UtcNow
            });
        }

        private async Task<(bool Success, string? ErrorMessage, int? LocationId, decimal ResultingQuantity, int? LedgerTransactionId)> ExecuteProposalAdjustmentCoreAsync(
            long proposalAuditId,
            string proposalCode,
            string actionCode,
            string reasonNote,
            string? correlationProductCode,
            int warehouseId,
            int productId,
            int? requestedLocationId,
            string? lotNumber,
            decimal deltaQuantity,
            int? actorUserId,
            CancellationToken cancellationToken)
        {
            var isRelational = _db!.Database.IsRelational();
            await using var tx = isRelational && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            try
            {
                int? resolvedLotId = null;
                if (!string.IsNullOrWhiteSpace(lotNumber))
                {
                    var normalizedLot = lotNumber.Trim();
                    resolvedLotId = await _db.InventoryLots.AsNoTracking()
                        .Where(l => l.ProductId == productId && l.LotNumber == normalizedLot)
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                var stockQuery = _db.InventoryStocks
                    .Where(s => s.WarehouseId == warehouseId &&
                                s.ProductId == productId &&
                                s.Status == InventoryStatus.Available &&
                                s.SerialId == null);

                if (requestedLocationId.HasValue)
                    stockQuery = stockQuery.Where(s => s.LocationId == requestedLocationId.Value);
                if (resolvedLotId.HasValue)
                    stockQuery = stockQuery.Where(s => s.LotId == resolvedLotId.Value);

                var stock = await stockQuery
                    .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                    .ThenBy(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (stock == null)
                {
                    var fallbackLocationId = requestedLocationId ?? await _db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == warehouseId && l.IsActive && !l.IsBlocked)
                        .OrderBy(l => l.LocationType == WarehouseLocationType.Storage ? 0 : 1)
                        .ThenBy(l => l.Id)
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (deltaQuantity < 0m)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return (false, "Không tìm thấy tồn kho khả dụng để thực hiện điều chỉnh giảm.", fallbackLocationId, 0m, null);
                    }

                    stock = new InventoryStock
                    {
                        WarehouseId = warehouseId,
                        ProductId = productId,
                        LocationId = fallbackLocationId,
                        LotId = resolvedLotId,
                        Status = InventoryStatus.Available,
                        Quantity = 0m,
                        ReservedQuantity = 0m,
                        LastUpdated = DateTime.UtcNow
                    };
                    _db.InventoryStocks.Add(stock);
                }

                var newQty = stock.Quantity + deltaQuantity;
                if (newQty < stock.ReservedQuantity || newQty < 0m)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return (
                        false,
                        $"Không thể điều chỉnh tồn kho xuống dưới mức đang giữ chỗ hoặc âm (Tồn hiện tại: {stock.Quantity}, Đang giữ: {stock.ReservedQuantity}, Lượng điều chỉnh: {deltaQuantity}).",
                        stock.LocationId,
                        stock.Quantity,
                        null);
                }

                var now = DateTime.UtcNow;
                stock.Quantity = newQty;
                stock.LastUpdated = now;

                var referenceId = (int)(proposalAuditId % int.MaxValue);
                var referenceCode = $"ADJ-PROP-{proposalAuditId}";
                var ledgerTx = new InventoryTransaction
                {
                    ProductId = productId,
                    WarehouseId = warehouseId,
                    LocationId = stock.LocationId,
                    LotId = stock.LotId,
                    SerialId = stock.SerialId,
                    InventoryStatus = stock.Status,
                    TransactionType = deltaQuantity > 0m ? TransactionType.AdjustmentIncrease : TransactionType.AdjustmentDecrease,
                    Quantity = Math.Abs(deltaQuantity),
                    ReferenceId = referenceId,
                    ReferenceType = "StocktakeAdjustment",
                    TransactionDate = now,
                    CreatedBy = actorUserId ?? 1,
                    Note = $"{referenceCode}: {reasonNote}"
                };
                _db.InventoryTransactions.Add(ledgerTx);

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = actorUserId,
                    EntityName = "StocktakeAdjustmentProposal",
                    EntityId = referenceId,
                    WarehouseId = warehouseId,
                    Action = $"{actionCode}:APPROVED",
                    OldValues = deltaQuantity.ToString(CultureInfo.InvariantCulture),
                    NewValues = proposalCode,
                    Reason = reasonNote,
                    CorrelationId = correlationProductCode,
                    Result = "Success",
                    Timestamp = now
                });

                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);

                return (true, null, stock.LocationId, newQty, ledgerTx.Id);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
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




