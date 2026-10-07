using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IInventoryStatusService
{
    Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default);

    Task<InventoryStatusChangeResultDto> ChangeAsync(
        CreateInventoryStatusChangeDto request,
        CancellationToken cancellationToken = default);
}
