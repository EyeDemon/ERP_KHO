using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingTaskOperationsService
{
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
            if (warehouseId.HasValue || accessibleIds == null || accessibleIds.Count > 0)
            {
                var seedWhId = warehouseId ?? (accessibleIds is { Count: > 0 } ? accessibleIds.First() : 1);
                await EnsureDefaultWaveTemplatesAsync(seedWhId, cancellationToken);
            }

            var templateQuery = db.PickingWaveTemplates
                .AsNoTracking()
                .Where(t => t.TemplateCode == requestedTemplateCode);
            if (warehouseId.HasValue)
                templateQuery = templateQuery.Where(t => t.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                templateQuery = templateQuery.Where(t => accessibleIds.Contains(t.WarehouseId));

            matchedTemplate = await templateQuery
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
}
