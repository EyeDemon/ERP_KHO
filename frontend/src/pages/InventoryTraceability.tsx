import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useInRouterContext, useLocation } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses, type TraceabilityWarehouse } from '../services/traceabilityWarehouses';
import { UiCard, UiPage, UiPageHeader, UiToolbarField } from '../ui/ProductionUi';
import {
  InventoryTraceabilityResults,
  TraceabilityRecallOverviewCard,
} from '../modules/inventory-pages/InventoryTraceabilityResults';
import {
  errorMessage,
  validPositiveId,
  type RecallTreeSimulationResult,
  type Result,
} from '../modules/inventory-pages/inventoryTraceabilityShared';

export default function InventoryTraceability() {
  const inRouter = useInRouterContext();
  // Read URL state from the router so navigation between two reversal links
  // updates the existing screen rather than reusing stale form/query data.
  const location = useLocation();
  const initialQuery = new URLSearchParams(location.search);
  const initialReferenceId = initialQuery.get('referenceId') ?? '';
  const requestedReferenceType = initialQuery.get('referenceType');
  const linkedReferenceType =
    requestedReferenceType === 'InventoryReversal' || requestedReferenceType === 'StockTransfer'
      ? requestedReferenceType
      : null;
  const linkedDocument =
    linkedReferenceType !== null &&
    /^[1-9]\d*$/.test(initialReferenceId) &&
    Number.isSafeInteger(Number(initialReferenceId));
  const [form, setForm] = useState({
    warehouseId: '',
    productId: '',
    lotNumber: '',
    serialNumber: '',
    referenceType: linkedDocument ? (linkedReferenceType ?? '') : '',
    referenceId: linkedDocument ? initialReferenceId : '',
  });
  const [result, setResult] = useState<Result | null>(null);
  const [loading, setLoading] = useState(false);
  const [eventLimit, setEventLimit] = useState<50 | 100 | 200 | 500>(200);
  const [bucketOffset, setBucketOffset] = useState(0);
  const [eventOffset, setEventOffset] = useState(0);
  const [warehouses, setWarehouses] = useState<TraceabilityWarehouse[]>([]);
  const [warehousesLoading, setWarehousesLoading] = useState(true);
  const [warehousesError, setWarehousesError] = useState('');
  const warehouseRequestSequence = useRef(0);
  const [validationError, setValidationError] = useState('');
  const [requestError, setRequestError] = useState('');
  const [recallSimulation, setRecallSimulation] = useState<RecallTreeSimulationResult | null>(null);
  const [recallSimulating, setRecallSimulating] = useState(false);
  const productIdRef = useRef<HTMLInputElement>(null);
  const referenceTypeRef = useRef<HTMLInputElement>(null);
  const referenceIdRef = useRef<HTMLInputElement>(null);
  // One generation counter guards both automatic deep links and manual searches.
  const requestSequence = useRef(0);

  const simulateRecallTree = async () => {
    if (recallSimulating) return;
    setRecallSimulating(true);
    setRequestError('');
    const queryValue = form.lotNumber.trim() || form.serialNumber.trim() || 'LÔ-FEFO-2026';
    try {
      const response = await apiClient.post<RecallTreeSimulationResult>(
        '/api/v1/inventory/traceability/simulate-recall-tree',
        { batchOrSerialQuery: queryValue },
      );
      if (response?.data?.simulationId) {
        setRecallSimulation(response.data);
      } else {
        setRecallSimulation(null);
        setRequestError('Phản hồi mô phỏng cây thu hồi không hợp lệ từ máy chủ.');
      }
    } catch (e) {
      setRecallSimulation(null);
      setRequestError(errorMessage(e));
    } finally {
      setRecallSimulating(false);
    }
  };

  const loadWarehouses = useCallback(async () => {
    const seq = ++warehouseRequestSequence.current;
    setWarehousesLoading(true);
    setWarehousesError('');
    try {
      const accessible = await getTraceabilityWarehouses();
      if (seq === warehouseRequestSequence.current) setWarehouses(accessible);
    } catch {
      if (seq === warehouseRequestSequence.current) {
        setWarehouses([]);
        setWarehousesError('Không thể tải danh sách kho được phân quyền. Hãy thử lại.');
      }
    } finally {
      if (seq === warehouseRequestSequence.current) setWarehousesLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadWarehouses();
    return () => {
      warehouseRequestSequence.current += 1;
    };
  }, [loadWarehouses]);

  useEffect(() => {
    const sequence = ++requestSequence.current;
    setBucketOffset(0);
    setEventOffset(0);
    if (!linkedDocument) {
      // A route transition back to an unfiltered trace view must not leave
      // the previous document or warehouse filters visible as current data.
      setForm({ warehouseId: '', productId: '', lotNumber: '', serialNumber: '', referenceType: '', referenceId: '' });
      setEventLimit(200);
      setResult(null);
      setRequestError('');
      setValidationError('');
      setLoading(false);
      return;
    }
    let active = true;
    setForm({
      warehouseId: '',
      productId: '',
      lotNumber: '',
      serialNumber: '',
      referenceType: linkedReferenceType ?? '',
      referenceId: initialReferenceId,
    });
    // Linked searches use the default 200-event window; keep the UI in sync.
    setEventLimit(200);
    setValidationError('');
    setRequestError('');
    setResult(null);
    const params = new URLSearchParams({
      referenceType: linkedReferenceType ?? '',
      referenceId: initialReferenceId,
      limit: '200',
    });
    setLoading(true);
    void apiClient
      .get<Result>('/api/v1/inventory/traceability?' + params.toString())
      .then((response) => {
        if (active && sequence === requestSequence.current) setResult(response.data);
      })
      .catch((e) => {
        if (active && sequence === requestSequence.current) setRequestError(errorMessage(e));
      })
      .finally(() => {
        if (active && sequence === requestSequence.current) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [linkedDocument, linkedReferenceType, initialReferenceId]);

  const referencePairInvalid = Boolean(
    (form.referenceType.trim() && !form.referenceId) || (!form.referenceType.trim() && form.referenceId),
  );

  const invalidateResults = () => {
    // Changing filters invalidates the old results and pending responses;
    // paging must never reuse stale query conditions.
    requestSequence.current += 1;
    setBucketOffset(0);
    setEventOffset(0);
    setResult(null);
    setLoading(false);
  };

  const updateField = (field: keyof typeof form, value: string) => {
    setForm((x) => ({ ...x, [field]: value }));
    invalidateResults();
    if (validationError) setValidationError('');
    if (requestError) setRequestError('');
  };

  const requestTrace = async (offset: number, nextEventOffset = eventOffset, resetAnchor = false) => {
    if (
      loading ||
      offset < 0 ||
      offset > 50_000 ||
      offset % 500 !== 0 ||
      nextEventOffset < 0 ||
      nextEventOffset > 50_000 ||
      nextEventOffset % eventLimit !== 0
    )
      return;
    setValidationError('');
    setRequestError('');
    const hasIdentity = form.warehouseId || form.productId || form.lotNumber.trim() || form.serialNumber.trim();
    const hasReference = form.referenceType.trim() && form.referenceId;
    if (referencePairInvalid) {
      setValidationError('Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.');
      if (!form.referenceType.trim()) referenceTypeRef.current?.focus();
      else referenceIdRef.current?.focus();
      return;
    }
    if (!hasIdentity && !hasReference) {
      setValidationError('Nhập ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu.');
      productIdRef.current?.focus();
      return;
    }
    if (form.productId && !validPositiveId(form.productId)) {
      setValidationError('ID sản phẩm phải là số nguyên dương hợp lệ.');
      productIdRef.current?.focus();
      return;
    }
    if (form.referenceId && !validPositiveId(form.referenceId)) {
      setValidationError('ID tham chiếu phải là số nguyên dương hợp lệ.');
      referenceIdRef.current?.focus();
      return;
    }
    if (nextEventOffset > 0 && (resetAnchor || result?.eventAnchorId == null)) {
      setRequestError('Thiếu mốc lịch sử. Vui lòng truy vết lại từ trang đầu.');
      return;
    }
    setResult(null);
    setBucketOffset(offset);
    setEventOffset(nextEventOffset);
    setLoading(true);
    const sequence = ++requestSequence.current;
    try {
      const params = new URLSearchParams();
      if (form.warehouseId) params.set('warehouseId', form.warehouseId);
      if (form.productId) params.set('productId', form.productId);
      if (form.lotNumber.trim()) params.set('lotNumber', form.lotNumber.trim());
      if (form.serialNumber.trim()) params.set('serialNumber', form.serialNumber.trim());
      if (form.referenceType.trim()) params.set('referenceType', form.referenceType.trim());
      if (form.referenceId) params.set('referenceId', form.referenceId);
      params.set('limit', String(eventLimit));
      if (offset > 0) params.set('bucketOffset', String(offset));
      if (nextEventOffset > 0) params.set('eventOffset', String(nextEventOffset));
      // Keep the same ledger ID fence across event AND stock paging.
      // A new search explicitly requests a fresh fence instead.
      if (!resetAnchor && result?.eventAnchorId != null) params.set('eventAnchorId', String(result.eventAnchorId));
      const response = await apiClient.get<Result>('/api/v1/inventory/traceability?' + params.toString());
      if (sequence === requestSequence.current) setResult(response.data);
    } catch (e) {
      if (sequence === requestSequence.current) {
        setResult(null);
        setRequestError(errorMessage(e));
      }
    } finally {
      if (sequence === requestSequence.current) setLoading(false);
    }
  };
  const search = (e: FormEvent) => {
    e.preventDefault();
    void requestTrace(0, 0, true);
  };
  const clearFilters = () => {
    setForm({ warehouseId: '', productId: '', lotNumber: '', serialNumber: '', referenceType: '', referenceId: '' });
    setEventLimit(200);
    setValidationError('');
    setRequestError('');
    invalidateResults();
  };

  const referencePairErrorActive = Boolean(validationError && referencePairInvalid);
  const resultStatus = loading
    ? 'Đang truy vết tồn kho.'
    : result
      ? `Đã tải ${result.currentBuckets.length} nhóm tồn kho hiện tại và ${result.events.length} sự kiện sổ cái.`
      : '';
  const showExposureCards = Boolean(
    form.productId && (form.lotNumber.trim() || form.serialNumber.trim()) && !form.referenceType.trim(),
  );

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kiểm soát tồn kho"
        title="Truy vết & phả hệ tồn kho"
        description="Xem toàn bộ nhóm tồn của một kho được phép, hoặc truy theo sản phẩm, lô, sê-ri và chứng từ. Dòng thời gian sổ cái giữ nguyên lịch sử bất biến."
      />
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
          { to: '/inventory-reconciliation', label: 'Đối chiếu Tồn kho & Hàng đợi Đồng bộ Sổ cái', testId: 'tab-inventory-reconciliation', active: false },
          { to: '/inventory-traceability', label: 'Truy vết & Phả hệ Lô / Sê-ri', testId: 'tab-inventory-traceability', active: true },
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
      {requestError && <p role="alert">{requestError}</p>}
      <UiCard title="Điều kiện truy vết">
        <form
          onSubmit={search}
          noValidate
          className="ui-form-grid"
          style={{ gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }}
          aria-busy={loading}
        >
          <UiToolbarField label="Kho truy vết">
            <select
              aria-label="Kho truy vết"
              value={form.warehouseId}
              disabled={warehousesLoading || Boolean(warehousesError) || warehouses.length === 0}
              aria-describedby="traceability-warehouse-help"
              onChange={(e) => updateField('warehouseId', e.target.value)}
            >
              <option value="">Tất cả kho được phân quyền (cần thêm bộ lọc)</option>
              {warehouses.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.code} — {x.name}
                </option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="ID sản phẩm">
            <input
              ref={productIdRef}
              type="text"
              value={form.productId}
              onChange={(e) => updateField('productId', e.target.value)}
              inputMode="numeric"
              placeholder="Chọn hoặc nhập mã định danh SP"
            />
          </UiToolbarField>
          <UiToolbarField label="Mã lô">
            <input value={form.lotNumber} onChange={(e) => updateField('lotNumber', e.target.value)} autoComplete="off" />
          </UiToolbarField>
          <UiToolbarField label="Số sê-ri">
            <input value={form.serialNumber} onChange={(e) => updateField('serialNumber', e.target.value)} autoComplete="off" />
          </UiToolbarField>
          <UiToolbarField label="Loại tham chiếu">
            <input
              ref={referenceTypeRef}
              value={form.referenceType}
              onChange={(e) => updateField('referenceType', e.target.value)}
              aria-invalid={referencePairErrorActive || undefined}
              aria-describedby={referencePairErrorActive ? 'traceability-reference-help traceability-validation-error' : 'traceability-reference-help'}
              autoComplete="off"
            />
          </UiToolbarField>
          <UiToolbarField label="ID tham chiếu">
            <input
              ref={referenceIdRef}
              type="text"
              min="1"
              value={form.referenceId}
              onChange={(e) => updateField('referenceId', e.target.value)}
              aria-invalid={referencePairErrorActive || undefined}
              aria-describedby={referencePairErrorActive ? 'traceability-reference-help traceability-validation-error' : 'traceability-reference-help'}
              inputMode="numeric"
            />
          </UiToolbarField>
          <UiToolbarField label="Giới hạn sự kiện">
            <select
              aria-label="Giới hạn sự kiện truy vết"
              value={eventLimit}
              disabled={loading}
              onChange={(e) => {
                setEventLimit(Number(e.target.value) as 50 | 100 | 200 | 500);
                invalidateResults();
              }}
            >
              <option value={50}>50 sự kiện</option>
              <option value={100}>100 sự kiện</option>
              <option value={200}>200 sự kiện</option>
              <option value={500}>500 sự kiện</option>
            </select>
          </UiToolbarField>
          <div className="ui-inline-actions" style={{ alignSelf: 'end', height: 38, alignItems: 'center' }}>
            <button type="submit" className="ui-primary-button" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }} disabled={loading}>
              {loading ? 'Đang truy vết...' : 'Truy vết'}
            </button>
            <button type="button" style={{ height: 38, minHeight: 38, whiteSpace: 'nowrap' }} disabled={loading} onClick={clearFilters}>
              Xóa bộ lọc
            </button>
          </div>
          <p id="traceability-warehouse-help" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
            Chọn một kho để xem toàn bộ tồn hiện tại và sự kiện gần nhất; 500 nhóm tồn mỗi trang. Để truy vết nhiều kho, cần nhập thêm sản phẩm, lô, sê-ri hoặc chứng từ.
          </p>
          {warehousesLoading && (
            <p role="status" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
              Đang tải kho được cấp quyền...
            </p>
          )}
          {warehousesError && (
            <div className="ui-stack" style={{ gridColumn: '1 / -1' }}>
              <p role="alert">{warehousesError}</p>
              <button type="button" onClick={() => void loadWarehouses()}>
                Tải lại danh sách kho
              </button>
            </div>
          )}
          {!warehousesLoading && !warehousesError && warehouses.length === 0 && (
            <p role="status" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
              Chưa có kho được cấp quyền truy vết. Liên hệ quản trị để kiểm tra quyền truy cập.
            </p>
          )}
          <p id="traceability-reference-help" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
            Loại tham chiếu và ID tham chiếu là một cặp; nhập cả hai khi truy theo chứng từ. Kết hợp chứng từ với bộ lọc khác chỉ lấy nhóm tồn liên quan đến chứng từ đó, kể cả khi giới hạn sự kiện thấp.
          </p>
          {validationError && (
            <p id="traceability-validation-error" role="alert" style={{ gridColumn: '1 / -1', margin: 0 }}>
              {validationError}
            </p>
          )}
          {resultStatus && (
            <p role="status" aria-live="polite" className="ui-muted-text" style={{ gridColumn: '1 / -1', margin: 0 }}>
              {resultStatus}
            </p>
          )}
        </form>
      </UiCard>

      <TraceabilityRecallOverviewCard
        result={result}
        recallSimulating={recallSimulating}
        recallSimulation={recallSimulation}
        onSimulateRecallTree={() => void simulateRecallTree()}
      />

      {result && (
        <InventoryTraceabilityResults
          result={result}
          loading={loading}
          bucketOffset={bucketOffset}
          eventOffset={eventOffset}
          eventLimit={eventLimit}
          showExposureCards={showExposureCards}
          onRequestTrace={(offset, nextEvt, reset) => void requestTrace(offset, nextEvt, reset)}
        />
      )}
    </UiPage>
  );
}
