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
    private static bool IsPutbackTask(PutawayTask task) =>
        !string.IsNullOrWhiteSpace(task.TaskType) &&
        task.TaskType.Contains("Putback", StringComparison.OrdinalIgnoreCase);

    private async Task CompletePutbackOrderCancellationAsync(
        PutawayTask completedTask,
        decimal movedBaseQty,
        DateTime nowUtc,
        CancellationToken token)
    {
        var taskProductIds = completedTask.Items.Select(i => i.ProductId).Distinct().ToList();
        var otherPendingPutbackTasks = await context.PutawayTasks.AsNoTracking()
            .Where(t => t.Id != completedTask.Id &&
                        t.WarehouseId == completedTask.WarehouseId &&
                        t.TaskType != null &&
                        t.TaskType.Contains("Putback") &&
                        t.Status != PutawayTaskStatus.Completed &&
                        t.Status != PutawayTaskStatus.Cancelled &&
                        (completedTask.ReceiptId <= 0 || t.ReceiptId == completedTask.ReceiptId))
            .AnyAsync(token);
        if (otherPendingPutbackTasks) return;

        var exportReceipts = await context.ExportReceipts
            .Include(e => e.Details)
            .Include(e => e.SalesOrder)
            .Where(e => e.WarehouseId == completedTask.WarehouseId &&
                        (e.Status == ReceiptStatus.CancelPendingPutback ||
                         (completedTask.ReceiptId > 0 && (e.Id == completedTask.ReceiptId || e.SalesOrderId == completedTask.ReceiptId))))
            .ToListAsync(token);

        var matchingExportReceipts = exportReceipts
            .Where(e => (completedTask.ReceiptId > 0 && (e.Id == completedTask.ReceiptId || e.SalesOrderId == completedTask.ReceiptId)) ||
                        e.Status == ReceiptStatus.CancelPendingPutback &&
                        (e.Details.Count == 0 || e.Details.Any(d => taskProductIds.Contains(d.ProductId))))
            .ToList();

        var salesOrderIds = matchingExportReceipts
            .Where(e => e.SalesOrderId.HasValue)
            .Select(e => e.SalesOrderId!.Value)
            .ToHashSet();
        if (completedTask.ReceiptId > 0) salesOrderIds.Add(completedTask.ReceiptId);

        var marketplaceOrderIds = matchingExportReceipts
            .Where(e => !string.IsNullOrWhiteSpace(e.MarketplaceOrderId))
            .Select(e => e.MarketplaceOrderId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var salesOrders = await context.SalesOrders
            .Include(o => o.Lines)
            .Where(o => o.Status == SalesOrderStatus.CancelPendingPutback ||
                        salesOrderIds.Contains(o.Id) ||
                        (!string.IsNullOrEmpty(o.ExternalOrderId) && marketplaceOrderIds.Contains(o.ExternalOrderId)) ||
                        (!string.IsNullOrEmpty(o.OrderCode) && marketplaceOrderIds.Contains(o.OrderCode)))
            .ToListAsync(token);

        var matchingSalesOrders = salesOrders
            .Where(o => salesOrderIds.Contains(o.Id) ||
                        (!string.IsNullOrEmpty(o.ExternalOrderId) && marketplaceOrderIds.Contains(o.ExternalOrderId)) ||
                        (!string.IsNullOrEmpty(o.OrderCode) && marketplaceOrderIds.Contains(o.OrderCode)) ||
                        (o.Status == SalesOrderStatus.CancelPendingPutback &&
                         (o.Lines.Count == 0 || o.Lines.Any(l => taskProductIds.Contains(l.ProductId)))))
            .ToList();

        foreach (var so in matchingSalesOrders)
        {
            if (so.Status == SalesOrderStatus.CancelPendingPutback)
            {
                so.Status = SalesOrderStatus.Cancelled;
            }
            salesOrderIds.Add(so.Id);
            if (!string.IsNullOrWhiteSpace(so.ExternalOrderId))
            {
                marketplaceOrderIds.Add(so.ExternalOrderId);
            }
            if (!string.IsNullOrWhiteSpace(so.OrderCode))
            {
                marketplaceOrderIds.Add(so.OrderCode);
            }
        }

        if (matchingExportReceipts.Count == 0 && (salesOrderIds.Count > 0 || marketplaceOrderIds.Count > 0))
        {
            var linkedReceipts = exportReceipts
                .Where(e => (e.SalesOrderId.HasValue && salesOrderIds.Contains(e.SalesOrderId.Value)) ||
                            (!string.IsNullOrWhiteSpace(e.MarketplaceOrderId) && marketplaceOrderIds.Contains(e.MarketplaceOrderId!)))
                .ToList();
            matchingExportReceipts.AddRange(linkedReceipts);
        }

        var exportReceiptIds = new HashSet<int>();
        foreach (var er in matchingExportReceipts)
        {
            if (er.Status is ReceiptStatus.CancelPendingPutback or ReceiptStatus.Approved or ReceiptStatus.Draft)
            {
                er.Status = ReceiptStatus.Cancelled;
            }
            exportReceiptIds.Add(er.Id);
        }

        var activeReservationStatuses = new[]
        {
            StockReservationStatus.Active,
            StockReservationStatus.PartiallyConsumed,
            StockReservationStatus.PartiallyAllocated,
            StockReservationStatus.Allocated
        };

        var reservations = await context.StockReservations
            .Where(r => r.WarehouseId == completedTask.WarehouseId &&
                        activeReservationStatuses.Contains(r.Status) &&
                        ((r.SourceType == "ExportReceipt" && r.SourceId.HasValue && exportReceiptIds.Contains(r.SourceId.Value)) ||
                         (r.SourceType == "SalesOrder" && r.SourceId.HasValue && salesOrderIds.Contains(r.SourceId.Value)) ||
                         taskProductIds.Contains(r.ProductId)))
            .ToListAsync(token);

        var matchingReservations = reservations
            .Where(r => (r.SourceType == "ExportReceipt" && r.SourceId.HasValue && exportReceiptIds.Contains(r.SourceId.Value)) ||
                        (r.SourceType == "SalesOrder" && r.SourceId.HasValue && salesOrderIds.Contains(r.SourceId.Value)))
            .ToList();

        if (matchingReservations.Count == 0)
        {
            matchingReservations = reservations
                .Where(r => taskProductIds.Contains(r.ProductId))
                .ToList();
        }

        var reservationIds = matchingReservations.Select(r => r.Id).ToHashSet();
        var activeAllocations = reservationIds.Count > 0
            ? await context.StockAllocations
                .Where(a => reservationIds.Contains(a.ReservationId) &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picking ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(token)
            : [];

        foreach (var alloc in activeAllocations)
        {
            alloc.Status = StockAllocationStatus.Released;
            alloc.ReleasedAt = nowUtc;
            alloc.ReleasedBy = currentUser.UserId;
            alloc.ReleaseReason = "Putback completed";
        }

        var releasedByProduct = new Dictionary<int, decimal>();
        foreach (var rsv in matchingReservations)
        {
            var unreleased = Math.Max(0m, rsv.Quantity - rsv.ConsumedQuantity - rsv.ReleasedQuantity);
            rsv.ReleasedQuantity += unreleased;
            rsv.AllocatedQuantity = 0m;
            rsv.Status = StockReservationStatus.Released;
            rsv.ReleasedAt = nowUtc;
            rsv.ReleasedBy = currentUser.UserId;
            rsv.ReleaseReason = "Putback completed";
            releasedByProduct[rsv.ProductId] = releasedByProduct.GetValueOrDefault(rsv.ProductId) + unreleased;
        }

        foreach (var prodId in taskProductIds)
        {
            if (!releasedByProduct.ContainsKey(prodId))
            {
                var taskProdQty = completedTask.Items
                    .Where(i => i.ProductId == prodId)
                    .Sum(i => i.RequiredBaseQuantity > 0 ? i.RequiredBaseQuantity : movedBaseQty);
                releasedByProduct[prodId] = taskProdQty;
            }
        }

        var warehouseStocks = await context.InventoryStocks
            .Where(s => s.WarehouseId == completedTask.WarehouseId && taskProductIds.Contains(s.ProductId))
            .OrderByDescending(s => s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .ToListAsync(token);

        foreach (var (prodId, qtyToRelease) in releasedByProduct)
        {
            var remReserve = qtyToRelease;
            var remAlloc = qtyToRelease;
            foreach (var st in warehouseStocks.Where(s => s.ProductId == prodId))
            {
                if (remReserve > 0m && st.ReservedQuantity > 0m)
                {
                    var dec = Math.Min(remReserve, st.ReservedQuantity);
                    st.ReservedQuantity -= dec;
                    remReserve -= dec;
                    st.LastUpdated = nowUtc;
                }
                if (remAlloc > 0m && st.AllocatedQuantity > 0m)
                {
                    var decAlloc = Math.Min(remAlloc, st.AllocatedQuantity);
                    st.AllocatedQuantity -= decAlloc;
                    remAlloc -= decAlloc;
                    st.LastUpdated = nowUtc;
                }
            }
        }
    }
    // Hold destination eligibility through commit, including a concurrent master deactivation.
    private Task<WarehouseLocation?> LocationForMutation(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND Id = {id}")
            : context.WarehouseLocations.Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private Task<WarehouseLocation?> LocationByCodeForMutation(int warehouseId, string code, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND WarehouseId = {warehouseId} AND Code = {code}")
            : context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.Code == code)).SingleOrDefaultAsync(token);
    private Task<Product?> ProductForCapacity(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.Products.FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {context.CurrentTenantId} AND Id = {id}").AsNoTracking()
            : context.Products.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private IQueryable<PutawayTask> Query()=>context.PutawayTasks.Include(x=>x.Receipt).Include(x=>x.Warehouse).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).ThenInclude(x=>x.Barcodes).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).ThenInclude(x=>x.Unit).Include(x=>x.Items).ThenInclude(x=>x.SourceLocation);
    private void EnsureActor(PutawayTask task){if(IsPutbackTask(task)&&(!task.AssignedUserId.HasValue||task.AssignedUserId.Value<=0))return;if(task.AssignedUserId!=currentUser.UserId&&!currentUser.IsGlobalAdmin&&currentUser.Role is not "Admin" and not "Manager")throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");}
    private static decimal ToBase(decimal qty, string unit, PutawayTaskItem item)
    {
        if (string.IsNullOrWhiteSpace(unit) || string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot))
            return qty;
        var code = unit.Trim().ToUpperInvariant();
        var baseCode = item.BaseUnitCodeSnapshot.Trim().ToUpperInvariant();
        var opCode = (item.OperationUnitCodeSnapshot ?? string.Empty).Trim().ToUpperInvariant();
        var prodUnitCode = item.ReceiptLine?.Product?.Unit?.Code?.Trim().ToUpperInvariant();
        var factor = item.ConversionFactorSnapshot > 0 ? item.ConversionFactorSnapshot : 1m;
        var result = (code == baseCode || (code == prodUnitCode && factor == 1m))
            ? qty
            : (code == opCode || code == prodUnitCode)
                ? qty * factor
                : throw new BusinessRuleException("Đơn vị tính không được hỗ trợ.");
        if (decimal.Round(result, item.BaseUnitDecimalPlaces) != result)
            throw new BusinessRuleException("Số lượng vượt quá độ chính xác cho phép; hệ thống không tự làm tròn.");
        return result;
    }
    private static WarehouseLocationType RequiredType(InventoryStatus s)=>s switch{InventoryStatus.Available=>WarehouseLocationType.Storage,InventoryStatus.Damaged=>WarehouseLocationType.Damaged,InventoryStatus.Rejected=>WarehouseLocationType.Rejected,_=>throw new BusinessRuleException("Trạng thái tồn kho không thuộc luồng cất hàng tự động.")};
    private static void ApplyVersion(byte[] actual,string encoded,Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PutawayTask,byte[]> p){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!actual.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");p.OriginalValue=expected;}
    private async Task<PutawayTaskDto> MapAsync(PutawayTask t,CancellationToken token)
    {
        var moves=await context.InventoryLocationMovements.AsNoTracking().Where(x=>x.PutawayTaskId==t.Id&&x.PutawayTaskItemId.HasValue).Join(context.WarehouseLocations,x=>x.FromLocationId,x=>x.Id,(m,f)=>new{m,f}).Join(context.WarehouseLocations,x=>x.m.ToLocationId,x=>x.Id,(x,d)=>new PutawayMovementDto{Id=x.m.Id,ItemId=x.m.PutawayTaskItemId.GetValueOrDefault(),FromLocationId=x.f.Id,FromLocationCode=x.f.Code.Trim(),ToLocationId=d.Id,ToLocationCode=d.Code.Trim(),BaseQuantity=x.m.BaseQuantity,CreatedAt=x.m.CreatedAt}).ToListAsync(token);
        var activeLocations=await context.WarehouseLocations.AsNoTracking().Where(x=>x.WarehouseId==t.WarehouseId&&x.IsActive&&!x.IsBlocked).OrderBy(x=>x.Code).ToListAsync(token);
        var canMutate=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";
        return new PutawayTaskDto
        {
            Id=t.Id,ReceiptId=t.ReceiptId,ReceiptCode=(t.Receipt?.Code??string.Empty).Trim(),WarehouseId=t.WarehouseId,WarehouseCode=(t.Warehouse?.Code??string.Empty).Trim(),WarehouseName=(t.Warehouse?.Name??string.Empty).Trim(),Status=t.Status.ToString(),AssignedUserId=t.AssignedUserId,RequiredBaseQuantity=t.Items.Sum(x=>x.RequiredBaseQuantity),MovedBaseQuantity=t.Items.Sum(x=>x.MovedBaseQuantity),IsCrossDock=t.IsCrossDock,CrossDockTargetSalesOrderId=t.CrossDockTargetSalesOrderId,CrossDockTargetExportReceiptId=t.CrossDockTargetExportReceiptId,CrossDockTargetBackorderId=t.CrossDockTargetBackorderId,CrossDockAllocatedQty=t.CrossDockAllocatedQty,CreatedAt=t.CreatedAt,RowVersion=canMutate&&t.RowVersion is { Length: > 0 }?Convert.ToBase64String(t.RowVersion):null,ExceptionReason=canMutate?t.ExceptionReason:null,
            Items=t.Items.Select(x=>
            {
                var reqType=RequiredType(x.InventoryStatus);
                var prodStorage=x.ReceiptLine?.Product?.StorageClass;
                var recLoc=activeLocations.FirstOrDefault(l=>l.LocationType==reqType&&(x.InventoryStatus!=InventoryStatus.Available||l.IsPickable)&&(l.StorageClass==null||(prodStorage!=null&&l.StorageClass==prodStorage)));
                var lastMove=moves.Where(m=>m.ItemId==x.Id).OrderByDescending(m=>m.Id).FirstOrDefault();
                var fallbackUom=(x.ReceiptLine?.Product?.Unit?.Code??string.Empty).Trim();
                if(fallbackUom is "QA_UNIT" or "QA-PCS") fallbackUom="THÙNG";
                var baseUom=!string.IsNullOrWhiteSpace(x.BaseUnitCodeSnapshot)?x.BaseUnitCodeSnapshot.Trim():fallbackUom;
                if(baseUom is "" or "QA_UNIT" or "QA-PCS") baseUom=!string.IsNullOrWhiteSpace(fallbackUom)?fallbackUom:"THÙNG";
                var opUom=!string.IsNullOrWhiteSpace(x.OperationUnitCodeSnapshot)?x.OperationUnitCodeSnapshot.Trim():baseUom;
                if(opUom is "" or "QA_UNIT" or "QA-PCS") opUom=baseUom;
                if((x.OperationUnitId==x.BaseUnitId&&x.ConversionFactorSnapshot<=1m)||string.Equals(opUom,baseUom,StringComparison.OrdinalIgnoreCase)) opUom=baseUom;
                return new PutawayTaskItemDto
                {
                    Id=x.Id,ReceiptLineId=x.ReceiptLineId,ProductId=x.ProductId,ProductCode=(x.ReceiptLine?.Product?.Code??string.Empty).Trim(),ProductName=(x.ReceiptLine?.Product?.Name??string.Empty).Trim(),ProductBarcodes=x.ReceiptLine?.Product?.Barcodes.Select(b=>b.Value.Trim()).ToList()??[],InventoryStatus=x.InventoryStatus.ToString(),SourceLocationId=x.SourceLocationId,SourceLocationCode=(x.SourceLocation?.Code??string.Empty).Trim(),
                    RecommendedLocationId=recLoc?.Id,RecommendedLocationCode=recLoc?.Code.Trim(),
                    ConfirmedLocationId=lastMove?.ToLocationId,ConfirmedLocationCode=lastMove?.ToLocationCode.Trim(),
                    OperationUnitCode=opUom,
                    BaseUnitCode=baseUom,
                    RequiredOperationQuantity=x.RequiredOperationQuantity,RequiredBaseQuantity=x.RequiredBaseQuantity,MovedBaseQuantity=x.MovedBaseQuantity,RemainingBaseQuantity=x.RemainingBaseQuantity,RowVersion=canMutate&&x.RowVersion is { Length: > 0 }?Convert.ToBase64String(x.RowVersion):null
                };
            }).ToList(),
            Movements=moves
        };
    }
    private static AuditLog Audit(int user,string action,int entity,int warehouse,string values)=>new(){UserId=user,Action=action,EntityName="PutawayTask",EntityId=entity,WarehouseId=warehouse,NewValues=values,Result="Success",Timestamp=DateTime.UtcNow};
    private static BusinessRuleException Conflict(string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;return e;}
    private static BusinessRuleException ConflictWithCode(string code,string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;e.Data["ErrorCode"]=code;return e;}
    private WarehouseLocationDto Location(WarehouseLocation x)=>new(){Id=x.Id,WarehouseId=x.WarehouseId,Code=x.Code.Trim(),Name=x.Name.Trim(),StructurePath=x.StructurePath?.Trim(),StorageClass=x.StorageClass?.Trim(),MaxWeightKg=x.MaxWeightKg,MaxVolumeM3=x.MaxVolumeM3,MaxPalletEquivalent=x.MaxPalletEquivalent,MapX=x.MapX,MapY=x.MapY,MapWidth=x.MapWidth,MapHeight=x.MapHeight,LocationType=x.LocationType.ToString(),IsActive=x.IsActive,IsBlocked=x.IsBlocked,IsPickable=x.IsPickable,IsReceivable=x.IsReceivable,IsSystemManaged=x.IsSystemManaged,RowVersion=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff"?Convert.ToBase64String(x.RowVersion):null};
    private static decimal? CapacityUtilizationPercent(WarehouseLocationCapacityDto? capacity)
    {
        if (capacity is null) return null;
        var ratios = new List<decimal>();
        if (capacity.MaxWeightKg.HasValue && capacity.UsedWeightKg.HasValue && capacity.MaxWeightKg > 0) ratios.Add(capacity.UsedWeightKg.Value / capacity.MaxWeightKg.Value * 100m);
        if (capacity.MaxVolumeM3.HasValue && capacity.UsedVolumeM3.HasValue && capacity.MaxVolumeM3 > 0) ratios.Add(capacity.UsedVolumeM3.Value / capacity.MaxVolumeM3.Value * 100m);
        if (capacity.MaxPalletEquivalent.HasValue && capacity.UsedPalletEquivalent.HasValue && capacity.MaxPalletEquivalent > 0) ratios.Add(capacity.UsedPalletEquivalent.Value / capacity.MaxPalletEquivalent.Value * 100m);
        return ratios.Count == 0 ? null : decimal.Round(ratios.Max(), 2);
    }
    private static void ValidateMapLayout(decimal? x, decimal? y, decimal? width, decimal? height)
    {
        var values = new[] { x, y, width, height };
        var populated = values.Count(value => value.HasValue);
        if (populated == 0) return;
        if (populated != 4) throw new BusinessRuleException("Layout phải có đủ X, Y, Width và Height hoặc để trống toàn bộ.");
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > 100 || y > 100 || width > 100 || height > 100 || x + width > 100 || y + height > 100)
            throw new BusinessRuleException("Layout phải nằm trong canvas 0–100% và không vượt biên.");
    }
    private static string Required(string value,string label)=>string.IsNullOrWhiteSpace(value)?throw new BusinessRuleException($"{label} là bắt buộc."):value.Trim();
    private static string? NormalizeStructurePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant()).ToArray();
        if (parts.Length != 5) throw new BusinessRuleException("Đường dẫn cấu trúc phải gồm đúng 5 cấp: Zone/Aisle/Rack/Level/Bin.");
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 32 || part.Any(ch => !((ch >= 'A' && ch <= 'Z') || char.IsDigit(ch) || ch is '-' or '_')))
                throw new BusinessRuleException("Mỗi cấp cấu trúc chỉ được dùng chữ A-Z, số, dấu gạch ngang hoặc gạch dưới.");
        }
        var normalized = string.Join("/", parts);
        if (normalized.Length > 160) throw new BusinessRuleException("Đường dẫn cấu trúc vượt quá độ dài cho phép.");
        return normalized;
    }
    private static string? NormalizeStorageClass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized=value.Trim().ToUpperInvariant();
        if (normalized.Length>32 || normalized.Any(ch=>!((ch>='A'&&ch<='Z')||char.IsDigit(ch)||ch is '-' or '_')))
            throw new BusinessRuleException("Storage Class chỉ được dùng chữ A-Z, số, dấu gạch ngang hoặc gạch dưới.");
        return normalized;
    }
    private static void ValidateCapacityLimits(decimal? weight,decimal? volume,decimal? pallet)
    {
        if(weight.HasValue&&weight<=0)throw new BusinessRuleException("Giới hạn trọng lượng phải lớn hơn 0.");
        if(volume.HasValue&&volume<=0)throw new BusinessRuleException("Giới hạn thể tích phải lớn hơn 0.");
        if(pallet.HasValue&&pallet<=0)throw new BusinessRuleException("Giới hạn pallet-equivalent phải lớn hơn 0.");
    }
    private async Task EnsureExistingStockFitsConstraintsAsync(int locationId,string? storageClass,decimal? maxWeight,decimal? maxVolume,decimal? maxPallet,CancellationToken token)
    {
        var rows=await context.InventoryStocks.AsNoTracking().Where(x=>x.LocationId==locationId&&x.Quantity>0)
            .Join(context.Products.AsNoTracking(),s=>s.ProductId,p=>p.Id,(s,p)=>new{s.Quantity,p.Code,p.StorageClass,p.UnitWeightKg,p.UnitVolumeM3,p.UnitPalletEquivalent})
            .ToListAsync(token);
        if(storageClass is not null&&rows.Any(x=>!string.Equals(x.StorageClass,storageClass,StringComparison.Ordinal)))
            throw Conflict("Không thể áp Storage Class vì vị trí đang chứa sản phẩm không tương thích.");
        if(maxWeight.HasValue)
        {
            if(rows.Any(x=>!x.UnitWeightKg.HasValue))throw Conflict("Không thể áp giới hạn trọng lượng vì có sản phẩm trong vị trí chưa có UnitWeightKg.");
            if(rows.Sum(x=>x.Quantity*x.UnitWeightKg!.Value)>maxWeight.Value)throw Conflict("Giới hạn trọng lượng mới thấp hơn tải hiện tại của vị trí.");
        }
        if(maxVolume.HasValue)
        {
            if(rows.Any(x=>!x.UnitVolumeM3.HasValue))throw Conflict("Không thể áp giới hạn thể tích vì có sản phẩm trong vị trí chưa có UnitVolumeM3.");
            if(rows.Sum(x=>x.Quantity*x.UnitVolumeM3!.Value)>maxVolume.Value)throw Conflict("Giới hạn thể tích mới thấp hơn tải hiện tại của vị trí.");
        }
        if(maxPallet.HasValue)
        {
            if(rows.Any(x=>!x.UnitPalletEquivalent.HasValue))throw Conflict("Không thể áp giới hạn pallet-equivalent vì có sản phẩm trong vị trí chưa có UnitPalletEquivalent.");
            if(rows.Sum(x=>x.Quantity*x.UnitPalletEquivalent!.Value)>maxPallet.Value)throw Conflict("Giới hạn pallet-equivalent mới thấp hơn tải hiện tại của vị trí.");
        }
    }
    private async Task EnsurePutawayCapacityAsync(WarehouseLocation location,Product product,decimal incomingBaseQty,CancellationToken token)
    {
        if(location.StorageClass is not null&&!string.Equals(location.StorageClass,product.StorageClass,StringComparison.Ordinal))
            throw new BusinessRuleException($"Storage Class không tương thích. Vị trí yêu cầu {location.StorageClass}, sản phẩm là {product.StorageClass ?? "CHƯA CẤU HÌNH"}.");
        var rows=await context.InventoryStocks.AsNoTracking().Where(x=>x.LocationId==location.Id&&x.Quantity>0)
            .Join(context.Products.AsNoTracking(),s=>s.ProductId,p=>p.Id,(s,p)=>new{s.Quantity,p.Code,p.StorageClass,p.UnitWeightKg,p.UnitVolumeM3,p.UnitPalletEquivalent})
            .ToListAsync(token);
        if(location.StorageClass is not null&&rows.Any(x=>!string.Equals(x.StorageClass,location.StorageClass,StringComparison.Ordinal)))
            throw Conflict("Vị trí đang có tồn kho không tương thích Storage Class; cần xử lý master data trước khi tiếp tục.");
        if(location.MaxWeightKg.HasValue)
        {
            if(!product.UnitWeightKg.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitWeightKg nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitWeightKg.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitWeightKg nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitWeightKg!.Value)+incomingBaseQty*product.UnitWeightKg.Value;
            if(projected>location.MaxWeightKg.Value)throw new BusinessRuleException($"Vượt giới hạn trọng lượng vị trí: {projected:0.######}/{location.MaxWeightKg.Value:0.######} kg.");
        }
        if(location.MaxVolumeM3.HasValue)
        {
            if(!product.UnitVolumeM3.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitVolumeM3 nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitVolumeM3.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitVolumeM3 nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitVolumeM3!.Value)+incomingBaseQty*product.UnitVolumeM3.Value;
            if(projected>location.MaxVolumeM3.Value)throw new BusinessRuleException($"Vượt giới hạn thể tích vị trí: {projected:0.########}/{location.MaxVolumeM3.Value:0.########} m³.");
        }
        if(location.MaxPalletEquivalent.HasValue)
        {
            if(!product.UnitPalletEquivalent.HasValue)throw new BusinessRuleException("Sản phẩm chưa có UnitPalletEquivalent nên không thể xác minh sức chứa.");
            if(rows.Any(x=>!x.UnitPalletEquivalent.HasValue))throw Conflict("Vị trí có tồn kho thiếu UnitPalletEquivalent nên không thể xác minh sức chứa.");
            var projected=rows.Sum(x=>x.Quantity*x.UnitPalletEquivalent!.Value)+incomingBaseQty*product.UnitPalletEquivalent.Value;
            if(projected>location.MaxPalletEquivalent.Value)throw new BusinessRuleException($"Vượt giới hạn pallet-equivalent vị trí: {projected:0.########}/{location.MaxPalletEquivalent.Value:0.########}.");
        }
    }
    private static void ValidateFlags(WarehouseLocationType type,bool pickable,bool receivable){if(type==WarehouseLocationType.Storage&&!pickable)throw new BusinessRuleException("Vị trí lưu trữ phải cho phép lấy hàng.");if((type is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected)&&pickable)throw new BusinessRuleException("Vị trí hư hỏng hoặc từ chối không được cho phép lấy hàng.");if(receivable)throw new BusinessRuleException("Chỉ vị trí nhận hàng do hệ thống quản lý được phép nhận hàng.");}
    private void ApplyLocationVersion(WarehouseLocation x,string encoded){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!x.RowVersion.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");context.Entry(x).Property(y=>y.RowVersion).OriginalValue=expected;}
}
