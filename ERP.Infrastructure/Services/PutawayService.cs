using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class PutawayService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouses, ICurrentUser currentUser)
    : IPutawayService, IReceiptPutawayIntegration
{
    private const string LocationManagePermission = "location.manage";
    public async Task<IReadOnlyList<WarehouseLocationDto>> ListLocationsAsync(int warehouseId, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var canManage = await HasPermissionAsync(LocationManagePermission, token);
        return (await LocationQuery().Where(x => x.WarehouseId == warehouseId).OrderBy(x => x.Code).ToListAsync(token))
            .Select(location => Location(location, canManage)).ToList();
    }

    public async Task<WarehouseLocationDto> GetLocationAsync(int id, CancellationToken token = default)
    {
        var entity = await LocationQuery().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy vị trí hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        return Location(entity, await HasPermissionAsync(LocationManagePermission, token));
    }

    public async Task<WarehouseLocationDto> ResolveLocationBarcodeAsync(string barcode, int? warehouseId = null, CancellationToken token = default)
    {
        var normalized = Required(barcode, "Mã vạch vị trí");
        IReadOnlyList<int> allowed;
        if (warehouseId.HasValue)
        {
            await warehouses.EnsureWarehouseAccessAsync(warehouseId.Value, token);
            allowed = [warehouseId.Value];
        }
        else
        {
            allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        }

        var matches = await LocationQuery()
            .Where(x => allowed.Contains(x.WarehouseId) && x.Barcode == normalized)
            .Take(2)
            .ToListAsync(token);
        if (matches.Count == 0) throw new NotFoundException("Không tìm thấy vị trí hoặc bạn không có quyền truy cập.");
        if (matches.Count > 1) throw Conflict("Mã vạch vị trí không duy nhất trong phạm vi truy cập. Hãy chọn kho trước khi quét.");
        return Location(matches[0], await HasPermissionAsync(LocationManagePermission, token));
    }

    public async Task<WarehouseLocationDto> CreateLocationAsync(CreateWarehouseLocationDto dto, CancellationToken token = default)
    {
        await warehouses.EnsureWarehouseAccessAsync(dto.WarehouseId, token);
        var code = Required(dto.Code, "Mã vị trí").ToUpperInvariant();
        var name = Required(dto.Name, "Tên vị trí");
        if (!Enum.TryParse<WarehouseLocationType>(dto.LocationType, true, out var type) || type is WarehouseLocationType.Receiving or WarehouseLocationType.Legacy)
            throw new BusinessRuleException("Loại vị trí không hợp lệ cho vị trí do người dùng tạo.");
        if (!dto.ZoneId.HasValue)
            throw new BusinessRuleException("Khu vực là bắt buộc cho vị trí do người dùng tạo.");
        var zone = await context.WarehouseZones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.ZoneId && x.WarehouseId == dto.WarehouseId, token)
            ?? throw new NotFoundException("Không tìm thấy khu vực hoặc bạn không có quyền truy cập.");
        if (!zone.IsActive) throw new BusinessRuleException("Không thể tạo vị trí trong khu vực ngừng hoạt động.");

        if (dto.RackLevelId.HasValue)
        {
            var rackLevelZoneId = await context.WarehouseRackLevels.AsNoTracking()
                .Where(x => x.Id == dto.RackLevelId)
                .Select(x => (int?)x.Rack.Aisle.ZoneId)
                .SingleOrDefaultAsync(token);
            if (!rackLevelZoneId.HasValue || rackLevelZoneId.Value != zone.Id)
                throw new BusinessRuleException("Tầng kệ không thuộc khu vực đã chọn.");
        }

        if (await context.WarehouseLocations.AnyAsync(x => x.WarehouseId == dto.WarehouseId && x.Code == code, token))
            throw Conflict("Mã vị trí đã tồn tại trong kho.");
        ValidateFlags(type, dto.IsPickable, dto.IsReceivable);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var entity = new WarehouseLocation
        {
            WarehouseId = dto.WarehouseId,
            ZoneId = zone.Id,
            RackLevelId = dto.RackLevelId,
            Code = code,
            Name = name,
            Barcode = Optional(dto.Barcode),
            PickPriority = dto.PickPriority,
            PutawayPriority = dto.PutawayPriority,
            LocationType = type,
            IsPickable = dto.IsPickable,
            IsReceivable = dto.IsReceivable,
            CreatedBy = currentUser.UserId
        };
        context.WarehouseLocations.Add(entity);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit(currentUser.UserId, "WarehouseLocation.Created", entity.Id, dto.WarehouseId, $"Code: {code}; ZoneId: {zone.Id}; RackLevelId: {dto.RackLevelId}"));
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        var created = await LocationQuery().SingleAsync(x => x.Id == entity.Id, token);
        return Location(created, true);
    }

    public async Task<WarehouseLocationDto> UpdateLocationAsync(int id, UpdateWarehouseLocationDto dto, CancellationToken token = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var entity = await LocationForMutation(id, token) ?? throw new NotFoundException("Không tìm thấy vị trí hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        ApplyLocationVersion(entity, dto.RowVersion);
        if (entity.IsSystemManaged && (!dto.IsActive || dto.IsBlocked || dto.IsPickable != entity.IsPickable || dto.IsReceivable != entity.IsReceivable))
            throw new BusinessRuleException("Không thể thay đổi thuộc tính vận hành của vị trí hệ thống.");
        ValidateFlags(entity.LocationType, dto.IsPickable, dto.IsReceivable);

        if (!entity.IsSystemManaged)
        {
            if (entity.ZoneId.HasValue && dto.ZoneId.HasValue && entity.ZoneId != dto.ZoneId)
                throw new BusinessRuleException("Không thể đổi khu vực của vị trí đã được gắn cấu trúc. Hãy tạo vị trí mới.");
            if (entity.RackLevelId.HasValue && dto.RackLevelId.HasValue && entity.RackLevelId != dto.RackLevelId)
                throw new BusinessRuleException("Không thể đổi tầng kệ của vị trí đã được gắn cấu trúc. Hãy tạo vị trí mới.");
            if (dto.RackLevelId.HasValue && !dto.ZoneId.HasValue && !entity.ZoneId.HasValue)
                throw new BusinessRuleException("Phải chọn khu vực trước khi gắn tầng kệ.");

            if (dto.ZoneId.HasValue)
            {
                var zone = await context.WarehouseZones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.ZoneId && x.WarehouseId == entity.WarehouseId, token)
                    ?? throw new NotFoundException("Không tìm thấy khu vực hoặc bạn không có quyền truy cập.");
                if (!zone.IsActive) throw new BusinessRuleException("Không thể gắn vị trí vào khu vực ngừng hoạt động.");
                if (dto.RackLevelId.HasValue)
                {
                    var rackLevelZoneId = await context.WarehouseRackLevels.AsNoTracking()
                        .Where(x => x.Id == dto.RackLevelId)
                        .Select(x => (int?)x.Rack.Aisle.ZoneId)
                        .SingleOrDefaultAsync(token);
                    if (!rackLevelZoneId.HasValue || rackLevelZoneId.Value != zone.Id)
                        throw new BusinessRuleException("Tầng kệ không thuộc khu vực đã chọn.");
                }
                entity.ZoneId ??= zone.Id;
                entity.RackLevelId ??= dto.RackLevelId;
            }
        }
        if (!dto.IsActive && (await context.InventoryStocks.AnyAsync(x => x.LocationId == id && x.Quantity != 0, token) ||
            await context.PutawayTaskItems.AnyAsync(x => x.SourceLocationId == id && x.PutawayTask.Status != PutawayTaskStatus.Completed && x.PutawayTask.Status != PutawayTaskStatus.Cancelled, token)))
            throw Conflict("Không thể ngừng hoạt động vị trí đang có tồn kho hoặc nhiệm vụ chưa hoàn tất.");
        entity.Name = Required(dto.Name, "Tên vị trí");
        entity.Barcode = Optional(dto.Barcode);
        entity.PickPriority = dto.PickPriority;
        entity.PutawayPriority = dto.PutawayPriority;
        entity.IsActive = dto.IsActive;
        entity.IsBlocked = dto.IsBlocked;
        entity.IsPickable = dto.IsPickable;
        entity.IsReceivable = dto.IsReceivable;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
        context.AuditLogs.Add(Audit(currentUser.UserId, "WarehouseLocation.Updated", entity.Id, entity.WarehouseId, $"Active: {entity.IsActive}; Blocked: {entity.IsBlocked}"));
        await context.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        var updated = await LocationQuery().SingleAsync(x => x.Id == entity.Id, token);
        return Location(updated, true);
    }

    public async Task<int> GetReceivingLocationIdAsync(int warehouseId, CancellationToken token = default) =>
        await context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.LocationType == WarehouseLocationType.Receiving && x.IsSystemManaged && x.IsActive)
            .Select(x => x.Id).SingleOrDefaultAsync(token) is var id && id > 0 ? id : throw new BusinessRuleException("Kho chưa được cấu hình vị trí nhận hàng.");

    public async Task CreateForPostedReceiptAsync(ImportReceipt receipt, int receivingLocationId, int actorId, CancellationToken token = default)
    {
        if (await context.PutawayTasks.AnyAsync(x => x.ReceiptId == receipt.Id, token)) throw Conflict("Nhiệm vụ cất hàng của phiếu nhập đã tồn tại.");
        var task = new PutawayTask { ReceiptId = receipt.Id, WarehouseId = receipt.WarehouseId, CreatedBy = actorId, CreatedAt = DateTime.UtcNow };
        foreach (var line in receipt.Details)
        {
            Add(line, InventoryStatus.Available, line.AcceptedQuantity, line.BaseAcceptedQuantity);
            Add(line, InventoryStatus.Damaged, line.DamagedQuantity, line.BaseDamagedQuantity);
            Add(line, InventoryStatus.Rejected, line.RejectedQuantity, line.BaseRejectedQuantity);
        }
        if (task.Items.Count == 0) throw new BusinessRuleException("Phiếu nhập không có số lượng hợp lệ để tạo nhiệm vụ cất hàng.");
        context.PutawayTasks.Add(task);
        await context.SaveChangesAsync(token);
        context.AuditLogs.Add(Audit(actorId, "PutawayTask.Created", task.Id, receipt.WarehouseId, $"ReceiptId: {receipt.Id}; Items: {task.Items.Count}"));
        void Add(ImportReceiptDetail line, InventoryStatus status, decimal operation, decimal quantity)
        {
            if (quantity <= 0) return;
            task.Items.Add(new PutawayTaskItem { ReceiptLineId = line.Id, ProductId = line.ProductId, InventoryStatus = status, SourceLocationId = receivingLocationId,
                OperationUnitId = line.OperationUnitId, OperationUnitCodeSnapshot = line.OperationUnitCodeSnapshot, BaseUnitId = line.BaseUnitId,
                BaseUnitCodeSnapshot = line.BaseUnitCodeSnapshot, ConversionFactorSnapshot = line.ConversionFactor, ConversionVersionSnapshot = line.ConversionVersion,
                BaseUnitDecimalPlaces = line.BaseUnitDecimalPlaces, RequiredOperationQuantity = operation, RequiredBaseQuantity = quantity });
        }
    }

    public async Task<IReadOnlyList<PutawayTaskListDto>> ListAsync(CancellationToken token = default)
    {
        var ids = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        return await context.PutawayTasks.AsNoTracking().Where(x => ids.Contains(x.WarehouseId)).OrderByDescending(x => x.CreatedAt)
            .Select(x => new PutawayTaskListDto { Id=x.Id, ReceiptId=x.ReceiptId, ReceiptCode=x.Receipt.Code, WarehouseId=x.WarehouseId, WarehouseName=x.Warehouse.Name,
                Status=x.Status.ToString(), AssignedUserId=x.AssignedUserId, RequiredBaseQuantity=x.Items.Sum(i=>i.RequiredBaseQuantity), MovedBaseQuantity=x.Items.Sum(i=>i.MovedBaseQuantity), CreatedAt=x.CreatedAt }).ToListAsync(token);
    }

    public async Task<PutawayTaskDto> GetAsync(int id, CancellationToken token = default)
    {
        var task = await Query().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(task.WarehouseId, token); return await MapAsync(task, token);
    }

    public async Task<IReadOnlyList<WarehouseLocationDto>> GetDestinationsAsync(int taskId, int itemId, CancellationToken token = default)
    {
        var task = await Load(taskId, token); var item = task.Items.SingleOrDefault(x=>x.Id==itemId) ?? throw new NotFoundException("Không tìm thấy dòng hàng.");
        var type = RequiredType(item.InventoryStatus);
        return (await context.WarehouseLocations.AsNoTracking().Where(x=>x.WarehouseId==task.WarehouseId && x.IsActive && !x.IsBlocked && x.LocationType==type && (item.InventoryStatus!=InventoryStatus.Available || x.IsPickable))
            .OrderBy(x=>x.Code).ToListAsync(token)).Select(location=>Location(location,false)).ToList();
    }

    public Task<PutawayTaskDto> AssignAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto, [PutawayTaskStatus.Open,PutawayTaskStatus.Assigned], task=>{ task.AssignedUserId=dto.AssignedUserId ?? throw new BusinessRuleException("Người thực hiện là bắt buộc."); task.Status=PutawayTaskStatus.Assigned; },"Assigned",token);
    public Task<PutawayTaskDto> StartAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Assigned],task=>{ EnsureActor(task); task.Status=PutawayTaskStatus.InProgress; task.StartedAt=DateTime.UtcNow; },"Started",token);
    public Task<PutawayTaskDto> OpenExceptionAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.InProgress],task=>{ EnsureActor(task); task.ExceptionReason=string.IsNullOrWhiteSpace(dto.Reason)?throw new BusinessRuleException("Lý do cần xử lý là bắt buộc."):dto.Reason.Trim(); task.Status=PutawayTaskStatus.Exception; },"ExceptionOpened",token);
    public Task<PutawayTaskDto> ResumeAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Exception],task=>{ EnsureActor(task); task.Status=PutawayTaskStatus.InProgress; task.ExceptionReason=null; },"Resumed",token);
    public Task<PutawayTaskDto> CancelAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default) => Mutate(id,dto,[PutawayTaskStatus.Open,PutawayTaskStatus.Assigned],task=>{ if(task.Items.Any(x=>x.MovedBaseQuantity>0)) throw Conflict("Không thể hủy nhiệm vụ đã phát sinh di chuyển."); task.Status=PutawayTaskStatus.Cancelled; task.CancelledAt=DateTime.UtcNow; },"Cancelled",token);

    public async Task<PutawayTaskDto> MoveAsync(int id, MovePutawayItemDto dto, CancellationToken token = default)
    {
        if(dto.Quantity<=0) throw new BusinessRuleException("Số lượng cất phải lớn hơn 0.");
        await using var tx=context.Database.CurrentTransaction is null?await context.Database.BeginTransactionAsync(token):null;
        try
        {
            var task=await Load(id,token); if(task.Status is not PutawayTaskStatus.Assigned and not PutawayTaskStatus.InProgress) throw Conflict("Nhiệm vụ không ở trạng thái có thể cất hàng."); EnsureActor(task);
            ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));
            var item=task.Items.SingleOrDefault(x=>x.Id==dto.ItemId)??throw new NotFoundException("Không tìm thấy dòng hàng.");
            var destination=await LocationForMutation(dto.DestinationLocationId,token)??throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(destination.WarehouseId!=task.WarehouseId) throw new NotFoundException("Không tìm thấy vị trí đích.");
            if(!destination.IsActive||destination.IsBlocked||destination.LocationType!=RequiredType(item.InventoryStatus)||(item.InventoryStatus==InventoryStatus.Available&&!destination.IsPickable)) throw new BusinessRuleException("Vị trí đích không phù hợp với hàng hóa này.");
            var baseQty=ToBase(dto.Quantity,dto.UnitCode,item); if(baseQty>item.RemainingBaseQuantity) throw new BusinessRuleException("Số lượng cất vượt quá số lượng còn lại.");
            var source=await context.InventoryStocks.SingleOrDefaultAsync(x=>x.ProductId==item.ProductId&&x.WarehouseId==task.WarehouseId&&x.Status==item.InventoryStatus&&x.LocationId==item.SourceLocationId,token);
            if(source is null||source.Quantity-source.ReservedQuantity<baseQty) throw Conflict("Tồn tại vị trí nguồn đã thay đổi. Vui lòng tải lại.");
            var dest=await context.InventoryStocks.SingleOrDefaultAsync(x=>x.ProductId==item.ProductId&&x.WarehouseId==task.WarehouseId&&x.Status==item.InventoryStatus&&x.LocationId==destination.Id,token);
            source.Quantity-=baseQty; source.LastUpdated=DateTime.UtcNow;
            if(dest is null) context.InventoryStocks.Add(new InventoryStock{ProductId=item.ProductId,WarehouseId=task.WarehouseId,Status=item.InventoryStatus,LocationId=destination.Id,Quantity=baseQty,LastUpdated=DateTime.UtcNow}); else {dest.Quantity+=baseQty;dest.LastUpdated=DateTime.UtcNow;}
            item.MovedBaseQuantity+=baseQty; task.Status=task.Items.All(x=>x.RemainingBaseQuantity==0)?PutawayTaskStatus.Completed:PutawayTaskStatus.InProgress; task.StartedAt??=DateTime.UtcNow; if(task.Status==PutawayTaskStatus.Completed)task.CompletedAt=DateTime.UtcNow;
            context.InventoryLocationMovements.Add(new InventoryLocationMovement{WarehouseId=task.WarehouseId,ProductId=item.ProductId,InventoryStatus=item.InventoryStatus,FromLocationId=item.SourceLocationId,ToLocationId=destination.Id,BaseQuantity=baseQty,EnteredQuantity=dto.Quantity,EnteredUnitCode=dto.UnitCode.Trim().ToUpperInvariant(),PutawayTaskId=task.Id,PutawayTaskItemId=item.Id,ReceiptId=task.ReceiptId,ReceiptLineId=item.ReceiptLineId,CreatedBy=currentUser.UserId});
            context.AuditLogs.Add(Audit(currentUser.UserId,"PutawayTask.Moved",task.Id,task.WarehouseId,$"ItemId: {item.Id}; BaseQuantity: {baseQty}; DestinationId: {destination.Id}"));
            await context.SaveChangesAsync(token); if(tx is not null)await tx.CommitAsync(token); return await GetAsync(id,token);
        }
        catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);} catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
    }

    private async Task<PutawayTaskDto> Mutate(int id,PutawayStateCommandDto dto,PutawayTaskStatus[] allowed,Action<PutawayTask> change,string action,CancellationToken token)
    {
        await using var tx=context.Database.CurrentTransaction is null?await context.Database.BeginTransactionAsync(token):null; try{var task=await Load(id,token);if(!allowed.Contains(task.Status))throw Conflict("Trạng thái nhiệm vụ không còn phù hợp.");ApplyVersion(task.RowVersion,dto.RowVersion,context.Entry(task).Property(x=>x.RowVersion));change(task);context.AuditLogs.Add(Audit(currentUser.UserId,$"PutawayTask.{action}",task.Id,task.WarehouseId,$"Status: {task.Status}"));await context.SaveChangesAsync(token);if(tx is not null)await tx.CommitAsync(token);return await GetAsync(id,token);}catch(DbUpdateConcurrencyException ex){if(tx is not null)await tx.RollbackAsync(token);throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}catch{if(tx is not null)await tx.RollbackAsync(token);throw;}
    }
    private async Task<PutawayTask> Load(int id,CancellationToken token){var task=await Query().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new NotFoundException("Không tìm thấy nhiệm vụ hoặc bạn không có quyền truy cập.");await warehouses.EnsureWarehouseAccessAsync(task.WarehouseId,token);return task;}
    // Hold destination eligibility through commit, including a concurrent master deactivation.
    private Task<WarehouseLocation?> LocationForMutation(int id, CancellationToken token) =>
        (context.Database.IsSqlServer()
            ? context.WarehouseLocations.FromSqlInterpolated($"SELECT * FROM dbo.WarehouseLocations WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            : context.WarehouseLocations.Where(x => x.Id == id)).SingleOrDefaultAsync(token);
    private IQueryable<WarehouseLocation> LocationQuery()=>context.WarehouseLocations.AsNoTracking()
        .Include(x=>x.Zone)
        .Include(x=>x.RackLevel).ThenInclude(x=>x!.Rack).ThenInclude(x=>x.Aisle);

    private IQueryable<PutawayTask> Query()=>context.PutawayTasks.Include(x=>x.Receipt).Include(x=>x.Warehouse).Include(x=>x.Items).ThenInclude(x=>x.ReceiptLine).ThenInclude(x=>x.Product).Include(x=>x.Items).ThenInclude(x=>x.SourceLocation);
    private void EnsureActor(PutawayTask task){if(task.AssignedUserId!=currentUser.UserId&&!currentUser.IsGlobalAdmin&&currentUser.Role is not "Admin" and not "Manager")throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");}
    private static decimal ToBase(decimal qty,string unit,PutawayTaskItem item){var code=unit.Trim().ToUpperInvariant();var result=code==item.BaseUnitCodeSnapshot.ToUpperInvariant()?qty:code==item.OperationUnitCodeSnapshot.ToUpperInvariant()?qty*item.ConversionFactorSnapshot:throw new BusinessRuleException("Đơn vị tính không được hỗ trợ.");if(decimal.Round(result,item.BaseUnitDecimalPlaces)!=result)throw new BusinessRuleException("Số lượng vượt quá độ chính xác cho phép; hệ thống không tự làm tròn.");return result;}
    private static WarehouseLocationType RequiredType(InventoryStatus s)=>s switch{InventoryStatus.Available=>WarehouseLocationType.Storage,InventoryStatus.Damaged=>WarehouseLocationType.Damaged,InventoryStatus.Rejected=>WarehouseLocationType.Rejected,_=>throw new BusinessRuleException("Trạng thái tồn kho không thuộc luồng cất hàng tự động.")};
    private static void ApplyVersion(byte[] actual,string encoded,Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PutawayTask,byte[]> p){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!actual.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");p.OriginalValue=expected;}
    private async Task<PutawayTaskDto> MapAsync(PutawayTask t,CancellationToken token){var moves=await context.InventoryLocationMovements.AsNoTracking().Where(x=>x.PutawayTaskId==t.Id).Join(context.WarehouseLocations,x=>x.FromLocationId,x=>x.Id,(m,f)=>new{m,f}).Join(context.WarehouseLocations,x=>x.m.ToLocationId,x=>x.Id,(x,d)=>new PutawayMovementDto{Id=x.m.Id,ItemId=x.m.PutawayTaskItemId,FromLocationCode=x.f.Code,ToLocationCode=d.Code,BaseQuantity=x.m.BaseQuantity,CreatedAt=x.m.CreatedAt}).ToListAsync(token);var canMutate=currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";return new PutawayTaskDto{Id=t.Id,ReceiptId=t.ReceiptId,ReceiptCode=t.Receipt.Code,WarehouseId=t.WarehouseId,WarehouseName=t.Warehouse.Name,Status=t.Status.ToString(),AssignedUserId=t.AssignedUserId,RequiredBaseQuantity=t.Items.Sum(x=>x.RequiredBaseQuantity),MovedBaseQuantity=t.Items.Sum(x=>x.MovedBaseQuantity),CreatedAt=t.CreatedAt,RowVersion=canMutate?Convert.ToBase64String(t.RowVersion):null,ExceptionReason=canMutate?t.ExceptionReason:null,Items=t.Items.Select(x=>new PutawayTaskItemDto{Id=x.Id,ReceiptLineId=x.ReceiptLineId,ProductId=x.ProductId,ProductCode=x.ReceiptLine.Product.Code,ProductName=x.ReceiptLine.Product.Name,InventoryStatus=x.InventoryStatus.ToString(),SourceLocationId=x.SourceLocationId,SourceLocationCode=x.SourceLocation.Code,OperationUnitCode=x.OperationUnitCodeSnapshot,BaseUnitCode=x.BaseUnitCodeSnapshot,RequiredOperationQuantity=x.RequiredOperationQuantity,RequiredBaseQuantity=x.RequiredBaseQuantity,MovedBaseQuantity=x.MovedBaseQuantity,RemainingBaseQuantity=x.RemainingBaseQuantity,RowVersion=canMutate?Convert.ToBase64String(x.RowVersion):null}).ToList(),Movements=moves};}
    private static AuditLog Audit(int user,string action,int entity,int warehouse,string values)=>new(){UserId=user,Action=action,EntityName="PutawayTask",EntityId=entity,WarehouseId=warehouse,NewValues=values,Result="Success",Timestamp=DateTime.UtcNow};
    private static BusinessRuleException Conflict(string message){var e=new BusinessRuleException(message);e.Data["HttpStatusCode"]=409;return e;}
    private WarehouseLocationDto Location(WarehouseLocation x,bool includeVersion)=>new(){Id=x.Id,WarehouseId=x.WarehouseId,ZoneId=x.ZoneId,ZoneCode=x.Zone?.Code,RackLevelId=x.RackLevelId,AisleCode=x.RackLevel?.Rack?.Aisle?.Code,RackCode=x.RackLevel?.Rack?.Code,LevelNo=x.RackLevel?.LevelNo,Code=x.Code,Name=x.Name,Barcode=x.Barcode,PickPriority=x.PickPriority,PutawayPriority=x.PutawayPriority,LocationType=x.LocationType.ToString(),IsActive=x.IsActive,IsBlocked=x.IsBlocked,IsPickable=x.IsPickable,IsReceivable=x.IsReceivable,IsSystemManaged=x.IsSystemManaged,RowVersion=includeVersion?Convert.ToBase64String(x.RowVersion):null};
    private Task<bool> HasPermissionAsync(string code,CancellationToken token)=>context.Users.AsNoTracking().Where(user=>user.Id==currentUser.UserId&&user.IsActive).Select(user=>user.Role.Permissions.Any(grant=>grant.Permission.Code==code)).SingleOrDefaultAsync(token);
    private static string Required(string value,string label)=>string.IsNullOrWhiteSpace(value)?throw new BusinessRuleException($"{label} là bắt buộc."):value.Trim();
    private static string? Optional(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static void ValidateFlags(WarehouseLocationType type,bool pickable,bool receivable){if((type is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected)&&pickable)throw new BusinessRuleException("Vị trí hư hỏng hoặc từ chối không được cho phép lấy hàng.");if(receivable)throw new BusinessRuleException("Chỉ vị trí nhận hàng do hệ thống quản lý được phép nhận hàng.");}
    private void ApplyLocationVersion(WarehouseLocation x,string encoded){byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}if(!x.RowVersion.SequenceEqual(expected))throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");context.Entry(x).Property(y=>y.RowVersion).OriginalValue=expected;}
}
