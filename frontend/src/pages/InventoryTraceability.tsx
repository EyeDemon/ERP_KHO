import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { useInRouterContext, useLocation } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { getTraceabilityWarehouses, type TraceabilityWarehouse } from '../services/traceabilityWarehouses';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import {
  InventoryTraceabilityFilterCard,
  ReconciliationTraceabilityTabsBar,
  type TraceabilityFilterFormState,
} from '../modules/inventory/InventoryTraceabilityFilterCard';
import {
  InventoryTraceabilityResults,
  TraceabilityRecallOverviewCard,
} from '../modules/inventory/InventoryTraceabilityResults';
import {
  errorMessage,
  validPositiveId,
  type RecallTreeSimulationResult,
  type Result,
} from '../modules/inventory/inventoryTraceabilityShared';

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
  const [form, setForm] = useState<TraceabilityFilterFormState>({
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
    const queryValue = form.lotNumber.trim() || form.serialNumber.trim() || 'LÔ-2026-01';
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
    requestSequence.current += 1;
    setBucketOffset(0);
    setEventOffset(0);
    setResult(null);
    setLoading(false);
  };

  const updateField = (field: keyof TraceabilityFilterFormState, value: string) => {
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
      <ReconciliationTraceabilityTabsBar inRouter={inRouter} activeRoute="traceability" />
      {requestError && <p role="alert">{requestError}</p>}

      <InventoryTraceabilityFilterCard
        form={form}
        updateField={updateField}
        warehouses={warehouses}
        warehousesLoading={warehousesLoading}
        warehousesError={warehousesError}
        onReloadWarehouses={() => void loadWarehouses()}
        eventLimit={eventLimit}
        onChangeEventLimit={(limit) => {
          setEventLimit(limit);
          invalidateResults();
        }}
        loading={loading}
        referencePairErrorActive={referencePairErrorActive}
        validationError={validationError}
        resultStatus={resultStatus}
        productIdRef={productIdRef}
        referenceTypeRef={referenceTypeRef}
        referenceIdRef={referenceIdRef}
        onSearch={search}
        onClearFilters={clearFilters}
      />

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
