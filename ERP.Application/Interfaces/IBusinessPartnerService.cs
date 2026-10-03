using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IBusinessPartnerService
{
    Task<PagedResult<BusinessPartnerDto>> GetAsync(int page, int pageSize, string? search, string? role, bool? active, CancellationToken ct = default);
    Task<BusinessPartnerDto> CreateAsync(SaveBusinessPartnerDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, SaveBusinessPartnerDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetImportSupplierAsync(int receiptId, int? partnerId, CancellationToken ct = default);
    Task SetExportCustomerAsync(int receiptId, int? partnerId, CancellationToken ct = default);
}
