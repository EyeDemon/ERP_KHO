using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryLockService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser) : IInventoryLockService, IInventoryLockEvaluator
{
    private static readonly InventoryLockType[] AutoExpirableTypes =
    [
        InventoryLockType.CountFreeze,
        InventoryLockType.MaintenanceFreeze,
        InventoryLockType.ManualOperationalLock
    ];

    public async Task<IReadOnlyList<InventoryLockDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowed = [warehouseId.Value];
        }

        var rows = await Query().AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var result = rows.Select(Map).ToList();
        if (!string.IsNullOrWhiteSpace(status))
            result = result.Where(x => string.Equals(x.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return result;
    }

    public async Task<InventoryLockDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Query().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy Inventory Lock hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, cancellationToken);
        return Map(entity);
    }

    public async Task<InventoryLockDto> CreateAsync(
        CreateInventoryLockDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.WarehouseId <= 0) throw new BusinessRuleException("Warehouse là bắt buộc.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new BusinessRuleException("Lý do khóa tồn là bắt buộc.");
        if (!Enum.TryParse<InventoryLockType>(request.LockType?.Trim(), true, out var lockType))
            throw new BusinessRuleException("Loại Inventory Lock không hợp lệ.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);

        var status = ParseInventoryStatus(request.InventoryStatus);
        await ValidateScopeAsync(request, status, cancellationToken);

        if (request.ExpiresAt.HasValue)
        {
            if (request.ExpiresAt.Value <= DateTime.UtcNow)
                throw new BusinessRuleException("Thời điểm hết hạn Inventory Lock phải ở tương lai.");
            if (!AutoExpirableTypes.Contains(lockType))
                throw new BusinessRuleException("QUALITY_HOLD, INVESTIGATION_HOLD và RECALL_HOLD phải được release thủ công.");
        }

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var entity = new InventoryLock
            {
                LockType = lockType,
                Status = InventoryLockStatus.Active,
                WarehouseId = request.WarehouseId,
                LocationId = request.LocationId,
                ProductId = request.ProductId,
                InventoryStatus = status,
                LotId = request.LotId,
                SerialId = request.SerialId,
                Reason = request.Reason.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser.UserId,
                ExpiresAt = request.ExpiresAt
            };
            context.InventoryLocks.Add(entity);
            var audit = new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "InventoryLock.Created",
                EntityName = "InventoryLock",
                WarehouseId = request.WarehouseId,
                Timestamp = DateTime.UtcNow,
                NewValues = ScopeText(entity),
                Reason = entity.Reason,
                Result = "Success",
                Severity = "Warning"
            };
            context.AuditLogs.Add(audit);
            await context.SaveChangesAsync(cancellationToken);
            audit.EntityId = entity.Id;
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return await GetAsync(entity.Id, cancellationToken);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<InventoryLockDto> ReleaseAsync(
        int id,
        ReleaseInventoryLockDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do release Inventory Lock là bắt buộc.");

        var own = context.Database.CurrentTransaction is null;
        await using var tx = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var entity = await Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy Inventory Lock hoặc bạn không có quyền truy cập.");
            await warehouseAuthorization.EnsureWarehouseAccessAsync(entity.WarehouseId, cancellationToken);
            ApplyVersion(entity, request.RowVersion);

            if (entity.Status != InventoryLockStatus.Active)
                throw Conflict("INV_STOCK_LOCKED", "Inventory Lock không còn ở trạng thái Active.");
            if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
                throw Conflict("INV_STOCK_LOCKED", "Inventory Lock đã hết hiệu lực theo thời gian; hãy tải lại danh sách.");

            entity.Status = InventoryLockStatus.Released;
            entity.ReleasedAt = DateTime.UtcNow;
            entity.ReleasedBy = currentUser.UserId;
            entity.ReleaseReason = request.Reason.Trim();
            context.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = "InventoryLock.Released",
                EntityName = "InventoryLock",
                EntityId = entity.Id,
                WarehouseId = entity.WarehouseId,
                Timestamp = DateTime.UtcNow,
                OldValues = ScopeText(entity),
                Reason = entity.ReleaseReason,
                Result = "Success",
                Severity = "Information"
            });
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return await GetAsync(entity.Id, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyException("Inventory Lock đã thay đổi. Vui lòng tải lại.", ex);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task EnsureBucketUnlockedAsync(
        int warehouseId,
        int? locationId,
        int? productId,
        InventoryStatus? inventoryStatus,
        int? lotId,
        int? serialId,
        CancellationToken cancellationToken = default)
    {
        if (await HasActiveLockAsync(
                warehouseId, locationId, productId, inventoryStatus, lotId, serialId, cancellationToken))
            throw Conflict("INV_STOCK_LOCKED", "Inventory bucket đang bị khóa bởi Inventory Lock.");
    }

    public Task<bool> HasActiveLockAsync(
        int warehouseId,
        int? locationId,
        int? productId,
        InventoryStatus? inventoryStatus,
        int? lotId,
        int? serialId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.InventoryLocks
            .EffectiveAt(now)
            .AnyAsync(x =>
                x.WarehouseId == warehouseId &&
                (!x.LocationId.HasValue || x.LocationId == locationId) &&
                (!x.ProductId.HasValue || x.ProductId == productId) &&
                (!x.InventoryStatus.HasValue || x.InventoryStatus == inventoryStatus) &&
                (!x.LotId.HasValue || x.LotId == lotId) &&
                (!x.SerialId.HasValue || x.SerialId == serialId),
                cancellationToken);
    }

    private async Task ValidateScopeAsync(
        CreateInventoryLockDto request,
        InventoryStatus? status,
        CancellationToken token)
    {
        if (request.LocationId.HasValue &&
            !await context.WarehouseLocations.AnyAsync(
                x => x.Id == request.LocationId && x.WarehouseId == request.WarehouseId, token))
            throw new NotFoundException("Không tìm thấy Location trong Warehouse.");

        if (request.ProductId.HasValue &&
            !await context.Products.AnyAsync(x => x.Id == request.ProductId, token))
            throw new NotFoundException("Không tìm thấy Product.");

        if (status.HasValue &&
            !await context.InventoryStatusDefinitions.AnyAsync(x => x.Id == status.Value, token))
            throw new BusinessRuleException("Inventory Status chưa được cấu hình.");

        if (request.LotId.HasValue)
        {
            if (!request.ProductId.HasValue)
                throw new BusinessRuleException("Lock theo Lot phải xác định Product.");
            if (!await context.InventoryLots.AnyAsync(
                    x => x.Id == request.LotId && x.ProductId == request.ProductId, token))
                throw new NotFoundException("Không tìm thấy Lot của Product.");
        }

        if (request.SerialId.HasValue)
        {
            if (!request.ProductId.HasValue)
                throw new BusinessRuleException("Lock theo Serial phải xác định Product.");
            var serial = await context.InventorySerials.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.SerialId, token)
                ?? throw new NotFoundException("Không tìm thấy Serial.");
            if (serial.ProductId != request.ProductId ||
                (request.LotId.HasValue && serial.LotId != request.LotId))
                throw new BusinessRuleException("Serial không khớp Product/Lot của scope lock.");
        }
    }

    private IQueryable<InventoryLock> Query() =>
        context.InventoryLocks
            .Include(x => x.Warehouse)
            .Include(x => x.Location)
            .Include(x => x.Product)
            .Include(x => x.Lot)
            .Include(x => x.Serial);

    private static InventoryStatus? ParseInventoryStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<InventoryStatus>())
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StatusCode(candidate).Replace("_", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        throw new BusinessRuleException("Inventory Status trong lock scope không hợp lệ.");
    }

    private static string StatusCode(InventoryStatus status) => status switch
    {
        InventoryStatus.QcHold => "QC_HOLD",
        InventoryStatus.RecallBlocked => "RECALL_BLOCKED",
        _ => status.ToString().ToUpperInvariant()
    };

    private static InventoryLockDto Map(InventoryLock entity)
    {
        var effectiveStatus = entity.Status == InventoryLockStatus.Active &&
                              entity.ExpiresAt.HasValue &&
                              entity.ExpiresAt.Value <= DateTime.UtcNow
            ? InventoryLockStatus.Expired.ToString()
            : entity.Status.ToString();
        return new InventoryLockDto
        {
            Id = entity.Id,
            LockType = entity.LockType.ToString(),
            Status = effectiveStatus,
            WarehouseId = entity.WarehouseId,
            WarehouseName = entity.Warehouse.Name,
            LocationId = entity.LocationId,
            LocationCode = entity.Location?.Code,
            ProductId = entity.ProductId,
            ProductCode = entity.Product?.Code,
            InventoryStatus = entity.InventoryStatus.HasValue ? StatusCode(entity.InventoryStatus.Value) : null,
            LotId = entity.LotId,
            LotNumber = entity.Lot?.LotNumber,
            SerialId = entity.SerialId,
            SerialNumber = entity.Serial?.SerialNumber,
            Reason = entity.Reason,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy,
            ExpiresAt = entity.ExpiresAt,
            ReleasedAt = entity.ReleasedAt,
            ReleasedBy = entity.ReleasedBy,
            ReleaseReason = entity.ReleaseReason,
            RowVersion = Convert.ToBase64String(entity.RowVersion)
        };
    }

    private void ApplyVersion(InventoryLock entity, string encoded)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("Phiên bản Inventory Lock không hợp lệ."); }
        if (!entity.RowVersion.SequenceEqual(expected))
            throw new ConcurrencyException("Inventory Lock đã thay đổi. Vui lòng tải lại.");
        context.Entry(entity).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static string ScopeText(InventoryLock entity) =>
        $"Type: {entity.LockType}; LocationId: {entity.LocationId}; ProductId: {entity.ProductId}; Status: {entity.InventoryStatus}; LotId: {entity.LotId}; SerialId: {entity.SerialId}; ExpiresAt: {entity.ExpiresAt:O}";

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
