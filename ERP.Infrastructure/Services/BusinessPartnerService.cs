using System.Text.RegularExpressions;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class BusinessPartnerService(ErpKhoDbContext context, IWarehouseAuthorizationService warehouses, ICurrentUser currentUser) : IBusinessPartnerService
{
    public async Task<PagedResult<BusinessPartnerDto>> GetAsync(int page, int pageSize, string? search, string? role, bool? active, CancellationToken ct = default)
    {
        page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,100);
        var query=context.BusinessPartners.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(search)){var q=search.Trim();query=query.Where(x=>x.Code.Contains(q)||x.Name.Contains(q));}
        if(role?.Equals("supplier",StringComparison.OrdinalIgnoreCase)==true)query=query.Where(x=>x.IsSupplier);
        if(role?.Equals("customer",StringComparison.OrdinalIgnoreCase)==true)query=query.Where(x=>x.IsCustomer);
        if(active.HasValue)query=query.Where(x=>x.IsActive==active.Value);
        var total=await query.CountAsync(ct);var items=await query.OrderBy(x=>x.Code).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        return new(){Items=items.Select(Map).ToList(),TotalRecords=total,PageIndex=page,PageSize=pageSize};
    }

    public async Task<BusinessPartnerDto> CreateAsync(SaveBusinessPartnerDto dto,CancellationToken ct=default)
    {
        var entity=new BusinessPartner{Code=Code(dto.Code),Name=Text(dto.Name,200,"Tên đối tác"),IsSupplier=dto.IsSupplier,IsCustomer=dto.IsCustomer,IsActive=dto.IsActive,Phone=Optional(dto.Phone,50,"Điện thoại"),Email=Optional(dto.Email,254,"Email"),Address=Optional(dto.Address,500,"Địa chỉ")};
        Roles(entity.IsSupplier,entity.IsCustomer);
        context.BusinessPartners.Add(entity);await Save("Mã đối tác đã tồn tại.",ct);return Map(entity);
    }

    public async Task UpdateAsync(int id,SaveBusinessPartnerDto dto,CancellationToken ct=default)
    {
        await using var tx=await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var entity=await context.BusinessPartners.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException($"Không tìm thấy đối tác id {id}");
        if(Code(dto.Code)!=entity.Code)throw new BusinessRuleException("Mã đối tác không được thay đổi.");
        Roles(dto.IsSupplier,dto.IsCustomer);
        if(!dto.IsSupplier&&await context.ImportReceipts.AnyAsync(x=>x.SupplierId==id,ct))throw Conflict("Không thể tắt vai trò nhà cung cấp đang được phiếu nhập sử dụng.");
        if(!dto.IsCustomer&&await context.ExportReceipts.AnyAsync(x=>x.CustomerId==id,ct))throw Conflict("Không thể tắt vai trò khách hàng đang được phiếu xuất sử dụng.");
        var expected=RequiredRowVersion(dto.RowVersion);
        context.Entry(entity).Property(x=>x.RowVersion).OriginalValue=expected;
        entity.Name=Text(dto.Name,200,"Tên đối tác");entity.IsSupplier=dto.IsSupplier;entity.IsCustomer=dto.IsCustomer;entity.IsActive=dto.IsActive;entity.Phone=Optional(dto.Phone,50,"Điện thoại");entity.Email=Optional(dto.Email,254,"Email");entity.Address=Optional(dto.Address,500,"Địa chỉ");entity.UpdatedAt=DateTime.UtcNow;
        await Save("Đối tác đã được thay đổi đồng thời.",ct);await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(int id,CancellationToken ct=default)
    {
        var entity=await context.BusinessPartners.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException($"Không tìm thấy đối tác id {id}");
        if(await context.ImportReceipts.AnyAsync(x=>x.SupplierId==id,ct)||await context.ExportReceipts.AnyAsync(x=>x.CustomerId==id,ct))throw Conflict("Không thể xóa đối tác đang được chứng từ sử dụng.");
        context.Remove(entity);await Save("Không thể xóa đối tác đang được chứng từ sử dụng.",ct);
    }

    public Task SetImportSupplierAsync(int receiptId,int? partnerId,CancellationToken ct=default)=>SetReceiptPartner(receiptId,partnerId,true,ct);
    public Task SetExportCustomerAsync(int receiptId,int? partnerId,CancellationToken ct=default)=>SetReceiptPartner(receiptId,partnerId,false,ct);

    private async Task SetReceiptPartner(int receiptId,int? partnerId,bool supplier,CancellationToken ct)
    {
        await using var tx=await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var import = supplier ? await context.ImportReceipts.SingleOrDefaultAsync(x=>x.Id==receiptId,ct) : null;
        var export = supplier ? null : await context.ExportReceipts.SingleOrDefaultAsync(x=>x.Id==receiptId,ct);
        var warehouseId = import?.WarehouseId ?? export?.WarehouseId
            ?? throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(warehouseId,ct);
        if ((import?.Status ?? export!.Status) != ReceiptStatus.Draft)
            throw Conflict(supplier ? "Chỉ được đổi nhà cung cấp khi phiếu nhập ở trạng thái nháp." : "Chỉ được đổi khách hàng khi phiếu xuất ở trạng thái nháp.");
        BusinessPartner? partner=null;
        if(partnerId.HasValue){partner=await context.BusinessPartners.SingleOrDefaultAsync(x=>x.Id==partnerId,ct)??throw new BusinessRuleException("Đối tác không tồn tại.");if(!partner.IsActive||(supplier?!partner.IsSupplier:!partner.IsCustomer))throw new BusinessRuleException(supplier?"Đối tác không phải nhà cung cấp đang hoạt động.":"Đối tác không phải khách hàng đang hoạt động.");}
        if(supplier) import!.SupplierId=partnerId;
        else export!.CustomerId=partnerId;
        context.AuditLogs.Add(new AuditLog{UserId=currentUser.UserId,Action=supplier?"ImportReceipt.SupplierChanged":"ExportReceipt.CustomerChanged",EntityName=supplier?"ImportReceipt":"ExportReceipt",EntityId=receiptId,WarehouseId=warehouseId,NewValues=partnerId.HasValue?$"PartnerId: {partnerId}":"PartnerId: null",Result="Success",Severity="Information",Timestamp=DateTime.UtcNow});
        await Save("Liên kết đối tác không còn khả dụng hoặc chứng từ đã thay đổi.",ct);await tx.CommitAsync(ct);
    }

    private async Task Save(string message,CancellationToken ct){try{await context.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException ex){throw Conflict(message,ex);}catch(DbUpdateException ex)when(ex.InnerException is SqlException{Number:2601 or 2627 or 547}){throw Conflict(message,ex);}}
    private static BusinessPartnerDto Map(BusinessPartner x)=>new(){Id=x.Id,Code=x.Code,Name=x.Name,IsSupplier=x.IsSupplier,IsCustomer=x.IsCustomer,IsActive=x.IsActive,Phone=x.Phone,Email=x.Email,Address=x.Address,RowVersion=Convert.ToBase64String(x.RowVersion)};
    private static string Code(string? x){var v=(x??"").Trim().ToUpperInvariant();if(v.Length is<1 or>50||!Regex.IsMatch(v,"^[A-Z0-9._-]+$",RegexOptions.CultureInvariant))throw new BusinessRuleException("Mã đối tác chỉ được chứa chữ cái ASCII, chữ số, dấu chấm, gạch dưới hoặc gạch ngang.");return v;}
    private static string Text(string? x,int max,string label){var v=(x??"").Trim();if(v.Length is<1||v.Length>max)throw new BusinessRuleException($"{label} phải có từ 1 đến {max} ký tự.");return v;}
    private static string? Optional(string? x,int max,string label){if(string.IsNullOrWhiteSpace(x))return null;var v=x.Trim();if(v.Length>max)throw new BusinessRuleException($"{label} không được vượt quá {max} ký tự.");return v;}
    private static void Roles(bool supplier,bool customer){if(!supplier&&!customer)throw new BusinessRuleException("Đối tác phải có ít nhất một vai trò nhà cung cấp hoặc khách hàng.");}
    private static byte[] RequiredRowVersion(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))throw Conflict("Thiếu phiên bản đối tác. Vui lòng tải lại dữ liệu trước khi lưu.");
        try{var bytes=Convert.FromBase64String(value);if(bytes.Length!=8)throw new FormatException();return bytes;}
        catch(FormatException ex){throw Conflict("Phiên bản đối tác không hợp lệ. Vui lòng tải lại dữ liệu trước khi lưu.",ex);}
    }
    private static BusinessRuleException Conflict(string message,Exception? inner=null){var e=inner is null?new BusinessRuleException(message):new BusinessRuleException(message,inner);e.Data["HttpStatusCode"]=409;return e;}
}
