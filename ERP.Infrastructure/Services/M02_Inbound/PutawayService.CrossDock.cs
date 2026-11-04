using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PutawayService
{
    public async Task<PutawayTaskDto> EvaluateAndExecuteCrossDockAsync(int id, ExecuteCrossDockDto? dto = null, CancellationToken token = default)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        await using var tx = ownTransaction
            ? (isSqlServer ? await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token) : await context.Database.BeginTransactionAsync(token))
            : null;
        try
        {
            if (isSqlServer)
            {
                var tenantId = context.CurrentTenantId;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.PutawayTasks WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}; SELECT 1 FROM dbo.PutawayTaskItems WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND PutawayTaskId = {id};",
                    token);
            }

            var task = await Load(id, token);
            var nowUtc = DateTime.UtcNow;
            var actorId = currentUser.UserId > 0 ? currentUser.UserId : (task.CreatedBy > 0 ? task.CreatedBy : 1);

            if (dto?.Reverse == true)
            {
                return await ReverseCrossDockExecutionAsync(id, task, dto, actorId, nowUtc, tx, token);
            }

            if (task.IsCrossDock && task.Status == PutawayTaskStatus.Completed)
            {
                throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status == PutawayTaskStatus.Completed)
            {
                throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", "Nhiệm vụ Cross-Dock đã được thực thi hoàn tất.");
            }
            if (task.Status == PutawayTaskStatus.Cancelled)
            {
                throw Conflict("Nhiệm vụ không ở trạng thái có thể thực thi Cross-Dock.");
            }
            if (task.IsCrossDock && dto?.TargetBackorderId is > 0 && task.CrossDockTargetBackorderId == dto.TargetBackorderId.Value)
            {
                var existingBo = await context.Backorders.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == dto.TargetBackorderId.Value, token);
                if (existingBo != null && existingBo.Status == BackorderStatus.Fulfilled)
                {
                    throw new ConflictException("CROSS_DOCK_ALREADY_COMPLETED", $"Nhiệm vụ Cross-Dock cho Backorder #{dto.TargetBackorderId.Value} đã được thực thi hoàn tất.");
                }
            }

            var item = (dto?.ItemId is > 0
                ? task.Items.FirstOrDefault(x => x.Id == dto.ItemId.Value)
                : task.Items.FirstOrDefault(x => x.InventoryStatus == InventoryStatus.Available && x.RemainingBaseQuantity > 0))
                ?? task.Items.FirstOrDefault();

            if (item is null)
                throw new NotFoundException("Không tìm thấy dòng hàng cất kho.");
            if (item.InventoryStatus != InventoryStatus.Available)
                throw new BusinessRuleException("Chỉ hàng hóa ở trạng thái Khả dụng (Available) mới được phép Cross-Dock trực tiếp sang khu vực xuất hàng.");
            if (item.RemainingBaseQuantity <= 0m)
                throw Conflict("Dòng hàng đã được cất hết số lượng yêu cầu.");

            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);

            // Match open Backorder or high-priority outbound SalesOrder / ExportReceipt
            var targetBoId = dto?.TargetBackorderId ?? task.CrossDockTargetBackorderId;
            Backorder? matchedBackorder = null;
            if (targetBoId is > 0)
            {
                matchedBackorder = await context.Backorders
                    .Include(b => b.SalesOrderLine)
                    .FirstOrDefaultAsync(b =>
                        b.Id == targetBoId.Value &&
                        b.WarehouseId == task.WarehouseId &&
                        b.Status == BackorderStatus.Open &&
                        b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0,
                        token);
            }
            matchedBackorder ??= await context.Backorders
                .Include(b => b.SalesOrderLine)
                    .ThenInclude(l => l.SalesOrder)
                .Where(b => b.WarehouseId == task.WarehouseId &&
                            b.Status == BackorderStatus.Open &&
                            b.Quantity - b.RecoveredQuantity - b.CancelledQuantity > 0 &&
                            b.SalesOrderLine.ProductId == item.ProductId)
                .OrderByDescending(b => b.SalesOrderLine.SalesOrder.Priority)
                .ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(token);

            int? matchedSalesOrderId = dto?.TargetSalesOrderId ?? matchedBackorder?.SalesOrderLine?.SalesOrderId ?? task.CrossDockTargetSalesOrderId;
            decimal? outboundDemandQty = matchedBackorder != null
                ? Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.RecoveredQuantity - matchedBackorder.CancelledQuantity)
                : null;

            if (!matchedSalesOrderId.HasValue)
            {
                var waitingSoLine = await context.SalesOrderLines
                    .Include(l => l.SalesOrder)
                    .Where(l => l.SalesOrder.WarehouseId == task.WarehouseId &&
                                l.ProductId == item.ProductId &&
                                l.OrderedQuantity - l.CancelledQuantity > 0 &&
                                (l.SalesOrder.Status == SalesOrderStatus.Released ||
                                 l.SalesOrder.Status == SalesOrderStatus.PartiallyFulfilled ||
                                 l.SalesOrder.Status == SalesOrderStatus.Draft))
                    .OrderByDescending(l => l.SalesOrder.Priority)
                    .ThenBy(l => l.SalesOrder.CreatedAt)
                    .FirstOrDefaultAsync(token);
                if (waitingSoLine != null)
                {
                    matchedSalesOrderId = waitingSoLine.SalesOrderId;
                    outboundDemandQty ??= Math.Max(0m, waitingSoLine.OrderedQuantity - waitingSoLine.CancelledQuantity);
                }
            }

            int? matchedExportReceiptId = dto?.TargetExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            if (!matchedExportReceiptId.HasValue)
            {
                var waitingExportLine = await context.ExportReceiptDetails
                    .Include(d => d.ExportReceipt)
                    .Where(d => d.ExportReceipt.WarehouseId == task.WarehouseId &&
                                d.ProductId == item.ProductId &&
                                d.Quantity > 0 &&
                                (d.ExportReceipt.Status == ReceiptStatus.Draft || d.ExportReceipt.Status == ReceiptStatus.Approved))
                    .OrderByDescending(d => d.ExportReceipt.Id)
                    .FirstOrDefaultAsync(token);
                if (waitingExportLine != null)
                {
                    matchedExportReceiptId = waitingExportLine.ExportReceiptId;
                    outboundDemandQty ??= waitingExportLine.Quantity;
                }
            }

            // Resolve Outbound Staging location (SHIPPING-STAGE / STAGE-OUT-01 / STAGE-01)
            WarehouseLocation? stagingLoc = null;
            if (dto?.OutboundStagingLocationId is > 0)
            {
                stagingLoc = await LocationForMutation(dto.OutboundStagingLocationId.Value, token);
            }
            if (stagingLoc is null && !string.IsNullOrWhiteSpace(dto?.OutboundStagingLocationCode))
            {
                var code = dto.OutboundStagingLocationCode.Trim().ToUpperInvariant();
                stagingLoc = await LocationByCodeForMutation(task.WarehouseId, code, token);
            }
            if (stagingLoc is null)
            {
                var stagingCodes = new[] { "SHIPPING-STAGE", "STAGE-OUT-01", "STAGE-01" };
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId && l.IsActive && !l.IsBlocked && stagingCodes.Contains(l.Code))
                    .OrderBy(l => l.Id)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = await context.WarehouseLocations
                    .Where(l => l.WarehouseId == task.WarehouseId &&
                                l.IsActive &&
                                !l.IsBlocked &&
                                l.Id != item.SourceLocationId &&
                                l.LocationType == WarehouseLocationType.Storage &&
                                l.IsPickable)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(token);
            }
            if (stagingLoc is null)
            {
                stagingLoc = new WarehouseLocation
                {
                    WarehouseId = task.WarehouseId,
                    Code = "SHIPPING-STAGE",
                    Name = "Bãi tập kết xuất hàng Cross-Dock",
                    LocationType = WarehouseLocationType.Storage,
                    IsPickable = true,
                    IsReceivable = false,
                    IsActive = true,
                    IsBlocked = false,
                    CreatedBy = actorId
                };
                context.WarehouseLocations.Add(stagingLoc);
                await context.SaveChangesAsync(token);
            }

            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == InventoryStatus.Available &&
                            x.LocationId == item.SourceLocationId &&
                            x.Quantity > x.ReservedQuantity)
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);

            var totalAvailAtReceiving = sourceBuckets.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQuantity));
            if (totalAvailAtReceiving <= 0m)
            {
                throw Conflict("Không còn tồn khả dụng tại bãi tiếp nhận để thực thi Cross-Dock.");
            }

            var targetQty = dto?.Quantity is > 0m
                ? Math.Min(dto.Quantity.Value, item.RemainingBaseQuantity)
                : Math.Min(outboundDemandQty ?? item.RemainingBaseQuantity, item.RemainingBaseQuantity);
            var crossDockQty = Math.Round(Math.Min(targetQty, totalAvailAtReceiving), 4);
            if (crossDockQty <= 0m)
            {
                throw new BusinessRuleException("Số lượng Cross-Dock phải lớn hơn 0.");
            }

            return await CommitForwardCrossDockExecutionAsync(id, task, item, stagingLoc, sourceBuckets, crossDockQty, matchedBackorder, matchedSalesOrderId, matchedExportReceiptId, actorId, nowUtc, tx, token);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(token);
            throw;
        }
    }
}
