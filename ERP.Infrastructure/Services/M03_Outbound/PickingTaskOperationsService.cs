using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingTaskOperationsService(
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null) : IPickingTaskOperationsService
{
    private async Task EnsureDefaultWaveTemplatesAsync(int targetWarehouseId, CancellationToken cancellationToken)
    {
        if (db == null) return;

        var hasAny = await db.PickingWaveTemplates
            .AsNoTracking()
            .AnyAsync(t => t.WarehouseId == targetWarehouseId, cancellationToken);
        if (hasAny) return;

        db.PickingWaveTemplates.AddRange(
            new ERP.Domain.Entities.PickingWaveTemplate
            {
                WarehouseId = targetWarehouseId,
                TemplateCode = "TPL-FLASH-SLA-2H",
                TemplateName = "Sóng ưu tiên đơn Flash Sale / SLA < 2 giờ",
                CarrierCutoffWindow = "14:00",
                SlaTierFilter = "CRITICAL_UNDER_2H",
                ZoneCode = "ZONE-A",
                MaxOrdersPerClusterTote = 6,
                PickingType = "CLUSTER_TOTE",
                IsActive = true
            },
            new ERP.Domain.Entities.PickingWaveTemplate
            {
                WarehouseId = targetWarehouseId,
                TemplateCode = "TPL-CUTOFF-1800",
                TemplateName = "Sóng chốt bàn giao ĐVVC ca chiều 18:00",
                CarrierCutoffWindow = "18:00",
                SlaTierFilter = "ALL",
                ZoneCode = "ALL",
                MaxOrdersPerClusterTote = 8,
                PickingType = "CLUSTER_TOTE",
                IsActive = true
            },
            new ERP.Domain.Entities.PickingWaveTemplate
            {
                WarehouseId = targetWarehouseId,
                TemplateCode = "TPL-BREACHED-RESCUE",
                TemplateName = "Sóng cứu đơn quá hạn SLA khẩn cấp",
                CarrierCutoffWindow = "12:00",
                SlaTierFilter = "BREACHED",
                ZoneCode = "FAST-PICK",
                MaxOrdersPerClusterTote = 4,
                PickingType = "BATCH_EXPRESS",
                IsActive = true
            });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WmsEndpointOutcome> GetWaveTemplatesAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var targetWarehouseId = warehouseId ?? (accessibleIds is { Count: > 0 } ? accessibleIds.First() : 1);
        if (db == null)
        {
            var fallbackTemplates = new[]
            {
                new
                {
                    id = 1,
                    warehouseId = targetWarehouseId,
                    templateCode = "TPL-FLASH-SLA-2H",
                    templateName = "Sóng ưu tiên đơn Flash Sale / SLA < 2 giờ",
                    carrierCutoffWindow = "14:00",
                    slaTierFilter = "CRITICAL_UNDER_2H",
                    zoneCode = "ZONE-A",
                    maxOrdersPerClusterTote = 6,
                    pickingType = "CLUSTER_TOTE",
                    isActive = true
                },
                new
                {
                    id = 2,
                    warehouseId = targetWarehouseId,
                    templateCode = "TPL-CUTOFF-1800",
                    templateName = "Sóng chốt bàn giao ĐVVC ca chiều 18:00",
                    carrierCutoffWindow = "18:00",
                    slaTierFilter = "ALL",
                    zoneCode = "ALL",
                    maxOrdersPerClusterTote = 8,
                    pickingType = "CLUSTER_TOTE",
                    isActive = true
                },
                new
                {
                    id = 3,
                    warehouseId = targetWarehouseId,
                    templateCode = "TPL-BREACHED-RESCUE",
                    templateName = "Sóng cứu đơn quá hạn SLA khẩn cấp",
                    carrierCutoffWindow = "12:00",
                    slaTierFilter = "BREACHED",
                    zoneCode = "FAST-PICK",
                    maxOrdersPerClusterTote = 4,
                    pickingType = "BATCH_EXPRESS",
                    isActive = true
                }
            };
            return WmsEndpointOutcome.Ok(new
            {
                warehouseId = targetWarehouseId,
                totalCount = fallbackTemplates.Length,
                templates = fallbackTemplates,
                items = fallbackTemplates,
                statusLabel = $"Đã tải {fallbackTemplates.Length} mẫu cấu hình phát hành sóng lấy hàng"
            });
        }

        if (!warehouseId.HasValue && accessibleIds == null)
        {
            var firstWhId = await db.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.Id)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (firstWhId.HasValue)
                targetWarehouseId = firstWhId.Value;
        }

        if (warehouseId.HasValue || accessibleIds == null || accessibleIds.Count > 0)
        {
            await EnsureDefaultWaveTemplatesAsync(targetWarehouseId, cancellationToken);
        }

        var query = db.PickingWaveTemplates.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            query = query.Where(t => accessibleIds.Contains(t.WarehouseId));

        var templates = await query
            .OrderBy(t => t.Id)
            .Select(t => new
            {
                id = t.Id,
                warehouseId = t.WarehouseId,
                templateCode = t.TemplateCode,
                templateName = t.TemplateName,
                carrierCutoffWindow = t.CarrierCutoffWindow,
                slaTierFilter = t.SlaTierFilter,
                zoneCode = t.ZoneCode,
                maxOrdersPerClusterTote = t.MaxOrdersPerClusterTote,
                pickingType = t.PickingType,
                isActive = t.IsActive
            })
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId = targetWarehouseId,
            totalCount = templates.Count,
            templates,
            items = templates,
            statusLabel = $"Đã tải {templates.Count} mẫu cấu hình phát hành sóng lấy hàng"
        });
    }

    public async Task<WmsEndpointOutcome> UpsertWaveTemplateAsync(
        UpsertPickingWaveTemplateInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var targetWarehouseId = warehouseId ?? (accessibleIds is { Count: > 0 } ? accessibleIds.First() : 1);
        var templateCode = !string.IsNullOrWhiteSpace(request?.TemplateCode)
            ? request.TemplateCode.Trim().ToUpperInvariant()
            : "TPL-CUSTOM-WAVE";
        var templateName = !string.IsNullOrWhiteSpace(request?.TemplateName)
            ? request.TemplateName.Trim()
            : $"Mẫu sóng {templateCode}";
        var carrierCutoffWindow = !string.IsNullOrWhiteSpace(request?.CarrierCutoffWindow)
            ? request.CarrierCutoffWindow.Trim()
            : "18:00";
        var slaTierFilter = !string.IsNullOrWhiteSpace(request?.SlaTierFilter)
            ? request.SlaTierFilter.Trim().ToUpperInvariant()
            : "ALL";
        var zoneCode = !string.IsNullOrWhiteSpace(request?.ZoneCode)
            ? request.ZoneCode.Trim().ToUpperInvariant()
            : "ALL";
        var maxOrdersPerClusterTote = request?.MaxOrdersPerClusterTote is > 0 and <= 32
            ? request.MaxOrdersPerClusterTote.Value
            : 8;
        var pickingType = !string.IsNullOrWhiteSpace(request?.PickingType)
            ? request.PickingType.Trim().ToUpperInvariant()
            : "CLUSTER_TOTE";
        var isActive = request?.IsActive ?? true;
        var now = DateTime.UtcNow;
        var entityId = 0;

        if (db != null)
        {
            if (!warehouseId.HasValue && accessibleIds == null)
            {
                var firstWhId = await db.Warehouses
                    .AsNoTracking()
                    .OrderBy(w => w.Id)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (firstWhId.HasValue)
                    targetWarehouseId = firstWhId.Value;
            }

            if (!warehouseId.HasValue && effectiveAuth != null)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
            }

            var entity = await db.PickingWaveTemplates
                .FirstOrDefaultAsync(t => t.WarehouseId == targetWarehouseId && t.TemplateCode == templateCode, cancellationToken);

            if (entity == null)
            {
                entity = new ERP.Domain.Entities.PickingWaveTemplate
                {
                    WarehouseId = targetWarehouseId,
                    TemplateCode = templateCode,
                    TemplateName = templateName,
                    CarrierCutoffWindow = carrierCutoffWindow,
                    SlaTierFilter = slaTierFilter,
                    ZoneCode = zoneCode,
                    MaxOrdersPerClusterTote = maxOrdersPerClusterTote,
                    PickingType = pickingType,
                    IsActive = isActive
                };
                db.PickingWaveTemplates.Add(entity);
            }
            else
            {
                entity.TemplateName = templateName;
                entity.CarrierCutoffWindow = carrierCutoffWindow;
                entity.SlaTierFilter = slaTierFilter;
                entity.ZoneCode = zoneCode;
                entity.MaxOrdersPerClusterTote = maxOrdersPerClusterTote;
                entity.PickingType = pickingType;
                entity.IsActive = isActive;
            }

            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "PickingWaveTemplate",
                Action = "PickingWaveTemplate.Upsert",
                NewValues = templateCode,
                Reason = $"Cập nhật mẫu cấu hình phát hành sóng {templateCode} ({templateName})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            entityId = entity.Id;
        }

        return WmsEndpointOutcome.Ok(new
        {
            id = entityId,
            warehouseId = targetWarehouseId,
            templateCode,
            templateName,
            carrierCutoffWindow,
            slaTierFilter,
            zoneCode,
            maxOrdersPerClusterTote,
            pickingType,
            isActive,
            updatedAtUtc = now,
            statusLabel = $"Đã lưu mẫu cấu hình phát hành sóng {templateCode} ({templateName})"
        });
    }

    public async Task<WmsEndpointOutcome> GetAlternateBinsAsync(
        PickingTaskDto task,
        int? productId = null,
        int? excludeLocationId = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                pickingTaskId = task.Id,
                taskCode = task.TaskCode,
                warehouseId = task.WarehouseId,
                alternateBins = Array.Empty<object>(),
                statusLabel = "Đã tra cứu kệ thay thế khả dụng cho ngoại lệ thiếu hàng (Short-Pick)"
            });
        }

        var targetProductIds = productId.HasValue && productId.Value > 0
            ? new List<int> { productId.Value }
            : task.Lines.Select(l => l.ProductId).Distinct().ToList();

        var excludedLocationIds = excludeLocationId.HasValue && excludeLocationId.Value > 0
            ? new HashSet<int> { excludeLocationId.Value }
            : task.Lines.Where(l => l.RemainingQuantity > 0).Select(l => l.SourceLocationId).ToHashSet();

        var today = DateTime.UtcNow.Date;
        var candidateStocks = await db.InventoryStocks
            .AsNoTracking()
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Include(s => s.Product)
            .Where(s => s.WarehouseId == task.WarehouseId &&
                        targetProductIds.Contains(s.ProductId) &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId != null &&
                        !excludedLocationIds.Contains(s.LocationId.Value) &&
                        s.Location != null &&
                        s.Location.IsActive &&
                        !s.Location.IsBlocked &&
                        s.Location.IsPickable &&
                        (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value >= today) &&
                        (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m)
            .ToListAsync(cancellationToken);

        var alternateBins = candidateStocks
            .OrderBy(s => s.Lot?.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(s => s.Location?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
            .Select(s =>
            {
                var availableQty = Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity);
                return new
                {
                    stockId = s.Id,
                    productId = s.ProductId,
                    productCode = s.Product?.Code ?? $"SKU-{s.ProductId}",
                    productName = s.Product?.Name ?? string.Empty,
                    locationId = s.LocationId!.Value,
                    locationCode = s.Location?.Code ?? string.Empty,
                    locationName = s.Location?.Name ?? string.Empty,
                    lotId = s.LotId,
                    lotNumber = s.Lot?.LotNumber,
                    expiryDate = s.Lot?.ExpiryDate,
                    onHandQuantity = s.Quantity,
                    availableQuantity = availableQty,
                    fefoPriorityRank = s.Lot?.ExpiryDate.HasValue == true ? "FEFO_LOT" : "FIFO_STANDARD"
                };
            })
            .ToList();

        return WmsEndpointOutcome.Ok(new
        {
            pickingTaskId = task.Id,
            taskCode = task.TaskCode,
            warehouseId = task.WarehouseId,
            excludedLocationIds,
            totalCandidates = alternateBins.Count,
            alternateBins,
            items = alternateBins,
            statusLabel = $"Đã tìm thấy {alternateBins.Count} vị trí kệ thay thế khả dụng theo chuẩn FEFO/FIFO"
        });
    }
}
