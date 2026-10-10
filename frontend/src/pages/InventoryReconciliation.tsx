import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
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
  eventStatus?:string; eventBeforeId?:number|null; nextEventBeforeId?:number|null;
  ledgerHasEventsAfterAnchor?:boolean;
  eventsTruncated:boolean; bucketsTruncated:boolean;
  currentQuantity:number; expectedQuantity:number; difference:number;
  allStatusCurrentQuantity?:number; allStatusReservedQuantity?:number;
  allStatusExpectedQuantity?:number|null; allStatusDifference?:number|null;
  allStatusStatus?: 'Match' | 'Mismatch' | 'Indeterminate';
  unclassifiedLedgerEventCount?:number; statusBreakdown?:StatusEvidence[];
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
        (row.historyInsufficientForNonAvailableStock !== undefined &&
          typeof row.historyInsufficientForNonAvailableStock !== 'boolean')) return false;

    const key = row.warehouseId + ':' + row.productId;
    if (seen.has(key)) return false;
    seen.add(key);

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
  const demoRuntime = isBlueprintDemoRuntime();
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

  useEffect(() => {
    if (demoRuntime) {
      setWarehouses(demoReconciliationWarehouses);
      return;
    }
    let active = true;
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
      if (!evidence || evidence.warehouseId !== warehouse ||
          evidence.productId !== product || evidence.isReadOnly !== true ||
          (evidence.allStatusStatus !== undefined &&
            evidence.allStatusStatus !== detailVerdict) ||
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
        investigation.eventStatus ?? 'Available');
    } else if (eventPageIndex > 0) {
      const index = eventPageIndex - 1;
      const before = eventPageCursors[index] ?? undefined;
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, before, index, eventPageCursors,
        investigation.bucketAnchorId, bucketPageCursors[bucketPageIndex] ?? undefined,
        bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available',
        investigation.eventStatus ?? 'Available');
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
        investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available');
    } else if (bucketPageIndex > 0) {
      const index = bucketPageIndex - 1;
      const after = bucketPageCursors[index] ?? undefined;
      void loadInvestigation(investigation.warehouseId, investigation.productId,
        investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors,
        investigation.bucketAnchorId, after, index, bucketPageCursors,
        investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available');
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
      investigation.eventStatus ?? 'Available');
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
      investigation.bucketStatus ?? 'Available', status);
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
        eyebrow="Inventory Control"
        title="Đối chiếu tồn kho & ledger"
        description="So sánh operational balance với immutable ledger để phát hiện chênh lệch. Work center này chỉ đọc; mọi sửa sai phải đi qua transaction có kiểm soát."
      />
      <p role="note" className="ui-muted-text">
        Danh sách hiển thị riêng số liệu AVAILABLE và kết luận đối chiếu cả 8 trạng thái.
        Tồn hiện tại không phải cùng snapshot với Ledger, nên kết quả chỉ dùng điều tra.
        Chưa bao phủ toàn bộ Owner, Handling Unit, Reserved, Allocated hay quy trình khôi phục số dư có phê duyệt.
        Kết quả này chỉ để phát hiện sai lệch, không tự động sửa Ledger hoặc Balance.
      </p>

      {demoRuntime && (
        <p role="note" className="ui-muted-text">
          Đây là bộ dữ liệu đối chiếu mô phỏng riêng của giao diện Real trên Vercel,
          không phải kết quả từ SQL Server thật. Số liệu này không cùng snapshot với
          Work Center Inventory Control hoặc kịch bản INV-11 trong System Blueprint.
        </p>
      )}

      <UiMetricGrid>
        <UiMetric value={numberFormat.format(result?.totalRecords ?? 0)} label="Cặp kho / sản phẩm" />
        <UiMetric value={numberFormat.format(mismatchCount)} label="Mismatch trang hiện tại" />
        <UiMetric value={numberFormat.format(indeterminateCount)} label="Chưa xác định trang hiện tại" />
        <UiMetric value={numberFormat.format(absoluteDifference)} label="Độ lệch đã xác định" />
        {!demoRuntime && <UiMetric
          value={numberFormat.format(currentRows.filter(row => row.allStatusStatus === 'Mismatch').length)}
          label="Lệch 8 trạng thái trang hiện tại" />}
      </UiMetricGrid>

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
            <table className="reconciliation-table">
              <thead>
                <tr>
                  <th>Kho</th>
                  <th>Sản phẩm</th>
                  <th>Tồn hiện tại</th>
                  <th>Ledger kỳ vọng</th>
                  <th>Chênh lệch</th>
                  <th>Trạng thái AVAILABLE</th>
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
                        <strong>{row.productCode}</strong>
                        <span className="reconciliation-product-name">{row.productName}</span>
                      </td>
                      <td>{numberFormat.format(row.currentQuantity)}</td>
                      <td>{row.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(row.expectedQuantity)}</td>
                      <td className={indeterminate ? undefined : row.difference === 0 ? 'reconciliation-difference-zero' : 'reconciliation-difference-alert'}>
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
                            <span>Ledger: {row.allStatusExpectedQuantity == null ? 'Chưa xác định'
                              : numberFormat.format(row.allStatusExpectedQuantity)}</span>
                            <span>Chênh lệch tổng: {row.allStatusDifference == null ? 'Chưa xác định'
                              : numberFormat.format(row.allStatusDifference)}</span>
                            {row.historyInsufficientForNonAvailableStock && (
                              <span>Thiếu lịch sử Ledger — cần đối chiếu chứng từ</span>
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
              {investigation.availableLedgerExpectedIsPartial && (
                <p role="alert">
                  Ledger AVAILABLE có giao dịch chưa xác định được chiều tăng/giảm.
                  Tổng Ledger AVAILABLE và chênh lệch bên dưới chỉ là một phần, không dùng để sửa tồn.
                </p>
              )}
              <UiMetricGrid>
                <UiMetric label="AVAILABLE · Số dư hiện tại" value={numberFormat.format(investigation.currentQuantity)} />
                <UiMetric label="AVAILABLE · Ledger đến mốc" value={numberFormat.format(investigation.expectedQuantity)} />
                <UiMetric label="AVAILABLE · Chênh lệch tham khảo" value={numberFormat.format(investigation.difference)} />
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
                  {(investigation.unclassifiedLedgerEventCount ?? 0) > 0 && (
                    <p role="alert">
                      Có {investigation.unclassifiedLedgerEventCount} giao dịch Ledger thiếu hoặc không hỗ trợ
                      phân loại trạng thái. Không thể kết luận số dư kỳ vọng hay chênh lệch theo từng trạng thái;
                      cần xác minh dữ liệu gốc trước khi sửa tồn.
                    </p>
                  )}
                  {investigation.allStatusStatus && (
                    <p role="status">
                      Kết luận 8 trạng thái:{' '}
                      <UiBadge tone={investigation.allStatusStatus === 'Indeterminate' ? 'warning'
                        : investigation.allStatusStatus === 'Match' ? 'success' : 'danger'}>
                        {investigation.allStatusStatus === 'Indeterminate' ? 'Chưa xác định'
                          : investigation.allStatusStatus === 'Match' ? 'Khớp tất cả'
                            : 'Lệch theo trạng thái'}
                      </UiBadge>
                    </p>
                  )}
                  {investigation.allStatusStatus === 'Mismatch' &&
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
                      value={investigation.allStatusExpectedQuantity == null ? 'Chưa xác định' : numberFormat.format(investigation.allStatusExpectedQuantity)} />
                  </UiMetricGrid>
                  <UiTableScroll><table aria-label="Đối chiếu từng trạng thái tồn kho theo Ledger">
                    <thead><tr>
                      <th>Trạng thái</th><th>Bucket</th><th>Tồn hiện tại</th><th>Đang giữ</th>
                      <th>Ledger nhập/xuất ròng</th><th>Chuyển trạng thái vào</th>
                      <th>Chuyển trạng thái ra</th><th>Ledger dự kiến</th><th>Lệch tham khảo</th>
                    </tr></thead>
                    <tbody>{investigation.statusBreakdown.map(status=>
                      <tr key={status.status}>
                        <td><strong>{statusLabels[status.status] ?? status.status}</strong></td>
                        <td>{status.bucketCount}</td>
                        <td>{numberFormat.format(status.currentQuantity)}</td>
                        <td>{numberFormat.format(status.reservedQuantity)}</td>
                        <td>{numberFormat.format(status.directLedgerNetQuantity)}</td>
                        <td>+{numberFormat.format(status.statusChangeInQuantity)}</td>
                        <td>-{numberFormat.format(status.statusChangeOutQuantity)}</td>
                        <td>{status.expectedQuantity == null ? 'Chưa xác định' : numberFormat.format(status.expectedQuantity)}</td>
                        <td>{status.difference == null ? 'Chưa xác định' : numberFormat.format(status.difference)}</td>
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
              {investigation.bucketHasRowsAfterAnchor && (
                <p role="status" className="ui-muted-text">
                  Đã xuất hiện bucket mới sau mốc ID #{investigation.bucketAnchorId}.
                  Phân trang đang giữ mốc cũ; chọn Làm mới mốc Ledger để bắt đầu lại.
                </p>
              )}
              <UiTableScroll><table aria-label="Bucket tồn phục vụ điều tra chênh lệch">
                <thead><tr><th>ID bucket</th><th>Vị trí</th><th>Lô</th><th>Sê-ri</th><th>Tồn</th><th>Đã giữ</th></tr></thead>
                <tbody>{investigation.buckets.length === 0?
                  <tr><td colSpan={6}>Không có bucket {statusLabels[investigation.bucketStatus ?? 'Available']}.</td></tr>:
                  investigation.buckets.map(b=><tr key={b.inventoryStockId}>
                    <td>#{b.inventoryStockId}</td><td>{b.locationCode ?? '—'}</td>
                    <td>{b.lotNumber ?? '—'}</td><td>{b.serialNumber ?? '—'}</td>
                    <td>{numberFormat.format(b.quantity)}</td><td>{numberFormat.format(b.reservedQuantity)}</td>
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
                <thead><tr><th>ID giao dịch</th><th>Loại</th><th>Vị trí</th><th>Lô/sê-ri</th><th>Số lượng có dấu</th><th>Chứng từ</th></tr></thead>
                <tbody>{investigation.events.length === 0?
                  <tr><td colSpan={6}>Không có sự kiện Ledger tại mốc này.</td></tr>:
                  investigation.events.map(e=><tr key={e.transactionId}>
                    <td>#{e.transactionId}</td><td>{e.transactionType}
                      {e.transactionType === 'StatusChange' && (
                        <span className="reconciliation-product-name">
                          {statusLabels[e.fromInventoryStatus ?? ''] ?? 'Không rõ'} →
                          {statusLabels[e.toInventoryStatus ?? ''] ?? 'Không rõ'}
                        </span>
                      )}
                    </td>
                    <td>{e.locationCode ?? '—'}</td>
                    <td>{e.lotNumber ?? '—'} / {e.serialNumber ?? '—'}</td>
                    <td>{e.signedQuantity == null ? 'Chưa xác định' : numberFormat.format(e.signedQuantity)}</td>
                    <td>{e.referenceType ?? '—'}{e.referenceId != null ? ` #${e.referenceId}` : ''}</td>
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
