import { useState, useEffect } from 'react';
import apiClient from '../services/apiClient';
import { canApproveExportImmediately, canManageCatalogs, canOperateWarehouse, currentRole, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';

interface ExportReceiptDetail {
  id: number;
  productCode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
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
  approvedAt?: string;
  dispatchedByName?: string;
  dispatchedAt?: string;
  dispatchMode?: string;
  reservationStatus?: string;
  allowPerReceiptDispatchMode?: boolean;
  allowWarehouseStaffDirectDispatch: boolean;
  writeEnabled: boolean;
  details: ExportReceiptDetail[];
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

interface ReceiptDetailForm {
  productId: number | '';
  quantity: number | '';
  unitPrice: number | '';
  note: string;
}

const ExportReceipts = () => {
  const role = currentRole();
  const userId = currentUserId();
  const canOperate = canOperateWarehouse(role);
  const canApproveAndReserve = canManageCatalogs(role);
  const [receipts, setReceipts] = useState<ExportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ExportReceipt | null>(null);
  const [actionInFlight, setActionInFlight] = useState<string | null>(null);

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);
  
  // Stock cache
  const [stockCache, setStockCache] = useState<Record<string, number | null>>({});

  const fetchData = async () => {
    try {
      const [whRes, prRes] = await Promise.all([
        apiClient.get('/api/warehouses'),
        apiClient.get('/api/products')
      ]);
      setWarehouses(whRes.data);
      setProducts(prRes.data);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải dữ liệu khởi tạo');
    }
  };

  const fetchReceipts = async () => {
    try {
      const res = await apiClient.get('/api/exportreceipts');
      setReceipts(res.data);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải danh sách phiếu xuất');
    }
  };

  useEffect(() => {
    fetchData();
    fetchReceipts();
  }, []);

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
              const stock = (res.data && res.data.length > 0) ? res.data[0].availableQuantity : 0;
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
    if (!confirm('Bạn có chắc muốn hủy phiếu xuất này?')) return;
    try {
      const action = `export-cancel:${id}`;
      await apiClient.post(`/api/exportreceipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      alert('Hủy thành công');
      fetchReceipts();
      if (selectedReceipt?.id === id) {
        const res = await apiClient.get(`/api/exportreceipts/${id}`);
        setSelectedReceipt(res.data);
      }
    } catch (err: any) {
      alert(err.response?.data?.message || 'Lỗi khi hủy phiếu xuất');
    }
  };

  const refreshAfterAction = async (id: number) => {
    await fetchReceipts();
    if (selectedReceipt?.id === id) {
      const res = await apiClient.get(`/api/exportreceipts/${id}`);
      setSelectedReceipt(res.data);
    }
  };

  const handleWorkflowAction = async (id: number, action: 'approve-and-reserve' | 'approve-and-dispatch' | 'dispatch') => {
    const messages = {
      'approve-and-reserve': 'Phiếu sẽ được duyệt và giữ hàng. Tồn thực tế chưa giảm cho đến khi thủ kho xác nhận xuất.',
      'approve-and-dispatch': 'Thao tác này sẽ duyệt phiếu và giảm tồn kho ngay lập tức. Bạn có chắc hàng đã được giao khỏi kho?',
      dispatch: 'Xác nhận hàng đã rời kho. Tồn thực tế sẽ giảm ngay.'
    };
    if (!confirm(messages[action])) return;
    setActionInFlight(`${id}-${action}`);
    try {
      const logicalAction = `export-${action}:${id}`;
      await apiClient.post(`/api/exportreceipts/${id}/${action}`, undefined, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      await refreshAfterAction(id);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Không thể xử lý phiếu xuất.');
    } finally {
      setActionInFlight(null);
    }
  };

  const handleViewDetails = async (id: number) => {
    try {
      const res = await apiClient.get(`/api/exportreceipts/${id}`);
      setSelectedReceipt(res.data);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tải chi tiết phiếu');
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

    try {
      const payload = {
        code: code.trim(),
        warehouseId: Number(warehouseId),
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
      setNote('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tạo phiếu xuất');
    }
  };

  const getStockDisplay = (wId: number | '', pId: number | '') => {
    if (wId === '' || pId === '') return '-';
    const key = `${wId}_${pId}`;
    const stock = stockCache[key];
    if (stock === undefined) return 'Đang tải...';
    if (stock === null) return 'Lỗi tải tồn';
    return stock;
  };

  return (
    <div>
      <h2>Quản Lý Phiếu Xuất Kho</h2>
      {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      {receipts.some(receipt => !receipt.writeEnabled) && <div role="status" style={{ color: '#92400e', marginBottom: '10px' }}>Workflow xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.</div>}
      
      {canOperate && <div style={{ marginBottom: '30px', padding: '15px', border: '1px solid #ccc', borderRadius: '5px' }}>
        <h3>Tạo Phiếu Xuất Kho</h3>
        <form onSubmit={handleCreate}>
          <div style={{ display: 'flex', gap: '15px', marginBottom: '15px' }}>
            <div>
              <label style={{ display: 'block' }}>Mã phiếu</label>
              <input value={code} onChange={e => setCode(e.target.value)} required />
            </div>
            <div>
              <label style={{ display: 'block' }}>Kho</label>
              <select value={warehouseId} onChange={e => setWarehouseId(e.target.value ? Number(e.target.value) : '')} required>
                <option value="">-- Chọn kho --</option>
                {warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
              </select>
            </div>
            <div>
              <label style={{ display: 'block' }}>Ghi chú phiếu</label>
              <input value={note} onChange={e => setNote(e.target.value)} />
            </div>
          </div>

          <h4>Chi tiết phiếu</h4>
          {details.map((d, i) => {
            const stockDisplay = getStockDisplay(warehouseId, d.productId);
            const isExceed = typeof stockDisplay === 'number' && Number(d.quantity) > stockDisplay;
            return (
              <div key={i} style={{ display: 'flex', gap: '10px', marginBottom: '10px', alignItems: 'center' }}>
                <select 
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
                  value={d.quantity} 
                  onChange={e => handleDetailChange(i, 'quantity', e.target.value ? Number(e.target.value) : '')}
                  required
                  min="0.01"
                  step="0.01"
                  style={{ borderColor: isExceed ? 'red' : undefined }}
                />
                
                <span style={{ fontSize: '14px', minWidth: '100px', color: isExceed ? 'red' : 'inherit' }}>
                  (Tồn: {stockDisplay})
                </span>
                
                <input 
                  type="number" 
                  placeholder="Đơn giá" 
                  value={d.unitPrice} 
                  onChange={e => handleDetailChange(i, 'unitPrice', e.target.value ? Number(e.target.value) : '')}
                  required
                  min="0"
                  step="0.01"
                />

                <input 
                  placeholder="Ghi chú" 
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
              disabled={isFormInvalid()}
              style={{ 
                backgroundColor: isFormInvalid() ? '#95a5a6' : '#2ecc71', 
                color: '#fff', 
                padding: '10px 20px', 
                border: 'none', 
                cursor: isFormInvalid() ? 'not-allowed' : 'pointer' 
              }}
            >
              Tạo Phiếu Xuất
            </button>
          </div>
        </form>
      </div>}

      <hr style={{ margin: '30px 0' }} />

      <h3>Danh Sách Phiếu Xuất</h3>
      <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: '30px' }}>
        <thead>
          <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã phiếu</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
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
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.status}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.createdByName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                <button onClick={() => handleViewDetails(r.id)} style={{ cursor: 'pointer', marginRight: '5px' }}>Chi tiết</button>
                {canOperate && r.status === 'Draft' && (
                  <>
                    {canApproveAndReserve && r.createdBy !== userId && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-reserve')} style={{ cursor: 'pointer', marginRight: '5px' }}>Duyệt và giữ hàng</button>}
                    {canApproveExportImmediately(role, r.allowWarehouseStaffDirectDispatch) && r.createdBy !== userId && <button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'approve-and-dispatch')} style={{ cursor: 'pointer', marginRight: '5px', backgroundColor: '#d97706', color: '#fff', border: 'none', padding: '5px 10px' }}>Duyệt và xuất ngay</button>}
                    <button disabled={actionInFlight !== null} onClick={() => handleCancel(r.id)} style={{ cursor: 'pointer', backgroundColor: '#e74c3c', color: '#fff', border: 'none', padding: '5px 10px' }}>Hủy</button>
                  </>
                )}
                {canOperate && r.status === 'Approved' && <><button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleWorkflowAction(r.id, 'dispatch')} style={{ cursor: 'pointer', marginRight: '5px', backgroundColor: '#d97706', color: '#fff', border: 'none', padding: '5px 10px' }}>Xác nhận xuất kho</button><button disabled={actionInFlight !== null || !r.writeEnabled} onClick={() => handleCancel(r.id)} style={{ cursor: 'pointer', backgroundColor: '#e74c3c', color: '#fff', border: 'none', padding: '5px 10px' }}>Hủy và giải phóng hàng</button></>}
              </td>
            </tr>
          ))}
          {receipts.length === 0 && <tr><td colSpan={5} style={{ textAlign: 'center', padding: '10px' }}>Chưa có phiếu xuất nào</td></tr>}
        </tbody>
      </table>

      {selectedReceipt && (
        <div style={{ padding: '15px', border: '1px solid #34495e', borderRadius: '5px', backgroundColor: '#f9f9f9' }}>
          <h3>Chi Tiết Phiếu Xuất: {selectedReceipt.code}</h3>
          <p><strong>Kho:</strong> {selectedReceipt.warehouseName}</p>
          <p><strong>Trạng thái:</strong> {selectedReceipt.status}</p>
          <p><strong>Ghi chú:</strong> {selectedReceipt.note || '-'}</p>
          <p><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString()}</p>
          {selectedReceipt.status === 'Approved' && <><p><strong>Đang giữ hàng:</strong> {selectedReceipt.reservationStatus}</p><p><strong>Người duyệt:</strong> {selectedReceipt.approvedByName || '-'}</p><p><strong>Thời gian duyệt:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString() : '-'}</p></>}
          {selectedReceipt.status === 'Dispatched' && <><p><strong>Đã xuất kho:</strong> {selectedReceipt.dispatchMode}</p><p><strong>Người duyệt/xuất:</strong> {selectedReceipt.approvedByName || '-'} / {selectedReceipt.dispatchedByName || '-'}</p><p><strong>Thời gian duyệt/xuất:</strong> {selectedReceipt.approvedAt ? new Date(selectedReceipt.approvedAt).toLocaleString() : '-'} / {selectedReceipt.dispatchedAt ? new Date(selectedReceipt.dispatchedAt).toLocaleString() : '-'}</p></>}
          
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
                  <td style={{ padding: '5px', border: '1px solid #bdc3c7' }}>{d.quantity}</td>
                  <td style={{ padding: '5px', border: '1px solid #bdc3c7' }}>{d.unitPrice}</td>
                </tr>
              ))}
              {selectedReceipt.details.length === 0 && <tr><td colSpan={3} style={{ textAlign: 'center', padding: '5px' }}>Không có chi tiết</td></tr>}
            </tbody>
          </table>
          <button onClick={() => setSelectedReceipt(null)} style={{ marginTop: '15px', cursor: 'pointer', padding: '8px 15px' }}>Đóng chi tiết</button>
        </div>
      )}
    </div>
  );
};

export default ExportReceipts;
