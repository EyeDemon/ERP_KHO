using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Common;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Inventory;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryReversalService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IInventoryMovementService movementService,
    IInventoryStatusService statusService) : IInventoryReversalService
{
    public Task<IReadOnlyList<InventoryReversalReasonDto>> GetReversalReasonsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InventoryReversalReasonCatalog.All);

    public async Task<IReadOnlyList<InventoryReversalWarehouseDto>> GetReversalWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await context.Warehouses.AsNoTracking()
            .Where(x => accessible.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new InventoryReversalWarehouseDto
            {
                Id = x.Id, Code = x.Code.Trim(), Name = x.Name.Trim()
            }).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<InventoryReversalCandidateDto>> GetCandidatesAsync(
        int? warehouseId = null, int page = 1, int pageSize = 20,
        int? transactionId = null, bool? isReversed = null,
        string? productCode = null, CancellationToken cancellationToken = default)
    {
        var normalizedProductCode = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim();
        if (normalizedProductCode is { Length: > 64 })
            throw new BusinessRuleException("Mã sản phẩm tìm kiếm không được quá 64 ký tự.");
        if (transactionId.HasValue && transactionId.Value <= 0)
            throw new BusinessRuleException("ID giao dịch tìm kiếm không hợp lệ.");
        if (warehouseId.HasValue && warehouseId.Value <= 0)
            throw new BusinessRuleException("Mã kho không hợp lệ.");
        if (warehouseId.HasValue)
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var permittedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var safePage = Math.Max(1, page);
        var safeSize = pageSize is >= 1 and <= 100 ? pageSize : 20;
        var offset = (long)(safePage - 1) * safeSize;
        if (offset > int.MaxValue)
            throw new BusinessRuleException("Số trang vượt quá phạm vi tra cứu cho phép.");

        var candidates = context.InventoryTransactions.AsNoTracking()
            .Where(x => permittedWarehouseIds.Contains(x.WarehouseId)
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (!transactionId.HasValue || x.Id == transactionId.Value)
                && (normalizedProductCode == null || x.Product.Code.StartsWith(normalizedProductCode))
                && (x.TransactionType == TransactionType.Move || x.TransactionType == TransactionType.StatusChange)
                && x.Quantity > 0
                // A corrective leg already belongs to an immutable reversal chain.
                && !context.InventoryTransactions.Any(marker => marker.CorrectiveTransactionId == x.Id));

        // Filter on SQL, before counting and paging. Only the persisted reversal marker
        // determines completion; this must not depend on the current frontend page.
        if (isReversed.HasValue)
        {
            candidates = isReversed.Value
                ? candidates.Where(x => context.InventoryTransactions.Any(marker =>
                    marker.ReversalOfTransactionId == x.Id ||
                    (marker.TransactionType == TransactionType.Reversal &&
                     marker.ReversalOfTransactionId == null &&
                     marker.WarehouseId == x.WarehouseId &&
                     marker.ReferenceType == "InventoryReversal" &&
                     marker.ReferenceId == x.Id)))
                : candidates.Where(x => !context.InventoryTransactions.Any(marker =>
                    marker.ReversalOfTransactionId == x.Id ||
                    (marker.TransactionType == TransactionType.Reversal &&
                     marker.ReversalOfTransactionId == null &&
                     marker.WarehouseId == x.WarehouseId &&
                     marker.ReferenceType == "InventoryReversal" &&
                     marker.ReferenceId == x.Id)));
        }

        var count = await candidates.CountAsync(cancellationToken);
        var items = await candidates.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Skip((int)offset).Take(safeSize)
            .Select(x => new InventoryReversalCandidateDto
            {
                Id = x.Id,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code.Trim(),
                ProductName = x.Product.Name.Trim(),
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name.Trim(),
                TransactionType = x.TransactionType.ToString(),
                InventoryStatus = x.InventoryStatus.ToString(),
                FromInventoryStatus = x.FromInventoryStatus.HasValue ? x.FromInventoryStatus.Value.ToString() : null,
                ToInventoryStatus = x.ToInventoryStatus.HasValue ? x.ToInventoryStatus.Value.ToString() : null,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber.Trim(),
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber.Trim(),
                Quantity = x.Quantity,
                TransactionDate = x.TransactionDate,
                // Database-authoritative: never infer reversal state from a limited frontend page.
                IsReversed = context.InventoryTransactions.Any(marker =>
                    marker.ReversalOfTransactionId == x.Id ||
                    (marker.TransactionType == TransactionType.Reversal &&
                     marker.ReversalOfTransactionId == null &&
                     marker.WarehouseId == x.WarehouseId &&
                     marker.ReferenceType == "InventoryReversal" &&
                     marker.ReferenceId == x.Id))
            }).ToListAsync(cancellationToken);

        return new PagedResult<InventoryReversalCandidateDto>
        {
            Items = items, TotalRecords = count, PageIndex = safePage, PageSize = safeSize
        };
    }
}
