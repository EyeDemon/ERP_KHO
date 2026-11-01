import { useEffect, useRef, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import type { FormEvent } from 'react';
import apiClient from '../services/apiClient';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { demoReconciliationRows, demoReconciliationWarehouses } from '../mocks/inventoryReconciliationDemo';
import { UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';
import type {
  Investigation,
  PagedResult,
  ReconciliationRow,
  SiteReadinessVerdict,
  WarehouseOption,
} from '../modules/inventory-pages/inventoryReconciliationValidators';
import {
  DEFAULT_OUTBOX_MESSAGES,
  isValidInvestigationEvidence,
  isValidReconciliationPage,
  mapOutboxMessages,
  numberFormat,
  pageSize,
  statusLabels,
} from '../modules/inventory-pages/inventoryReconciliationValidators';
import { ReconciliationGovernanceAndTable } from '../modules/inventory-pages/ReconciliationGovernanceAndTable';
import { ReconciliationInvestigationCard } from '../modules/inventory-pages/ReconciliationInvestigationCard';
import './InventoryReconciliation.css';

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
  const [eventPageCursors, setEventPageCursors] = useState<(number | null)[]>([null]);
  const [eventPageIndex, setEventPageIndex] = useState(0);
  const [bucketPageCursors, setBucketPageCursors] = useState<(number | null)[]>([null]);
  const [bucketPageIndex, setBucketPageIndex] = useState(0);
  const investigationGeneration = useRef(0);
  const investigationTrigger = useRef<HTMLButtonElement | null>(null);
  const [outboxDispatchNotice, setOutboxDispatchNotice] = useState('');
  const [rebuildNotice, setRebuildNotice] = useState('');
  const [outboxMessages, setOutboxMessages] = useState(DEFAULT_OUTBOX_MESSAGES);
  const [siteReadinessVerdict, setSiteReadinessVerdict] = useState<SiteReadinessVerdict | null>(null);

  useEffect(() => {
    if (demoRuntime) {
      setWarehouses(demoReconciliationWarehouses);
      return;
    }
    let active = true;
    void Promise.resolve(apiClient.get('/api/v1/inventory-reconciliation/outbox-messages'))
      .then((res) => {
        if (!active) return;
        const mapped = mapOutboxMessages((res as { data?: unknown } | undefined)?.data);
        if (mapped) setOutboxMessages(mapped);
      })
      .catch(() => null);
    apiClient
      .get('/api/v1/inventory-reconciliation/warehouses')
      .then((response) => {
        if (!active) return;
        const list: unknown = response.data;
        if (
          !Array.isArray(list) ||
          !list.every((item) => item && Number.isSafeInteger(item.id) && item.id > 0 && typeof item.name === 'string' && item.name.trim().length > 0 && typeof item.code === 'string' && item.code.trim().length > 0) ||
          new Set(list.map((item) => item.id)).size !== list.length
        ) {
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
          const filtered = demoReconciliationRows.filter((row) => {
            const warehouseMatches = !applied.warehouseId || row.warehouseId === Number(applied.warehouseId);
            const productMatches = !applied.productId || row.productId === Number(applied.productId);
            const keywordMatches = !normalizedKeyword || row.productCode.toLocaleLowerCase('vi').includes(normalizedKeyword) || row.productName.toLocaleLowerCase('vi').includes(normalizedKeyword);
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
        const response = await apiClient.get('/api/v1/inventory-reconciliation?' + params.toString());
        if (active) {
          if (!isValidReconciliationPage(response.data, page, applied.warehouseId, applied.productId))
            throw new Error('INVALID_RECONCILIATION_RESPONSE');
          setResult(response.data);
        }
      } catch (cause) {
        if (active) {
          setResult(null);
          const code = (cause as { response?: { status?: number } })?.response?.status;
          setError(
            cause instanceof Error && cause.message === 'INVALID_RECONCILIATION_RESPONSE'
              ? 'Máy chủ trả dữ liệu đối chiếu không hợp lệ. Không sử dụng dữ liệu này để điều chỉnh tồn kho.'
              : code === 403
                ? 'Bạn không có quyền xem sổ cái đối chiếu tồn kho.'
                : code === 404
                  ? 'Không tìm thấy kho đối chiếu trong phạm vi được cấp quyền.'
                  : 'Không thể tải dữ liệu đối chiếu tồn kho. Hãy kiểm tra kết nối và thử lại.',
          );
        }
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => { active = false; };
  }, [applied, demoRuntime, page]);

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
    allStatusBucketAnchor?: number, allStatusBucketAggregateFingerprint?: string,
  ) => {
    if (demoRuntime) return;
    const generation = ++investigationGeneration.current;
    setInvestigation(null);
    setInvestigationError('');
    setInvestigating(true);
    try {
      const params = new URLSearchParams({ warehouseId: String(warehouse), productId: String(product), limit: '50' });
      if (anchor !== undefined) params.set('eventAnchorId', String(anchor));
      if (before !== undefined) params.set('eventBeforeId', String(before));
      if (bucketAnchor !== undefined) params.set('bucketAnchorId', String(bucketAnchor));
      if (bucketAfter !== undefined) params.set('bucketAfterId', String(bucketAfter));
      if (bucketStatus !== 'Available') params.set('bucketStatus', bucketStatus);
      if (eventStatus !== 'Available') params.set('eventStatus', eventStatus);
      if (allStatusBucketAnchor !== undefined) params.set('allStatusBucketAnchorId', String(allStatusBucketAnchor));
      if (allStatusBucketAggregateFingerprint !== undefined) params.set('allStatusBucketAggregateFingerprint', allStatusBucketAggregateFingerprint);

      const response = await apiClient.get('/api/v1/inventory-reconciliation/investigation?' + params.toString());
      if (generation !== investigationGeneration.current) return;
      const evidence = response.data as Investigation;
      if (!isValidInvestigationEvidence(evidence, { warehouse, product, anchor, before, bucketAnchor, bucketAfter, bucketStatus, eventStatus, allStatusBucketAnchor, allStatusBucketAggregateFingerprint })) {
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
      setInvestigationError(
        status === 403
          ? 'Bạn không có quyền xem bằng chứng đối chiếu.'
          : status === 404
            ? 'Không tìm thấy bằng chứng trong kho được cấp quyền.'
            : 'Không thể tải bằng chứng điều tra. Hãy thử lại.',
      );
    } finally {
      if (generation === investigationGeneration.current) setInvestigating(false);
    }
  };

  const openInvestigation = (row: ReconciliationRow, trigger: HTMLButtonElement) => {
    investigationTrigger.current = trigger;
    void loadInvestigation(row.warehouseId, row.productId);
  };

  const changeEventPage = (direction: 'older' | 'newer') => {
    if (!investigation || investigating) return;
    const bucketAfter = bucketPageCursors[bucketPageIndex] ?? undefined;
    if (direction === 'older') {
      const next = investigation.nextEventBeforeId;
      if (!investigation.eventsTruncated || !Number.isSafeInteger(next) || !next || next <= 0) return;
      const cursors = [...eventPageCursors.slice(0, eventPageIndex + 1), next];
      void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, next, eventPageIndex + 1, cursors, investigation.bucketAnchorId, bucketAfter, bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
    } else if (eventPageIndex > 0) {
      const index = eventPageIndex - 1;
      void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, eventPageCursors[index] ?? undefined, index, eventPageCursors, investigation.bucketAnchorId, bucketAfter, bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
    }
  };

  const changeBucketPage = (direction: 'next' | 'previous') => {
    if (!investigation || investigating || investigation.bucketAnchorId === undefined) return;
    const eventBefore = eventPageCursors[eventPageIndex] ?? undefined;
    if (direction === 'next') {
      const next = investigation.nextBucketAfterId;
      if (!investigation.bucketsTruncated || !Number.isSafeInteger(next) || !next || next <= 0) return;
      const cursors = [...bucketPageCursors.slice(0, bucketPageIndex + 1), next];
      void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors, investigation.bucketAnchorId, next, bucketPageIndex + 1, cursors, investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
    } else if (bucketPageIndex > 0) {
      const index = bucketPageIndex - 1;
      void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, eventBefore, eventPageIndex, eventPageCursors, investigation.bucketAnchorId, bucketPageCursors[index] ?? undefined, index, bucketPageCursors, investigation.bucketStatus ?? 'Available', investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
    }
  };

  const changeBucketStatus = (status: string) => {
    if (!investigation || investigating || !(status in statusLabels) || status === (investigation.bucketStatus ?? 'Available')) return;
    void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, eventPageCursors[eventPageIndex] ?? undefined, eventPageIndex, eventPageCursors, undefined, undefined, 0, [null], status, investigation.eventStatus ?? 'Available', investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
  };

  const changeEventStatus = (status: string) => {
    if (!investigation || investigating || !(status in statusLabels) || status === (investigation.eventStatus ?? 'Available')) return;
    void loadInvestigation(investigation.warehouseId, investigation.productId, investigation.eventAnchorId, undefined, 0, [null], investigation.bucketAnchorId, bucketPageCursors[bucketPageIndex] ?? undefined, bucketPageIndex, bucketPageCursors, investigation.bucketStatus ?? 'Available', status, investigation.allStatusBucketAnchorId, investigation.allStatusBucketAggregateFingerprint);
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
  const mismatchCount = currentRows.filter((row) => row.status === 'Mismatch').length;
  const indeterminateCount = currentRows.filter((row) => row.status === 'Indeterminate').length;
  const absoluteDifference = currentRows.reduce((sum, row) => sum + (row.difference == null ? 0 : Math.abs(row.difference)), 0);

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
        Danh sách hiển thị riêng số liệu tồn khả dụng và kết luận đối chiếu trên cả 8 trạng thái tồn kho. Số dư hiện tại và sổ cái bất biến được tổng hợp độc lập để phục vụ điều tra đối soát; hệ thống chỉ cảnh báo chênh lệch và không tự động sửa sổ cái hay số dư khi chưa qua giao dịch đảo có phê duyệt.
      </p>

      {demoRuntime && (
        <p role="note" className="ui-muted-text">
          Đây là bộ dữ liệu đối chiếu mô phỏng riêng của giao diện Real trên Vercel, không phải kết quả từ SQL Server thật. Số liệu này không cùng snapshot với Work Center Inventory Control trong môi trường mô phỏng.
        </p>
      )}

      <UiMetricGrid>
        <UiMetric value={numberFormat.format(result?.totalRecords ?? 0)} label="Cặp kho / sản phẩm" />
        <UiMetric value={numberFormat.format(mismatchCount)} label="Lệch số dư trang hiện tại" />
        <UiMetric value={numberFormat.format(indeterminateCount)} label="Chưa xác định trang hiện tại" />
        <UiMetric value={numberFormat.format(absoluteDifference)} label="Độ lệch đã xác định" />
        {!demoRuntime && (
          <UiMetric value={numberFormat.format(currentRows.filter((row) => row.allStatusStatus === 'Mismatch').length)} label="Lệch 8 trạng thái trang hiện tại" />
        )}
      </UiMetricGrid>

      <div
        data-testid="reconciliation-traceability-tabs"
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 12, padding: 6, borderRadius: 10, background: '#f1f5f9', border: '1px solid #cbd5e1' }}
      >
        {[
          { to: '/inventory-reconciliation', label: 'Đối chiếu Tồn kho & Hàng đợi Đồng bộ Sổ cái', testId: 'tab-inventory-reconciliation', active: true },
          { to: '/inventory-traceability', label: 'Truy vết & Phả hệ Lô / Sê-ri', testId: 'tab-inventory-traceability', active: false },
        ].map((tab) => {
          const style: React.CSSProperties = {
            padding: '8px 14px', borderRadius: 8, fontWeight: 700, fontSize: 13, textDecoration: 'none',
            background: tab.active ? '#0f172a' : '#ffffff', color: tab.active ? '#ffffff' : '#334155',
            border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
          };
          return inRouter ? (
            <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>{tab.label}</Link>
          ) : (
            <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>{tab.label}</a>
          );
        })}
      </div>

      <ReconciliationGovernanceAndTable
        currentRows={currentRows}
        mismatchCount={mismatchCount}
        indeterminateCount={indeterminateCount}
        loading={loading}
        demoRuntime={demoRuntime}
        investigating={investigating}
        appliedWarehouseId={applied.warehouseId}
        outboxMessages={outboxMessages}
        outboxDispatchNotice={outboxDispatchNotice}
        setOutboxDispatchNotice={setOutboxDispatchNotice}
        rebuildNotice={rebuildNotice}
        setRebuildNotice={setRebuildNotice}
        siteReadinessVerdict={siteReadinessVerdict}
        setSiteReadinessVerdict={setSiteReadinessVerdict}
        setError={setError}
        warehouseId={warehouseId}
        setWarehouseId={setWarehouseId}
        warehouses={warehouses}
        warehouseError={warehouseError}
        productId={productId}
        setProductId={setProductId}
        productInputRef={productInputRef}
        filterError={filterError}
        keyword={keyword}
        setKeyword={setKeyword}
        error={error}
        onSubmit={submit}
        onReset={reset}
        result={result}
        page={page}
        setPage={setPage}
        onOpenInvestigation={openInvestigation}
      />

      <ReconciliationInvestigationCard
        investigating={investigating}
        investigationError={investigationError}
        investigation={investigation}
        bucketPageIndex={bucketPageIndex}
        eventPageIndex={eventPageIndex}
        onRefreshAnchor={(whId, prodId) => void loadInvestigation(whId, prodId)}
        onChangeBucketStatus={changeBucketStatus}
        onChangeBucketPage={changeBucketPage}
        onChangeEventStatus={changeEventStatus}
        onChangeEventPage={changeEventPage}
        onClose={closeInvestigation}
      />
    </UiPage>
  );
}
