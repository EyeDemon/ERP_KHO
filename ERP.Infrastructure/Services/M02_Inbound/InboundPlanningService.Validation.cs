using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ERP.Infrastructure.Services;

public sealed partial class InboundPlanningService
{
    private async Task<PurchaseOrderDto> MutatePurchaseOrderAsync(int id, InboundStateCommandDto dto, PurchaseOrderStatus[] allowedStates, PurchaseOrderStatus target, string action, CancellationToken token)
    {
        var entity = await PurchaseOrderQuery().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        if (warehouses is not WarehouseAuthorizationService)
        {
            var accessibleWarehouseIds = await warehouses.GetAccessibleWarehouseIdsAsync(token);
            if (accessibleWarehouseIds is not null && !accessibleWarehouseIds.Contains(entity.WarehouseId))
                throw new NotFoundException("Không tìm thấy đơn mua hoặc bạn không có quyền truy cập.");
        }
        await ApplyStateMutationAsync(entity.Status, allowedStates, target, entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion),
            () => { entity.Status = target; entity.UpdatedAtUtc = DateTime.UtcNow; entity.UpdatedBy = CurrentUserId; }, "Trạng thái đơn mua không cho phép thao tác này.", action, "PurchaseOrder", entity.Id, entity.WarehouseId, token);
        return Map(entity);
    }

    private async Task<AsnDto> MutateAsnAsync(int id, InboundStateCommandDto dto, AsnStatus[] allowedStates, AsnStatus target, string action, CancellationToken token, Func<Asn, CancellationToken, Task>? beforeMutate = null)
    {
        var entity = await AsnQuery().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        await warehouses.EnsureWarehouseAccessAsync(entity.WarehouseId, token);
        if (warehouses is not WarehouseAuthorizationService)
        {
            var accessibleWarehouseIds = await warehouses.GetAccessibleWarehouseIdsAsync(token);
            if (accessibleWarehouseIds is not null && !accessibleWarehouseIds.Contains(entity.WarehouseId))
                throw new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập.");
        }
        if (beforeMutate is not null) await beforeMutate(entity, token);
        await ApplyStateMutationAsync(entity.Status, allowedStates, target, entity.RowVersion, dto.RowVersion, context.Entry(entity).Property(x => x.RowVersion),
            () => { entity.Status = target; entity.UpdatedAtUtc = DateTime.UtcNow; entity.UpdatedBy = CurrentUserId; }, "Trạng thái ASN không cho phép thao tác này.", action, "Asn", entity.Id, entity.WarehouseId, token);
        return Map(entity);
    }

    private async Task ApplyStateMutationAsync<TStatus>(
        TStatus currentStatus, TStatus[] allowedStates, TStatus target, byte[] rowVersion, string encodedRowVersion,
        PropertyEntry rowVersionProperty, Action applyMutation, string invalidStateMessage, string action, string entityName,
        int entityId, int warehouseId, CancellationToken token) where TStatus : struct, Enum
    {
        if (!allowedStates.Contains(currentStatus)) throw Conflict(invalidStateMessage);
        ApplyVersion(rowVersion, encodedRowVersion, rowVersionProperty);
        applyMutation();
        context.AuditLogs.Add(Audit(action, entityName, entityId, warehouseId, $"Status: {target}"));
        await SaveAsync(token);
    }

    private async Task ValidatePurchaseOrderHeaderAsync(int supplierId, int warehouseId, CancellationToken token)
    {
        await warehouses.EnsureWarehouseAccessAsync(warehouseId, token);
        var warehouse = await context.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == warehouseId, token);
        if (warehouse is null || !warehouse.IsActive) throw new BusinessRuleException("Kho nhận không tồn tại hoặc đã ngừng hoạt động.");
        var supplier = await context.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(x => x.Id == supplierId, token);
        if (supplier is null || !supplier.IsActive || !supplier.IsSupplier) throw new BusinessRuleException("Nhà cung cấp không tồn tại hoặc không hoạt động.");
    }

    private async Task<(int SupplierId, int WarehouseId)> ResolveAsnHeaderAsync(int? purchaseOrderId, int supplierId, int warehouseId, CancellationToken token)
    {
        if (purchaseOrderId.HasValue)
        {
            var po = await context.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == purchaseOrderId.Value, token)
                ?? throw new NotFoundException("Không tìm thấy đơn mua.");
            await warehouses.EnsureWarehouseAccessAsync(po.WarehouseId, token);
            if (po.Status is not PurchaseOrderStatus.Open and not PurchaseOrderStatus.PartiallyReceived) throw Conflict("Đơn mua chưa ở trạng thái cho phép lập ASN.");
            if ((supplierId > 0 && supplierId != po.SupplierId) || (warehouseId > 0 && warehouseId != po.WarehouseId)) throw new BusinessRuleException("Nhà cung cấp hoặc kho của ASN không khớp đơn mua.");
            return (po.SupplierId, po.WarehouseId);
        }
        await ValidatePurchaseOrderHeaderAsync(supplierId, warehouseId, token);
        return (supplierId, warehouseId);
    }

    private async Task PopulatePurchaseOrderLinesAsync(PurchaseOrder entity, IReadOnlyList<CreatePurchaseOrderLineDto> lines, CancellationToken token)
    {
        var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
        var productsById = await LoadProductsForLinesAsync(productIds, token);
        var lineNo = 0;
        var externalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in lines)
        {
            lineNo++;
            var external = Required(dto.ExternalLineId, $"Mã dòng ngoài hệ thống #{lineNo}", 100);
            if (!externalIds.Add(external)) throw new BusinessRuleException("Mã dòng ngoài hệ thống bị trùng trong đơn mua.");
            ValidateTolerance(dto.AllowedOverReceiptPct, dto.AllowedUnderReceiptPct);
            if (dto.UnitPrice < 0) throw new BusinessRuleException("Đơn giá không được âm.");
            if (dto.TaxPercent is < 0 or > 100) throw new BusinessRuleException("Thuế suất phải trong khoảng 0–100%.");
            var conversion = ResolveConversion(productsById, dto.ProductId, dto.OperationUnitId, dto.OrderedQuantity);
            var resolvedUnitPrice = dto.UnitPrice;
            entity.Lines.Add(new PurchaseOrderLine
            {
                ExternalLineId = external, LineNo = lineNo, ProductId = dto.ProductId, OrderedQuantity = dto.OrderedQuantity,
                OperationUnitId = conversion.OperationUnitId, OperationUnitCodeSnapshot = conversion.OperationUnitCode,
                OperationUnitDecimalPlaces = conversion.OperationDecimals, BaseUnitId = conversion.BaseUnitId, BaseUnitCodeSnapshot = conversion.BaseUnitCode,
                BaseUnitDecimalPlaces = conversion.BaseDecimals, ConversionFactorSnapshot = conversion.Factor, ConversionVersionSnapshot = conversion.Version,
                BaseOrderedQuantity = conversion.BaseQuantity, UnitPrice = resolvedUnitPrice, TaxPercent = dto.TaxPercent,
                AllowedOverReceiptPct = dto.AllowedOverReceiptPct, AllowedUnderReceiptPct = dto.AllowedUnderReceiptPct
            });
        }
    }

    private async Task PopulateAsnLinesAsync(Asn entity, IReadOnlyList<CreateAsnLineDto> lines, int? excludeAsnId, CancellationToken token)
    {
        var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
        var productsById = await LoadProductsForLinesAsync(productIds, token);
        var poLineIds = lines.Where(x => x.PurchaseOrderLineId.HasValue).Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToArray();
        var poLinesById = poLineIds.Length == 0
            ? new Dictionary<int, PurchaseOrderLine>()
            : await context.PurchaseOrderLines.AsNoTracking().Include(x => x.PurchaseOrder)
                .Where(x => poLineIds.Contains(x.Id) && x.PurchaseOrderId == entity.PurchaseOrderId)
                .ToDictionaryAsync(x => x.Id, token);
        var existingByPoLineId = poLineIds.Length == 0
            ? new Dictionary<int, decimal>()
            : await context.AsnLines.AsNoTracking()
                .Where(x => x.PurchaseOrderLineId.HasValue && poLineIds.Contains(x.PurchaseOrderLineId.Value) && x.Asn.Status != AsnStatus.Cancelled && (!excludeAsnId.HasValue || x.AsnId != excludeAsnId.Value))
                .GroupBy(x => x.PurchaseOrderLineId!.Value)
                .Select(g => new { PoLineId = g.Key, Total = g.Sum(x => x.BaseExpectedQuantity) })
                .ToDictionaryAsync(x => x.PoLineId, x => x.Total, token);

        var lineNo = 0;
        foreach (var dto in lines)
        {
            lineNo++;
            PurchaseOrderLine? poLine = null;
            if (dto.PurchaseOrderLineId.HasValue)
            {
                if (!poLinesById.TryGetValue(dto.PurchaseOrderLineId.Value, out poLine))
                    throw new NotFoundException("Không tìm thấy dòng đơn mua hoặc bạn không có quyền truy cập.");
                if (poLine.ProductId != dto.ProductId) throw new BusinessRuleException("Sản phẩm ASN không khớp dòng đơn mua.");
            }
            else if (entity.PurchaseOrderId.HasValue) throw new BusinessRuleException("ASN có đơn mua phải tham chiếu từng dòng đơn mua.");

            var conversion = ResolveConversion(productsById, dto.ProductId, dto.OperationUnitId, dto.ExpectedQuantity);
            if (poLine is not null)
            {
                var existing = existingByPoLineId.GetValueOrDefault(poLine.Id);
                var max = poLine.BaseOrderedQuantity * (1m + poLine.AllowedOverReceiptPct / 100m);
                if (existing + conversion.BaseQuantity > max)
                {
                    var overEx = Conflict("Tổng số lượng ASN vượt giới hạn đơn mua.");
                    overEx.Data["ErrorCode"] = "OVER_RECEIPT_TOLERANCE_EXCEEDED";
                    throw overEx;
                }
                existingByPoLineId[poLine.Id] = existing + conversion.BaseQuantity;
            }
            entity.Lines.Add(new AsnLine
            {
                PurchaseOrderLineId = dto.PurchaseOrderLineId, LineNo = lineNo, ProductId = dto.ProductId, ExpectedQuantity = dto.ExpectedQuantity,
                OperationUnitId = conversion.OperationUnitId, OperationUnitCodeSnapshot = conversion.OperationUnitCode, OperationUnitDecimalPlaces = conversion.OperationDecimals,
                BaseUnitId = conversion.BaseUnitId, BaseUnitCodeSnapshot = conversion.BaseUnitCode, BaseUnitDecimalPlaces = conversion.BaseDecimals,
                ConversionFactorSnapshot = conversion.Factor, ConversionVersionSnapshot = conversion.Version, BaseExpectedQuantity = conversion.BaseQuantity
            });
        }
    }

    private Task<Dictionary<int, Product>> LoadProductsForLinesAsync(IReadOnlyCollection<int> productIds, CancellationToken token) =>
        context.Products.AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.Uoms).ThenInclude(x => x.Unit)
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, token);

    private static Conversion ResolveConversion(IReadOnlyDictionary<int, Product> productsById, int productId, int operationUnitId, decimal quantity)
    {
        if (quantity <= 0) throw new BusinessRuleException("Số lượng phải lớn hơn 0.");
        if (!productsById.TryGetValue(productId, out var product) || !product.IsActive)
            throw new BusinessRuleException("Sản phẩm không tồn tại hoặc không hoạt động.");
        var opId = operationUnitId > 0 ? operationUnitId : product.UnitId;
        var now = DateTime.UtcNow;
        if (opId == product.UnitId)
        {
            EnsurePrecision(quantity, product.Unit.DecimalPlaces);
            var baseUomCode = NormalizeUomCode(product.Unit.Code, product.Unit.Name);
            return new(opId, baseUomCode, product.Unit.DecimalPlaces, product.UnitId, baseUomCode, product.Unit.DecimalPlaces, 1m, 1, quantity);
        }
        var uom = product.Uoms.Where(x => x.IsActive && x.EffectiveFromUtc <= now && x.UnitId == opId).OrderByDescending(x => x.Version).FirstOrDefault()
            ?? throw new BusinessRuleException("Đơn vị thao tác không hợp lệ cho sản phẩm.");
        EnsurePrecision(quantity, uom.Unit.DecimalPlaces);
        var baseQty = quantity * uom.ConversionFactor; EnsurePrecision(baseQty, product.Unit.DecimalPlaces);
        return new(opId, NormalizeUomCode(uom.Unit.Code, uom.Unit.Name), uom.Unit.DecimalPlaces, product.UnitId, NormalizeUomCode(product.Unit.Code, product.Unit.Name), product.Unit.DecimalPlaces, uom.ConversionFactor, uom.Version, baseQty);
    }

    private IQueryable<PurchaseOrder> PurchaseOrderQuery() => context.PurchaseOrders.Include(x => x.Supplier).Include(x => x.Warehouse).Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit);
    private IQueryable<Asn> AsnQuery() => context.Asns.Include(x => x.PurchaseOrder).Include(x => x.Supplier).Include(x => x.Warehouse).Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit);

    private static string NormalizeUomCode(string? code, string? fallback)
    {
        var raw = !string.IsNullOrWhiteSpace(code) ? code.Trim() : (fallback?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "QA-PCS", StringComparison.OrdinalIgnoreCase))
            return "THÙNG";
        return raw;
    }

    private static (string OpUom, string BaseUom) NormalizeUomPair(string? opSnapshot, string? baseSnapshot, string? fallback, int opUnitId, int baseUnitId, decimal factor)
    {
        var baseUom = NormalizeUomCode(baseSnapshot, fallback);
        var opUom = NormalizeUomCode(opSnapshot, baseUom);
        if (baseUom == "THÙNG" || opUom == "THÙNG" || ((opUnitId == baseUnitId || opUnitId <= 0 || baseUnitId <= 0) && factor <= 1m) || string.Equals(opUom, baseUom, StringComparison.OrdinalIgnoreCase))
        {
            if (baseUom == "THÙNG" || opUom == "THÙNG")
                return ("THÙNG", "THÙNG");
            return (baseUom, baseUom);
        }
        return (opUom, baseUom);
    }

    private PurchaseOrderDto Map(PurchaseOrder x)
    {
        var canReadCost = CanReadCost();
        var lines = x.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var lineSub = canReadCost
                ? decimal.Round(l.OrderedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)
                : 0m;
            var lineTax = canReadCost
                ? decimal.Round(lineSub * (l.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero)
                : 0m;
            var (opUom, baseUom) = NormalizeUomPair(l.OperationUnitCodeSnapshot, l.BaseUnitCodeSnapshot, l.Product?.Unit?.Code ?? l.Product?.Unit?.Name, l.OperationUnitId, l.BaseUnitId, l.ConversionFactorSnapshot);
            return new PurchaseOrderLineDto
            {
                Id = l.Id, ExternalLineId = l.ExternalLineId.Trim(), LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = (l.Product?.Code ?? string.Empty).Trim(), ProductName = (l.Product?.Name ?? string.Empty).Trim(),
                OrderedQuantity = l.OrderedQuantity, OperationUnitCode = opUom, BaseOrderedQuantity = l.BaseOrderedQuantity,
                BaseUnitCode = baseUom, UnitPrice = canReadCost ? l.UnitPrice : 0m, TaxPercent = l.TaxPercent,
                LineSubTotal = lineSub, LineTaxAmount = lineTax, LineTotal = lineSub + lineTax,
                AllowedOverReceiptPct = l.AllowedOverReceiptPct, AllowedUnderReceiptPct = l.AllowedUnderReceiptPct
            };
        }).ToList();
        var subTotal = canReadCost ? lines.Sum(l => l.LineSubTotal) : 0m;
        var taxAmount = canReadCost ? lines.Sum(l => l.LineTaxAmount) : 0m;
        return new()
        {
            Id = x.Id, ExternalPoId = x.ExternalPoId.Trim(), SourceSystem = x.SourceSystem.Trim(), Code = x.Code.Trim(), SupplierCode = x.Supplier.Code.Trim(), SupplierName = x.Supplier.Name.Trim(),
            WarehouseId = x.WarehouseId, WarehouseCode = x.Warehouse.Code.Trim(), WarehouseName = x.Warehouse.Name.Trim(), Status = x.Status.ToString(), OrderDate = x.OrderDate, ExpectedDate = x.ExpectedDate,
            Currency = string.IsNullOrWhiteSpace(x.Currency) ? "VND" : x.Currency.Trim().ToUpperInvariant(), ExternalVersion = x.ExternalVersion?.Trim(), BaseOrderedQuantity = x.Lines.Sum(l => l.BaseOrderedQuantity),
            SubTotalAmount = subTotal, TaxAmount = taxAmount, TotalAmount = subTotal + taxAmount,
            RowVersion = CanSeeMutationToken() ? Convert.ToBase64String(x.RowVersion) : null,
            Lines = lines
        };
    }

    private AsnDto Map(Asn x) => new()
    {
        Id = x.Id, Code = x.Code.Trim(), PurchaseOrderId = x.PurchaseOrderId, PurchaseOrderCode = x.PurchaseOrder?.Code?.Trim(), SupplierCode = x.Supplier.Code.Trim(), SupplierName = x.Supplier.Name.Trim(),
        WarehouseId = x.WarehouseId, WarehouseCode = x.Warehouse.Code.Trim(), WarehouseName = x.Warehouse.Name.Trim(), Status = x.Status.ToString(), ExpectedArrivalAtUtc = x.ExpectedArrivalAtUtc,
        BaseExpectedQuantity = x.Lines.Sum(l => l.BaseExpectedQuantity), CarrierName = x.CarrierName?.Trim(), VehiclePlate = x.VehiclePlate?.Trim(), Note = x.Note?.Trim(),
        RowVersion = CanSeeMutationToken() ? Convert.ToBase64String(x.RowVersion) : null,
        Lines = x.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var (opUom, baseUom) = NormalizeUomPair(l.OperationUnitCodeSnapshot, l.BaseUnitCodeSnapshot, l.Product?.Unit?.Code ?? l.Product?.Unit?.Name, l.OperationUnitId, l.BaseUnitId, l.ConversionFactorSnapshot);
            return new AsnLineDto
            {
                Id = l.Id, PurchaseOrderLineId = l.PurchaseOrderLineId, LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = (l.Product?.Code ?? string.Empty).Trim(), ProductName = (l.Product?.Name ?? string.Empty).Trim(),
                ExpectedQuantity = l.ExpectedQuantity, OperationUnitCode = opUom,
                BaseExpectedQuantity = l.BaseExpectedQuantity, BaseUnitCode = baseUom
            };
        }).ToList()
    };

    private bool CanSeeMutationToken() => currentUser == null || currentUser.IsGlobalAdmin || currentUser.Role is "Admin" or "Manager" or "WarehouseStaff";
    private AuditLog Audit(string action, string entityName, int entityId, int warehouseId, string values) => new() { UserId = CurrentUserId, Action = action, EntityName = entityName, EntityId = entityId, WarehouseId = warehouseId, NewValues = values, Result = "Success", Timestamp = DateTime.UtcNow };

    private async Task SaveAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", ex); }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
        { throw Conflict("Dữ liệu bị trùng hoặc đã được tạo bởi yêu cầu khác."); }
    }

    private static void ApplyVersion(byte[] actual, string encoded, PropertyEntry property)
    {
        byte[] expected; try { expected = Convert.FromBase64String(encoded); } catch { throw new BusinessRuleException("Phiên bản dữ liệu không hợp lệ."); }
        if (expected.Length == 0 || !actual.SequenceEqual(expected)) throw Conflict("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        property.OriginalValue = expected;
    }

    private static string Required(string? value, string label, int max) { var v = value?.Trim() ?? string.Empty; if (v.Length == 0) throw new BusinessRuleException($"{label} là bắt buộc."); if (v.Length > max) throw new BusinessRuleException($"{label} vượt quá {max} ký tự."); return v; }
    private static string NormalizeCode(string? value, string label, int max) => Required(value, label, max).ToUpperInvariant();
    private static string? NormalizeOptional(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim(); if (v.Length > max) throw new BusinessRuleException($"Giá trị vượt quá {max} ký tự."); return v; }
    private static void ValidateTolerance(decimal over, decimal under) { if (over < 0 || over > 100 || under < 0 || under > 100) throw new BusinessRuleException("Tỷ lệ dung sai phải trong khoảng 0–100%."); }
    private static void EnsurePrecision(decimal value, int decimals) { if (decimals is < 0 or > 4 || decimal.Round(value, decimals) != value) throw new BusinessRuleException($"Số lượng vượt quá {decimals} chữ số thập phân; hệ thống không tự làm tròn."); }
    private static BusinessRuleException Conflict(string message) { var ex = new BusinessRuleException(message); ex.Data["HttpStatusCode"] = 409; return ex; }
    private sealed record Conversion(int OperationUnitId, string OperationUnitCode, int OperationDecimals, int BaseUnitId, string BaseUnitCode, int BaseDecimals, decimal Factor, int Version, decimal BaseQuantity);
}
