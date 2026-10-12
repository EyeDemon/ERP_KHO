using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IReceivingReasonCatalogQuery
{
    Task<PagedResult<ReceivingReasonCatalogDto>> ListAsync(string? keyword, bool? active, ReasonEffectivity? effectivity, int page, int pageSize, CancellationToken token);
    Task<ReceivingReasonCatalogDto> DetailAsync(int id, CancellationToken token);
    Task<IReadOnlyList<ReceivingReasonCatalogDto>> VersionsAsync(string code, CancellationToken token);
}
