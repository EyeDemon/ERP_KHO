import { useState, useEffect, useRef } from 'react';
import apiClient from '../services/apiClient';
import { currentUserId, hasPermission, usePermissionSet } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import './ExportReceipts.css';

interface ExportReceiptDetail {
  id: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitPrice?: number;
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
  if (status === 409) return 'Dữ liệu đã thay đổi. Danh sách đã được làm mới, vui lòng kiểm tra lại.';
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
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  
  // Stock cache
  const [stockCache, setStockCache] = useState<Record<string, number | null>>({});

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

  // Fetch stock logic
  useEffect(() => {
    details.forEach(d => {
      if (warehouseId !== '' && d.productId !== '') {
        const key = `${warehouseId}_${d.productId}`;
        if (stockCache[key] === undefined) {
          // Mark as fetching
          setStockCache(prev => ({ ...prev, [key]: null }));
          apiClient.get('/api/inventorystocks/current', { params: { warehouseId, productId: d.productId } })
            .then(res => {
              const stock = Array.isArray(res.data)
                ? res.data.reduce((total: number, row: { availableQuantity?: number }) => total + Number(row.availableQuantity || 0), 0)
                : 0;
              setStockCache(prev => ({ ...prev, [key]: stock }));
            })
            .catch(() => {
              setStockCache(prev => ({ ...prev, [key]: null }));
            });
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
      if (err?.response?.status === 409) await refreshAfterAction(id);
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
      if (err?.response?.status === 409) await refreshAfterAction(id);
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
    if (!code.trim() || warehouseId === '' || details.length === 0) return true;
    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '' || d.quantity === '' || Number(d.quantity) <= 0 || d.unitPrice === '' || Number(d.unitPrice) < 0) return true;
      const key = `${warehouseId}_${d.productId}`;
      const stock = stockCache[key];
      if (stock === undefined || stock === null || Number(d.quantity) > stock) return true;
    }
    return false;
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (createInFlight.current) return;
    setError('');
    setSuccessMsg('');

    if (!code.trim()) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.quantity === '' || Number(d.quantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng xuất phải > 0`);
      if (d.unitPrice === '' || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);

      const key = `${warehouseId}_${d.productId}`;
      const stock = stockCache[key];
      if (stock === undefined) return setError(`Dòng ${i + 1}: Đang tải tồn kho...`);
      if (stock === null) return setError(`Dòng ${i + 1}: Lỗi tải tồn kho!`);
      if (Number(d.quantity) > stock) {
        return setError(`Dòng ${i + 1}: Số lượng xuất (${d.quantity}) vượt quá tồn khả dụng (${stock})`);
      }
    }

    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: code.trim(),
        warehouseId: Number(warehouseId),
        customerId: customerId === '' ? null : Number(customerId),
        note,
        details: details.map(d => ({
          productId: Number(d.productId),
          quantity: Number(d.quantity),
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
    if (stock === undefined) return 'Đang tải...';
    if (stock === null) return 'Lỗi tải tồn';
    return stock;
  };

  const statusTone = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
    if (status === 'Cancelled') return 'danger';
    if (status === 'Dispatched' || status === 'Completed') return 'success';
    if (status === 'Draft' || status === 'Approved') return 'warning';
    return 'neutral';
  };

  const statusLabel = (status: string) => ({ Draft: 'Nháp', Approved: 'Đã duyệt và giữ hàng', Dispatched: 'Đã xuất kho', Cancelled: 'Đã hủy', Completed: 'Hoàn tất' }[status] || 'Không xác định');

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

        {canCreate && (
          <UiCard title="Tạo Phiếu Xuất Kho">
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

              <div className="export-section-actions">
                <button type="button" onClick={handleAddDetail}>+ Thêm dòng</button>
                <button type="submit" disabled={creating || isFormInvalid()}>
                  {creating ? 'Đang lưu...' : 'Tạo Phiếu Xuất'}
                </button>
              </div>
            </form>
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
                  <th>Trạng thái</th>
                  <th>Người tạo</th>
                  <th>Hành động</th>
                </tr>
              </thead>
              <tbody>
                {receipts.map(r => (
                  <tr key={r.id}>
                    <td><strong>{r.code}</strong></td>
                    <td>{r.warehouseName}</td>
                    <td>{r.customerCode ? r.customerCode + ' - ' + r.customerName : '—'}</td>
                    <td><UiBadge tone={statusTone(r.status)}>{statusLabel(r.status)}</UiBadge></td>
                    <td>{r.createdByName}</td>
                    <td>
                      <div className="export-row-actions">
                        <button type="button" onClick={() => handleViewDetails(r.id)}>Chi tiết</button>
                        <button type="button" disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)}>
                          {printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}
                        </button>
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
                {receipts.length === 0 && <tr><td colSpan={6} className="ui-empty-cell">Chưa có phiếu xuất nào</td></tr>}
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
                  </tr>
                </thead>
                <tbody>
                  {selectedReceipt.details.map(d => (
                    <tr key={d.id}>
                      <td>{d.productCode} - {d.productName}</td>
                      <td className="export-numeric">{d.quantity}</td>
                      <td className="export-numeric">{d.unitPrice == null ? '—' : d.unitPrice}</td>
                    </tr>
                  ))}
                  {selectedReceipt.details.length === 0 && <tr><td colSpan={3} className="ui-empty-cell">Không có chi tiết</td></tr>}
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
