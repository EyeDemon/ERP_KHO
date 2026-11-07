export interface WarehouseOption {
  id: number;
  name: string;
  code?: string;
  isActive?: boolean;
}

export interface ReconciliationRow {
  productId: number;
  productCode: string;
  productName: string;
  warehouseId: number;
  warehouseName: string;
  currentQuantity: number;
  expectedQuantity: number | null;
  difference: number | null;
  allStatusCurrentQuantity?: number;
  allStatusExpectedQuantity?: number | null;
  allStatusDifference?: number | null;
  allStatusStatus?: 'Match' | 'Mismatch' | 'Indeterminate';
  historyInsufficientForNonAvailableStock?: boolean;
  unclassifiedLedgerEventCount?: number;
  unclassifiedStockBucketCount?: number;
  invalidReservationBucketCount?: number;
  negativePhysicalBucketCount?: number;
  statusChangeInQuantity?: number;
  statusChangeOutQuantity?: number;
  importQuantity: number;
  exportQuantity: number;
  transferInQuantity: number;
  transferOutQuantity: number;
  adjustmentIncreaseQuantity: number;
  adjustmentDecreaseQuantity: number;
  status: string;
}

export interface StatusEvidence {
  status: string;
  currentQuantity: number;
  reservedQuantity: number;
  bucketCount: number;
  directLedgerNetQuantity: number;
  statusChangeInQuantity: number;
  statusChangeOutQuantity: number;
  expectedQuantity?: number | null;
  difference?: number | null;
}

export const statusLabels: Record<string, string> = {
  Available: 'Khả dụng',
  QcHold: 'Chờ kiểm định',
  Quarantine: 'Cách ly',
  Damaged: 'Hư hỏng',
  Rejected: 'Từ chối',
  Blocked: 'Bị khóa',
  Expired: 'Hết hạn',
  RecallBlocked: 'Khóa thu hồi',
};

export interface InvestigationBucket {
  inventoryStockId: number;
  locationId?: number | null;
  locationCode?: string | null;
  lotId?: number | null;
  lotNumber?: string | null;
  serialId?: number | null;
  serialNumber?: string | null;
  quantity: number;
  reservedQuantity: number;
}

export interface InvestigationEvent {
  transactionId: number;
  transactionType: string;
  locationId?: number | null;
  locationCode?: string | null;
  lotId?: number | null;
  lotNumber?: string | null;
  serialId?: number | null;
  serialNumber?: string | null;
  quantity: number;
  signedQuantity: number | null;
  inventoryStatus?: string;
  fromInventoryStatus?: string | null;
  toInventoryStatus?: string | null;
  referenceType?: string | null;
  referenceId?: number | null;
  transactionDate: string;
}

export interface Investigation {
  warehouseId: number;
  warehouseName: string;
  productId: number;
  productCode: string;
  productName: string;
  eventAnchorId: number;
  eventCount: number;
  bucketCount: number;
  bucketAnchorId?: number;
  bucketAfterId?: number | null;
  nextBucketAfterId?: number | null;
  bucketHasRowsAfterAnchor?: boolean;
  bucketStatus?: string;
  allStatusBucketAnchorId?: number;
  allStatusBucketHasRowsAfterAnchor?: boolean;
  allStatusBucketAggregateFingerprint?: string;
  allStatusBucketAggregateChanged?: boolean;
  eventStatus?: string;
  eventBeforeId?: number | null;
  nextEventBeforeId?: number | null;
  ledgerHasEventsAfterAnchor?: boolean;
  evidenceAnchorsStale?: boolean;
  snapshotComparisonStatus?: 'Match' | 'Mismatch' | 'Indeterminate';
  snapshotComparisonExpectedQuantity?: number | null;
  snapshotComparisonDifference?: number | null;
  eventsTruncated: boolean;
  bucketsTruncated: boolean;
  currentQuantity: number;
  expectedQuantity: number;
  difference: number;
  allStatusCurrentQuantity?: number;
  allStatusReservedQuantity?: number;
  allStatusExpectedQuantity?: number | null;
  allStatusDifference?: number | null;
  allStatusStatus?: 'Match' | 'Mismatch' | 'Indeterminate';
  unclassifiedLedgerEventCount?: number;
  unclassifiedStockBucketCount?: number;
  invalidReservationBucketCount?: number;
  negativePhysicalBucketCount?: number;
  statusBreakdown?: StatusEvidence[];
  availableLedgerExpectedIsPartial?: boolean;
  historyInsufficientForNonAvailableStock?: boolean;
  isReadOnly: boolean;
  buckets: InvestigationBucket[];
  events: InvestigationEvent[];
}

export interface PagedResult<T> {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
}

export interface OutboxMessageItem {
  messageId: string;
  eventType: string;
  channelLabel: string;
  referenceCode: string;
  deliveryStatus: string;
}

export interface SiteReadinessVerdict {
  readinessVerdict: string;
  checkedRows: number;
  mismatchCount: number;
  outboxSynced: boolean;
  verifiedAtUtc: string;
}

export const pageSize = 20;
export const numberFormat = new Intl.NumberFormat('vi-VN');
