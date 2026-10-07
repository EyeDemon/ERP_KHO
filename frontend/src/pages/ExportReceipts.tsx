import { useState, useEffect, useLayoutEffect, useRef, useCallback } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermissionSet, currentUserId, currentPermissions } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';

interface ExportReceiptDetail {
  id: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitPrice?: number;
  unitName?: string;
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
  approvedByName?: string;
  approvedBy?: number;
  rowVersion?: string;
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

const ExportReceipts = () => {
  const permissionSet = usePermissionSet();
  const canRead = hasPermission('export_receipt.read');
  const canCreate = hasPermission('export_receipt.create');
  const canUpdate = hasPermission('export_receipt.update');
  const canDispatch = hasPermission('export_receipt.dispatch');
  const canCancel = hasPermission('export_receipt.cancel');
  const epoch = useRef(0);
  const actionGuard = useRef(false);
  const detailRequest = useRef(0);
  const printRequest = useRef(0);
  const listRequest = useRef(0);
  const userId = currentUserId();
  const canApproveAndReserve = hasPermission('export_receipt.approve');
  const [receipts, setReceipts] = useState<ExportReceipt[]>([]);
  const [listLoading, setListLoading] = useState(false);
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

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [customerId, setCustomerId] = useState<number | ''>('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  
  // Stock cache
  const [stockCache, setStockCache] = useState<Record<string, number | null>>({});

  const fetchData = useCallback(async () => {
    const permissions = new Set(currentPermissions(permissionSet));
    const generation = epoch.current;
    const current = () => generation === epoch.current;
    const loads = [
      permissions.has('warehouse.read') && canCreate ? apiClient.get('/api/warehouses').then(r => { if (current()) setWarehouses(r.data); }) : Promise.resolve(),
      permissions.has('product.read') && canCreate ? apiClient.get('/api/products').then(r => { if (current()) setProducts(r.data); }) : Promise.resolve(),
      permissions.has('partner.read') && (canCreate || canUpdate) ? apiClient.get('/api/business-partners', { params: { role: 'customer', pageSize: 100 } }).then(r => { if (current()) setCustomers(r.data.items); }) : Promise.resolve(),
    ];
    const results = await Promise.allSettled(loads);
    if (current() && results.some(r => r.status === 'rejected')) setError('Không thể tải một số dữ liệu tham khảo. Vui lòng thử lại.');
  }, [permissionSet, canCreate, canUpdate]);

  const fetchReceipts = useCallback(async () => {
    if (!canRead || !currentPermissions(permissionSet).includes('export_receipt.read')) return;
    const generation = epoch.current, request = ++listRequest.current;
    setListLoading(true);
    try {
      const res = await apiClient.get('/api/exportreceipts');
      if (generation === epoch.current && request === listRequest.current && hasPermission('export_receipt.read')) setReceipts(res.data);
    } catch (err) {
      if (generation === epoch.current && request === listRequest.current) setError(permissionError(err, 'Không thể tải danh sách phiếu xuất.'));
    } finally {
      if (generation === epoch.current && request === listRequest.current) setListLoading(false);
    }
  }, [permissionSet, canRead]);

  useLayoutEffect(() => {
    const requests = epoch;
    requests.current++;
    setReceipts([]); setSelectedReceipt(null); setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(null);
    setWarehouses([]); setProducts([]); setCustomers([]); setStockCache({});
    setCode(''); setNote(''); setDetails([]); setWarehouseId(''); setCustomerId('');
    setError(''); setSuccessMsg('');
    return () => { requests.current++; };
  }, [permissionSet]);
  useEffect(() => {
    document.title = 'Phiếu xuất kho — ERP KHO';
    void fetchData(); void fetchReceipts();
  }, [fetchData, fetchReceipts]);

  // Fetch stock logic
  useEffect(() => {
    details.forEach(d => {
      if (warehouseId !== '' && d.productId !== '') {
        const key = `${warehouseId}_${d.productId}`;
        if (stockCache[key] === undefined) {
          // Mark as fetching
          setStockCache(prev => ({ ...prev, [key]: null }));
          const generation = epoch.current;
          apiClient.get('/api/inventorystocks/current', { params: { warehouseId, productId: d.productId } })
            .then(res => {
              if (generation !== epoch.current) return;
              const stock = (res.data && res.data.length > 0) ? res.data[0].availableQuantity : 0;
              setStockCache(prev => ({ ...prev, [key]: stock }));
            })
            .catch(() => {
              if (generation !== epoch.current) return;
              setStockCache(prev => ({ ...prev, [key]: null }));
            });
        }
      }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [warehouseId, details]);

  const refreshAfterAction = async (id: number) => {
    await fetchReceipts();
    if (selectedReceipt?.id === id) await handleViewDetails(id);
  };
  const handleCancel = (id: number) => handleWorkflowAction(id, 'cancel');
  const handleWorkflowAction = async (id: number, action: 'approve-and-reserve' | 'approve-and-dispatch' | 'dispatch' | 'cancel') => {
    const required = action === 'dispatch' ? canDispatch : action === 'cancel' ? canCancel : canApproveAndReserve && (action !== 'approve-and-dispatch' || canDispatch);
    if (!required || actionGuard.current) return;
    const messages = {
      'approve-and-reserve': 'Duyệt và giữ hàng cho phiếu này? Tồn thực tế chưa giảm.',
      'approve-and-dispatch': 'Duyệt và xuất ngay là luồng tương thích. Xác nhận hàng đã rời kho?',
      dispatch: 'Xác nhận hàng đã rời kho? Tồn thực tế sẽ giảm.',
      cancel: 'Hủy phiếu xuất này và giải phóng hàng đang giữ?',
    };
    if (!confirm(messages[action])) return;
    actionGuard.current = true; setActionInFlight(`${id}-${action}`); setError('');
    const generation = epoch.current;
    try {
      const logicalAction = `export-${action}:${id}`;
      const rowVersion = receipts.find(r => r.id === id)?.rowVersion || selectedReceipt?.rowVersion;
      await apiClient.post(`/api/exportreceipts/${id}/${action}`, { rowVersion }, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      if (generation === epoch.current) await refreshAfterAction(id);
    } catch (err) {
      if (generation === epoch.current) { setError(permissionError(err, 'Không thể xử lý phiếu xuất.')); await fetchReceipts(); setSelectedReceipt(null); }
    } finally {
      actionGuard.current = false; setActionInFlight(null);
    }
  };

  const handleViewDetails = async (id: number) => {
    if (!hasPermission('export_receipt.read')) return;
    const generation = epoch.current, request = ++detailRequest.current;
    setSelectedReceipt(null);
    try {
      const res = await apiClient.get(`/api/exportreceipts/${id}`);
      if (generation === epoch.current && request === detailRequest.current && hasPermission('export_receipt.read')) setSelectedReceipt(res.data);
    } catch (err: any) {
      if (generation === epoch.current) setError(permissionError(err, 'Không thể tải chi tiết phiếu.'));
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
      if (typeof stock === 'number' && Number(d.quantity) > stock) return true;
    }
    return false;
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canCreate || createInFlight.current) return;
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
      if (typeof stock === 'number' && Number(d.quantity) > stock) {
        return setError(`Dòng ${i + 1}: Số lượng xuất (${d.quantity}) vượt quá tồn khả dụng (${stock})`);
      }
    }

    createInFlight.current = true; setCreating(true);
    const generation = epoch.current;
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
      if (generation !== epoch.current) return;
      setSuccessMsg('Tạo phiếu xuất nháp thành công.');
      
      setCode('');
      setWarehouseId('');
      setCustomerId('');
      setNote('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      if (generation === epoch.current) setError(permissionError(err, 'Không thể tạo phiếu xuất.'));
    } finally {
      createInFlight.current = false; setCreating(false);
    }
  };

  const changeCustomer = async (value: number | null) => {
    if (!canUpdate || !selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true; setPartnerUpdating(true); setError('');
    const generation = epoch.current;
    const action = `export-customer:${selectedReceipt.id}:${value}`;
    try { await apiClient.put(`/api/exportreceipts/${selectedReceipt.id}/customer`, { partnerId: value, rowVersion: selectedReceipt.rowVersion }, { headers: idempotencyHeaders(action) }); completeIdempotentAction(action); if (generation !== epoch.current) return; await fetchReceipts(); await handleViewDetails(selectedReceipt.id); }
    catch (x: any) { if (generation === epoch.current) setError(permissionError(x, 'Không đổi được khách hàng.')); }
    finally { partnerMutationInFlight.current = false; setPartnerUpdating(false); }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    if (!hasPermission('export_receipt.read')) return;
    const generation = epoch.current, request = ++printRequest.current;
    printTriggerRef.current = trigger; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(id); setError('');
    try { const res = await apiClient.get(`/api/exportreceipts/${id}`); if (generation === epoch.current && request === printRequest.current && hasPermission('export_receipt.read')) { setPrintReceipt(res.data); setPrintFetchedAt(new Date()); } }
    catch (x: any) { if (generation === epoch.current) setError(permissionError(x, 'Không tải được dữ liệu bản in.')); }
    finally { if (generation === epoch.current && request === printRequest.current) setPrintLoadingId(null); }
  };
  const closePrintPreview = () => { printRequest.current++; setPrintReceipt(null); setPrintFetchedAt(null); queueMicrotask(() => printTriggerRef.current?.focus()); };

  const getStockDisplay = (wId: number | '', pId: number | '') => {
    if (wId === '' || pId === '') return '-';
    const key = `${wId}_${pId}`;
    const stock = stockCache[key];
    if (stock === undefined) return 'Đang tải...';
    if (stock === null) return 'Lỗi tải tồn';
    return stock;
  };

  if (!canRead) return <div><h2>Quản Lý Phiếu Xuất Kho</h2><p role="alert">Bạn không có quyền thực hiện thao tác này.</p></div>;
  const statusLabel = (status: string) => ({ Draft: 'Bản nháp', Approved: 'Đã duyệt và giữ hàng', Dispatched: 'Đã xuất kho', Cancelled: 'Đã hủy' }[status] || 'Chưa xác định');
  return (
    <div>
      <h2>Quản Lý Phiếu Xuất Kho</h2>
      {error && <div role="alert" style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}
      {successMsg && <div role="status" style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      {receipts.some(receipt => !receipt.writeEnabled) && <div role="status" style={{ color: '#92400e', marginBottom: '10px' }}>Luồng xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.</div>}
      
      {canCreate && <div style={{ marginBottom: '30px', padding: '15px', border: '1px solid #ccc', borderRadius: '5px' }}>
        <h3>Tạo Phiếu Xuất Kho</h3>
        <form onSubmit={handleCreate}>
          <div style={{ display: 'flex', gap: '15px', marginBottom: '15px' }}>
            <div>
              <label style={{ display: 'block' }}>Mã phiếu</label>
              <input aria-label="Mã phiếu" value={code} onChange={e => setCode(e.target.value)} required />
            </div>
            <div>
              <label style={{ display: 'block' }}>Kho</label>
              <select aria-label="Kho" value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                <option value="">-- Chọn kho --</option>
                {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
              </select>
            </div>
            <div>
              <label style={{ display: 'block' }}>Ghi chú phiếu</label>
              <input aria-label="Ghi chú phiếu" value={note} onChange={e => setNote(e.target.value)} />
            </div>
            <div><label style={{display:'block'}}>Khách hàng</label><select aria-label="Khách hàng" value={customerId} onChange={e=>setCustomerId(e.target.value?Number(e.target.value):'')}><option value="">-- Không chọn --</option>{customers.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}</select></div>
          </div>

          <h4>Chi tiết phiếu</h4>
          {details.map((d, i) => {
            const stockDisplay = getStockDisplay(warehouseId, d.productId);
            const isExceed = typeof stockDisplay === 'number' && Number(d.quantity) > stockDisplay;
            return (
              <div key={i} style={{ display: 'flex', gap: '10px', marginBottom: '10px', alignItems: 'center' }}>
                <select 
                  aria-label={`Sản phẩm dòng ${i + 1}`}
                  value={d.productId} 
                  onChange={e => handleDetailChange(i, 'productId', e.target.value ? Number(e.target.value) : '')}
                  required
                >
                  <option value="">-- Chọn sản phẩm --</option>
                  {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
                </select>
                
                <input 
                  type="number" 
                  placeholder="Số lượng" 
                  aria-label={`Số lượng dòng ${i + 1}`}
                  value={d.quantity} 
                  onChange={e => handleDetailChange(i, 'quantity', e.target.value ? Number(e.target.value) : '')}
                  required
                  min="0.0001"
                  step="any"
                  style={{ borderColor: isExceed ? 'red' : undefined }}
                />
                
                <span style={{ fontSize: '14px', minWidth: '100px', color: isExceed ? 'red' : 'inherit' }}>
                  (Tồn: {stockDisplay})
                </span>
                
                <input 
                  type="number" 
                  placeholder="Đơn giá" 
                  aria-label={`Đơn giá dòng ${i + 1}`}
                  value={d.unitPrice} 
                  onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')}
                  required
                  min="0"
                  step="0.01"
                />

                <input 
                  placeholder="Ghi chú" 
                  aria-label={`Ghi chú dòng ${i + 1}`}
                  value={d.note} 
                  onChange={e => handleDetailChange(i, 'note', e.target.value)}
                />

                <button type="button" onClick={() => handleRemoveDetail(i)}>Xóa</button>
              </div>
            );
          })}
          
          <button type="button" onClick={handleAddDetail} style={{ marginBottom: '15px' }}>+ Thêm dòng</button>
          
          <div style={{ marginTop: '15px' }}>
            <button 
              type="submit" 
              disabled={creating || isFormInvalid()}
              style={{ 
                backgroundColor: creating || isFormInvalid() ? '#95a5a6' : '#2ecc71',
                color: '#fff', 
                padding: '10px 20px', 
                border: 'none', 
                cursor: creating || isFormInvalid() ? 'not-allowed' : 'pointer'
              }}
            >
              {creating ? 'Đang lưu...' : 'Tạo Phiếu Xuất'}
            </button>
          </div>
        </form>
      </div>}

      <hr style={{ margin: '30px 0' }} />

      <h3>Danh Sách Phiếu Xuất</h3>
      {listLoading && <p role="status">Đang tải danh sách phiếu xuất...</p>}
      <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: '30px' }}>
        <thead>
          <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã phiếu</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Khách hàng</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Người tạo</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
          </tr>
        </thead>
        <tbody>
          {receipts.map(r => (
            <tr key={r.id}>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.code}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.warehouseName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.customerCode ? `${r.customerCode} - ${r.customerName}` : '—'}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{statusLabel(r.status)}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.createdByName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                <button onClick={() => handleViewDetails(r.id)} style={{ cursor: 'pointer', marginRight: '5px' }}>Chi tiết</button>
                <button disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)} style={{ cursor: 'pointer', marginRight: '5px' }}>{printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}</button>
                {r.status === 'Draft' && (
                  <>
                    {canApproveAndReserve && r.createdBy !== userId && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-reserve')} style={{ cursor: 'pointer', marginRight: '5px' }}>Duyệt và giữ hàng</button>}
                    {(canApproveAndReserve && canDispatch) && r.createdBy !== userId && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-dispatch')} style={{ cursor: 'pointer', marginRight: '5px', backgroundColor: '#d97706', color: '#fff', border: 'none', padding: '5px 10px' }}>Duyệt và xuất ngay</button>}
                    {canCancel && <button disabled={actionInFlight !== null} onClick={() => handleCancel(r.id)} style={{ cursor: 'pointer', backgroundColor: '#e74c3c', color: '#fff', border: 'none', padding: '5px 10px' }}>Hủy</button>}
                  </>
                )}
                {r.status === 'Approved' && <>
                  {canDispatch && r.approvedBy !== userId && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'dispatch')}>Xác nhận xuất kho</button>}
                  {canCancel && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleCancel(r.id)}>Hủy và giải phóng hàng</button>}
                </>}
              </td>
            </tr>
          ))}
          {!listLoading && receipts.length === 0 && <tr><td colSpan={6} style={{ textAlign: 'center', padding: '10px' }}>Chưa có phiếu xuất nào</td></tr>}
        </tbody>
      </table>

      {selectedReceipt && (
        <div style={{ padding: '15px', border: '1px solid #34495e', borderRadius: '5px', backgroundColor: '#f9f9f9' }}>
          <h3>Chi Tiết Phiếu Xuất: {selectedReceipt.code}</h3>
          <p><strong>Kho:</strong> {selectedReceipt.warehouseName}</p>
          <p><strong>Khách hàng:</strong> {selectedReceipt.customerCode ? `${selectedReceipt.customerCode} - ${selectedReceipt.customerName}` : '—'}</p>
          {selectedReceipt.status === 'Draft' && canUpdate && hasPermission('partner.read') && <p><label>Đổi khách hàng <select aria-label="Đổi khách hàng" disabled={partnerUpdating} value={selectedReceipt.customerId||''} onChange={e=>void changeCustomer(e.target.value?Number(e.target.value):null)}><option value="">-- Gỡ liên kết --</option>{customers.filter(x=>x.isActive||x.id===selectedReceipt.customerId).map(x=><option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive?'':' (ngừng hoạt động)'}</option>)}</select></label>{partnerUpdating && <span role="status"> Đang cập nhật...</span>}</p>}
          <p><strong>Trạng thái:</strong> {statusLabel(selectedReceipt.status)}</p>
          <p><strong>Ghi chú:</strong> {selectedReceipt.note || '-'}</p>
          <p><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString()}</p>
          {selectedReceipt.status === 'Approved' && <><p><strong>Đang giữ hàng:</strong> Đã giữ hàng khi duyệt</p><p><strong>Người duyệt:</strong> {selectedReceipt.approvedByName || '-'}</p><p><strong>Thời gian duyệt:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString() : '-'}</p></>}
          {selectedReceipt.status === 'Dispatched' && <><p><strong>Đã xuất kho:</strong> {selectedReceipt.dispatchMode === 'DispatchOnApproval' ? 'Duyệt và xuất ngay (tương thích)' : 'Duyệt giữ hàng, sau đó xác nhận xuất'}</p><p><strong>Người duyệt/xuất:</strong> {selectedReceipt.approvedByName || '-'} / {selectedReceipt.dispatchedByName || '-'}</p><p><strong>Thời gian duyệt/xuất:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString() : '-'} / {selectedReceipt.dispatchedAt ? new Date(selectedReceipt.dispatchedAt).toLocaleString() : '-'}</p></>}
          
          <h4>Sản phẩm</h4>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                <th style={{ padding: '5px', border: '1px solid #bdc3c7' }}>Sản phẩm</th>
                <th style={{ padding: '5px', border: '1px solid #bdc3c7' }}>Số lượng</th>
                <th style={{ padding: '5px', border: '1px solid #bdc3c7' }}>Đơn giá</th>
              </tr>
            </thead>
            <tbody>
              {selectedReceipt.details.map(d => (
                <tr key={d.id}>
                  <td style={{ padding: '5px', border: '1px solid #bdc3c7' }}>{d.productCode} - {d.productName}</td>
                  <td style={{ padding: '5px', border: '1px solid #bdc3c7' }}>{d.quantity} {d.unitName || 'Chưa có đơn vị lịch sử'}</td>
                  <td style={{ padding: '5px', border: '1px solid #bdc3c7' }}>{d.unitPrice === undefined ? '—' : d.unitPrice}</td>
                </tr>
              ))}
              {selectedReceipt.details.length === 0 && <tr><td colSpan={3} style={{ textAlign: 'center', padding: '5px' }}>Không có chi tiết</td></tr>}
            </tbody>
          </table>
          <button onClick={() => setSelectedReceipt(null)} style={{ marginTop: '15px', cursor: 'pointer', padding: '8px 15px' }}>Đóng chi tiết</button>
        </div>
      )}
      {printReceipt && printFetchedAt && <ReceiptPrintPreview kind="export" receipt={printReceipt} fetchedAt={printFetchedAt} onClose={closePrintPreview} />}
    </div>
  );
};

export default ExportReceipts;
