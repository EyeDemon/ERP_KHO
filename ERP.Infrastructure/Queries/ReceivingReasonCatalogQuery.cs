using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public sealed class ReceivingReasonCatalogQuery(ErpKhoDbContext context, TimeProvider clock) : IReceivingReasonCatalogQuery
{
    public async Task<PagedResult<ReceivingReasonCatalogDto>> ListAsync(string? keyword, bool? active, ReasonEffectivity? effectivity, int page, int pageSize, CancellationToken token)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = context.ReceivingReasonCodes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim().ToLowerInvariant();
            query = query.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search));
        }
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        query = effectivity switch
        {
            ReasonEffectivity.NotYetEffective => query.Where(x => x.EffectiveFromUtc > now),
            ReasonEffectivity.Effective => query.Where(x => x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)),
            ReasonEffectivity.Expired => query.Where(x => x.EffectiveToUtc != null && x.EffectiveToUtc <= now),
            _ => query
        };
        page = Math.Clamp(page, 1, int.MaxValue / 100); pageSize = Math.Clamp(pageSize, 1, 100);
        var count = await query.CountAsync(token);
        var rows = await query.OrderBy(x => x.Code).ThenByDescending(x => x.Version).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        return new() { Items = rows.Select(x => Map(x, now)).ToArray(), TotalRecords = count, PageIndex = page, PageSize = pageSize };
    }

    public async Task<ReceivingReasonCatalogDto> DetailAsync(int id, CancellationToken token)
    {
        var row = await context.ReceivingReasonCodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw Missing();
        return Map(row, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<IReadOnlyList<ReceivingReasonCatalogDto>> VersionsAsync(string code, CancellationToken token)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var rows = await context.ReceivingReasonCodes.AsNoTracking().Where(x => x.Code.ToUpper() == normalized)
            .OrderByDescending(x => x.Version).ThenBy(x => x.Id).ToListAsync(token);
        if (rows.Count == 0) throw Missing();
        var now = clock.GetUtcNow().UtcDateTime;
        return rows.Select(x => Map(x, now)).ToArray();
    }

    private static NotFoundException Missing() => new("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");

    private static ReceivingReasonCatalogDto Map(ReceivingReasonCode row, DateTime now) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name, Category = row.Category, Version = row.Version, IsActive = row.IsActive,
        EffectiveFromUtc = new(DateTime.SpecifyKind(row.EffectiveFromUtc, DateTimeKind.Utc)),
        EffectiveToUtc = row.EffectiveToUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.EffectiveToUtc.Value, DateTimeKind.Utc)) : null,
        Effectivity = row.EffectiveFromUtc > now ? ReasonEffectivity.NotYetEffective : row.EffectiveToUtc <= now ? ReasonEffectivity.Expired : ReasonEffectivity.Effective,
        RequiresNote = row.RequiresNote, RequiresAttachment = row.RequiresAttachment, RequiresApproval = row.RequiresApproval
    };
}
