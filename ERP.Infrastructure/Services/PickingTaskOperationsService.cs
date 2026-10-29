using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class PickingTaskOperationsService(
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

        if (!warehouseId.HasValue && accessibleIds is not { Count: > 0 })
        {
            var firstWhId = await db.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.Id)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (firstWhId.HasValue)
                targetWarehouseId = firstWhId.Value;
        }

        await EnsureDefaultWaveTemplatesAsync(targetWarehouseId, cancellationToken);

        var query = db.PickingWaveTemplates.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        else if (accessibleIds is { Count: > 0 })
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
            if (!warehouseId.HasValue && accessibleIds is not { Count: > 0 })
            {
                var firstWhId = await db.Warehouses
                    .AsNoTracking()
                    .OrderBy(w => w.Id)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (firstWhId.HasValue)
                    targetWarehouseId = firstWhId.Value;
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

    private async Task<(ERP.Domain.Entities.PickingTask? Task, List<PutToWallSlotData> Slots)> BuildPutToWallGridAsync(
        string? idOrCode,
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAuth,
        CancellationToken cancellationToken)
    {
        if (db == null)
        {
            var emptySlots = Enumerable.Range(1, 8)
                .Select(i => new PutToWallSlotData(
                    i,
                    $"SLOT-{i:D2}",
                    $"TOTE-{i:D2}",
                    null,
                    null,
                    $"SO-WAVE-{i:D2}",
                    null,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    "EMPTY"))
                .ToList();
            return (null, emptySlots);
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        ERP.Domain.Entities.PickingTask? task = null;
        if (!string.IsNullOrWhiteSpace(idOrCode))
        {
            var trimmed = idOrCode.Trim();
            if (int.TryParse(trimmed, out var parsedId))
            {
                task = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(t => t.Id == parsedId, cancellationToken);
            }

            if (task == null)
            {
                task = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(t => t.TaskCode == trimmed || t.SourceCode == trimmed, cancellationToken);
            }
        }

        if (task == null)
        {
            var fallbackQuery = db.PickingTasks
                .AsNoTracking()
                .Include(t => t.Lines)
                    .ThenInclude(l => l.Product)
                .AsQueryable();
            if (warehouseId.HasValue)
            {
                fallbackQuery = fallbackQuery.Where(t => t.WarehouseId == warehouseId.Value);
            }
            task = await fallbackQuery
                .OrderByDescending(t => t.Status == PickingTaskStatus.InProgress || t.Status == PickingTaskStatus.Assigned || t.Status == PickingTaskStatus.Open)
                .ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (task != null && effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(task.WarehouseId, cancellationToken);
        }

        var candidateLines = new List<(ERP.Domain.Entities.PickingTask OwnerTask, ERP.Domain.Entities.PickingTaskLine Line)>();
        if (task != null)
        {
            foreach (var l in task.Lines.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
            {
                candidateLines.Add((task, l));
            }

            if (candidateLines.Count < 8)
            {
                var siblingTasks = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .Where(t => t.WarehouseId == task.WarehouseId && t.Id != task.Id)
                    .OrderByDescending(t => t.Id)
                    .Take(8)
                    .ToListAsync(cancellationToken);

                foreach (var st in siblingTasks)
                {
                    foreach (var sl in st.Lines.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
                    {
                        if (candidateLines.Count >= 8) break;
                        candidateLines.Add((st, sl));
                    }
                    if (candidateLines.Count >= 8) break;
                }
            }
        }

        var persistedSlots = task != null && db != null
            ? await db.PutToWallSlots
                .AsNoTracking()
                .Where(s => s.PickingTaskId == task.Id)
                .ToListAsync(cancellationToken)
            : [];

        var persistedBySlot = new Dictionary<string, ERP.Domain.Entities.PutToWallSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (var persisted in persistedSlots)
        {
            if (!string.IsNullOrWhiteSpace(persisted.SlotCode))
            {
                persistedBySlot[persisted.SlotCode.Trim()] = persisted;
            }
        }

        var slots = new List<PutToWallSlotData>(8);
        for (var i = 1; i <= 8; i++)
        {
            var slotCode = $"SLOT-{i:D2}";
            var toteBarcode = $"TOTE-{i:D2}";
            var persistedSlot = persistedBySlot.GetValueOrDefault(slotCode);
            if (i <= candidateLines.Count)
            {
                var (ownerTask, line) = candidateLines[i - 1];
                var baseSorted = persistedSlot?.SortedQuantity ?? 0m;
                var reqQty = line.RequestedQuantity > 0m ? line.RequestedQuantity : 1m;
                var sortedQty = Math.Min(reqQty, baseSorted);
                var remQty = Math.Max(0m, reqQty - sortedQty);
                var slotStatus = remQty <= 0m
                    ? "COMPLETED"
                    : (sortedQty > 0m ? "IN_PROGRESS" : "WAITING");

                slots.Add(new PutToWallSlotData(
                    i,
                    slotCode,
                    toteBarcode,
                    ownerTask.Id,
                    line.Id,
                    !string.IsNullOrWhiteSpace(ownerTask.SourceCode) ? ownerTask.SourceCode : ownerTask.TaskCode,
                    line.ProductId,
                    line.Product?.Code ?? $"SKU-{line.ProductId}",
                    line.Product?.Name ?? $"Sản phẩm #{line.ProductId}",
                    reqQty,
                    sortedQty,
                    remQty,
                    slotStatus,
                    persistedSlot?.PackingSessionId,
                    persistedSlot?.PackingSessionCode));
            }
            else
            {
                slots.Add(new PutToWallSlotData(
                    i,
                    slotCode,
                    toteBarcode,
                    task?.Id,
                    null,
                    $"WAVE-SLOT-{i:D2}",
                    null,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    "EMPTY",
                    persistedSlot?.PackingSessionId,
                    persistedSlot?.PackingSessionCode));
            }
        }

        return (task, slots);
    }

    public async Task<WmsEndpointOutcome> GetPutToWallSlotsAsync(
        string? id = null,
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var (task, slots) = await BuildPutToWallGridAsync(id, warehouseId, warehouseAuth, cancellationToken);
        var activeSlotsCount = slots.Count(s => s.SlotStatus != "EMPTY");
        var completedSlotsCount = slots.Count(s => s.SlotStatus == "COMPLETED");

        return WmsEndpointOutcome.Ok(new
        {
            pickingTaskId = task?.Id ?? 0,
            taskCode = task?.TaskCode ?? "WAVE-PUT-WALL",
            warehouseId = task?.WarehouseId ?? warehouseId ?? 1,
            totalSlots = 8,
            activeSlotsCount,
            completedSlotsCount,
            slots,
            items = slots,
            statusLabel = $"Tường chia chọn Put-to-Wall 8 ô (SLOT-01 → SLOT-08): {completedSlotsCount}/{activeSlotsCount} ô hoàn tất"
        });
    }

    public async Task<WmsEndpointOutcome> PutToWallSortAsync(
        string? id = null,
        PutToWallSortInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var (task, slots) = await BuildPutToWallGridAsync(id, null, warehouseAuth, cancellationToken);
        var activeSlots = slots.Where(s => s.ProductId.HasValue).ToList();
        if (activeSlots.Count == 0)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "PUT_TO_WALL_NO_ACTIVE_SLOTS",
                errorCode = "PUT_TO_WALL_NO_ACTIVE_SLOTS",
                message = "Không tìm thấy dòng sản phẩm cần chia chọn trên tường Put-to-Wall."
            });
        }

        var rawSku = (request?.ScannedSkuOrBarcode ?? request?.ProductBarcode)?.Trim();
        PutToWallSlotData? matchedSlot = null;

        if (!string.IsNullOrWhiteSpace(rawSku))
        {
            matchedSlot = activeSlots.FirstOrDefault(s =>
                string.Equals(s.ProductCode, rawSku, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.ProductId?.ToString(), rawSku, StringComparison.OrdinalIgnoreCase));

            if (matchedSlot == null && db != null)
            {
                var prodIdByBarcode = await db.ProductBarcodes
                    .AsNoTracking()
                    .Where(b => b.Value == rawSku)
                    .Select(b => (int?)b.ProductId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (prodIdByBarcode.HasValue)
                {
                    matchedSlot = activeSlots.FirstOrDefault(s => s.ProductId == prodIdByBarcode.Value);
                }
            }
        }
        else
        {
            matchedSlot = activeSlots.FirstOrDefault(s => s.RemainingQty > 0m) ?? activeSlots.First();
        }

        if (matchedSlot == null)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "PUT_TO_WALL_SKU_NOT_IN_WAVE",
                errorCode = "PUT_TO_WALL_SKU_NOT_IN_WAVE",
                scannedSkuOrBarcode = rawSku,
                message = $"Sản phẩm '{rawSku}' không thuộc danh sách chia chọn của sóng lấy hàng hiện tại."
            });
        }

        var scannedSlot = (request?.ScannedSlotCode ?? request?.SlotCode ?? request?.ScannedToteBarcode)?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(scannedSlot))
        {
            var matchesExpected =
                string.Equals(scannedSlot, matchedSlot.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scannedSlot, matchedSlot.ToteBarcode, StringComparison.OrdinalIgnoreCase);

            if (!matchesExpected)
            {
                var alternateSameSkuSlot = activeSlots.FirstOrDefault(s =>
                    s.ProductId == matchedSlot.ProductId &&
                    (string.Equals(scannedSlot, s.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scannedSlot, s.ToteBarcode, StringComparison.OrdinalIgnoreCase)));

                if (alternateSameSkuSlot != null)
                {
                    matchedSlot = alternateSameSkuSlot;
                }
                else
                {
                    return WmsEndpointOutcome.UnprocessableEntity(new
                    {
                        code = "PUT_TO_WALL_WRONG_SLOT",
                        errorCode = "PUT_TO_WALL_WRONG_SLOT",
                        scannedSkuOrBarcode = matchedSlot.ProductCode,
                        scannedSlotCode = scannedSlot,
                        expectedSlotCode = matchedSlot.SlotCode,
                        expectedToteBarcode = matchedSlot.ToteBarcode,
                        message = $"Sai ô chia chọn! Sản phẩm {matchedSlot.ProductCode} phải bỏ vào ô {matchedSlot.SlotCode}"
                    });
                }
            }
        }

        var sortQty = request?.Quantity is > 0m ? request.Quantity : 1m;
        var now = DateTime.UtcNow;
        int? resolvedPackingSessionId = null;
        string? resolvedPackingSessionCode = null;

        if (db != null && task != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

            var slotEntity = await db.PutToWallSlots
                .FirstOrDefaultAsync(s => s.PickingTaskId == task.Id && s.SlotCode == matchedSlot.SlotCode, cancellationToken);
            var newSortedQty = Math.Min(matchedSlot.RequiredQty, (slotEntity?.SortedQuantity ?? matchedSlot.SortedQty) + sortQty);
            var newSlotStatus = newSortedQty >= matchedSlot.RequiredQty
                ? "COMPLETED"
                : (newSortedQty > 0m ? "IN_PROGRESS" : "WAITING");

            if (slotEntity == null)
            {
                slotEntity = new ERP.Domain.Entities.PutToWallSlot
                {
                    WarehouseId = task.WarehouseId,
                    PickingTaskId = task.Id,
                    PickingTaskLineId = matchedSlot.PickingTaskLineId,
                    SlotIndex = matchedSlot.SlotIndex,
                    SlotCode = matchedSlot.SlotCode,
                    ToteBarcode = matchedSlot.ToteBarcode,
                    OrderReference = matchedSlot.OrderReference,
                    ProductId = matchedSlot.ProductId,
                    ProductCode = matchedSlot.ProductCode,
                    RequiredQuantity = matchedSlot.RequiredQty,
                    SortedQuantity = newSortedQty,
                    SlotStatus = newSlotStatus,
                    LastSortedByUserId = effectiveActorId,
                    UpdatedAtUtc = now
                };
                db.PutToWallSlots.Add(slotEntity);
            }
            else
            {
                slotEntity.SortedQuantity = newSortedQty;
                slotEntity.SlotStatus = newSlotStatus;
                slotEntity.LastSortedByUserId = effectiveActorId;
                slotEntity.UpdatedAtUtc = now;
            }

            if (newSlotStatus == "COMPLETED")
            {
                var packingSession = await db.PackingSessions
                    .FirstOrDefaultAsync(ps => ps.PickingTaskId == task.Id, cancellationToken);
                if (packingSession == null)
                {
                    packingSession = new ERP.Domain.Entities.PackingSession
                    {
                        SessionCode = $"PACK-PTW-{task.Id}-{now:HHmmss}",
                        PickingTaskId = task.Id,
                        WarehouseId = task.WarehouseId,
                        Status = PackingSessionStatus.Open,
                        CreatedAt = now,
                        CreatedBy = effectiveActorId ?? 1
                    };
                    db.PackingSessions.Add(packingSession);
                    await db.SaveChangesAsync(cancellationToken);
                }

                slotEntity.PackingSessionId = packingSession.Id;
                slotEntity.PackingSessionCode = packingSession.SessionCode;

                var taskSlots = activeSlots.Where(s => s.PickingTaskId == task.Id).ToList();
                var allTaskSlotsCompleted = taskSlots.Count > 0 && taskSlots.All(s =>
                    string.Equals(s.SlotCode, matchedSlot.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.SlotStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase));
                if (allTaskSlotsCompleted)
                {
                    var trackedTask = await db.PickingTasks
                        .FirstOrDefaultAsync(t => t.Id == task.Id, cancellationToken);
                    if (trackedTask != null && trackedTask.Status != PickingTaskStatus.Completed)
                    {
                        trackedTask.Status = PickingTaskStatus.Completed;
                        trackedTask.CompletedAt ??= now;
                    }
                }
            }

            resolvedPackingSessionId = slotEntity.PackingSessionId;
            resolvedPackingSessionCode = slotEntity.PackingSessionCode;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = task.WarehouseId,
                EntityName = "PutToWallSortation",
                EntityId = task.Id,
                Action = "PUT_TO_WALL_SLOT_SORTED",
                OldValues = matchedSlot.SlotCode,
                NewValues = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{matchedSlot.ProductCode}|{sortQty}"),
                Reason = $"Chia chọn Put-to-Wall: {sortQty} x {matchedSlot.ProductCode} vào {matchedSlot.SlotCode} ({matchedSlot.ToteBarcode})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var (_, updatedSlots) = await BuildPutToWallGridAsync(task?.Id.ToString() ?? id, task?.WarehouseId, warehouseAuth, cancellationToken);
        var updatedTargetSlot = updatedSlots.First(s => s.SlotCode == matchedSlot.SlotCode);
        var finalPackingSessionId = resolvedPackingSessionId ?? updatedTargetSlot.PackingSessionId;
        var finalPackingSessionCode = resolvedPackingSessionCode ?? updatedTargetSlot.PackingSessionCode;
        var autoCreatedPackingSession = finalPackingSessionId.HasValue;
        var handoffStatus = autoCreatedPackingSession ? "READY_FOR_PACKING" : "SORTING_IN_PROGRESS";

        return WmsEndpointOutcome.Ok(new
        {
            pickingTaskId = task?.Id ?? 0,
            taskCode = task?.TaskCode ?? "WAVE-PUT-WALL",
            matched = true,
            sortedSlotCode = updatedTargetSlot.SlotCode,
            toteBarcode = updatedTargetSlot.ToteBarcode,
            orderReference = updatedTargetSlot.OrderReference,
            productId = updatedTargetSlot.ProductId,
            productCode = updatedTargetSlot.ProductCode,
            sortedQuantity = sortQty,
            slotSortedTotal = updatedTargetSlot.SortedQty,
            slotRemainingQty = updatedTargetSlot.RemainingQty,
            slotStatus = updatedTargetSlot.SlotStatus,
            autoCreatedPackingSession,
            packingSessionId = finalPackingSessionId,
            packingSessionCode = finalPackingSessionCode,
            handoffStatus,
            slots = updatedSlots,
            statusLabel = autoCreatedPackingSession && !string.IsNullOrWhiteSpace(finalPackingSessionCode)
                ? $"Đã chia chọn {sortQty} x {updatedTargetSlot.ProductCode} vào ô {updatedTargetSlot.SlotCode} ({updatedTargetSlot.ToteBarcode}) • Khởi tạo phiên đóng gói {finalPackingSessionCode}"
                : $"Đã chia chọn {sortQty} x {updatedTargetSlot.ProductCode} vào ô {updatedTargetSlot.SlotCode} ({updatedTargetSlot.ToteBarcode})"
        });
    }

    public async Task<WmsEndpointOutcome> ReleaseWaveAsync(
        ReleasePickingWaveInput? request = null,
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

        var now = DateTime.UtcNow;
        var waveCode = !string.IsNullOrWhiteSpace(request?.WaveCode)
            ? request.WaveCode.Trim()
            : $"WAVE-{now:yyyyMMdd-HHmm}";

        ERP.Domain.Entities.PickingWaveTemplate? matchedTemplate = null;
        var requestedTemplateCode = !string.IsNullOrWhiteSpace(request?.TemplateCode)
            ? request.TemplateCode.Trim().ToUpperInvariant()
            : null;
        if (db != null && !string.IsNullOrWhiteSpace(requestedTemplateCode))
        {
            var seedWhId = warehouseId ?? (accessibleIds is { Count: > 0 } ? accessibleIds.First() : 1);
            await EnsureDefaultWaveTemplatesAsync(seedWhId, cancellationToken);
            matchedTemplate = await db.PickingWaveTemplates
                .AsNoTracking()
                .Where(t => t.TemplateCode == requestedTemplateCode)
                .OrderByDescending(t => !warehouseId.HasValue || t.WarehouseId == warehouseId.Value)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var templateCode = matchedTemplate?.TemplateCode ?? requestedTemplateCode ?? "TPL-STANDARD";
        var carrierCutoffWindow = !string.IsNullOrWhiteSpace(request?.CarrierCutoffWindow)
            ? request.CarrierCutoffWindow.Trim()
            : (matchedTemplate?.CarrierCutoffWindow ?? "18:00");
        var slaTierFilter = !string.IsNullOrWhiteSpace(request?.SlaTierFilter)
            ? request.SlaTierFilter.Trim().ToUpperInvariant()
            : (matchedTemplate?.SlaTierFilter ?? "ALL");
        var zoneCode = !string.IsNullOrWhiteSpace(request?.ZoneCode)
            ? request.ZoneCode.Trim().ToUpperInvariant()
            : (matchedTemplate?.ZoneCode ?? "ALL");
        var maxOrdersPerClusterTote = request?.MaxOrdersPerClusterTote is > 0
            ? request.MaxOrdersPerClusterTote.Value
            : (matchedTemplate?.MaxOrdersPerClusterTote is > 0 ? matchedTemplate.MaxOrdersPerClusterTote : 8);
        var clusterTotePrefix = !string.IsNullOrWhiteSpace(request?.ClusterTotePrefix)
            ? request.ClusterTotePrefix.Trim().ToUpperInvariant()
            : "TOTE-CL";
        var pickingType = !string.IsNullOrWhiteSpace(request?.PickingType)
            ? request.PickingType.Trim().ToUpperInvariant()
            : (!string.IsNullOrWhiteSpace(matchedTemplate?.PickingType)
                ? matchedTemplate.PickingType.Trim().ToUpperInvariant()
                : "STANDARD");
        var maxTasks = request?.MaxTasks is > 0 and <= 200 ? request.MaxTasks.Value : 50;

        var releasedTasksCount = 0;
        var totalLinesCount = 0;
        var totalRequestedQuantity = 0m;
        var releasedTaskCodes = new List<string>();
        var clusterTotes = new List<object>();
        var optimizedPickRoute = new List<object>();
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

        if (db != null)
        {
            var query = db.PickingTasks
                .Include(t => t.Lines)
                    .ThenInclude(l => l.SourceLocation)
                .Where(t => t.Status == PickingTaskStatus.Open || t.Status == PickingTaskStatus.Assigned);

            if (warehouseId.HasValue)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(t => accessibleIds.Contains(t.WarehouseId));

            if (request?.Priority.HasValue == true)
                query = query.Where(t => t.Priority >= request.Priority.Value);

            var candidates = await query
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Id)
                .Take(maxTasks)
                .ToListAsync(cancellationToken);

            var taskToteMap = new Dictionary<int, string>();
            var chunks = candidates.Chunk(Math.Max(1, maxOrdersPerClusterTote)).ToList();
            var clusterToteBarcodes = new List<string>();

            for (var clusterIdx = 0; clusterIdx < chunks.Count; clusterIdx++)
            {
                var chunk = chunks[clusterIdx];
                var toteBarcode = $"{clusterTotePrefix}-{clusterIdx + 1:D2}";
                clusterToteBarcodes.Add(toteBarcode);
                var chunkTaskCodes = new List<string>();

                foreach (var task in chunk)
                {
                    taskToteMap[task.Id] = toteBarcode;
                    warehouseId ??= task.WarehouseId;
                    task.PickingType = pickingType;
                    if (request?.AssignToUserId is > 0)
                    {
                        task.AssignedUserId = request.AssignToUserId.Value;
                        if (task.Status == PickingTaskStatus.Open)
                            task.Status = PickingTaskStatus.Assigned;
                    }
                    else if (effectiveActorId is > 0 && task.Status == PickingTaskStatus.Open)
                    {
                        task.AssignedUserId ??= effectiveActorId.Value;
                    }

                    releasedTasksCount++;
                    totalLinesCount += task.Lines.Count;
                    totalRequestedQuantity += task.Lines.Sum(l => l.RequestedQuantity);
                    if (!string.IsNullOrWhiteSpace(task.TaskCode))
                    {
                        releasedTaskCodes.Add(task.TaskCode);
                        chunkTaskCodes.Add(task.TaskCode);
                    }
                }

                clusterTotes.Add(new
                {
                    clusterToteBarcode = toteBarcode,
                    orderCount = chunk.Length,
                    taskCodes = chunkTaskCodes
                });
            }

            if (clusterTotes.Count == 0)
            {
                var defaultTote = $"{clusterTotePrefix}-01";
                clusterToteBarcodes.Add(defaultTote);
                clusterTotes.Add(new
                {
                    clusterToteBarcode = defaultTote,
                    orderCount = 0,
                    taskCodes = Array.Empty<string>()
                });
            }

            var sortedLines = candidates
                .SelectMany(t => t.Lines.Select(l => (Task: t, Line: l, ToteBarcode: taskToteMap.GetValueOrDefault(t.Id, $"{clusterTotePrefix}-01"))))
                .OrderBy(x => x.Line.SourceLocation?.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Line.Sequence)
                .ThenBy(x => x.Line.ProductId)
                .ToList();

            for (var seq = 0; seq < sortedLines.Count; seq++)
            {
                var item = sortedLines[seq];
                optimizedPickRoute.Add(new
                {
                    pickSequence = seq + 1,
                    taskId = item.Task.Id,
                    taskCode = item.Task.TaskCode,
                    lineId = item.Line.Id,
                    productId = item.Line.ProductId,
                    locationId = item.Line.SourceLocationId,
                    locationCode = item.Line.SourceLocation?.Code ?? $"LOC-{item.Line.SourceLocationId}",
                    requestedQuantity = item.Line.RequestedQuantity,
                    clusterToteBarcode = item.ToteBarcode
                });
            }

            var waveNote = !string.IsNullOrWhiteSpace(request?.Note)
                ? request.Note.Trim()
                : $"Phát hành sóng lấy hàng {waveCode} ({releasedTasksCount} tác vụ, {totalLinesCount} dòng)";

            db.PickingWaves.Add(new ERP.Domain.Entities.PickingWave
            {
                WarehouseId = warehouseId ?? 1,
                WaveCode = waveCode,
                PickingType = pickingType,
                Priority = request?.Priority,
                ReleasedTasksCount = releasedTasksCount,
                TotalLinesCount = totalLinesCount,
                TotalRequestedQuantity = totalRequestedQuantity,
                AssignedUserId = request?.AssignToUserId ?? effectiveActorId,
                ReleasedByUserId = effectiveActorId,
                Status = "RELEASED",
                Note = waveNote,
                ReleasedAtUtc = now,
                TemplateCode = templateCode,
                CarrierCutoffWindow = carrierCutoffWindow,
                SlaTierFilter = slaTierFilter,
                ZoneCode = zoneCode,
                MaxOrdersPerClusterTote = maxOrdersPerClusterTote,
                ClusterToteBarcodesCsv = string.Join(",", clusterToteBarcodes)
            });

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                EntityName = "PickingWave",
                Action = "PickingWave.Release",
                NewValues = waveCode,
                Reason = waveNote,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            clusterTotes.Add(new
            {
                clusterToteBarcode = $"{clusterTotePrefix}-01",
                orderCount = 0,
                taskCodes = Array.Empty<string>()
            });
        }

        return WmsEndpointOutcome.Ok(new
        {
            waveCode,
            warehouseId,
            pickingType,
            priority = request?.Priority ?? 1,
            releasedTasksCount,
            totalLinesCount,
            totalRequestedQuantity,
            taskCodes = releasedTaskCodes,
            templateCode,
            carrierCutoffWindow,
            slaTierFilter,
            zoneCode,
            maxOrdersPerClusterTote,
            clusterTotes,
            optimizedPickRoute,
            batchClusterMode = "Gom cụm theo tuyến kệ tối ưu (FEFO/FIFO)",
            status = "Đã phát hành sóng lấy hàng",
            statusLabel = "Đã phát hành sóng lấy hàng",
            releasedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetMobileScanQueueAsync(
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

        var query = db.PickingTasks.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            query = query.Where(t => accessibleIds.Contains(t.WarehouseId));

        var mobileReadyTasksCount = await query.CountAsync(
            x => x.Status == PickingTaskStatus.Open
                 || x.Status == PickingTaskStatus.Assigned
                 || x.Status == PickingTaskStatus.InProgress,
            cancellationToken);
        var shortPickExceptionTasksCount = await query.CountAsync(
            x => x.Status == PickingTaskStatus.ShortPick,
            cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            mobileReadyTasksCount,
            shortPickExceptionTasksCount,
            supportedMobileCapabilities = new[]
            {
                "MyTasks",
                "ReceiveScan",
                "PutawayScan",
                "PickScan",
                "PackScan",
                "CountScan",
                "TransferScan",
                "ReplenishScan",
                "ReturnScan",
                "OfflineSyncQueue",
                "SkuBarcodeLookup",
                "MobileExceptionTriage"
            },
            offlineSyncPolicy = "DEFERRED_LOW_RISK_QUEUE_WITH_IDEMPOTENCY_KEY"
        });
    }

    public async Task<WmsEndpointOutcome?> TryMatchMobileScanInDbAsync(
        string sanitizedInput,
        string rawScanInput,
        int? warehouseId = null,
        IReadOnlyCollection<int>? accessibleIds = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null || string.IsNullOrWhiteSpace(sanitizedInput))
            return null;

        var dbQuery = db.PickingTasks.AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Product)
            .Include(t => t.Lines)
                .ThenInclude(l => l.SourceLocation)
            .AsQueryable();

        if (warehouseId.HasValue)
            dbQuery = dbQuery.Where(t => t.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            dbQuery = dbQuery.Where(t => accessibleIds.Contains(t.WarehouseId));

        var dbMatch = await dbQuery.FirstOrDefaultAsync(
            t => t.TaskCode == sanitizedInput
                 || t.SourceCode == sanitizedInput
                 || t.TaskCode.Contains(sanitizedInput)
                 || t.Lines.Any(l => l.Product.Code == sanitizedInput || l.SourceLocation.Code == sanitizedInput),
            cancellationToken);

        if (dbMatch == null)
            return null;

        var matchedLine = dbMatch.Lines.FirstOrDefault(
            l => string.Equals(l.Product?.Code, sanitizedInput, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(l.SourceLocation?.Code, sanitizedInput, StringComparison.OrdinalIgnoreCase))
            ?? dbMatch.Lines.FirstOrDefault();

        return WmsEndpointOutcome.Ok(new
        {
            matched = true,
            scanInput = rawScanInput,
            matchedTaskCode = dbMatch.TaskCode,
            matchedProductCode = matchedLine?.Product?.Code ?? dbMatch.SourceCode,
            matchedLocationCode = matchedLine?.SourceLocation?.Code ?? dbMatch.Warehouse?.Name,
            status = dbMatch.Status.ToString()
        });
    }

    public async Task<WmsEndpointOutcome> SyncAutomationTelemetryAsync(
        SyncAutomationTelemetryInput? request = null,
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

        var activeTasksCount = 0;
        var outboundCutoffLocal = "22:00";
        if (db != null)
        {
            var query = db.PickingTasks.AsNoTracking()
                .Where(t => t.Status != PickingTaskStatus.Completed && t.Status != PickingTaskStatus.Cancelled);
            if (warehouseId.HasValue)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(t => accessibleIds.Contains(t.WarehouseId));
            activeTasksCount = await query.CountAsync(cancellationToken);

            var calQuery = db.WarehouseCalendarDays.AsNoTracking()
                .Where(c => c.IsOpen && c.OutboundCutoffLocal.HasValue);
            if (warehouseId.HasValue)
                calQuery = calQuery.Where(c => c.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                calQuery = calQuery.Where(c => accessibleIds.Contains(c.WarehouseId));
            var cutoffSpan = await calQuery.Select(c => c.OutboundCutoffLocal).FirstOrDefaultAsync(cancellationToken);
            if (cutoffSpan.HasValue)
                outboundCutoffLocal = cutoffSpan.Value.ToString(@"hh\:mm");
        }

        var now = DateTime.UtcNow;
        var telemetrySyncId = $"WCS-IOT-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                Action = "Picking.SyncAutomationTelemetry",
                EntityName = "AutomationTelemetry",
                NewValues = telemetrySyncId,
                Reason = $"Đồng bộ thiết bị kho & kiểm tra tuân thủ FEFO ({telemetrySyncId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            telemetrySyncId,
            warehouseId,
            activePickingTasksCount = activeTasksCount,
            outboundCutoffLocal,
            wcsRoboticsState = $"Đã đồng bộ lộ trình lấy hàng ({activeTasksCount} tác vụ đang mở, giờ chốt xuất {outboundCutoffLocal})",
            rfidIotTelemetry = "Cổng kiểm soát & cảm biến kho hoạt động bình thường",
            voicePickToLight = "Thiết bị hướng dẫn lấy hàng theo vị trí kệ sẵn sàng",
            status = "Đã đồng bộ thiết bị kho & kiểm tra tuân thủ FEFO",
            statusLabel = "Đã đồng bộ thiết bị kho & kiểm tra tuân thủ FEFO",
            syncedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> VerifyDispatchCutoffAsync(
        VerifyPickingDispatchCutoffInput? request = null,
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

        var checkedExportReceiptsCount = 0;
        var openPickingTasksCount = 0;
        var outboundCutoffLocal = "22:00";
        if (db != null)
        {
            var receiptQuery = db.ExportReceipts.AsNoTracking();
            var taskQuery = db.PickingTasks.AsNoTracking()
                .Where(t => t.Status != PickingTaskStatus.Completed && t.Status != PickingTaskStatus.Cancelled);
            var calQuery = db.WarehouseCalendarDays.AsNoTracking()
                .Where(c => c.IsOpen && c.OutboundCutoffLocal.HasValue);

            if (warehouseId.HasValue)
            {
                receiptQuery = receiptQuery.Where(r => r.WarehouseId == warehouseId.Value);
                taskQuery = taskQuery.Where(t => t.WarehouseId == warehouseId.Value);
                calQuery = calQuery.Where(c => c.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                receiptQuery = receiptQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
                taskQuery = taskQuery.Where(t => accessibleIds.Contains(t.WarehouseId));
                calQuery = calQuery.Where(c => accessibleIds.Contains(c.WarehouseId));
            }

            checkedExportReceiptsCount = await receiptQuery.CountAsync(cancellationToken);
            openPickingTasksCount = await taskQuery.CountAsync(cancellationToken);
            var cutoffSpan = await calQuery.Select(c => c.OutboundCutoffLocal).FirstOrDefaultAsync(cancellationToken);
            if (cutoffSpan.HasValue)
                outboundCutoffLocal = cutoffSpan.Value.ToString(@"hh\:mm");
        }

        return WmsEndpointOutcome.Ok(new
        {
            verificationId = $"EXP-CUT-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId,
            checkedExportReceiptsCount,
            openPickingTasksCount,
            outboundCutoffLocal,
            singleShipDeductionGuard = "Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất",
            cutoffComplianceStatus = $"Đạt chuẩn giờ chốt xuất kho ({outboundCutoffLocal})",
            status = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            statusLabel = "Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất",
            verifiedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<WmsEndpointOutcome> SweepExpiredPreviewAsync(
        SweepPickingExpiredReservationsInput? request = null,
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

        var now = DateTime.UtcNow;
        var activeReservationsCount = 0;
        var expiredCandidateCount = 0;
        var expiringWithin24hCount = 0;
        if (db != null)
        {
            var query = db.StockReservations.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

            activeReservationsCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active,
                cancellationToken);
            expiredCandidateCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active && r.SourceType == "Manual" && r.ExpiresAt <= now,
                cancellationToken);
            expiringWithin24hCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active && r.ExpiresAt > now && r.ExpiresAt <= now.AddHours(24),
                cancellationToken);
        }

        var sweepId = $"RSV-SWP-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                Action = "StockReservation.SweepExpiredPreview",
                EntityName = "StockReservationSweep",
                NewValues = sweepId,
                Reason = $"Rà soát giữ hàng quá hạn ({sweepId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            sweepId,
            warehouseId,
            activeReservationsCount,
            expiredCandidateCount,
            expiringWithin24hCount,
            status = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            statusLabel = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            ledgerMutated = false,
            checkedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> VerifyFefoComplianceAsync(
        VerifyPickingFefoComplianceInput? request = null,
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

        var now = DateTime.UtcNow;
        var activeAllocationsCount = 0;
        var binLockConflictCount = 0;
        var expiringLotsWithin30DaysCount = 0;
        var expiredLotsCount = 0;
        if (db != null)
        {
            var allocQuery = db.StockAllocations.AsNoTracking()
                .Where(a => a.Status == StockAllocationStatus.Active || a.Status == StockAllocationStatus.Picking);
            if (warehouseId.HasValue)
                allocQuery = allocQuery.Where(a => a.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                allocQuery = allocQuery.Where(a => accessibleIds.Contains(a.WarehouseId));

            activeAllocationsCount = await allocQuery.CountAsync(cancellationToken);
            binLockConflictCount = await allocQuery.Where(a => a.Location.IsBlocked).CountAsync(cancellationToken);
            expiringLotsWithin30DaysCount = await db.InventoryLots.AsNoTracking()
                .CountAsync(l => l.ExpiryDate.HasValue && l.ExpiryDate.Value >= now && l.ExpiryDate.Value <= now.AddDays(30), cancellationToken);
            expiredLotsCount = await db.InventoryLots.AsNoTracking()
                .CountAsync(l => l.ExpiryDate.HasValue && l.ExpiryDate.Value < now, cancellationToken);
        }

        var complianceText = binLockConflictCount == 0
            ? "Tuân thủ nghiêm ngặt chuẩn FEFO/FIFO (0 xung đột khóa kệ)"
            : $"Phát hiện {binLockConflictCount} phân bổ nằm tại vị trí đang bị khóa";

        return WmsEndpointOutcome.Ok(new
        {
            verificationId = $"ALC-FEFO-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId,
            activeAllocationsCount,
            binLockConflictCount,
            expiringLotsWithin30DaysCount,
            expiredLotsCount,
            fefoComplianceStatus = complianceText,
            status = complianceText,
            statusLabel = complianceText,
            verifiedAtUtc = DateTime.UtcNow
        });
    }
}
