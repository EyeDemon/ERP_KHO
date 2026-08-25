using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public class StockReservationService(
    ErpKhoDbContext context,
    IInventoryStockRepository stockRepository,
    IUnitOfWork unitOfWork,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    StockReservationOptions options) : IStockReservationService
{
    public async Task<StockReservationDto> CreateAsync(CreateStockReservationDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var exists = await context.Products.AnyAsync(x => x.Id == request.ProductId && x.IsActive, cancellationToken);
        if (!exists) throw new NotFoundException("Không tìm thấy sản phẩm.");
        var expiresAt = request.ExpiresAt ?? DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes);
        if (expiresAt <= DateTime.UtcNow) throw new BusinessRuleException("Thời hạn giữ hàng phải ở tương lai.");

        await unitOfWork.BeginTransactionAsync();
        try
        {
            var reservation = await ReserveCoreAsync("Manual", null, null, request.WarehouseId, request.ProductId, request.Quantity, currentUser.UserId, expiresAt, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return await GetAsync(reservation.Id, cancellationToken);
        }
        catch { await unitOfWork.RollbackTransactionAsync(); throw; }
    }

    public async Task<StockReservation> ReserveForExportAsync(int exportReceiptId, string exportCode, int warehouseId, int productId, decimal quantity, int userId, CancellationToken cancellationToken = default)
    {
        var existing = await context.StockReservations.FirstOrDefaultAsync(x => x.SourceType == "ExportReceipt" && x.SourceId == exportReceiptId && x.ProductId == productId, cancellationToken);
        if (existing is not null) return existing;
        return await ReserveCoreAsync("ExportReceipt", exportReceiptId, exportCode, warehouseId, productId, quantity, userId, DateTime.UtcNow.AddMinutes(options.DefaultExpiryMinutes), cancellationToken);
    }

    public async Task<StockReservation> GetExportReservationAsync(int exportReceiptId, int warehouseId, int productId, CancellationToken cancellationToken = default)
    {
        return await context.StockReservations.FirstOrDefaultAsync(
                   x => x.SourceType == "ExportReceipt" &&
                        x.SourceId == exportReceiptId &&
                        x.WarehouseId == warehouseId &&
                        x.ProductId == productId,
                   cancellationToken)
               ?? throw new ConcurrencyException("Không tìm thấy reservation đã được tạo khi duyệt phiếu xuất.");
    }

    private async Task<StockReservation> ReserveCoreAsync(string sourceType, int? sourceId, string? sourceCode, int warehouseId, int productId, decimal quantity, int userId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        var stale = await context.StockReservations.Where(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.ExpiresAt <= DateTime.UtcNow && (x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed)).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        foreach (var item in stale) await ReleaseCoreAsync(item, null, userId, "Expired before availability check", StockReservationStatus.Expired, cancellationToken);
        if (!await stockRepository.TryReserveAsync(productId, warehouseId, quantity, cancellationToken))
            throw new ConcurrencyException("Không đủ tồn khả dụng để giữ hàng.");
        var reservation = new StockReservation
        {
            ReservationCode = $"RSV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
            ProductId = productId, WarehouseId = warehouseId, Quantity = quantity,
            Status = StockReservationStatus.Active, SourceType = sourceType, SourceId = sourceId, SourceCode = sourceCode,
            CreatedAt = DateTime.UtcNow, CreatedBy = userId, ExpiresAt = expiresAt
        };
        context.StockReservations.Add(reservation);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(Audit(userId, "StockReservation.Created", reservation, $"Quantity: {quantity}, Source: {sourceType}/{sourceId}"));
        return reservation;
    }

    public async Task ConsumeAsync(StockReservation reservation, int userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (reservation.ExpiresAt <= now || reservation.Status is not StockReservationStatus.Active and not StockReservationStatus.PartiallyConsumed)
            throw new ConcurrencyException("Reservation đã hết hạn hoặc không còn hiệu lực.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0 || !await stockRepository.TryConsumeReservationAsync(reservation.ProductId, reservation.WarehouseId, remaining, cancellationToken))
            throw new ConcurrencyException("Reservation không thể được tiêu thụ do dữ liệu tồn kho đã thay đổi.");
        reservation.ConsumedQuantity += remaining;
        reservation.ConsumedAt = now;
        reservation.Status = StockReservationStatus.Consumed;
        context.AuditLogs.Add(Audit(userId, "StockReservation.Consumed", reservation, $"Quantity: {remaining}"));
    }

    public async Task ReleaseSourceAsync(string sourceType, int sourceId, int userId, string reason, CancellationToken cancellationToken = default)
    {
        var reservations = await context.StockReservations.Where(x => x.SourceType == sourceType && x.SourceId == sourceId && (x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed)).OrderBy(x => x.ProductId).ToListAsync(cancellationToken);
        foreach (var reservation in reservations) await ReleaseCoreAsync(reservation, null, userId, reason, StockReservationStatus.Cancelled, cancellationToken);
    }

    public async Task ReleaseAsync(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken = default)
    {
        var reservation = await ScopedQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Không tìm thấy reservation.");
        await unitOfWork.BeginTransactionAsync();
        try
        {
            await ReleaseCoreAsync(reservation, request.Quantity, currentUser.UserId, request.Reason, StockReservationStatus.Released, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
        }
        catch { await unitOfWork.RollbackTransactionAsync(); throw; }
    }

    private async Task ReleaseCoreAsync(StockReservation reservation, decimal? requested, int userId, string reason, StockReservationStatus finalStatus, CancellationToken cancellationToken)
    {
        if (reservation.Status is not StockReservationStatus.Active and not StockReservationStatus.PartiallyConsumed) throw new ConcurrencyException("Reservation không còn có thể giải phóng.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        var quantity = requested ?? remaining;
        if (quantity <= 0 || quantity > remaining) throw new BusinessRuleException("Số lượng giải phóng không hợp lệ.");
        if (!await stockRepository.TryReleaseReservationAsync(reservation.ProductId, reservation.WarehouseId, quantity, cancellationToken)) throw new ConcurrencyException("Dữ liệu giữ hàng đã thay đổi.");
        reservation.ReleasedQuantity += quantity;
        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleasedBy = userId;
        reservation.ReleaseReason = reason;
        reservation.Status = reservation.ConsumedQuantity + reservation.ReleasedQuantity == reservation.Quantity ? finalStatus : StockReservationStatus.PartiallyConsumed;
        context.AuditLogs.Add(Audit(userId, "StockReservation.Released", reservation, $"Quantity: {quantity}, Reason: {reason}"));
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync();
        try
        {
            var expired = await ScopedQuery().Where(x => x.ExpiresAt <= DateTime.UtcNow && (x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed)).OrderBy(x => x.ProductId).ToListAsync(cancellationToken);
            foreach (var item in expired) await ReleaseCoreAsync(item, null, currentUser.UserId, "Expired", StockReservationStatus.Expired, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return expired.Count;
        }
        catch { await unitOfWork.RollbackTransactionAsync(); throw; }
    }

    public async Task<StockReservationPageDto> GetPageAsync(int page, int pageSize, int? warehouseId, int? productId, string? status, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = ScopedQuery().AsNoTracking();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        if (Enum.TryParse<StockReservationStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var entities = await query.Include(x => x.Product).Include(x => x.Warehouse).OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = entities.Select(Map).ToList();
        return new() { Items = items, TotalRecords = total, PageIndex = page, PageSize = pageSize };
    }

    public async Task<StockReservationDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await ScopedQuery().AsNoTracking().Include(x => x.Product).Include(x => x.Warehouse).FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Không tìm thấy reservation.");
        return Map(entity);
    }

    public async Task<IReadOnlyList<ReservationReconciliationIssueDto>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        var issues = new List<ReservationReconciliationIssueDto>();
        var ledger = await context.StockReservations.AsNoTracking().Where(x => allowed.Contains(x.WarehouseId) && (x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed)).GroupBy(x => new { x.ProductId, x.WarehouseId }).Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Reserved = g.Sum(x => x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity) }).ToListAsync(cancellationToken);
        var stocks = await context.InventoryStocks.AsNoTracking().Where(x => allowed.Contains(x.WarehouseId)).ToListAsync(cancellationToken);
        foreach (var key in ledger.Select(x => (x.ProductId, x.WarehouseId)).Union(stocks.Select(x => (x.ProductId, x.WarehouseId))))
        {
            var l = ledger.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId)?.Reserved ?? 0;
            var s = stocks.FirstOrDefault(x => x.ProductId == key.ProductId && x.WarehouseId == key.WarehouseId);
            if (s is null || l != s.ReservedQuantity || (s.Quantity - s.ReservedQuantity) < 0) issues.Add(new() { Issue = s is null ? "MissingStock" : l != s.ReservedQuantity ? "LedgerMismatch" : "NegativeAvailable", ProductId = key.ProductId, WarehouseId = key.WarehouseId, LedgerReserved = l, StockReserved = s?.ReservedQuantity });
        }
        var invalidReservations = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                ((x.Status == StockReservationStatus.Active || x.Status == StockReservationStatus.PartiallyConsumed) &&
                 (x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity <= 0 || x.ExpiresAt <= DateTime.UtcNow)))
            .Select(x => new ReservationReconciliationIssueDto { Issue = x.ExpiresAt <= DateTime.UtcNow ? "ExpiredStillActive" : "ActiveWithoutRemaining", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidReservations);
        var invalidExportSources = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) && x.SourceType == "ExportReceipt" &&
                (!x.SourceId.HasValue || !context.ExportReceipts.Any(e => e.Id == x.SourceId && e.WarehouseId == x.WarehouseId) ||
                 !context.ExportReceiptDetails.Any(d => d.ExportReceiptId == x.SourceId && d.ProductId == x.ProductId)))
            .Select(x => new ReservationReconciliationIssueDto { Issue = "InvalidSourceReference", ReservationId = x.Id, ProductId = x.ProductId, WarehouseId = x.WarehouseId })
            .ToListAsync(cancellationToken);
        issues.AddRange(invalidExportSources);
        return issues;
    }

    private IQueryable<StockReservation> ScopedQuery()
    {
        if (currentUser.IsGlobalAdmin) return context.StockReservations;
        return context.StockReservations.Where(x => context.UserWarehouses.Any(a => a.UserId == currentUser.UserId && a.WarehouseId == x.WarehouseId));
    }
    private static void Validate(CreateStockReservationDto x) { if (x.ProductId <= 0 || x.WarehouseId <= 0 || x.Quantity <= 0) throw new BusinessRuleException("Sản phẩm, kho và số lượng giữ phải hợp lệ."); }
    private static StockReservationDto Map(StockReservation x) => new() { Id = x.Id, ReservationCode = x.ReservationCode, ProductId = x.ProductId, ProductCode = x.Product.Code, ProductName = x.Product.Name, WarehouseId = x.WarehouseId, WarehouseName = x.Warehouse.Name, Quantity = x.Quantity, ConsumedQuantity = x.ConsumedQuantity, ReleasedQuantity = x.ReleasedQuantity, Status = x.Status.ToString(), SourceType = x.SourceType, SourceId = x.SourceId, SourceCode = x.SourceCode, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt };
    private static AuditLog Audit(int userId, string action, StockReservation x, string values) => new() { UserId = userId, Action = action, EntityName = "StockReservation", EntityId = x.Id, Timestamp = DateTime.UtcNow, NewValues = values };
}
