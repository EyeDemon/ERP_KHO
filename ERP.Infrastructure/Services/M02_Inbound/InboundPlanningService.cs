using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Security;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InboundPlanningService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouses,
    ICurrentUser? currentUser = null) : IInboundPlanningService
{
    private bool CanReadCost() =>
        currentUser is { IsAuthenticated: true } && currentUser.HasPermission(AppPermissions.InventoryCostRead);

    private int CurrentUserId => currentUser?.UserId ?? 1;

    public async Task<IReadOnlyList<PurchaseOrderListDto>> ListPurchaseOrdersAsync(PurchaseOrderStatus? status = null, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var query = context.PurchaseOrders.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Lines)
            .Where(x => allowed.Contains(x.WarehouseId));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var entities = await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(token);
        var canReadCost = CanReadCost();
        return entities.Select(x =>
        {
            var subTotal = canReadCost
                ? x.Lines.Sum(l => decimal.Round(l.OrderedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero))
                : 0m;
            var taxAmount = canReadCost
                ? x.Lines.Sum(l =>
                {
                    var lineSub = decimal.Round(l.OrderedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero);
                    return decimal.Round(lineSub * (l.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
                })
                : 0m;
            return new PurchaseOrderListDto
            {
                Id = x.Id, ExternalPoId = x.ExternalPoId.Trim(), Code = x.Code.Trim(), SupplierCode = x.Supplier.Code.Trim(), SupplierName = x.Supplier.Name.Trim(),
                WarehouseId = x.WarehouseId, WarehouseCode = x.Warehouse.Code.Trim(), WarehouseName = x.Warehouse.Name.Trim(), Status = x.Status.ToString(), OrderDate = x.OrderDate,
                ExpectedDate = x.ExpectedDate, Currency = string.IsNullOrWhiteSpace(x.Currency) ? "VND" : x.Currency.Trim().ToUpperInvariant(), BaseOrderedQuantity = x.Lines.Sum(l => l.BaseOrderedQuantity),
                SubTotalAmount = subTotal, TaxAmount = taxAmount, TotalAmount = subTotal + taxAmount
            };
        }).ToList();
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
            ExternalPoId = external, SourceSystem = source, Code = code, SupplierId = dto.SupplierId, WarehouseId = dto.WarehouseId,
            OrderDate = dto.OrderDate == default ? DateTime.UtcNow.Date : dto.OrderDate, ExpectedDate = dto.ExpectedDate,
            Currency = NormalizeOptional(dto.Currency, 10), ExternalVersion = NormalizeOptional(dto.ExternalVersion, 100),
            CreatedAtUtc = DateTime.UtcNow, CreatedBy = CurrentUserId
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
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion));
        if (NormalizeCode(dto.SourceSystem, "Hệ thống nguồn", 50) != entity.SourceSystem || Required(dto.ExternalPoId, "Mã PO ngoài hệ thống", 100) != entity.ExternalPoId)
            throw Conflict("Định danh PO ngoài hệ thống không được thay đổi.");
        await ValidatePurchaseOrderHeaderAsync(dto.SupplierId, dto.WarehouseId, token);
        var code = NormalizeCode(dto.Code, "Mã đơn mua", 50);
        if (await context.PurchaseOrders.AnyAsync(x => x.Id != id && x.WarehouseId == dto.WarehouseId && x.Code == code, token))
            throw Conflict("Mã đơn mua đã tồn tại trong kho.");
        if (dto.Lines.Count == 0) throw new BusinessRuleException("Đơn mua phải có ít nhất một dòng hàng.");

        entity.Code = code; entity.SupplierId = dto.SupplierId; entity.WarehouseId = dto.WarehouseId;
        entity.OrderDate = dto.OrderDate; entity.ExpectedDate = dto.ExpectedDate; entity.Currency = NormalizeOptional(dto.Currency, 10);
        entity.ExternalVersion = NormalizeOptional(dto.ExternalVersion, 100); entity.UpdatedAtUtc = DateTime.UtcNow; entity.UpdatedBy = CurrentUserId;
        context.PurchaseOrderLines.RemoveRange(entity.Lines); entity.Lines.Clear();
        await PopulatePurchaseOrderLinesAsync(entity, dto.Lines, token);
        context.AuditLogs.Add(Audit("PurchaseOrder.Updated", "PurchaseOrder", entity.Id, entity.WarehouseId, $"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
        await SaveAsync(token);
        return await GetPurchaseOrderAsync(id, token);
    }

    public Task<PurchaseOrderDto> OpenPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id, dto, [PurchaseOrderStatus.Draft], PurchaseOrderStatus.Open, "PurchaseOrder.Opened", token);
    public Task<PurchaseOrderDto> CancelPurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id, dto, [PurchaseOrderStatus.Draft, PurchaseOrderStatus.Open], PurchaseOrderStatus.Cancelled, "PurchaseOrder.Cancelled", token);
    public Task<PurchaseOrderDto> ClosePurchaseOrderAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutatePurchaseOrderAsync(id, dto, [PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received], PurchaseOrderStatus.Closed, "PurchaseOrder.Closed", token);

    public async Task<IReadOnlyList<AsnListDto>> ListAsnsAsync(AsnStatus? status = null, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var query = context.Asns.AsNoTracking().Where(x => allowed.Contains(x.WarehouseId));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query.OrderByDescending(x => x.CreatedAtUtc).Select(x => new AsnListDto
        {
            Id = x.Id, Code = x.Code.Trim(), PurchaseOrderId = x.PurchaseOrderId, PurchaseOrderCode = x.PurchaseOrder != null ? x.PurchaseOrder.Code.Trim() : null,
            SupplierCode = x.Supplier.Code.Trim(), SupplierName = x.Supplier.Name.Trim(), WarehouseId = x.WarehouseId, WarehouseCode = x.Warehouse.Code.Trim(), WarehouseName = x.Warehouse.Name.Trim(),
            Status = x.Status.ToString(), ExpectedArrivalAtUtc = x.ExpectedArrivalAtUtc, BaseExpectedQuantity = x.Lines.Sum(l => l.BaseExpectedQuantity)
        }).ToListAsync(token);
    }

    public async Task<AsnDto> GetAsnAsync(int id, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity = await AsnQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), token)
            ?? throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        return Map(entity);
    }

    public async Task<AsnDto> CreateAsnAsync(CreateAsnDto dto, CancellationToken token = default)
    {
        var header = await ResolveAsnHeaderAsync(dto.PurchaseOrderId, dto.SupplierId, dto.WarehouseId, token);
        var code = NormalizeCode(dto.Code, "Mã ASN", 50);
        if (await context.Asns.AnyAsync(x => x.WarehouseId == header.WarehouseId && x.Code == code, token)) throw Conflict("Mã ASN đã tồn tại trong kho.");
        if (dto.Lines.Count == 0) throw new BusinessRuleException("ASN phải có ít nhất một dòng hàng.");
        var entity = new Asn
        {
            Code = code, PurchaseOrderId = dto.PurchaseOrderId, SupplierId = header.SupplierId, WarehouseId = header.WarehouseId,
            ExpectedArrivalAtUtc = dto.ExpectedArrivalAtUtc, CarrierName = NormalizeOptional(dto.CarrierName, 200),
            VehiclePlate = NormalizeOptional(dto.VehiclePlate, 30)?.ToUpperInvariant(), Note = NormalizeOptional(dto.Note, 500),
            CreatedAtUtc = DateTime.UtcNow, CreatedBy = CurrentUserId
        };
        await PopulateAsnLinesAsync(entity, dto.Lines, null, token);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        try
        {
            context.Asns.Add(entity);
            await SaveAsync(token);
            context.AuditLogs.Add(Audit("Asn.Created", "Asn", entity.Id, entity.WarehouseId, $"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
            await SaveAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(token);
            throw;
        }
        return await GetAsnAsync(entity.Id, token);
    }

    public async Task<AsnDto> UpdateAsnAsync(int id, UpdateAsnDto dto, CancellationToken token = default)
    {
        var allowed = await warehouses.GetAccessibleWarehouseIdsAsync(token);
        var entity = await AsnQuery().SingleOrDefaultAsync(x => x.Id == id && allowed.Contains(x.WarehouseId), token)
            ?? throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        if (entity.Status != AsnStatus.Draft) throw Conflict("Chỉ ASN nháp mới được chỉnh sửa.");
        ApplyVersion(entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion));
        var header = await ResolveAsnHeaderAsync(dto.PurchaseOrderId, dto.SupplierId, dto.WarehouseId, token);
        var code = NormalizeCode(dto.Code, "Mã ASN", 50);
        if (await context.Asns.AnyAsync(x => x.Id != id && x.WarehouseId == header.WarehouseId && x.Code == code, token)) throw Conflict("Mã ASN đã tồn tại trong kho.");
        if (dto.Lines.Count == 0) throw new BusinessRuleException("ASN phải có ít nhất một dòng hàng.");

        entity.Code = code; entity.PurchaseOrderId = dto.PurchaseOrderId; entity.SupplierId = header.SupplierId; entity.WarehouseId = header.WarehouseId;
        entity.ExpectedArrivalAtUtc = dto.ExpectedArrivalAtUtc; entity.CarrierName = NormalizeOptional(dto.CarrierName, 200);
        entity.VehiclePlate = NormalizeOptional(dto.VehiclePlate, 30)?.ToUpperInvariant(); entity.Note = NormalizeOptional(dto.Note, 500);
        entity.UpdatedAtUtc = DateTime.UtcNow; entity.UpdatedBy = CurrentUserId;
        context.AsnLines.RemoveRange(entity.Lines); entity.Lines.Clear();
        await PopulateAsnLinesAsync(entity, dto.Lines, id, token);
        context.AuditLogs.Add(Audit("Asn.Updated", "Asn", entity.Id, entity.WarehouseId, $"Code: {entity.Code}; Lines: {entity.Lines.Count}"));
        await SaveAsync(token);
        return await GetAsnAsync(id, token);
    }

    public Task<AsnDto> ConfirmAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.Draft], AsnStatus.Confirmed, "Asn.Confirmed", token);
    public Task<AsnDto> MarkAsnInTransitAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.Confirmed], AsnStatus.InTransit, "Asn.MarkedInTransit", token);
    public Task<AsnDto> ArriveAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.InTransit], AsnStatus.Arrived, "Asn.Arrived", token);
    public Task<AsnDto> StartReceivingAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.Arrived], AsnStatus.Receiving, "Asn.ReceivingStarted", token);
    public Task<AsnDto> CompleteAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.Receiving], AsnStatus.Completed, "Asn.Completed", token, async (asn, ct) =>
        {
            var hasOpenReceipts = await context.ImportReceipts.AsNoTracking().AnyAsync(
                x => x.AsnId == asn.Id && x.Status != ReceiptStatus.Posted && x.Status != ReceiptStatus.Cancelled,
                ct);
            if (hasOpenReceipts)
                throw Conflict("Không thể hoàn tất ASN khi còn phiếu nhập liên kết chưa được POST.");
        });
    public Task<AsnDto> CancelAsnAsync(int id, InboundStateCommandDto dto, CancellationToken token = default) =>
        MutateAsnAsync(id, dto, [AsnStatus.Draft, AsnStatus.Confirmed], AsnStatus.Cancelled, "Asn.Cancelled", token);
}
