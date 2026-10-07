using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces;

public interface IReceiptInventoryIdentityService
{
    Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> SetAsync(
        int receiptId,
        SetImportReceiptInventoryIdentitiesDto request,
        int actorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportReceiptInventoryIdentityDto>> GetAsync(
        int receiptId,
        CancellationToken cancellationToken = default);

    Task ValidateReadyToPostAsync(
        int receiptId,
        CancellationToken cancellationToken = default);

    Task<bool> TryPostTrackedBucketAsync(
        ImportReceipt receipt,
        ImportReceiptDetail detail,
        InventoryStatus status,
        decimal baseQuantity,
        int actorId,
        int? locationId,
        CancellationToken cancellationToken = default);
}
