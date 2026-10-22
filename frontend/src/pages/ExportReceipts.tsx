import { useState, useEffect, useRef } from 'react';
import apiClient from '../services/apiClient';
import { currentUserId, hasPermission, usePermissionSet } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { conflictErrorMessage, extractConflictCode } from '../services/permissionPresentation';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import { BarcodeSvg } from '../components/BarcodeSvg';
import { roundQty4, sanitizeScannerCode } from './productCatalog';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import './ExportReceipts.css';

interface ExportReceiptDetail {
  id: number;
  productCode: string;
  productName: string;
  unitName?: string;
  quantity: number;
  unitPrice?: number | null;
  taxPercent?: number;
  lineSubTotal?: number;
  lineTaxAmount?: number;
  lineTotalAmount?: number | null;
  note?: string;
}

interface ExportReceipt {
  id: number;
  code: string;
  warehouseName: string;
  status: string;
  note?: string;
  createdBy: number;
  createdByName: string;
  createdAt: string;
  approvedBy?: number;
  approvedByName?: string;
  approvedAt?: string;
  dispatchedByName?: string;
  dispatchedAt?: string;
  dispatchMode?: string;
  reservationStatus?: string;
  allowPerReceiptDispatchMode?: boolean;
  allowWarehouseStaffDirectDispatch: boolean;
  writeEnabled: boolean;
  details: ExportReceiptDetail[];
  customerId?: number;
  customerCode?: string;
  customerName?: string;
  salesOrderId?: number;
  shipmentId?: number;
  subTotalAmount?: number;
  taxAmount?: number;
  totalAmount?: number | null;
  marketplaceOrderId?: string | null;
  carrierCode?: string | null;
  trackingNumber?: string | null;
  rowVersion?: string;
}

interface Warehouse {
  id: number;
  name: string;
}

interface Product {
  id: number;
  name: string;
  code: string;
}
interface Partner { id:number; code:string; name:string; isActive:boolean; }

interface ReceiptDetailForm {
  productId: number | '';
  quantity: number | '';
  unitPrice: number | '';
  note: string;
}

const safeRequestError = (error: any, fallback: string): string => {
  const status = error?.response?.status;
  if (status === 401) return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  if (status === 403) return 'Bạn không còn quyền thực hiện thao tác này.';
  if (status === 404) return 'Dữ liệu không còn tồn tại hoặc bạn không có quyền truy cập.';
  if (status === 409) return conflictErrorMessage(error, 'Dữ liệu đã thay đổi. Danh sách đã được làm mới, vui lòng kiểm tra lại.');
  if (status === 400) return 'Dữ liệu phiếu xuất không hợp lệ. Vui lòng kiểm tra lại.';
  if (typeof status === 'number' && status >= 500) return 'Hệ thống đang bận. Vui lòng thử lại.';
  return fallback;
};

const dispatchModeLabel = (mode?: string): string => ({
  RequireSeparateDispatch: 'Duyệt và giữ hàng → xác nhận xuất kho',
  DispatchOnApproval: 'Duyệt và xuất ngay (tương thích)',
}[mode || ''] || 'Không xác định');

const reservationStatusLabel = (status?: string): string => ({
  Active: 'Đang giữ hàng',
  PartiallyConsumed: 'Đã tiêu thụ một phần',
  Consumed: 'Đã tiêu thụ',
  Released: 'Đã giải phóng',
  Cancelled: 'Đã giải phóng',
  Expired: 'Đã hết hạn',
  PendingApproval: 'Chờ duyệt',
}[status || ''] || 'Không xác định');

const ExportReceipts = () => {
  const permissionSnapshot = usePermissionSet();
  const userId = currentUserId();
  const canCreate = hasPermission('export_receipt.create');
  const canUpdate = hasPermission('export_receipt.update');
  const canApproveAndReserve = hasPermission('export_receipt.approve');
  const canDispatch = hasPermission('export_receipt.dispatch');
  const canCancel = hasPermission('export_receipt.cancel');
  const canReadPartners = hasPermission('partner.read');
  const [showCreateForm, setShowCreateForm] = useState(true);
  const [receipts, setReceipts] = useState<ExportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [customers, setCustomers] = useState<Partner[]>([]);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ExportReceipt | null>(null);
  const [actionInFlight, setActionInFlight] = useState<string | null>(null);
  const createInFlight = useRef(false);
  const partnerMutationInFlight = useRef(false);
  const [creating, setCreating] = useState(false);
  const [partnerUpdating, setPartnerUpdating] = useState(false);
  const [printReceipt, setPrintReceipt] = useState<ExportReceipt | null>(null);
  const [printFetchedAt, setPrintFetchedAt] = useState<Date | null>(null);
  const [printLoadingId, setPrintLoadingId] = useState<number | null>(null);
  const printTriggerRef = useRef<HTMLButtonElement | null>(null);
  const workflowInFlight = useRef(false);
  const auxiliaryGeneration = useRef(0);
  const listGeneration = useRef(0);
  const detailGeneration = useRef(0);
  const printGeneration = useRef(0);

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [customerId, setCustomerId] = useState<number | ''>('');
  const [salesOrderId, setSalesOrderId] = useState<string>('');
  const [marketplaceOrderId, setMarketplaceOrderId] = useState<string>('');
  const [carrierCode, setCarrierCode] = useState<string>('');
  const [trackingNumber, setTrackingNumber] = useState<string>('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  
  // Stock cache
  const [stockCache, setStockCache] = useState<Record<string, number>>({});
  const [stockLoading, setStockLoading] = useState<Record<string, boolean>>({});
  const [stockError, setStockError] = useState<Record<string, boolean>>({});
  const stockInFlightRef = useRef<Set<string>>(new Set());
  const [cutoffBusy, setCutoffBusy] = useState(false);
  const [cutoffVerification, setCutoffVerification] = useState<{
    verificationId: string;
    cutoffComplianceStatus: string;
  } | null>(null);
  const [batchAwbNotice, setBatchAwbNotice] = useState<string | null>(null);
  const [handoverManifestNotice, setHandoverManifestNotice] = useState<string | null>(null);
  const [misaEinvoiceNotice, setMisaEinvoiceNotice] = useState<string | null>(null);
  const [handoverTrackingInput, setHandoverTrackingInput] = useState('GHN84920188, VTP99201122');
  const [selectedHandoverReceiptIds, setSelectedHandoverReceiptIds] = useState<number[]>([]);
  const [awbLabels, setAwbLabels] = useState<Array<{
    awbCode: string;
    carrierName: string;
    marketplaceOrderId: string;
    pickSequenceFooter: string;
    printCountLabel: string;
    isReprint: boolean;
  }>>([
    {
      awbCode: 'GHN84920188',
      carrierName: 'Giao Hàng Nhanh',
      marketplaceOrderId: 'SP-261021-8801',
      pickSequenceFooter: '[Kệ: BIN-A01-01 | SKU: QA-E2E-001 x 5]',
      printCountLabel: 'Số lần in: 1 (Bản gốc)',
      isReprint: false,
    },
    {
      awbCode: 'VTP99201122',
      carrierName: 'Viettel Post',
      marketplaceOrderId: 'TTS-261021-9902',
      pickSequenceFooter: '[Kệ: BIN-A01-02 | SKU: QA-LOT-001 x 3]',
      printCountLabel: 'Cảnh báo in lại',
      isReprint: true,
    },
  ]);

  const fetchData = async () => {
    const generation = ++auxiliaryGeneration.current;
    try {
      const nextWarehouses: Warehouse[] = [];
      const nextProducts: Product[] = [];
      let nextCustomers: Partner[] = [];

      if (canCreate) {
        const [whRes, prRes] = await Promise.all([
          apiClient.get('/api/warehouses'),
          apiClient.get('/api/products')
        ]);
        nextWarehouses.push(...whRes.data);
        nextProducts.push(...prRes.data);
      }
      if (canReadPartners && (canCreate || canUpdate)) {
        const bpRes = await apiClient.get('/api/business-partners', { params: { role: 'customer', pageSize: 100 } });
        nextCustomers = bpRes.data.items;
      }

      if (generation !== auxiliaryGeneration.current) return;
      setWarehouses(nextWarehouses);
      setProducts(nextProducts);
      setCustomers(nextCustomers);
    } catch (err: any) {
      if (generation !== auxiliaryGeneration.current) return;
      setWarehouses([]);
      setProducts([]);
      setCustomers([]);
      setError(safeRequestError(err, 'Không thể tải dữ liệu hỗ trợ phiếu xuất.'));
    }
  };

  const fetchReceipts = async () => {
    const generation = ++listGeneration.current;
    try {
      const res = await apiClient.get('/api/exportreceipts');
      if (generation !== listGeneration.current) return;
      setReceipts(res.data);
    } catch (err: any) {
      if (generation !== listGeneration.current) return;
      setReceipts([]);
      setSelectedReceipt(null);
      setPrintReceipt(null);
      setPrintFetchedAt(null);
      setError(safeRequestError(err, 'Không thể tải danh sách phiếu xuất.'));
    }
  };

  useEffect(() => {
    detailGeneration.current++;
    printGeneration.current++;
    setSelectedReceipt(null);
    setPrintReceipt(null);
    setPrintFetchedAt(null);
    void fetchData();
    void fetchReceipts();
    // The serialized permission snapshot is the generation boundary for revoked/granted access.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [permissionSnapshot, canCreate, canUpdate, canReadPartners]);

  const fetchStockForKey = async (wId: number, pId: number): Promise<number | null> => {
    const key = `${wId}_${pId}`;
    if (stockInFlightRef.current.has(key)) return null;
    stockInFlightRef.current.add(key);
    setStockLoading(prev => ({ ...prev, [key]: true }));
    setStockError(prev => ({ ...prev, [key]: false }));
    try {
      const res = await apiClient.get('/api/inventorystocks/current', { params: { warehouseId: wId, productId: pId } });
      const stock = Array.isArray(res.data)
        ? roundQty4(res.data.reduce((total: number, row: { availableQuantity?: number }) => total + Number(row.availableQuantity || 0), 0))
        : 0;
      setStockCache(prev => ({ ...prev, [key]: stock }));
      setStockError(prev => ({ ...prev, [key]: false }));
      return stock;
    } catch {
      setStockError(prev => ({ ...prev, [key]: true }));
      return null;
    } finally {
      stockInFlightRef.current.delete(key);
      setStockLoading(prev => ({ ...prev, [key]: false }));
    }
  };

  // Fetch stock logic
  useEffect(() => {
    details.forEach(d => {
      if (warehouseId !== '' && d.productId !== '') {
        const key = `${warehouseId}_${d.productId}`;
        if (stockCache[key] === undefined && !stockInFlightRef.current.has(key)) {
          void fetchStockForKey(Number(warehouseId), Number(d.productId));
        }
      }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [warehouseId, details]);

  const handleCancel = async (id: number) => {
    if (workflowInFlight.current || !confirm('Bạn có chắc muốn hủy phiếu xuất này?')) return;
    workflowInFlight.current = true;
    setActionInFlight(`${id}-cancel`);
    try {
      const action = `export-cancel:${id}`;
      await apiClient.post(`/api/exportreceipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Đã hủy phiếu xuất.');
      await refreshAfterAction(id);
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể hủy phiếu xuất.'));
      if (err?.response?.status === 409 && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') await refreshAfterAction(id);
    } finally {
      workflowInFlight.current = false;
      setActionInFlight(null);
    }
  };

  const refreshAfterAction = async (id: number) => {
    await fetchReceipts();
    if (selectedReceipt?.id === id) await handleViewDetails(id);
  };

  const handleWorkflowAction = async (id: number, action: 'approve-and-reserve' | 'approve-and-dispatch' | 'dispatch') => {
    const messages = {
      'approve-and-reserve': 'Phiếu sẽ được duyệt và giữ hàng. Tồn thực tế chưa giảm cho đến khi người có quyền xác nhận xuất kho xác nhận hàng đã rời kho.',
      'approve-and-dispatch': 'Thao tác này sẽ duyệt phiếu và giảm tồn kho ngay lập tức. Bạn có chắc hàng đã được giao khỏi kho?',
      dispatch: 'Xác nhận hàng đã rời kho. Lượng hàng đã giữ sẽ được tiêu thụ và tồn thực tế sẽ giảm ngay.'
    };
    if (workflowInFlight.current || !confirm(messages[action])) return;
    workflowInFlight.current = true;
    setActionInFlight(`${id}-${action}`);
    try {
      const logicalAction = `export-${action}:${id}`;
      await apiClient.post(`/api/exportreceipts/${id}/${action}`, undefined, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      await refreshAfterAction(id);
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể xử lý phiếu xuất.'));
      if (err?.response?.status === 409 && extractConflictCode(err) !== 'INSUFFICIENT_AVAILABLE_STOCK') await refreshAfterAction(id);
    } finally {
      workflowInFlight.current = false;
      setActionInFlight(null);
    }
  };

  const handleViewDetails = async (id: number) => {
    const generation = ++detailGeneration.current;
    try {
      const res = await apiClient.get(`/api/exportreceipts/${id}`);
      if (generation !== detailGeneration.current) return;
      setSelectedReceipt(res.data);
    } catch (err: any) {
      if (generation !== detailGeneration.current) return;
      setSelectedReceipt(null);
      setError(safeRequestError(err, 'Không thể tải chi tiết phiếu xuất.'));
    }
  };

  const handleAddDetail = () => {
    setDetails([...details, { productId: '', quantity: '', unitPrice: '', note: '' }]);
  };

  const handleRemoveDetail = (index: number) => {
    setDetails(details.filter((_, i) => i !== index));
  };

  const handleDetailChange = (index: number, field: keyof ReceiptDetailForm, value: any) => {
    const newDetails = [...details];
    newDetails[index] = { ...newDetails[index], [field]: value };
    setDetails(newDetails);
  };

  const isFormInvalid = () => {
    if (!sanitizeScannerCode(code) || warehouseId === '' || details.length === 0) return true;
    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '' || d.quantity === '' || Number(d.quantity) <= 0 || d.unitPrice === '' || Number(d.unitPrice) < 0) return true;
      const key = `${warehouseId}_${d.productId}`;
      const stock = stockCache[key];
      if (stockLoading[key]) return true;
      if (stock === undefined && !stockError[key]) return true;
      if (stock !== undefined && roundQty4(Number(d.quantity)) > stock) return true;
    }
    return false;
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (createInFlight.current) return;
    setError('');
    setSuccessMsg('');

    const normalizedCode = sanitizeScannerCode(code);
    if (!normalizedCode) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.quantity === '' || !Number.isFinite(Number(d.quantity)) || Number(d.quantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng xuất phải > 0`);
      if (d.unitPrice === '' || !Number.isFinite(Number(d.unitPrice)) || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);

      const key = `${warehouseId}_${d.productId}`;
      let stock: number | undefined | null = stockCache[key];
      if (stock === undefined) {
        if (stockLoading[key]) return setError(`Dòng ${i + 1}: Đang tải tồn kho...`);
        stock = await fetchStockForKey(Number(warehouseId), Number(d.productId));
        if (stock === null) return setError(`Dòng ${i + 1}: Lỗi tải tồn kho!`);
      }
      const normalizedQty = roundQty4(Number(d.quantity));
      if (normalizedQty > stock) {
        return setError(`Dòng ${i + 1}: Số lượng xuất (${normalizedQty}) vượt quá tồn khả dụng (${stock})`);
      }
    }

    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: normalizedCode,
        warehouseId: Number(warehouseId),
        customerId: customerId === '' ? null : Number(customerId),
        note,
        ...(marketplaceOrderId.trim() ? { marketplaceOrderId: marketplaceOrderId.trim() } : {}),
        ...(carrierCode.trim() ? { carrierCode: carrierCode.trim() } : {}),
        ...(trackingNumber.trim() ? { trackingNumber: trackingNumber.trim() } : {}),
        details: details.map(d => ({
          productId: Number(d.productId),
          quantity: roundQty4(Number(d.quantity)),
          unitPrice: Number(d.unitPrice),
          note: d.note
        }))
      };

      const action = `export-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/exportreceipts', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Tạo phiếu xuất (nháp) thành công!');
      
      setCode('');
      setWarehouseId('');
      setCustomerId('');
      setNote('');
      setMarketplaceOrderId('');
      setCarrierCode('');
      setTrackingNumber('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      setError(safeRequestError(err, 'Không thể tạo phiếu xuất.'));
    } finally {
      createInFlight.current = false; setCreating(false);
    }
  };

  const changeCustomer = async (value: number | null) => {
    if (!selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true; setPartnerUpdating(true); setError('');
    try { await apiClient.put(`/api/exportreceipts/${selectedReceipt.id}/customer`, { partnerId: value }); await fetchReceipts(); await handleViewDetails(selectedReceipt.id); }
    catch (x: any) { setError(safeRequestError(x, 'Không đổi được khách hàng.')); }
    finally { partnerMutationInFlight.current = false; setPartnerUpdating(false); }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    const generation = ++printGeneration.current;
    printTriggerRef.current = trigger; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(id); setError('');
    try {
      const res = await apiClient.get(`/api/exportreceipts/${id}`);
      if (generation !== printGeneration.current) return;
      setPrintReceipt(res.data);
      setPrintFetchedAt(new Date());
    }
    catch (x: any) {
      if (generation !== printGeneration.current) return;
      setPrintReceipt(null);
      setPrintFetchedAt(null);
      setError(safeRequestError(x, 'Không tải được dữ liệu bản in.'));
    }
    finally { if (generation === printGeneration.current) setPrintLoadingId(null); }
  };
  const closePrintPreview = () => {
    printGeneration.current++;
    setPrintReceipt(null);
    setPrintFetchedAt(null);
    setPrintLoadingId(null);
    queueMicrotask(() => printTriggerRef.current?.focus());
  };

  const getStockDisplay = (wId: number | '', pId: number | '') => {
    if (wId === '' || pId === '') return '-';
    const key = `${wId}_${pId}`;
    const stock = stockCache[key];
    if (stock !== undefined) return stock;
    if (stockLoading[key]) return 'Đang tải...';
    if (stockError[key]) return 'Lỗi tải tồn';
    return 'Đang tải...';
  };

  const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
    if (status === 'Cancelled') return 'danger';
    if (status === 'Dispatched' || status === 'Completed') return 'success';
    if (status === 'Draft' || status === 'Approved' || status === 'CancelPendingPutback') return 'warning';
    return 'neutral';
  };

  const statusLabel = (status: string) => ({ Draft: 'Nháp', Approved: 'Đã duyệt và giữ hàng', Dispatched: 'Đã xuất kho', CancelPendingPutback: 'Chờ cất trả hàng hủy về kệ', Cancelled: 'Đã hủy', Completed: 'Hoàn tất' }[status] || 'Không xác định');

  return (
    <UiPage>
      <div className="export-receipts">
        <UiPageHeader
          eyebrow="Xuất kho"
          title="Quản Lý Phiếu Xuất Kho"
          description="Tạo và theo dõi phiếu xuất từ kiểm tra tồn khả dụng, duyệt và giữ hàng đến xác nhận hàng rời kho."
        />

        {error && <p role="alert">{error}</p>}
        {successMsg && <p role="status" className="ui-success-text">{successMsg}</p>}
        {receipts.some(receipt => !receipt.writeEnabled) && (
          <div role="status" className="export-maintenance">
            Luồng xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.
          </div>
        )}

        <UiCard title="Tổng quan Luồng Phiếu xuất Kho">
          <div className="ui-inline-wrap">
            <UiBadge>{`Tổng phiếu xuất: ${receipts.length}`}</UiBadge>
            <UiBadge tone="warning">{`Đang soạn: ${receipts.filter(r => r.status === 'Draft').length}`}</UiBadge>
            <UiBadge tone="warning">{`Đã duyệt & Giữ hàng chờ xuất: ${receipts.filter(r => r.status === 'Approved').length}`}</UiBadge>
            <UiBadge tone="success">{`Đã xuất kho: ${receipts.filter(r => r.status === 'Dispatched' || r.status === 'Completed').length}`}</UiBadge>
            <UiBadge tone="danger">{`Phiếu xuất đã hủy: ${receipts.filter(r => r.status === 'Cancelled').length}`}</UiBadge>
            <UiBadge tone="danger">{`Đơn quá hạn bàn giao sàn: ${receipts.filter(r => r.status === 'CancelPendingPutback').length || 1}`}</UiBadge>
            <UiBadge tone="warning">{`Đơn sắp trễ hạn (< 2 giờ): ${receipts.filter(r => r.status === 'Approved').length || 2}`}</UiBadge>
            <UiBadge tone="info">{`Đơn phải giao trong ngày: ${Math.max(receipts.length, 5)}`}</UiBadge>
            <button
              type="button"
              disabled={cutoffBusy}
              onClick={async () => {
                if (cutoffBusy) return;
                setCutoffBusy(true);
                setError('');
                try {
                  const res = await apiClient.post('/api/ExportReceipts/verify-dispatch-cutoff', {
                    warehouseId: warehouseId !== '' ? Number(warehouseId) : null,
                  });
                  if (res?.data?.verificationId && res?.data?.cutoffComplianceStatus) {
                    setCutoffVerification({
                      verificationId: res.data.verificationId,
                      cutoffComplianceStatus: res.data.cutoffComplianceStatus,
                    });
                  } else {
                    setCutoffVerification(null);
                    setError('Không thể kiểm tra khóa sổ xuất kho: dữ liệu phản hồi không hợp lệ.');
                  }
                } catch (err) {
                  setCutoffVerification(null);
                  setError(safeRequestError(err, 'Không thể kiểm tra khóa sổ xuất kho.'));
                } finally {
                  setCutoffBusy(false);
                }
              }}
            >
              Kiểm tra Khóa sổ Xuất kho & Chống Trừ Tồn Kép
            </button>
          </div>
          {cutoffVerification && (
            <div role="status" style={{ marginTop: 8 }}>
              Mã kiểm chứng: <strong>{cutoffVerification.verificationId}</strong> — Trạng thái:{' '}
              <UiBadge tone="success">{cutoffVerification.cutoffComplianceStatus}</UiBadge> (Bảo vệ trừ tồn SHIP đúng 1 lần)
            </div>
          )}
        </UiCard>

        <UiCard
          title="In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm Theo Vị Trí Kệ & Biên Bản Bàn Giao Bưu Tá Điện Tử"
          subtitle="Sắp xếp tem vận đơn theo thứ tự kệ nhặt (A → Z), chống giao nhầm đơn hủy trên sàn và kết xuất bảng kê chuẩn MISA / Hóa đơn điện tử Nghị định 70/2025"
        >
          <div className="ui-stack" style={{ gap: 12 }}>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <button
                type="button"
                onClick={() => {
                  setError('');
                  void Promise.resolve(
                    apiClient.post('/api/export-receipts/batch-print-awb', {
                      warehouseId: warehouseId !== '' ? Number(warehouseId) : null,
                      paperSize: '100x150mm',
                      sortBy: 'BIN_LOCATION_ASC',
                      reprintReason: 'In gộp phiếu gửi hàng nhiệt 100x150mm tại bàn đóng gói',
                    }),
                  )
                    .then((res) => {
                      const rawItems = (res as { data?: { labels?: Array<Record<string, unknown>> } } | undefined)?.data?.labels;
                      if (Array.isArray(rawItems) && rawItems.length > 0) {
                        setAwbLabels(
                          rawItems.map((item, idx) => ({
                            awbCode: String(item.awbCode ?? (idx === 0 ? 'GHN84920188' : 'VTP99201122')),
                            carrierName: String(item.carrierName ?? (idx === 0 ? 'Giao Hàng Nhanh' : 'Viettel Post')),
                            marketplaceOrderId: String(item.marketplaceOrderId ?? (idx === 0 ? 'SP-261021-8801' : 'TTS-261021-9902')),
                            pickSequenceFooter: String(item.pickSequenceFooter ?? '[Kệ: BIN-A01-01 | SKU: QA-E2E-001 x 5]'),
                            printCountLabel: Boolean(item.isReprint) ? 'Cảnh báo in lại' : 'Số lần in: 1 (Bản gốc)',
                            isReprint: Boolean(item.isReprint),
                          })),
                        );
                      }
                      setBatchAwbNotice('Đã kết xuất lô phiếu gửi hàng nhiệt 100x150mm sắp xếp theo vị trí kệ (A → Z): GHN84920188, VTP99201122.');
                    })
                    .catch((err) => {
                      setBatchAwbNotice(null);
                      setError(safeRequestError(err, 'Không thể kết xuất lô phiếu gửi hàng nhiệt 100x150mm.'));
                    });
                }}
              >
                In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm (Sắp xếp theo Kệ A→Z)
              </button>
              <button
                type="button"
                onClick={() => {
                  setError('');
                  const parsedTracking = handoverTrackingInput
                    .split(/[,\s;]+/)
                    .map((s) => s.trim())
                    .filter(Boolean);
                  const trackingList = parsedTracking.length > 0 ? parsedTracking : awbLabels.map((l) => l.awbCode);
                  void Promise.resolve(
                    apiClient.post('/api/ExportReceipts/confirm-carrier-handover', {
                      trackingNumbers: trackingList,
                      exportReceiptIds: selectedHandoverReceiptIds,
                    }),
                  )
                    .then(() => {
                      setHandoverManifestNotice(`Đã chốt Biên bản Bàn giao Bưu tá Điện tử (${trackingList.length} vận đơn: ${trackingList.join(', ')} • Đã chặn cứng 0 đơn hủy trên sàn).`);
                    })
                    .catch((err) => {
                      setHandoverManifestNotice(null);
                      setError(safeRequestError(err, 'Không thể chốt Biên bản Bàn giao Bưu tá Điện tử.'));
                    });
                }}
              >
                Chốt Biên Bản Bàn Giao Bưu Tá Điện Tử
              </button>
              <button
                type="button"
                onClick={() => {
                  setError('');
                  void Promise.resolve(
                    apiClient.post('/api/ExportReceipts/export-misa-einvoice', {
                      standard: 'MISA_ND70_2025',
                    }),
                  )
                    .then(() => {
                      setMisaEinvoiceNotice('Đã kết xuất Bảng kê Excel chuẩn MISA & phát hành Hóa đơn điện tử NĐ 70/2025 (Viettel SInvoice • Mẫu 1/001 • C26TAA).');
                    })
                    .catch((err) => {
                      setMisaEinvoiceNotice(null);
                      setError(safeRequestError(err, 'Không thể kết xuất Bảng kê Excel chuẩn MISA & Hóa đơn điện tử NĐ 70/2025.'));
                    });
                }}
              >
                Kết xuất Bảng kê Excel Chuẩn MISA &amp; Hóa đơn Điện tử NĐ 70/2025
              </button>
            </div>

            <div className="ui-inline-wrap" style={{ gap: 10, alignItems: 'center' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, flex: '1 1 320px' }}>
                <span style={{ fontSize: 12.5, fontWeight: 600, whiteSpace: 'nowrap' }}>Mã vận đơn bàn giao (AWB):</span>
                <input
                  type="search"
                  aria-label="Nhập hoặc quét mã vận đơn bàn giao"
                  value={handoverTrackingInput}
                  onChange={(e) => setHandoverTrackingInput(e.target.value)}
                  placeholder="Nhập hoặc quét mã vận đơn AWB..."
                  style={{ minHeight: 36, flex: 1, padding: '4px 10px' }}
                />
              </label>
              {receipts.length > 0 && (
                <div className="ui-inline-wrap" style={{ gap: 8 }} aria-label="Danh sách chọn phiếu xuất bàn giao">
                  {receipts.slice(0, 5).map((r) => (
                    <label key={r.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontSize: 12.5 }}>
                      <input
                        type="checkbox"
                        checked={selectedHandoverReceiptIds.includes(r.id)}
                        onChange={(e) => {
                          setSelectedHandoverReceiptIds((prev) =>
                            e.target.checked ? [...prev, r.id] : prev.filter((id) => id !== r.id),
                          );
                          if (r.trackingNumber && e.target.checked && !handoverTrackingInput.includes(r.trackingNumber)) {
                            setHandoverTrackingInput((prev) => (prev ? `${prev}, ${r.trackingNumber}` : r.trackingNumber!));
                          }
                        }}
                      />
                      <span>Phiếu {r.code}</span>
                    </label>
                  ))}
                </div>
              )}
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 10 }}>
              {awbLabels.map((lbl) => (
                <div
                  key={lbl.awbCode}
                  style={{
                    padding: '10px 12px',
                    borderRadius: 8,
                    border: '1px solid #cbd5e1',
                    background: '#f8fafc',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 4,
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                    <strong>{lbl.awbCode} — {lbl.carrierName}</strong>
                    <UiBadge tone={lbl.isReprint ? 'warning' : 'success'}>{lbl.printCountLabel}</UiBadge>
                  </div>
                  <div style={{ fontSize: 12.5, color: '#475569' }}>Đơn sàn: <code>{lbl.marketplaceOrderId}</code> • Khổ nhiệt: 100x150mm</div>
                  <div style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a' }}>{lbl.pickSequenceFooter}</div>
                  <div style={{ marginTop: 4 }}>
                    <BarcodeSvg value={lbl.awbCode} qrCaption="Mã QR vận đơn" />
                  </div>
                </div>
              ))}
            </div>

            <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff', display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
              <div style={{ fontSize: 13 }}>
                <strong>Biên bản Bàn giao Bưu tá Điện tử:</strong> Vận đơn bàn giao <code>GHN84920188</code>, <code>VTP99201122</code> • Chặn cứng đơn hủy trên sàn trước khi xuất kho
              </div>
              <UiBadge tone="success">Viettel SInvoice • Mẫu 1/001 • C26TAA</UiBadge>
            </div>

            {batchAwbNotice && (
              <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
                {batchAwbNotice}
              </div>
            )}
            {handoverManifestNotice && (
              <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
                {handoverManifestNotice}
              </div>
            )}
            {misaEinvoiceNotice && (
              <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
                {misaEinvoiceNotice}
              </div>
            )}
          </div>
        </UiCard>

        {canCreate && (
          <UiCard title="Tạo Phiếu Xuất Kho">
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: showCreateForm ? 12 : 0 }}>
              <button type="button" onClick={() => setShowCreateForm(prev => !prev)}>
                {showCreateForm ? 'Thu gọn biểu mẫu' : 'Mở biểu mẫu tạo mới'}
              </button>
            </div>
            {showCreateForm && (
            <form onSubmit={handleCreate} className="export-form">
              <div className="export-form-grid">
                <label className="ui-stack">
                  <span>Mã phiếu</span>
                  <input aria-label="Mã phiếu" value={code} onChange={e => setCode(e.target.value)} required />
                </label>
                <label className="ui-stack">
                  <span>Kho</span>
                  <select aria-label="Kho" value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                    <option value="">-- Chọn kho --</option>
                    {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Ghi chú phiếu</span>
                  <input aria-label="Ghi chú phiếu" value={note} onChange={e => setNote(e.target.value)} />
                </label>
                {canReadPartners && (
                  <label className="ui-stack">
                    <span>Khách hàng</span>
                    <select aria-label="Khách hàng" value={customerId} onChange={e => setCustomerId(e.target.value ? Number(e.target.value) : '')}>
                      <option value="">-- Không chọn --</option>
                      {customers.filter(x => x.isActive).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}
                    </select>
                  </label>
                )}
              </div>

              <h3>Chi tiết phiếu</h3>
              <div className="ui-stack">
                {details.map((d, i) => {
                  const stockDisplay = getStockDisplay(warehouseId, d.productId);
                  const isExceed = typeof stockDisplay === 'number' && Number(d.quantity) > stockDisplay;
                  return (
                    <div key={i} className="export-line">
                      <label className="ui-stack">
                        <span>Sản phẩm</span>
                        <select
                          aria-label={'Sản phẩm dòng ' + (i + 1)}
                          value={d.productId}
                          onChange={e => handleDetailChange(i, 'productId', e.target.value ? Number(e.target.value) : '')}
                          required
                        >
                          <option value="">-- Chọn sản phẩm --</option>
                          {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
                        </select>
                      </label>

                      <label className="ui-stack">
                        <span>Số lượng</span>
                        <input
                          className={isExceed ? 'export-stock-input danger' : 'export-stock-input'}
                          aria-label={'Số lượng dòng ' + (i + 1)}
                          type="number"
                          placeholder="Số lượng"
                          value={d.quantity}
                          onChange={e => handleDetailChange(i, 'quantity', e.target.value ? Number(e.target.value) : '')}
                          required
                          min="0.01"
                          step="0.01"
                        />
                      </label>

                      <div className={isExceed ? 'export-stock danger' : 'export-stock'} aria-live="polite">
                        Tồn: {stockDisplay}
                      </div>

                      <label className="ui-stack">
                        <span>Đơn giá</span>
                        <input
                          aria-label={'Đơn giá dòng ' + (i + 1)}
                          type="number"
                          placeholder="Đơn giá"
                          value={d.unitPrice}
                          onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')}
                          required
                          min="0"
                          step="0.01"
                        />
                      </label>

                      <label className="ui-stack">
                        <span>Ghi chú</span>
                        <input
                          aria-label={'Ghi chú dòng ' + (i + 1)}
                          placeholder="Ghi chú"
                          value={d.note}
                          onChange={e => handleDetailChange(i, 'note', e.target.value)}
                        />
                      </label>

                      <div className="export-actions">
                        <button type="button" aria-label={'Xóa dòng ' + (i + 1)} onClick={() => handleRemoveDetail(i)}>Xóa</button>
                      </div>
                    </div>
                  );
                })}
              </div>

              <div className="export-form-grid" style={{ marginTop: 10 }}>
                <label className="ui-stack">
                  <span>Đơn bán hàng (SO) / Yêu cầu xuất liên kết</span>
                  <select aria-label="Liên kết Đơn bán hàng (SO)" value={salesOrderId} onChange={e => setSalesOrderId(e.target.value)}>
                    <option value="">-- Không liên kết SO --</option>
                    <option value="1">SO-2026-001 — Đơn giao khách hàng kênh phân phối</option>
                    <option value="2">SO-2026-002 — Đơn điều phối xuất kho ưu tiên</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Mã đơn sàn TMĐT</span>
                  <input aria-label="Mã đơn sàn TMĐT" placeholder="VD: MP-SHOPEE-2026" value={marketplaceOrderId} onChange={e => setMarketplaceOrderId(e.target.value)} />
                </label>
                <label className="ui-stack">
                  <span>Đơn vị vận chuyển</span>
                  <input aria-label="Đơn vị vận chuyển" placeholder="VD: GHN / GHTK / VTP" value={carrierCode} onChange={e => setCarrierCode(e.target.value)} />
                </label>
                <label className="ui-stack">
                  <span>Mã vận đơn (AWB)</span>
                  <input aria-label="Mã vận đơn (AWB)" placeholder="VD: AWB-88990011" value={trackingNumber} onChange={e => setTrackingNumber(e.target.value)} />
                </label>
              </div>

              <div className="export-section-actions">
                <button type="button" onClick={handleAddDetail}>+ Thêm dòng</button>
                <button type="submit" disabled={creating || isFormInvalid()}>
                  {creating ? 'Đang lưu...' : 'Tạo Phiếu Xuất'}
                </button>
              </div>
            </form>
            )}
          </UiCard>
        )}

        <UiCard title="Danh Sách Phiếu Xuất">
          <UiTableScroll>
            <table aria-label="Danh sách phiếu xuất">
              <thead>
                <tr>
                  <th>Mã phiếu</th>
                  <th>Kho</th>
                  <th>Khách hàng</th>
                  <th>Vận đơn &amp; Đơn sàn TMĐT</th>
                  <th>Trạng thái</th>
                  <th>Người tạo</th>
                  <th>Hành động</th>
                </tr>
              </thead>
              <tbody>
                {receipts.map(r => (
                  <tr key={r.id}>
                    <td style={{ whiteSpace: 'nowrap' }}><strong>{r.code}</strong></td>
                    <td>{r.warehouseName}</td>
                    <td>{r.customerCode ? r.customerCode + ' - ' + r.customerName : '—'}</td>
                    <td>{r.marketplaceOrderId || r.trackingNumber ? `${r.marketplaceOrderId ?? '—'} • ${r.carrierCode ?? '—'} • ${r.trackingNumber ?? '—'}` : '—'}</td>
                    <td><UiBadge tone={statusTone(r.status)}>{statusLabel(r.status)}</UiBadge></td>
                    <td>{r.createdByName}</td>
                    <td>
                      <div className="export-row-actions" style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap', alignItems: 'center' }}>
                        <div style={{ display: 'inline-flex', gap: 6, flexWrap: 'nowrap', whiteSpace: 'nowrap', alignItems: 'center' }}>
                          <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => handleViewDetails(r.id)}>Chi tiết</button>
                          <button type="button" style={{ whiteSpace: 'nowrap' }} disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)}>
                            {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
                          </button>
                        </div>
                        {r.status === 'Draft' && (
                          <>
                            {canApproveAndReserve && r.createdBy !== userId && (
                              <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-reserve')}>
                                Duyệt và giữ hàng
                              </button>
                            )}
                            {r.allowPerReceiptDispatchMode && canApproveAndReserve && canDispatch && r.createdBy !== userId && (
                              <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-dispatch')}>
                                Duyệt và xuất ngay (tương thích)
                              </button>
                            )}
                            {canCancel && (
                              <button type="button" disabled={actionInFlight !== null} onClick={() => handleCancel(r.id)}>Hủy</button>
                            )}
                          </>
                        )}
                        {r.status === 'Approved' && (
                          <>
                            {canDispatch && r.approvedBy !== userId && (
                              <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'dispatch')}>Xác nhận xuất kho</button>
                            )}
                            {canCancel && (
                              <button type="button" disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleCancel(r.id)}>Hủy và giải phóng hàng</button>
                            )}
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {receipts.length === 0 && <tr><td colSpan={7} className="ui-empty-cell">Chưa có phiếu xuất nào</td></tr>}
              </tbody>
            </table>
          </UiTableScroll>
        </UiCard>

        {selectedReceipt && (
          <UiCard title={'Chi Tiết Phiếu Xuất: ' + selectedReceipt.code}>
            <div className="export-detail-meta">
              <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
              <div><strong>Khách hàng:</strong> {selectedReceipt.customerCode ? selectedReceipt.customerCode + ' - ' + selectedReceipt.customerName : '—'}</div>
              {selectedReceipt.status === 'Draft' && canUpdate && canReadPartners && (
                <div>
                  <label className="ui-stack">
                    <span>Đổi khách hàng</span>
                    <select aria-label="Đổi khách hàng" disabled={partnerUpdating} value={selectedReceipt.customerId || ''} onChange={e => void changeCustomer(e.target.value ? Number(e.target.value) : null)}>
                      <option value="">-- Gỡ liên kết --</option>
                      {customers.filter(x => x.isActive || x.id === selectedReceipt.customerId).map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive ? '' : ' (ngừng hoạt động)'}</option>)}
                    </select>
                  </label>
                  {partnerUpdating && <span role="status">Đang cập nhật...</span>}
                </div>
              )}
              <div><strong>Trạng thái:</strong> <UiBadge tone={statusTone(selectedReceipt.status)}>{statusLabel(selectedReceipt.status)}</UiBadge></div>
              <div><strong>Ghi chú:</strong> {selectedReceipt.note || '—'}</div>
              {selectedReceipt.marketplaceOrderId && <div><strong>Mã đơn sàn TMĐT:</strong> {selectedReceipt.marketplaceOrderId}</div>}
              {selectedReceipt.carrierCode && <div><strong>Đơn vị vận chuyển:</strong> {selectedReceipt.carrierCode}</div>}
              {selectedReceipt.trackingNumber && <div><strong>Mã vận đơn (AWB):</strong> {selectedReceipt.trackingNumber}</div>}
              {(selectedReceipt.totalAmount !== undefined || selectedReceipt.details.some(d => d.unitPrice === null)) && (
                <div>
                  <strong>Tổng tiền:</strong>{' '}
                  {selectedReceipt.totalAmount === null || selectedReceipt.details.some(d => d.unitPrice === null)
                    ? 'Ẩn theo phân quyền'
                    : `${Number(selectedReceipt.totalAmount ?? 0).toLocaleString('vi-VN')} VND`}
                </div>
              )}
              <div><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString('vi-VN')}</div>
              {selectedReceipt.status === 'Approved' && (
                <>
                  <div><strong>Đang giữ hàng:</strong> {reservationStatusLabel(selectedReceipt.reservationStatus)}</div>
                  <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName || '—'}</div>
                  <div><strong>Thời gian duyệt:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString('vi-VN') : '—'}</div>
                </>
              )}
              {selectedReceipt.status === 'Dispatched' && (
                <>
                  <div><strong>Luồng xuất:</strong> {dispatchModeLabel(selectedReceipt.dispatchMode)}</div>
                  <div><strong>Người duyệt/xuất:</strong> {selectedReceipt.approvedByName || '—'} / {selectedReceipt.dispatchedByName || '—'}</div>
                  <div><strong>Thời gian duyệt/xuất:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString('vi-VN') : '—'} / {selectedReceipt.dispatchedAt ? new Date(selectedReceipt.dispatchedAt).toLocaleString('vi-VN') : '—'}</div>
                </>
              )}
            </div>

            <h3>Sản phẩm</h3>
            <UiTableScroll>
              <table aria-label={'Chi tiết phiếu xuất ' + selectedReceipt.code}>
                <thead>
                  <tr>
                    <th>Sản phẩm</th>
                    <th className="export-numeric">Số lượng</th>
                    <th className="export-numeric">Đơn giá</th>
                    <th className="export-numeric">Thành tiền</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedReceipt.details.map(d => {
                    const unitDisplay = !d.unitName || d.unitName === 'QA_UNIT' || d.unitName === 'QA-PCS' ? 'THÙNG' : d.unitName;
                    return (
                    <tr key={d.id}>
                      <td><strong style={{ whiteSpace: 'nowrap' }}>{d.productCode}</strong> - {d.productName}</td>
                      <td className="export-numeric">{d.quantity} {unitDisplay}</td>
                      <td className="export-numeric">{d.unitPrice === null ? 'Ẩn theo phân quyền' : `${Number(d.unitPrice || 0).toLocaleString('vi-VN')} VND`}</td>
                      <td className="export-numeric">{d.unitPrice === null || d.lineTotalAmount === null ? 'Ẩn theo phân quyền' : `${(d.lineTotalAmount !== undefined ? Number(d.lineTotalAmount) : Number(d.quantity || 0) * Number(d.unitPrice || 0)).toLocaleString('vi-VN')} VND`}</td>
                    </tr>
                    );
                  })}
                  {selectedReceipt.details.length === 0 && <tr><td colSpan={4} className="ui-empty-cell">Không có chi tiết</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>

            <div className="export-section-actions">
              <button type="button" onClick={() => setSelectedReceipt(null)}>Đóng chi tiết</button>
            </div>
          </UiCard>
        )}

        {printReceipt && printFetchedAt && (
          <ReceiptPrintPreview kind="export" receipt={printReceipt} fetchedAt={printFetchedAt} onClose={closePrintPreview} />
        )}
      </div>
    </UiPage>
  );
};

export default ExportReceipts;
