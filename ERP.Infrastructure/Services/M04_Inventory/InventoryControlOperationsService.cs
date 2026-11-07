using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService(
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null) : IInventoryControlOperationsService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _replenishLocks = new();

    private bool IsAuthenticatedNonAdmin() =>
        currentUser is { IsAuthenticated: true } &&
        !currentUser.IsGlobalAdmin &&
        currentUser.Role is not "Admin" and not "SuperAdmin";

    private async Task<IReadOnlyList<int>?> ResolveScopedWarehouseIdsAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? effectiveAuth,
        CancellationToken cancellationToken)
    {
        if (warehouseId.HasValue)
        {
            if (effectiveAuth != null)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            return [warehouseId.Value];
        }

        if (effectiveAuth != null)
        {
            var accessible = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (IsAuthenticatedNonAdmin() || accessible.Count > 0)
            {
                return accessible;
            }
            return null;
        }

        if (IsAuthenticatedNonAdmin() && db != null)
        {
            return await db.UserWarehouses.AsNoTracking()
                .Where(uw => uw.UserId == currentUser!.UserId &&
                             !db.Warehouses.Any(w => w.Id == uw.WarehouseId && !w.IsActive))
                .Select(uw => uw.WarehouseId)
                .ToListAsync(cancellationToken);
        }

        return null;
    }

    private async Task<(int WarehouseId, WmsEndpointOutcome? Error)> ResolveTargetWarehouseIdAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? effectiveAuth,
        CancellationToken cancellationToken)
    {
        if (warehouseId.HasValue)
        {
            if (warehouseId.Value <= 0)
            {
                return (0, WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "INVALID_WAREHOUSE_ID",
                    errorCode = "INVALID_WAREHOUSE_ID",
                    message = "Mã kho (WarehouseId) phải lớn hơn 0."
                }));
            }

            if (effectiveAuth != null)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }

            if (db != null && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
            {
                return (0, WmsEndpointOutcome.NotFound(new
                {
                    code = "WAREHOUSE_NOT_FOUND",
                    errorCode = "WAREHOUSE_NOT_FOUND",
                    message = $"Không tìm thấy kho #{warehouseId.Value}."
                }));
            }

            return (warehouseId.Value, null);
        }

        if (effectiveAuth != null)
        {
            var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count > 0)
            {
                return (accessibleIds[0], null);
            }

            if (IsAuthenticatedNonAdmin())
            {
                return (0, WmsEndpointOutcome.Forbid());
            }
        }
        else if (IsAuthenticatedNonAdmin() && db != null)
        {
            var accessibleIds = await db.UserWarehouses.AsNoTracking()
                .Where(uw => uw.UserId == currentUser!.UserId &&
                             !db.Warehouses.Any(w => w.Id == uw.WarehouseId && !w.IsActive))
                .Select(uw => uw.WarehouseId)
                .ToListAsync(cancellationToken);
            if (accessibleIds.Count == 0)
            {
                return (0, WmsEndpointOutcome.Forbid());
            }
            return (accessibleIds[0], null);
        }

        if (db != null)
        {
            var activeWhId = await db.Warehouses.AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (activeWhId > 0)
            {
                return (activeWhId, null);
            }
        }

        return (0, WmsEndpointOutcome.NotFound(new
        {
            code = "WAREHOUSE_NOT_FOUND",
            errorCode = "WAREHOUSE_NOT_FOUND",
            message = "Không tìm thấy kho đang hoạt động hợp lệ."
        }));
    }

    public async Task<WmsEndpointOutcome> UpsertLocationReplenishmentRuleAsync(
        UpsertLocationReplenishmentRuleInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return WmsEndpointOutcome.Json(400, new
            {
                code = "REPLENISH_RULE_REQUEST_REQUIRED",
                errorCode = "REPLENISH_RULE_REQUEST_REQUIRED",
                message = "Dữ liệu cấu hình ngưỡng bổ sung kệ nhặt (Min/Max) không hợp lệ."
            });
        }

        if ((request.MinBaseQty.HasValue && request.MinBaseQty.Value < 0m) ||
            (request.MaxBaseQty.HasValue && request.MaxBaseQty.Value <= 0m) ||
            (request.MinBaseQty.HasValue && request.MaxBaseQty.HasValue && request.MaxBaseQty.Value <= request.MinBaseQty.Value))
        {
            return WmsEndpointOutcome.Json(422, new
            {
                code = "REPLENISH_INVALID_MIN_MAX",
                errorCode = "REPLENISH_INVALID_MIN_MAX",
                minBaseQty = request.MinBaseQty,
                maxBaseQty = request.MaxBaseQty,
                message = "Ngưỡng tồn tối thiểu (MinBaseQty) phải >= 0 và ngưỡng tối đa (MaxBaseQty) phải lớn hơn MinBaseQty."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (request.WarehouseId.HasValue)
        {
            if (request.WarehouseId.Value <= 0)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "INVALID_WAREHOUSE_ID",
                    errorCode = "INVALID_WAREHOUSE_ID",
                    message = "Mã kho (WarehouseId) không hợp lệ."
                });
            }

            if (!await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == request.WarehouseId.Value, cancellationToken))
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "WAREHOUSE_NOT_FOUND",
                    errorCode = "WAREHOUSE_NOT_FOUND",
                    message = $"Không tìm thấy kho #{request.WarehouseId.Value}."
                });
            }

            if (effectiveAuth != null)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(request.WarehouseId.Value, cancellationToken);
            }
        }

        ERP.Domain.Entities.WarehouseLocation? loc = null;
        if (request.LocationId is > 0)
        {
            loc = await db.WarehouseLocations.FirstOrDefaultAsync(l => l.Id == request.LocationId.Value, cancellationToken);
            if (loc != null && request.WarehouseId is > 0 && loc.WarehouseId != request.WarehouseId.Value)
            {
                loc = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            var code = request.LocationCode.Trim().ToUpperInvariant();
            var q = db.WarehouseLocations.Where(l => l.Code == code);
            if (request.WarehouseId is > 0)
                q = q.Where(l => l.WarehouseId == request.WarehouseId.Value);
            loc = await q.FirstOrDefaultAsync(cancellationToken);
        }

        if (loc == null)
        {
            return WmsEndpointOutcome.Json(404, new
            {
                code = "LOCATION_NOT_FOUND",
                errorCode = "LOCATION_NOT_FOUND",
                message = "Không tìm thấy vị trí kệ nhặt lẻ cần cấu hình ngưỡng Min/Max."
            });
        }

        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(loc.WarehouseId, cancellationToken);
        }

        int? resolvedReplenishFromId = request.ReplenishFromLocationId;
        if (!resolvedReplenishFromId.HasValue && !string.IsNullOrWhiteSpace(request.ReplenishFromLocationCode))
        {
            var srcCode = request.ReplenishFromLocationCode.Trim().ToUpperInvariant();
            resolvedReplenishFromId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == loc.WarehouseId && l.Code == srcCode)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var newMin = request.MinBaseQty ?? loc.MinBaseQty;
        var newMax = request.MaxBaseQty ?? loc.MaxBaseQty;
        if ((newMin.HasValue && newMin.Value < 0m) ||
            (newMax.HasValue && newMax.Value <= 0m) ||
            (newMin.HasValue && newMax.HasValue && newMax.Value <= newMin.Value))
        {
            return WmsEndpointOutcome.Json(422, new
            {
                code = "REPLENISH_INVALID_MIN_MAX",
                errorCode = "REPLENISH_INVALID_MIN_MAX",
                minBaseQty = newMin,
                maxBaseQty = newMax,
                message = "Ngưỡng tồn tối thiểu (MinBaseQty) phải >= 0 và ngưỡng tối đa (MaxBaseQty) phải lớn hơn MinBaseQty."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        loc.MinBaseQty = newMin.HasValue ? Math.Round(newMin.Value, 4) : null;
        loc.MaxBaseQty = newMax.HasValue ? Math.Round(newMax.Value, 4) : null;
        if (resolvedReplenishFromId.HasValue)
        {
            loc.ReplenishFromLocationId = resolvedReplenishFromId.Value;
        }
        loc.UpdatedAt = now;
        loc.UpdatedBy = effectiveActorId;

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = loc.WarehouseId,
            EntityName = "WarehouseLocation",
            EntityId = loc.Id,
            Action = "UPSERT_LOCATION_REPLENISHMENT_RULE",
            NewValues = $"Code={loc.Code};Min={loc.MinBaseQty:0.####};Max={loc.MaxBaseQty:0.####};FromLocId={loc.ReplenishFromLocationId}",
            Reason = $"Cấu hình ngưỡng bổ sung kệ nhặt lẻ {loc.Code} (Min={loc.MinBaseQty:0.####}, Max={loc.MaxBaseQty:0.####})",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            locationId = loc.Id,
            locationCode = loc.Code,
            locationName = loc.Name,
            warehouseId = loc.WarehouseId,
            minBaseQty = loc.MinBaseQty,
            maxBaseQty = loc.MaxBaseQty,
            replenishFromLocationId = loc.ReplenishFromLocationId,
            updated = true,
            statusLabel = $"Đã cập nhật ngưỡng bổ sung tự động cho vị trí {loc.Code} (Min: {loc.MinBaseQty:0.##} / Max: {loc.MaxBaseQty:0.##})"
        });
    }
}
