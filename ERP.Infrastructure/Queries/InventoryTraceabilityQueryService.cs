using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public sealed partial class InventoryTraceabilityQueryService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization) : IInventoryTraceabilityQueryService
{
    public async Task<IReadOnlyList<InventoryReversalWarehouseDto>> GetAccessibleWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        return await context.Warehouses.AsNoTracking()
            .Where(x => accessible.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Select(x => new InventoryReversalWarehouseDto
            {
                Id = x.Id,
                Code = x.Code.Trim(),
                Name = x.Name.Trim()
            }).ToListAsync(cancellationToken);
    }

    public async Task<InventoryTraceabilityResultDto> TraceAsync(
        int? warehouseId = null,
        int? productId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? referenceType = null,
        int? referenceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default,
        int bucketOffset = 0,
        int eventOffset = 0,
        int? eventAnchorId = null)
    {
        // A malformed positive-identifier filter must never be treated as an
        // empty search or silently widened to all accessible warehouses.
        if (warehouseId is <= 0)
            throw new BusinessRuleException("ID kho truy vết phải là số nguyên dương.");
        if (productId is <= 0)
            throw new BusinessRuleException("ID sản phẩm truy vết phải là số nguyên dương.");
        if (referenceId is <= 0)
            throw new BusinessRuleException("ID tham chiếu truy vết phải là số nguyên dương.");
        // The stock window advances by 500 rows. Reject negative, unaligned
        // and excessive offsets before any warehouse query is executed.
        if (bucketOffset < 0 || bucketOffset > 50_000 || bucketOffset % 500 != 0)
            throw new BusinessRuleException("Trang nhóm tồn không hợp lệ; vui lòng tải lại trang đầu hoặc dùng bước 500 nhóm.");

        var lot = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        var serial = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        var refType = string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim();
        var hasReference = refType is not null || referenceId.HasValue;
        if (hasReference && (refType is null || !referenceId.HasValue))
            throw new BusinessRuleException("Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.");
        // A single explicit warehouse is a bounded, authorized inventory view.
        // Never allow a completely empty query to fan out across all assigned
        // warehouses: a product/identity/reference or warehouse is required.
        if (!warehouseId.HasValue && !productId.HasValue && lot is null && serial is null && !hasReference)
            throw new BusinessRuleException("Cần ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu để truy vết.");
        limit = limit is < 1 or > 500 ? 200 : limit;
        // Ledger history is paged independently of the current-stock buckets.
        // Limit changes reset the page; reject misaligned/oversized offsets
        // before the first warehouse authorization or SQL query.
        if (eventOffset < 0 || eventOffset > 50_000 || eventOffset % limit != 0)
            throw new BusinessRuleException("Trang sự kiện sổ cái không hợp lệ; hãy quay về trang đầu hoặc tải lại theo bước của giới hạn sự kiện.");

        if (eventAnchorId is < 0)
            throw new BusinessRuleException("Giá trị mốc sự kiện sổ cái không hợp lệ.");
        if (eventOffset > 0 && !eventAnchorId.HasValue)
            throw new BusinessRuleException("Thiếu mốc lịch sử khi chuyển trang sự kiện; hãy truy vết lại từ trang đầu.");

        var allowedWarehouseIds = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowedWarehouseIds = [warehouseId.Value];
        }

        // Snapshot the whole authorized warehouse ledger, not only direct
        // reference matches. An existing reversal/corrective leg can use
        // another ReferenceType and a higher transaction ID than its original.
        var scopedLedger = context.InventoryTransactions.AsNoTracking()
            .Where(x => allowedWarehouseIds.Contains(x.WarehouseId));
        if (warehouseId.HasValue) scopedLedger = scopedLedger.Where(x => x.WarehouseId == warehouseId.Value);
        var eventQuery = scopedLedger;
        if (productId.HasValue) eventQuery = eventQuery.Where(x => x.ProductId == productId.Value);
        if (lot is not null) eventQuery = eventQuery.Where(x => x.Lot != null && x.Lot.LotNumber == lot);
        if (serial is not null) eventQuery = eventQuery.Where(x => x.Serial != null && x.Serial.SerialNumber == serial);
        if (hasReference)
            eventQuery = eventQuery.Where(x => x.ReferenceType == refType && x.ReferenceId == referenceId);

        // Stock-reference matching intentionally uses the full document history:
        // anchoring the timeline must not hide live current-stock identities.
        var referenceStockEvents = eventQuery;
        // Identity IDs grow monotonically. New commits cannot shift older pages,
        // even when a later transaction carries a backdated TransactionDate.
        var anchorId = eventAnchorId ?? await scopedLedger.MaxAsync(x => (int?)x.Id, cancellationToken) ?? 0;
        eventQuery = eventQuery.Where(x => x.Id <= anchorId);

        // Read one extra event so the operator can distinguish a complete
        // history from a capped window without running a full COUNT query.
        var eventWindow = await ProjectEvents(eventQuery)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.TransactionId)
            .Skip(eventOffset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var eventsTruncated = eventWindow.Count > limit;
        var events = eventWindow.Take(limit).ToList();

        events = await ExpandAndLinkReversalChainAsync(
            events, allowedWarehouseIds, anchorId, limit, cancellationToken);

        var bucketsResult = await LoadCurrentStockBucketsAsync(
            allowedWarehouseIds, warehouseId, productId, lot, serial,
            hasReference, referenceStockEvents, bucketOffset, cancellationToken);

        var exposures = await LoadDocumentAndReceiptExposuresAsync(
            scopedLedger, anchorId, productId, lot, serial, hasReference, cancellationToken);

        var shipmentEvidence = await LoadShipmentTraceabilityEvidenceAsync(
            scopedLedger, allowedWarehouseIds, anchorId, productId, lot, serial, hasReference, cancellationToken);

        return new InventoryTraceabilityResultDto
        {
            CurrentBuckets = bucketsResult.Buckets,
            Events = events,
            RelatedDocuments = exposures.RelatedDocuments,
            RelatedDocumentsTruncated = exposures.RelatedDocumentsTruncated,
            ReceiptExposures = exposures.ReceiptExposures,
            ReceiptExposuresTruncated = exposures.ReceiptExposuresTruncated,
            ShipmentExposures = shipmentEvidence.ShipmentExposures,
            ShipmentExposuresTruncated = shipmentEvidence.ShipmentExposuresTruncated,
            ShipmentPickingEvidence = shipmentEvidence.ShipmentPickingEvidence,
            ShipmentPickingEvidenceTruncated = shipmentEvidence.ShipmentPickingEvidenceTruncated,
            ShipmentHuEvidence = shipmentEvidence.ShipmentHuEvidence,
            ShipmentHuEvidenceTruncated = shipmentEvidence.ShipmentHuEvidenceTruncated,
            ShipmentExportSources = shipmentEvidence.ShipmentExportSources,
            ShipmentExportSourcesTruncated = shipmentEvidence.ShipmentExportSourcesTruncated,
            EventAnchorId = anchorId,
            EventsTruncated = eventsTruncated,
            BucketsTruncated = bucketsResult.BucketsTruncated
        };
    }

    private static IQueryable<InventoryTraceabilityEventDto> ProjectEvents(IQueryable<InventoryTransaction> query) =>
        query.Select(x => new InventoryTraceabilityEventDto
        {
            TransactionId = x.Id,
            ProductId = x.ProductId,
            ProductCode = x.Product.Code.Trim(),
            ProductName = x.Product.Name.Trim(),
            WarehouseId = x.WarehouseId,
            WarehouseName = x.Warehouse.Name.Trim(),
            TransactionType = x.TransactionType.ToString(),
            InventoryStatus = x.InventoryStatus.ToString(),
            FromInventoryStatus = x.FromInventoryStatus.HasValue ? x.FromInventoryStatus.Value.ToString() : null,
            ToInventoryStatus = x.ToInventoryStatus.HasValue ? x.ToInventoryStatus.Value.ToString() : null,
            LocationId = x.LocationId,
            LocationCode = x.Location == null ? null : x.Location.Code.Trim(),
            FromLocationId = x.FromLocationId,
            FromLocationCode = x.FromLocation == null ? null : x.FromLocation.Code.Trim(),
            ToLocationId = x.ToLocationId,
            ToLocationCode = x.ToLocation == null ? null : x.ToLocation.Code.Trim(),
            LotId = x.LotId,
            LotNumber = x.Lot == null ? null : x.Lot.LotNumber.Trim(),
            ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
            SerialId = x.SerialId,
            SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber.Trim(),
            Quantity = x.Quantity,
            ReferenceType = x.ReferenceType == null ? null : x.ReferenceType.Trim(),
            ReferenceId = x.ReferenceId,
            TransactionDate = x.TransactionDate,
            CreatedBy = x.CreatedBy,
            CreatedByName = (x.CreatedByUser.FullName ?? x.CreatedByUser.Username).Trim(),
            Note = x.Note == null ? null : x.Note.Trim(),
            ReasonCode = x.ReasonCode == null ? null : x.ReasonCode.Trim(),
            ReversalOfTransactionId = x.ReversalOfTransactionId ??
                (x.TransactionType == TransactionType.Reversal && x.ReferenceType == "InventoryReversal"
                    ? x.ReferenceId
                    : null),
            CorrectiveTransactionId = x.CorrectiveTransactionId
        });
}
