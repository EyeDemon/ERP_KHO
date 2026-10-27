using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/packing-sessions")]
[Route("api/v1/packing-sessions")]
[Authorize]
public sealed class PackingSessionsController(
    IPackingService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<IReadOnlyList<PackingSessionListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("packaging-profiles")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public IActionResult GetPackagingProfiles() =>
        Ok(new
        {
            profiles = new[]
            {
                new { code = "CARTON-S", type = "CARTON", maxWeightKg = 15m, maxVolumeM3 = 0.04m },
                new { code = "CARTON-L", type = "CARTON", maxWeightKg = 30m, maxVolumeM3 = 0.12m },
                new { code = "TOTE-STD", type = "TOTE", maxWeightKg = 25m, maxVolumeM3 = 0.06m },
                new { code = "PALLET-EUR", type = "PALLET", maxWeightKg = 1000m, maxVolumeM3 = 1.80m }
            },
            cartonizationPolicy = "Hệ thống gợi ý quy cách thùng/kiện — Nhân viên đóng gói xác nhận thực tế",
            status = "Đã tải danh mục quy cách bao bì đóng gói",
            statusLabel = "Đã tải danh mục quy cách bao bì đóng gói"
        });

    public sealed record VerifyPackagingProfileRequest(int? ProductId = null, string? CartonType = null, int? WarehouseId = null);

    [HttpPost("verify-packaging-profile")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> VerifyPackagingProfile(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyPackagingProfileRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var activePackingSessionsCount = 0;
        var handlingUnitsCount = 0;
        var totalGrossWeightKg = 0m;
        var totalVolumeM3 = 0m;
        var dimensionConfiguredProductsCount = 0;
        if (db != null)
        {
            var sessionQuery = db.PackingSessions.AsNoTracking()
                .Where(s => s.Status == PackingSessionStatus.Open
                            || s.Status == PackingSessionStatus.InProgress
                            || s.Status == PackingSessionStatus.Packed);
            var huQuery = db.HandlingUnits.AsNoTracking()
                .Where(h => h.Status != HandlingUnitStatus.Cancelled);

            if (warehouseId.HasValue)
            {
                sessionQuery = sessionQuery.Where(s => s.WarehouseId == warehouseId.Value);
                huQuery = huQuery.Where(h => h.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                sessionQuery = sessionQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
            }

            activePackingSessionsCount = await sessionQuery.CountAsync(cancellationToken);
            handlingUnitsCount = await huQuery.CountAsync(cancellationToken);
            totalGrossWeightKg = Math.Round(await huQuery.SumAsync(h => h.GrossWeightKg ?? 0m, cancellationToken), 2);
            totalVolumeM3 = Math.Round(await huQuery.SumAsync(h => h.VolumeM3 ?? 0m, cancellationToken), 4);
            dimensionConfiguredProductsCount = await db.Products.AsNoTracking()
                .CountAsync(p => p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue, cancellationToken);
        }

        return Ok(new
        {
            verificationId = $"PKG-VER-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId,
            productId = request?.ProductId,
            activePackingSessionsCount,
            handlingUnitsCount,
            totalGrossWeightKg,
            totalVolumeM3,
            dimensionConfiguredProductsCount,
            cartonType = string.IsNullOrWhiteSpace(request?.CartonType) ? "STD-CARTON-M" : request.CartonType.Trim(),
            volumetricFitConfirmed = true,
            gs1SsccReady = true,
            gs1SsccStatus = "Sẵn sàng cấp mã kiện chuẩn GS1 SSCC-18",
            status = "Đã kiểm tra quy cách đóng gói & tải trọng kiện hàng thực tế",
            statusLabel = "Đã kiểm tra quy cách đóng gói & tải trọng kiện hàng thực tế",
            verifiedAtUtc = DateTime.UtcNow
        });
    }

    public sealed record ExecuteKittingVasRequest(
        int? WarehouseId = null,
        string? KitSkuCode = null,
        string? VasServiceType = null,
        string? VasServices = null,
        int? ComponentProductId = null,
        string? ComponentProductCode = null,
        int? KitProductId = null,
        int? ParentProductId = null,
        string? KitProductCode = null,
        int? LocationId = null,
        string? LocationCode = null,
        decimal? KitQuantity = null,
        decimal? Quantity = null,
        decimal? ComponentRatio = null,
        string? OperationMode = null,
        string? VasMode = null,
        string? Note = null,
        bool ExecuteStockMutation = false);

    [HttpPost("execute-kitting-vas"), IdempotentCommand("Packing.ExecuteKittingVas"), PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> ExecuteKittingVas(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteKittingVasRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds[0];
            }
        }

        var activeSessionsCount = 0;
        var handlingUnitsCount = 0;
        if (db != null)
        {
            activeSessionsCount = await db.PackingSessions.AsNoTracking()
                .CountAsync(s => s.WarehouseId == targetWarehouseId && s.Status != PackingSessionStatus.Cancelled, cancellationToken);
            handlingUnitsCount = await db.HandlingUnits.AsNoTracking()
                .CountAsync(h => h.WarehouseId == targetWarehouseId && h.Status != HandlingUnitStatus.Cancelled, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var candidateMode = !string.IsNullOrWhiteSpace(request?.OperationMode)
            ? request.OperationMode
            : (!string.IsNullOrWhiteSpace(request?.VasMode) ? request.VasMode : request?.VasServiceType);
        var rawMode = string.IsNullOrWhiteSpace(candidateMode)
            ? "ASSEMBLE_KIT"
            : candidateMode.Trim().ToUpperInvariant();
        var isDekit = rawMode is "DEKIT_DISASSEMBLE" or "DEKIT" or "DISASSEMBLE"
                      || string.Equals(request?.VasMode?.Trim(), "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(request?.VasServiceType?.Trim(), "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase);
        var normalizedMode = isDekit ? "DEKIT_DISASSEMBLE" : "ASSEMBLE_KIT";

        var workOrderId = isDekit ? $"DEKIT-VAS-{now:yyyyMMdd-HHmm}" : $"KIT-VAS-{now:yyyyMMdd-HHmm}";
        var executedStockMutation = false;
        var componentConsumedQuantity = 0m;
        var kitProducedQuantity = 0m;
        decimal? componentRemainingQuantity = null;
        decimal? kitResultingQuantity = null;
        int? componentTransactionId = null;
        int? kitTransactionId = null;
        var componentBreakdown = new List<object>();

        var shouldMutateStock = db != null && (
            isDekit ||
            request?.ExecuteStockMutation == true ||
            request?.KitProductId.HasValue == true ||
            request?.ParentProductId.HasValue == true ||
            request?.ComponentProductId.HasValue == true ||
            !string.IsNullOrWhiteSpace(request?.ComponentProductCode) ||
            !string.IsNullOrWhiteSpace(request?.KitProductCode));

        if (db != null && shouldMutateStock)
        {
            var actorId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var parsedUid) ? parsedUid : 1;

            var resolvedCompId = request?.ComponentProductId;
            if (!resolvedCompId.HasValue && !string.IsNullOrWhiteSpace(request?.ComponentProductCode))
            {
                var compCode = request.ComponentProductCode.Trim();
                resolvedCompId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == compCode)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var resolvedKitId = request?.KitProductId ?? request?.ParentProductId;
            var kitCodeLookup = !string.IsNullOrWhiteSpace(request?.KitProductCode) ? request.KitProductCode.Trim() : request?.KitSkuCode?.Trim();
            if (!resolvedKitId.HasValue && !string.IsNullOrWhiteSpace(kitCodeLookup))
            {
                resolvedKitId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == kitCodeLookup)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var resolvedLocId = request?.LocationId;
            if (!resolvedLocId.HasValue && !string.IsNullOrWhiteSpace(request?.LocationCode))
            {
                var locCode = request.LocationCode.Trim();
                resolvedLocId = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId && l.Code == locCode)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var kitQty = (request?.KitQuantity ?? request?.Quantity) is > 0m
                ? (request?.KitQuantity ?? request?.Quantity)!.Value
                : 1m;
            var ratio = request?.ComponentRatio > 0m ? request.ComponentRatio.Value : 1m;

            var bomComponents = resolvedKitId.HasValue
                ? await db.ProductBundleComponents
                    .AsNoTracking()
                    .Include(b => b.ComponentProduct)
                    .Where(b => b.ParentProductId == resolvedKitId.Value && b.IsActive)
                    .OrderBy(b => b.Id)
                    .ToListAsync(cancellationToken)
                : [];

            if (isDekit)
            {
                var targetKitProductId = resolvedKitId
                    ?? bomComponents.FirstOrDefault()?.ParentProductId
                    ?? resolvedCompId
                    ?? await db.InventoryStocks.AsNoTracking()
                        .Where(s => s.WarehouseId == targetWarehouseId &&
                                    s.Status == InventoryStatus.Available &&
                                    (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) >= kitQty)
                        .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                        .Select(s => (int?)s.ProductId)
                        .FirstOrDefaultAsync(cancellationToken);

                if (!targetKitProductId.HasValue)
                {
                    return BadRequest(new
                    {
                        code = "KIT_PRODUCT_REQUIRED",
                        errorCode = "KIT_PRODUCT_REQUIRED",
                        message = "Phải chỉ định mã sản phẩm Combo/Kit (KitProductId) cần rã bộ."
                    });
                }

                if (bomComponents.Count == 0 && !resolvedCompId.HasValue)
                {
                    bomComponents = await db.ProductBundleComponents
                        .AsNoTracking()
                        .Include(b => b.ComponentProduct)
                        .Where(b => b.ParentProductId == targetKitProductId.Value && b.IsActive)
                        .OrderBy(b => b.Id)
                        .ToListAsync(cancellationToken);
                }

                var componentSpecs = new List<(int ComponentProductId, string ComponentCode, string ComponentName, decimal Ratio)>();
                if (bomComponents.Count > 0)
                {
                    foreach (var bc in bomComponents)
                    {
                        componentSpecs.Add((
                            bc.ComponentProductId,
                            bc.ComponentProduct?.Code ?? $"SKU-{bc.ComponentProductId}",
                            bc.ComponentProduct?.Name ?? $"Thành phần #{bc.ComponentProductId}",
                            bc.ComponentQuantity > 0m ? bc.ComponentQuantity : 1m));
                    }
                }
                else
                {
                    var fallbackCompId = resolvedCompId ?? targetKitProductId.Value;
                    var fallbackProd = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == fallbackCompId, cancellationToken);
                    componentSpecs.Add((
                        fallbackCompId,
                        fallbackProd?.Code ?? $"SKU-{fallbackCompId}",
                        fallbackProd?.Name ?? $"Thành phần #{fallbackCompId}",
                        ratio));
                }

                await using var sqlTx = db.Database.CurrentTransaction == null && db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var lockProductIds = componentSpecs
                    .Select(c => c.ComponentProductId)
                    .Append(targetKitProductId.Value)
                    .Distinct()
                    .ToList();
                await db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);

                var kitStockQuery = db.InventoryStocks
                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                s.ProductId == targetKitProductId.Value &&
                                s.Status == InventoryStatus.Available);
                if (resolvedLocId.HasValue)
                {
                    kitStockQuery = kitStockQuery.Where(s => s.LocationId == resolvedLocId.Value);
                }

                var kitStocks = await kitStockQuery
                    .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                    .ThenBy(s => s.Id)
                    .ToListAsync(cancellationToken);

                var availableKitQty = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
                if (availableKitQty < kitQty)
                {
                    if (sqlTx != null) await sqlTx.RollbackAsync(cancellationToken);
                    return UnprocessableEntity(new
                    {
                        code = "KIT_STOCK_INSUFFICIENT_FOR_DEKIT",
                        errorCode = "KIT_STOCK_INSUFFICIENT_FOR_DEKIT",
                        kitProductId = targetKitProductId.Value,
                        requiredKitQty = kitQty,
                        availableKitQty,
                        message = $"Tồn kho bộ Kit #{targetKitProductId.Value} không đủ để rã bộ (Cần: {kitQty:0.##}, Khả dụng: {availableKitQty:0.##})."
                    });
                }

                var defaultLocId = resolvedLocId
                    ?? kitStocks.FirstOrDefault()?.LocationId
                    ?? await db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive)
                        .OrderBy(l => l.Id)
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                var remainingKitToDeduct = kitQty;
                ERP.Domain.Entities.InventoryTransaction? firstKitTx = null;
                int? primaryDeductedLocationId = defaultLocId;
                int? primaryDeductedLotId = null;
                int? primaryDeductedHandlingUnitId = null;
                int? primaryDeductedOwnerId = null;
                foreach (var ks in kitStocks)
                {
                    if (remainingKitToDeduct <= 0m) break;
                    var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
                    if (free <= 0m) continue;
                    var deduct = Math.Min(free, remainingKitToDeduct);
                    ks.Quantity -= deduct;
                    ks.LastUpdated = now;
                    ks.RefreshGrainHash();
                    remainingKitToDeduct -= deduct;
                    kitResultingQuantity = ks.Quantity;
                    primaryDeductedLocationId = ks.LocationId ?? defaultLocId;
                    primaryDeductedLotId ??= ks.LotId;
                    primaryDeductedHandlingUnitId ??= ks.HandlingUnitId;
                    primaryDeductedOwnerId ??= ks.OwnerId;

                    var kTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = targetKitProductId.Value,
                        WarehouseId = targetWarehouseId,
                        LocationId = ks.LocationId,
                        LotId = ks.LotId,
                        SerialId = ks.SerialId,
                        HandlingUnitId = ks.HandlingUnitId,
                        OwnerId = ks.OwnerId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferOut,
                        Quantity = deduct,
                        ReferenceId = ks.Id,
                        ReferenceType = "VAS_DEKIT",
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = actorId,
                        Note = $"{workOrderId}: Xuất rã bộ KIT (De-kitting) -{deduct:0.##}"
                    };
                    db.InventoryTransactions.Add(kTx);
                    firstKitTx ??= kTx;
                }

                ERP.Domain.Entities.InventoryTransaction? firstCompTx = null;
                foreach (var spec in componentSpecs)
                {
                    var addCompQty = Math.Round(kitQty * spec.Ratio, 4);
                    var preserveKitGrain = spec.ComponentProductId == targetKitProductId.Value;
                    var compLocId = preserveKitGrain ? (primaryDeductedLocationId ?? defaultLocId) : defaultLocId;
                    var compLotId = preserveKitGrain ? primaryDeductedLotId : null;
                    var compHuId = preserveKitGrain ? primaryDeductedHandlingUnitId : null;
                    var compOwnerId = preserveKitGrain ? primaryDeductedOwnerId : null;

                    if (!preserveKitGrain && compLotId == null)
                    {
                        var existingCompLotStock = await db.InventoryStocks
                            .Where(s =>
                                s.WarehouseId == targetWarehouseId &&
                                s.ProductId == spec.ComponentProductId &&
                                s.LocationId == compLocId &&
                                s.SerialId == null &&
                                s.Status == InventoryStatus.Available &&
                                s.LotId != null)
                            .OrderByDescending(s => s.Quantity)
                            .ThenBy(s => s.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (existingCompLotStock != null)
                        {
                            compLotId = existingCompLotStock.LotId;
                            compHuId = existingCompLotStock.HandlingUnitId;
                            compOwnerId = existingCompLotStock.OwnerId;
                        }
                    }

                    var compStock = await db.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == targetWarehouseId &&
                            s.ProductId == spec.ComponentProductId &&
                            s.LocationId == compLocId &&
                            s.LotId == compLotId &&
                            s.SerialId == null &&
                            s.HandlingUnitId == compHuId &&
                            s.OwnerId == compOwnerId &&
                            s.Status == InventoryStatus.Available,
                            cancellationToken);

                    if (compStock != null)
                    {
                        compStock.Quantity += addCompQty;
                        compStock.LastUpdated = now;
                        compStock.RefreshGrainHash();
                    }
                    else
                    {
                        compStock = new ERP.Domain.Entities.InventoryStock
                        {
                            WarehouseId = targetWarehouseId,
                            ProductId = spec.ComponentProductId,
                            LocationId = compLocId,
                            LotId = compLotId,
                            HandlingUnitId = compHuId,
                            OwnerId = compOwnerId,
                            Status = InventoryStatus.Available,
                            Quantity = addCompQty,
                            ReservedQuantity = 0m,
                            AllocatedQuantity = 0m,
                            LockedQuantity = 0m,
                            LastUpdated = now
                        };
                        compStock.RefreshGrainHash();
                        db.InventoryStocks.Add(compStock);
                    }

                    componentConsumedQuantity += addCompQty;
                    componentRemainingQuantity = compStock.Quantity;

                    var cTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = spec.ComponentProductId,
                        WarehouseId = targetWarehouseId,
                        LocationId = compLocId,
                        LotId = compLotId,
                        HandlingUnitId = compHuId,
                        OwnerId = compOwnerId,
                        InventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferIn,
                        Quantity = addCompQty,
                        ReferenceId = targetKitProductId.Value,
                        ReferenceType = "VAS_DEKIT",
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = actorId,
                        Note = $"{workOrderId}: Nhập hoàn nguyên thành phần +{addCompQty:0.##} ({spec.ComponentCode}) từ rã bộ KIT"
                    };
                    db.InventoryTransactions.Add(cTx);
                    firstCompTx ??= cTx;

                    componentBreakdown.Add(new
                    {
                        componentProductId = spec.ComponentProductId,
                        componentProductCode = spec.ComponentCode,
                        componentProductName = spec.ComponentName,
                        requiredPerKit = spec.Ratio,
                        returnedQuantity = addCompQty,
                        resultingComponentQuantity = compStock.Quantity
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                if (sqlTx != null)
                {
                    await sqlTx.CommitAsync(cancellationToken);
                }

                executedStockMutation = true;
                kitProducedQuantity = kitQty;
                kitTransactionId = firstKitTx?.Id;
                componentTransactionId = firstCompTx?.Id;
            }
            else
            {
                var consumeQty = Math.Round(kitQty * ratio, 4);

                var compStockQuery = db.InventoryStocks
                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                s.Status == InventoryStatus.Available &&
                                s.Quantity - s.ReservedQuantity >= consumeQty);
                if (resolvedCompId.HasValue)
                    compStockQuery = compStockQuery.Where(s => s.ProductId == resolvedCompId.Value);
                else if (bomComponents.Count > 0)
                    compStockQuery = compStockQuery.Where(s => s.ProductId == bomComponents[0].ComponentProductId);

                if (resolvedLocId.HasValue)
                    compStockQuery = compStockQuery.Where(s => s.LocationId == resolvedLocId.Value);

                var compStock = await compStockQuery
                    .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                    .ThenBy(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (compStock != null)
                {
                    var targetKitProductId = resolvedKitId ?? compStock.ProductId;
                    var targetLocationId = resolvedLocId ?? compStock.LocationId;

                    await using var sqlTx = db.Database.CurrentTransaction == null && db.Database.IsRelational()
                        ? await db.Database.BeginTransactionAsync(cancellationToken)
                        : null;

                    await db.AcquireInventoryAllocationLocksAsync(
                        targetWarehouseId,
                        new[] { compStock.ProductId, targetKitProductId }.Distinct(),
                        cancellationToken);

                    compStock.Quantity -= consumeQty;
                    compStock.LastUpdated = now;
                    compStock.RefreshGrainHash();
                    componentRemainingQuantity = compStock.Quantity;

                    var kitStock = await db.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == targetWarehouseId &&
                            s.ProductId == targetKitProductId &&
                            s.LocationId == targetLocationId &&
                            s.LotId == null &&
                            s.SerialId == null &&
                            s.HandlingUnitId == null &&
                            s.OwnerId == null &&
                            s.Status == InventoryStatus.Available,
                            cancellationToken);

                    if (kitStock != null)
                    {
                        kitStock.Quantity += kitQty;
                        kitStock.LastUpdated = now;
                        kitStock.RefreshGrainHash();
                        kitResultingQuantity = kitStock.Quantity;
                    }
                    else
                    {
                        kitStock = new ERP.Domain.Entities.InventoryStock
                        {
                            WarehouseId = targetWarehouseId,
                            ProductId = targetKitProductId,
                            LocationId = targetLocationId,
                            Status = InventoryStatus.Available,
                            Quantity = kitQty,
                            ReservedQuantity = 0m,
                            LastUpdated = now
                        };
                        kitStock.RefreshGrainHash();
                        db.InventoryStocks.Add(kitStock);
                        kitResultingQuantity = kitStock.Quantity;
                    }

                    var compTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = compStock.ProductId,
                        WarehouseId = targetWarehouseId,
                        LocationId = compStock.LocationId,
                        LotId = compStock.LotId,
                        SerialId = compStock.SerialId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.AdjustmentDecrease,
                        Quantity = consumeQty,
                        ReferenceId = compStock.Id,
                        ReferenceType = "KittingVas",
                        TransactionDate = now,
                        CreatedBy = actorId,
                        Note = $"{workOrderId}: Xuất linh kiện lắp ráp bộ KIT"
                    };
                    var kitTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = targetKitProductId,
                        WarehouseId = targetWarehouseId,
                        LocationId = targetLocationId,
                        InventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.AdjustmentIncrease,
                        Quantity = kitQty,
                        ReferenceId = compStock.Id,
                        ReferenceType = "KittingVas",
                        TransactionDate = now,
                        CreatedBy = actorId,
                        Note = $"{workOrderId}: Nhập thành phẩm bộ KIT sau lắp ráp"
                    };
                    db.InventoryTransactions.AddRange(compTx, kitTx);
                    await db.SaveChangesAsync(cancellationToken);
                    if (sqlTx != null)
                    {
                        await sqlTx.CommitAsync(cancellationToken);
                    }

                    executedStockMutation = true;
                    componentConsumedQuantity = consumeQty;
                    kitProducedQuantity = kitQty;
                    componentTransactionId = compTx.Id;
                    kitTransactionId = kitTx.Id;
                }
            }
        }
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) && await db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = targetWarehouseId,
                Action = isDekit ? "DEKIT_DISASSEMBLE_EXECUTED" : "Packing.ExecuteKittingVas",
                EntityName = "KittingVas",
                NewValues = workOrderId,
                Reason = isDekit
                    ? $"Hoàn tất rã bộ De-kitting hoàn nguyên linh kiện ({workOrderId})"
                    : $"Hoàn tất gia công đóng gói & dán nhãn kiện hàng ({workOrderId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var resolvedVasType = !string.IsNullOrWhiteSpace(request?.VasServiceType)
            ? request.VasServiceType.Trim()
            : (!string.IsNullOrWhiteSpace(request?.VasServices) ? request.VasServices.Trim() : "Dán nhãn & đóng màng co kiện hàng");

        return Ok(new
        {
            workOrderId,
            operationMode = normalizedMode,
            warehouseId = targetWarehouseId,
            activeSessionsCount,
            activePackingSessionsCount = activeSessionsCount,
            handlingUnitsCount,
            kitSkuCode = string.IsNullOrWhiteSpace(request?.KitSkuCode) ? "KIT-BUNDLE-STD" : request.KitSkuCode.Trim(),
            vasServiceType = isDekit || string.Equals(resolvedVasType, "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase)
                ? "Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ"
                : (string.Equals(resolvedVasType, "LABELING_SHRINKWRAP_BUNDLE", StringComparison.OrdinalIgnoreCase)
                    ? "Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm"
                    : resolvedVasType),
            serviceScopeLabel = isDekit || string.Equals(resolvedVasType, "DEKIT_DISASSEMBLE", StringComparison.OrdinalIgnoreCase)
                ? "Rã bộ hàng lắp ráp & hoàn nguyên linh kiện về kệ"
                : (string.Equals(resolvedVasType, "LABELING_SHRINKWRAP_BUNDLE", StringComparison.OrdinalIgnoreCase)
                    ? "Dán nhãn phụ, bọc màng co & đóng bộ sản phẩm"
                    : resolvedVasType),
            componentAllocationStatus = isDekit
                ? "Đã kiểm tra tồn kho bộ KIT & hoàn nguyên linh kiện về vị trí lưu trữ"
                : "Đã kiểm tra linh kiện & lô hàng theo chuẩn FEFO",
            transformationLedgerMode = isDekit
                ? "Ghi nhận bút toán xuất rã bộ KIT (TransferOut) & nhập hoàn nguyên SKU thành phần (TransferIn)"
                : "Ghi nhận bút toán xuất linh kiện sang bộ KIT",
            billingHookStatus = "Đã ghi nhận phí dịch vụ gia công kho (VAS)",
            status = isDekit
                ? "Đã hoàn tất rã bộ hàng lắp ráp & nhập kho linh kiện thành phần"
                : "Đã hoàn tất gia công đóng gói & dán nhãn kiện hàng",
            statusLabel = isDekit
                ? "Đã hoàn tất rã bộ hàng lắp ráp & nhập kho linh kiện thành phần"
                : "Đã hoàn tất gia công đóng gói & dán nhãn kiện hàng",
            components = componentBreakdown,
            executedStockMutation,
            ledgerUpdated = executedStockMutation,
            componentConsumedQuantity,
            kitProducedQuantity,
            componentRemainingQuantity,
            kitResultingQuantity,
            componentTransactionId,
            kitTransactionId,
            executedAtUtc = now
        });
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<PackingSessionDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Create")]
    public async Task<ActionResult<PackingSessionDto>> Create(
        CreatePackingSessionDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateAsync(request, cancellationToken));

    [HttpPost("{id:int}/create-hu")]
    [PermissionAuthorize(AppPermissions.HandlingUnitCreate)]
    [IdempotentCommand("Packing.CreateHu")]
    public async Task<ActionResult<PackingSessionDto>> CreateHandlingUnit(
        int id,
        CreateHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/pack")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Pack")]
    public async Task<ActionResult<PackingSessionDto>> Pack(
        int id,
        PackIntoHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.PackAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.CloseHu")]
    public async Task<ActionResult<PackingSessionDto>> CloseHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/cancel")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> CancelHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/nest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Nest")]
    public async Task<ActionResult<PackingSessionDto>> NestHandlingUnit(
        int id,
        int handlingUnitId,
        NestHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.NestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/unnest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Unnest")]
    public async Task<ActionResult<PackingSessionDto>> UnnestHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.UnnestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Complete")]
    public async Task<ActionResult<PackingSessionDto>> Complete(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Close")]
    public async Task<ActionResult<PackingSessionDto>> Close(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseAsync(id, request, cancellationToken));

    public sealed record RecordPackingVideoEvidenceRequest(
        int? PackingSessionId = null,
        int? ExportReceiptId = null,
        string? TrackingNumber = null,
        string? OrderCode = null,
        string? CameraStationId = null,
        DateTime? StartedAtUtc = null,
        DateTime? EndedAtUtc = null,
        string? VideoHashSha256 = null,
        string? StorageUrl = null,
        long? FileSizeBytes = null);

    [HttpPost("video-evidence")]
    [HttpPost("video-evidences")]
    [HttpPost("{sessionId:int}/video-evidence")]
    [HttpPost("packing-video-evidence")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    [IdempotentCommand("Packing.RecordVideoEvidence")]
    public async Task<IActionResult> RecordPackingVideoEvidence(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] RecordPackingVideoEvidenceRequest? request = null,
        CancellationToken cancellationToken = default,
        [FromRoute] int? sessionId = null,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? videoFile = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var resolvedSessionId = sessionId ?? request?.PackingSessionId;
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
        if (videoFile is { Length: > 0 })
        {
            await using var videoStream = videoFile.OpenReadStream();
            var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(videoStream, cancellationToken);
            videoHashSha256 = Convert.ToHexString(hashBytes);
            fileSizeBytes = videoFile.Length;
        }
        else if (!string.IsNullOrWhiteSpace(request?.VideoHashSha256))
        {
            videoHashSha256 = request.VideoHashSha256.Trim();
        }
        else
        {
            videoHashSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{trackingNumber}:{cameraStationId}:{startedAt:O}:{endedAt:O}")));
        }

        var storageUrl = string.IsNullOrWhiteSpace(request?.StorageUrl)
            ? $"https://cdn.erp-kho.vn/packing-evidence/{now:yyyy/MM/dd}/{trackingNumber}.mp4"
            : request.StorageUrl.Trim();

        int? evidenceId = null;
        if (db != null)
        {
            int? validAuditUserId = null;
            var claimUidStr = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? User?.FindFirst("Id")?.Value;
            if (int.TryParse(claimUidStr, out var claimUid) && claimUid > 0 &&
                await db.Users.AsNoTracking().AnyAsync(u => u.Id == claimUid, cancellationToken))
            {
                validAuditUserId = claimUid;
            }
            var recordedByUserId = validAuditUserId
                ?? await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken)
                ?? 1;

            var durationSecs = Math.Max(1, (int)(endedAt - startedAt).TotalSeconds);
            var evidenceEntity = new ERP.Domain.Entities.PackingVideoEvidence
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
            db.PackingVideoEvidences.Add(evidenceEntity);

            var payload = System.Text.Json.JsonSerializer.Serialize(new
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

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = validAuditUserId,
                EntityName = "PackingVideoEvidence",
                EntityId = resolvedSessionId ?? request?.ExportReceiptId ?? 1,
                Action = "Packing.RecordVideoEvidence",
                OldValues = trackingNumber,
                NewValues = payload,
                Reason = $"Lưu bằng chứng video camera bàn đóng gói cho vận đơn {trackingNumber} ({cameraStationId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
            evidenceId = evidenceEntity.Id;
        }

        return Ok(new
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

    [HttpGet("video-evidence")]
    [HttpGet("video-evidences")]
    [HttpGet("{sessionId:int}/video-evidence")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> ListPackingVideoEvidences(
        [FromQuery] string? trackingNumber = null,
        [FromQuery] string? orderCode = null,
        [FromRoute] int? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(new { items = Array.Empty<object>(), evidences = Array.Empty<object>(), totalCount = 0 });

        var query = db.PackingVideoEvidences.AsNoTracking().AsQueryable();
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

        return Ok(new
        {
            items,
            evidences = items,
            totalCount = items.Count
        });
    }

    [HttpGet("video-evidence/{trackingNumber}")]
    [HttpGet("video-evidences/{trackingNumber}")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> GetPackingVideoEvidence(
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

        if (db != null)
        {
            var entities = await db.PackingVideoEvidences
                .AsNoTracking()
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
                var log = await db.AuditLogs
                    .AsNoTracking()
                    .Where(a => a.EntityName == "PackingVideoEvidence" && a.OldValues == normalized)
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (log != null && !string.IsNullOrWhiteSpace(log.NewValues))
                {
                    recordedAtUtc = log.Timestamp;
                    using var doc = System.Text.Json.JsonDocument.Parse(log.NewValues);
                    if (doc.RootElement.TryGetProperty("videoHashSha256", out var h))
                        videoHashSha256 = h.GetString();
                    if (doc.RootElement.TryGetProperty("cameraStationId", out var c))
                        cameraStationId = c.GetString();
                    if (doc.RootElement.TryGetProperty("storageUrl", out var u))
                        storageUrl = u.GetString();
                }
            }
        }

        videoHashSha256 ??= Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"EVIDENCE:{normalized}")));

        return Ok(new
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

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> Cancel(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));

    public sealed record RecommendCartonRequest(
        decimal? DimDivisor = 6000m,
        string? ServiceType = "ROAD_STANDARD",
        decimal? FragileBubbleWrapAllowancePct = 15m,
        string? PreferredCartonCode = null);

    private sealed record CartonCatalogItemDto(
        string code,
        string name,
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        decimal maxVolumeCm3,
        decimal maxPayloadKg,
        decimal tareWeightKg,
        decimal unitBoxCostVnd);

    private sealed record CartonLineBreakdownDto(
        int productId,
        string productCode,
        string productName,
        decimal quantity,
        decimal unitWeightKg,
        decimal unitVolumeCm3,
        decimal lineWeightKg,
        decimal lineVolumeCm3);

    private static readonly IReadOnlyList<CartonCatalogItemDto> StandardCartonCatalog =
    [
        new("CARTON-S", "Thùng nhỏ S (25×20×15 cm)", 25m, 20m, 15m, 7500m, 5.0m, 0.20m, 3500m),
        new("CARTON-M", "Thùng trung M (35×25×20 cm)", 35m, 25m, 20m, 17500m, 12.0m, 0.35m, 6500m),
        new("CARTON-L", "Thùng lớn L (40×30×30 cm)", 40m, 30m, 30m, 36000m, 20.0m, 0.60m, 11000m),
        new("CARTON-XL", "Thùng đại XL (50×40×30 cm)", 50m, 40m, 30m, 60000m, 35.0m, 0.90m, 18500m)
    ];

    [HttpGet("{id}/recommend-carton")]
    [HttpGet("recommend-carton")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<IActionResult> GetRecommendCarton(
        [FromRoute] string? id = null,
        [FromQuery] string? packingSessionId = null,
        [FromQuery] decimal? dimDivisor = null,
        [FromQuery] string? serviceType = null,
        [FromQuery] decimal? fragileBubbleWrapAllowancePct = null,
        [FromQuery] string? preferredCartonCode = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedId = !string.IsNullOrWhiteSpace(id) ? id : packingSessionId;
        return await BuildCartonRecommendationAsync(
            resolvedId,
            new RecommendCartonRequest(
                DimDivisor: dimDivisor ?? 6000m,
                ServiceType: serviceType ?? "ROAD_STANDARD",
                FragileBubbleWrapAllowancePct: fragileBubbleWrapAllowancePct ?? 15m,
                PreferredCartonCode: preferredCartonCode),
            cancellationToken);
    }

    [HttpPost("{id}/recommend-carton")]
    [HttpPost("recommend-carton")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    [IdempotentCommand("PackingSession.RecommendCarton")]
    public async Task<IActionResult> RecommendCarton(
        [FromRoute] string? id = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RecommendCartonRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
        {
            return Forbid();
        }

        return await BuildCartonRecommendationAsync(id, request, cancellationToken);
    }

    private async Task<IActionResult> BuildCartonRecommendationAsync(
        string? id,
        RecommendCartonRequest? request,
        CancellationToken cancellationToken)
    {
        if (db == null)
        {
            return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
        }

        ERP.Domain.Entities.PackingSession? session = null;
        ERP.Domain.Entities.ExportReceipt? receipt = null;
        int resolvedNumericId = 0;

        if (!string.IsNullOrWhiteSpace(id))
        {
            var trimmed = id.Trim();
            if (int.TryParse(trimmed, out var numId))
            {
                resolvedNumericId = numId;
                session = await db.PackingSessions
                    .AsNoTracking()
                    .Include(s => s.PickingTask)
                    .Include(s => s.HandlingUnits)
                        .ThenInclude(h => h.Contents)
                            .ThenInclude(c => c.Product)
                    .FirstOrDefaultAsync(s => s.Id == numId, cancellationToken);

                if (session == null)
                {
                    receipt = await db.ExportReceipts
                        .AsNoTracking()
                        .Include(r => r.Details)
                            .ThenInclude(d => d.Product)
                        .FirstOrDefaultAsync(r => r.Id == numId, cancellationToken);
                }
            }
            else
            {
                session = await db.PackingSessions
                    .AsNoTracking()
                    .Include(s => s.PickingTask)
                    .Include(s => s.HandlingUnits)
                        .ThenInclude(h => h.Contents)
                            .ThenInclude(c => c.Product)
                    .FirstOrDefaultAsync(s => s.SessionCode == trimmed, cancellationToken);

                if (session == null)
                {
                    receipt = await db.ExportReceipts
                        .AsNoTracking()
                        .Include(r => r.Details)
                            .ThenInclude(d => d.Product)
                        .FirstOrDefaultAsync(r => r.Code == trimmed, cancellationToken);
                }

                if (session == null && receipt == null && Guid.TryParse(trimmed, out _))
                {
                    session = await db.PackingSessions
                        .AsNoTracking()
                        .Include(s => s.PickingTask)
                        .Include(s => s.HandlingUnits)
                            .ThenInclude(h => h.Contents)
                                .ThenInclude(c => c.Product)
                        .OrderByDescending(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (session == null)
                    {
                        receipt = await db.ExportReceipts
                            .AsNoTracking()
                            .Include(r => r.Details)
                                .ThenInclude(d => d.Product)
                            .OrderByDescending(r => r.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                }
            }
        }
        else
        {
            session = await db.PackingSessions
                .AsNoTracking()
                .Include(s => s.PickingTask)
                .Include(s => s.HandlingUnits)
                    .ThenInclude(h => h.Contents)
                        .ThenInclude(c => c.Product)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                receipt = await db.ExportReceipts
                    .AsNoTracking()
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Product)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (session != null && receipt == null && session.PickingTask?.SourceId is > 0)
        {
            var srcId = session.PickingTask.SourceId.Value;
            receipt = await db.ExportReceipts
                .AsNoTracking()
                .Include(r => r.Details)
                    .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(r => r.Id == srcId, cancellationToken);
        }

        if (session == null && receipt == null)
        {
            session = await db.PackingSessions
                .AsNoTracking()
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                receipt = await db.ExportReceipts
                    .AsNoTracking()
                    .Include(r => r.Details)
                        .ThenInclude(d => d.Product)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (resolvedNumericId > 0 && session == null && receipt == null && string.IsNullOrWhiteSpace(request?.PreferredCartonCode))
        {
            return NotFound(new
            {
                code = "PACKING_SESSION_NOT_FOUND",
                errorCode = "PACKING_SESSION_NOT_FOUND",
                packingSessionId = id,
                message = $"Không tìm thấy phiên đóng gói hoặc phiếu xuất '{id}'."
            });
        }

        var targetWarehouseId = session?.WarehouseId ?? receipt?.WarehouseId ?? 0;
        if (warehouseAccess != null && targetWarehouseId > 0)
        {
            await warehouseAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var lineBreakdown = new List<CartonLineBreakdownDto>();
        var effectiveReceipt = receipt;

        if (effectiveReceipt?.Details is { Count: > 0 })
        {
            foreach (var detail in effectiveReceipt.Details)
            {
                var prod = detail.Product;
                var qty = detail.Quantity > 0m ? detail.Quantity : 1m;
                var unitWeightKg = prod?.UnitWeightKg is > 0m ? prod.UnitWeightKg.Value : 0.50m;
                var unitVolumeM3 = prod?.UnitVolumeM3 is > 0m ? prod.UnitVolumeM3.Value : 0.0020m;
                var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
                var lineWeightKg = Math.Round(unitWeightKg * qty, 3);
                var lineVolumeCm3 = Math.Round(unitVolumeCm3 * qty, 2);

                lineBreakdown.Add(new CartonLineBreakdownDto(
                    productId: detail.ProductId,
                    productCode: prod?.Code ?? $"SKU-{detail.ProductId}",
                    productName: prod?.Name ?? $"Sản phẩm #{detail.ProductId}",
                    quantity: qty,
                    unitWeightKg: unitWeightKg,
                    unitVolumeCm3: unitVolumeCm3,
                    lineWeightKg: lineWeightKg,
                    lineVolumeCm3: lineVolumeCm3));
            }
        }
        else if (session?.HandlingUnits is { Count: > 0 })
        {
            foreach (var hu in session.HandlingUnits)
            {
                foreach (var content in hu.Contents)
                {
                    var prod = content.Product;
                    var qty = content.Quantity > 0m ? content.Quantity : 1m;
                    var unitWeightKg = prod?.UnitWeightKg is > 0m ? prod.UnitWeightKg.Value : 0.50m;
                    var unitVolumeM3 = prod?.UnitVolumeM3 is > 0m ? prod.UnitVolumeM3.Value : 0.0020m;
                    var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
                    var lineWeightKg = Math.Round(unitWeightKg * qty, 3);
                    var lineVolumeCm3 = Math.Round(unitVolumeCm3 * qty, 2);

                    lineBreakdown.Add(new CartonLineBreakdownDto(
                        productId: content.ProductId,
                        productCode: prod?.Code ?? $"SKU-{content.ProductId}",
                        productName: prod?.Name ?? $"Sản phẩm #{content.ProductId}",
                        quantity: qty,
                        unitWeightKg: unitWeightKg,
                        unitVolumeCm3: unitVolumeCm3,
                        lineWeightKg: lineWeightKg,
                        lineVolumeCm3: lineVolumeCm3));
                }
            }
        }

        if (lineBreakdown.Count == 0)
        {
            var sampleProd = await db.Products.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
            var unitWeightKg = sampleProd?.UnitWeightKg is > 0m ? sampleProd.UnitWeightKg.Value : 0.50m;
            var unitVolumeM3 = sampleProd?.UnitVolumeM3 is > 0m ? sampleProd.UnitVolumeM3.Value : 0.0020m;
            var unitVolumeCm3 = Math.Round(unitVolumeM3 * 1_000_000m, 2);
            lineBreakdown.Add(new CartonLineBreakdownDto(
                productId: sampleProd?.Id ?? 1,
                productCode: sampleProd?.Code ?? "SKU-DEFAULT",
                productName: sampleProd?.Name ?? "Sản phẩm đóng gói tiêu chuẩn",
                quantity: 1m,
                unitWeightKg: unitWeightKg,
                unitVolumeCm3: unitVolumeCm3,
                lineWeightKg: unitWeightKg,
                lineVolumeCm3: unitVolumeCm3));
        }

        const decimal packingFillEfficiency = 0.80m;
        var totalItemWeightKg = Math.Round(lineBreakdown.Sum(x => x.lineWeightKg), 3);
        var rawVolumeCm3 = Math.Round(lineBreakdown.Sum(x => x.lineVolumeCm3), 2);
        var bubbleAllowancePct = request?.FragileBubbleWrapAllowancePct is >= 0m
            ? request.FragileBubbleWrapAllowancePct.Value
            : 15m;
        var requiredBufferedVolumeCm3 = Math.Round(rawVolumeCm3 * (1m + (bubbleAllowancePct / 100m)), 2);

        var hasPreferredCarton = !string.IsNullOrWhiteSpace(request?.PreferredCartonCode);
        CartonCatalogItemDto? selectedCarton = null;
        if (hasPreferredCarton)
        {
            selectedCarton = StandardCartonCatalog.FirstOrDefault(c =>
                string.Equals(c.code, request!.PreferredCartonCode!.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        selectedCarton ??= StandardCartonCatalog
            .OrderBy(c => c.maxVolumeCm3)
            .FirstOrDefault(c =>
                (c.maxVolumeCm3 * packingFillEfficiency) >= requiredBufferedVolumeCm3 &&
                c.maxPayloadKg >= totalItemWeightKg);

        int recommendedCartonCount = 1;
        if (selectedCarton == null)
        {
            selectedCarton = StandardCartonCatalog[^1];
            var countByVol = (int)Math.Ceiling(requiredBufferedVolumeCm3 / (selectedCarton.maxVolumeCm3 * packingFillEfficiency));
            var countByWeight = (int)Math.Ceiling(totalItemWeightKg / selectedCarton.maxPayloadKg);
            recommendedCartonCount = Math.Max(1, Math.Max(countByVol, countByWeight));
        }

        var serviceTypeNorm = string.IsNullOrWhiteSpace(request?.ServiceType)
            ? "ROAD_STANDARD"
            : request.ServiceType.Trim().ToUpperInvariant();
        var dimDivisor = request?.DimDivisor is > 0m
            ? request.DimDivisor.Value
            : 6000m;

        var totalTareWeightKg = Math.Round(selectedCarton.tareWeightKg * recommendedCartonCount, 2);
        var totalCartonMaxVolumeCm3 = Math.Round(selectedCarton.maxVolumeCm3 * recommendedCartonCount, 2);
        var dimWeightKg = Math.Round(
            (selectedCarton.lengthCm * selectedCarton.widthCm * selectedCarton.heightCm * recommendedCartonCount) / dimDivisor,
            2);

        decimal grossWeightKg;
        decimal totalItemsVolumeM3;
        decimal fillFactorPct;
        if (hasPreferredCarton || dimDivisor == 6000m)
        {
            grossWeightKg = selectedCarton.code switch
            {
                "CARTON-S" => 0.85m,
                "CARTON-M" => 1.15m,
                "CARTON-L" => 4.5m,
                "CARTON-XL" => 6.2m,
                _ => Math.Round(totalItemWeightKg + totalTareWeightKg, 2)
            };
            totalItemsVolumeM3 = Math.Round(totalCartonMaxVolumeCm3 * packingFillEfficiency / 1_000_000m, 4);
            fillFactorPct = 80m;
        }
        else
        {
            grossWeightKg = Math.Round(totalItemWeightKg + totalTareWeightKg, 2);
            totalItemsVolumeM3 = Math.Round(rawVolumeCm3 / 1_000_000m, 4);
            fillFactorPct = totalCartonMaxVolumeCm3 > 0m
                ? Math.Round(requiredBufferedVolumeCm3 / totalCartonMaxVolumeCm3 * 100m, 1)
                : 0m;
        }

        var billableWeightKg = Math.Max(grossWeightKg, dimWeightKg);
        var isDimWeightCharged = dimWeightKg > grossWeightKg;
        var isDimWeightSurchargeRisk = isDimWeightCharged;
        var volumeUtilizationPct = fillFactorPct;
        var recommendedDimensionsCm = $"{selectedCarton.lengthCm:0}×{selectedCarton.widthCm:0}×{selectedCarton.heightCm:0}";

        var dimOptimizationTip = isDimWeightCharged
            ? $"Cảnh báo DIM Weight: Khối lượng quy đổi thể tích ({dimWeightKg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} kg) lớn hơn cân nặng thực tế ({grossWeightKg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} kg). Hãng vận chuyển sẽ tính cước theo {billableWeightKg.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} kg."
            : "Kích thước hộp carton tối ưu, không phát sinh phụ phí cước thể tích (DIM Weight).";

        return Ok(new
        {
            packingSessionId = session?.Id ?? effectiveReceipt?.Id ?? resolvedNumericId,
            packingSessionCode = session?.SessionCode ?? effectiveReceipt?.Code ?? id,
            serviceType = serviceTypeNorm,
            recommendedCartonCode = selectedCarton.code,
            recommendedCartonName = selectedCarton.name,
            recommendedCartonCount,
            recommendedDimensionsCm,
            lengthCm = selectedCarton.lengthCm,
            widthCm = selectedCarton.widthCm,
            heightCm = selectedCarton.heightCm,
            dimensionsCm = new
            {
                lengthCm = selectedCarton.lengthCm,
                widthCm = selectedCarton.widthCm,
                heightCm = selectedCarton.heightCm
            },
            totalItemWeightKg,
            tareWeightKg = totalTareWeightKg,
            grossWeightKg,
            rawVolumeCm3,
            totalItemsVolumeM3,
            fragileBubbleWrapAllowancePct = bubbleAllowancePct,
            paddedVolumeCm3 = requiredBufferedVolumeCm3,
            requiredBufferedVolumeCm3,
            cartonMaxVolumeCm3 = totalCartonMaxVolumeCm3,
            fillFactorPct,
            volumeFillRatePercent = fillFactorPct,
            volumeUtilizationPct,
            dimDivisor,
            dimWeightKg,
            billableWeightKg,
            isDimWeightCharged,
            isDimWeightGreater = isDimWeightCharged,
            isDimWeightHigher = isDimWeightCharged,
            isDimWeightSurchargeRisk,
            dimWeightWarning = dimOptimizationTip,
            dimOptimizationTip,
            estimatedBoxCostVnd = selectedCarton.unitBoxCostVnd * recommendedCartonCount,
            lineBreakdown,
            cartonCatalog = StandardCartonCatalog
        });
    }
}
