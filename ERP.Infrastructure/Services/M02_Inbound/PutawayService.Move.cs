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
    public async Task<PutawayTaskDto> MoveAsync(int id, MovePutawayItemDto dto, CancellationToken token = default)
    {
        if(dto.Quantity<=0) throw new BusinessRuleException("Số lượng cất phải lớn hơn 0.");
        var ownTransaction = context.Database.CurrentTransaction is null;
        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
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
            var task=await Load(id,token);
            var isPutback = IsPutbackTask(task);
            if(task.Status is not PutawayTaskStatus.Assigned and not PutawayTaskStatus.InProgress && !(isPutback && task.Status == PutawayTaskStatus.Open))
                throw Conflict("Nhiệm vụ không ở trạng thái có thể cất hàng.");
            EnsureActor(task);
            if (task.Status == PutawayTaskStatus.InProgress)
            {
                if (!string.IsNullOrWhiteSpace(dto.RowVersion))
                {
                    try { _ = Convert.FromBase64String(dto.RowVersion); }
                    catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
                }
                context.Entry(task).Property(x => x.RowVersion).OriginalValue = task.RowVersion;
            }
            else if (task.RowVersion.Length > 0 && !string.IsNullOrWhiteSpace(dto.RowVersion))
            {
                ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));
            }
            var item=(dto.ItemId > 0 ? task.Items.SingleOrDefault(x=>x.Id==dto.ItemId) : task.Items.FirstOrDefault())??throw new NotFoundException("Không tìm thấy dòng hàng.");
            await context.AcquireInventoryAllocationLocksAsync([(task.WarehouseId, item.ProductId)], token);
            WarehouseLocation? destination;
            if (dto.DestinationLocationId > 0)
            {
                destination = await LocationForMutation(dto.DestinationLocationId, token) ?? throw new NotFoundException("Không tìm thấy vị trí đích.");
                if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode) &&
                    !string.Equals(destination.Code, dto.DestinationLocationBarcode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Mã vị trí quét ({dto.DestinationLocationBarcode.Trim()}) không khớp với vị trí đích ({destination.Code}).");
                }
            }
            else if (!string.IsNullOrWhiteSpace(dto.DestinationLocationBarcode))
            {
                var scannedBinCode = dto.DestinationLocationBarcode.Trim().ToUpperInvariant();
                destination = await LocationByCodeForMutation(task.WarehouseId, scannedBinCode, token)
                              ?? throw ConflictWithCode("PUTAWAY_WRONG_BIN", $"Không tìm thấy vị trí kệ lưu trữ với mã quét '{scannedBinCode}'.");
            }
            else
            {
                throw new NotFoundException("Không tìm thấy vị trí đích.");
            }
            if(destination.WarehouseId!=task.WarehouseId) throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(!destination.IsActive||destination.IsBlocked||destination.LocationType!=RequiredType(item.InventoryStatus)||(item.InventoryStatus==InventoryStatus.Available&&!destination.IsPickable&&destination.LocationType!=WarehouseLocationType.Storage)) throw new BusinessRuleException("Vị trí đích không phù hợp với hàng hóa này.");
            var baseQty=ToBase(dto.Quantity,dto.UnitCode,item); if(baseQty>item.RemainingBaseQuantity) throw new BusinessRuleException("Số lượng cất vượt quá số lượng còn lại.");
            var product=await ProductForCapacity(item.ProductId,token)??throw new NotFoundException("Không tìm thấy sản phẩm.");
            if (!string.IsNullOrWhiteSpace(dto.ProductBarcode))
            {
                var scannedProductCode = dto.ProductBarcode.Trim();
                var matchesSku = string.Equals(product.Code, scannedProductCode, StringComparison.OrdinalIgnoreCase);
                var matchesBarcode = matchesSku || await context.ProductBarcodes.AsNoTracking()
                    .AnyAsync(b => b.ProductId == item.ProductId && b.Value == scannedProductCode, token);
                if (!matchesBarcode)
                {
                    throw ConflictWithCode("PUTAWAY_WRONG_PRODUCT", $"Mã vạch sản phẩm quét ({scannedProductCode}) không khớp với SKU ({product.Code}).");
                }
            }
            await EnsurePutawayCapacityAsync(destination, product, baseQty, token);
            var sourceBuckets = await context.InventoryStocks
                .Include(x => x.Lot)
                .Include(x => x.Serial)
                .Where(x => x.ProductId == item.ProductId &&
                            x.WarehouseId == task.WarehouseId &&
                            x.Status == item.InventoryStatus &&
                            x.LocationId == item.SourceLocationId &&
                            (isPutback ? x.Quantity > 0 : x.Quantity > x.ReservedQuantity))
                .OrderBy(x => x.LotId)
                .ThenBy(x => x.SerialId)
                .ThenBy(x => x.Id)
                .ToListAsync(token);
            if (!isPutback && item.ReceiptLineId > 0)
            {
                var lineIdentities = await context.ImportReceiptInventoryIdentities.AsNoTracking()
                    .Where(x => x.ImportReceiptDetailId == item.ReceiptLineId)
                    .ToListAsync(token);
                var statusIdentities = lineIdentities.Where(x => x.TargetStatus == item.InventoryStatus).ToList();
                var identities = statusIdentities.Count > 0 ? statusIdentities : lineIdentities;
                if (identities.Count > 0)
                {
                    var allowedLots = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.LotNumber))
                        .Select(x => x.LotNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowedSerials = identities
                        .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
                        .Select(x => x.SerialNumber!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allowUntracked = identities.Any(x => string.IsNullOrWhiteSpace(x.LotNumber) && string.IsNullOrWhiteSpace(x.SerialNumber));

                    sourceBuckets = sourceBuckets.Where(b =>
                    {
                        var lotKey = b.Lot?.LotNumber ?? (b.LotId.HasValue ? b.LotId.Value.ToString() : null);
                        var serialKey = b.Serial?.SerialNumber ?? (b.SerialId.HasValue ? b.SerialId.Value.ToString() : null);
                        if (serialKey is not null)
                            return allowedSerials.Contains(serialKey) &&
                                   (lotKey is null || allowedLots.Count == 0 || allowedLots.Contains(lotKey));
                        if (lotKey is not null)
                            return allowedLots.Contains(lotKey);
                        return allowUntracked;
                    }).ToList();
                }
                else if (task.ReceiptId > 0)
                {
                    var receiptTxIdentities = await context.InventoryTransactions.AsNoTracking()
                        .Where(t => t.ReferenceType == "ImportReceipt" &&
                                    t.ReferenceId == task.ReceiptId &&
                                    t.ProductId == item.ProductId &&
                                    t.InventoryStatus == item.InventoryStatus &&
                                    (t.LotId.HasValue || t.SerialId.HasValue))
                        .Select(t => new { t.LotId, t.SerialId })
                        .ToListAsync(token);
                    if (receiptTxIdentities.Count > 0)
                    {
                        var allowedPairs = receiptTxIdentities
                            .Select(t => (t.LotId, t.SerialId))
                            .ToHashSet();
                        sourceBuckets = sourceBuckets
                            .Where(b => allowedPairs.Contains((b.LotId, b.SerialId)))
                            .ToList();
                    }
                }
            }
            if (sourceBuckets.Count == 0 || sourceBuckets.Sum(x => isPutback ? x.Quantity : x.Quantity - x.ReservedQuantity) < baseQty)
                throw Conflict("Tồn tại vị trí nguồn đã thay đổi. Vui lòng tải lại.");
            var nowUtc = DateTime.UtcNow;
            var remainingToMove = baseQty;
            var enteredUnitCode = string.IsNullOrWhiteSpace(dto.UnitCode) ? "THÙNG" : dto.UnitCode.Trim().ToUpperInvariant();
            foreach (var sourceBucket in sourceBuckets)
            {
                if (remainingToMove <= 0) break;
                var availableInBucket = isPutback
                    ? sourceBucket.Quantity
                    : sourceBucket.Quantity - sourceBucket.ReservedQuantity;
                if (availableInBucket <= 0) continue;
                var chunk = Math.Min(remainingToMove, availableInBucket);

                var sourceLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == item.SourceLocationId) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                var destinationLocked = await context.InventoryLocks.EffectiveAt(nowUtc).AnyAsync(l =>
                    l.WarehouseId == task.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == destination.Id) &&
                    (!l.ProductId.HasValue || l.ProductId == item.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == item.InventoryStatus) &&
                    (!l.LotId.HasValue || l.LotId == sourceBucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == sourceBucket.SerialId), token);
                if (sourceLocked || destinationLocked)
                {
                    var locked = new BusinessRuleException("Inventory bucket đang bị khóa bởi Inventory Lock.");
                    locked.Data["HttpStatusCode"] = 409;
                    locked.Data["ErrorCode"] = "INV_STOCK_LOCKED";
                    throw locked;
                }

                sourceBucket.Quantity -= chunk;
                var reservedMoved = 0m;
                var allocatedMoved = 0m;
                if (isPutback)
                {
                    if (sourceBucket.ReservedQuantity > sourceBucket.Quantity)
                    {
                        reservedMoved = sourceBucket.ReservedQuantity - sourceBucket.Quantity;
                        sourceBucket.ReservedQuantity = sourceBucket.Quantity;
                    }
                    if (sourceBucket.AllocatedQuantity > sourceBucket.Quantity)
                    {
                        allocatedMoved = sourceBucket.AllocatedQuantity - sourceBucket.Quantity;
                        sourceBucket.AllocatedQuantity = sourceBucket.Quantity;
                    }
                }
                sourceBucket.LastUpdated = nowUtc;

                var dest = context.InventoryStocks.Local.FirstOrDefault(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId)
                           ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                               x.ProductId == item.ProductId &&
                               x.WarehouseId == task.WarehouseId &&
                               x.Status == item.InventoryStatus &&
                               x.LocationId == destination.Id &&
                               x.LotId == sourceBucket.LotId &&
                               x.SerialId == sourceBucket.SerialId &&
                               x.HandlingUnitId == sourceBucket.HandlingUnitId &&
                               x.OwnerId == sourceBucket.OwnerId, token);
                if (dest is null)
                {
                    context.InventoryStocks.Add(new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = item.InventoryStatus,
                        LocationId = destination.Id,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = reservedMoved,
                        AllocatedQuantity = allocatedMoved,
                        MovingAverageCost = sourceBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    });
                }
                else
                {
                    dest.Quantity += chunk;
                    dest.ReservedQuantity += reservedMoved;
                    dest.AllocatedQuantity += allocatedMoved;
                    dest.LastUpdated = nowUtc;
                }

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = item.InventoryStatus,
                    LotId = sourceBucket.LotId,
                    SerialId = sourceBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = destination.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk == baseQty ? dto.Quantity : decimal.Round(dto.Quantity * chunk / baseQty, 4),
                    EnteredUnitCode = enteredUnitCode,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = currentUser.UserId
                });

                var noteText = $"Putaway từ {item.SourceLocation?.Code ?? "RECEIVING"} sang {destination.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = destination.Id,
                        InventoryStatus = item.InventoryStatus,
                        FromInventoryStatus = item.InventoryStatus,
                        ToInventoryStatus = item.InventoryStatus,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = sourceBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayTask",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = destination.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = destination.Id,
                        InventoryStatus = item.InventoryStatus,
                        FromInventoryStatus = item.InventoryStatus,
                        ToInventoryStatus = item.InventoryStatus,
                        LotId = sourceBucket.LotId,
                        SerialId = sourceBucket.SerialId,
                        HandlingUnitId = sourceBucket.HandlingUnitId,
                        OwnerId = sourceBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = sourceBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayTask",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = noteText
                    });

                remainingToMove -= chunk;
            }
            item.MovedBaseQuantity+=baseQty; task.Status=task.Items.All(x=>x.RemainingBaseQuantity==0)?PutawayTaskStatus.Completed:PutawayTaskStatus.InProgress; task.StartedAt??=nowUtc; if(task.Status==PutawayTaskStatus.Completed)task.CompletedAt=nowUtc;
            if (isPutback && task.Status == PutawayTaskStatus.Completed)
            {
                await CompletePutbackOrderCancellationAsync(task, baseQty, nowUtc, token);
            }
            context.AuditLogs.Add(Audit(currentUser.UserId,"PutawayTask.Moved",task.Id,task.WarehouseId,$"ItemId: {item.Id}; BaseQuantity: {baseQty}; DestinationId: {destination.Id}"));
            await context.SaveChangesAsync(token); if(tx is not null)await tx.CommitAsync(token); return await GetAsync(id,token);
        }
        catch (Exception ex) when (ownTransaction && attempt < maxAttempts && (IsDeadlock(ex) || ex is Microsoft.Data.SqlClient.SqlException { Number: 51033 }))
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            await Task.Delay(15 * attempt, token);
        }
        catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}
        catch(Exception ex) when (IsDeadlock(ex)){if(tx is not null)await tx.RollbackAsync(token);throw new DeadlockException("Giao dịch Putaway bị deadlock.",ex);}
        catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
        }
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is DeadlockException or Microsoft.Data.SqlClient.SqlException { Number: 1205 })
                return true;
        }
        return false;
    }

}
