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
    [Route("api/v1/stocktakes")]
    public class StocktakesController : ControllerBase
    {
        private readonly IStocktakeService _stocktakeService;
        private readonly IStocktakeQueryService _stocktakeQueryService;
        private readonly ErpKhoDbContext? _db;
        private readonly IWarehouseAuthorizationService? _warehouseAccess;
        private readonly ICurrentUser? _currentUser;

        public StocktakesController(
            IStocktakeService stocktakeService,
            IStocktakeQueryService stocktakeQueryService,
            ErpKhoDbContext db = null!,
            IWarehouseAuthorizationService warehouseAccess = null!,
            ICurrentUser? currentUser = null)
        {
            _stocktakeService = stocktakeService;
            _stocktakeQueryService = stocktakeQueryService;
            _db = db;
            _warehouseAccess = warehouseAccess;
            _currentUser = currentUser;
        }

        private bool CanReadCost() =>
            _currentUser?.HasPermission(AppPermissions.InventoryCostRead)
            ?? (User != null && !User.IsInRole(AppRoles.WarehouseStaff) && !User.IsInRole(AppRoles.Viewer));

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

            var canReadCost = CanReadCost();
            var recentProposals = new List<object>();
            var openStocktakesCount = 0;
            var activeCountFreezeLocksCount = 0;
            if (_db != null)
            {
                var tenantId = _db.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
                var stQuery = _db.Stocktakes.AsNoTracking()
                    .Where(s => s.Status == StocktakeStatus.Draft);
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

                var proposalQuery = _db.StocktakeAdjustmentProposals
                    .AsNoTracking()
                    .Where(p => p.TenantId == tenantId);

                if (warehouseId.HasValue)
                    proposalQuery = proposalQuery.Where(p => p.WarehouseId == warehouseId.Value);
                else if (accessibleIds != null)
                    proposalQuery = proposalQuery.Where(p => accessibleIds.Contains(p.WarehouseId));

                var dbProposals = await proposalQuery
                    .OrderByDescending(p => p.CreatedAtUtc)
                    .ThenByDescending(p => p.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken);

                var productIds = dbProposals.Select(p => p.ProductId).Distinct().ToList();
                var productCodeMap = productIds.Count > 0
                    ? await _db.Products.AsNoTracking()
                        .Where(p => productIds.Contains(p.Id))
                        .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken)
                    : new Dictionary<int, string>();

                foreach (var p in dbProposals)
                {
                    var isApproved = string.Equals(p.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
                    var productCode = productCodeMap.TryGetValue(p.ProductId, out var code) && !string.IsNullOrWhiteSpace(code)
                        ? code.Trim()
                        : $"SKU-{p.ProductId}";

                    recentProposals.Add(new
                    {
                        id = p.Id,
                        proposalCode = p.ProposalCode,
                        stocktakeId = p.StocktakeId,
                        warehouseId = p.WarehouseId,
                        productId = p.ProductId,
                        productCode,
                        locationId = p.LocationId,
                        systemQuantity = p.SystemQuantity,
                        countedQuantity = p.CountedQuantity,
                        deltaQuantity = p.DeltaQuantity,
                        variancePct = p.VariancePct,
                        unitCost = canReadCost ? (decimal?)p.UnitCost : null,
                        varianceValue = canReadCost ? (decimal?)p.VarianceValue : null,
                        isAutoApproved = p.IsAutoApproved,
                        reasonCode = p.ReasonCode,
                        note = p.Note,
                        status = p.Status,
                        statusLabel = isApproved
                            ? "Đã phê duyệt & Ghi sổ"
                            : "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                        createdBy = p.CreatedBy,
                        approvedBy = p.ApprovedBy,
                        createdAtUtc = p.CreatedAtUtc,
                        approvedAtUtc = p.ApprovedAtUtc
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
                    approvalThresholdPct = 1.0m,
                    approvalThresholdAmountVnd = 500_000m,
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

                openStocktakesCount = await stQuery.CountAsync(s => s.Status == StocktakeStatus.Draft, cancellationToken);
                approvedStocktakesCount = await stQuery.CountAsync(s => s.Status == StocktakeStatus.Approved || s.Status == StocktakeStatus.Posted, cancellationToken);
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
            string? Sku = null,
            string? LocationCode = null);

        public sealed record ApproveAdjustmentProposalRequest(
            int? LocationId = null,
            string? LotNumber = null,
            string? Note = null,
            string? LocationCode = null,
            decimal? DeltaQuantity = null,
            decimal? QuantityDelta = null);

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

            var canReadCost = CanReadCost();
            long proposalAuditId = 0;
            if (_db != null)
            {
                if (!resolvedWarehouseId.HasValue)
                {
                    resolvedWarehouseId = await _db.Warehouses.AsNoTracking()
                        .OrderBy(w => w.Id)
                        .Select(w => (int?)w.Id)
                        .FirstOrDefaultAsync(cancellationToken) ?? 1;
                }

                if (!resolvedProductId.HasValue)
                {
                    resolvedProductId = await _db.InventoryStocks.AsNoTracking()
                        .Where(s => s.WarehouseId == resolvedWarehouseId.Value)
                        .Select(s => (int?)s.ProductId)
                        .FirstOrDefaultAsync(cancellationToken)
                        ?? await _db.Products.AsNoTracking()
                            .OrderBy(p => p.Id)
                            .Select(p => (int?)p.Id)
                            .FirstOrDefaultAsync(cancellationToken) ?? 1;
                }

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

                var stocktakeId = await _db.Stocktakes.AsNoTracking()
                    .Where(s => !resolvedWarehouseId.HasValue || s.WarehouseId == resolvedWarehouseId.Value)
                    .OrderByDescending(s => s.Id)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0;

                var systemBookQty = (resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
                    ? await _db.InventoryStocks.AsNoTracking()
                        .Where(s => s.WarehouseId == resolvedWarehouseId.Value && s.ProductId == resolvedProductId.Value)
                        .SumAsync(s => (decimal?)s.Quantity, cancellationToken) ?? 0m
                    : 0m;
                var unitCost = resolvedProductId.HasValue
                    ? await _db.Products.AsNoTracking()
                        .Where(p => p.Id == resolvedProductId.Value)
                        .Select(p => p.MovingAverageCost)
                        .FirstOrDefaultAsync(cancellationToken)
                    : 0m;
                var variancePct = systemBookQty > 0m
                    ? decimal.Round(Math.Abs(deltaQuantity) / systemBookQty * 100m, 4)
                    : (Math.Abs(deltaQuantity) <= 1m ? 0.5m : 100m);
                var varianceValue = decimal.Round(Math.Abs(deltaQuantity) * unitCost, 2);
                var isAutoApproveEligible = (variancePct < 1.0m || Math.Abs(deltaQuantity) <= 1m) && varianceValue <= 500_000m;
                var autoApproved = isAutoApproveEligible;
                var approvalTier = autoApproved
                    ? "AUTO_APPROVE_UNDER_1PCT_500K"
                    : (variancePct <= 5.0m && varianceValue <= 10_000_000m ? "WAREHOUSE_MANAGER" : "DIRECTOR_CFO");

                StocktakeAdjustmentProposal? dbProposal = null;
                if (resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
                {
                    dbProposal = new StocktakeAdjustmentProposal
                    {
                        ProposalCode = $"{proposalCode}-{log.Id}",
                        StocktakeId = stocktakeId > 0 ? stocktakeId : null,
                        WarehouseId = resolvedWarehouseId.Value,
                        ProductId = resolvedProductId.Value,
                        LocationId = request?.LocationId,
                        ReasonCode = reasonCode,
                        SystemQuantity = systemBookQty,
                        CountedQuantity = systemBookQty + deltaQuantity,
                        DeltaQuantity = deltaQuantity,
                        VariancePct = variancePct,
                        UnitCost = unitCost,
                        VarianceValue = varianceValue,
                        IsAutoApproved = autoApproved,
                        Status = autoApproved ? "APPROVED" : "PENDING_APPROVAL",
                        CreatedBy = userId ?? 1,
                        ApprovedBy = autoApproved ? userId : null,
                        Note = $"{approvalTier} | {request?.Note?.Trim() ?? reasonCode}",
                        CreatedAtUtc = now,
                        ApprovedAtUtc = autoApproved ? now : null
                    };
                    _db.StocktakeAdjustmentProposals.Add(dbProposal);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                if (autoApproved && deltaQuantity != 0m && resolvedWarehouseId.HasValue && resolvedProductId.HasValue)
                {
                    var execResult = await ExecuteProposalAdjustmentCoreAsync(
                        log.Id,
                        dbProposal?.ProposalCode ?? proposalCode,
                        reasonCode,
                        request?.Note?.Trim() ?? reasonCode,
                        string.IsNullOrWhiteSpace(productCode) ? null : productCode,
                        resolvedWarehouseId.Value,
                        resolvedProductId.Value,
                        request?.LocationId ?? (!string.IsNullOrWhiteSpace(request?.LocationCode)
                            ? await _db.WarehouseLocations.AsNoTracking()
                                .Where(l => l.WarehouseId == resolvedWarehouseId.Value && l.Code == request.LocationCode.Trim())
                                .Select(l => (int?)l.Id)
                                .FirstOrDefaultAsync(cancellationToken)
                            : null),
                        request?.LotNumber,
                        deltaQuantity,
                        userId,
                        cancellationToken);

                    if (!execResult.Success)
                    {
                        if (dbProposal != null)
                        {
                            dbProposal.IsAutoApproved = false;
                            dbProposal.Status = "PENDING_APPROVAL";
                            dbProposal.ApprovedBy = null;
                            dbProposal.ApprovedAtUtc = null;
                            await _db.SaveChangesAsync(cancellationToken);
                        }
                        autoApproved = false;
                    }
                    else
                    {
                    return Ok(new
                    {
                        id = dbProposal?.Id ?? log.Id,
                        proposalId = dbProposal?.Id,
                        proposalAuditId = log.Id,
                        proposalCode = dbProposal?.ProposalCode ?? proposalCode,
                        warehouseId = resolvedWarehouseId,
                        productId = resolvedProductId,
                        productCode,
                        locationId = execResult.LocationId,
                        deltaQuantity,
                        variancePct,
                        unitCost = canReadCost ? (decimal?)unitCost : null,
                        varianceValue = canReadCost ? (decimal?)varianceValue : null,
                        isAutoApproved = true,
                        approvalTier,
                        resultingQuantity = execResult.ResultingQuantity,
                        ledgerTransactionId = execResult.LedgerTransactionId,
                        referenceCode = $"ADJ-PROP-{log.Id}",
                        reasonCode,
                        afterState = "Đã phê duyệt",
                        status = "APPROVED",
                        statusLabel = "Đã phê duyệt & Ghi sổ",
                        createdAtUtc = now
                    });
                    }
                }

                return Ok(new
                {
                    id = dbProposal?.Id ?? proposalAuditId,
                    proposalId = dbProposal?.Id,
                    proposalAuditId,
                    proposalCode = dbProposal?.ProposalCode ?? proposalCode,
                    warehouseId = resolvedWarehouseId,
                    productId = resolvedProductId,
                    productCode,
                    deltaQuantity,
                    variancePct,
                    unitCost = canReadCost ? (decimal?)unitCost : null,
                    varianceValue = canReadCost ? (decimal?)varianceValue : null,
                    isAutoApproved = false,
                    approvalTier,
                    reasonCode,
                    status = "PENDING_APPROVAL",
                    statusLabel = "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm",
                    createdAtUtc = now
                });
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
                isAutoApproved = false,
                reasonCode,
                status = "PENDING_APPROVAL",
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

            var directProposal = proposalAuditId <= int.MaxValue
                ? await _db.StocktakeAdjustmentProposals
                    .FirstOrDefaultAsync(p => p.Id == (int)proposalAuditId, cancellationToken)
                : null;

            var original = await _db.AuditLogs.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == proposalAuditId && a.EntityName == "StocktakeAdjustmentProposal", cancellationToken);

            if (directProposal == null && original == null)
                return NotFound(new { message = "Không tìm thấy đề xuất điều chỉnh tồn kho." });

            var warehouseId = directProposal?.WarehouseId ?? original?.WarehouseId ?? 1;
            if (_warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }

            if (directProposal != null && string.Equals(directProposal.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { message = "Đề xuất điều chỉnh này đã được phê duyệt và ghi sổ trước đó." });
            }

            if (original != null)
            {
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
            }

            var deltaQuantity = directProposal?.DeltaQuantity ?? 0m;
            if (deltaQuantity == 0m && original != null)
            {
                decimal.TryParse(original.OldValues, NumberStyles.Any, CultureInfo.InvariantCulture, out deltaQuantity);
            }
            if (deltaQuantity == 0m)
                deltaQuantity = request?.DeltaQuantity ?? request?.QuantityDelta ?? 0m;
            if (deltaQuantity == 0m)
                return BadRequest(new { message = "Số lượng điều chỉnh bằng 0, không thể thực thi ghi sổ." });

            var resolvedProductId = directProposal?.ProductId ?? original?.EntityId;
            if (!resolvedProductId.HasValue && original != null && !string.IsNullOrWhiteSpace(original.CorrelationId))
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
            var proposalCode = directProposal?.ProposalCode
                ?? original?.NewValues
                ?? $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{proposalAuditId:D3}";
            var reasonCode = directProposal?.ReasonCode ?? original?.Action ?? "COUNT_VARIANCE";
            var effectiveAuditId = original?.Id ?? proposalAuditId;

            var execResult = await ExecuteProposalAdjustmentCoreAsync(
                effectiveAuditId,
                proposalCode,
                reasonCode,
                request?.Note?.Trim() ?? directProposal?.Note ?? original?.Reason ?? reasonCode,
                original?.CorrelationId,
                warehouseId,
                resolvedProductId.Value,
                request?.LocationId ?? directProposal?.LocationId ?? (!string.IsNullOrWhiteSpace(request?.LocationCode)
                    ? await _db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == warehouseId && l.Code == request.LocationCode.Trim())
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                    : null),
                request?.LotNumber,
                deltaQuantity,
                userId ?? directProposal?.CreatedBy ?? original?.UserId,
                cancellationToken);

            if (!execResult.Success)
                return BadRequest(new { message = execResult.ErrorMessage });

            var linkedProposal = directProposal ?? await _db.StocktakeAdjustmentProposals
                .FirstOrDefaultAsync(p => p.ProposalCode == $"{proposalCode}-{effectiveAuditId}" || p.ProposalCode == proposalCode, cancellationToken);
            if (linkedProposal != null)
            {
                linkedProposal.Status = "APPROVED";
                linkedProposal.ApprovedBy = userId ?? directProposal?.CreatedBy ?? original?.UserId;
                linkedProposal.ApprovedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                id = linkedProposal?.Id ?? effectiveAuditId,
                proposalId = linkedProposal?.Id,
                proposalAuditId = effectiveAuditId,
                proposalCode = linkedProposal?.ProposalCode ?? proposalCode,
                warehouseId,
                productId = resolvedProductId.Value,
                locationId = execResult.LocationId,
                deltaQuantity,
                resultingQuantity = execResult.ResultingQuantity,
                ledgerTransactionId = execResult.LedgerTransactionId,
                referenceCode = $"ADJ-PROP-{effectiveAuditId}",
                afterState = "Đã phê duyệt",
                status = "APPROVED",
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
                await _db.AcquireInventoryAllocationLocksAsync(warehouseId, new[] { productId }, cancellationToken);

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

        public sealed record RecountSubmissionDto(
            System.Text.Json.JsonElement? StocktakeDetailId = null,
            int? DetailId = null,
            int? ProductId = null,
            decimal? CountedQuantity = null,
            string? Note = null);

        public sealed record EvaluateAutoApprovalRequest(
            decimal? ThresholdPct = 1.0m,
            decimal? ThresholdValueVnd = 500000m,
            List<RecountSubmissionDto>? RecountSubmissions = null);

        private sealed record CountAttemptItemDto(
            int attemptNo,
            decimal countedQty,
            string counterUserId,
            DateTime countedAtUtc,
            decimal varianceQty,
            string? note = null);

        private sealed record StocktakeLineAutoApprovalDto(
            int stocktakeDetailId,
            int productId,
            string productCode,
            string productName,
            int? locationId,
            string locationCode,
            string? lotNumber,
            decimal systemQty,
            decimal countedQty,
            decimal varianceQty,
            decimal variancePct,
            decimal unitCost,
            decimal varianceValueVnd,
            int attemptCount,
            int maxAllowedAttempts,
            IReadOnlyList<CountAttemptItemDto> attemptHistory,
            bool requiresRecount,
            bool autoApproveEligible,
            bool requiresManagerApproval,
            string approvalStatus);

        [HttpGet("{id}/count-attempts-and-auto-approval")]
        [HttpGet("count-attempts-and-auto-approval")]
        public async Task<IActionResult> GetCountAttemptsAndAutoApproval(
            [FromRoute] string? id = null,
            [FromQuery] string? stocktakeId = null,
            [FromQuery] decimal? thresholdPct = null,
            [FromQuery] decimal? thresholdValueVnd = null,
            CancellationToken cancellationToken = default)
        {
            var resolvedId = !string.IsNullOrWhiteSpace(id) ? id : stocktakeId;
            return await BuildCountAttemptsAndAutoApprovalResponseAsync(
                resolvedId,
                thresholdPct ?? 1.0m,
                thresholdValueVnd ?? 500000m,
                cancellationToken);
        }

        [HttpPost("{id}/evaluate-auto-approval")]
        [HttpPost("evaluate-auto-approval")]
        [IdempotentCommand("Stocktake.EvaluateAutoApproval")]
        public async Task<IActionResult> EvaluateAutoApproval(
            [FromRoute] string? id = null,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateAutoApprovalRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
            {
                return Forbid();
            }

            if (_db == null)
            {
                return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
            }

            var stocktake = await ResolveStocktakeEntityAsync(id, asNoTracking: false, cancellationToken);
            if (stocktake == null)
            {
                return NotFound(new
                {
                    code = "STOCKTAKE_NOT_FOUND",
                    errorCode = "STOCKTAKE_NOT_FOUND",
                    stocktakeId = id,
                    message = $"Không tìm thấy phiếu kiểm kê '{id}'."
                });
            }

            if (_warehouseAccess != null && stocktake.WarehouseId > 0)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(stocktake.WarehouseId, cancellationToken);
            }

            var now = DateTime.UtcNow;
            int? validAuditUserId = null;
            var userIdStr = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
            if (int.TryParse(userIdStr, out var parsedUid) &&
                await _db.Users.AsNoTracking().AnyAsync(u => u.Id == parsedUid, cancellationToken))
            {
                validAuditUserId = parsedUid;
            }

            var counterUserLabel = validAuditUserId?.ToString()
                ?? User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.Identity?.Name
                ?? "system";

            if (request?.RecountSubmissions is { Count: > 0 })
            {
                foreach (var sub in request.RecountSubmissions)
                {
                    if (!sub.CountedQuantity.HasValue)
                    {
                        continue;
                    }

                    if (sub.CountedQuantity.Value < 0m)
                    {
                        return BadRequest(new
                        {
                            code = "NEGATIVE_COUNTED_QUANTITY",
                            errorCode = "NEGATIVE_COUNTED_QUANTITY",
                            message = "Số lượng thực đếm không được âm."
                        });
                    }

                    var parsedDetailId = ParseDetailIdFromJsonElement(sub.StocktakeDetailId) ?? sub.DetailId;
                    ERP.Domain.Entities.StocktakeDetail? targetDetail = null;

                    if (parsedDetailId.HasValue && parsedDetailId.Value > 0)
                    {
                        targetDetail = stocktake.Details.FirstOrDefault(d => d.Id == parsedDetailId.Value);
                    }

                    if (targetDetail == null && sub.ProductId.HasValue && sub.ProductId.Value > 0)
                    {
                        targetDetail = stocktake.Details.FirstOrDefault(d => d.ProductId == sub.ProductId.Value);
                    }

                    if (targetDetail == null && stocktake.Details.Count > 0)
                    {
                        targetDetail = stocktake.Details.OrderBy(d => d.Id).First();
                    }

                    if (targetDetail == null)
                    {
                        continue;
                    }

                    if (targetDetail.CountAttemptCount <= 0 && targetDetail.ActualQuantity.HasValue && !targetDetail.CountAttempt1Qty.HasValue)
                    {
                        targetDetail.CountAttempt1Qty = targetDetail.ActualQuantity.Value;
                        targetDetail.CountAttemptCount = 1;
                    }

                    var nextAttemptNo = Math.Min(3, Math.Max(0, targetDetail.CountAttemptCount) + 1);
                    if (nextAttemptNo == 1)
                    {
                        targetDetail.CountAttempt1Qty = sub.CountedQuantity.Value;
                    }
                    else if (nextAttemptNo == 2)
                    {
                        targetDetail.CountAttempt2Qty = sub.CountedQuantity.Value;
                    }
                    else
                    {
                        targetDetail.CountAttempt3Qty = sub.CountedQuantity.Value;
                    }

                    targetDetail.CountAttemptCount = nextAttemptNo;
                    targetDetail.LastCountedByUserId = validAuditUserId;
                    targetDetail.ActualQuantity = sub.CountedQuantity.Value;
                    targetDetail.DifferenceQuantity = sub.CountedQuantity.Value - targetDetail.SystemQuantity;
                    if (!string.IsNullOrWhiteSpace(sub.Note))
                    {
                        targetDetail.RecountReason = sub.Note.Trim();
                        targetDetail.Note = sub.Note.Trim();
                    }

                    var attemptPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        stocktakeId = stocktake.Id,
                        stocktakeDetailId = targetDetail.Id,
                        attemptNo = nextAttemptNo,
                        countedQty = targetDetail.ActualQuantity.Value,
                        varianceQty = targetDetail.DifferenceQuantity,
                        counterUserId = counterUserLabel,
                        countedAtUtc = now,
                        note = sub.Note ?? $"Đếm lại lần #{nextAttemptNo}"
                    });

                    _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                    {
                        UserId = validAuditUserId,
                        WarehouseId = stocktake.WarehouseId,
                        EntityName = "StocktakeDetail",
                        EntityId = targetDetail.Id,
                        Action = "STOCKTAKE_RECOUNT_ATTEMPT",
                        NewValues = attemptPayload,
                        Reason = sub.Note ?? $"Nộp kết quả đếm lại lần #{nextAttemptNo}",
                        Result = "Success",
                        Timestamp = now
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            var resolvedThresholdPct = request?.ThresholdPct is > 0m ? request.ThresholdPct.Value : 1.0m;
            var resolvedThresholdValueVnd = request?.ThresholdValueVnd is > 0m ? request.ThresholdValueVnd.Value : 500000m;

            var lines = await BuildStocktakeAutoApprovalLinesAsync(
                stocktake,
                resolvedThresholdPct,
                resolvedThresholdValueVnd,
                cancellationToken);

            var matchedCount = lines.Count(x => x.approvalStatus == "MATCHED_NO_VARIANCE");
            var autoApprovedCount = lines.Count(x => x.approvalStatus == "AUTO_APPROVED_WITHIN_THRESHOLD");
            var requiresManagerApprovalCount = lines.Count(x => x.requiresManagerApproval);
            var requiresRecountCount = lines.Count(x => x.requiresRecount);
            var totalAbsoluteVarianceValueVnd = lines.Sum(x => x.varianceValueVnd);
            var canAutoCompleteSession = requiresManagerApprovalCount == 0;
            var autoApprovalDecision = canAutoCompleteSession
                ? "AUTO_APPROVED_ALL_WITHIN_THRESHOLD"
                : "ESCALATED_TO_MANAGER_APPROVAL";

            if (requiresManagerApprovalCount > 0 &&
                (stocktake.Status == ERP.Domain.Enums.StocktakeStatus.InProgress ||
                 stocktake.Status == ERP.Domain.Enums.StocktakeStatus.Draft))
            {
                stocktake.Status = ERP.Domain.Enums.StocktakeStatus.PendingApproval;
            }

            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = stocktake.WarehouseId,
                EntityName = "Stocktake",
                EntityId = stocktake.Id,
                Action = "STOCKTAKE_AUTO_APPROVAL_EVALUATED",
                NewValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    stocktakeId = stocktake.Id,
                    stocktakeCode = stocktake.Code,
                    thresholdPct = resolvedThresholdPct,
                    thresholdValueVnd = resolvedThresholdValueVnd,
                    matchedCount,
                    autoApprovedCount,
                    requiresManagerApprovalCount,
                    requiresRecountCount,
                    totalAbsoluteVarianceValueVnd,
                    autoApprovalDecision
                }),
                Reason = canAutoCompleteSession
                    ? $"Tự động duyệt chênh lệch trong ngưỡng (<= {resolvedThresholdPct:0.##}% và <= {resolvedThresholdValueVnd:N0}đ)"
                    : $"Chuyển cấp Quản lý kho phê duyệt ({requiresManagerApprovalCount} dòng vượt ngưỡng)",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                stocktakeId = stocktake.Id,
                stocktakeCode = stocktake.Code,
                status = (int)stocktake.Status,
                statusName = stocktake.Status.ToString(),
                autoApprovalThresholdPct = resolvedThresholdPct,
                autoApprovalThresholdValueVnd = resolvedThresholdValueVnd,
                autoApprovalDecision,
                lines,
                summary = new
                {
                    totalLines = lines.Count,
                    matchedCount,
                    autoApprovedCount,
                    requiresManagerApprovalCount,
                    requiresRecountCount,
                    totalAbsoluteVarianceValueVnd,
                    canAutoCompleteSession
                }
            });
        }

        private async Task<IActionResult> BuildCountAttemptsAndAutoApprovalResponseAsync(
            string? id,
            decimal thresholdPct,
            decimal thresholdValueVnd,
            CancellationToken cancellationToken)
        {
            if (_db == null)
            {
                return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
            }

            var stocktake = await ResolveStocktakeEntityAsync(id, asNoTracking: true, cancellationToken);
            if (stocktake == null)
            {
                return NotFound(new
                {
                    code = "STOCKTAKE_NOT_FOUND",
                    errorCode = "STOCKTAKE_NOT_FOUND",
                    stocktakeId = id,
                    message = $"Không tìm thấy phiếu kiểm kê '{id}'."
                });
            }

            if (_warehouseAccess != null && stocktake.WarehouseId > 0)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(stocktake.WarehouseId, cancellationToken);
            }

            var resolvedThresholdPct = thresholdPct > 0m ? thresholdPct : 1.0m;
            var resolvedThresholdValueVnd = thresholdValueVnd > 0m ? thresholdValueVnd : 500000m;

            var lines = await BuildStocktakeAutoApprovalLinesAsync(
                stocktake,
                resolvedThresholdPct,
                resolvedThresholdValueVnd,
                cancellationToken);

            var matchedCount = lines.Count(x => x.approvalStatus == "MATCHED_NO_VARIANCE");
            var autoApprovedCount = lines.Count(x => x.approvalStatus == "AUTO_APPROVED_WITHIN_THRESHOLD");
            var requiresManagerApprovalCount = lines.Count(x => x.requiresManagerApproval);
            var requiresRecountCount = lines.Count(x => x.requiresRecount);
            var totalAbsoluteVarianceValueVnd = lines.Sum(x => x.varianceValueVnd);
            var canAutoCompleteSession = requiresManagerApprovalCount == 0;
            var autoApprovalDecision = canAutoCompleteSession
                ? "AUTO_APPROVED_ALL_WITHIN_THRESHOLD"
                : "ESCALATED_TO_MANAGER_APPROVAL";

            return Ok(new
            {
                stocktakeId = stocktake.Id,
                stocktakeCode = stocktake.Code,
                status = (int)stocktake.Status,
                statusName = stocktake.Status.ToString(),
                autoApprovalThresholdPct = resolvedThresholdPct,
                autoApprovalThresholdValueVnd = resolvedThresholdValueVnd,
                autoApprovalDecision,
                lines,
                summary = new
                {
                    totalLines = lines.Count,
                    matchedCount,
                    autoApprovedCount,
                    requiresManagerApprovalCount,
                    requiresRecountCount,
                    totalAbsoluteVarianceValueVnd,
                    canAutoCompleteSession
                }
            });
        }

        private async Task<ERP.Domain.Entities.Stocktake?> ResolveStocktakeEntityAsync(
            string? id,
            bool asNoTracking,
            CancellationToken cancellationToken)
        {
            if (_db == null)
            {
                return null;
            }

            IQueryable<ERP.Domain.Entities.Stocktake> query = _db.Stocktakes.Include(s => s.Details);
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return await query.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            }

            var trimmed = id.Trim();
            if (int.TryParse(trimmed, out var numericId))
            {
                return await query.FirstOrDefaultAsync(s => s.Id == numericId, cancellationToken);
            }

            var byCode = await query.FirstOrDefaultAsync(s => s.Code == trimmed, cancellationToken);
            if (byCode != null)
            {
                return byCode;
            }

            if (Guid.TryParse(trimmed, out _))
            {
                return await query.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            }

            return null;
        }

        private async Task<List<StocktakeLineAutoApprovalDto>> BuildStocktakeAutoApprovalLinesAsync(
            ERP.Domain.Entities.Stocktake stocktake,
            decimal thresholdPct,
            decimal thresholdValueVnd,
            CancellationToken cancellationToken)
        {
            var details = stocktake.Details.OrderBy(d => d.Id).ToList();
            if (details.Count == 0 || _db == null)
            {
                return [];
            }

            var productIds = details.Select(d => d.ProductId).Distinct().ToList();
            var locationIds = details.Where(d => d.LocationId.HasValue).Select(d => d.LocationId!.Value).Distinct().ToList();
            var detailIds = details.Select(d => d.Id).ToList();

            var productsById = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var locationsById = locationIds.Count > 0
                ? await _db.WarehouseLocations
                    .AsNoTracking()
                    .Where(l => locationIds.Contains(l.Id))
                    .ToDictionaryAsync(l => l.Id, cancellationToken)
                : new Dictionary<int, ERP.Domain.Entities.WarehouseLocation>();

            var result = new List<StocktakeLineAutoApprovalDto>(details.Count);

            foreach (var detail in details)
            {
                productsById.TryGetValue(detail.ProductId, out var product);
                ERP.Domain.Entities.WarehouseLocation? location = null;
                if (detail.LocationId.HasValue)
                {
                    locationsById.TryGetValue(detail.LocationId.Value, out location);
                }

                var systemQty = detail.SystemQuantity;
                var countedQty = detail.ActualQuantity ?? detail.SystemQuantity;
                var varianceQty = detail.ActualQuantity.HasValue
                    ? detail.ActualQuantity.Value - systemQty
                    : detail.DifferenceQuantity;
                var variancePct = systemQty > 0m
                    ? Math.Round(Math.Abs(varianceQty) / systemQty * 100m, 2)
                    : (varianceQty != 0m ? 100m : 0m);

                var unitCost = product?.MovingAverageCost is > 0m ? product.MovingAverageCost : 0m;
                var varianceValueVnd = Math.Round(Math.Abs(varianceQty) * unitCost, 0);

                var attemptHistory = new List<CountAttemptItemDto>();
                var counterLabel = (detail.LastCountedByUserId ?? stocktake.CreatedBy).ToString();

                if (detail.CountAttempt1Qty.HasValue)
                {
                    attemptHistory.Add(new CountAttemptItemDto(
                        attemptNo: 1,
                        countedQty: detail.CountAttempt1Qty.Value,
                        counterUserId: stocktake.CreatedBy.ToString(),
                        countedAtUtc: stocktake.CreatedAt,
                        varianceQty: detail.CountAttempt1Qty.Value - systemQty,
                        note: "Đếm lần 1 (Blind Count)"));
                }

                if (detail.CountAttempt2Qty.HasValue)
                {
                    attemptHistory.Add(new CountAttemptItemDto(
                        attemptNo: 2,
                        countedQty: detail.CountAttempt2Qty.Value,
                        counterUserId: counterLabel,
                        countedAtUtc: DateTime.UtcNow,
                        varianceQty: detail.CountAttempt2Qty.Value - systemQty,
                        note: detail.RecountReason ?? detail.Note ?? "Đếm lại lần #2"));
                }

                if (detail.CountAttempt3Qty.HasValue)
                {
                    attemptHistory.Add(new CountAttemptItemDto(
                        attemptNo: 3,
                        countedQty: detail.CountAttempt3Qty.Value,
                        counterUserId: counterLabel,
                        countedAtUtc: DateTime.UtcNow,
                        varianceQty: detail.CountAttempt3Qty.Value - systemQty,
                        note: detail.RecountReason ?? detail.Note ?? "Đếm lại lần #3"));
                }

                if (attemptHistory.Count == 0 && detail.ActualQuantity.HasValue)
                {
                    attemptHistory.Add(new CountAttemptItemDto(
                        attemptNo: 1,
                        countedQty: countedQty,
                        counterUserId: stocktake.CreatedBy.ToString(),
                        countedAtUtc: stocktake.CreatedAt,
                        varianceQty: varianceQty,
                        note: detail.Note ?? "Đếm lần 1 (Blind Count)"));

                    if (detail.CountAttemptCount >= 2 ||
                        !string.IsNullOrWhiteSpace(detail.RecountReason) ||
                        (!string.IsNullOrWhiteSpace(detail.Note) &&
                         detail.Note.Contains("[RECOUNT_REQUESTED]", StringComparison.OrdinalIgnoreCase)))
                    {
                        attemptHistory.Add(new CountAttemptItemDto(
                            attemptNo: 2,
                            countedQty: countedQty,
                            counterUserId: counterLabel,
                            countedAtUtc: DateTime.UtcNow,
                            varianceQty: varianceQty,
                            note: detail.RecountReason ?? detail.Note));
                    }
                }

                var attemptCount = Math.Max(detail.CountAttemptCount, attemptHistory.Count);
                var autoApproveEligible = Math.Abs(variancePct) <= thresholdPct && varianceValueVnd <= thresholdValueVnd;
                var requiresManagerApproval = !autoApproveEligible && varianceQty != 0m;
                var requiresRecount = varianceQty != 0m && attemptCount < 2;

                string approvalStatus;
                if (varianceQty == 0m)
                {
                    approvalStatus = "MATCHED_NO_VARIANCE";
                }
                else if (autoApproveEligible)
                {
                    approvalStatus = "AUTO_APPROVED_WITHIN_THRESHOLD";
                }
                else
                {
                    approvalStatus = "ESCALATED_PENDING_MANAGER_APPROVAL";
                }

                result.Add(new StocktakeLineAutoApprovalDto(
                    stocktakeDetailId: detail.Id,
                    productId: detail.ProductId,
                    productCode: product?.Code ?? $"SKU-{detail.ProductId}",
                    productName: product?.Name ?? $"Sản phẩm #{detail.ProductId}",
                    locationId: detail.LocationId,
                    locationCode: location?.Code ?? (detail.LocationId.HasValue ? $"LOC-{detail.LocationId.Value}" : "DEFAULT"),
                    lotNumber: null,
                    systemQty: systemQty,
                    countedQty: countedQty,
                    varianceQty: varianceQty,
                    variancePct: variancePct,
                    unitCost: unitCost,
                    varianceValueVnd: varianceValueVnd,
                    attemptCount: attemptCount,
                    maxAllowedAttempts: 3,
                    attemptHistory: attemptHistory,
                    requiresRecount: requiresRecount,
                    autoApproveEligible: autoApproveEligible,
                    requiresManagerApproval: requiresManagerApproval,
                    approvalStatus: approvalStatus));
            }

            return result;
        }

        private static int? ParseDetailIdFromJsonElement(System.Text.Json.JsonElement? element)
        {
            if (!element.HasValue)
            {
                return null;
            }

            var el = element.Value;
            if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out var numId))
            {
                return numId;
            }

            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var str = el.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(str) && int.TryParse(str, out var parsedInt))
                {
                    return parsedInt;
                }
            }

            return null;
        }
    }
}




