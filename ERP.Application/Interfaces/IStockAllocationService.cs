using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IStockAllocationService
{
    Task<StockAllocationPageDto> GetPageAsync(int page, int pageSize, int? warehouseId, int? reservationId, string? status, CancellationToken cancellationToken = default);
    Task<StockAllocationDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AllocatableReservationDto>> GetReservationsAsync(int? warehouseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockAllocationCandidateDto>> GetCandidatesAsync(int reservationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockAllocationDto>> CreateAsync(CreateStockAllocationDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockAllocationDto>> AutoAllocateAsync(CreateStockAllocationDto request, CancellationToken cancellationToken = default);
    Task ReleaseAsync(int id, ReleaseStockAllocationDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockAllocationDto>> ReallocateAsync(int id, ReallocateStockAllocationDto request, CancellationToken cancellationToken = default);
}
