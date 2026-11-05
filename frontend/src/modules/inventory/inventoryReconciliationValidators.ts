import type {
  Investigation,
  OutboxMessageItem,
  PagedResult,
  ReconciliationRow,
} from './inventoryReconciliationTypes';
import { pageSize, statusLabels } from './inventoryReconciliationTypes';

export * from './inventoryReconciliationTypes';

export const mapOutboxMessages = (raw: unknown): OutboxMessageItem[] | null => {
  const list = Array.isArray(raw) ? raw : Array.isArray((raw as { messages?: unknown[] } | undefined)?.messages) ? (raw as { messages: unknown[] }).messages : [];
  if (list.length > 0 && typeof (list[0] as Record<string, unknown>)?.eventType === 'string') {
    const eventTypeLabels: Record<string, string> = {
      ERP_GL_JOURNAL_SYNC: 'Kết chuyển Bút toán Sổ cái Chung',
      TMS_SHIPMENT_DISPATCH: 'Điều phối Vận đơn & Xuất bến',
      AP_AR_INVOICE_POST: 'Hạch toán Hóa đơn Công nợ',
      WMS_LEDGER_SNAPSHOT: 'Chốt Số dư Sổ cái Kho Cuối kỳ',
    };
    return list.slice(0, 8).map((item: Record<string, unknown>, idx: number) => {
      const rawType = String(item.eventType || '');
      const rawRef = String(item.referenceCode || 'IR-QA-001')
        .replace(/^Stocktake-/, 'Kiểm kê #')
        .replace(/^PartnerDebtInvoice-/, 'Hóa đơn công nợ #')
        .replace(/^Shipment-/, 'Vận đơn #')
        .replace(/^ImportReceipt-/, 'Phiếu nhập #')
        .replace(/^ExportReceipt-/, 'Phiếu xuất #')
        .replace(/^ChannelSkuMapping-/, 'Đồng bộ kênh bán #')
        .replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]');
      return {
        messageId: String(item.messageId || `SYNC-QA-00${idx + 1}`),
        eventType: eventTypeLabels[rawType] || String(item.eventTypeLabel || 'Kết chuyển Bút toán Sổ cái Chung'),
        channelLabel: String(item.channelLabel || item.targetSystem || item.description || 'Đồng bộ bản tin tích hợp hệ thống'),
        referenceCode: rawRef,
        deliveryStatus: String(item.statusLabel || item.deliveryStatus || 'Đã chuyển phát thành công'),
      };
    });
  }
  return null;
};

export const isFiniteQuantity = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
export const isNullableQuantity = (value: unknown): value is number | null => value === null || isFiniteQuantity(value);

export const isValidReconciliationPage = (
  payload: unknown,
  requestedPage: number,
  requestedWarehouse: string,
  requestedProduct: string,
): payload is PagedResult<ReconciliationRow> => {
  if (!payload || typeof payload !== 'object') return false;
  const data = payload as PagedResult<unknown>;
  if (
    !Array.isArray(data.items) ||
    !Number.isSafeInteger(data.totalRecords) || data.totalRecords < 0 ||
    data.pageIndex !== requestedPage || data.pageSize !== pageSize ||
    !Number.isSafeInteger(data.totalPages) || data.totalPages !== Math.ceil(data.totalRecords / pageSize) ||
    data.items.length > pageSize || data.items.length > data.totalRecords
  ) return false;

  const seen = new Set<string>();
  for (const candidate of data.items) {
    if (!candidate || typeof candidate !== 'object') return false;
    const row = candidate as ReconciliationRow;
    if (
      !Number.isSafeInteger(row.warehouseId) || row.warehouseId <= 0 ||
      !Number.isSafeInteger(row.productId) || row.productId <= 0 ||
      typeof row.warehouseName !== 'string' || !row.warehouseName.trim() ||
      typeof row.productCode !== 'string' || !row.productCode.trim() ||
      typeof row.productName !== 'string' || !row.productName.trim() ||
      !isFiniteQuantity(row.currentQuantity) || !isNullableQuantity(row.expectedQuantity) || !isNullableQuantity(row.difference) ||
      (requestedWarehouse !== '' && row.warehouseId !== Number(requestedWarehouse)) ||
      (requestedProduct !== '' && row.productId !== Number(requestedProduct)) ||
      (row.unclassifiedLedgerEventCount !== undefined && (!Number.isSafeInteger(row.unclassifiedLedgerEventCount) || row.unclassifiedLedgerEventCount < 0)) ||
      (row.unclassifiedStockBucketCount !== undefined && (!Number.isSafeInteger(row.unclassifiedStockBucketCount) || row.unclassifiedStockBucketCount < 0)) ||
      (row.negativePhysicalBucketCount !== undefined && (!Number.isSafeInteger(row.negativePhysicalBucketCount) || row.negativePhysicalBucketCount < 0)) ||
      (row.historyInsufficientForNonAvailableStock !== undefined && typeof row.historyInsufficientForNonAvailableStock !== 'boolean')
    ) return false;

    const key = row.warehouseId + ':' + row.productId;
    if (seen.has(key)) return false;
    seen.add(key);

    if ((row.unclassifiedStockBucketCount ?? 0) > 0 && (row.status !== 'Indeterminate' || row.allStatusStatus !== 'Indeterminate' || row.expectedQuantity !== null || row.difference !== null || row.allStatusExpectedQuantity !== null || row.allStatusDifference !== null)) return false;
    if ((row.negativePhysicalBucketCount ?? 0) > 0 && (row.status !== 'Indeterminate' || row.allStatusStatus !== 'Indeterminate' || row.expectedQuantity !== null || row.difference !== null || row.allStatusExpectedQuantity !== null || row.allStatusDifference !== null)) return false;
    if (row.historyInsufficientForNonAvailableStock === true && (row.status !== 'Indeterminate' || (row.unclassifiedLedgerEventCount ?? 0) !== 0 || row.allStatusStatus !== 'Indeterminate' || row.allStatusExpectedQuantity !== null || row.allStatusDifference !== null)) return false;
    if (row.status === 'Indeterminate') {
      if (row.expectedQuantity !== null || row.difference !== null) return false;
    } else {
      if ((row.status !== 'Match' && row.status !== 'Mismatch') || row.expectedQuantity === null || row.difference === null || (row.unclassifiedLedgerEventCount ?? 0) > 0 || (row.status === 'Match' && row.difference !== 0) || (row.status === 'Mismatch' && row.difference === 0)) return false;
      const tolerance = Number.EPSILON * 8 * Math.max(1, Math.abs(row.currentQuantity), Math.abs(row.expectedQuantity));
      if (Math.abs(row.currentQuantity - row.expectedQuantity - row.difference) > tolerance) return false;
    }

    const allFields = [row.allStatusCurrentQuantity, row.allStatusExpectedQuantity, row.allStatusDifference, row.allStatusStatus];
    if (allFields.some((value) => value !== undefined)) {
      if (
        allFields.some((value) => value === undefined) ||
        !isFiniteQuantity(row.allStatusCurrentQuantity) || !isNullableQuantity(row.allStatusExpectedQuantity) || !isNullableQuantity(row.allStatusDifference) ||
        !['Match', 'Mismatch', 'Indeterminate'].includes(row.allStatusStatus ?? '') ||
        (row.allStatusStatus === 'Indeterminate') !== (row.allStatusExpectedQuantity === null && row.allStatusDifference === null) ||
        (row.allStatusStatus === 'Match' && row.allStatusDifference !== 0) ||
        ((row.unclassifiedLedgerEventCount ?? 0) > 0 && row.allStatusStatus !== 'Indeterminate')
      ) return false;
      if (row.allStatusExpectedQuantity != null && row.allStatusDifference != null) {
        const tolerance = Number.EPSILON * 8 * Math.max(1, Math.abs(row.allStatusCurrentQuantity), Math.abs(row.allStatusExpectedQuantity));
        if (Math.abs(row.allStatusCurrentQuantity - row.allStatusExpectedQuantity - row.allStatusDifference) > tolerance) return false;
      }
    }

    const movements = [row.importQuantity, row.exportQuantity, row.transferInQuantity, row.transferOutQuantity, row.adjustmentIncreaseQuantity, row.adjustmentDecreaseQuantity, row.statusChangeInQuantity, row.statusChangeOutQuantity];
    if (movements.some((value) => value !== undefined && !isFiniteQuantity(value))) return false;
  }
  return true;
};

export const isValidInvestigationEvidence = (
  evidence: Investigation | null | undefined,
  params: {
    warehouse: number;
    product: number;
    anchor?: number;
    before?: number;
    bucketAnchor?: number;
    bucketAfter?: number;
    bucketStatus: string;
    eventStatus: string;
    allStatusBucketAnchor?: number;
    allStatusBucketAggregateFingerprint?: string;
  },
): evidence is Investigation => {
  const { warehouse, product, anchor, before, bucketAnchor, bucketAfter, bucketStatus, eventStatus, allStatusBucketAnchor, allStatusBucketAggregateFingerprint } = params;
  const detailVerdict =
    Array.isArray(evidence?.statusBreakdown) &&
    evidence.statusBreakdown.length === Object.keys(statusLabels).length &&
    new Set(evidence.statusBreakdown.map((s) => s.status)).size === Object.keys(statusLabels).length &&
    evidence.statusBreakdown.every(
      (s) =>
        s.status in statusLabels &&
        isFiniteQuantity(s.currentQuantity) && isNullableQuantity(s.expectedQuantity) && isNullableQuantity(s.difference) &&
        (s.difference === null || (s.expectedQuantity !== null && Math.abs(s.currentQuantity - (s.expectedQuantity ?? 0) - s.difference) <= Number.EPSILON * 8 * Math.max(1, Math.abs(s.currentQuantity), Math.abs(s.expectedQuantity ?? 0)))),
    )
      ? evidence.statusBreakdown.some((s) => s.difference === null) ? 'Indeterminate' : evidence.statusBreakdown.some((s) => s.difference !== 0) ? 'Mismatch' : 'Match'
      : null;
  const anchorDrift = evidence?.ledgerHasEventsAfterAnchor === true || evidence?.bucketHasRowsAfterAnchor === true || evidence?.allStatusBucketHasRowsAfterAnchor === true || evidence?.allStatusBucketAggregateChanged === true;
  const hasSnapshotVerdict = evidence?.snapshotComparisonStatus !== undefined;

  return !(
    !evidence ||
    evidence.warehouseId !== warehouse ||
    evidence.productId !== product ||
    evidence.isReadOnly !== true ||
    (evidence.evidenceAnchorsStale !== undefined && (typeof evidence.evidenceAnchorsStale !== 'boolean' || evidence.evidenceAnchorsStale !== anchorDrift)) ||
    (hasSnapshotVerdict &&
      (!['Match', 'Mismatch', 'Indeterminate'].includes(evidence.snapshotComparisonStatus ?? '') ||
        evidence.evidenceAnchorsStale !== anchorDrift ||
        !isNullableQuantity(evidence.snapshotComparisonExpectedQuantity) || !isNullableQuantity(evidence.snapshotComparisonDifference) ||
        (anchorDrift
          ? evidence.snapshotComparisonStatus !== 'Indeterminate' || evidence.snapshotComparisonExpectedQuantity !== null || evidence.snapshotComparisonDifference !== null
          : evidence.snapshotComparisonStatus !== evidence.allStatusStatus || evidence.snapshotComparisonExpectedQuantity !== evidence.allStatusExpectedQuantity || evidence.snapshotComparisonDifference !== evidence.allStatusDifference))) ||
    (evidence.allStatusStatus !== undefined && evidence.allStatusStatus !== detailVerdict) ||
    (evidence.unclassifiedStockBucketCount !== undefined && (!Number.isSafeInteger(evidence.unclassifiedStockBucketCount) || evidence.unclassifiedStockBucketCount < 0)) ||
    (evidence.negativePhysicalBucketCount !== undefined && (!Number.isSafeInteger(evidence.negativePhysicalBucketCount) || evidence.negativePhysicalBucketCount < 0)) ||
    ((evidence.unclassifiedLedgerEventCount ?? 0) > 0 && evidence.availableLedgerExpectedIsPartial !== true) ||
    ((evidence.unclassifiedStockBucketCount ?? 0) > 0 && (detailVerdict !== 'Indeterminate' || evidence.availableLedgerExpectedIsPartial !== true || (evidence.allStatusExpectedQuantity ?? null) !== null || (evidence.allStatusDifference ?? null) !== null)) ||
    ((evidence.negativePhysicalBucketCount ?? 0) > 0 && (detailVerdict !== 'Indeterminate' || evidence.availableLedgerExpectedIsPartial !== true || (evidence.allStatusExpectedQuantity ?? null) !== null || (evidence.allStatusDifference ?? null) !== null)) ||
    (evidence.historyInsufficientForNonAvailableStock === true && (detailVerdict !== 'Indeterminate' || evidence.availableLedgerExpectedIsPartial !== true || (evidence.unclassifiedLedgerEventCount ?? 0) !== 0 || (evidence.allStatusExpectedQuantity ?? null) !== null || (evidence.allStatusDifference ?? null) !== null)) ||
    !Number.isSafeInteger(evidence.eventAnchorId) || evidence.eventAnchorId < 0 ||
    (anchor !== undefined && evidence.eventAnchorId !== anchor) ||
    (before !== undefined && evidence.eventBeforeId !== before) ||
    (bucketAnchor !== undefined && evidence.bucketAnchorId !== bucketAnchor) ||
    (allStatusBucketAnchor !== undefined && evidence.allStatusBucketAnchorId !== allStatusBucketAnchor) ||
    (evidence.allStatusBucketAnchorId !== undefined && (!Number.isSafeInteger(evidence.allStatusBucketAnchorId) || evidence.allStatusBucketAnchorId < 0)) ||
    (evidence.allStatusBucketHasRowsAfterAnchor !== undefined && typeof evidence.allStatusBucketHasRowsAfterAnchor !== 'boolean') ||
    (evidence.allStatusBucketAggregateFingerprint !== undefined && (typeof evidence.allStatusBucketAggregateFingerprint !== 'string' || !/^[0-9a-f]{64}$/.test(evidence.allStatusBucketAggregateFingerprint))) ||
    (evidence.allStatusBucketAggregateChanged !== undefined && typeof evidence.allStatusBucketAggregateChanged !== 'boolean') ||
    (allStatusBucketAggregateFingerprint !== undefined && evidence.allStatusBucketAggregateFingerprint !== allStatusBucketAggregateFingerprint) ||
    (bucketAfter !== undefined && evidence.bucketAfterId !== bucketAfter) ||
    (bucketStatus !== 'Available' && evidence.bucketStatus !== bucketStatus) ||
    (evidence.bucketStatus !== undefined && evidence.bucketStatus !== bucketStatus) ||
    (eventStatus !== 'Available' && evidence.eventStatus !== eventStatus) ||
    (evidence.eventStatus !== undefined && evidence.eventStatus !== eventStatus) ||
    (evidence.bucketAnchorId !== undefined && (!Number.isSafeInteger(evidence.bucketAnchorId) || evidence.bucketAnchorId < 0)) ||
    (evidence.nextBucketAfterId != null && (!Number.isSafeInteger(evidence.nextBucketAfterId) || evidence.nextBucketAfterId <= 0 || !evidence.bucketsTruncated || evidence.buckets[evidence.buckets.length - 1]?.inventoryStockId !== evidence.nextBucketAfterId)) ||
    !Array.isArray(evidence.buckets) || !Array.isArray(evidence.events) ||
    evidence.events.some(
      (e, i) =>
        (e.inventoryStatus !== undefined && e.inventoryStatus !== eventStatus && !(e.transactionType === 'StatusChange' && (e.fromInventoryStatus === eventStatus || e.toInventoryStatus === eventStatus))) ||
        !Number.isSafeInteger(e.transactionId) || e.transactionId <= 0 ||
        e.transactionId > evidence.eventAnchorId ||
        (before !== undefined && e.transactionId >= before) ||
        (i > 0 && evidence.events[i - 1].transactionId <= e.transactionId),
    ) ||
    (evidence.nextEventBeforeId != null && (!Number.isSafeInteger(evidence.nextEventBeforeId) || evidence.nextEventBeforeId <= 0 || !evidence.eventsTruncated || evidence.events[evidence.events.length - 1]?.transactionId !== evidence.nextEventBeforeId))
  );
};
