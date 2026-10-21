using System.Security.Claims;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
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
    public class ImportReceiptsController : ControllerBase
    {
        private readonly IImportReceiptService _importReceiptService;
        private readonly IReceivingDiscrepancyService? _discrepancies;
        private readonly ErpKhoDbContext? _db;
        private readonly IWarehouseAuthorizationService? _warehouseAccess;
        private IReceivingDiscrepancyService Discrepancies => _discrepancies
            ?? throw new InvalidOperationException("Receiving discrepancy service is not configured.");

        public ImportReceiptsController(
            IImportReceiptService importReceiptService,
            IReceivingDiscrepancyService? discrepancies = null,
            ErpKhoDbContext db = null!,
            IWarehouseAuthorizationService warehouseAccess = null!)
        {
            _importReceiptService = importReceiptService;
            _discrepancies = discrepancies;
            _db = db;
            _warehouseAccess = warehouseAccess;
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

        [HttpGet("{id:int}/inventory-identities")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> GetInventoryIdentities(int id, CancellationToken token)
            => Ok(await _importReceiptService.GetInventoryIdentitiesAsync(id, token));

        [HttpPost("{id:int}/inventory-identities")]
        [IdempotentCommand("ImportReceipt.InventoryIdentity.Set")]
        [PermissionAuthorize(AppPermissions.ReceiptUpdate)]
        public async Task<IActionResult> SetInventoryIdentities(
            int id,
            [FromBody] ERP.Application.DTOs.SetImportReceiptInventoryIdentitiesDto dto,
            CancellationToken token)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
            return Ok(await _importReceiptService.SetInventoryIdentitiesAsync(id, dto, userId, token));
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

        [HttpGet("pipeline-summary")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> GetPipelineSummary()
        {
            var items = (await _importReceiptService.GetAllAsync(null)).ToList();
            return Ok(new
            {
                totalReceipts = items.Count,
                draftOrReceivingCount = items.Count(x =>
                    x.Status is "Draft" or "Received" or "0" or "3"
                    || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
                qcOrApprovalPendingCount = items.Count(x =>
                    x.Status is "QcPending" or "QcCompleted" or "ReadyToPost" or "4" or "5"),
                postedCount = items.Count(x => x.Status is "Posted" or "Approved" or "1"),
                cancelledCount = items.Count(x => x.Status is "Cancelled" or "2"),
                status = "Đã tải tổng quan luồng phiếu nhập kho",
                statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
            });
        }

        [HttpGet("qc-and-rma-summary")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> GetQcAndRmaSummary()
        {
            var items = (await _importReceiptService.GetAllAsync(null)).ToList();
            return Ok(new
            {
                totalReceiptsInspected = items.Count,
                pendingQcOrPostingCount = items.Count(x =>
                    x.Status is "Draft" or "Approved" or "Received" or "QcPending" or "QcCompleted" or "ReadyToPost" or "0" or "3" or "4" or "5"
                    || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
                postedWithLotOrQcCount = items.Count(x => x.Status is "Posted" or "Approved" or "1"),
                rmaEligibleReceiptsCount = items.Count(x =>
                    (!string.IsNullOrWhiteSpace(x.Note) && (x.Note.Contains("RMA", StringComparison.OrdinalIgnoreCase) || x.Note.Contains("RETURN", StringComparison.OrdinalIgnoreCase) || x.Note.Contains("trả", StringComparison.OrdinalIgnoreCase)))
                    || x.Status.StartsWith("Discrepancy", StringComparison.OrdinalIgnoreCase)),
                qcWorkCenterModes = new[]
                {
                    "Kiểm định chất lượng đầu vào",
                    "Kiểm định hàng trả về",
                    "Rà soát hàng biệt trữ & hư hỏng"
                },
                status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
                statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
            });
        }

        public sealed record RmaIntakeCheckRequest(
            string? ReturnReference = null,
            string? ReasonCode = null,
            int? WarehouseId = null,
            string? RmaReferenceCode = null,
            int? ProductId = null,
            string? ProductCode = null,
            int? LocationId = null,
            string? LocationCode = null,
            string? LotNumber = null,
            decimal? Quantity = null);

        [HttpPost("rma-intake-check")]
        [HttpPost("initiate-rma-inspection")]
        [IdempotentCommand("ImportReceipt.RmaIntakeCheck")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> RmaIntakeCheck(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RmaIntakeCheckRequest? request = null,
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

            var quarantineBucketCount = 0;
            var pendingQcOrRmaReceiptsCount = 0;
            var openStocktakesCount = 0;
            var activeCountFreezeLocksCount = 0;
            if (_db != null)
            {
                var query = _db.InventoryStocks.AsNoTracking()
                    .Where(s => s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine);
                var receiptQuery = _db.ImportReceipts.AsNoTracking()
                    .Where(r => r.Status != ERP.Domain.Enums.ReceiptStatus.Posted &&
                                r.Status != ERP.Domain.Enums.ReceiptStatus.Approved &&
                                r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled);
                var stocktakeQuery = _db.Stocktakes.AsNoTracking()
                    .Where(s => s.Status == ERP.Domain.Enums.ReceiptStatus.Draft);
                var lockQuery = _db.InventoryLocks.AsNoTracking()
                    .Where(l => l.Status == ERP.Domain.Enums.InventoryLockStatus.Active &&
                                l.LockType == ERP.Domain.Enums.InventoryLockType.CountFreeze);

                if (warehouseId.HasValue)
                {
                    query = query.Where(s => s.WarehouseId == warehouseId.Value);
                    receiptQuery = receiptQuery.Where(r => r.WarehouseId == warehouseId.Value);
                    stocktakeQuery = stocktakeQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
                }
                else if (accessibleIds != null)
                {
                    query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
                    receiptQuery = receiptQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
                    stocktakeQuery = stocktakeQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                    lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
                }

                quarantineBucketCount = await query.CountAsync(cancellationToken);
                pendingQcOrRmaReceiptsCount = await receiptQuery.CountAsync(cancellationToken);
                openStocktakesCount = await stocktakeQuery.CountAsync(cancellationToken);
                activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);
            }

            var now = DateTime.UtcNow;
            var rmaInspectionCode = $"RMA-INSP-{now:yyyyMMdd-HHmm}";
            var rmaReferenceCode = string.IsNullOrWhiteSpace(request?.RmaReferenceCode)
                ? "RMA-AUTO"
                : request.RmaReferenceCode.Trim();

            if (_db != null)
            {
                var actorUserId = int.TryParse(
                    User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                    out var uid) ? uid : (int?)null;
                _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = actorUserId,
                    WarehouseId = warehouseId,
                    Action = "ImportReceipt.RmaIntakeCheck",
                    EntityName = "RmaInspection",
                    OldValues = "QUARANTINE",
                    NewValues = rmaInspectionCode,
                    Reason = string.IsNullOrWhiteSpace(request?.ReasonCode)
                        ? $"Tiếp nhận kiểm định RMA ({pendingQcOrRmaReceiptsCount} phiếu chờ QC/RMA, {quarantineBucketCount} vị trí biệt trữ)"
                        : request.ReasonCode.Trim(),
                    Result = "Success",
                    Timestamp = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                rmaCode = $"RMA-{now:yyyyMMdd}-01",
                rmaInspectionCode,
                rmaReferenceCode,
                returnReference = request?.ReturnReference ?? string.Empty,
                reasonCode = request?.ReasonCode ?? string.Empty,
                quarantineBucketCount,
                pendingQcOrRmaReceiptsCount,
                openStocktakesCount,
                activeCountFreezeLocksCount,
                targetBucket = "Khu vực biệt trữ chờ kiểm định",
                nextStep = "Chuyển trạm kiểm định chất lượng đầu vào",
                dispositionSummary = $"Đã tiếp nhận kiểm định hàng trả về ({pendingQcOrRmaReceiptsCount} phiếu chờ kiểm định, {quarantineBucketCount} vị trí biệt trữ)",
                status = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ",
                statusLabel = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ",
                queuedAtUtc = now
            });
        }

        public sealed record ExecuteRtvRefurbishRequest(
            int? WarehouseId = null,
            string? DispositionType = null,
            int? ProductId = null,
            int? LocationId = null,
            string? LotNumber = null,
            bool ExecuteStockMutation = false,
            string? ActionType = null,
            decimal Quantity = 1m,
            string? ProductCode = null,
            string? LocationCode = null,
            string? ReasonCode = null,
            string? Note = null);

        [HttpPost("execute-rtv-refurbish")]
        [IdempotentCommand("ImportReceipt.ExecuteRtvRefurbish")]
        [PermissionAuthorize(AppPermissions.ReceiptRead)]
        public async Task<IActionResult> ExecuteRtvRefurbish(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteRtvRefurbishRequest? request = null,
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

            var now = DateTime.UtcNow;
            var dispositionCode = $"DISP-RTV-{now:yyyyMMdd-HHmm}";
            var rawAction = !string.IsNullOrWhiteSpace(request?.ActionType)
                ? request.ActionType.Trim().ToUpperInvariant()
                : (!string.IsNullOrWhiteSpace(request?.DispositionType) ? request.DispositionType.Trim().ToUpperInvariant() : "REFURBISH_AND_RTV_SPLIT");
            var isRtvOrScrap = rawAction is "RTV" or "SCRAP" ||
                               (rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal)) ||
                               rawAction.StartsWith("SCRAP", StringComparison.Ordinal);

            var ledgerUpdated = false;
            int? ledgerTransactionId = null;
            decimal? resultingStockQuantity = null;
            string? resultingStatus = null;

            var actorUserId = int.TryParse(
                User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            var resolvedProductId = request?.ProductId;
            var resolvedLocationId = request?.LocationId;
            if (_db != null)
            {
                if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(request?.ProductCode))
                {
                    var code = request.ProductCode.Trim();
                    resolvedProductId = await _db.Products.AsNoTracking()
                        .Where(p => p.Code == code)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }
                if (!resolvedLocationId.HasValue && !string.IsNullOrWhiteSpace(request?.LocationCode))
                {
                    var locCode = request.LocationCode.Trim();
                    var locQuery = _db.WarehouseLocations.AsNoTracking().Where(l => l.Code == locCode);
                    if (warehouseId.HasValue)
                        locQuery = locQuery.Where(l => l.WarehouseId == warehouseId.Value);
                    resolvedLocationId = await locQuery.Select(l => (int?)l.Id).FirstOrDefaultAsync(cancellationToken);
                }
            }

            if (_db != null && (request?.ExecuteStockMutation == true || resolvedProductId.HasValue) && resolvedProductId.HasValue)
            {
                var isRelational = _db.Database.IsRelational();
                await using var tx = isRelational && _db.Database.CurrentTransaction == null
                    ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                    : null;

                try
                {
                    var stockQuery = _db.InventoryStocks
                        .Where(s => s.ProductId == resolvedProductId.Value && (!resolvedLocationId.HasValue || s.LocationId == resolvedLocationId.Value));
                    if (warehouseId.HasValue)
                        stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
                    else if (accessibleIds != null)
                        stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));

                    if (!string.IsNullOrWhiteSpace(request?.LotNumber))
                    {
                        var normalizedLot = request.LotNumber.Trim();
                        stockQuery = stockQuery.Where(s => s.Lot != null && s.Lot.LotNumber == normalizedLot);
                    }

                    var stock = await stockQuery
                        .OrderBy(s => s.Status == ERP.Domain.Enums.InventoryStatus.Available ? 1 : 0)
                        .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                        .ThenBy(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (stock == null)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return NotFound(new { message = "Không tìm thấy tồn kho phù hợp để thực thi xử lý hàng hỏng / trả NCC / tân trang." });
                    }

                    warehouseId ??= stock.WarehouseId;

                    if (isRtvOrScrap)
                    {
                        var writeOffQty = request?.Quantity > 0m ? request.Quantity : 1m;
                        var availableInBucket = stock.Quantity - stock.ReservedQuantity;
                        if (availableInBucket < writeOffQty)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return BadRequest(new
                            {
                                message = $"Không đủ tồn kho khả dụng để xử lý {rawAction} (Khả dụng: {availableInBucket}, Yêu cầu: {writeOffQty})."
                            });
                        }

                        stock.Quantity -= writeOffQty;
                        stock.LastUpdated = now;

                        var ledgerTx = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = stock.ProductId,
                            WarehouseId = stock.WarehouseId,
                            LocationId = stock.LocationId,
                            LotId = stock.LotId,
                            SerialId = stock.SerialId,
                            InventoryStatus = stock.Status,
                            FromInventoryStatus = stock.Status,
                            ToInventoryStatus = null,
                            TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                            Quantity = writeOffQty,
                            ReferenceId = stock.Id,
                            ReferenceType = rawAction.StartsWith("RTV", StringComparison.Ordinal)
                                ? "QualityDispositionRtv"
                                : "QualityDispositionScrap",
                            TransactionDate = now,
                            CreatedBy = actorUserId ?? 1,
                            Note = $"{dispositionCode}: Xử lý {rawAction}"
                        };
                        _db.InventoryTransactions.Add(ledgerTx);
                        await _db.SaveChangesAsync(cancellationToken);

                        ledgerUpdated = true;
                        ledgerTransactionId = ledgerTx.Id;
                        resultingStockQuantity = stock.Quantity;
                        resultingStatus = stock.Status.ToString();
                    }
                    else
                    {
                        var fromStatus = stock.Status;
                        var moveQty = request?.Quantity > 0m && request.Quantity <= stock.Quantity
                            ? request.Quantity
                            : stock.Quantity;

                        if (stock.Quantity - stock.ReservedQuantity < moveQty)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return BadRequest(new
                            {
                                message = $"Không thể tân trang phần tồn kho đang giữ chỗ (Khả dụng: {stock.Quantity - stock.ReservedQuantity}, Yêu cầu: {moveQty})."
                            });
                        }

                        var existingAvailable = await _db.InventoryStocks
                            .FirstOrDefaultAsync(s =>
                                s.Id != stock.Id &&
                                s.WarehouseId == stock.WarehouseId &&
                                s.ProductId == stock.ProductId &&
                                s.LocationId == stock.LocationId &&
                                s.LotId == stock.LotId &&
                                s.SerialId == stock.SerialId &&
                                s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                                cancellationToken);

                        if (existingAvailable != null && stock.Status != ERP.Domain.Enums.InventoryStatus.Available)
                        {
                            stock.Quantity -= moveQty;
                            stock.LastUpdated = now;
                            existingAvailable.Quantity += moveQty;
                            existingAvailable.LastUpdated = now;
                            resultingStockQuantity = existingAvailable.Quantity;
                        }
                        else if (moveQty < stock.Quantity && stock.Status != ERP.Domain.Enums.InventoryStatus.Available)
                        {
                            stock.Quantity -= moveQty;
                            stock.LastUpdated = now;
                            var createdAvailable = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = stock.WarehouseId,
                                ProductId = stock.ProductId,
                                LocationId = stock.LocationId,
                                LotId = stock.LotId,
                                SerialId = stock.SerialId,
                                Status = ERP.Domain.Enums.InventoryStatus.Available,
                                Quantity = moveQty,
                                ReservedQuantity = 0m,
                                LastUpdated = now
                            };
                            _db.InventoryStocks.Add(createdAvailable);
                            resultingStockQuantity = createdAvailable.Quantity;
                        }
                        else
                        {
                            stock.Status = ERP.Domain.Enums.InventoryStatus.Available;
                            stock.LastUpdated = now;
                            resultingStockQuantity = stock.Quantity;
                        }

                        var effectiveFromStatus = fromStatus == ERP.Domain.Enums.InventoryStatus.Available
                            ? ERP.Domain.Enums.InventoryStatus.Quarantine
                            : fromStatus;
                        var statusTx = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = stock.ProductId,
                            WarehouseId = stock.WarehouseId,
                            LocationId = stock.LocationId,
                            FromLocationId = stock.LocationId,
                            ToLocationId = stock.LocationId,
                            LotId = stock.LotId,
                            SerialId = stock.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            FromInventoryStatus = effectiveFromStatus,
                            ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                            Quantity = moveQty > 0m ? moveQty : 1m,
                            ReferenceId = stock.Id,
                            ReferenceType = "InventoryStatusChange",
                            TransactionDate = now,
                            CreatedBy = actorUserId ?? 1,
                            Note = $"{dispositionCode}: Tân trang & chuyển về hàng khả dụng"
                        };
                        _db.InventoryTransactions.Add(statusTx);
                        await _db.SaveChangesAsync(cancellationToken);

                        ledgerUpdated = true;
                        ledgerTransactionId = statusTx.Id;
                        resultingStatus = ERP.Domain.Enums.InventoryStatus.Available.ToString();
                    }

                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            var nonAvailableBucketsCount = 0;
            if (_db != null)
            {
                var query = _db.InventoryStocks.AsNoTracking()
                    .Where(s => s.Status != ERP.Domain.Enums.InventoryStatus.Available);
                if (warehouseId.HasValue)
                    query = query.Where(s => s.WarehouseId == warehouseId.Value);
                else if (accessibleIds != null)
                    query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
                nonAvailableBucketsCount = await query.CountAsync(cancellationToken);

                _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = actorUserId,
                    WarehouseId = warehouseId,
                    Action = "ImportReceipt.ExecuteRtvRefurbish",
                    EntityName = "RtvRefurbish",
                    NewValues = dispositionCode,
                    Reason = $"Thực thi xử lý hàng biệt trữ ({rawAction})",
                    Result = "Success",
                    Timestamp = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            var vietnameseDispositionType = (rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal))
                ? "Hoàn trả nhà cung cấp & giảm trừ tồn kho"
                : (rawAction.StartsWith("SCRAP", StringComparison.Ordinal)
                    ? "Hủy bỏ hàng lỗi & xuất toán tồn kho"
                    : "Tân trang hàng trả về & chuyển về tồn khả dụng");

            return Ok(new
            {
                dispositionCode,
                warehouseId,
                nonAvailableBucketsCount,
                dispositionType = vietnameseDispositionType,
                actionType = vietnameseDispositionType,
                dispositionSummary = $"{vietnameseDispositionType} ({nonAvailableBucketsCount} vị trí biệt trữ và hư hỏng đã rà soát)",
                status = "Đã thực thi & Ghi sổ kiểm toán",
                statusLabel = "Đã thực thi & Ghi sổ kiểm toán",
                ledgerUpdated,
                executedStockMutation = ledgerUpdated,
                mutatedQuantity = ledgerUpdated ? (request?.Quantity > 0m ? request.Quantity : 1m) : 0m,
                ledgerTransactionId,
                resultingStockQuantity,
                resultingStatus,
                refurbishRouting = "Chuyển tồn kho sau tân trang về khu vực hàng khả dụng",
                rtvClaimStatus = "Đã ghi nhận giảm trừ tồn kho và sẵn sàng hoàn trả nhà cung cấp",
                executedAtUtc = now
            });
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

        [HttpPut("{id:int}/supplier"), IdempotentCommand("ImportReceipt.SetSupplier"), PermissionAuthorize(AppPermissions.ReceiptUpdate)]
        public async Task<IActionResult> SetSupplier(int id, [FromBody] ERP.Application.DTOs.SetReceiptPartnerDto dto, [FromServices] IBusinessPartnerService partners, CancellationToken ct)
        { await partners.SetImportSupplierAsync(id, dto.PartnerId, ct); return NoContent(); }
    }
}
