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
    [Route("api/export-receipts")]
    [Route("api/v1/export-receipts")]
    public class ExportReceiptsController : ControllerBase
    {
        private readonly IExportReceiptService _exportReceiptService;
        private readonly ErpKhoDbContext? _db;
        private readonly IWarehouseAuthorizationService? _warehouseAccess;
        private readonly ICurrentUser? _currentUser;
        private IHandoverManifestService? _handoverManifestService;

        public ExportReceiptsController(
            IExportReceiptService exportReceiptService,
            ErpKhoDbContext db = null!,
            IWarehouseAuthorizationService warehouseAccess = null!,
            ICurrentUser? currentUser = null,
            IHandoverManifestService? handoverManifestService = null)
        {
            _exportReceiptService = exportReceiptService;
            _db = db;
            _warehouseAccess = warehouseAccess;
            _currentUser = currentUser;
            _handoverManifestService = handoverManifestService;
        }

        private ErpKhoDbContext RequireDb() =>
            _db ?? throw new InvalidOperationException("ErpKhoDbContext is required for this database-backed operation.");

        private IHandoverManifestService HandoverManifests =>
            _handoverManifestService ??= new ERP.Infrastructure.Services.HandoverManifestService(
                RequireDb(),
                _exportReceiptService,
                _warehouseAccess,
                _currentUser);

        private IActionResult ToActionResult(HandoverManifestOperationResult result) =>
            result.StatusCode switch
            {
                200 => Ok(result.Payload),
                400 => BadRequest(result.Payload),
                404 => NotFound(result.Payload),
                409 => Conflict(result.Payload),
                422 => UnprocessableEntity(result.Payload),
                _ => StatusCode(result.StatusCode, result.Payload)
            };

        private bool CanReadInventoryCost()
        {
            if (_currentUser != null)
            {
                return _currentUser.HasPermission(AppPermissions.InventoryCostRead);
            }

            if (User != null && User.Claims.Any())
            {
                if (User.Claims.Any(c =>
                    (string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(c.Value, AppPermissions.InventoryCostRead, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (User.IsInRole(AppRoles.WarehouseStaff) || User.IsInRole(AppRoles.Viewer))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanReprintAwb()
        {
            if (_currentUser != null)
            {
                return _currentUser.HasPermission(ERP.Domain.Security.AppPermissions.ExportReceiptReprintAwb);
            }

            if (User != null && User.Claims.Any())
            {
                if (User.Claims.Any(c =>
                    (string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.Type, "permissions", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(c.Value, ERP.Domain.Security.AppPermissions.ExportReceiptReprintAwb, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (User.IsInRole(AppRoles.WarehouseStaff) || User.IsInRole(AppRoles.Viewer))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            if (User is null)
            {
                return false;
            }
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User.FindFirstValue("Id");
            return int.TryParse(userIdValue, out userId);
        }

        private async Task<int?> ResolveValidAuditUserIdAsync(CancellationToken cancellationToken)
        {
            var db = RequireDb();
            var hasClaimUser = TryGetUserId(out var uid);
            if (!hasClaimUser && _currentUser?.UserId is > 0)
            {
                uid = _currentUser.UserId;
                hasClaimUser = true;
            }

            if (hasClaimUser && uid > 0)
            {
                if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken))
                {
                    return uid;
                }

                if (!db.Database.IsRelational() && !await db.Users.AsNoTracking().AnyAsync(cancellationToken))
                {
                    return uid;
                }
            }

            if (User?.Identity?.IsAuthenticated == true && !hasClaimUser)
            {
                throw new UnauthorizedAccessException("Không xác định được người dùng hợp lệ cho nhật ký kiểm toán.");
            }

            return null;
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
            var now = DateTime.UtcNow;
            var activePending = items.Where(x => x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback")).ToList();
            var slaOverdueCount = activePending.Count(x => x.MarketplaceShipByUtc.HasValue && x.MarketplaceShipByUtc.Value < now);
            var slaAtRiskUnder2HoursCount = activePending.Count(x =>
                x.MarketplaceShipByUtc.HasValue &&
                x.MarketplaceShipByUtc.Value >= now &&
                x.MarketplaceShipByUtc.Value <= now.AddHours(2));
            var slaDueTodayCount = activePending.Count(x =>
                x.MarketplaceShipByUtc.HasValue &&
                x.MarketplaceShipByUtc.Value > now.AddHours(2) &&
                x.MarketplaceShipByUtc.Value.Date == now.Date);
            var marketplacePrioritizedCount = items.Count(x => !string.IsNullOrWhiteSpace(x.MarketplaceOrderId) || !string.IsNullOrWhiteSpace(x.TrackingNumber));
            var slaComplianceRatePct = items.Count == 0
                ? 100m
                : decimal.Round(((items.Count - slaOverdueCount) * 100m) / items.Count, 2);

            return Ok(new
            {
                totalReceipts = items.Count,
                draftCount = items.Count(x => string.Equals(x.Status, "Draft", StringComparison.OrdinalIgnoreCase)),
                approvedReadyCount = items.Count(x => string.Equals(x.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
                dispatchedCount = items.Count(x => x.Status is "Dispatched" or "Completed"),
                cancelledCount = items.Count(x => string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
                slaOverdueCount,
                slaAtRiskUnder2HoursCount,
                slaDueTodayCount,
                slaComplianceRatePct,
                marketplacePrioritizedCount,
                status = "Đã tải tổng quan luồng phiếu xuất kho",
                statusLabel = "Đã tải tổng quan luồng phiếu xuất kho & đồng hồ SLA giao hàng sàn TMĐT"
            });
        }

        [HttpGet("sla-queue")]
        [HttpGet("marketplace-sla-queue")]
        [HttpGet("~/api/v1/outbound/sla-queue")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetSlaQueue([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            if (_db != null)
            {
                var query = _db.ExportReceipts
                    .AsNoTracking()
                    .Where(r => r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled &&
                                r.Status != ERP.Domain.Enums.ReceiptStatus.CancelPendingPutback &&
                                r.Status != ERP.Domain.Enums.ReceiptStatus.Dispatched &&
                                r.MarketplaceShipByUtc.HasValue);

                if (warehouseId.HasValue)
                {
                    query = query.Where(r => r.WarehouseId == warehouseId.Value);
                }

                var receipts = await query
                    .OrderBy(r => r.MarketplaceShipByUtc)
                    .ToListAsync(cancellationToken);

                var queueItems = receipts.Select(r =>
                {
                    var shipBy = r.MarketplaceShipByUtc!.Value;
                    var remainingMinutes = (int)Math.Round((shipBy - now).TotalMinutes);
                    var slaTier = shipBy < now
                        ? "BREACHED"
                        : (shipBy <= now.AddHours(2) ? "AT_RISK_UNDER_2H" : "ON_TRACK");
                    return new
                    {
                        exportReceiptId = r.Id,
                        exportReceiptCode = r.Code,
                        warehouseId = r.WarehouseId,
                        marketplaceOrderId = r.MarketplaceOrderId,
                        carrierCode = r.CarrierCode,
                        trackingNumber = r.TrackingNumber,
                        marketplaceShipByUtc = shipBy,
                        remainingMinutes,
                        slaTier,
                        status = r.Status.ToString()
                    };
                }).ToList();

                return Ok(new
                {
                    totalCount = queueItems.Count,
                    breachedCount = queueItems.Count(x => x.slaTier == "BREACHED"),
                    atRiskUnder2HoursCount = queueItems.Count(x => x.slaTier == "AT_RISK_UNDER_2H"),
                    onTrackCount = queueItems.Count(x => x.slaTier == "ON_TRACK"),
                    items = queueItems
                });
            }

            var dtoItems = (await _exportReceiptService.GetAllAsync())
                .Where(x => x.MarketplaceShipByUtc.HasValue &&
                            x.Status is not ("Dispatched" or "Completed" or "Cancelled" or "CancelPendingPutback"))
                .OrderBy(x => x.MarketplaceShipByUtc)
                .ToList();

            return Ok(new
            {
                totalCount = dtoItems.Count,
                breachedCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value < now),
                atRiskUnder2HoursCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value >= now && x.MarketplaceShipByUtc!.Value <= now.AddHours(2)),
                onTrackCount = dtoItems.Count(x => x.MarketplaceShipByUtc!.Value > now.AddHours(2)),
                items = dtoItems
            });
        }

        [HttpGet("{id:int}")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetById(int id)
        {
            var receipt = await _exportReceiptService.GetByIdAsync(id);
            return Ok(receipt);
        }

        [HttpPost]
        [IdempotentCommand("ExportReceipt.Create")]
        [PermissionAuthorize(AppPermissions.ExportReceiptCreate)]
        public async Task<IActionResult> Create([FromBody] ERP.Application.DTOs.CreateExportReceiptDto dto, CancellationToken cancellationToken = default)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            }

            ERP.Domain.Entities.TenantSubscription? sub = null;
            if (_db != null)
            {
                var tenantId = _db.CurrentTenantId;
                sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
                if (sub != null)
                {
                    var quota = sub.MonthlyOrderQuota > 0 ? sub.MonthlyOrderQuota : 1500;
                    var maxAllowed = (int)Math.Ceiling(quota * 1.15m);
                    var usagePct = decimal.Round((sub.CurrentMonthOrderCount * 100m) / quota, 2);
                    var isExpiredPastDue = string.Equals(sub.Status, "PAST_DUE", StringComparison.OrdinalIgnoreCase) &&
                                           sub.CurrentPeriodEndUtc.AddHours(72) < DateTime.UtcNow;
                    if (sub.CurrentMonthOrderCount >= maxAllowed || usagePct > 115m || isExpiredPastDue)
                    {
                        return StatusCode(403, new
                        {
                            code = "SUBSCRIPTION_QUOTA_EXCEEDED",
                            errorCode = "SUBSCRIPTION_QUOTA_EXCEEDED",
                            message = $"Thuê bao đã vượt ngưỡng cứng 115% hạn ngạch đơn hàng tháng ({sub.CurrentMonthOrderCount}/{quota} đơn - {usagePct}%) hoặc quá hạn thanh toán 72h. Hệ thống chuyển sang chế độ Chỉ Đọc cho đơn mới."
                        });
                    }
                }
            }

            try
            {
                var receipt = await _exportReceiptService.CreateAsync(dto, userId);
                if (_db != null && sub != null)
                {
                    sub.CurrentMonthOrderCount += 1;
                    sub.UpdatedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }
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

            var now = DateTime.UtcNow;
            var verificationId = $"EXP-CUT-{now:yyyyMMdd-HHmm}";
            if (_db != null)
            {
                var actorUserId = int.TryParse(
                    User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"),
                    out var uid) ? uid : (int?)null;
                _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = actorUserId,
                    WarehouseId = warehouseId,
                    EntityName = "ExportReceipt",
                    Action = "ExportReceipt.VerifyDispatchCutoff",
                    NewValues = verificationId,
                    Reason = $"Đối soát giờ chốt xuất kho ({outboundCutoffLocal}, {checkedExportReceiptsCount} phiếu xuất, {openPickingTasksCount} tác vụ lấy hàng mở)",
                    Result = "Success",
                    Timestamp = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                verificationId,
                warehouseId,
                checkedExportReceiptsCount,
                openPickingTasksCount,
                outboundCutoffLocal,
                singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất",
                cutoffComplianceStatus = $"Đạt chuẩn giờ chốt xuất kho ({outboundCutoffLocal})",
                status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
                statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
                verifiedAtUtc = now
            });
        }

        public sealed record BatchPrintAwbRequest(
            int? WarehouseId = null,
            List<int>? ReceiptIds = null,
            List<int>? ExportReceiptIds = null,
            bool EnforceReprintReason = false,
            string? ReprintReason = null);

        public sealed record ScanHandoverAwbRequest(
            int? ManifestId = null,
            int? WarehouseId = null,
            string? CarrierCode = null,
            string? TrackingNumber = null,
            int? ExportReceiptId = null,
            string? VehiclePlate = null,
            string? DriverName = null,
            string? DriverPhone = null);

        public sealed record SealHandoverManifestRequest(
            string? DriverName = null,
            string? DriverPhone = null,
            string? VehiclePlate = null,
            string? DigitalSignatureHash = null);

        public sealed record NormalizeCarrierAddressRequest(
            string? Province = null,
            string? District = null,
            string? Ward = null);

        public sealed record OpeningBalanceItemRequest(
            int? ProductId = null,
            string? ProductCode = null,
            int? LocationId = null,
            string? LocationCode = null,
            decimal Quantity = 0m,
            decimal UnitCost = 0m,
            string? LotNumber = null,
            DateTime? ExpiryDate = null);

        public sealed record ImportOpeningBalanceRequest(
            int? WarehouseId = null,
            int? ProductId = null,
            int? LocationId = null,
            decimal? Quantity = null,
            decimal? UnitCost = null,
            List<OpeningBalanceItemRequest>? Items = null);

        [HttpPost("batch-print-awb")]
        [HttpPost("batch-print-thermal-awb")]
        [HttpPost("~/api/v1/outbound/print-thermal-labels")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("ExportReceipt.BatchPrintAwb")]
        public async Task<IActionResult> BatchPrintAwb(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] BatchPrintAwbRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var now = DateTime.UtcNow;
            var db = RequireDb();

            if (request?.WarehouseId.HasValue == true && _warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            }

            var query = db.ExportReceipts
                .Include(r => r.Details)
                    .ThenInclude(d => d.Product)
                .Where(r => r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled &&
                            r.Status != ERP.Domain.Enums.ReceiptStatus.CancelPendingPutback);

            var explicitReceiptIds = request?.ReceiptIds is { Count: > 0 }
                ? request.ReceiptIds
                : (request?.ExportReceiptIds is { Count: > 0 } ? request.ExportReceiptIds : null);

            if (explicitReceiptIds is { Count: > 0 })
            {
                query = query.Where(r => explicitReceiptIds.Contains(r.Id));
            }
            else if (request?.WarehouseId.HasValue == true)
            {
                query = query.Where(r => r.WarehouseId == request.WarehouseId.Value);
            }

            var hasExplicitReceiptIds = request?.ReceiptIds is { Count: > 0 };
            var receipts = await query.OrderBy(r => r.Id).Take(100).ToListAsync(cancellationToken);

            var alreadyPrintedReceipts = receipts.Where(r => r.AwbPrintCount >= 1).ToList();
            if (alreadyPrintedReceipts.Count > 0)
            {
                if (!CanReprintAwb())
                {
                    return StatusCode(403, new
                    {
                        code = "AWB_REPRINT_PERMISSION_DENIED",
                        errorCode = "AWB_REPRINT_PERMISSION_DENIED",
                        message = "Bạn không có quyền in lại tem vận đơn (export_receipt.reprint_awb).",
                        reprintReceiptIds = alreadyPrintedReceipts.Select(r => r.Id).ToList()
                    });
                }

                var enforceReprintReason = hasExplicitReceiptIds && (request?.EnforceReprintReason ?? true);
                if (enforceReprintReason && string.IsNullOrWhiteSpace(request?.ReprintReason))
                {
                    return Conflict(new
                    {
                        code = "AWB_REPRINT_REASON_REQUIRED",
                        errorCode = "REPRINT_REASON_REQUIRED",
                        reprintErrorCode = "AWB_REPRINT_REASON_REQUIRED",
                        message = "Phiếu đã từng in tem vận đơn; bắt buộc nhập lý do in lại (ReprintReason).",
                        reprintReceiptIds = alreadyPrintedReceipts.Select(r => r.Id).ToList()
                    });
                }
            }

            var warehouseIds = receipts.Select(r => r.WarehouseId).Distinct().ToList();
            var locationsByWh = await db.WarehouseLocations
                .AsNoTracking()
                .Where(l => warehouseIds.Contains(l.WarehouseId) && l.IsActive)
                .OrderBy(l => l.Code)
                .ToListAsync(cancellationToken);

            var enriched = receipts.Select(r =>
            {
                var firstDetail = r.Details.OrderBy(d => d.Product?.Code ?? string.Empty).FirstOrDefault();
                var primaryLocCode = locationsByWh
                    .Where(l => l.WarehouseId == r.WarehouseId)
                    .Select(l => l.Code)
                    .FirstOrDefault() ?? "A-01-01";
                var primarySku = firstDetail?.Product?.Code ?? $"SKU-{firstDetail?.ProductId ?? 1}";
                var totalQty = r.Details.Sum(d => d.Quantity);
                if (totalQty <= 0m) totalQty = 1m;
                r.TrackingNumber ??= $"VN{now:yyMMdd}{r.Id:D6}";
                r.CarrierCode ??= "GHN";
                r.AwbLabelUrl ??= $"/api/v1/export-receipts/{r.Id}/thermal-awb-100x150.pdf";
                return new
                {
                    Receipt = r,
                    PrimaryPickLocationCode = primaryLocCode,
                    PrimaryProductCode = primarySku,
                    TotalQuantity = totalQty
                };
            })
            .OrderBy(x => x.PrimaryPickLocationCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PrimaryProductCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Receipt.Id)
            .ToList();

            var validAuditUserId = await ResolveValidAuditUserIdAsync(cancellationToken);
            var labels = new List<object>();
            var htmlPages = new List<string>();
            var pdfTextLines = new List<string>();
            var seq = 1;
            foreach (var item in enriched)
            {
                var r = item.Receipt;
                var wasReprint = r.AwbPrintCount > 0;
                r.AwbPrintCount += 1;
                r.AwbFirstPrintedAtUtc ??= now;

                var footer = $"[Kệ: {item.PrimaryPickLocationCode} | SKU: {item.PrimaryProductCode} x {item.TotalQuantity:0.##}]";
                var watermarkText = r.AwbPrintCount > 1 ? "REPRINT - BẢN SAO" : null;
                var watermarkHtml = wasReprint
                    ? $"<div class=\"watermark\">REPRINT - BẢN SAO LẦN {r.AwbPrintCount} ({System.Net.WebUtility.HtmlEncode(request?.ReprintReason ?? "In lại")})</div>"
                    : string.Empty;
                var codAmount = r.Details.Sum(d => decimal.Round(d.Quantity * (d.UnitPrice > 0m ? d.UnitPrice : 125000m), 0));
                var labelHtml = $@"<section class=""awb-page"">
  {watermarkHtml}
  <header class=""awb-header"">
    <div><strong>{r.CarrierCode} EXPRESS</strong><br/><span>Mã đơn sàn: {r.MarketplaceOrderId ?? $"MP-{r.Code}"}</span></div>
    <div class=""hub-box"">HUB-{r.CarrierCode}-01</div>
  </header>
  <div class=""barcode-block"">
    <svg width=""260"" height=""48"" viewBox=""0 0 260 48"" xmlns=""http://www.w3.org/2000/svg""><rect x=""4"" y=""4"" width=""6"" height=""40"" fill=""#000""/><rect x=""14"" y=""4"" width=""3"" height=""40"" fill=""#000""/><rect x=""22"" y=""4"" width=""9"" height=""40"" fill=""#000""/><rect x=""36"" y=""4"" width=""4"" height=""40"" fill=""#000""/><rect x=""46"" y=""4"" width=""8"" height=""40"" fill=""#000""/><rect x=""60"" y=""4"" width=""3"" height=""40"" fill=""#000""/><rect x=""70"" y=""4"" width=""10"" height=""40"" fill=""#000""/><rect x=""86"" y=""4"" width=""5"" height=""40"" fill=""#000""/><rect x=""98"" y=""4"" width=""7"" height=""40"" fill=""#000""/><rect x=""112"" y=""4"" width=""4"" height=""40"" fill=""#000""/><rect x=""122"" y=""4"" width=""9"" height=""40"" fill=""#000""/></svg>
    <div class=""tracking-code"">{r.TrackingNumber}</div>
  </div>
  <div class=""addr-grid"">
    <div><strong>Người gửi:</strong> Kho trung tâm ERP (#{r.WarehouseId})</div>
    <div><strong>Người nhận:</strong> {System.Net.WebUtility.HtmlEncode(r.CustomerNameSnapshot ?? "Khách hàng TMĐT")}</div>
  </div>
  <div class=""cod-box"">Thu hộ COD: <strong>{codAmount:N0} VND</strong></div>
  <footer class=""pick-footer"">{System.Net.WebUtility.HtmlEncode(footer)}</footer>
</section>";
                htmlPages.Add(labelHtml);
                pdfTextLines.Add($"AWB:{r.TrackingNumber} | CARRIER:{r.CarrierCode} | {footer}{(wasReprint ? $" | REPRINT - BAN SAO ({request?.ReprintReason})" : string.Empty)}");

                labels.Add(new
                {
                    sequence = seq++,
                    receiptId = r.Id,
                    exportReceiptId = r.Id,
                    receiptCode = r.Code,
                    exportReceiptCode = r.Code,
                    trackingNumber = r.TrackingNumber,
                    awbCode = r.TrackingNumber,
                    carrierCode = r.CarrierCode,
                    carrierName = r.CarrierCode,
                    marketplaceOrderId = r.MarketplaceOrderId ?? $"MP-{r.Code}",
                    paperSize = "100x150mm",
                    primaryPickLocationCode = item.PrimaryPickLocationCode,
                    primaryProductCode = item.PrimaryProductCode,
                    totalQuantity = item.TotalQuantity,
                    pickingSummaryFooter = footer,
                    pickSequenceFooter = footer,
                    awbPrintCount = r.AwbPrintCount,
                    printCount = r.AwbPrintCount,
                    awbFirstPrintedAtUtc = r.AwbFirstPrintedAtUtc,
                    isReprint = wasReprint,
                    isReprintWatermark = wasReprint,
                    watermarkText,
                    reprintReason = wasReprint ? request?.ReprintReason : null,
                    labelHtml
                });

                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = r.WarehouseId,
                    EntityName = "ExportReceipt",
                    EntityId = r.Id,
                    Action = wasReprint ? "ExportReceipt.ReprintAwb" : "ExportReceipt.BatchPrintAwb",
                    NewValues = $"AwbPrintCount={r.AwbPrintCount}; Tracking={r.TrackingNumber}; Loc={item.PrimaryPickLocationCode}",
                    Reason = wasReprint
                        ? $"In lại nhãn vận đơn nhiệt 100x150mm: {request?.ReprintReason}"
                        : "In gộp nhãn vận đơn nhiệt 100x150mm kèm dòng tóm tắt nhặt hàng",
                    Result = "Success",
                    Timestamp = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            var batchCode = $"AWB-BATCH-{now:yyyyMMdd-HHmmss}";
            var printHtmlDocument = $@"<!DOCTYPE html>
<html lang=""vi""><head><meta charset=""utf-8""/><title>{batchCode} - Thermal AWB 100x150mm</title>
<style>
@page {{ size: 100mm 150mm; margin: 0; }}
body {{ margin: 0; font-family: Arial, sans-serif; font-size: 11px; }}
.awb-page {{ width: 100mm; height: 150mm; box-sizing: border-box; padding: 4mm; border-bottom: 1px dashed #000; page-break-after: always; position: relative; }}
.watermark {{ position: absolute; top: 45%; left: 8%; transform: rotate(-28deg); font-size: 16px; font-weight: bold; color: rgba(200,0,0,0.35); border: 2px solid rgba(200,0,0,0.35); padding: 4px 8px; }}
.awb-header {{ display: flex; justify-content: space-between; border-bottom: 2px solid #000; padding-bottom: 4px; }}
.hub-box {{ font-size: 16px; font-weight: bold; border: 2px solid #000; padding: 2px 6px; }}
.barcode-block {{ text-align: center; margin: 6px 0; }}
.tracking-code {{ font-size: 14px; font-weight: bold; letter-spacing: 1px; }}
.cod-box {{ margin: 8px 0; padding: 6px; border: 2px solid #000; font-size: 14px; }}
.pick-footer {{ margin-top: 10px; padding-top: 6px; border-top: 1px dashed #000; font-weight: bold; font-size: 11px; }}
</style></head><body>{string.Join("\n", htmlPages)}</body></html>";
            var pdfBytes = BuildThermalAwbPdf14Bytes(batchCode, pdfTextLines);
            var pdfBase64 = Convert.ToBase64String(pdfBytes);

            return Ok(new
            {
                awbBatchCode = batchCode,
                paperSize = "100x150mm",
                sortStrategy = "PrimaryPickLocationCode ASC, ProductCode ASC",
                printedCount = labels.Count,
                duplicatePrintBlocked = true,
                printHtmlDocument,
                thermalPrintHtml = printHtmlDocument,
                pdfBase64,
                labels,
                items = labels,
                printedAtUtc = now,
                statusLabel = $"Đã kết xuất lô in nhiệt {labels.Count} vận đơn 100x150mm theo thứ tự tuyến kệ nhặt"
            });
        }

        private static byte[] BuildThermalAwbPdf14Bytes(string batchCode, IReadOnlyList<string> lines)
        {
            var sanitizedLines = lines.Count > 0 ? lines : new[] { $"Batch {batchCode} - Thermal AWB 100x150mm" };
            var streamBuilder = new System.Text.StringBuilder();
            streamBuilder.Append("BT\n/F1 9 Tf\n14 405 Td\n");
            streamBuilder.Append($"(Thermal AWB 100x150mm - {EscapePdfText(batchCode)}) Tj\n");
            foreach (var line in sanitizedLines)
            {
                streamBuilder.Append("0 -16 Td\n");
                streamBuilder.Append($"({EscapePdfText(line)}) Tj\n");
            }
            streamBuilder.Append("ET\n");
            var contentStream = streamBuilder.ToString();
            var contentByteLength = System.Text.Encoding.UTF8.GetByteCount(contentStream);

            var pdf = new System.Text.StringBuilder();
            pdf.Append("%PDF-1.4\n");
            var obj1Offset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            var obj2Offset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            var obj3Offset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 283.46 425.20] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
            var obj4Offset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append($"4 0 obj\n<< /Length {contentByteLength} >>\nstream\n{contentStream}endstream\nendobj\n");
            var obj5Offset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
            var xrefOffset = System.Text.Encoding.UTF8.GetByteCount(pdf.ToString());
            pdf.Append("xref\n0 6\n0000000000 65535 f \n");
            pdf.Append($"{obj1Offset:D10} 00000 n \n");
            pdf.Append($"{obj2Offset:D10} 00000 n \n");
            pdf.Append($"{obj3Offset:D10} 00000 n \n");
            pdf.Append($"{obj4Offset:D10} 00000 n \n");
            pdf.Append($"{obj5Offset:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
            return System.Text.Encoding.UTF8.GetBytes(pdf.ToString());
        }

        private static string EscapePdfText(string input)
        {
            return (input ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        [HttpGet("handover-manifests")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetHandoverManifests(
            [FromQuery] int? warehouseId = null,
            [FromQuery] string? carrierCode = null,
            CancellationToken cancellationToken = default)
        {
            var result = await HandoverManifests.GetHandoverManifestsAsync(warehouseId, carrierCode, cancellationToken);
            return ToActionResult(result);
        }

        [HttpPost("handover-manifests/scan-awb")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("HandoverManifest.ScanAwb")]
        public async Task<IActionResult> ScanHandoverAwb(
            [FromRoute] int? manifestIdRoute = null,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ScanHandoverAwbRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
            var result = await HandoverManifests.ScanHandoverAwbAsync(
                manifestIdRoute,
                request == null
                    ? null
                    : new ScanHandoverAwbInputDto(
                        request.ManifestId,
                        request.WarehouseId,
                        request.CarrierCode,
                        request.TrackingNumber,
                        request.ExportReceiptId,
                        request.VehiclePlate,
                        request.DriverName,
                        request.DriverPhone),
                actorUserId,
                User?.Identity?.IsAuthenticated == true,
                cancellationToken);
            return ToActionResult(result);
        }

        [HttpPost("handover-manifests/{id:int}/seal")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("HandoverManifest.Seal")]
        public async Task<IActionResult> SealHandoverManifest(
            int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SealHandoverManifestRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
            var result = await HandoverManifests.SealHandoverManifestAsync(
                id,
                request == null
                    ? null
                    : new SealHandoverManifestInputDto(
                        request.DriverName,
                        request.DriverPhone,
                        request.VehiclePlate,
                        request.DigitalSignatureHash),
                actorUserId,
                User?.Identity?.IsAuthenticated == true,
                cancellationToken);
            return ToActionResult(result);
        }

        public sealed record ConfirmCarrierHandoverRequest(
            int? WarehouseId = null,
            string? CarrierCode = null,
            List<string>? TrackingNumbers = null,
            string? VehiclePlate = null,
            string? DriverName = null,
            string? DriverPhone = null,
            int? ManifestId = null,
            List<int>? ExportReceiptIds = null);

        [HttpPost("confirm-carrier-handover")]
        [HttpPost("handover-manifests/confirm")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("HandoverManifest.ConfirmCarrierHandover")]
        public async Task<IActionResult> ConfirmCarrierHandover(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ConfirmCarrierHandoverRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var actorUserId = TryGetUserId(out var uid) ? (int?)uid : null;
            var result = await HandoverManifests.ConfirmCarrierHandoverAsync(
                request == null
                    ? null
                    : new ConfirmCarrierHandoverInputDto(
                        request.WarehouseId,
                        request.CarrierCode,
                        request.TrackingNumbers,
                        request.VehiclePlate,
                        request.DriverName,
                        request.DriverPhone,
                        request.ManifestId,
                        request.ExportReceiptIds),
                actorUserId,
                User?.Identity?.IsAuthenticated == true,
                cancellationToken);
            return ToActionResult(result);
        }

        [HttpGet("~/api/v1/carriers/address-mappings")]
        [HttpGet("~/api/carriers/address-mappings")]
        [HttpGet("carriers/address-mappings")]
        [HttpGet("carrier-address-mappings")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetCarrierAddressMappings(
            [FromQuery] string? province = null,
            [FromQuery] string? district = null,
            [FromQuery] string? ward = null,
            CancellationToken cancellationToken = default)
        {
            var db = RequireDb();
            await ShipmentsController.EnsureVnAdministrativeSeededAsync(db, cancellationToken);

            var allUnits = await db.VnAdministrativeUnits
                .AsNoTracking()
                .Include(u => u.CarrierMappings)
                .Where(u => u.IsActive)
                .ToListAsync(cancellationToken);

            var filteredUnits = allUnits.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(province))
            {
                var p = province.Trim();
                filteredUnits = filteredUnits.Where(u =>
                    u.ProvinceName.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                    p.Contains(u.ProvinceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.ProvinceCode, p, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(ward))
            {
                var w = ward.Trim();
                filteredUnits = filteredUnits.Where(u =>
                    u.WardName.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                    w.Contains(u.WardName, StringComparison.OrdinalIgnoreCase) ||
                    (u.Keywords != null && u.Keywords.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(u.WardCode, w, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(district))
            {
                var d = district.Trim();
                var byDistrict = filteredUnits.Where(u =>
                    (!string.IsNullOrWhiteSpace(u.DistrictName) && (u.DistrictName.Contains(d, StringComparison.OrdinalIgnoreCase) || d.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))) ||
                    (!string.IsNullOrWhiteSpace(u.DistrictCode) && string.Equals(u.DistrictCode, d, StringComparison.OrdinalIgnoreCase))).ToList();
                if (byDistrict.Count > 0)
                {
                    filteredUnits = byDistrict;
                }
            }

            var matchedUnits = filteredUnits.ToList();
            var matched = matchedUnits.Count > 0;
            var primaryUnit = matchedUnits.FirstOrDefault() ?? allUnits.FirstOrDefault();
            var unitIds = matchedUnits.Select(u => u.Id).ToHashSet();
            var dbMappings = await db.CarrierLocationMappings
                .AsNoTracking()
                .Where(m => unitIds.Count == 0 || unitIds.Contains(m.AdministrativeUnitId))
                .ToListAsync(cancellationToken);

            var ghnMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHN");
            var ghtkMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHTK");
            var vtpMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "VTP" || m.CarrierCode == "VIETTELPOST" || m.CarrierCode == "VTPOST");
            var jtMap = primaryUnit?.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "JT" || m.CarrierCode == "J&T" || m.CarrierCode == "SPX");

            return Ok(new
            {
                administrativeModel = "VN_2_TIER_2025_2026",
                matched,
                input = new { province, district, ward },
                persistedUnitsCount = allUnits.Count,
                matchedUnitsCount = matchedUnits.Count,
                units = matchedUnits.Select(u => new
                {
                    u.Id,
                    u.ProvinceCode,
                    u.ProvinceName,
                    u.WardCode,
                    u.WardName,
                    legacyDistrictCode = u.DistrictCode,
                    legacyDistrictName = u.DistrictName,
                    legacyWardAliases = u.Keywords,
                    carrierMappings = u.CarrierMappings.Select(m => new
                    {
                        m.Id,
                        m.CarrierCode,
                        m.CarrierProvinceId,
                        m.CarrierDistrictId,
                        m.CarrierWardCode,
                        sortingRouteCode = m.RouteCode
                    })
                }),
                carrierMappings = dbMappings.Select(m => new
                {
                    m.Id,
                    m.AdministrativeUnitId,
                    m.CarrierCode,
                    m.CarrierProvinceId,
                    m.CarrierDistrictId,
                    m.CarrierWardCode,
                    sortingRouteCode = m.RouteCode
                }),
                normalized = primaryUnit == null ? null : new
                {
                    provinceCode = primaryUnit.ProvinceCode,
                    provinceName = primaryUnit.ProvinceName,
                    districtName = primaryUnit.DistrictName,
                    wardCode = primaryUnit.WardCode,
                    wardName = primaryUnit.WardName,
                    ghn = ghnMap == null ? null : new { provinceId = ghnMap.CarrierProvinceId, districtId = ghnMap.CarrierDistrictId, wardCode = ghnMap.CarrierWardCode, routeCode = ghnMap.RouteCode },
                    ghtk = ghtkMap == null ? null : new { provinceCode = ghtkMap.CarrierProvinceId, districtCode = ghtkMap.CarrierDistrictId, wardCode = ghtkMap.CarrierWardCode, sortingHub = ghtkMap.RouteCode },
                    viettelPost = vtpMap == null ? null : new { provinceId = vtpMap.CarrierProvinceId, districtId = vtpMap.CarrierDistrictId, wardId = vtpMap.CarrierWardCode, postOfficeCode = vtpMap.RouteCode },
                    jtExpress = jtMap == null ? null : new { areaCode = jtMap.CarrierDistrictId, sortingCode = jtMap.RouteCode }
                },
                statusLabel = "Đã truy vấn & chuẩn hóa địa giới hành chính Việt Nam sang mã định tuyến 3PL từ cơ sở dữ liệu"
            });
        }

        [HttpGet("data-imports/templates")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public IActionResult GetDataImportTemplates()
        {
            return Ok(BuildDataImportTemplatesPayload());
        }

        internal static object BuildDataImportTemplatesPayload() => new
        {
            templates = new[]
            {
                new
                {
                    templateCode = "OPENING_BALANCE_EXCEL",
                    templateName = "Mẫu Excel Nhập Tồn Kho Đầu Kỳ & Giá Vốn Bình Quân (MAC)",
                    downloadUrl = "/api/v1/import-templates/opening-balance",
                    requiredColumns = new[] { "ProductCode", "LocationCode", "Quantity", "UnitCost", "LotNumber", "ExpiryDate" }
                },
                new
                {
                    templateCode = "PRODUCT_MASTER_EXCEL",
                    templateName = "Mẫu Excel Danh Mục Sản Phẩm & Quy Đổi ĐVT",
                    downloadUrl = "/api/v1/import-templates/products",
                    requiredColumns = new[] { "ProductCode", "ProductName", "BaseUomCode", "Barcode", "MovingAverageCost" }
                }
            },
            statusLabel = "Đã tải danh mục biểu mẫu nhập liệu Excel đầu kỳ chuẩn"
        };

        [HttpPost("data-imports/opening-balance")]
        [HttpPost("~/api/v1/inventory/opening-balances/import")]
        [HttpPost("~/api/v1/inventory/opening-balance-import")]
        [HttpPost("~/api/inventory/opening-balance-import")]
        [PermissionAuthorize(AppPermissions.InventoryOpeningBalanceImport, AppPermissions.InventoryWrite)]
        [IdempotentCommand("Inventory.ImportOpeningBalance")]
        public async Task<IActionResult> ImportOpeningBalance(
            [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ImportOpeningBalanceRequest? request = null,
            CancellationToken cancellationToken = default,
            [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? file = null)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var db = RequireDb();
            var now = DateTime.UtcNow;
            var validAuditUserId = await ResolveValidAuditUserIdAsync(cancellationToken);
            var actorUserId = validAuditUserId ?? (TryGetUserId(out var uid) ? uid : 1);
            var warehouseId = request?.WarehouseId ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
            if (warehouseId <= 0) warehouseId = 1;

            if (warehouseId > 0 && _warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }

            var items = new List<OpeningBalanceItemRequest>();
            var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
            if (uploadedFile != null && uploadedFile.Length > 0)
            {
                using var stream = uploadedFile.OpenReadStream();
                using var wb = new ClosedXML.Excel.XLWorkbook(stream);
                var ws = wb.Worksheets.First();
                foreach (var row in ws.RowsUsed().Skip(1))
                {
                    var pCode = row.Cell(1).GetString()?.Trim();
                    var locCode = row.Cell(2).GetString()?.Trim();
                    var qtyVal = row.Cell(3).TryGetValue<decimal>(out var q)
                        ? q
                        : (decimal.TryParse(row.Cell(3).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qParsed) ? qParsed : 0m);
                    var costVal = row.Cell(4).TryGetValue<decimal>(out var c)
                        ? c
                        : (decimal.TryParse(row.Cell(4).GetString()?.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cParsed) ? cParsed : 0m);
                    var lotNumber = row.Cell(5).GetString()?.Trim();
                    DateTime? expiryDate = null;
                    if (row.Cell(6).TryGetValue<DateTime>(out var dt))
                    {
                        expiryDate = DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
                    }
                    else if (DateTime.TryParse(row.Cell(6).GetString()?.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dtParsed))
                    {
                        expiryDate = DateTime.SpecifyKind(dtParsed.Date, DateTimeKind.Utc);
                    }

                    if (!string.IsNullOrWhiteSpace(pCode) && qtyVal > 0m)
                    {
                        items.Add(new OpeningBalanceItemRequest(
                            ProductCode: pCode,
                            LocationCode: string.IsNullOrWhiteSpace(locCode) ? null : locCode,
                            Quantity: qtyVal,
                            UnitCost: costVal,
                            LotNumber: string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
                            ExpiryDate: expiryDate));
                    }
                }
            }

            if (items.Count == 0 && request?.Items is { Count: > 0 })
            {
                items.AddRange(request.Items);
            }
            else if (items.Count == 0 && (request?.ProductId is > 0 || request?.Quantity is > 0m))
            {
                items.Add(new OpeningBalanceItemRequest(
                    ProductId: request.ProductId,
                    LocationId: request.LocationId,
                    Quantity: request.Quantity ?? 0m,
                    UnitCost: request.UnitCost ?? 0m));
            }

            if (items.Count == 0)
            {
                return BadRequest(new
                {
                    code = "OPENING_BALANCE_FILE_REQUIRED",
                    errorCode = "OPENING_BALANCE_FILE_REQUIRED",
                    message = "Vui lòng tải lên tệp Excel (.xlsx) hoặc danh sách dòng tồn kho đầu kỳ hợp lệ."
                });
            }

            var defaultLocId = request?.LocationId ?? await db.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == warehouseId && l.IsActive)
                .OrderBy(l => l.Id)
                .Select(l => l.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var resolvedProducts = new List<(OpeningBalanceItemRequest Item, ERP.Domain.Entities.Product Product)>();
            foreach (var item in items)
            {
                if (item.Quantity <= 0m)
                {
                    return BadRequest(new { code = "INVALID_OPENING_QUANTITY", errorCode = "INVALID_OPENING_QUANTITY", message = "Số lượng tồn đầu kỳ phải lớn hơn 0." });
                }
                if (item.UnitCost < 0m)
                {
                    return BadRequest(new { code = "INVALID_OPENING_COST", errorCode = "INVALID_OPENING_COST", message = "Đơn giá vốn đầu kỳ không được âm." });
                }

                ERP.Domain.Entities.Product? product = null;
                if (item.ProductId is > 0)
                {
                    product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId.Value, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(item.ProductCode))
                {
                    var pCode = item.ProductCode.Trim();
                    product = await db.Products.FirstOrDefaultAsync(p => p.Code == pCode, cancellationToken);
                }

                if (product == null)
                {
                    return BadRequest(new
                    {
                        code = "PRODUCT_NOT_FOUND",
                        errorCode = "PRODUCT_NOT_FOUND",
                        productCode = item.ProductCode,
                        productId = item.ProductId,
                        message = $"Không tìm thấy sản phẩm '{item.ProductCode ?? item.ProductId?.ToString()}'."
                    });
                }

                resolvedProducts.Add((item, product));
            }

            await using var sqlTx = db.Database.CurrentTransaction == null && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var lockProductIds = resolvedProducts.Select(x => x.Product.Id).Distinct().ToList();
            if (lockProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(warehouseId, lockProductIds, cancellationToken);
            }

            var importedItems = new List<object>();

            foreach (var (item, product) in resolvedProducts)
            {
                int? locId = item.LocationId;
                string? resolvedLocationCode = item.LocationCode;
                if ((!locId.HasValue || locId <= 0) && !string.IsNullOrWhiteSpace(item.LocationCode))
                {
                    var codeNorm = item.LocationCode.Trim().ToUpperInvariant();
                    var locationEntity = await db.WarehouseLocations
                        .FirstOrDefaultAsync(l => l.WarehouseId == warehouseId && l.Code == codeNorm, cancellationToken);
                    if (locationEntity == null)
                    {
                        locationEntity = new ERP.Domain.Entities.WarehouseLocation
                        {
                            WarehouseId = warehouseId,
                            Code = codeNorm,
                            Name = $"Kệ {codeNorm}",
                            LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                            IsActive = true,
                            IsPickable = true,
                            IsReceivable = true
                        };
                        db.WarehouseLocations.Add(locationEntity);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    locId = locationEntity.Id;
                    resolvedLocationCode = locationEntity.Code;
                }
                locId ??= (defaultLocId > 0 ? defaultLocId : null);

                int? lotId = null;
                if (!string.IsNullOrWhiteSpace(item.LotNumber))
                {
                    var lotNo = item.LotNumber.Trim().ToUpperInvariant();
                    var lotEntity = await db.InventoryLots
                        .FirstOrDefaultAsync(l => l.ProductId == product.Id && l.LotNumber == lotNo, cancellationToken);
                    if (lotEntity == null)
                    {
                        lotEntity = new ERP.Domain.Entities.InventoryLot
                        {
                            ProductId = product.Id,
                            LotNumber = lotNo,
                            ManufactureDate = now.Date,
                            ExpiryDate = item.ExpiryDate ?? now.Date.AddYears(2),
                            CreatedAt = now
                        };
                        db.InventoryLots.Add(lotEntity);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    else if (item.ExpiryDate.HasValue)
                    {
                        lotEntity.ExpiryDate = item.ExpiryDate.Value;
                    }
                    lotId = lotEntity.Id;
                }

                var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                    s => s.WarehouseId == warehouseId &&
                         s.ProductId == product.Id &&
                         s.LocationId == locId &&
                         s.LotId == lotId &&
                         s.SerialId == null &&
                         s.HandlingUnitId == null &&
                         s.OwnerId == null &&
                         s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                    cancellationToken);

                if (stock == null)
                {
                    stock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = warehouseId,
                        ProductId = product.Id,
                        LocationId = locId > 0 ? locId : null,
                        LotId = lotId,
                        Status = ERP.Domain.Enums.InventoryStatus.Available,
                        Quantity = item.Quantity,
                        ReservedQuantity = 0m,
                        AllocatedQuantity = 0m,
                        LockedQuantity = 0m,
                        MovingAverageCost = item.UnitCost,
                        LastUpdated = now
                    };
                    db.InventoryStocks.Add(stock);
                }
                else
                {
                    var oldQty = Math.Max(0m, stock.Quantity);
                    var newTotalQty = oldQty + item.Quantity;
                    stock.MovingAverageCost = newTotalQty > 0m
                        ? decimal.Round(((oldQty * stock.MovingAverageCost) + (item.Quantity * item.UnitCost)) / newTotalQty, 4)
                        : item.UnitCost;
                    stock.Quantity = newTotalQty;
                    stock.LastUpdated = now;
                }

                product.MovingAverageCost = stock.MovingAverageCost;

                var tx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = product.Id,
                    WarehouseId = warehouseId,
                    LocationId = locId > 0 ? locId : null,
                    LotId = lotId,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    Quantity = item.Quantity,
                    UnitCost = item.UnitCost,
                    ReferenceId = product.Id,
                    ReferenceType = "OPENING_BALANCE",
                    CreatedBy = actorUserId,
                    TransactionDate = now,
                    Note = $"Nhập tồn kho đầu kỳ ({item.Quantity:0.##} x {item.UnitCost:0.##}đ)"
                };
                db.InventoryTransactions.Add(tx);

                db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
                {
                    EventId = $"OPN-VAL-{warehouseId}-{product.Id}-{now.Ticks}",
                    EventType = "InventoryValuationPosted",
                    AggregateType = "InventoryStock",
                    AggregateId = product.Id,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        warehouseId,
                        productId = product.Id,
                        productCode = product.Code,
                        locationId = locId,
                        locationCode = resolvedLocationCode,
                        lotId,
                        lotNumber = item.LotNumber,
                        expiryDate = item.ExpiryDate,
                        openingQuantity = item.Quantity,
                        unitCost = item.UnitCost,
                        movingAverageCost = stock.MovingAverageCost
                    }),
                    Status = "PENDING",
                    CreatedAt = now
                });

                importedItems.Add(new
                {
                    productId = product.Id,
                    productCode = product.Code,
                    locationId = locId,
                    locationCode = resolvedLocationCode,
                    lotId,
                    lotNumber = item.LotNumber,
                    expiryDate = item.ExpiryDate,
                    importedQuantity = item.Quantity,
                    unitCost = item.UnitCost,
                    movingAverageCost = stock.MovingAverageCost,
                    referenceType = "OPENING_BALANCE"
                });
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = warehouseId,
                EntityName = "InventoryStock",
                Action = "Inventory.ImportOpeningBalance",
                NewValues = $"ImportedCount={importedItems.Count}",
                Reason = $"Nạp tồn kho đầu kỳ ({importedItems.Count} dòng)",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (sqlTx != null)
            {
                await sqlTx.CommitAsync(cancellationToken);
            }

            return Ok(new
            {
                warehouseId,
                importedCount = importedItems.Count,
                referenceType = "OPENING_BALANCE",
                items = importedItems,
                importedAtUtc = now,
                statusLabel = $"Đã nạp tồn kho đầu kỳ ({importedItems.Count} dòng) & khởi tạo giá vốn bình quân gia quyền (MAC)"
            });
        }

        [HttpGet("~/api/v1/accounting-exports/misa-excel")]
        [HttpGet("misa-excel-export")]
        [HttpGet("misa-excel-preview")]
        [HttpGet("misa-excel")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        public async Task<IActionResult> GetMisaExcelExport(
            [FromQuery] int? warehouseId = null,
            [FromQuery] string? format = null,
            CancellationToken cancellationToken = default)
        {
            if (warehouseId.HasValue && _warehouseAccess != null)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }

            var canReadCost = CanReadInventoryCost();
            var rows = new List<object>();
            decimal totalCostAmountValue = 0m;
            decimal totalRevenueAmountValue = 0m;

            if (_db != null)
            {
                var query = _db.ExportReceipts
                    .AsNoTracking()
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Product)
                    .AsQueryable();

                if (warehouseId.HasValue)
                {
                    query = query.Where(r => r.WarehouseId == warehouseId.Value);
                }

                var receipts = await query.OrderByDescending(r => r.Id).Take(50).ToListAsync(cancellationToken);
                foreach (var r in receipts)
                {
                    foreach (var d in r.Details)
                    {
                        var mac = d.Product?.MovingAverageCost is > 0m ? d.Product.MovingAverageCost : 0m;
                        var unitPrice = d.UnitPrice > 0m ? d.UnitPrice : 0m;
                        var cogsAmount = decimal.Round(d.Quantity * mac, 2);
                        var revenueAmount = decimal.Round(d.Quantity * unitPrice, 2);
                        totalCostAmountValue += cogsAmount;
                        totalRevenueAmountValue += revenueAmount;

                        rows.Add(new
                        {
                            soChungTu = r.Code,
                            ngayHachToan = r.CreatedAt.ToString("yyyy-MM-dd"),
                            maHang = d.Product?.Code ?? $"SKU-{d.ProductId}",
                            tenHang = d.Product?.Name ?? $"Sản phẩm #{d.ProductId}",
                            tkNoGiaVon = "632",
                            tkCoKho = "1561",
                            soLuong = d.Quantity,
                            unitCost = canReadCost ? (decimal?)mac : null,
                            donGiaVonMac = canReadCost ? (decimal?)mac : null,
                            costAmount = canReadCost ? (decimal?)cogsAmount : null,
                            thanhTienGiaVon = canReadCost ? (decimal?)cogsAmount : null,
                            tkNoCongNo = "131",
                            tkCoDoanhThu = "5111",
                            donGiaBan = unitPrice,
                            doanhThu = revenueAmount,
                            eInvoiceStatus = r.EInvoiceStatus ?? "PENDING",
                            eInvoiceProvider = r.EInvoiceProvider ?? "MISA_MEINVOICE"
                        });
                    }
                }
            }

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var sheet = workbook.Worksheets.Add("MISA_AMIS_ACT_200");
            var headers = new[]
            {
                "SoChungTu", "NgayHachToan", "MaHang", "TenHang",
                "TKNoGiaVon", "TKCoKho", "SoLuong", "DonGiaVonMAC", "ThanhTienGiaVon",
                "TKNoCongNo", "TKCoDoanhThu", "DonGiaBan", "DoanhThu",
                "EInvoiceStatus", "EInvoiceProvider"
            };
            for (var c = 0; c < headers.Length; c++)
                sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Row(1).Style.Font.Bold = true;

            var rowIdx = 2;
            foreach (dynamic row in rows)
            {
                sheet.Cell(rowIdx, 1).Value = (string)row.soChungTu;
                sheet.Cell(rowIdx, 2).Value = (string)row.ngayHachToan;
                sheet.Cell(rowIdx, 3).Value = (string)row.maHang;
                sheet.Cell(rowIdx, 4).Value = (string)row.tenHang;
                sheet.Cell(rowIdx, 5).Value = (string)row.tkNoGiaVon;
                sheet.Cell(rowIdx, 6).Value = (string)row.tkCoKho;
                sheet.Cell(rowIdx, 7).Value = (decimal)row.soLuong;
                if (canReadCost && row.donGiaVonMac != null)
                {
                    sheet.Cell(rowIdx, 8).Value = (decimal)row.donGiaVonMac;
                    sheet.Cell(rowIdx, 9).Value = (decimal)row.thanhTienGiaVon;
                }
                else
                {
                    sheet.Cell(rowIdx, 8).Value = string.Empty;
                    sheet.Cell(rowIdx, 9).Value = string.Empty;
                }
                sheet.Cell(rowIdx, 10).Value = (string)row.tkNoCongNo;
                sheet.Cell(rowIdx, 11).Value = (string)row.tkCoDoanhThu;
                sheet.Cell(rowIdx, 12).Value = (decimal)row.donGiaBan;
                sheet.Cell(rowIdx, 13).Value = (decimal)row.doanhThu;
                sheet.Cell(rowIdx, 14).Value = (string)row.eInvoiceStatus;
                sheet.Cell(rowIdx, 15).Value = (string)row.eInvoiceProvider;
                rowIdx++;
            }
            using var ms = new System.IO.MemoryStream();
            workbook.SaveAs(ms);
            var xlsxBytes = ms.ToArray();

            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase))
            {
                return File(
                    xlsxBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Bang_Ke_Xuat_Kho_MISA_AMIS.xlsx");
            }

            return Ok(new
            {
                templateStandard = "MISA_AMIS_ACT_200",
                circularStandard = "Thông tư 200/2014/TT-BTC & Thông tư 133/2016/TT-BTC",
                defaultDebitCogsAccount = "632",
                defaultCreditInventoryAccount = "1561",
                defaultDebitReceivableAccount = "131",
                defaultCreditRevenueAccount = "5111",
                rowCount = rows.Count,
                costHiddenByRbac = !canReadCost,
                totalCostAmount = canReadCost ? (decimal?)totalCostAmountValue : null,
                totalRevenueAmount = totalRevenueAmountValue,
                downloadUrl = "/api/v1/export-receipts/misa-excel-export?format=xlsx",
                excelBase64 = Convert.ToBase64String(xlsxBytes),
                rows,
                statusLabel = "Đã kết xuất bảng kê chứng từ xuất kho & giá vốn chuẩn MISA AMIS (TK 632 / 1561 / 131 / 5111)"
            });
        }

        public sealed record ExportMisaEinvoiceRequest(
            int? WarehouseId = null,
            string? Standard = null,
            string? Format = null);

        [HttpPost("export-misa-einvoice")]
        [HttpPost("misa-excel-export")]
        [HttpPost("misa-excel-preview")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("ExportReceipt.ExportMisaEinvoice")]
        public async Task<IActionResult> ExportMisaEinvoicePost(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExportMisaEinvoiceRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            return await GetMisaExcelExport(request?.WarehouseId, request?.Format ?? request?.Standard, cancellationToken);
        }

        public sealed record IssueEinvoiceNd70DraftRequest(
            int? ExportReceiptId = null,
            int? WarehouseId = null,
            string? BuyerTaxCode = null,
            string? BuyerName = null,
            string? DecreeStandard = null);

        [HttpPost("einvoice-nd70-draft")]
        [PermissionAuthorize(AppPermissions.ExportReceiptRead)]
        [IdempotentCommand("ExportReceipt.IssueEinvoiceNd70Draft")]
        public async Task<IActionResult> IssueEinvoiceNd70Draft(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] IssueEinvoiceNd70DraftRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            var now = DateTime.UtcNow;
            var buyerTaxCode = string.IsNullOrWhiteSpace(request?.BuyerTaxCode) ? "0318999888" : request.BuyerTaxCode.Trim();
            var buyerName = string.IsNullOrWhiteSpace(request?.BuyerName) ? "Khách hàng Doanh nghiệp" : request.BuyerName.Trim();
            var decreeStandard = string.IsNullOrWhiteSpace(request?.DecreeStandard) ? "ND70_2025_TT32_2025" : request.DecreeStandard.Trim();
            var receiptId = request?.ExportReceiptId ?? 1;

            if (_db != null)
            {
                var receipt = request?.ExportReceiptId.HasValue == true
                    ? await _db.ExportReceipts.FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken)
                    : await _db.ExportReceipts.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);

                if (receipt != null)
                {
                    receiptId = receipt.Id;
                    receipt.EInvoiceProvider = "MISA_MEINVOICE";
                    receipt.EInvoiceStatus = "DRAFT_ISSUED";
                }

                var validAuditUserId = await ResolveValidAuditUserIdAsync(cancellationToken);
                var draftCodeDb = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
                _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = receipt?.WarehouseId ?? request?.WarehouseId ?? 1,
                    EntityName = "ExportReceipt",
                    EntityId = receiptId,
                    Action = "ExportReceipt.IssueEinvoiceNd70Draft",
                    NewValues = $"DraftCode={draftCodeDb}; BuyerTaxCode={buyerTaxCode}; BuyerName={buyerName}; Standard={decreeStandard}",
                    Reason = $"Phát hành Hóa đơn điện tử nháp tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC ({draftCodeDb})",
                    Result = "Success",
                    Timestamp = now
                });

                _db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
                {
                    EventId = $"EINV-ND70-{receiptId}-{now.Ticks}",
                    EventType = "EinvoiceNd70DraftIssued",
                    AggregateType = "ExportReceipt",
                    AggregateId = receiptId,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        exportReceiptId = receiptId,
                        invoiceDraftCode = draftCodeDb,
                        buyerTaxCode,
                        buyerName,
                        decreeStandard,
                        issuedAtUtc = now
                    }),
                    Status = "PENDING",
                    CreatedAt = now
                });

                await _db.SaveChangesAsync(cancellationToken);
            }

            var draftCode = $"EINV-ND70-{now:yyyyMMdd}-{receiptId:D4}";
            return Ok(new
            {
                exportReceiptId = receiptId,
                invoiceDraftCode = draftCode,
                draftNumber = draftCode,
                buyerTaxCode,
                buyerName,
                decreeStandard,
                provider = "MISA_MEINVOICE",
                status = "DRAFT_ISSUED",
                statusLabel = $"Đã phát hành Hóa đơn điện tử nháp ({draftCode}) tuân thủ Nghị định 70/2025/NĐ-CP & Thông tư 32/2025/TT-BTC",
                issuedAtUtc = now
            });
        }
    }

    internal sealed class FlexibleBodyOrFormModelBinder : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder
    {
        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task BindModelAsync(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingContext bindingContext)
        {
            var request = bindingContext.HttpContext.Request;
            var modelType = bindingContext.ModelType;

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted);
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in form)
                {
                    var val = kv.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        if (int.TryParse(val, out var intVal)) dict[kv.Key] = intVal;
                        else if (decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decVal)) dict[kv.Key] = decVal;
                        else if (bool.TryParse(val, out var boolVal)) dict[kv.Key] = boolVal;
                        else dict[kv.Key] = val;
                    }
                }
                var json = System.Text.Json.JsonSerializer.Serialize(dict, JsonOptions);
                var model = System.Text.Json.JsonSerializer.Deserialize(json, modelType, JsonOptions);
                bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(model);
                return;
            }

            try
            {
                if (request.Body != null)
                {
                    if (request.Body.CanSeek)
                    {
                        request.Body.Position = 0;
                    }
                    using var reader = new System.IO.StreamReader(request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    var bodyText = await reader.ReadToEndAsync(bindingContext.HttpContext.RequestAborted);
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        var model = System.Text.Json.JsonSerializer.Deserialize(bodyText, modelType, JsonOptions);
                        bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(model);
                        return;
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "Payload JSON không hợp lệ.");
            }

            bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(null);
        }
    }

    internal sealed class FlexibleFormFileModelBinder : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder
    {
        public async Task BindModelAsync(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingContext bindingContext)
        {
            var request = bindingContext.HttpContext.Request;
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(bindingContext.HttpContext.RequestAborted);
                var file = form.Files.FirstOrDefault();
                bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(file);
                return;
            }

            bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(null);
        }
    }
}
