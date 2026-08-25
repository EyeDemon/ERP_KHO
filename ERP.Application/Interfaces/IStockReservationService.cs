using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public interface IStockReservationService
{
    Task<StockReservationDto> CreateAsync(CreateStockReservationDto request, CancellationToken cancellationToken = default);
    Task<StockReservationPageDto> GetPageAsync(int page, int pageSize, int? warehouseId, int? productId, string? status, CancellationToken cancellationToken = default);
    Task<StockReservationDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task ReleaseAsync(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken = default);
    Task<int> ExpireAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReservationReconciliationIssueDto>> ReconcileAsync(CancellationToken cancellationToken = default);
    Task<StockReservation> ReserveForExportAsync(int exportReceiptId, string exportCode, int warehouseId, int productId, decimal quantity, int userId, CancellationToken cancellationToken = default);
    Task<StockReservation> GetExportReservationAsync(int exportReceiptId, int warehouseId, int productId, CancellationToken cancellationToken = default);
    Task ConsumeAsync(StockReservation reservation, int userId, CancellationToken cancellationToken = default);
    Task ReleaseSourceAsync(string sourceType, int sourceId, int userId, string reason, CancellationToken cancellationToken = default);
}
