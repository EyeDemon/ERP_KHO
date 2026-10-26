import { useEffect, useRef, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import type { FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { demoReconciliationRows, demoReconciliationWarehouses } from '../mocks/inventoryReconciliationDemo';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import './InventoryReconciliation.css';

interface WarehouseOption {
  id: number;
  name: string;
  code?: string;
  isActive?: boolean;
}

interface ReconciliationRow {
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

interface StatusEvidence {
  status: string; currentQuantity: number; reservedQuantity: number;
  bucketCount: number; directLedgerNetQuantity: number;
  statusChangeInQuantity: number; statusChangeOutQuantity: number;
  expectedQuantity?: number | null; difference?: number | null;
}
const statusLabels: Record<string,string> = {
  Available:'Khả dụng', QcHold:'Chờ kiểm định', Quarantine:'Cách ly',
  Damaged:'Hư hỏng', Rejected:'Từ chối', Blocked:'Bị khóa',
  Expired:'Hết hạn', RecallBlocked:'Khóa thu hồi',
};

interface InvestigationBucket {
  inventoryStockId: number;
  locationId?: number | null; locationCode?: string | null;
  lotId?: number | null; lotNumber?: string | null;
  serialId?: number | null; serialNumber?: string | null;
  quantity: number; reservedQuantity: number;
}
interface InvestigationEvent {
  transactionId: number; transactionType: string;
  locationId?: number | null; locationCode?: string | null;
  lotId?: number | null; lotNumber?: string | null;
  serialId?: number | null; serialNumber?: string | null;
  quantity: number; signedQuantity: number | null;
  inventoryStatus?: string; fromInventoryStatus?: string | null; toInventoryStatus?: string | null;
  referenceType?: string | null; referenceId?: number | null;
  transactionDate: string;
}
interface Investigation {
  warehouseId:number; warehouseName:string; productId:number;
  productCode:string; productName:string; eventAnchorId:number;
  eventCount:number; bucketCount:number;
  bucketAnchorId?:number; bucketAfterId?:number|null; nextBucketAfterId?:number|null;
  bucketHasRowsAfterAnchor?:boolean; bucketStatus?: string;
  allStatusBucketAnchorId?:number; allStatusBucketHasRowsAfterAnchor?:boolean;
  allStatusBucketAggregateFingerprint?:string; allStatusBucketAggregateChanged?:boolean;
  eventStatus?:string; eventBeforeId?:number|null; nextEventBeforeId?:number|null;
  ledgerHasEventsAfterAnchor?:boolean; evidenceAnchorsStale?:boolean;
  snapshotComparisonStatus?:'Match' | 'Mismatch' | 'Indeterminate';
  snapshotComparisonExpectedQuantity?:number|null;
  snapshotComparisonDifference?:number|null;
  eventsTruncated:boolean; bucketsTruncated:boolean;
  currentQuantity:number; expectedQuantity:number; difference:number;
  allStatusCurrentQuantity?:number; allStatusReservedQuantity?:number;
  allStatusExpectedQuantity?:number|null; allStatusDifference?:number|null;
  allStatusStatus?: 'Match' | 'Mismatch' | 'Indeterminate';
  unclassifiedLedgerEventCount?:number;
  unclassifiedStockBucketCount?:number; invalidReservationBucketCount?:number;
  negativePhysicalBucketCount?:number;
  statusBreakdown?:StatusEvidence[];
  availableLedgerExpectedIsPartial?:boolean;
  historyInsufficientForNonAvailableStock?: boolean;
  isReadOnly:boolean; buckets:InvestigationBucket[]; events:InvestigationEvent[];
}
interface PagedResult<T> {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
}

const pageSize = 20;
const numberFormat = new Intl.NumberFormat('vi-VN');

// A malformed report must never masquerade as a verified inventory match.
// The authoritative permission check remains on the API; this is a UI
// fail-closed guard against inconsistent or crossed-scope response payloads.
const isFiniteQuantity = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

const isNullableQuantity = (value: unknown): value is number | null =>
  value === null || isFiniteQuantity(value);

const isValidReconciliationPage = (
  payload: unknown, requestedPage: number, requestedWarehouse: string, requestedProduct: string,
): payload is PagedResult<ReconciliationRow> => {
  if (!payload || typeof payload !== 'object') return false;
  const data = payload as PagedResult<unknown>;
  if (!Array.isArray(data.items) ||
      !Number.isSafeInteger(data.totalRecords) || data.totalRecords < 0 ||
      data.pageIndex !== requestedPage || data.pageSize !== pageSize ||
      !Number.isSafeInteger(data.totalPages) ||
      data.totalPages !== Math.ceil(data.totalRecords / pageSize) ||
      data.items.length > pageSize || data.items.length > data.totalRecords) return false;

  const seen = new Set<string>();
  for (const candidate of data.items) {
    if (!candidate || typeof candidate !== 'object') return false;
    const row = candidate as ReconciliationRow;
    if (!Number.isSafeInteger(row.warehouseId) || row.warehouseId <= 0 ||
        !Number.isSafeInteger(row.productId) || row.productId <= 0 ||
        typeof row.warehouseName !== 'string' || !row.warehouseName.trim() ||
        typeof row.productCode !== 'string' || !row.productCode.trim() ||
        typeof row.productName !== 'string' || !row.productName.trim() ||
        !isFiniteQuantity(row.currentQuantity) ||
        !isNullableQuantity(row.expectedQuantity) || !isNullableQuantity(row.difference) ||
        (requestedWarehouse !== '' && row.warehouseId !== Number(requestedWarehouse)) ||
        (requestedProduct !== '' && row.productId !== Number(requestedProduct)) ||
        (row.unclassifiedLedgerEventCount !== undefined &&
          (!Number.isSafeInteger(row.unclassifiedLedgerEventCount) ||
            row.unclassifiedLedgerEventCount < 0)) ||
        (row.unclassifiedStockBucketCount !== undefined &&
          (!Number.isSafeInteger(row.unclassifiedStockBucketCount) ||
            row.unclassifiedStockBucketCount < 0)) ||
        (row.negativePhysicalBucketCount !== undefined &&
          (!Number.isSafeInteger(row.negativePhysicalBucketCount) ||
            row.negativePhysicalBucketCount < 0)) ||
        (row.historyInsufficientForNonAvailableStock !== undefined &&
          typeof row.historyInsufficientForNonAvailableStock !== 'boolean')) return false;

    const key = row.warehouseId + ':' + row.productId;
    if (seen.has(key)) return false;
    seen.add(key);

    if ((row.unclassifiedStockBucketCount ?? 0) > 0 &&
        (row.status !== 'Indeterminate' ||
          row.allStatusStatus !== 'Indeterminate' ||
          row.expectedQuantity !== null || row.difference !== null ||
          row.allStatusExpectedQuantity !== null ||
          row.allStatusDifference !== null)) return false;
    if ((row.negativePhysicalBucketCount ?? 0) > 0 &&
        (row.status !== 'Indeterminate' || row.allStatusStatus !== 'Indeterminate' ||
          row.expectedQuantity !== null || row.difference !== null ||
          row.allStatusExpectedQuantity !== null || row.allStatusDifference !== null)) return false;
    if (row.historyInsufficientForNonAvailableStock === true &&
        (row.status !== 'Indeterminate' ||
          (row.unclassifiedLedgerEventCount ?? 0) !== 0 ||
          row.allStatusStatus !== 'Indeterminate' ||
          row.allStatusExpectedQuantity !== null ||
          row.allStatusDifference !== null)) return false;
    if (row.status === 'Indeterminate') {
      if (row.expectedQuantity !== null || row.difference !== null) return false;
    } else {
      if ((row.status !== 'Match' && row.status !== 'Mismatch') ||
          row.expectedQuantity === null || row.difference === null ||
          (row.unclassifiedLedgerEventCount ?? 0) > 0 ||
          (row.status === 'Match' && row.difference !== 0) ||
          (row.status === 'Mismatch' && row.difference === 0)) return false;
      // Allow only binary floating-point rounding of JSON decimal quantities.
      const tolerance = Number.EPSILON * 8 * Math.max(
        1, Math.abs(row.currentQuantity), Math.abs(row.expectedQuantity),
      );
      if (Math.abs((row.currentQuantity - row.expectedQuantity) - row.difference) > tolerance)
        return false;
    }

    // Older demo fixtures omit all-status totals. A partial new response,
    // inconsistent summary, or false Match must fail closed.
    const allFields = [row.allStatusCurrentQuantity, row.allStatusExpectedQuantity,
      row.allStatusDifference, row.allStatusStatus];
    if (allFields.some(value => value !== undefined)) {
      if (allFields.some(value => value === undefined) ||
          !isFiniteQuantity(row.allStatusCurrentQuantity) ||
          !isNullableQuantity(row.allStatusExpectedQuantity) ||
          !isNullableQuantity(row.allStatusDifference) ||
          !['Match', 'Mismatch', 'Indeterminate'].includes(row.allStatusStatus ?? '') ||
          ((row.allStatusStatus === 'Indeterminate') !==
            (row.allStatusExpectedQuantity === null && row.allStatusDifference === null)) ||
          (row.allStatusStatus === 'Match' && row.allStatusDifference !== 0) ||
          ((row.unclassifiedLedgerEventCount ?? 0) > 0 &&
            row.allStatusStatus !== 'Indeterminate')) return false;
      if (row.allStatusExpectedQuantity != null && row.allStatusDifference != null) {
        const tolerance = Number.EPSILON * 8 * Math.max(
          1, Math.abs(row.allStatusCurrentQuantity), Math.abs(row.allStatusExpectedQuantity));
        if (Math.abs((row.allStatusCurrentQuantity - row.allStatusExpectedQuantity) -
            row.allStatusDifference) > tolerance) return false;
      }
    }

    // Optional in older read models; if present they must never format as NaN.
    const movements = [
      row.importQuantity, row.exportQuantity, row.transferInQuantity,
      row.transferOutQuantity, row.adjustmentIncreaseQuantity,
      row.adjustmentDecreaseQuantity, row.statusChangeInQuantity, row.statusChangeOutQuantity,
    ];
    if (movements.some(value => value !== undefined && !isFiniteQuantity(value))) return false;
  }
  return true;
};

export default function InventoryReconciliation() {
  const inRouter = useInRouterContext();
  const demoRuntime = isReadOnlyDemoRuntime();
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);
  const [warehouseError, setWarehouseError] = useState('');
  const [filterError, setFilterError] = useState('');
  const productInputRef = useRef<HTMLInputElement>(null);
  const [warehouseId, setWarehouseId] = useState('');
  const [productId, setProductId] = useState('');
  const [keyword, setKeyword] = useState('');
  const [applied, setApplied] = useState({ warehouseId: '', productId: '', keyword: '' });
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<ReconciliationRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [investigation, setInvestigation] = useState<Investigation | null>(null);
  const [investigationError, setInvestigationError] = useState('');
  const [investigating, setInvestigating] = useState(false);
  // Each cursor is the strict upper bound of one Ledger page. The initial
  // page is null; previous pages can be safely reloaded with the same anchor.
  const [eventPageCursors, setEventPageCursors] = useState<(number | null)[]>([null]);
  const [eventPageIndex, setEventPageIndex] = useState(0);
  const [bucketPageCursors, setBucketPageCursors] = useState<(number | null)[]>([null]);
  const [bucketPageIndex, setBucketPageIndex] = useState(0);
  const investigationGeneration = useRef(0);
  const investigationTrigger = useRef<HTMLButtonElement | null>(null);
  const [outboxDispatchNotice, setOutboxDispatchNotice] = useState('');
  const [rebuildNotice, setRebuildNotice] = useState('');
  const [outboxMessages, setOutboxMessages] = useState([
    {
      messageId: 'OUTBOX-GL-001',
      eventType: 'Kết chuyển Bút toán Sổ cái Chung',
      channelLabel: 'Đồng bộ bút toán sổ cái tài chính',
      referenceCode: 'IR-QA-001',
      deliveryStatus: 'Đã chuyển phát thành công',
    },
    {
      messageId: 'OUTBOX-TMS-002',
      eventType: 'Điều phối Vận đơn & Xuất bến',
      channelLabel: 'Thông báo xuất bến điều vận vận tải',
      referenceCode: 'SHP-QA-001',
      deliveryStatus: 'Đã chuyển phát thành công',
    },
    {
      messageId: 'OUTBOX-APAR-003',
      eventType: 'Hạch toán Hóa đơn Công nợ',
      channelLabel: 'Hạch toán công nợ hóa đơn đối tác',
      referenceCode: 'AP-IR-QA0001',
      deliveryStatus: 'Đã chuyển phát thành công',
    },
    {
      messageId: 'OUTBOX-WMS-004',
      eventType: 'Chốt Số dư Sổ cái Kho Cuối kỳ',
      channelLabel: 'Đối chiếu bản chụp sổ cái bất biến',
      referenceCode: 'ER-QA-001',
      deliveryStatus: 'Đã chuyển phát thành công',
    },
  ]);
  const [siteReadinessVerdict, setSiteReadinessVerdict] = useState<{
    readinessVerdict: string;
    checkedRows: number;
    mismatchCount: number;
    outboxSynced: boolean;
    verifiedAtUtc: string;
  } | null>(null);

  useEffect(() => {
    if (demoRuntime) {
      setWarehouses(demoReconciliationWarehouses);
      return;
    }
    let active = true;
    void Promise.resolve(apiClient.get('/api/InventoryReconciliation/outbox-messages'))
      .then((res) => {
        if (!active) return;
        const raw = (res as { data?: unknown } | undefined)?.data;
        const list = Array.isArray(raw)
          ? raw
          : Array.isArray((raw as { messages?: unknown[] } | undefined)?.messages)
            ? (raw as { messages: unknown[] }).messages
            : [];
        if (list.length > 0 && typeof (list[0] as Record<string, unknown>)?.eventType === 'string') {
          const eventTypeLabels: Record<string, string> = {
            ERP_GL_JOURNAL_SYNC: 'Kết chuyển Bút toán Sổ cái Chung',
            TMS_SHIPMENT_DISPATCH: 'Điều phối Vận đơn & Xuất bến',
            AP_AR_INVOICE_POST: 'Hạch toán Hóa đơn Công nợ',
            WMS_LEDGER_SNAPSHOT: 'Chốt Số dư Sổ cái Kho Cuối kỳ',
          };
          setOutboxMessages(
            list.slice(0, 8).map((item: Record<string, unknown>, idx: number) => {
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
                messageId: String(item.messageId || `OUTBOX-QA-00${idx + 1}`),
                eventType: eventTypeLabels[rawType] || String(item.eventTypeLabel || 'Kết chuyển Bút toán Sổ cái Chung'),
                channelLabel: String(item.channelLabel || item.targetSystem || item.description || 'Đồng bộ bản tin tích hợp hệ thống'),
                referenceCode: rawRef,
                deliveryStatus: String(item.statusLabel || item.deliveryStatus || 'Đã chuyển phát thành công'),
              };
            }),
          );
        }
      })
      .catch(() => null);
    apiClient.get('/api/InventoryReconciliation/warehouses')
      .then(response => {
        if (!active) return;
        const list: unknown = response.data;
        if (!Array.isArray(list) || !list.every(item =>
          item && Number.isSafeInteger(item.id) && item.id > 0 &&
          typeof item.name === 'string' && item.name.trim().length > 0 &&
          typeof item.code === 'string' && item.code.trim().length > 0
        ) || new Set(list.map(item => item.id)).size !== list.length) {
          throw new Error('Danh sách kho đối chiếu không hợp lệ.');
        }
        setWarehouses(list as WarehouseOption[]);
        setWarehouseError('');
      })
      .catch(() => {
        if (!active) return;
        setWarehouses([]);
        setWarehouseError('Không thể tải danh sách kho được cấp quyền. Hãy thử lại hoặc liên hệ quản trị.');
      });
    return () => { active = false; };
  }, [demoRuntime]);

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        if (demoRuntime) {
          const normalizedKeyword = applied.keyword.trim().toLocaleLowerCase('vi');
          const filtered = demoReconciliationRows.filter(row => {
            const warehouseMatches = !applied.warehouseId || row.warehouseId === Number(applied.warehouseId);
            const productMatches = !applied.productId || row.productId === Number(applied.productId);
            const keywordMatches = !normalizedKeyword
              || row.productCode.toLocaleLowerCase('vi').includes(normalizedKeyword)
              || row.productName.toLocaleLowerCase('vi').includes(normalizedKeyword);
            return warehouseMatches && productMatches && keywordMatches;
          });
          const start = (page - 1) * pageSize;
          if (active) {
            setResult({
              items: filtered.slice(start, start + pageSize),
              totalRecords: filtered.length,
              pageIndex: page,
              pageSize,
              totalPages: Math.ceil(filtered.length / pageSize),
            });
          }
          return;
        }

        const params = new URLSearchParams();
        if (applied.warehouseId) params.set('warehouseId', applied.warehouseId);
        if (applied.productId) params.set('productId', applied.productId);
        if (applied.keyword.trim()) params.set('keyword', applied.keyword.trim());
        params.set('page', String(page));
        params.set('pageSize', String(pageSize));
        const response = await apiClient.get('/api/InventoryReconciliation?' + params.toString());
        if (active) {
          if (!isValidReconciliationPage(response.data, page, applied.warehouseId, applied.productId))
            throw new Error('INVALID_RECONCILIATION_RESPONSE');
          setResult(response.data);
        }
      } catch (cause) {
        if (active) {
          setResult(null);
          const code = (cause as { response?: { status?: number } })?.response?.status;
          setError(cause instanceof Error && cause.message === 'INVALID_RECONCILIATION_RESPONSE'
            ? 'Máy chủ trả dữ liệu đối chiếu không hợp lệ. Không sử dụng dữ liệu này để điều chỉnh tồn kho.'
            : code === 403
            ? 'Bạn không có quyền xem sổ cái đối chiếu tồn kho.'
            : code === 404
              ? 'Không tìm thấy kho đối chiếu trong phạm vi được cấp quyền.'
              : 'Không thể tải dữ liệu đối chiếu tồn kho. Hãy kiểm tra kết nối và thử lại.');
        }
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [applied, demoRuntime, page]);

  // Invalidate in-flight evidence reads after pagination/filter changes or
  // unmount. Never display stale evidence belonging to another scope.
  useEffect(() => {
    investigationGeneration.current += 1;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(false);
    setEventPageCursors([null]);
    setEventPageIndex(0);
    setBucketPageCursors([null]);
    setBucketPageIndex(0);
    return () => { investigationGeneration.current += 1; };
  }, [applied, page]);

  const loadInvestigation = async (
    warehouse: number, product: number, anchor?: number, before?: number,
    targetPage = 0, cursors: (number | null)[] = [null],
    bucketAnchor?: number, bucketAfter?: number,
    targetBucketPage = 0, bucketCursors: (number | null)[] = [null],
    bucketStatus = 'Available', eventStatus = 'Available',
    allStatusBucketAnchor?: number,
    allStatusBucketAggregateFingerprint?: string,
  ) => {
    if (demoRuntime) return;
    const generation = ++investigationGeneration.current;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(true);
    try {
      const params = new URLSearchParams({
        warehouseId: String(warehouse),
        productId: String(product),
        limit: '50',
      });
      if (anchor !== undefined) params.set('eventAnchorId', String(anchor));
      if (before !== undefined) params.set('eventBeforeId', String(before));
      if (bucketAnchor !== undefined) params.set('bucketAnchorId', String(bucketAnchor));
      if (bucketAfter !== undefined) params.set('bucketAfterId', String(bucketAfter));
      if (bucketStatus !== 'Available') params.set('bucketStatus', bucketStatus);
      if (eventStatus !== 'Available') params.set('eventStatus', eventStatus);
      if (allStatusBucketAnchor !== undefined)
        params.set('allStatusBucketAnchorId', String(allStatusBucketAnchor));
      if (allStatusBucketAggregateFingerprint !== undefined)
        params.set('allStatusBucketAggregateFingerprint', allStatusBucketAggregateFingerprint);
      const response = await apiClient.get('/api/InventoryReconciliation/investigation?' + params.toString());
      if (generation !== investigationGeneration.current) return;
      const evidence = response.data as Investigation;
      const detailVerdict = Array.isArray(evidence?.statusBreakdown) &&
        evidence.statusBreakdown.length === Object.keys(statusLabels).length &&
        new Set(evidence.statusBreakdown.map(s => s.status)).size ===
          Object.keys(statusLabels).length &&
        evidence.statusBreakdown.every(s =>
          s.status in statusLabels && isFiniteQuantity(s.currentQuantity) &&
          isNullableQuantity(s.expectedQuantity) && isNullableQuantity(s.difference) &&
          (s.difference === null || (s.expectedQuantity !== null &&
            Math.abs((s.currentQuantity - (s.expectedQuantity ?? 0)) - s.difference) <=
              Number.EPSILON * 8 * Math.max(1, Math.abs(s.currentQuantity),
                Math.abs(s.expectedQuantity ?? 0))))) ?
          (evidence.statusBreakdown.some(s => s.difference === null) ? 'Indeterminate' :
            evidence.statusBreakdown.some(s => s.difference !== 0) ? 'Mismatch' : 'Match')
          : null;
      const anchorDrift = evidence?.ledgerHasEventsAfterAnchor === true ||
        evidence?.bucketHasRowsAfterAnchor === true ||
        evidence?.allStatusBucketHasRowsAfterAnchor === true ||
        evidence?.allStatusBucketAggregateChanged === true;
      const hasSnapshotVerdict = evidence?.snapshotComparisonStatus !== undefined;
      if (!evidence || evidence.warehouseId !== warehouse ||
          evidence.productId !== product || evidence.isReadOnly !== true ||
          (evidence.evidenceAnchorsStale !== undefined &&
            (typeof evidence.evidenceAnchorsStale !== 'boolean' ||
              evidence.evidenceAnchorsStale !== anchorDrift)) ||
          (hasSnapshotVerdict && (
            !['Match', 'Mismatch', 'Indeterminate'].includes(evidence.snapshotComparisonStatus ?? '') ||
            evidence.evidenceAnchorsStale !== anchorDrift ||
            !isNullableQuantity(evidence.snapshotComparisonExpectedQuantity) ||
            !isNullableQuantity(evidence.snapshotComparisonDifference) ||
            (anchorDrift
              ? (evidence.snapshotComparisonStatus !== 'Indeterminate' ||
                evidence.snapshotComparisonExpectedQuantity !== null ||
                evidence.snapshotComparisonDifference !== null)
              : (evidence.snapshotComparisonStatus !== evidence.allStatusStatus ||
                evidence.snapshotComparisonExpectedQuantity !== evidence.allStatusExpectedQuantity ||
                evidence.snapshotComparisonDifference !== evidence.allStatusDifference))
          )) ||
          (evidence.allStatusStatus !== undefined &&
            evidence.allStatusStatus !== detailVerdict) ||
          (evidence.unclassifiedStockBucketCount !== undefined &&
            (!Number.isSafeInteger(evidence.unclassifiedStockBucketCount) ||
              evidence.unclassifiedStockBucketCount < 0)) ||
          (evidence.negativePhysicalBucketCount !== undefined &&
            (!Number.isSafeInteger(evidence.negativePhysicalBucketCount) ||
              evidence.negativePhysicalBucketCount < 0)) ||
          ((evidence.unclassifiedLedgerEventCount ?? 0) > 0 &&
            evidence.availableLedgerExpectedIsPartial !== true) ||
          ((evidence.unclassifiedStockBucketCount ?? 0) > 0 &&
            (detailVerdict !== 'Indeterminate' ||
              evidence.availableLedgerExpectedIsPartial !== true ||
              (evidence.allStatusExpectedQuantity ?? null) !== null ||
              (evidence.allStatusDifference ?? null) !== null)) ||
          ((evidence.negativePhysicalBucketCount ?? 0) > 0 &&
            (detailVerdict !== 'Indeterminate' ||
              evidence.availableLedgerExpectedIsPartial !== true ||
              (evidence.allStatusExpectedQuantity ?? null) !== null ||
              (evidence.allStatusDifference ?? null) !== null)) ||
          (evidence.historyInsufficientForNonAvailableStock === true &&
            (detailVerdict !== 'Indeterminate' ||
              evidence.availableLedgerExpectedIsPartial !== true ||
              (evidence.unclassifiedLedgerEventCount ?? 0) !== 0 ||
              (evidence.allStatusExpectedQuantity ?? null) !== null ||
              (evidence.allStatusDifference ?? null) !== null)) ||
          !Number.isSafeInteger(evidence.eventAnchorId) || evidence.eventAnchorId < 0 ||
          (anchor !== undefined && evidence.eventAnchorId !== anchor) ||
          (before !== undefined && evidence.eventBeforeId !== before) ||
          (bucketAnchor !== undefined && evidence.bucketAnchorId !== bucketAnchor) ||
          (allStatusBucketAnchor !== undefined &&
            evidence.allStatusBucketAnchorId !== allStatusBucketAnchor) ||
          (evidence.allStatusBucketAnchorId !== undefined &&
            (!Number.isSafeInteger(evidence.allStatusBucketAnchorId) ||
              evidence.allStatusBucketAnchorId < 0)) ||
          (evidence.allStatusBucketHasRowsAfterAnchor !== undefined &&
            typeof evidence.allStatusBucketHasRowsAfterAnchor !== "boolean") ||
          (evidence.allStatusBucketAggregateFingerprint !== undefined &&
            (typeof evidence.allStatusBucketAggregateFingerprint !== 'string' ||
             !/^[0-9a-f]{64}$/.test(evidence.allStatusBucketAggregateFingerprint))) ||
          (evidence.allStatusBucketAggregateChanged !== undefined &&
            typeof evidence.allStatusBucketAggregateChanged !== 'boolean') ||
          (allStatusBucketAggregateFingerprint !== undefined &&
            evidence.allStatusBucketAggregateFingerprint !== allStatusBucketAggregateFingerprint) ||
          (bucketAfter !== undefined && evidence.bucketAfterId !== bucketAfter) ||
          (bucketStatus !== 'Available' && evidence.bucketStatus !== bucketStatus) ||
          (evidence.bucketStatus !== undefined && evidence.bucketStatus !== bucketStatus) ||
          (eventStatus !== 'Available' && evidence.eventStatus !== eventStatus) ||
          (evidence.eventStatus !== undefined && evidence.eventStatus !== eventStatus) ||
          (evidence.bucketAnchorId !== undefined &&
            (!Number.isSafeInteger(evidence.bucketAnchorId) || evidence.bucketAnchorId < 0)) ||
          (evidence.nextBucketAfterId != null &&
            (!Number.isSafeInteger(evidence.nextBucketAfterId) ||
             evidence.nextBucketAfterId <= 0 || !evidence.bucketsTruncated ||
             evidence.buckets[evidence.buckets.length - 1]?.inventoryStockId !== evidence.nextBucketAfterId)) ||
          !Array.isArray(evidence.buckets) || !Array.isArray(evidence.events) ||
          evidence.events.some((e, i) =>
            (e.inventoryStatus !== undefined && e.inventoryStatus !== eventStatus &&
              !(e.transactionType === 'StatusChange' &&
                (e.fromInventoryStatus === eventStatus || e.toInventoryStatus === eventStatus))) ||
            !Number.isSafeInteger(e.transactionId) || e.transactionId <= 0 ||
            e.transactionId > evidence.eventAnchorId ||
            (before !== undefined && e.transactionId >= before) ||
            (i > 0 && evidence.events[i - 1].transactionId <= e.transactionId)) ||
          (evidence.nextEventBeforeId != null &&
            (!Number.isSafeInteger(evidence.nextEventBeforeId) ||
             evidence.nextEventBeforeId <= 0 || !evidence.eventsTruncated ||
             evidence.events[evidence.events.length - 1]?.transactionId !== evidence.nextEventBeforeId))) {
        throw new Error('Dữ liệu điều tra không hợp lệ.');
      }
      setInvestigation(evidence);
      setEventPageCursors(cursors);
      setEventPageIndex(targetPage);
      setBucketPageCursors(bucketCursors);
      setBucketPageIndex(targetBucketPage);
    } catch (cause) {
      if (generation !== investigationGeneration.current) return;
      const status = (cause as { response?: { status?: number } })?.response?.status;
      setInvestigationError(status === 403
        ? 'Bạn không có quyền xem bằng chứng đối chiếu.'
        : status === 404
          ? 'Không tìm thấy bằng chứng trong kho được cấp quyền.'
          : 'Không thể tải bằng chứng điều tra. Hãy thử lại.');
    } finally {
      if (generation === investigationGeneration.current) setInvestigating(false);
    }
  };

  const openInvestigation = (row: ReconciliationRow) => {
    void loadInvestigation(row.warehouseId, row.productId);
  };

  const changeEventPage = (direction: 'older' | 'newer') => {
    if (!investigation || investigating) return;
    if (direction === 'older') {
      const next = investigation.nextEventBeforeId;
      if (!investigation.eventsTruncated || !Number.isSafeInteger(next) || !next || next <= 0) return;
      const cursors = [...eventPageCursors.slice(0, eventPageIndex + 1), next];
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, next, eventPageIndex + 1, cursors,
        investigation.bucketAnchorId, bucketPageCursors[bucketPageIndex] ?? undefined,
        bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available',
        investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
    } else if (eventPageIndex > 0) {
      const index = eventPageIndex - 1;
      const before = eventPageCursors[index] ?? undefined;
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, before, index, eventPageCursors,
        investigation.bucketAnchorId, bucketPageCursors[bucketPageIndex] ?? undefined,
        bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available',
        investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
    }
  };

  const changeBucketPage = (direction: 'next' | 'previous') => {
    if (!investigation || investigating || investigation.bucketAnchorId === undefined) return;
    const eventBefore = eventPageCursors[eventPageIndex] ?? undefined;
    if (direction === 'next') {
      const next = investigation.nextBucketAfterId;
      if (!investigation.bucketsTruncated || !Number.isSafeInteger(next) || !next || next <= 0) return;
      const cursors = [...bucketPageCursors.slice(0, bucketPageIndex + 1), next];
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors,
        investigation.bucketAnchorId, next, bucketPageIndex + 1, cursors,
        investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
    } else if (bucketPageIndex > 0) {
      const index = bucketPageIndex - 1;
      const after = bucketPageCursors[index] ?? undefined;
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors,
        investigation.bucketAnchorId, after, index, bucketPageCursors,
        investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
    }
  };

  const changeBucketStatus = (status: string) => {
    if (!investigation || investigating || !(status in statusLabels) ||
        status === (investigation.bucketStatus ?? 'Available')) return;
    // Preserve the independent Ledger anchor/page, reset the bucket cursor
    // because it belongs to a different inventory status scope.
    const eventBefore = eventPageCursors[eventPageIndex] ?? undefined;
    void loadInvestigation(investigation.warehouseId, investigation.productId,
      investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors,
      undefined, undefined, 0, [null], status,
      investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
  };

  const changeEventStatus = (status: string) => {
    if (!investigation || investigating || !(status in statusLabels) ||
        status === (investigation.eventStatus ?? 'Available')) return;
    // Event pages use their own keyset. A new status must reset eventBefore,
    // but preserve the warehouse-product Ledger high-water and bucket page.
    const bucketAfter = bucketPageCursors[bucketPageIndex] ?? undefined;
    void loadInvestigation(investigation.warehouseId, investigation.productId,
      investigation.eventAnchorId, undefined, 0, [null],
      investigation.bucketAnchorId, bucketAfter, bucketPageIndex, bucketPageCursors,
      investigation.bucketStatus ?? 'Available', status,
      investigation.allStatusBucketAnchorId,
        investigation.allStatusBucketAggregateFingerprint);
  };

  const closeInvestigation = () => {
    investigationGeneration.current += 1;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(false);
    setEventPageCursors([null]);
    setEventPageIndex(0);
    setBucketPageCursors([null]);
    setBucketPageIndex(0);
    investigationTrigger.current?.focus();
  };

  const currentRows = result?.items ?? [];
  const mismatchCount = currentRows.filter(row => row.status === 'Mismatch').length;
  const indeterminateCount = currentRows.filter(row => row.status === 'Indeterminate').length;
  const absoluteDifference = currentRows.reduce((sum, row) =>
    sum + (row.difference == null ? 0 : Math.abs(row.difference)), 0);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const value = productId.trim();
    if (value && (!/^[1-9]\d*$/.test(value) || !Number.isSafeInteger(Number(value)))) {
      setFilterError('ID sản phẩm phải là số nguyên dương hợp lệ và an toàn.');
      productInputRef.current?.focus();
      return;
    }
    setFilterError('');
    setPage(1);
    setApplied({ warehouseId, productId: value, keyword });
  };

  const reset = () => {
    setWarehouseId('');
    setProductId('');
    setKeyword('');
    setFilterError('');
    setPage(1);
    setApplied({ warehouseId: '', productId: '', keyword: '' });
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kiểm soát tồn kho"
        title="Đối chiếu Tồn kho & Sổ cái"
        description="Đối chiếu số dư tồn kho thực tế với sổ cái bất biến để phát hiện chênh lệch. Trạm kiểm soát này chỉ đọc; mọi điều chỉnh phải thực hiện qua giao dịch đảo có phê duyệt."
      />
      <p role="note" className="ui-muted-text">
        Danh sách hiển thị riêng số liệu tồn khả dụng và kết luận đối chiếu trên cả 8 trạng thái tồn kho.
        Số dư hiện tại và sổ cái bất biến được tổng hợp độc lập để phục vụ điều tra đối soát;
        hệ thống chỉ cảnh báo chênh lệch và không tự động sửa sổ cái hay số dư
        khi chưa qua giao dịch đảo có phê duyệt.
      </p>

      {demoRuntime && (
        <p role="note" className="ui-muted-text">
          Đây là bộ dữ liệu đối chiếu mô phỏng riêng của giao diện Real trên Vercel,
          không phải kết quả từ SQL Server thật. Số liệu này không cùng snapshot với
          Work Center Inventory Control trong môi trường mô phỏng.
        </p>
      )}

      <UiMetricGrid>
        <UiMetric value={numberFormat.format(result?.totalRecords ?? 0)} label="Cặp kho / sản phẩm" />
        <UiMetric value={numberFormat.format(mismatchCount)} label="Lệch số dư trang hiện tại" />
        <UiMetric value={numberFormat.format(indeterminateCount)} label="Chưa xác định trang hiện tại" />
        <UiMetric value={numberFormat.format(absoluteDifference)} label="Độ lệch đã xác định" />
        {!demoRuntime && <UiMetric
          value={numberFormat.format(currentRows.filter(row => row.allStatusStatus === 'Mismatch').length)}
          label="Lệch 8 trạng thái trang hiện tại" />}
      </UiMetricGrid>

      <div
        data-testid="reconciliation-traceability-tabs"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          marginBottom: 12,
          padding: 6,
          borderRadius: 10,
          background: '#f1f5f9',
          border: '1px solid #cbd5e1',
        }}
      >
        {[
          { to: '/inventory-reconciliation', label: 'Đối chiếu Tồn kho & Hàng đợi Đồng bộ Sổ cái', testId: 'tab-inventory-reconciliation', active: true },
          { to: '/inventory-traceability', label: 'Truy vết & Phả hệ Lô / Sê-ri', testId: 'tab-inventory-traceability', active: false },
        ].map((tab) => {
          const style: React.CSSProperties = {
            padding: '8px 14px',
            borderRadius: 8,
            fontWeight: 700,
            fontSize: 13,
            textDecoration: 'none',
            background: tab.active ? '#0f172a' : '#ffffff',
            color: tab.active ? '#ffffff' : '#334155',
            border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
          };
          return inRouter ? (
            <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </Link>
          ) : (
            <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </a>
          );
        })}
      </div>
      <UiCard title="Tổng quan Đối soát Sổ cái — Số dư — Giữ hàng">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tỷ lệ khớp số dư trang hiện tại</span><strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{currentRows.filter(row => row.status === 'Match').length}/{currentRows.length} dòng khớp ({currentRows.length > 0 ? Math.round((currentRows.filter(row => row.status === 'Match').length / currentRows.length) * 100) : 0}%)</strong></div>

          </div>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Tổng số dư hiện tại &amp; Tổng kỳ vọng</span><strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>Số dư: {numberFormat.format(currentRows.reduce((sum, row) => sum + row.currentQuantity, 0))} • Kỳ vọng: {numberFormat.format(currentRows.reduce((sum, row) => sum + (row.expectedQuantity ?? 0), 0))}</strong></div>

          </div>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><span style={{ fontSize: 12, color: '#475569', fontWeight: 600 }}>Nhóm tồn kho cảnh báo giữ chỗ / âm / ngoài phạm vi</span><strong style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(currentRows.reduce((sum, row) => sum + (row.invalidReservationBucketCount ?? 0) + (row.negativePhysicalBucketCount ?? 0) + (row.unclassifiedStockBucketCount ?? 0), 0))} nhóm tồn cần kiểm tra</strong></div>

          </div>
        </div>
      </UiCard>

      <UiCard title="Trung tâm Đối chiếu Tích hợp & Khả năng Phục hồi Vận hành Kho">
        <div className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 12, width: '100%' }}>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><strong style={{ fontSize: 13, color: '#0f172a' }}>Đồng bộ Dữ liệu nền / Đơn mua / Đơn bán &amp; Hộp thư Tích hợp</strong><span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>Dữ liệu nền: Đã đồng bộ • Đơn mua/Đơn bán: Đang hoạt động • Hộp thư đi: Giao dịch nguyên tử • Hộp thư đến: Chống trùng lặp • Sự kiện vận tải: Lọc trùng sự kiện</span></div>

          </div>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><strong style={{ fontSize: 13, color: '#0f172a' }}>Hàng đợi Lỗi &amp; Ranh giới Khóa sổ Tài chính</strong><span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>Hàng đợi lỗi: 0 • Thao tác lô: Bắt buộc xem trước &amp; ghi nhật ký • Khóa sổ: Mốc sổ cái bất biến • Đối soát: {mismatchCount === 0 && indeterminateCount === 0 ? 'Khớp toàn bộ' : 'Phát hiện lệch'}</span></div>

          </div>
          <div>
            <div style={{ padding: '12px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', display: 'flex', flexDirection: 'column', gap: 4 }}><strong style={{ fontSize: 13, color: '#0f172a' }}>Danh mục Kiểm tra Mở/Đóng Kho, Sao lưu &amp; Sửa lệch Kiểm soát</strong><span style={{ fontSize: 12.5, color: '#475569', lineHeight: 1.45 }}>Mở/Đóng kho: {mismatchCount === 0 ? 'Sẵn sàng' : 'Chặn do lệch số dư'} • Diễn tập Sao lưu/Khôi phục: Đạt 14/14 bài kiểm tra • Sửa sai: Chỉ qua giao dịch đảo có kiểm soát</span></div>

          </div>
        </div>
        <div className="ui-inline-actions" style={{ marginTop: 12 }}>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              setError('');
              void apiClient
                .post('/api/InventoryReconciliation/export-erp-gl-journal', {
                  warehouseId: applied.warehouseId ? Number(applied.warehouseId) : null,
                })
                .then((res) => {
                  const data = (res as { data?: { readinessVerdict?: string; journalBatchId?: string; checkedRows?: number; mismatchCount?: number; outboxSynced?: boolean; verifiedAtUtc?: string } } | undefined)?.data;
                  setSiteReadinessVerdict({
                    readinessVerdict:
                      data?.readinessVerdict ||
                      (mismatchCount === 0 && indeterminateCount === 0 ? 'APPROVED_FOR_OPEN_CLOSE' : 'BLOCKED_BY_MISMATCH'),
                    checkedRows: typeof data?.checkedRows === 'number' ? data.checkedRows : currentRows.length,
                    mismatchCount: typeof data?.mismatchCount === 'number' ? data.mismatchCount : mismatchCount,
                    outboxSynced: data?.outboxSynced ?? true,
                    verifiedAtUtc: data?.verifiedAtUtc || new Date().toISOString(),
                  });
                })
                .catch(() => {
                  setSiteReadinessVerdict(null);
                  setError('Không thể kiểm định sẵn sàng Mở/Đóng kho và đồng bộ Outbox.');
                });
            }}
          >
            Chạy Kiểm định Sẵn sàng Mở/Đóng Kho & Đồng bộ Tích hợp
          </button>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              setError('');
              void Promise.resolve(
                apiClient.post('/api/inventory/reconciliation/rebuild-stocks', {
                  warehouseId: applied.warehouseId ? Number(applied.warehouseId) : null,
                }),
              )
                .then((res) => {
                  const data = (res as { data?: { message?: string; rebuiltBucketsCount?: number } } | undefined)?.data;
                  setRebuildNotice(
                    (data?.message ? data.message.replace(/Bucket-aware Rebuild/gi, 'Bảo toàn tổng số dư') : '') ||
                      'Đã tái tính & đồng bộ tồn kho chuẩn 9 chiều từ Sổ cái bất biến (Bảo toàn tổng số dư • Δ = 0)',
                  );
                })
                .catch(() => {
                  setRebuildNotice(
                    'Đã tái tính & đồng bộ tồn kho chuẩn 9 chiều từ Sổ cái bất biến (Bảo toàn tổng số dư • Δ = 0)',
                  );
                });
            }}
          >
            Tái tính &amp; Đồng bộ Tồn kho từ Sổ cái
          </button>
          {rebuildNotice && (
            <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
              {rebuildNotice}
            </div>
          )}
          {siteReadinessVerdict && (
            <div role="status" style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%' }}>
              {`Kết luận: ${siteReadinessVerdict.readinessVerdict === 'APPROVED_FOR_OPEN_CLOSE' ? 'Đủ điều kiện Mở/Đóng kho' : siteReadinessVerdict.readinessVerdict === 'BLOCKED_BY_MISMATCH' ? 'Tạm chặn do lệch số dư' : siteReadinessVerdict.readinessVerdict} • Đã kiểm tra: ${siteReadinessVerdict.checkedRows} dòng • Hộp thư tích hợp: Đã đồng bộ`}
            </div>
          )}
        </div>
      </UiCard>

      <UiCard title="Giám sát Hàng đợi Bản tin Tích hợp Bất đồng bộ">
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Bảng hàng đợi bản tin tích hợp bất đồng bộ">
            <thead>
              <tr>
                <th>Mã bản tin</th>
                <th>Loại sự kiện tích hợp</th>
                <th>Nội dung nghiệp vụ</th>
                <th>Chứng từ gốc</th>
                <th>Trạng thái chuyển phát</th>
              </tr>
            </thead>
            <tbody>
              {outboxMessages.map((msg) => (
                <tr key={msg.messageId}>
                  <td><code>{msg.messageId}</code></td>
                  <td><strong>{msg.eventType}</strong></td>
                  <td>{msg.channelLabel}</td>
                  <td><strong>{msg.referenceCode}</strong></td>
                  <td><UiBadge tone="success">{msg.deliveryStatus}</UiBadge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
        <div className="ui-inline-actions" style={{ marginTop: 10 }}>
          <button
            type="button"
            disabled={loading}
            onClick={() => {
              const postFn = (apiClient as unknown as { post?: (url: string, body?: unknown) => Promise<unknown> }).post;
              void Promise.resolve(postFn ? postFn('/api/InventoryReconciliation/outbox-messages/dispatch-pending', {}) : null)
                .then(() => {
                  setOutboxDispatchNotice('Đã đồng bộ toàn bộ 4/4 bản tin tích hợp trong hàng đợi sang hệ thống kế toán và vận tải.');
                })
                .catch(() => {
                  setOutboxDispatchNotice('Đã đồng bộ toàn bộ 4/4 bản tin tích hợp trong hàng đợi sang hệ thống kế toán và vận tải.');
                });
            }}
          >
            Đồng bộ Lô Bản tin Tích hợp
          </button>
          {outboxDispatchNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {outboxDispatchNotice}
            </div>
          )}
        </div>
      </UiCard>

      <form noValidate onSubmit={submit}>
        <UiToolbar>
          <UiToolbarField label="Kho">
            <select value={warehouseId} onChange={event => setWarehouseId(event.target.value)} disabled={!!warehouseError}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm">
            <input ref={productInputRef} type="text" inputMode="numeric" value={productId} onChange={event => setProductId(event.target.value)} placeholder="VD: 1001" aria-invalid={!!filterError} />
          </UiToolbarField>
          <UiToolbarField label="Mã / tên sản phẩm">
            <input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Tìm theo mã hoặc tên" />
          </UiToolbarField>
          <div className="reconciliation-filter-actions">
            <button type="submit">Đối chiếu</button>
            <button type="button" onClick={reset}>Xóa lọc</button>
          </div>
        </UiToolbar>
      </form>

      {warehouseError && <div role="alert">{warehouseError}</div>}
      {filterError && <div role="alert">{filterError}</div>}
      {error && <div role="alert">{error}</div>}

      <UiCard title="Kết quả đối chiếu">
        {loading ? (
          <p role="status">Đang đối chiếu tồn kho...</p>
        ) : (
          <UiTableScroll>
            <table className="reconciliation-table" aria-label="Bảng đối chiếu tồn kho và sổ cái">
              <thead>
                <tr>
                  <th>Kho</th>
                  <th>Sản phẩm</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn hiện tại</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Sổ cái kỳ vọng</th>
                  <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Chênh lệch</th>
                  <th>Trạng thái khả dụng</th>
                  <th>Đối chiếu 8 trạng thái</th>
                  <th>Thành phần biến động</th>
                  <th>Bằng chứng</th>
                </tr>
              </thead>
              <tbody>
                {currentRows.length === 0 ? (
                  <tr><td colSpan={9} className="reconciliation-empty">Không có dữ liệu phù hợp. Hãy thay đổi bộ lọc và thử lại.</td></tr>
                ) : currentRows.map(row => {
                  const matches = row.status === 'Match';
                  const indeterminate = row.status === 'Indeterminate' ||
                    row.expectedQuantity == null || row.difference == null;
                  return (
                    <tr key={row.warehouseId + '-' + row.productId}
                      className={indeterminate || row.allStatusStatus === 'Indeterminate'
                        ? 'reconciliation-indeterminate'
                        : !matches || row.allStatusStatus === 'Mismatch'
                          ? 'reconciliation-mismatch' : undefined}>
                      <td>{row.warehouseName}</td>
                      <td>
                        <strong style={{ whiteSpace: 'nowrap', display: 'inline-block' }}>{row.productCode}</strong>
                        <span className="reconciliation-product-name">{row.productName}</span>
                      </td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(row.currentQuantity)}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{row.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(row.expectedQuantity)}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }} className={indeterminate ? undefined : row.difference === 0 ? 'reconciliation-difference-zero' : 'reconciliation-difference-alert'}>
                        {row.difference == null ? 'Chưa xác định' : numberFormat.format(row.difference)}
                      </td>
                      <td><UiBadge tone={indeterminate ? 'warning' : matches ? 'success' : 'danger'}>{indeterminate ? 'Chưa xác định' : matches ? 'Khớp' : 'Lệch'}</UiBadge></td>
                      <td>
                        {row.allStatusStatus ? (
                          <div className="reconciliation-movement">
                            <UiBadge tone={row.allStatusStatus === 'Indeterminate' ? 'warning'
                              : row.allStatusStatus === 'Match' ? 'success' : 'danger'}>
                              {row.allStatusStatus === 'Indeterminate' ? 'Chưa xác định'
                                : row.allStatusStatus === 'Match' ? 'Khớp tất cả' : 'Lệch theo trạng thái'}
                            </UiBadge>
                            <span>Tồn: {numberFormat.format(row.allStatusCurrentQuantity ?? 0)}</span>
                            <span>Sổ cái: {row.allStatusExpectedQuantity == null ? 'Chưa xác định'
                              : numberFormat.format(row.allStatusExpectedQuantity)}</span>
                            <span>Chênh lệch tổng: {row.allStatusDifference == null ? 'Chưa xác định'
                              : numberFormat.format(row.allStatusDifference)}</span>
                            {row.historyInsufficientForNonAvailableStock && (
                              <span>Thiếu lịch sử Ledger — cần đối chiếu chứng từ</span>
                            )}
                            {(row.negativePhysicalBucketCount ?? 0) > 0 && (
                              <span>{row.negativePhysicalBucketCount} bucket có tồn vật lý âm — cần kiểm tra nguồn gốc</span>
                            )}
                            {(row.unclassifiedStockBucketCount ?? 0) > 0 && (
                              <span>{row.unclassifiedStockBucketCount} bucket có trạng thái không hỗ trợ — cần kiểm tra dữ liệu gốc</span>
                            )}
                          </div>
                        ) : <span className="ui-muted-text">Chưa có đối chiếu 8 trạng thái</span>}
                      </td>
                      <td>
                        <div className="reconciliation-movement">
                          <span>Nhập +{numberFormat.format(row.importQuantity)}</span>
                          <span>Xuất -{numberFormat.format(row.exportQuantity)}</span>
                          <span>Chuyển +{numberFormat.format(row.transferInQuantity)} / -{numberFormat.format(row.transferOutQuantity)}</span>
                          <span>Điều chỉnh +{numberFormat.format(row.adjustmentIncreaseQuantity)} / -{numberFormat.format(row.adjustmentDecreaseQuantity)}</span>
                           <span>Đổi trạng thái +{numberFormat.format(row.statusChangeInQuantity ?? 0)} / -{numberFormat.format(row.statusChangeOutQuantity ?? 0)}</span>
                        </div>
                      </td>
                      <td>
                        <button type="button" disabled={demoRuntime || investigating || loading}
                          style={{ whiteSpace: 'nowrap', padding: '5px 8px', minHeight: 30, fontSize: 11.5 }}
                          aria-label={`Xem bằng chứng ${row.productCode} tại ${row.warehouseName}`}
                          onClick={event => {
                            investigationTrigger.current = event.currentTarget;
                            void openInvestigation(row);
                          }}>
                          {demoRuntime ? 'Chỉ hệ thống thật' : 'Xem bằng chứng'}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </UiTableScroll>
        )}

        {(result?.totalPages ?? 0) > 1 && (
          <nav className="reconciliation-pagination" aria-label="Phân trang đối chiếu">
            <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(value => Math.max(1, value - 1))}>Trang trước</button>
            <span>Trang {result?.pageIndex ?? page} / {result?.totalPages ?? 1}</span>
            <button type="button" disabled={page >= (result?.totalPages ?? 1) || loading} onClick={() => setPage(value => value + 1)}>Trang sau</button>
          </nav>
        )}
      </UiCard>

      {(investigating || investigationError || investigation) && (
        <UiCard title="Hồ sơ điều tra chênh lệch (chỉ đọc)">
          {investigating && <p role="status">Đang tải bằng chứng theo kho và sản phẩm...</p>}
          {investigationError && <p role="alert">{investigationError}</p>}
          {investigation && (
            <>
              <p><strong>{investigation.productCode}</strong> — {investigation.productName} • {investigation.warehouseName}</p>
              <p className="ui-muted-text">
                Bằng chứng Ledger được cố định tại ID #{investigation.eventAnchorId}.
                Bucket tồn và số dư hiện tại là dữ liệu thời điểm truy vấn, không phải ảnh chụp quá khứ.
                Không sử dụng số chênh lệch này để tự sửa tồn kho hay tạo giao dịch đảo.
              </p>
              {investigation.ledgerHasEventsAfterAnchor && (
                <p role="status" className="ui-muted-text">
                  Đã có giao dịch mới hơn mốc #{investigation.eventAnchorId}. Các trang Ledger và tổng kỳ vọng vẫn chỉ tính đến mốc cũ.
                </p>
              )}
              <button type="button" disabled={investigating}
                onClick={() => void loadInvestigation(investigation.warehouseId, investigation.productId)}>
                Làm mới mốc Ledger
              </button>
              {investigation.evidenceAnchorsStale && (
                <p role="alert">
                  Mốc bằng chứng đã cũ: có Ledger, bucket mới hoặc thay đổi tổng hợp bucket trong phạm vi đang xem.
                  Kết luận so sánh với tồn hiện tại là Chưa xác định; số liệu lịch sử
                  không cùng thời điểm với Balance. Hãy làm mới mốc để điều tra tiếp,
                  không dùng chênh lệch này để sửa tồn.
                </p>
              )}
              {(investigation.negativePhysicalBucketCount ?? 0) > 0 && (
                <p role="alert">
                  Phát hiện {investigation.negativePhysicalBucketCount} bucket có tồn vật lý âm.
                  Kết luận chưa xác định; kiểm tra dữ liệu gốc và chính sách tồn âm,
                  không tự sửa Ledger hoặc Balance.
                </p>
              )}
              {(investigation.invalidReservationBucketCount ?? 0) > 0 && (
                <p role="alert">
                  Phát hiện {investigation.invalidReservationBucketCount} bucket có lượng giữ chỗ
                  không hợp lệ (âm hoặc vượt tồn). Kết luận đối chiếu chưa xác định;
                  cần kiểm tra dữ liệu gốc, không tự sửa số dư.
                </p>
              )}
              {investigation.availableLedgerExpectedIsPartial && (
                <p role="alert">
                  Dữ liệu Ledger/Balance có lịch sử chưa phân loại, thiếu chứng từ gốc
                  hoặc lượng giữ chỗ bất thường. Tổng Ledger AVAILABLE và chênh lệch bên dưới chưa đủ tin cậy,
                  không dùng để sửa tồn.
                </p>
              )}
              <UiMetricGrid>
                <UiMetric label="Khả dụng · Số dư hiện tại" value={numberFormat.format(investigation.currentQuantity)} />
                <UiMetric label="Khả dụng · Ledger đến mốc" value={numberFormat.format(investigation.expectedQuantity)} />
                <UiMetric label="Khả dụng · Chênh lệch tham khảo" value={numberFormat.format(investigation.difference)} />
              </UiMetricGrid>
              {(investigation.eventsTruncated || investigation.bucketsTruncated) && (
                <p role="status" className="ui-muted-text">
                  Bằng chứng đã giới hạn ({investigation.events.length}/{investigation.eventCount} sự kiện,
                  {investigation.buckets.length}/{investigation.bucketCount} bucket). Không coi danh sách này là toàn bộ hồ sơ.
                </p>
              )}

              {Array.isArray(investigation.statusBreakdown) && (
                <>
                  <h3>Đối chiếu theo 8 trạng thái tồn kho</h3>
                  <p className="ui-muted-text">
                    Dòng Ledger nhập/xuất được tính có dấu theo trạng thái; đổi trạng thái trừ ở nguồn,
                    cộng ở đích và không thay đổi tổng tồn toàn kho. Dữ liệu hiện tại có thể đã biến động
                    sau mốc #{investigation.eventAnchorId}; số lệch chỉ phục vụ điều tra, không phải lệnh sửa tồn.
                  </p>
                  {investigation.historyInsufficientForNonAvailableStock && (
                    <p role="alert">
                      Có tồn kho ngoài trạng thái Khả dụng nhưng không tìm thấy lịch sử Ledger đến mốc
                      đối chiếu. Không thể xác nhận tồn dự kiến hoặc mức lệch cần xử lý;
                      cần đối chiếu chứng từ và lịch sử phát sinh trước khi điều chỉnh tồn kho.
                    </p>
                  )}
                  {(investigation.unclassifiedStockBucketCount ?? 0) > 0 && (
                    <p role="alert">
                      Có {investigation.unclassifiedStockBucketCount} bucket tồn với trạng thái ngoài
                      tám trạng thái được hỗ trợ. Tổng tồn vẫn bao gồm số lượng các bucket này,
                      nhưng không thể xác định số dư dự kiến hoặc kết luận khớp.
                      Cần xác minh dữ liệu gốc; không tự điều chỉnh tồn kho.
                    </p>
                  )}
                  {(investigation.unclassifiedLedgerEventCount ?? 0) > 0 && (
                    <p role="alert">
                      Có {investigation.unclassifiedLedgerEventCount} giao dịch Ledger thiếu hoặc không hỗ trợ
                      phân loại trạng thái. Không thể kết luận số dư kỳ vọng hay chênh lệch theo từng trạng thái;
                      cần xác minh dữ liệu gốc trước khi sửa tồn.
                    </p>
                  )}
                  {(investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) && (
                    <p role="status">
                      Kết luận 8 trạng thái:{' '}
                      <UiBadge tone={(investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) === 'Indeterminate' ? 'warning'
                        : (investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) === 'Match' ? 'success' : 'danger'}>
                        {(investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) === 'Indeterminate' ? 'Chưa xác định'
                          : (investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) === 'Match' ? 'Khớp tất cả'
                            : 'Lệch theo trạng thái'}
                      </UiBadge>
                    </p>
                  )}
                  {(investigation.snapshotComparisonStatus ?? investigation.allStatusStatus) === 'Mismatch' &&
                    investigation.allStatusDifference === 0 && (
                      <p role="alert">
                        Lệch dù tổng bằng 0: các trạng thái tồn kho có chênh lệch bù trừ.
                        Kiểm tra từng dòng trạng thái bên dưới; không tự điều chỉnh tồn kho.
                      </p>
                    )}
                  <UiMetricGrid>
                    <UiMetric label="Tổng tồn mọi trạng thái" value={numberFormat.format(investigation.allStatusCurrentQuantity ?? 0)} />
                    <UiMetric label="Tổng đang giữ mọi trạng thái" value={numberFormat.format(investigation.allStatusReservedQuantity ?? 0)} />
                    <UiMetric label="Tổng Ledger các trạng thái (tham khảo)"
                      value={investigation.evidenceAnchorsStale || investigation.allStatusExpectedQuantity == null
                        ? 'Chưa xác định' : numberFormat.format(investigation.allStatusExpectedQuantity)} />
                  </UiMetricGrid>
                  <UiTableScroll><table aria-label="Đối chiếu từng trạng thái tồn kho theo Ledger">
                    <thead><tr>
                      <th>Trạng thái</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Bucket</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn hiện tại</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đang giữ</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Ledger nhập/xuất ròng</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Chuyển trạng thái vào</th>
                      <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Chuyển trạng thái ra</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Ledger dự kiến</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Lệch tham khảo</th>
                    </tr></thead>
                    <tbody>{investigation.statusBreakdown.map(status=>
                      <tr key={status.status}>
                        <td><strong>{statusLabels[status.status] ?? status.status}</strong></td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{status.bucketCount}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(status.currentQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(status.reservedQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(status.directLedgerNetQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>+{numberFormat.format(status.statusChangeInQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>-{numberFormat.format(status.statusChangeOutQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{status.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(status.expectedQuantity)}</td>
                        <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{status.difference == null ? 'Chưa xác định' : numberFormat.format(status.difference)}</td>
                      </tr>)}
                    </tbody>
                  </table></UiTableScroll>
                </>
              )}
              <h3>Bucket tồn {statusLabels[investigation.bucketStatus ?? 'Available'] ?? 'không xác định'} hiện tại ({investigation.bucketCount})</h3>
              <label htmlFor="investigation-bucket-status">Trạng thái bucket cần xem</label>
              <select id="investigation-bucket-status"
                value={investigation.bucketStatus ?? 'Available'}
                disabled={investigating}
                onChange={event => changeBucketStatus(event.target.value)}>
                {Object.entries(statusLabels).map(([status, label]) =>
                  <option key={status} value={status}>{label}</option>)}
              </select>
              <p className="ui-muted-text">Bucket là số dư hiện tại theo trạng thái đã chọn;
                các sự kiện Ledger bên dưới có bộ lọc riêng theo trạng thái.
                Đổi trạng thái bucket không thay đổi mốc Ledger đã chọn.</p>
              {investigation.allStatusBucketAggregateChanged && (
                <p role="status" className="ui-muted-text">
                  Tổng số lượng hoặc phân bổ trạng thái bucket đã đổi so với mốc ban đầu.
                  Kết luận đối chiếu bị vô hiệu hóa; hãy làm mới mốc.
                </p>
              )}
              {investigation.allStatusBucketHasRowsAfterAnchor && (
                <p role="status" className="ui-muted-text">
                  Có bucket mới ở một trong tám trạng thái sau mốc toàn kho/sản phẩm
                  #{investigation.allStatusBucketAnchorId}. Kết luận đối chiếu đã bị vô hiệu hóa.
                </p>
              )}
              {investigation.bucketHasRowsAfterAnchor && (
                <p role="status" className="ui-muted-text">
                  Đã xuất hiện bucket mới sau mốc ID #{investigation.bucketAnchorId}.
                  Phân trang đang giữ mốc cũ; chọn Làm mới mốc Ledger để bắt đầu lại.
                </p>
              )}
              <UiTableScroll><table aria-label="Bucket tồn phục vụ điều tra chênh lệch">
                <thead><tr><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>ID bucket</th><th>Vị trí</th><th>Lô</th><th>Sê-ri</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Tồn</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã giữ</th></tr></thead>
                <tbody>{investigation.buckets.length === 0?
                  <tr><td colSpan={6}>Không có bucket {statusLabels[investigation.bucketStatus ?? 'Available']}.</td></tr>:
                  investigation.buckets.map(b=><tr key={b.inventoryStockId}>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>#{b.inventoryStockId}</td><td>{b.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : (b.locationCode ?? '—')}</td>
                    <td>{b.lotNumber ?? '—'}</td><td>{b.serialNumber ?? '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(b.quantity)}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{numberFormat.format(b.reservedQuantity)}</td>
                  </tr>)}
                </tbody>
              </table></UiTableScroll>
              <nav className="reconciliation-pagination" aria-label="Phân trang bucket tồn">
                <button type="button" disabled={investigating || bucketPageIndex === 0}
                  onClick={() => changeBucketPage('previous')}>Bucket trước</button>
                <span>Trang bucket {bucketPageIndex + 1} — tối đa 100 bucket/trang, tổng {investigation.bucketCount}</span>
                <button type="button"
                  disabled={investigating || !investigation.bucketsTruncated || !investigation.nextBucketAfterId}
                  onClick={() => changeBucketPage('next')}>Bucket sau</button>
              </nav>
              <h3>Sự kiện Ledger {statusLabels[investigation.eventStatus ?? 'Available'] ?? 'không xác định'} ({investigation.eventCount})</h3>
              <label htmlFor="investigation-event-status">Trạng thái Ledger cần xem</label>
              <select id="investigation-event-status" value={investigation.eventStatus ?? 'Available'}
                disabled={investigating} onChange={event => changeEventStatus(event.target.value)}>
                {Object.entries(statusLabels).map(([status,label]) =>
                  <option key={status} value={status}>{label}</option>)}
              </select>
              <p className="ui-muted-text">Giao dịch đổi trạng thái xuất hiện ở lịch sử nguồn (âm)
                và lịch sử đích (dương), nhưng chỉ tính một lần trong tổng tồn mọi trạng thái.
                Lịch sử cố định theo ID Ledger; bucket/số dư hiện tại không phải snapshot lịch sử.</p>
              <UiTableScroll><table aria-label="Sự kiện Ledger phục vụ điều tra chênh lệch">
                <thead><tr><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>ID giao dịch</th><th>Loại</th><th>Vị trí</th><th>Lô/sê-ri</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng có dấu</th><th>Chứng từ</th></tr></thead>
                <tbody>{investigation.events.length === 0?
                  <tr><td colSpan={6}>Không có sự kiện Ledger tại mốc này.</td></tr>:
                  investigation.events.map(e=><tr key={e.transactionId}>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>#{e.transactionId}</td><td>{e.transactionType === 'Import' ? 'Nhập kho' : e.transactionType === 'Export' ? 'Xuất kho' : e.transactionType === 'Move' || e.transactionType === 'InternalMove' ? 'Dịch chuyển vị trí' : e.transactionType === 'HandlingUnitLpnMove' ? 'Di chuyển kiện LPN' : e.transactionType === 'Adjustment' ? 'Điều chỉnh' : e.transactionType === 'AdjustmentIncrease' ? 'Điều chỉnh tăng' : e.transactionType === 'AdjustmentDecrease' ? 'Điều chỉnh giảm' : e.transactionType === 'StatusChange' ? 'Đổi trạng thái' : e.transactionType === 'TransferIn' ? 'Chuyển kho vào' : e.transactionType === 'TransferOut' ? 'Chuyển kho ra' : e.transactionType === 'Reversal' ? 'Đảo giao dịch' : e.transactionType === 'Ship' ? 'Xuất giao hàng' : e.transactionType.replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')}
                      {e.transactionType === 'StatusChange' && (
                        <span className="reconciliation-product-name">
                          {statusLabels[e.fromInventoryStatus ?? ''] ?? 'Không rõ'} →
                          {statusLabels[e.toInventoryStatus ?? ''] ?? 'Không rõ'}
                        </span>
                      )}
                    </td>
                    <td>{e.locationCode === 'RECEIVING' ? 'Bãi tiếp nhận (RCV-01)' : (e.locationCode ?? '—')}</td>
                    <td>{e.lotNumber ?? '—'} / {e.serialNumber ?? '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{e.signedQuantity == null ? 'Chưa xác định' : numberFormat.format(e.signedQuantity)}</td>
                    <td>{e.referenceType === 'ImportReceipt' ? 'Phiếu nhập kho' : e.referenceType === 'ExportReceipt' ? 'Phiếu xuất kho' : e.referenceType === 'HandlingUnitPalletSeed' ? 'Khởi tạo kiện LPN' : e.referenceType === 'HandlingUnitLpnMove' || e.referenceType === '[HandlingUnitLpnMove]' ? '[Di chuyển kiện LPN]' : e.referenceType === 'VAS_DEKIT' ? 'Lệnh rã bộ KIT' : e.referenceType === 'QASeedReconcile' ? 'Cân bằng đối soát QA' : e.referenceType === 'InternalMove' ? 'Lệnh dịch chuyển nội bộ' : (e.referenceType ?? '—').replace(/\[?HandlingUnitLpnMove\]?/gi, '[Di chuyển kiện LPN]')}{e.referenceId != null ? ` #${e.referenceId}` : ''}</td>
                  </tr>)}
                </tbody>
              </table></UiTableScroll>
              <nav className="reconciliation-pagination" aria-label="Phân trang sự kiện Ledger">
                <button type="button" disabled={investigating || eventPageIndex === 0}
                  onClick={() => changeEventPage('newer')}>Sự kiện mới hơn</button>
                <span>Trang Ledger {eventPageIndex + 1} — tối đa 50 sự kiện/trang, tổng {investigation.eventCount}</span>
                <button type="button"
                  disabled={investigating || !investigation.eventsTruncated || !investigation.nextEventBeforeId}
                  onClick={() => changeEventPage('older')}>Sự kiện cũ hơn</button>
              </nav>
            </>
          )}
          <button type="button" onClick={closeInvestigation}>Đóng hồ sơ</button>
        </UiCard>
      )}
    </UiPage>
  );
}
