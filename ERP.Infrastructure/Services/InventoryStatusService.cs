using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryStatusService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IInventoryStatusService
{
    public async Task<IReadOnlyList<InventoryStatusDefinitionDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default) =>
        await context.InventoryStatusDefinitions.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new InventoryStatusDefinitionDto
            {
                Code = x.Code,
                Name = x.Name,
                IsAvailable = x.IsAvailable,
                IsReservable = x.IsReservable,
                IsAllocatable = x.IsAllocatable,
                IsPickable = x.IsPickable,
                IsShippable = x.IsShippable
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryBucketDto>> GetBucketsAsync(
        int? warehouseId = null,
        int? productId = null,
        string? status = null,
        string? lotNumber = null,
        string? serialNumber = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = context.InventoryStocks.AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId));
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseStatus(status, out var parsed))
                throw new BusinessRuleException("Inventory status filter không hợp lệ.");
            query = query.Where(x => x.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            var lot = lotNumber.Trim();
            query = query.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        }
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            var serial = serialNumber.Trim();
            query = query.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
        }

        var today = DateTime.UtcNow.Date;
        return await query
            .OrderBy(x => x.Product.Code)
            .ThenBy(x => x.Status)
            .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .Take(1000)
            .Select(x => new InventoryBucketDto
            {
                InventoryStockId = x.Id,
                ProductId = x.ProductId,
                ProductCode = x.Product.Code,
                ProductName = x.Product.Name,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                LocationId = x.LocationId ?? 0,
                LocationCode = x.Location == null ? string.Empty : x.Location.Code,
                Status = x.StatusDefinition.Code,
                IsReservable = x.StatusDefinition.IsReservable &&
                               (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsAllocatable = x.StatusDefinition.IsAllocatable &&
                                (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsPickable = x.StatusDefinition.IsPickable &&
                             (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                IsShippable = x.StatusDefinition.IsShippable &&
                              (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today),
                LotId = x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber,
                ManufactureDate = x.Lot == null ? null : x.Lot.ManufactureDate,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber,
                OnHandQuantity = x.Quantity,
                ReservedQuantity = x.ReservedQuantity,
                AvailableQuantity = x.Quantity - x.ReservedQuantity,
                LastUpdated = x.LastUpdated
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryStatusChangeResultDto> ChangeAsync(
        CreateInventoryStatusChangeDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId và Quantity phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do status change là bắt buộc.");
        if (!TryParseStatus(request.ToStatus, out var toStatus))
            throw new BusinessRuleException("Inventory status đích không hợp lệ.");

        var ownTransaction = context.Database.CurrentTransaction is null;
        await using var tx = ownTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var source = await context.InventoryStocks
                .Include(x => x.StatusDefinition)
                .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy inventory bucket.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(source.WarehouseId, cancellationToken);

            if (source.Status == toStatus)
                throw new BusinessRuleException("Status đích phải khác status nguồn.");
            ValidateTransition(source.Status, toStatus);
            if (source.Quantity - source.ReservedQuantity < request.Quantity)
                throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", "Không thể đổi status phần tồn đang reserved/allocation.");
            if (!await context.InventoryStatusDefinitions.AnyAsync(x => x.Id == toStatus, cancellationToken))
                throw new BusinessRuleException("Inventory status đích chưa được cấu hình.");

            var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                x => x.ProductId == source.ProductId &&
                     x.WarehouseId == source.WarehouseId &&
                     x.LocationId == source.LocationId &&
                     x.Status == toStatus &&
                     x.LotId == source.LotId &&
                     x.SerialId == source.SerialId,
                cancellationToken);

            source.Quantity -= request.Quantity;
            source.LastUpdated = DateTime.UtcNow;
            if (destination is null)
            {
                destination = new InventoryStock
                {
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = source.LocationId,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    Status = toStatus,
                    Quantity = request.Quantity,
                    ReservedQuantity = 0,
                    LastUpdated = DateTime.UtcNow
                };
                context.InventoryStocks.Add(destination);
            }
            else
            {
                destination.Quantity += request.Quantity;
                destination.LastUpdated = DateTime.UtcNow;
            }

            var ledger = new InventoryTransaction
            {
                ProductId = source.ProductId,
                WarehouseId = source.WarehouseId,
                LocationId = source.LocationId,
                LotId = source.LotId,
                SerialId = source.SerialId,
                InventoryStatus = toStatus,
                FromInventoryStatus = source.Status,
                ToInventoryStatus = toStatus,
                TransactionType = TransactionType.StatusChange,
                Quantity = request.Quantity,
                ReferenceId = source.Id,
                ReferenceType = "InventoryStatusChange",
                TransactionDate = DateTime.UtcNow,
                CreatedBy = currentUser.UserId,
                Note = request.Reason.Trim()
            };
            context.InventoryTransactions.Add(ledger);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "Inventory.StatusChanged",
                EntityName = "InventoryStock",
                EntityId = source.Id,
                WarehouseId = source.WarehouseId,
                Timestamp = DateTime.UtcNow,
                OldValues = $"Status: {source.Status}; Quantity: {source.Quantity + request.Quantity}",
                NewValues = $"Status: {toStatus}; MovedQuantity: {request.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                Reason = request.Reason.Trim(),
                Result = "Success",
                Severity = "Information"
            });

            await context.SaveChangesAsync(cancellationToken);
            var result = new InventoryStatusChangeResultDto
            {
                SourceStockId = source.Id,
                DestinationStockId = destination.Id,
                TransactionId = ledger.Id,
                FromStatus = source.Status.ToString(),
                ToStatus = toStatus.ToString(),
                Quantity = request.Quantity,
                ProductId = source.ProductId,
                WarehouseId = source.WarehouseId,
                LocationId = source.LocationId ?? 0,
                LotId = source.LotId,
                SerialId = source.SerialId
            };
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw Conflict("CONCURRENCY_CONFLICT", "Inventory bucket đích đã thay đổi đồng thời.");
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateTransition(InventoryStatus fromStatus, InventoryStatus toStatus)
    {
        if (fromStatus is InventoryStatus.Expired or InventoryStatus.RecallBlocked)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Status {fromStatus} chỉ được release qua disposition/recall workflow chuyên biệt.");

        if (toStatus == InventoryStatus.Available &&
            fromStatus is InventoryStatus.Quarantine or InventoryStatus.Damaged or InventoryStatus.Rejected)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Không thể generic-release {fromStatus} về AVAILABLE; cần disposition/release workflow chuyên biệt.");
    }

    private static bool TryParseStatus(string value, out InventoryStatus status)
    {
        var normalized = value?.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<InventoryStatus>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ToCode(candidate).Replace("_", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }
        status = default;
        return false;
    }

    private static string ToCode(InventoryStatus status) => status switch
    {
        InventoryStatus.QcHold => "QC_HOLD",
        InventoryStatus.RecallBlocked => "RECALL_BLOCKED",
        _ => status.ToString().ToUpperInvariant()
    };

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
