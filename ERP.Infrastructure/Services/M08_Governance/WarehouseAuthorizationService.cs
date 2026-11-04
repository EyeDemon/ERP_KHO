using System.Diagnostics;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure.Services;

public sealed class WarehouseAuthorizationService(
    ErpKhoDbContext context,
    ICurrentUser currentUser,
    ILogger<WarehouseAuthorizationService>? logger = null,
    IHttpContextAccessor? httpContextAccessor = null) : IWarehouseAuthorizationService
{
    private const int MaxTransactionCacheEntries = 64;
    private readonly Dictionary<(int TenantId, int UserId), IReadOnlyList<int>> _accessibleWarehouseCache = new();
    private readonly Dictionary<(Guid TransactionId, int TenantId, int UserId), IReadOnlyList<int>> _txAccessibleWarehouses = new();
    private readonly HashSet<(Guid TransactionId, int TenantId, int UserId, int WarehouseId)> _txVerifiedAccess = new();
    private Guid? _lastTransactionId;

    public int? CurrentUserId => currentUser.IsAuthenticated && currentUser.UserId > 0 ? currentUser.UserId : null;

    private int EffectiveTenantId => currentUser.TenantId > 0 ? currentUser.TenantId : context.CurrentTenantId;

    public void InvalidateWarehouseAccessCache(int? userId = null)
    {
        if (userId.HasValue)
        {
            foreach (var key in _accessibleWarehouseCache.Keys.Where(k => k.UserId == userId.Value).ToList())
            {
                _accessibleWarehouseCache.Remove(key);
            }
            foreach (var key in _txAccessibleWarehouses.Keys.Where(k => k.UserId == userId.Value).ToList())
            {
                _txAccessibleWarehouses.Remove(key);
            }
            _txVerifiedAccess.RemoveWhere(x => x.UserId == userId.Value);
            return;
        }

        _accessibleWarehouseCache.Clear();
        _txAccessibleWarehouses.Clear();
        _txVerifiedAccess.Clear();
        _lastTransactionId = null;
    }

    private Guid? GetActiveTransactionIdAndPrune()
    {
        var txId = context.Database.CurrentTransaction?.TransactionId;
        if (_lastTransactionId != txId ||
            _txAccessibleWarehouses.Count > MaxTransactionCacheEntries ||
            _txVerifiedAccess.Count > MaxTransactionCacheEntries)
        {
            _txAccessibleWarehouses.Clear();
            _txVerifiedAccess.Clear();
            _lastTransactionId = txId;
        }

        return txId;
    }

    public async Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Người dùng chưa xác thực.");

        var tenantId = EffectiveTenantId;
        var txId = GetActiveTransactionIdAndPrune();
        if (txId.HasValue && _txAccessibleWarehouses.TryGetValue((txId.Value, tenantId, currentUser.UserId), out var cachedTxList))
            return cachedTxList;

        IReadOnlyList<int> warehouseIds;
        if (currentUser.IsGlobalAdmin)
        {
            warehouseIds = await context.Warehouses.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            warehouseIds = await context.UserWarehouses.AsNoTracking()
                .Where(x => x.UserId == currentUser.UserId && !context.Warehouses.Any(w => w.Id == x.WarehouseId && !w.IsActive))
                .Select(x => x.WarehouseId)
                .ToListAsync(cancellationToken);
        }

        _accessibleWarehouseCache[(tenantId, currentUser.UserId)] = warehouseIds;
        if (txId.HasValue)
        {
            _txAccessibleWarehouses[(txId.Value, tenantId, currentUser.UserId)] = warehouseIds;
            foreach (var wid in warehouseIds)
                _txVerifiedAccess.Add((txId.Value, tenantId, currentUser.UserId, wid));
        }
        return warehouseIds;
    }

    public async Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (warehouseId <= 0)
            return false;

        var tenantId = EffectiveTenantId;
        var txId = GetActiveTransactionIdAndPrune();
        if (txId.HasValue && _txVerifiedAccess.Contains((txId.Value, tenantId, currentUser.UserId, warehouseId)))
            return true;

        // Global admin bypasses membership, never resource existence or active state.
        // An unknown or inactive warehouse must fail closed like an unassigned one.
        bool allowed;
        if (currentUser.IsGlobalAdmin)
        {
            allowed = await context.Warehouses.AsNoTracking()
                .AnyAsync(x => x.Id == warehouseId && x.IsActive, cancellationToken);
        }
        else
        {
            allowed = await context.UserWarehouses.AsNoTracking()
                .AnyAsync(x =>
                    x.UserId == currentUser.UserId &&
                    x.WarehouseId == warehouseId &&
                    !context.Warehouses.Any(w => w.Id == x.WarehouseId && !w.IsActive),
                    cancellationToken);
        }

        if (allowed)
        {
            if (txId.HasValue)
                _txVerifiedAccess.Add((txId.Value, tenantId, currentUser.UserId, warehouseId));
        }
        else
        {
            _accessibleWarehouseCache.Remove((tenantId, currentUser.UserId));
            if (txId.HasValue)
            {
                _txAccessibleWarehouses.Remove((txId.Value, tenantId, currentUser.UserId));
                _txVerifiedAccess.Remove((txId.Value, tenantId, currentUser.UserId, warehouseId));
            }
        }

        return allowed;
    }

    private string ResolveCorrelationId()
    {
        var httpContext = httpContextAccessor?.HttpContext;
        return httpContext?.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext?.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext?.TraceIdentifier
            ?? Activity.Current?.Id
            ?? "N/A";
    }

    private void LogWarehouseScopeDenied(int warehouseId, int statusCode)
    {
        var actorId = currentUser.IsAuthenticated ? currentUser.UserId : 0;
        logger?.LogWarning(
            "SECURITY_WAREHOUSE_SCOPE_DENIED UserId={UserId} TenantId={TenantId} WarehouseId={WarehouseId} StatusCode={StatusCode} CorrelationId={CorrelationId}",
            actorId,
            EffectiveTenantId,
            warehouseId,
            statusCode,
            ResolveCorrelationId());
    }

    public async Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessWarehouseAsync(warehouseId, cancellationToken))
        {
            LogWarehouseScopeDenied(warehouseId, StatusCodes.Status404NotFound);
            throw new NotFoundException("Không tìm thấy tài nguyên.");
        }
    }

    public async Task EnsureWarehouseCommandAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (warehouseId <= 0)
        {
            LogWarehouseScopeDenied(warehouseId, StatusCodes.Status404NotFound);
            throw new NotFoundException("Không tìm thấy tài nguyên.");
        }

        var warehouse = await context.Warehouses.AsNoTracking()
            .Where(x => x.Id == warehouseId)
            .Select(x => new { x.Id, x.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse is null)
        {
            LogWarehouseScopeDenied(warehouseId, StatusCodes.Status404NotFound);
            throw new NotFoundException("Không tìm thấy tài nguyên.");
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Người dùng chưa xác thực.");
        }

        if (!currentUser.IsGlobalAdmin)
        {
            var hasMembership = await context.UserWarehouses.AsNoTracking()
                .AnyAsync(x => x.UserId == currentUser.UserId && x.WarehouseId == warehouseId, cancellationToken);
            if (!hasMembership)
            {
                LogWarehouseScopeDenied(warehouseId, StatusCodes.Status403Forbidden);
                throw new ForbiddenException("Bạn không có quyền truy cập kho này.", "WAREHOUSE_ACCESS_DENIED");
            }
        }

        if (!warehouse.IsActive)
        {
            var inactiveEx = new BusinessRuleException($"Kho #{warehouseId} hiện đang ngừng hoạt động.");
            inactiveEx.Data["ErrorCode"] = "WAREHOUSE_INACTIVE";
            throw inactiveEx;
        }
    }
}
