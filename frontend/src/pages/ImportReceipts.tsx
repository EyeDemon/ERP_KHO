import { useState, useEffect, useRef } from 'react';
import apiClient from '../services/apiClient';
import { canManageCatalogs, currentRole, currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import ReceiptPrintPreview from '../components/ReceiptPrintPreview';

interface ImportReceiptDetail {
  id: number;
  productId: number;
  productCode: string;
  productName: string;
  operationUnitId: number; operationUnitCode: string; baseUnitCode: string; conversionFactor: number; conversionVersion: number;
  expectedQuantity: number; receivedQuantity: number; acceptedQuantity: number; damagedQuantity: number; rejectedQuantity: number;
  postedQuantity: number; baseExpectedQuantity: number; baseReceivedQuantity: number; baseAcceptedQuantity: number; basePostedQuantity: number;
  unitPrice?: number;
  note: string;
}

interface ImportReceipt {
  id: number;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: string;
  note: string;
  createdBy: number;
  createdByName: string;
  createdAt: string;
  approvedBy: number;
  approvedByName: string;
  approvedAt: string;
  supplierId?: number;
  supplierCode?: string;
  supplierName?: string;
  details: ImportReceiptDetail[];
}

interface Warehouse {
  id: number;
  name: string;
}

interface Product {
  id: number;
  name: string;
  code: string;
  unitId: number; unitCode: string; unitName: string; unitDecimalPlaces: number;
  uoms: Array<{ unitId:number; unitCode:string; unitName:string; decimalPlaces:number; conversionFactor:number; version:number }>;
}
interface Partner { id:number; code:string; name:string; isActive:boolean; }

interface ReceiptDetailForm {
  productId: number | '';
  operationUnitId: number | '';
  expectedQuantity: number | '';
  unitPrice: number | '';
  note: string;
}
type ReceiveLineForm = { receivedQuantity: number; acceptedQuantity: number; damagedQuantity: 0; rejectedQuantity: 0 };

const ImportReceipts = () => {
  const canApprove = canManageCatalogs(currentRole());
  const userId = currentUserId();
  const [receipts, setReceipts] = useState<ImportReceipt[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [suppliers, setSuppliers] = useState<Partner[]>([]);
  const [error, setError] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [selectedReceipt, setSelectedReceipt] = useState<ImportReceipt | null>(null);
  const [receiveLines, setReceiveLines] = useState<Record<number, ReceiveLineForm>>({});
  const [approvingId, setApprovingId] = useState<number | null>(null);
  const [workflowId, setWorkflowId] = useState<number | null>(null);
  const createInFlight = useRef(false);
  const approveInFlight = useRef<number | null>(null);
  const cancelInFlight = useRef<number | null>(null);
  const workflowInFlight = useRef<number | null>(null);
  const partnerMutationInFlight = useRef(false);
  const [creating, setCreating] = useState(false);
  const [partnerUpdating, setPartnerUpdating] = useState(false);
  const [printReceipt, setPrintReceipt] = useState<ImportReceipt | null>(null);
  const [printFetchedAt, setPrintFetchedAt] = useState<Date | null>(null);
  const [printLoadingId, setPrintLoadingId] = useState<number | null>(null);
  const printTriggerRef = useRef<HTMLButtonElement | null>(null);

  // Form states
  const [code, setCode] = useState('');
  const [warehouseId, setWarehouseId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [supplierId, setSupplierId] = useState<number | ''>('');
  const [details, setDetails] = useState<ReceiptDetailForm[]>([]);

  const fetchData = async () => {
    try {
      const [whRes, prRes, bpRes] = await Promise.all([
        apiClient.get('/api/warehouses'),
        apiClient.get('/api/products'),
        apiClient.get('/api/business-partners', { params: { role: 'supplier', pageSize: 100 } })
      ]);
      setWarehouses(whRes.data);
      setProducts(prRes.data);
      setSuppliers(bpRes.data.items);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải dữ liệu khởi tạo');
    }
  };

  const fetchReceipts = async () => {
    try {
      const res = await apiClient.get('/api/importreceipts');
      setReceipts(res.data);
    } catch (err: any) {
      console.error(err);
      setError('Lỗi khi tải danh sách phiếu nhập');
    }
  };

  useEffect(() => {
    fetchData();
    fetchReceipts();
  }, []);

  const handleApprove = async (id: number) => {
    if (approveInFlight.current !== null) return;
    approveInFlight.current = id;
    setApprovingId(id);
    setError('');
    try {
      const action = `import-approve:${id}`;
      await apiClient.post(`/api/importreceipts/${id}/approve`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      alert('Duyệt thành công');
      await fetchReceipts();
      if (selectedReceipt?.id === id) {
        await handleViewDetails(id);
      }
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(err.response?.data?.message || 'Lỗi khi duyệt phiếu');
    } finally {
      approveInFlight.current = null;
      setApprovingId(null);
    }
  };

  const handleCancel = async (id: number) => {
    if (cancelInFlight.current !== null) return;
    if (!window.confirm('Bạn có chắc chắn muốn hủy phiếu nhập này?')) return;
    cancelInFlight.current = id;
    try {
      const action = `import-cancel:${id}`;
      await apiClient.put(`/api/importreceipts/${id}/cancel`, undefined, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      alert('Hủy thành công');
      fetchReceipts();
      if (selectedReceipt?.id === id) {
          handleViewDetails(id);
      }
    } catch (err: any) {
      if (selectedReceipt?.id === id) setSelectedReceipt(null);
      setError(err.response?.data?.message || 'Lỗi khi hủy phiếu');
    } finally { cancelInFlight.current = null; }
  };

  const handleViewDetails = async (id: number) => {
    try {
      const res = await apiClient.get(`/api/importreceipts/${id}`);
      setSelectedReceipt(res.data);
      setReceiveLines(Object.fromEntries((res.data.details as ImportReceiptDetail[]).map(d => [d.id, { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }])));
    } catch (err: any) {
      setSelectedReceipt(null);
      setError(err.response?.data?.message || 'Lỗi khi tải chi tiết phiếu');
    }
  };

  const handleAddDetail = () => {
    setDetails([...details, { productId: '', operationUnitId: '', expectedQuantity: '', unitPrice: '', note: '' }]);
  };

  const handleRemoveDetail = (index: number) => {
    setDetails(details.filter((_, i) => i !== index));
  };

  const handleDetailChange = (index: number, field: keyof ReceiptDetailForm, value: any) => {
    const newDetails = [...details];
    newDetails[index] = { ...newDetails[index], [field]: value };
    setDetails(newDetails);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (createInFlight.current) return;
    setError('');
    setSuccessMsg('');

    // Validation
    if (!code.trim()) return setError('Vui lòng nhập mã phiếu');
    if (warehouseId === '') return setError('Vui lòng chọn kho');
    if (details.length === 0) return setError('Cần ít nhất 1 dòng chi tiết');

    for (let i = 0; i < details.length; i++) {
      const d = details[i];
      if (d.productId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn sản phẩm`);
      if (d.expectedQuantity === '' || Number(d.expectedQuantity) <= 0) return setError(`Dòng ${i + 1}: Số lượng dự kiến phải > 0`);
      if (d.operationUnitId === '') return setError(`Dòng ${i + 1}: Vui lòng chọn UOM thao tác`);
      if (d.unitPrice === '' || Number(d.unitPrice) < 0) return setError(`Dòng ${i + 1}: Đơn giá phải >= 0`);
    }

    createInFlight.current = true; setCreating(true);
    try {
      const payload = {
        code: code.trim(),
        warehouseId: Number(warehouseId),
        supplierId: supplierId === '' ? null : Number(supplierId),
        note,
        details: details.map(d => ({
          productId: Number(d.productId),
          operationUnitId: Number(d.operationUnitId),
          expectedQuantity: Number(d.expectedQuantity),
          unitPrice: Number(d.unitPrice),
          note: d.note
        }))
      };

      const action = `import-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/importreceipts', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg('Tạo phiếu nháp thành công!');
      
      // Reset form
      setCode('');
      setWarehouseId('');
      setSupplierId('');
      setNote('');
      setDetails([]);
      
      fetchReceipts();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Lỗi khi tạo phiếu nhập');
    } finally {
      createInFlight.current = false; setCreating(false);
    }
  };

  const runWorkflow = async (id: number, command: 'receive' | 'post', receipt?: ImportReceipt) => {
    if (workflowInFlight.current !== null) return;
    workflowInFlight.current = id;
    setWorkflowId(id); setError('');
    try {
      const action = `import-${command}:${id}`;
      const source = command === 'receive' && !receipt ? (await apiClient.get(`/api/importreceipts/${id}`)).data as ImportReceipt : receipt;
      const body = command === 'receive' ? { lines: (source?.details || []).map(d => ({ lineId: d.id, ...(receiveLines[d.id] || { receivedQuantity: d.expectedQuantity, acceptedQuantity: d.expectedQuantity, damagedQuantity: 0, rejectedQuantity: 0 }) })) } : undefined;
      if (body?.lines.some(line => line.receivedQuantity <= 0 || line.acceptedQuantity < 0 || line.acceptedQuantity + line.damagedQuantity + line.rejectedQuantity !== line.receivedQuantity))
        throw new Error('Tổng chấp nhận, hư hỏng và từ chối phải bằng số lượng nhận.');
      await apiClient.post(`/api/importreceipts/${id}/${command}`, body, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setSuccessMsg(command === 'receive' ? 'Đã hoàn tất nhận hàng. Tồn kho chưa thay đổi.' : 'Đã post phiếu và ghi tăng tồn kho.');
      await fetchReceipts();
      if (selectedReceipt?.id === id) await handleViewDetails(id);
    } catch (err: any) { if (err.response && selectedReceipt?.id === id) setSelectedReceipt(null); setError(err.response?.status === 409 ? 'Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.' : err.response?.data?.message || err.message || 'Không thể xử lý phiếu nhập'); }
    finally { workflowInFlight.current = null; setWorkflowId(null); }
  };

  const statusLabel = (status: string) => ({ Draft: 'Nháp', Received: 'Đã nhận — chưa ghi tồn', ReadyToPost: 'Sẵn sàng post', Posted: 'Đã post', Approved: 'Đã duyệt (dữ liệu cũ)', Cancelled: 'Đã hủy' }[status] || status);

  const changeSupplier = async (value: number | null) => {
    if (!selectedReceipt || partnerMutationInFlight.current) return;
    partnerMutationInFlight.current = true; setPartnerUpdating(true); setError('');
    try { await apiClient.put(`/api/importreceipts/${selectedReceipt.id}/supplier`, { partnerId: value }); await fetchReceipts(); await handleViewDetails(selectedReceipt.id); }
    catch (x: any) { setError(x.response?.data?.message || 'Không đổi được nhà cung cấp.'); }
    finally { partnerMutationInFlight.current = false; setPartnerUpdating(false); }
  };

  const openPrintPreview = async (id: number, trigger: HTMLButtonElement) => {
    printTriggerRef.current = trigger; setPrintReceipt(null); setPrintFetchedAt(null); setPrintLoadingId(id); setError('');
    try { const res = await apiClient.get(`/api/importreceipts/${id}`); setPrintReceipt(res.data); setPrintFetchedAt(new Date()); }
    catch (x: any) { setError(x.response?.data?.message || 'Không tải được dữ liệu bản in.'); }
    finally { setPrintLoadingId(null); }
  };
  const closePrintPreview = () => { setPrintReceipt(null); setPrintFetchedAt(null); queueMicrotask(() => printTriggerRef.current?.focus()); };

  return (
    <div>
      <h2>Quản Lý Nhập Kho</h2>
      {error && <div style={{ color: 'red', marginBottom: '10px' }}>{error}</div>}
      {successMsg && <div style={{ color: 'green', marginBottom: '10px' }}>{successMsg}</div>}
      
      <div style={{ marginBottom: '30px', padding: '15px', border: '1px solid #ccc', borderRadius: '5px' }}>
        <h3>Tạo Phiếu Nhập (Nháp)</h3>
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
            <div><label style={{display:'block'}}>Nhà cung cấp</label><select aria-label="Nhà cung cấp" value={supplierId} onChange={e=>setSupplierId(e.target.value?Number(e.target.value):'')}><option value="">-- Không chọn --</option>{suppliers.filter(x=>x.isActive).map(x=><option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}</select></div>
          </div>

          <h4>Chi tiết phiếu</h4>
          {details.map((d, i) => (
            <div key={i} style={{ display: 'flex', gap: '10px', marginBottom: '10px', alignItems: 'center' }}>
              <select 
                value={d.productId} 
                onChange={e => { const productId = e.target.value ? Number(e.target.value) : ''; const product = products.find(x => x.id === productId); const next = [...details]; next[i] = { ...next[i], productId, operationUnitId: product?.unitId || '' }; setDetails(next); }}
                required
              >
                <option value="">-- Chọn sản phẩm --</option>
                {products.map(p => <option key={p.id} value={p.id}>{p.code} - {p.name}</option>)}
              </select>
              
              <select aria-label={`UOM thao tác dòng ${i + 1}`} value={d.operationUnitId} onChange={e => handleDetailChange(i, 'operationUnitId', e.target.value ? Number(e.target.value) : '')} required>
                <option value="">-- UOM thao tác --</option>
                {(products.find(x => x.id === d.productId)?.uoms || []).map(u => <option key={`${u.unitId}-${u.version}`} value={u.unitId}>{u.unitCode} — × {u.conversionFactor}</option>)}
              </select>

              <input 
                type="number" 
                aria-label={`Số lượng dự kiến dòng ${i + 1}`}
                placeholder="Số lượng dự kiến"
                value={d.expectedQuantity}
                onChange={e => handleDetailChange(i, 'expectedQuantity', e.target.value ? Number(e.target.value) : '')}
                required
                min="0.01"
                step="0.01"
              />
              {(() => { const p=products.find(x=>x.id===d.productId); const u=p?.uoms?.find(x=>x.unitId===d.operationUnitId); return p&&u&&d.expectedQuantity!=='' ? <span>{d.expectedQuantity} {u.unitCode} = {Number(d.expectedQuantity)*u.conversionFactor} {p.unitCode} Base UOM</span> : null; })()}
              
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
          ))}
          
          <button type="button" onClick={handleAddDetail} style={{ marginBottom: '15px' }}>+ Thêm dòng</button>
          
          <div style={{ marginTop: '15px' }}>
            <button type="submit" disabled={creating} style={{ backgroundColor: creating ? '#95a5a6' : '#2ecc71', color: '#fff', padding: '10px 20px', border: 'none', cursor: creating ? 'not-allowed' : 'pointer' }}>{creating ? 'Đang lưu...' : 'Lưu Phiếu Nháp'}</button>
          </div>
        </form>
      </div>

      <hr style={{ margin: '30px 0' }} />

      <h3>Danh Sách Phiếu Nhập</h3>
      <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: '30px' }}>
        <thead>
          <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã phiếu</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ngày tạo</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Kho</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Nhà cung cấp</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Người tạo</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Trạng thái</th>
            <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Hành động</th>
          </tr>
        </thead>
        <tbody>
          {receipts.map(r => (
            <tr key={r.id}>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.code}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{new Date(r.createdAt).toLocaleString()}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.warehouseName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.supplierCode ? `${r.supplierCode} - ${r.supplierName}` : '—'}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{r.createdByName}</td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7', fontWeight: 'bold', color: r.status === 'Draft' ? '#f39c12' : r.status === 'Cancelled' ? '#c0392b' : '#27ae60' }}>
                {statusLabel(r.status)}
              </td>
              <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>
                <button onClick={() => handleViewDetails(r.id)} style={{ cursor: 'pointer', marginRight: '5px' }}>Chi tiết</button>
                <button disabled={printLoadingId !== null} onClick={e => void openPrintPreview(r.id, e.currentTarget)} style={{ cursor: 'pointer', marginRight: '5px' }}>{printLoadingId === r.id ? 'Đang tải bản in...' : 'Xem bản in'}</button>
                {r.status === 'Draft' && (
                  <>
                    <button disabled={workflowId === r.id} onClick={() => void runWorkflow(r.id, 'receive')}>{workflowId === r.id ? 'Đang xử lý...' : 'Hoàn tất nhận hàng'}</button>
                    <button onClick={() => handleCancel(r.id)} style={{ cursor: 'pointer', backgroundColor: '#e74c3c', color: '#fff', border: 'none', padding: '5px 10px', borderRadius: '3px' }}>Hủy</button>
                  </>
                )}
                {r.status === 'Received' && canApprove && r.createdBy !== userId && <button
                      onClick={() => handleApprove(r.id)} 
                      disabled={approvingId === r.id}
                      style={{ 
                        cursor: approvingId === r.id ? 'not-allowed' : 'pointer', 
                        marginRight: '5px', 
                        backgroundColor: approvingId === r.id ? '#95a5a6' : '#3498db', 
                        color: '#fff', 
                        border: 'none', 
                        padding: '5px 10px', 
                        borderRadius: '3px' 
                      }}
                    >
                      {approvingId === r.id ? 'Đang duyệt...' : 'Duyệt để post'}
                    </button>}
                {r.status === 'ReadyToPost' && canApprove && r.createdBy !== userId && <button disabled={workflowId === r.id} onClick={() => { if (window.confirm('Post sẽ ghi tăng tồn theo Base UOM đã lưu trên phiếu. Tiếp tục?')) void runWorkflow(r.id, 'post'); }}>{workflowId === r.id ? 'Đang post...' : 'Post ghi tồn'}</button>}
              </td>
            </tr>
          ))}
          {receipts.length === 0 && (
            <tr>
              <td colSpan={7} style={{ textAlign: 'center', padding: '10px' }}>Chưa có phiếu nhập</td>
            </tr>
          )}
        </tbody>
      </table>

      {selectedReceipt && (
        <div style={{ padding: '15px', border: '1px solid #34495e', borderRadius: '5px', backgroundColor: '#f9f9f9' }}>
          <h3>Chi Tiết Phiếu Nhập: {selectedReceipt.code}</h3>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px', marginBottom: '15px' }}>
            <div><strong>Kho:</strong> {selectedReceipt.warehouseName}</div>
            <div><strong>Nhà cung cấp:</strong> {selectedReceipt.supplierCode ? `${selectedReceipt.supplierCode} - ${selectedReceipt.supplierName}` : '—'}</div>
            {selectedReceipt.status === 'Draft' && <div><label>Đổi nhà cung cấp <select aria-label="Đổi nhà cung cấp" disabled={partnerUpdating} value={selectedReceipt.supplierId||''} onChange={e=>void changeSupplier(e.target.value?Number(e.target.value):null)}><option value="">-- Gỡ liên kết --</option>{suppliers.filter(x=>x.isActive||x.id===selectedReceipt.supplierId).map(x=><option key={x.id} value={x.id}>{x.code} - {x.name}{x.isActive?'':' (ngừng hoạt động)'}</option>)}</select></label>{partnerUpdating && <span role="status"> Đang cập nhật...</span>}</div>}
            <div><strong>Trạng thái:</strong> {statusLabel(selectedReceipt.status)}</div>
            <div><strong>Người tạo:</strong> {selectedReceipt.createdByName}</div>
            <div><strong>Ngày tạo:</strong> {new Date(selectedReceipt.createdAt).toLocaleString()}</div>
            {['Approved', 'ReadyToPost', 'Posted'].includes(selectedReceipt.status) && selectedReceipt.approvedAt && (
              <>
                <div><strong>Người duyệt:</strong> {selectedReceipt.approvedByName}</div>
                <div><strong>Ngày duyệt:</strong> {new Date(selectedReceipt.approvedAt).toLocaleString()}</div>
              </>
            )}
            <div><strong>Ghi chú:</strong> {selectedReceipt.note}</div>
          </div>
          
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ backgroundColor: '#ecf0f1', textAlign: 'left' }}>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Mã SP</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Tên SP</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Dự kiến</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Nhận / Chấp nhận</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>Base UOM</th>
                <th style={{ padding: '10px', border: '1px solid #bdc3c7' }}>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              {selectedReceipt.details.map(d => (
                <tr key={d.id}>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.productCode}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.productName}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>{d.expectedQuantity} {d.operationUnitCode}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>
                    {selectedReceipt.status === 'Draft' ? <div style={{display:'grid', gap:'4px'}}>
                      <label>Số lượng nhận <input aria-label={`Số lượng nhận ${d.productCode}`} type="number" min="0" step="any" value={receiveLines[d.id]?.receivedQuantity ?? d.expectedQuantity} onChange={e=>setReceiveLines(x=>({...x,[d.id]:{...(x[d.id]||{receivedQuantity:d.expectedQuantity,acceptedQuantity:d.expectedQuantity,damagedQuantity:0,rejectedQuantity:0}),receivedQuantity:Number(e.target.value)}}))}/></label>
                      <label>Số lượng chấp nhận <input aria-label={`Số lượng chấp nhận ${d.productCode}`} type="number" min="0" step="any" value={receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity} onChange={e=>setReceiveLines(x=>({...x,[d.id]:{...(x[d.id]||{receivedQuantity:d.expectedQuantity,acceptedQuantity:d.expectedQuantity,damagedQuantity:0,rejectedQuantity:0}),acceptedQuantity:Number(e.target.value)}}))}/></label>
                      <span>Hư hỏng: 0 · Từ chối: 0 <small>(no-QC)</small></span>
                    </div> : <>{d.receivedQuantity} / {d.acceptedQuantity} {d.operationUnitCode}<br/><small>Hư hỏng: {d.damagedQuantity}; Từ chối: {d.rejectedQuantity}</small></>}
                  </td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7', textAlign: 'right' }}>× {d.conversionFactor} (v{d.conversionVersion})<br/>{selectedReceipt.status === 'Draft' ? (receiveLines[d.id]?.acceptedQuantity ?? d.expectedQuantity) * d.conversionFactor : d.baseAcceptedQuantity || d.baseExpectedQuantity} {d.baseUnitCode}</td>
                  <td style={{ padding: '10px', border: '1px solid #bdc3c7' }}>{d.note}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {selectedReceipt.status === 'Draft' && <button disabled={workflowId === selectedReceipt.id} onClick={() => void runWorkflow(selectedReceipt.id, 'receive', selectedReceipt)} style={{marginTop:'12px'}}>{workflowId === selectedReceipt.id ? 'Đang xử lý...' : 'Hoàn tất nhận hàng (no-QC)'}</button>}
          
          <button onClick={() => setSelectedReceipt(null)} style={{ marginTop: '15px', cursor: 'pointer', padding: '8px 15px' }}>Đóng chi tiết</button>
        </div>
      )}
      {printReceipt && printFetchedAt && <ReceiptPrintPreview kind="import" receipt={{...printReceipt, details: printReceipt.details.map(d => ({...d, unitName: d.operationUnitCode, quantity: d.postedQuantity || d.receivedQuantity || d.expectedQuantity}))}} fetchedAt={printFetchedAt} onClose={closePrintPreview} />}
    </div>
  );
};

export default ImportReceipts;
