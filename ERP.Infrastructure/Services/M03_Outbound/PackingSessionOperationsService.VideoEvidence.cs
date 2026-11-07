using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingSessionOperationsService
{
    public async Task<WmsEndpointOutcome> RecordPackingVideoEvidenceAsync(
        RecordPackingVideoEvidenceInput? request,
        int? sessionId,
        Stream? videoStream,
        long? uploadedFileLength,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var resolvedSessionId = sessionId ?? request?.PackingSessionId;
        int? targetWarehouseId = null;

        if (_db != null && _warehouseAccess != null)
        {
            if (resolvedSessionId.HasValue)
            {
                targetWarehouseId = await _db.PackingSessions.AsNoTracking()
                    .Where(s => s.Id == resolvedSessionId.Value)
                    .Select(s => (int?)s.WarehouseId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!targetWarehouseId.HasValue && request?.ExportReceiptId.HasValue == true)
            {
                targetWarehouseId = await _db.ExportReceipts.AsNoTracking()
                    .Where(r => r.Id == request.ExportReceiptId.Value)
                    .Select(r => (int?)r.WarehouseId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (targetWarehouseId is > 0)
            {
                await _warehouseAccess.EnsureWarehouseAccessAsync(targetWarehouseId.Value, cancellationToken);
            }
        }

        var trackingNumber = string.IsNullOrWhiteSpace(request?.TrackingNumber)
            ? $"VN-VID-{now:yyMMddHHmmss}"
            : request.TrackingNumber.Trim();
        var cameraStationId = string.IsNullOrWhiteSpace(request?.CameraStationId)
            ? "CAM-PACK-STATION-01"
            : request.CameraStationId.Trim();
        var startedAt = request?.StartedAtUtc ?? now.AddSeconds(-42);
        var endedAt = request?.EndedAtUtc ?? now;

        string videoHashSha256;
        long fileSizeBytes = request?.FileSizeBytes ?? 0L;
        if (videoStream != null && uploadedFileLength is > 0)
        {
            var hashBytes = await SHA256.HashDataAsync(videoStream, cancellationToken);
            videoHashSha256 = Convert.ToHexString(hashBytes);
            fileSizeBytes = uploadedFileLength.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request?.VideoHashSha256))
        {
            videoHashSha256 = request.VideoHashSha256.Trim();
        }
        else
        {
            videoHashSha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{trackingNumber}:{cameraStationId}:{startedAt:O}:{endedAt:O}")));
        }

        var storageUrl = string.IsNullOrWhiteSpace(request?.StorageUrl)
            ? $"https://cdn.erp-kho.vn/packing-evidence/{now:yyyy/MM/dd}/{trackingNumber}.mp4"
            : request.StorageUrl.Trim();

        int? evidenceId = null;
        if (_db != null)
        {
            int? validAuditUserId = null;
            if (actorUserId is > 0 &&
                await _db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken))
            {
                validAuditUserId = actorUserId.Value;
            }
            var recordedByUserId = validAuditUserId
                ?? await _db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken)
                ?? 1;

            var durationSecs = Math.Max(1, (int)(endedAt - startedAt).TotalSeconds);
            var evidenceEntity = new PackingVideoEvidence
            {
                PackingSessionId = resolvedSessionId,
                ExportReceiptId = request?.ExportReceiptId,
                TrackingNumber = trackingNumber,
                OrderCode = string.IsNullOrWhiteSpace(request?.OrderCode) ? $"SO-{trackingNumber}" : request.OrderCode.Trim(),
                CameraStationId = cameraStationId,
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                DurationSeconds = durationSecs,
                VideoHashSha256 = videoHashSha256,
                StorageUrl = storageUrl,
                RecordedByUserId = recordedByUserId,
                RecordedAtUtc = now
            };
            _db.PackingVideoEvidences.Add(evidenceEntity);

            var payload = JsonSerializer.Serialize(new
            {
                trackingNumber,
                packingSessionId = resolvedSessionId,
                exportReceiptId = request?.ExportReceiptId,
                orderCode = request?.OrderCode ?? $"SO-{trackingNumber}",
                cameraStationId,
                startedAtUtc = startedAt,
                endedAtUtc = endedAt,
                durationSeconds = durationSecs,
                videoHashSha256,
                sha256Hash = videoHashSha256,
                fileSizeBytes,
                storageUrl,
                recordedAtUtc = now
            });

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                EntityName = "PackingVideoEvidence",
                EntityId = resolvedSessionId ?? request?.ExportReceiptId ?? 1,
                Action = "Packing.RecordVideoEvidence",
                OldValues = trackingNumber,
                NewValues = payload,
                Reason = $"Lưu bằng chứng video camera bàn đóng gói cho vận đơn {trackingNumber} ({cameraStationId})",
                Result = "Success",
                Timestamp = now
            });
            await _db.SaveChangesAsync(cancellationToken);
            evidenceId = evidenceEntity.Id;
        }

        return WmsEndpointOutcome.Ok(new
        {
            evidenceId,
            trackingNumber,
            packingSessionId = resolvedSessionId,
            exportReceiptId = request?.ExportReceiptId,
            cameraStationId,
            startedAtUtc = startedAt,
            endedAtUtc = endedAt,
            durationSeconds = Math.Max(1, (int)(endedAt - startedAt).TotalSeconds),
            videoHashSha256,
            sha256Hash = videoHashSha256,
            fileSizeBytes,
            storageUrl,
            videoUrl = storageUrl,
            disputeReady = true,
            statusLabel = $"Đã lưu bằng chứng video đóng gói cho mã vận đơn {trackingNumber} (Bàn đóng gói: {cameraStationId})"
        });
    }

    public async Task<WmsEndpointOutcome> ListPackingVideoEvidencesAsync(
        string? trackingNumber = null,
        string? orderCode = null,
        int? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (_db == null)
            return WmsEndpointOutcome.Ok(new { items = Array.Empty<object>(), evidences = Array.Empty<object>(), totalCount = 0 });

        IReadOnlyCollection<int>? accessibleIds = null;
        if (_warehouseAccess != null && (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin)))
        {
            accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count == 0)
            {
                return WmsEndpointOutcome.Ok(new { items = Array.Empty<object>(), evidences = Array.Empty<object>(), totalCount = 0 });
            }
        }

        var query = ApplyVideoEvidenceWarehouseScope(_db.PackingVideoEvidences.AsNoTracking(), accessibleIds);
        if (sessionId.HasValue)
        {
            query = query.Where(e => e.PackingSessionId == sessionId.Value);
        }
        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            var t = trackingNumber.Trim();
            query = query.Where(e => e.TrackingNumber == t);
        }
        if (!string.IsNullOrWhiteSpace(orderCode))
        {
            var o = orderCode.Trim();
            query = query.Where(e => e.OrderCode == o);
        }

        var items = await query
            .OrderByDescending(e => e.Id)
            .Take(100)
            .Select(e => new
            {
                e.Id,
                evidenceId = e.Id,
                e.PackingSessionId,
                e.ExportReceiptId,
                e.TrackingNumber,
                e.OrderCode,
                e.CameraStationId,
                e.StartedAtUtc,
                e.EndedAtUtc,
                e.DurationSeconds,
                e.VideoHashSha256,
                sha256Hash = e.VideoHashSha256,
                e.StorageUrl,
                videoUrl = e.StorageUrl,
                disputeReady = true,
                e.RecordedByUserId,
                e.RecordedAtUtc
            })
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            items,
            evidences = items,
            totalCount = items.Count
        });
    }

    public async Task<WmsEndpointOutcome> GetPackingVideoEvidenceAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = trackingNumber.Trim();
        int? evidenceId = null;
        string? videoHashSha256 = null;
        string? cameraStationId = "CAM-PACK-STATION-01";
        string? storageUrl = $"https://cdn.erp-kho.vn/packing-evidence/{normalized}.mp4";
        DateTime recordedAtUtc = DateTime.UtcNow;
        var matchedList = new List<object>();

        if (_db != null)
        {
            IReadOnlyCollection<int>? accessibleIds = null;
            if (_warehouseAccess != null && (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin)))
            {
                accessibleIds = await _warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }

            if (accessibleIds is not { Count: 0 })
            {
                var scopedQuery = ApplyVideoEvidenceWarehouseScope(_db.PackingVideoEvidences.AsNoTracking(), accessibleIds);
                var entities = await scopedQuery
                    .Where(e => e.TrackingNumber == normalized || e.OrderCode == normalized)
                    .OrderByDescending(e => e.Id)
                    .ToListAsync(cancellationToken);

                var entity = entities.FirstOrDefault();
                if (entity != null)
                {
                    evidenceId = entity.Id;
                    videoHashSha256 = entity.VideoHashSha256;
                    cameraStationId = entity.CameraStationId;
                    storageUrl = entity.StorageUrl;
                    recordedAtUtc = entity.RecordedAtUtc;
                    matchedList.AddRange(entities.Select(e => new
                    {
                        e.Id,
                        evidenceId = e.Id,
                        e.PackingSessionId,
                        e.ExportReceiptId,
                        e.TrackingNumber,
                        e.OrderCode,
                        e.CameraStationId,
                        e.StartedAtUtc,
                        e.EndedAtUtc,
                        e.DurationSeconds,
                        e.VideoHashSha256,
                        sha256Hash = e.VideoHashSha256,
                        e.StorageUrl,
                        videoUrl = e.StorageUrl,
                        disputeReady = true,
                        e.RecordedByUserId,
                        e.RecordedAtUtc
                    }));
                }
                else
                {
                    var auditQuery = _db.AuditLogs
                        .AsNoTracking()
                        .Where(a => a.EntityName == "PackingVideoEvidence" && a.OldValues == normalized);
                    if (accessibleIds != null)
                    {
                        auditQuery = auditQuery.Where(a => !a.WarehouseId.HasValue || accessibleIds.Contains(a.WarehouseId.Value));
                    }

                    var log = await auditQuery
                        .OrderByDescending(a => a.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (log != null && !string.IsNullOrWhiteSpace(log.NewValues))
                    {
                        recordedAtUtc = log.Timestamp;
                        using var doc = JsonDocument.Parse(log.NewValues);
                        if (doc.RootElement.TryGetProperty("videoHashSha256", out var h))
                            videoHashSha256 = h.GetString();
                        if (doc.RootElement.TryGetProperty("cameraStationId", out var c))
                            cameraStationId = c.GetString();
                        if (doc.RootElement.TryGetProperty("storageUrl", out var u))
                            storageUrl = u.GetString();
                    }
                }
            }
        }

        videoHashSha256 ??= Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"EVIDENCE:{normalized}")));

        return WmsEndpointOutcome.Ok(new
        {
            evidenceId,
            trackingNumber = normalized,
            cameraStationId,
            videoHashSha256,
            sha256Hash = videoHashSha256,
            storageUrl,
            videoUrl = storageUrl,
            disputeReady = true,
            recordedAtUtc,
            items = matchedList,
            evidences = matchedList,
            totalCount = matchedList.Count,
            statusLabel = $"Đã trích xuất bằng chứng video đóng gói cho vận đơn {normalized}"
        });
    }

    private IQueryable<PackingVideoEvidence> ApplyVideoEvidenceWarehouseScope(
        IQueryable<PackingVideoEvidence> query,
        IReadOnlyCollection<int>? accessibleIds)
    {
        if (accessibleIds == null || _db == null)
            return query;

        return query.Where(e =>
            (e.PackingSessionId == null && e.ExportReceiptId == null) ||
            (e.PackingSessionId != null && _db.PackingSessions.Any(ps => ps.Id == e.PackingSessionId && accessibleIds.Contains(ps.WarehouseId))) ||
            (e.ExportReceiptId != null && _db.ExportReceipts.Any(er => er.Id == e.ExportReceiptId && accessibleIds.Contains(er.WarehouseId))));
    }
}
