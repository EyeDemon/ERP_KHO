import { useState, useEffect, useRef, useCallback } from 'react';
import apiClient from '../services/apiClient';
import { usePermission, currentUserId, hasPermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import { conflictErrorMessage, extractConflictCode, permissionError } from '../services/permissionPresentation';
import { roundQty4 } from './productCatalog';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import { formatWarehouseLabel } from '../utils/warehouseLabel';
import {
  type AsnOption, type Discrepancy, type ImportReceipt, type ImportReceiptDetail, type MacPreviewLine,
  type Partner, type PoOption, type Product, type ReasonCode, type ReceiptDetailForm, type ReceiveLineForm,
  type ResolutionForm, type Warehouse, type WarehouseLocationItem,
  buildWorkflowPayloadAndValidate, checkCompactViewport, nextShortReceiptCode,
} from '../modules/inbound/ImportReceiptTypes';
import { ImportReceiptScannerSection, ImportReceiptWorkspaceTabs } from '../modules/inbound/ImportReceiptScannerAndCreateSection';
import { ImportReceiptCreateFormCard } from '../modules/inbound/ImportReceiptCreateFormCard';
import { ImportReceiptKpiCard, ImportReceiptListCard } from '../modules/inbound/ImportReceiptListSection';
import { ImportReceiptDetailViewCard } from '../modules/inbound/ImportReceiptDetailCard';
import { useImportReceiptAuxiliaryActions } from '../modules/inbound/ImportReceiptDiscrepancySection';
import './ImportReceipts.css';

const ImportReceipts = () => {
  const canApprove = usePermission('receipt.complete'), canRead = usePermission('receipt.read');
  const canCreate = usePermission('receipt.create'), canUpdate = usePermission('receipt.update');
  const canCancel = usePermission('receipt.cancel'), canReceive = usePermission('receipt.receive');
  const canPost = usePermission('receipt.post'), canQc = usePermission('quality_inspection.execute');
  const canCompleteQc = usePermission('quality_inspection.complete'), canApproveQc = usePermission('quality_disposition.approve');
  const canReadDiscrepancy = usePermission('receiving_discrepancy.read'), canObserve = usePermission('receiving_discrepancy.create');
  const canSubmit = usePermission('receiving_discrepancy.submit'), canApproveDiscrepancy = usePermission('receiving_discrepancy.approve');
  const canRejectDiscrepancy = usePermission('receiving_discrepancy.reject'), canResolve = usePermission('receiving_discrepancy.resolve');
  const canReadReasons = usePermission('reason_code.read'), canReadWarehouses = usePermission('warehouse.read');
  const canReadProducts = usePermission('product.read'), canReadPartners = usePermission('partner.read');
  const canReadPurchaseOrders = usePermission('purchase_order.read'), canReadAsns = usePermission('asn.read');
  const userId = currentUserId();

  const [receipts, setReceipts] = useState<ImportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [suppliers, setSuppliers] = useState<Partner[]>([]);
  const [purchaseOrders, setPurchaseOrders] = useState<PoOption[]>([]);
  const [asns, setAsns] = useState<AsnOption[]>([]);
  const [error, setError] = useState(''), [successMsg, setSuccessMsg] = useState('');
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [activeWorkspaceTab, setActiveWorkspaceTab] = useState<'receipts' | 'scanner' | 'rma'>('receipts');
  const [isCompactViewport, setIsCompactViewport] = useState<boolean>(() => checkCompactViewport(640));

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const syncViewport = () => setIsCompactViewport(checkCompactViewport(640));
    const mq = typeof window.matchMedia === 'function' ? window.matchMedia('(max-width: 640px)') : null;
    window.addEventListener('resize', syncViewport);
    mq?.addEventListener?.('change', syncViewport);
    return () => {
      window.removeEventListener('resize', syncViewport);
      mq?.removeEventListener?.('change', syncViewport);
    };
  }, []);
  const [selectedReceipt, setSelectedReceipt] = useState<ImportReceipt | null>(null);
  const [receiveLines, setReceiveLines] = useState<Record<number, ReceiveLineForm>>({});
  const [qcExcludedLines, setQcExcludedLines] = useState<number[]>([]);
  const [discrepancies, setDiscrepancies] = useState<Discrepancy[]>([]);
  const [reasonCodes, setReasonCodes] = useState<ReasonCode[]>([]);
  const [resolutionForms, setResolutionForms] = useState<Record<number, ResolutionForm>>({});
  const [approvingId, setApprovingId] = useState<number | null>(null), [workflowId, setWorkflowId] = useState<number | null>(null);

  const createInFlight = useRef(false), partnerMutationInFlight = useRef(false);
  const approveInFlight = useRef<number | null>(null), cancelInFlight = useRef<number | null>(null), workflowInFlight = useRef<number | null>(null);
  const detailRequest = useRef(0), listRequest = useRef(0), referenceRequest = useRef(0), printRequest = useRef(0);
  const [creating, setCreating] = useState(false), [partnerUpdating, setPartnerUpdating] = useState(false);
  const [printReceipt, setPrintReceipt] = useState<ImportReceipt | null>(null);
  const [printFetchedAt, setPrintFetchedAt] = useState<Date | null>(null), [printLoadingId, setPrintLoadingId] = useState<number | null>(null);
  const printTriggerRef = useRef<HTMLButtonElement | null>(null), scannerContainerRef = useRef<HTMLDivElement | null>(null);

  const [code, setCode] = useState(''), [warehouseId, setWarehouseId] = useState<number | ''>(''), [note, setNote] = useState('');
  const [supplierId, setSupplierId] = useState<number | ''>(''), [purchaseOrderId, setPurchaseOrderId] = useState<string>(''), [asnId, setAsnId] = useState<string>('');
  const [warehouseLocations, setWarehouseLocations] = useState<WarehouseLocationItem[]>([]);
  const [receivingReceiptId, setReceivingReceiptId] = useState<string>(''), [receivingFromLocationId, setReceivingFromLocationId] = useState<string>('');
  const [receivingToLocationId, setReceivingToLocationId] = useState<string>(''), [receivingMoveQty, setReceivingMoveQty] = useState<number>(10);
  const [receivingWorkspaceNotice, setReceivingWorkspaceNotice] = useState<string>(''), [receivingConfirmedSignature, setReceivingConfirmedSignature] = useState<string>('');
  const [listWarehouseFilter, setListWarehouseFilter] = useState<string>(''), [listStatusFilter, setListStatusFilter] = useState<string>(''), [listSearchFilter, setListSearchFilter] = useState<string>('');
  const receivingReceiptInitialized = useRef(false);
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  const [macPreviewLines, setMacPreviewLines] = useState<MacPreviewLine[]>([]), [macPreviewLoading, setMacPreviewLoading] = useState(false);

  const focusScannerInput = useCallback(() => {
    window.requestAnimationFrame(() => {
      scannerContainerRef.current?.querySelector<HTMLInputElement>('input[type="search"]')?.focus();
    });
  }, []);

  useEffect(() => {
    if (activeWorkspaceTab === 'scanner') focusScannerInput();
  }, [activeWorkspaceTab, focusScannerInput]);

  const fetchData = useCallback(async () => {
    const request = ++referenceRequest.current;
    try {
      const [whRes, prRes, bpRes, poRes, asnRes] = await Promise.all([
        canReadWarehouses ? apiClient.get('/api/v1/warehouses') : Promise.resolve({ data: [] }),
        canReadProducts ? apiClient.get('/api/v1/products') : Promise.resolve({ data: [] }),
        canReadPartners ? apiClient.get('/api/v1/business-partners', { params: { role: 'supplier', pageSize: 100 } }) : Promise.resolve({ data: { items: [] } }),
        canReadPurchaseOrders ? Promise.resolve(apiClient.get('/api/v1/purchase-orders')).catch(() => ({ data: [] })) : Promise.resolve({ data: [] }),
        canReadAsns ? Promise.resolve(apiClient.get('/api/v1/asns')).catch(() => ({ data: [] })) : Promise.resolve({ data: [] }),
      ]);
      if (request !== referenceRequest.current) return;
      setWarehouses(
        hasPermission('warehouse.read')
          ? (whRes.data as Warehouse[]).map(w => ({ ...w, name: formatWarehouseLabel(w.name, (w as { code?: string }).code, w.id) }))
          : [],
      );
      setProducts(hasPermission('product.read') ? prRes.data : []);
      setSuppliers(hasPermission('partner.read') ? bpRes.data.items : []);
      const poRaw = Array.isArray(poRes?.data) ? poRes.data : (Array.isArray(poRes?.data?.items) ? poRes.data.items : []);
      const asnRaw = Array.isArray(asnRes?.data) ? asnRes.data : (Array.isArray(asnRes?.data?.items) ? asnRes.data.items : []);
      setPurchaseOrders(hasPermission('purchase_order.read') ? poRaw : []);
      setAsns(hasPermission('asn.read') ? asnRaw : []);
    } catch (err: any) {
      if (request !== referenceRequest.current) return;
      console.error(err);
      setWarehouses([]); setProducts([]); setSuppliers([]); setPurchaseOrders([]); setAsns([]);
      setError(permissionError(err, 'Không thể tải dữ liệu tham khảo. Vui lòng thử lại.'));
    }
  }, [canReadWarehouses, canReadProducts, canReadPartners, canReadPurchaseOrders, canReadAsns]);

  const fetchReceipts = useCallback(async () => {
    const request = ++listRequest.current;
    if (!canRead) return;
    try {
      const res = await apiClient.get('/api/v1/import-receipts');
      if (request !== listRequest.current) return;
      setReceipts(hasPermission('receipt.read') ? res.data : []);
    } catch (err: any) {
      if (request !== listRequest.current) return;
      console.error(err);
      setReceipts([]); setSelectedReceipt(null); setPrintReceipt(null);
      setError(permissionError(err, 'Không thể tải danh sách phiếu nhập. Vui lòng thử lại.'));
    }
  }, [canRead]);

  useEffect(() => {
    fetchData(); fetchReceipts();
    const references = referenceRequest, lists = listRequest;
    return () => { references.current++; lists.current++; };
  }, [fetchData, fetchReceipts]);

  useEffect(() => {
    if (!receivingReceiptInitialized.current && receipts.length > 0) {
      receivingReceiptInitialized.current = true;
      setReceivingReceiptId(String(receipts[0].id));
    }
  }, [receipts]);

  useEffect(() => {
    const targetWhId = warehouseId ? Number(warehouseId) : (warehouses[0]?.id ?? 0);
    if (!canReadWarehouses || !targetWhId) return;
    let active = true;
    void Promise.resolve(apiClient.get(`/api/v1/warehouses/${targetWhId}/locations`))
      .then(res => {
        if (!active) return;
        const rows = Array.isArray(res?.data) ? (res.data as WarehouseLocationItem[]) : [];
        setWarehouseLocations(rows);
        if (rows.length > 0) {
          const fromLoc = rows.find(l => l.locationType === 'Receiving' || l.locationType === 'Staging') ?? rows[0];
          const toLoc = rows.find(l => l.locationType === 'Storage') ?? rows[rows.length - 1];
          setReceivingFromLocationId(prev => prev || String(fromLoc.id));
          setReceivingToLocationId(prev => prev || String(toLoc.id));
        }
      })
      .catch(() => { if (active) setWarehouseLocations([]); });
    return () => { active = false; };
  }, [canReadWarehouses, warehouseId, warehouses]);

  useEffect(() => {
    const dReq = detailRequest, pReq = printRequest;
    return () => { dReq.current++; pReq.current++; };
  }, [canRead]);

  useEffect(() => {
    if (!canRead) { detailRequest.current++; setReceipts([]); setSelectedReceipt(null); setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(null); setReceiveLines({}); }
    if (!canReadDiscrepancy) { setDiscrepancies([]); setResolutionForms({}); }
    if (!canReadReasons) setReasonCodes([]);
    if (!canReadPartners) setSuppliers([]);
    if (!canReadProducts) setProducts([]);
    if (!canReadWarehouses) setWarehouses([]);
  }, [canRead, canReadDiscrepancy, canReadReasons, canReadPartners, canReadProducts, canReadWarehouses]);

  const handleViewDetails = async (id: number) => {
    if (!canRead) return;
    const request = ++detailRequest.current;
    setSelectedReceipt(null); setDiscrepancies([]); setReasonCodes([]); setResolutionForms({});
    try {
      const res = await apiClient.get(`/api/v1/import-receipts/${id}`);
      if (request !== detailRequest.current || !hasPermission('receipt.read')) return;
      setSelectedReceipt(res.data);
      receivingReceiptInitialized.current = true;
      setReceivingReceiptId(String(res.data.id));
      setQcExcludedLines([]); setDiscrepancies([]); setReasonCodes([]); setResolutionForms({});
      setReceiveLines(Object.fromEntries((res.data.details as ImportReceiptDetail[]).map(d => [d.id, { receivedQuantity: d.receivedQuantity || d.expectedQuantity, acceptedQuantity: d.requiresQc ? d.acceptedQuantity : d.expectedQuantity, damagedQuantity: d.damagedQuantity, rejectedQuantity: d.rejectedQuantity, reasonCode: d.qcDispositionReasonCode || '', note: d.qcDispositionNote || '' }])));
      const [discrepancyRes, reasonRes] = await Promise.allSettled([
        canReadDiscrepancy ? apiClient.get(`/api/v1/import-receipts/${id}/discrepancies`) : Promise.resolve({ data: [] }),
        canReadReasons ? apiClient.get('/api/v1/import-receipts/discrepancy-reasons') : Promise.resolve({ data: [] }),
      ]);
      if (request !== detailRequest.current || !hasPermission('receipt.read')) return;
      setDiscrepancies(hasPermission('receiving_discrepancy.read') && discrepancyRes.status === 'fulfilled' ? discrepancyRes.value.data : []);
      setReasonCodes(hasPermission('reason_code.read') && reasonRes.status === 'fulfilled' ? reasonRes.value.data : []);
    } catch (err: any) {
      if (request !== detailRequest.current) return;
      setSelectedReceipt(null); setDiscrepancies([]);
      setError(permissionError(err) || 'Lỗi khi tải chi tiết phiếu');
    }
  };

  const handleApprove = async (id: number) => {
    if (!canApprove || (receipts.find(r => r.id === id)?.status === 'QcCompleted' && !canApproveQc) || approveInFlight.current !== null) return;
    approveInFlight.current = id; setApprovingId(id); setError(''); setSuccessMsg('');
    try {
      const action = `import-approve:${id}`;
      await apiClient.post(`/api/v1/import-receipts/${id}/approve`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); setSuccessMsg('Duyệt thành công');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(permissionError(err) || 'Lỗi khi duyệt phiếu');
    } finally { approveInFlight.current = null; setApprovingId(null); }
  };

  const handleCancel = async (id: number) => {
    if (!canCancel || cancelInFlight.current !== null) return;
    if (!window.confirm('Bạn có chắc chắn muốn hủy phiếu nhập này?')) return;
    cancelInFlight.current = id; setError(''); setSuccessMsg('');
    try {
      const action = `import-cancel:${id}`;
      await apiClient.put(`/api/v1/import-receipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action); setSuccessMsg('Hủy thành công');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(permissionError(err) || 'Lỗi khi hủy phiếu');
    } finally { cancelInFlight.current = null; }
  };

  const {
    previewMacBeforePost, observeReceipt, submitResolution, reviewResolution,
    recountDiscrepancy, changeSupplier, openPrintPreview, closePrintPreview,
  } = useImportReceiptAuxiliaryActions({
    canObserve, canSubmit, canApproveDiscrepancy, canRejectDiscrepancy, canResolve, canUpdate, canReadPartners,
    selectedReceipt, setSelectedReceipt, receiveLines, resolutionForms, reasonCodes, setDiscrepancies,
    workflowInFlight, partnerMutationInFlight, printRequest, printTriggerRef, setWorkflowId, setPartnerUpdating,
    setPrintReceipt, setPrintFetchedAt, setPrintLoadingId, setMacPreviewLines, setMacPreviewLoading,
    setError, setSuccessMsg, fetchReceipts, handleViewDetails,
  });

  const handleAddDetail = () => setDetails([...details, { productId: '', operationUnitId: '', expectedQuantity: '', unitPrice: '', note: '' }]);
  const handleRemoveDetail = (index: number) => setDetails(details.filter((_, i) => i !== index));
  const handleDetailChange = (index: number, field: keyof ReceiptDetailForm, value: any) => {
    const newDetails = [...details];
    newDetails[index] = { ...newDetails[index], [field]: value };
    setDetails(newDetails);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canCreate || createInFlight.current) return;
    setError(''); setSuccessMsg('');
    if (!code.trim()) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');
    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.expectedQuantity === '' || !Number.isFinite(Number(d.expectedQuantity)) || Number(d.expectedQuantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng dự kiến phải > 0`);
      if (d.operationUnitId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn Đơn vị thao tác`);
      if (d.unitPrice === '' || !Number.isFinite(Number(d.unitPrice)) || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);
    }
    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: code.trim(), warehouseId: Number(warehouseId), supplierId: supplierId === '' ? null : Number(supplierId),
        ...(purchaseOrderId !== '' ? { purchaseOrderId: Number(purchaseOrderId) } : {}),
        ...(asnId !== '' ? { asnId: Number(asnId) } : {}),
        note, details: details.map(d => ({ productId: Number(d.productId), operationUnitId: Number(d.operationUnitId), expectedQuantity: Number(d.expectedQuantity), unitPrice: Number(d.unitPrice), note: d.note })),
      };
      const action = `import-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/v1/import-receipts', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Tạo phiếu nháp thành công!');
      setCode(''); setWarehouseId(''); setSupplierId(''); setPurchaseOrderId(''); setAsnId(''); setNote(''); setDetails([]);
      fetchReceipts();
    } catch (err: any) {
      setError(permissionError(err) || 'Lỗi khi tạo phiếu nhập');
    } finally { createInFlight.current = false; setCreating(false); }
  };

  const runWorkflow = async (id: number, command: 'receive' | 'qc-disposition' | 'post', receipt?: ImportReceipt) => {
    if (command === 'receive' ? !canReceive : command === 'qc-disposition' ? !canQc : !canPost) return;
    if (workflowInFlight.current !== null) return;
    workflowInFlight.current = id; setWorkflowId(id); setError('');
    try {
      const action = `import-${command}:${id}`;
      const source = !receipt && (command === 'receive' || command === 'qc-disposition' || command === 'post') ? (await apiClient.get(`/api/v1/import-receipts/${id}`)).data as ImportReceipt : receipt;
      const { body, earlyError } = buildWorkflowPayloadAndValidate(command, source, receiveLines, qcExcludedLines, canCompleteQc);
      if (earlyError) return setError(earlyError);
      if (command === 'post') {
        const available = roundQty4(source?.details.reduce((sum, line) => sum + line.baseAcceptedQuantity, 0) ?? 0);
        const damaged = roundQty4(source?.details.reduce((sum, line) => sum + line.damagedQuantity * line.conversionFactor, 0) ?? 0);
        const rejected = roundQty4(source?.details.reduce((sum, line) => sum + line.rejectedQuantity * line.conversionFactor, 0) ?? 0);
        if (!window.confirm(`Ghi nhận tồn kho sẽ tăng lượng có thể sử dụng ${available}, hư hỏng ${damaged}, hàng bị từ chối ${rejected} theo đơn vị cơ sở đã lưu. Tiếp tục?`)) return;
      }
      await apiClient.post(`/api/v1/import-receipts/${id}/${command}`, body, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(command === 'receive' ? 'Đã hoàn tất nhận hàng. Tồn kho chưa thay đổi.' : command === 'qc-disposition' ? 'Đã ghi nhận kết quả kiểm tra chất lượng. Tồn kho chưa thay đổi.' : 'Đã ghi nhận tồn kho phiếu theo từng trạng thái tồn kho.');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) {
      if (err.response && selectedReceipt?.id === id && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') setSelectedReceipt(null);
      if (['CONCURRENCY_CONFLICT', 'INVALID_STATE_TRANSITION'].includes(extractConflictCode(err) || '')) void fetchReceipts();
      setError(err.response?.status === 409 ? conflictErrorMessage(err, 'Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.') : permissionError(err) || 'Không thể xử lý phiếu nhập');
    } finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const selectedPoObj = purchaseOrders.find(po => String(po.id) === purchaseOrderId);
  const filteredAsns = purchaseOrderId
    ? asns.filter(asn => (asn.purchaseOrderId != null && String(asn.purchaseOrderId) === purchaseOrderId) || (selectedPoObj && asn.purchaseOrderCode && asn.purchaseOrderCode === selectedPoObj.code) || (asn.purchaseOrderId == null && !asn.purchaseOrderCode))
    : asns;
  const currentReceivingSignature = `${receivingReceiptId}|${receivingFromLocationId}|${receivingToLocationId}|${receivingMoveQty}`;
  const isReceivingAlreadyConfirmed = receivingConfirmedSignature !== '' && receivingConfirmedSignature === currentReceivingSignature;
  const receivingDestLocation = warehouseLocations.find(l => String(l.id) === receivingToLocationId)?.code || '';

  return (
    <UiPage>
      <div className="import-receipts" style={{ minWidth: 0, maxWidth: '100%' }}>
        <style>{`@media (max-width: 768px) { .import-receipts .ui-mobile-card-list { display: grid !important; } .import-receipts .ui-desktop-table-wrap { display: none !important; } .import-receipts .import-row-actions-mobile { flex-wrap: nowrap !important; } .import-receipts .import-compact-bar input, .import-receipts .import-compact-bar button { min-height: 40px !important; height: 40px !important; } .import-receipts .import-compact-header button { min-height: 36px !important; height: 36px !important; } }`}</style>
        {isCompactViewport ? (
          <div className="import-compact-header no-print" style={{ display: 'flex', flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 6, padding: 0, margin: '0 0 6px 0' }}>
            <h1 style={{ margin: 0, fontSize: 16, fontWeight: 700, lineHeight: 1.25, color: '#0f172a', whiteSpace: 'normal' }}>
              Quản lý nhập kho
            </h1>
            <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
              <button
                type="button"
                data-testid="tab-import-scanner"
                className="ui-btn ui-btn-secondary"
                onClick={() => setActiveWorkspaceTab(activeWorkspaceTab === 'scanner' ? 'receipts' : 'scanner')}
                style={{ padding: '0 10px', height: 36, minHeight: 36, fontSize: 12.5, whiteSpace: 'nowrap' }}
              >
                {activeWorkspaceTab === 'scanner' ? `Danh sách (${receipts.length})` : 'Trạm quét'}
              </button>
              {canCreate && activeWorkspaceTab === 'receipts' && (
                <button
                  type="button"
                  data-testid="btn-header-toggle-create-import"
                  className="ui-btn ui-btn-primary"
                  onClick={() => {
                    if (!showCreateForm && !code.trim()) setCode(nextShortReceiptCode(receipts));
                    setShowCreateForm(v => !v);
                  }}
                  style={{ padding: '0 10px', height: 36, minHeight: 36, fontSize: 12.5, fontWeight: 600, background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', whiteSpace: 'nowrap' }}
                >
                  {showCreateForm ? 'Thu gọn' : '+ Tạo phiếu'}
                </button>
              )}
            </div>
          </div>
        ) : (
          <UiPageHeader
            eyebrow="Nhập kho"
            title="Quản lý nhập kho"
            description="Tạo và theo dõi phiếu nhập từ nhận hàng, xử lý sai lệch, kiểm tra chất lượng đến ghi nhận tồn kho."
            actions={
              canCreate && activeWorkspaceTab === 'receipts' ? (
                <button type="button" data-testid="btn-header-toggle-create-import" className="ui-btn ui-btn-primary" onClick={() => { if (!showCreateForm && !code.trim()) setCode(nextShortReceiptCode(receipts)); setShowCreateForm(v => !v); }} style={{ background: '#1d4ed8', color: '#ffffff', borderColor: '#1d4ed8', fontWeight: 600 }}>
                  {showCreateForm ? 'Thu gọn biểu mẫu' : '+ Tạo phiếu nhập'}
                </button>
              ) : undefined
            }
          />
        )}

        {error && <p role="alert">{error}</p>}
        {successMsg && !successMsg.includes('RMA') && !successMsg.includes('hàng hoàn trả') && !successMsg.includes('hậu cần thu hồi') && (
          <div role="status" className="ui-success-text" style={{ marginTop: 6, padding: '8px 12px', borderRadius: 8, border: '1px solid #a7f3d0', background: '#ecfdf5', color: '#065f46', fontSize: 13, fontWeight: 500 }}>{successMsg}</div>
        )}

        {!isCompactViewport && (
          <ImportReceiptWorkspaceTabs activeWorkspaceTab={activeWorkspaceTab} setActiveWorkspaceTab={setActiveWorkspaceTab} receiptsCount={receipts.length} setReceivingWorkspaceNotice={setReceivingWorkspaceNotice} setReceivingConfirmedSignature={setReceivingConfirmedSignature} />
        )}

        <ImportReceiptScannerSection
          scannerContainerRef={scannerContainerRef} activeWorkspaceTab={activeWorkspaceTab} warehouseId={warehouseId} warehouses={warehouses} receipts={receipts} creating={creating} workflowId={workflowId}
          receivingReceiptId={receivingReceiptId} setReceivingReceiptId={setReceivingReceiptId} receivingFromLocationId={receivingFromLocationId} setReceivingFromLocationId={setReceivingFromLocationId}
          receivingToLocationId={receivingToLocationId} setReceivingToLocationId={setReceivingToLocationId} receivingMoveQty={receivingMoveQty} setReceivingMoveQty={setReceivingMoveQty}
          receivingWorkspaceNotice={receivingWorkspaceNotice} setReceivingWorkspaceNotice={setReceivingWorkspaceNotice} setReceivingConfirmedSignature={setReceivingConfirmedSignature}
          currentReceivingSignature={currentReceivingSignature} isReceivingAlreadyConfirmed={isReceivingAlreadyConfirmed} warehouseLocations={warehouseLocations}
          selectedReceipt={selectedReceipt} setSelectedReceipt={setSelectedReceipt} handleViewDetails={handleViewDetails} focusScannerInput={focusScannerInput} setError={setError}
        />

        <div className="no-print" style={activeWorkspaceTab === 'receipts' ? { display: 'flex', flexDirection: 'column', gap: isCompactViewport ? 6 : 16 } : { display: 'none' }}>
          {!isCompactViewport && <ImportReceiptKpiCard receipts={receipts} />}
          {canCreate && (!isCompactViewport || showCreateForm) && (
            <ImportReceiptCreateFormCard
              showCreateForm={showCreateForm} setShowCreateForm={setShowCreateForm} code={code} setCode={setCode} warehouseId={warehouseId} setWarehouseId={setWarehouseId} note={note} setNote={setNote}
              supplierId={supplierId} setSupplierId={setSupplierId} purchaseOrderId={purchaseOrderId} setPurchaseOrderId={setPurchaseOrderId} asnId={asnId} setAsnId={setAsnId} details={details} setDetails={setDetails}
              receipts={receipts} warehouses={warehouses} suppliers={suppliers} products={products} purchaseOrders={purchaseOrders} asns={asns} filteredAsns={filteredAsns}
              creating={creating} handleCreate={handleCreate} handleAddDetail={handleAddDetail} handleRemoveDetail={handleRemoveDetail} handleDetailChange={handleDetailChange}
            />
          )}
          <ImportReceiptListCard
            receipts={receipts} warehouses={warehouses} selectedReceipt={selectedReceipt} listWarehouseFilter={listWarehouseFilter} setListWarehouseFilter={setListWarehouseFilter}
            listStatusFilter={listStatusFilter} setListStatusFilter={setListStatusFilter} listSearchFilter={listSearchFilter} setListSearchFilter={setListSearchFilter}
            printLoadingId={printLoadingId} workflowId={workflowId} setWorkflowId={setWorkflowId} approvingId={approvingId} userId={userId}
            canObserve={canObserve} canCancel={canCancel} canApprove={canApprove} canApproveQc={canApproveQc} canPost={canPost} canReceive={canReceive}
            receivingDestLocation={receivingDestLocation} handleViewDetails={handleViewDetails} openPrintPreview={openPrintPreview} observeReceipt={observeReceipt}
            handleCancel={handleCancel} handleApprove={handleApprove} runWorkflow={runWorkflow} fetchData={async () => { await fetchReceipts(); await fetchData(); }} setError={setError} setSuccessMsg={setSuccessMsg}
          />
        </div>

        {selectedReceipt && (
          <ImportReceiptDetailViewCard
            selectedReceipt={selectedReceipt} activeWorkspaceTab={activeWorkspaceTab} canUpdate={canUpdate} canReadPartners={canReadPartners} partnerUpdating={partnerUpdating} suppliers={suppliers} changeSupplier={changeSupplier}
            canReceive={canReceive} canObserve={canObserve} canQc={canQc} receiveLines={receiveLines} setReceiveLines={setReceiveLines} qcExcludedLines={qcExcludedLines} setQcExcludedLines={setQcExcludedLines}
            macPreviewLines={macPreviewLines} macPreviewLoading={macPreviewLoading} previewMacBeforePost={previewMacBeforePost} workflowId={workflowId} observeReceipt={observeReceipt}
            canReadDiscrepancy={canReadDiscrepancy} discrepancies={discrepancies} resolutionForms={resolutionForms} setResolutionForms={setResolutionForms} reasonCodes={reasonCodes}
            canResolve={canResolve} canSubmit={canSubmit} canApproveDiscrepancy={canApproveDiscrepancy} canRejectDiscrepancy={canRejectDiscrepancy} recountDiscrepancy={recountDiscrepancy}
            submitResolution={submitResolution} reviewResolution={reviewResolution} runWorkflow={runWorkflow} setSelectedReceipt={setSelectedReceipt}
          />
        )}

        {printReceipt && printFetchedAt && (
          <ReceiptPrintPreview kind="import" receipt={{ ...printReceipt, details: printReceipt.details.map(d => ({ ...d, unitName: d.operationUnitCode, quantity: d.postedQuantity || d.receivedQuantity || d.expectedQuantity })) }} fetchedAt={printFetchedAt} onClose={closePrintPreview} />
        )}
      </div>
    </UiPage>
  );
};

export default ImportReceipts;
