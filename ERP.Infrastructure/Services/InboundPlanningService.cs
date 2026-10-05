using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ERP.Infrastructure.Services;

public sealed class InboundPlanningService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouses,
    ICurrentUser currentUser) : IInboundPlanningService
{
    public async Task<IReadOnlyList<PurchaseOrderListDto>> ListPurchaseOrdersAsync(PurchaseOrderStatus? status = null, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var query = context.PurchaseOrders.AsNoTracking().Where(x => allowed.Contains(x.WarehouseId));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query.OrderByDescending(x => x.CreatedAtUtc).Select(x => new PurchaseOrderListDto
        {
            Id=x.Id, ExternalPoId=x.ExternalPoId, Code=x.Code, SupplierCode=x.Supplier.Code, SupplierName=x.Supplier.Name,
            WarehouseId=x.WarehouseId, WarehouseName=x.Warehouse.Name, Status=x.Status.ToString(), OrderDate=x.OrderDate,
            ExpectedDate=x.ExpectedDate, BaseOrderedQuantity=x.Lines.Sum(l=>l.BaseOrderedQuantity)
        }).ToListAsync(token);
    }

    public async Task<PurchaseOrderDto> GetPurchaseOrderAsync(int id, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity = await PurchaseOrderQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), token)
            ?? throw new NotFoundException("Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.");
        return Map(entity);
    }

    public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderDto dto, CancellationToken token = default)
    {
        await ValidatePurchaseOrderHeaderAsync(dto.SupplierId, dto.WarehouseId, token);
        var source = NormalizeCode(dto.SourceSystem, "Hệ thống nguồn", 50);
        var external = Required(dto.ExternalPoId, "Mã PO ngoài hệ thống", 100);
        var code = NormalizeCode(dto.Code, "Mã đơn mua", 50);
        if (await context.PurchaseOrders.AnyAsync(x => x.SourceSystem == source && x.ExternalPoId == external, token))
            throw Conflict("Đơn mua với mã nguồn này đã tồn tại.");
        if (await context.PurchaseOrders.AnyAsync(x => x.WarehouseId == dto.WarehouseId && x.Code == code, token))
            throw Conflict("Mã đơn mua đã tồn tại trong kho.");
        if (dto.Lines.Count == 0) throw new BusinessRuleException("Đơn mua phải có ít nhất một dòng hàng.");

        var entity = new PurchaseOrder
        {
            ExternalPoId=external, SourceSystem=source, Code=code, SupplierId=dto.SupplierId, WarehouseId=dto.WarehouseId,
            OrderDate=dto.OrderDate == default ? DateTime.UtcNow.Date : dto.OrderDate, ExpectedDate=dto.ExpectedDate,
            Currency=NormalizeOptional(dto.Currency,10), ExternalVersion=NormalizeOptional(dto.ExternalVersion,100),
            CreatedAtUtc=DateTime.UtcNow, CreatedBy=currentUser.UserId
        };
        await PopulatePurchaseOrderLinesAsync(entity, dto.Lines, token);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            context.PurchaseOrders.Add(entity);
            await SaveAsync(token);
            context.AuditLogs.Add(Audit("PurchaseOrder.Created", "PurchaseOrder", entity.Id, entity.WarehouseId, $"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
            await SaveAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(token);
            throw;
        }
        return await GetPurchaseOrderAsync(entity.Id, token);
    }

    public async Task<PurchaseOrderDto> UpdatePurchaseOrderAsync(int id, UpdatePurchaseOrderDto dto, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity = await PurchaseOrderQuery().SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), token)
            ?? throw new NotFoundException("Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.");
        if (entity.Status != PurchaseOrderStatus.Draft) throw Conflict("Chỉ đơn mua nháp mới được chỉnh sửa.");
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x=>x.RowVersion));
        if (NormalizeCode(dto.SourceSystem,"Hệ thống nguồn",50) != entity.SourceSystem || Required(dto.ExternalPoId,"Mã PO ngoài hệ thống",100) != entity.ExternalPoId)
            throw Conflict("Định danh PO ngoài hệ thống không được thay đổi.");
        await ValidatePurchaseOrderHeaderAsync(dto.SupplierId, dto.WarehouseId, token);
        var code=NormalizeCode(dto.Code,"Mã đơn mua",50);
        if (await context.PurchaseOrders.AnyAsync(x=>x.Id!=id&&x.WarehouseId==dto.WarehouseId&&x.Code==code,token))
            throw Conflict("Mã đơn mua đã tồn tại trong kho.");
        if (dto.Lines.Count == 0) throw new BusinessRuleException("Đơn mua phải có ít nhất một dòng hàng.");

        entity.Code=code; entity.SupplierId=dto.SupplierId; entity.WarehouseId=dto.WarehouseId;
        entity.OrderDate=dto.OrderDate; entity.ExpectedDate=dto.ExpectedDate; entity.Currency=NormalizeOptional(dto.Currency,10);
        entity.ExternalVersion=NormalizeOptional(dto.ExternalVersion,100); entity.UpdatedAtUtc=DateTime.UtcNow; entity.UpdatedBy=currentUser.UserId;
        context.PurchaseOrderLines.RemoveRange(entity.Lines); entity.Lines.Clear();
        await PopulatePurchaseOrderLinesAsync(entity,dto.Lines,token);
        context.AuditLogs.Add(Audit("PurchaseOrder.Updated","PurchaseOrder",entity.Id,entity.WarehouseId,$"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
        await SaveAsync(token);
        return await GetPurchaseOrderAsync(id,token);
    }

    public Task<PurchaseOrderDto> OpenPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id,dto,[PurchaseOrderStatus.Draft],PurchaseOrderStatus.Open,"PurchaseOrder.Opened",token);
    public Task<PurchaseOrderDto> CancelPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id,dto,[PurchaseOrderStatus.Draft,PurchaseOrderStatus.Open],PurchaseOrderStatus.Cancelled,"PurchaseOrder.Cancelled",token);
    public Task<PurchaseOrderDto> ClosePurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id,dto,[PurchaseOrderStatus.PartiallyReceived,PurchaseOrderStatus.Received],PurchaseOrderStatus.Closed,"PurchaseOrder.Closed",token);

    public async Task<IReadOnlyList<AsnListDto>> ListAsnsAsync(AsnStatus? status = null, CancellationToken token = default)
    {
        var allowed=await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var query=context.Asns.AsNoTracking().Where(x=>allowed.Contains(x.WarehouseId));
        if(status.HasValue) query=query.Where(x=>x.Status==status.Value);
        return await query.OrderByDescending(x=>x.CreatedAtUtc).Select(x=>new AsnListDto
        {
            Id=x.Id,Code=x.Code,PurchaseOrderId=x.PurchaseOrderId,PurchaseOrderCode=x.PurchaseOrder!=null?x.PurchaseOrder.Code:null,
            SupplierCode=x.Supplier.Code,SupplierName=x.Supplier.Name,WarehouseId=x.WarehouseId,WarehouseName=x.Warehouse.Name,
            Status=x.Status.ToString(),ExpectedArrivalAtUtc=x.ExpectedArrivalAtUtc,BaseExpectedQuantity=x.Lines.Sum(l=>l.BaseExpectedQuantity)
        }).ToListAsync(token);
    }

    public async Task<AsnDto> GetAsnAsync(int id, CancellationToken token = default)
    {
        var allowed=await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity=await AsnQuery().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&allowed.Contains(x.WarehouseId),token)
            ?? throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        return Map(entity);
    }

    public async Task<AsnDto> CreateAsnAsync(CreateAsnDto dto, CancellationToken token = default)
    {
        var header=await ResolveAsnHeaderAsync(dto.PurchaseOrderId,dto.SupplierId,dto.WarehouseId,token);
        var code=NormalizeCode(dto.Code,"Mã ASN",50);
        if(await context.Asns.AnyAsync(x=>x.WarehouseId==header.WarehouseId&&x.Code==code,token)) throw Conflict("Mã ASN đã tồn tại trong kho.");
        if(dto.Lines.Count==0) throw new BusinessRuleException("ASN phải có ít nhất một dòng hàng.");
        var entity=new Asn
        {
            Code=code,PurchaseOrderId=dto.PurchaseOrderId,SupplierId=header.SupplierId,WarehouseId=header.WarehouseId,
            ExpectedArrivalAtUtc=dto.ExpectedArrivalAtUtc,CarrierName=NormalizeOptional(dto.CarrierName,200),
            VehiclePlate=NormalizeOptional(dto.VehiclePlate,30)?.ToUpperInvariant(),Note=NormalizeOptional(dto.Note,500),
            CreatedAtUtc=DateTime.UtcNow,CreatedBy=currentUser.UserId
        };
        await PopulateAsnLinesAsync(entity,dto.Lines,null,token);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            context.Asns.Add(entity);
            await SaveAsync(token);
            context.AuditLogs.Add(Audit("Asn.Created","Asn",entity.Id,entity.WarehouseId,$"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
            await SaveAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(token);
            throw;
        }
        return await GetAsnAsync(entity.Id,token);
    }

    public async Task<AsnDto> UpdateAsnAsync(int id, UpdateAsnDto dto, CancellationToken token = default)
    {
        var allowed=await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity=await AsnQuery().SingleOrDefaultAsync(x=>x.Id==id&&allowed.Contains(x.WarehouseId),token)
            ?? throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        if(entity.Status!=AsnStatus.Draft) throw Conflict("Chỉ ASN nháp mới được chỉnh sửa.");
        ApplyVersion(entity.RowVersion,dto.RowVersion,context.Entry(entity).Property(x=>x.RowVersion));
        var header=await ResolveAsnHeaderAsync(dto.PurchaseOrderId,dto.SupplierId,dto.WarehouseId,token);
        var code=NormalizeCode(dto.Code,"Mã ASN",50);
        if(await context.Asns.AnyAsync(x=>x.Id!=id&&x.WarehouseId==header.WarehouseId&&x.Code==code,token)) throw Conflict("Mã ASN đã tồn tại trong kho.");
        if(dto.Lines.Count==0) throw new BusinessRuleException("ASN phải có ít nhất một dòng hàng.");

        entity.Code=code;entity.PurchaseOrderId=dto.PurchaseOrderId;entity.SupplierId=header.SupplierId;entity.WarehouseId=header.WarehouseId;
        entity.ExpectedArrivalAtUtc=dto.ExpectedArrivalAtUtc;entity.CarrierName=NormalizeOptional(dto.CarrierName,200);
        entity.VehiclePlate=NormalizeOptional(dto.VehiclePlate,30)?.ToUpperInvariant();entity.Note=NormalizeOptional(dto.Note,500);
        entity.UpdatedAtUtc=DateTime.UtcNow;entity.UpdatedBy=currentUser.UserId;
        context.AsnLines.RemoveRange(entity.Lines);entity.Lines.Clear();
        await PopulateAsnLinesAsync(entity,dto.Lines,id,token);
        context.AuditLogs.Add(Audit("Asn.Updated","Asn",entity.Id,entity.WarehouseId,$"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
        await SaveAsync(token);
        return await GetAsnAsync(id,token);
    }

    public Task<AsnDto> ConfirmAsnAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.Draft],AsnStatus.Confirmed,"Asn.Confirmed",token);
    public Task<AsnDto> MarkAsnInTransitAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.Confirmed],AsnStatus.InTransit,"Asn.MarkedInTransit",token);
    public Task<AsnDto> ArriveAsnAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.InTransit],AsnStatus.Arrived,"Asn.Arrived",token);
    public Task<AsnDto> StartReceivingAsnAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.Arrived],AsnStatus.Receiving,"Asn.ReceivingStarted",token);
    public Task<AsnDto> CompleteAsnAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.Receiving],AsnStatus.Completed,"Asn.Completed",token);
    public Task<AsnDto> CancelAsnAsync(int id,InboundStateCommandDto dto,CancellationToken token=default)=>
        MutateAsnAsync(id,dto,[AsnStatus.Draft,AsnStatus.Confirmed],AsnStatus.Cancelled,"Asn.Cancelled",token);

    private async Task<PurchaseOrderDto> MutatePurchaseOrderAsync(int id,InboundStateCommandDto dto,PurchaseOrderStatus[] allowedStates,PurchaseOrderStatus target,string action,CancellationToken token)
    {
        var accessibleWarehouseIds=await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity=await PurchaseOrderQuery().SingleOrDefaultAsync(x=>x.Id==id&&accessibleWarehouseIds.Contains(x.WarehouseId),token)??throw new NotFoundException("Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.");
        await ApplyStateMutationAsync(entity.Status,allowedStates,target,entity.RowVersion,dto.RowVersion,context.Entry(entity).Property(x=>x.RowVersion),
            ()=>{entity.Status=target;entity.UpdatedAtUtc=DateTime.UtcNow;entity.UpdatedBy=currentUser.UserId;},"Trạng thái đơn mua không cho phép thao tác này.",action,"PurchaseOrder",entity.Id,entity.WarehouseId,token);
        return await GetPurchaseOrderAsync(id,token);
    }

    private async Task<AsnDto> MutateAsnAsync(int id,InboundStateCommandDto dto,AsnStatus[] allowedStates,AsnStatus target,string action,CancellationToken token)
    {
        var accessibleWarehouseIds=await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity=await AsnQuery().SingleOrDefaultAsync(x=>x.Id==id&&accessibleWarehouseIds.Contains(x.WarehouseId),token)??throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        await ApplyStateMutationAsync(entity.Status,allowedStates,target,entity.RowVersion,dto.RowVersion,context.Entry(entity).Property(x=>x.RowVersion),
            ()=>{entity.Status=target;entity.UpdatedAtUtc=DateTime.UtcNow;entity.UpdatedBy=currentUser.UserId;},"Trạng thái ASN không cho phép thao tác này.",action,"Asn",entity.Id,entity.WarehouseId,token);
        return await GetAsnAsync(id,token);
    }

    private async Task ApplyStateMutationAsync<TStatus>(
        TStatus currentStatus,TStatus[] allowedStates,TStatus target,byte[] rowVersion,string encodedRowVersion,
        PropertyEntry rowVersionProperty,Action applyMutation,string invalidStateMessage,string action,string entityName,
        int entityId,int warehouseId,CancellationToken token) where TStatus:struct,Enum
    {
        if(!allowedStates.Contains(currentStatus)) throw Conflict(invalidStateMessage);
        ApplyVersion(rowVersion,encodedRowVersion,rowVersionProperty);
        applyMutation();
        context.AuditLogs.Add(Audit(action,entityName,entityId,warehouseId,$"Status: {target}"));
        await SaveAsync(token);
    }

    private async Task ValidatePurchaseOrderHeaderAsync(int supplierId,int warehouseId,CancellationToken token)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId,token);
        var warehouse=await context.Warehouses.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==warehouseId,token);
        if(warehouse is null||!warehouse.IsActive) throw new BusinessRuleException("Kho nhận không tồn tại hoặc đã ngừng hoạt động.");
        var supplier=await context.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==supplierId,token);
        if(supplier is null||!supplier.IsActive||!supplier.IsSupplier) throw new BusinessRuleException("Nhà cung cấp không tồn tại hoặc không hoạt động.");
    }

    private async Task<(int SupplierId,int WarehouseId)> ResolveAsnHeaderAsync(int? purchaseOrderId,int supplierId,int warehouseId,CancellationToken token)
    {
        if(purchaseOrderId.HasValue)
        {
            var po=await context.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==purchaseOrderId.Value,token)
                ?? throw new NotFoundException("Không tìm thấy đơn mua.");
            await warehouses.EnsureWarehouseAccessAsync(po.WarehouseId,token);
            if(po.Status is not PurchaseOrderStatus.Open and not PurchaseOrderStatus.PartiallyReceived) throw Conflict("Đơn mua chưa ở trạng thái cho phép lập ASN.");
            if((supplierId>0&&supplierId!=po.SupplierId)||(warehouseId>0&&warehouseId!=po.WarehouseId)) throw new BusinessRuleException("Nhà cung cấp hoặc kho của ASN không khớp đơn mua.");
            return(po.SupplierId,po.WarehouseId);
        }
        await ValidatePurchaseOrderHeaderAsync(supplierId,warehouseId,token);
        return(supplierId,warehouseId);
    }

    private async Task PopulatePurchaseOrderLinesAsync(PurchaseOrder entity,IReadOnlyList<CreatePurchaseOrderLineDto> lines,CancellationToken token)
    {
        var lineNo=0;
        var externalIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var dto in lines)
        {
            lineNo++;
            var external=Required(dto.ExternalLineId,$"Mã dòng ngoài hệ thống #{lineNo}",100);
            if(!externalIds.Add(external)) throw new BusinessRuleException("Mã dòng ngoài hệ thống bị trùng trong đơn mua.");
            ValidateTolerance(dto.AllowedOverReceiptPct,dto.AllowedUnderReceiptPct);
            var conversion=await ResolveConversionAsync(dto.ProductId,dto.OperationUnitId,dto.OrderedQuantity,token);
            entity.Lines.Add(new PurchaseOrderLine
            {
                ExternalLineId=external,LineNo=lineNo,ProductId=dto.ProductId,OrderedQuantity=dto.OrderedQuantity,
                OperationUnitId=conversion.OperationUnitId,OperationUnitCodeSnapshot=conversion.OperationUnitCode,
                OperationUnitDecimalPlaces=conversion.OperationDecimals,BaseUnitId=conversion.BaseUnitId,BaseUnitCodeSnapshot=conversion.BaseUnitCode,
                BaseUnitDecimalPlaces=conversion.BaseDecimals,ConversionFactorSnapshot=conversion.Factor,ConversionVersionSnapshot=conversion.Version,
                BaseOrderedQuantity=conversion.BaseQuantity,AllowedOverReceiptPct=dto.AllowedOverReceiptPct,AllowedUnderReceiptPct=dto.AllowedUnderReceiptPct
            });
        }
    }

    private async Task PopulateAsnLinesAsync(Asn entity,IReadOnlyList<CreateAsnLineDto> lines,int? excludeAsnId,CancellationToken token)
    {
        var lineNo=0;
        foreach(var dto in lines)
        {
            lineNo++;
            PurchaseOrderLine? poLine=null;
            if(dto.PurchaseOrderLineId.HasValue)
            {
                poLine=await context.PurchaseOrderLines.AsNoTracking().Include(x=>x.PurchaseOrder)
                    .SingleOrDefaultAsync(x=>x.Id==dto.PurchaseOrderLineId.Value&&x.PurchaseOrderId==entity.PurchaseOrderId,token)
                    ?? throw new NotFoundException("Không tìm thấy dòng đơn mua hoặc bạn không có quyền truy cập.");
                if(poLine.ProductId!=dto.ProductId) throw new BusinessRuleException("Sản phẩm ASN không khớp dòng đơn mua.");
            }
            else if(entity.PurchaseOrderId.HasValue) throw new BusinessRuleException("ASN có đơn mua phải tham chiếu từng dòng đơn mua.");

            var conversion=await ResolveConversionAsync(dto.ProductId,dto.OperationUnitId,dto.ExpectedQuantity,token);
            if(poLine is not null)
            {
                var existing=await context.AsnLines.AsNoTracking()
                    .Where(x=>x.PurchaseOrderLineId==poLine.Id&&x.Asn.Status!=AsnStatus.Cancelled&&(!excludeAsnId.HasValue||x.AsnId!=excludeAsnId.Value))
                    .SumAsync(x=>(decimal?)x.BaseExpectedQuantity,token)??0m;
                var max=poLine.BaseOrderedQuantity*(1m+poLine.AllowedOverReceiptPct/100m);
                if(existing+conversion.BaseQuantity>max) throw Conflict("Tổng số lượng ASN vượt giới hạn đơn mua.");
            }
            entity.Lines.Add(new AsnLine
            {
                PurchaseOrderLineId=dto.PurchaseOrderLineId,LineNo=lineNo,ProductId=dto.ProductId,ExpectedQuantity=dto.ExpectedQuantity,
                OperationUnitId=conversion.OperationUnitId,OperationUnitCodeSnapshot=conversion.OperationUnitCode,OperationUnitDecimalPlaces=conversion.OperationDecimals,
                BaseUnitId=conversion.BaseUnitId,BaseUnitCodeSnapshot=conversion.BaseUnitCode,BaseUnitDecimalPlaces=conversion.BaseDecimals,
                ConversionFactorSnapshot=conversion.Factor,ConversionVersionSnapshot=conversion.Version,BaseExpectedQuantity=conversion.BaseQuantity
            });
        }
    }

    private async Task<Conversion> ResolveConversionAsync(int productId,int operationUnitId,decimal quantity,CancellationToken token)
    {
        if(quantity<=0) throw new BusinessRuleException("Số lượng phải lớn hơn 0.");
        var product=await context.Products.AsNoTracking().Include(x=>x.Unit).Include(x=>x.Uoms).ThenInclude(x=>x.Unit).SingleOrDefaultAsync(x=>x.Id==productId,token);
        if(product is null||!product.IsActive) throw new BusinessRuleException("Sản phẩm không tồn tại hoặc không hoạt động.");
        var opId=operationUnitId>0?operationUnitId:product.UnitId;
        var now=DateTime.UtcNow;
        if(opId==product.UnitId)
        {
            EnsurePrecision(quantity,product.Unit.DecimalPlaces);
            return new(opId,product.Unit.Code,product.Unit.DecimalPlaces,product.UnitId,product.Unit.Code,product.Unit.DecimalPlaces,1m,1,quantity);
        }
        var uom=product.Uoms.Where(x=>x.IsActive&&x.EffectiveFromUtc<=now&&x.UnitId==opId).OrderByDescending(x=>x.Version).FirstOrDefault()
            ?? throw new BusinessRuleException("Đơn vị thao tác không hợp lệ cho sản phẩm.");
        EnsurePrecision(quantity,uom.Unit.DecimalPlaces);
        var baseQty=quantity*uom.ConversionFactor; EnsurePrecision(baseQty,product.Unit.DecimalPlaces);
        return new(opId,uom.Unit.Code,uom.Unit.DecimalPlaces,product.UnitId,product.Unit.Code,product.Unit.DecimalPlaces,uom.ConversionFactor,uom.Version,baseQty);
    }

    private IQueryable<PurchaseOrder> PurchaseOrderQuery()=>context.PurchaseOrders.Include(x=>x.Supplier).Include(x=>x.Warehouse).Include(x=>x.Lines).ThenInclude(x=>x.Product);
    private IQueryable<Asn> AsnQuery()=>context.Asns.Include(x=>x.PurchaseOrder).Include(x=>x.Supplier).Include(x=>x.Warehouse).Include(x=>x.Lines).ThenInclude(x=>x.Product);

    private PurchaseOrderDto Map(PurchaseOrder x)=>new()
    {
        Id=x.Id,ExternalPoId=x.ExternalPoId,SourceSystem=x.SourceSystem,Code=x.Code,SupplierCode=x.Supplier.Code,SupplierName=x.Supplier.Name,
        WarehouseId=x.WarehouseId,WarehouseName=x.Warehouse.Name,Status=x.Status.ToString(),OrderDate=x.OrderDate,ExpectedDate=x.ExpectedDate,
        Currency=x.Currency,ExternalVersion=x.ExternalVersion,BaseOrderedQuantity=x.Lines.Sum(l=>l.BaseOrderedQuantity),
        RowVersion=CanSeeMutationToken()?Convert.ToBase64String(x.RowVersion):null,
        Lines=x.Lines.OrderBy(l=>l.LineNo).Select(l=>new PurchaseOrderLineDto
        {
            Id=l.Id,ExternalLineId=l.ExternalLineId,LineNo=l.LineNo,ProductId=l.ProductId,ProductCode=l.Product.Code,ProductName=l.Product.Name,
            OrderedQuantity=l.OrderedQuantity,OperationUnitCode=l.OperationUnitCodeSnapshot,BaseOrderedQuantity=l.BaseOrderedQuantity,
            BaseUnitCode=l.BaseUnitCodeSnapshot,AllowedOverReceiptPct=l.AllowedOverReceiptPct,AllowedUnderReceiptPct=l.AllowedUnderReceiptPct
        }).ToList()
    };

    private AsnDto Map(Asn x)=>new()
    {
        Id=x.Id,Code=x.Code,PurchaseOrderId=x.PurchaseOrderId,PurchaseOrderCode=x.PurchaseOrder?.Code,SupplierCode=x.Supplier.Code,SupplierName=x.Supplier.Name,
        WarehouseId=x.WarehouseId,WarehouseName=x.Warehouse.Name,Status=x.Status.ToString(),ExpectedArrivalAtUtc=x.ExpectedArrivalAtUtc,
        BaseExpectedQuantity=x.Lines.Sum(l=>l.BaseExpectedQuantity),CarrierName=x.CarrierName,VehiclePlate=x.VehiclePlate,Note=x.Note,
        RowVersion=CanSeeMutationToken()?Convert.ToBase64String(x.RowVersion):null,
        Lines=x.Lines.OrderBy(l=>l.LineNo).Select(l=>new AsnLineDto
        {
            Id=l.Id,PurchaseOrderLineId=l.PurchaseOrderLineId,LineNo=l.LineNo,ProductId=l.ProductId,ProductCode=l.Product.Code,ProductName=l.Product.Name,
            ExpectedQuantity=l.ExpectedQuantity,OperationUnitCode=l.OperationUnitCodeSnapshot,BaseExpectedQuantity=l.BaseExpectedQuantity,BaseUnitCode=l.BaseUnitCodeSnapshot
        }).ToList()
    };

    private bool CanSeeMutationToken()=>currentUser.IsGlobalAdmin||currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";
    private AuditLog Audit(string action,string entityName,int entityId,int warehouseId,string values)=>new(){UserId=currentUser.UserId,Action=action,EntityName=entityName,EntityId=entityId,WarehouseId=warehouseId,NewValues=values,Result="Success",Timestamp=DateTime.UtcNow};

    private async Task SaveAsync(CancellationToken token)
    {
        try{await context.SaveChangesAsync(token);}
        catch(DbUpdateConcurrencyException ex){throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.",ex);}
        catch(DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE",StringComparison.OrdinalIgnoreCase)==true||ex.InnerException?.Message.Contains("duplicate",StringComparison.OrdinalIgnoreCase)==true)
        {throw Conflict("Dữ liệu bị trùng hoặc đã được tạo bởi yêu cầu khác.");}
    }

    private static void ApplyVersion(byte[] actual,string encoded,PropertyEntry property)
    {
        byte[] expected;try{expected=Convert.FromBase64String(encoded);}catch{throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ.");}
        if(expected.Length==0||!actual.SequenceEqual(expected)) throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        property.OriginalValue=expected;
    }
    private static string Required(string? value,string label,int max){var v=value?.Trim()??string.Empty;if(v.Length==0)throw new BusinessRuleException($"{label} là bắt buộc.");if(v.Length>max)throw new BusinessRuleException($"{label} vượt quá {max} ký tự.");return v;}
    private static string NormalizeCode(string? value,string label,int max)=>Required(value,label,max).ToUpperInvariant();
    private static string? NormalizeOptional(string? value,int max){if(string.IsNullOrWhiteSpace(value))return null;var v=value.Trim();if(v.Length>max)throw new BusinessRuleException($"Giá trị vượt quá {max} ký tự.");return v;}
    private static void ValidateTolerance(decimal over,decimal under){if(over<0||over>100||under<0||under>100)throw new BusinessRuleException("Tỷ lệ dung sai phải trong khoảng 0–100%.");}
    private static void EnsurePrecision(decimal value,int decimals){if(decimals is <0 or >4||decimal.Round(value,decimals)!=value)throw new BusinessRuleException($"Số lượng vượt quá {decimals} chữ số thập phân; hệ thống không tự làm tròn.");}
    private static BusinessRuleException Conflict(string message){var ex=new BusinessRuleException(message);ex.Data["HttpStatusCode"]=409;return ex;}
    private sealed record Conversion(int OperationUnitId,string OperationUnitCode,int OperationDecimals,int BaseUnitId,string BaseUnitCode,int BaseDecimals,decimal Factor,int Version,decimal BaseQuantity);
}
