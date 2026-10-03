using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IReceiptPutawayIntegration
{
    Task<int> GetReceivingLocationIdAsync(int warehouseId, CancellationToken token = default);
    Task CreateForPostedReceiptAsync(ImportReceipt receipt, int receivingLocationId, int actorId, CancellationToken token = default);
}

public interface IPutawayService
{
    Task<IReadOnlyList<WarehouseLocationDto>> ListLocationsAsync(int warehouseId, CancellationToken token = default);
    Task<WarehouseLocationDto> CreateLocationAsync(CreateWarehouseLocationDto dto, CancellationToken token = default);
    Task<WarehouseLocationDto> UpdateLocationAsync(int id, UpdateWarehouseLocationDto dto, CancellationToken token = default);
    Task<IReadOnlyList<PutawayTaskListDto>> ListAsync(CancellationToken token = default);
    Task<PutawayTaskDto> GetAsync(int id, CancellationToken token = default);
    Task<IReadOnlyList<WarehouseLocationDto>> GetDestinationsAsync(int taskId, int itemId, CancellationToken token = default);
    Task<PutawayTaskDto> AssignAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default);
    Task<PutawayTaskDto> StartAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default);
    Task<PutawayTaskDto> MoveAsync(int id, MovePutawayItemDto dto, CancellationToken token = default);
    Task<PutawayTaskDto> OpenExceptionAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default);
    Task<PutawayTaskDto> ResumeAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default);
    Task<PutawayTaskDto> CancelAsync(int id, PutawayStateCommandDto dto, CancellationToken token = default);
}
