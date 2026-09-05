import { useEffect, useState } from 'react';
import { ArrowRight, Check, PackageCheck, Plus, Send, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { currentUserId } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import './StockTransfers.css';

type Warehouse = { id: number; name: string };
type Product = { id: number; code: string; name: string };
type Line = { productId: number | ''; quantity: number | ''; note: string };
type TransferLine = { productId: number; productCode: string; productName: string; requestedQuantity: number; dispatchedQuantity: number; receivedQuantity: number; missingQuantity: number; damagedQuantity: number; inTransitQuantity: number; note?: string };
type Transfer = { id: number; code: string; sourceWarehouseId: number; sourceWarehouseName: string; destinationWarehouseId: number; destinationWarehouseName: string; status: string | number; note?: string; createdBy: number; createdAt: string; approvedAt?: string; dispatchedAt?: string; receivedAt?: string; completedAt?: string; cancelledAt?: string; details: TransferLine[] };

const statusNames = ['Draft', 'Approved', 'InTransit', 'Received', 'Completed', 'Cancelled'];
const statusLabel: Record<string, string> = { Draft: 'Nháp', Approved: 'Đã duyệt', InTransit: 'Đang vận chuyển', Received: 'Đã nhận', Completed: 'Hoàn tất', Cancelled: 'Đã hủy' };
const normalizeStatus = (status: string | number) => typeof status === 'number' ? statusNames[status] : status;

export default function StockTransfers() {
  const role = localStorage.getItem('role') || 'Viewer';
  const canWrite = ['Admin', 'Manager', 'WarehouseStaff'].includes(role);
  const canApprove = ['Admin', 'Manager'].includes(role);
  const userId = currentUserId();
  const [items, setItems] = useState<Transfer[]>([]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [selected, setSelected] = useState<Transfer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [sourceId, setSourceId] = useState<number | ''>('');
  const [destinationId, setDestinationId] = useState<number | ''>('');
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<Line[]>([{ productId: '', quantity: '', note: '' }]);
  const [filters, setFilters] = useState({ search: '', status: '', sourceWarehouseId: '', destinationWarehouseId: '' });
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [receive, setReceive] = useState<Record<number, { receivedQuantity: number; missingQuantity: number; damagedQuantity: number }>>({});
  const [actionInFlight, setActionInFlight] = useState(false);

  const load = async () => {
    setLoading(true); setError('');
    try {
      const params = { ...filters, pageIndex: page, pageSize: 15 };
      const [transfers, warehouseResult, productResult] = await Promise.all([
        apiClient.get('/api/stock-transfers', { params }), apiClient.get('/api/warehouses'), apiClient.get('/api/products')
      ]);
      setItems(transfers.data.items); setTotalPages(transfers.data.totalPages || 1);
      setWarehouses(warehouseResult.data); setProducts(productResult.data);
    } catch (e: any) { setError(e.response?.data?.message || 'Không thể tải dữ liệu điều chuyển.'); }
    finally { setLoading(false); }
  };

  // Filtering is submitted explicitly; page changes trigger the current query.
  // oxlint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { void load(); }, [page]);

  const openDetail = async (id: number) => {
    try {
      const response = await apiClient.get(`/api/stock-transfers/${id}`);
      setSelected(response.data);
      setReceive(Object.fromEntries(response.data.details.map((x: TransferLine) => [x.productId, { receivedQuantity: x.dispatchedQuantity, missingQuantity: 0, damagedQuantity: 0 }])));
    } catch (e: any) { setError(e.response?.data?.message || 'Không thể tải chi tiết phiếu.'); }
  };

  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    if (sourceId === destinationId) return setError('Kho nguồn và kho đích phải khác nhau.');
    if (lines.some(x => !x.productId || Number(x.quantity) <= 0)) return setError('Mỗi dòng phải có sản phẩm và số lượng lớn hơn 0.');
    if (new Set(lines.map(x => x.productId)).size !== lines.length) return setError('Sản phẩm không được trùng dòng.');
    try {
      const payload = { sourceWarehouseId: sourceId, destinationWarehouseId: destinationId, note, details: lines };
      const action = `transfer-create:${JSON.stringify(payload)}`;
      await apiClient.post('/api/stock-transfers', payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      setShowCreate(false); setLines([{ productId: '', quantity: '', note: '' }]); setSourceId(''); setDestinationId(''); setNote(''); await load();
    } catch (e: any) { setError(e.response?.data?.message || 'Không thể tạo phiếu.'); }
  };

  const act = async (action: string, body?: unknown) => {
    if (!selected || !confirm(`Xác nhận thao tác ${statusLabel[action] || action}?`)) return;
    if (actionInFlight) return;
    setActionInFlight(true);
    const logicalAction = `transfer-${action}:${selected.id}`;
    try { await apiClient.post(`/api/stock-transfers/${selected.id}/${action}`, body, { headers: idempotencyHeaders(logicalAction) }); completeIdempotentAction(logicalAction); await openDetail(selected.id); await load(); }
    catch (e: any) { setError(e.response?.data?.message || 'Thao tác không thành công.'); }
    finally { setActionInFlight(false); }
  };

  const submitReceive = () => act('receive', { details: selected?.details.map(x => ({ productId: x.productId, ...receive[x.productId] })) });
  const status = selected ? normalizeStatus(selected.status) : '';

  return <div className="transfer-page">
    <div className="transfer-heading"><div><h1>Điều chuyển kho</h1><p>Theo dõi hàng từ kho nguồn đến khi kho đích xác nhận.</p></div>{canWrite && <button className="primary" onClick={() => setShowCreate(true)}><Plus size={18}/> Tạo phiếu</button>}</div>
    <div className="transfer-filters">
      <input placeholder="Tìm mã phiếu" value={filters.search} onChange={e => setFilters({ ...filters, search: e.target.value })}/>
      <select value={filters.status} onChange={e => setFilters({ ...filters, status: e.target.value })}><option value="">Mọi trạng thái</option>{statusNames.map((x, i) => <option key={x} value={i}>{statusLabel[x]}</option>)}</select>
      <select value={filters.sourceWarehouseId} onChange={e => setFilters({ ...filters, sourceWarehouseId: e.target.value })}><option value="">Mọi kho nguồn</option>{warehouses.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select>
      <select value={filters.destinationWarehouseId} onChange={e => setFilters({ ...filters, destinationWarehouseId: e.target.value })}><option value="">Mọi kho đích</option>{warehouses.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select>
      <button onClick={() => { setPage(1); void load(); }}>Lọc</button>
    </div>
    {error && <div className="transfer-error">{error}<button aria-label="Đóng" onClick={() => setError('')}><X size={16}/></button></div>}
    {loading ? <div className="transfer-empty">Đang tải...</div> : items.length === 0 ? <div className="transfer-empty">Chưa có phiếu điều chuyển phù hợp.</div> : <div className="transfer-table-wrap"><table><thead><tr><th>Mã phiếu</th><th>Luồng kho</th><th>Trạng thái</th><th>Ngày tạo</th></tr></thead><tbody>{items.map(x => <tr key={x.id} onClick={() => void openDetail(x.id)}><td><strong>{x.code}</strong></td><td>{x.sourceWarehouseName} <ArrowRight size={14}/> {x.destinationWarehouseName}</td><td><span className={`status ${normalizeStatus(x.status)}`}>{statusLabel[normalizeStatus(x.status)]}</span></td><td>{new Date(x.createdAt).toLocaleString('vi-VN')}</td></tr>)}</tbody></table></div>}
    <div className="transfer-pagination"><button disabled={page <= 1} onClick={() => setPage(page - 1)}>Trước</button><span>{page} / {totalPages}</span><button disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Sau</button></div>

    {showCreate && <div className="transfer-modal"><form className="transfer-dialog" onSubmit={create}><div className="dialog-title"><h2>Tạo phiếu điều chuyển</h2><button type="button" aria-label="Đóng" onClick={() => setShowCreate(false)}><X/></button></div><div className="warehouse-pair"><label>Kho nguồn<select required value={sourceId} onChange={e => setSourceId(Number(e.target.value) || '')}><option value="">Chọn kho</option>{warehouses.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><ArrowRight/><label>Kho đích<select required value={destinationId} onChange={e => setDestinationId(Number(e.target.value) || '')}><option value="">Chọn kho</option>{warehouses.filter(x => x.id !== sourceId).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label></div><label>Ghi chú<textarea value={note} onChange={e => setNote(e.target.value)}/></label><div className="line-header"><h3>Sản phẩm</h3><button type="button" onClick={() => setLines([...lines, { productId: '', quantity: '', note: '' }])}><Plus size={16}/> Thêm dòng</button></div>{lines.map((line, index) => <div className="transfer-line" key={index}><select required value={line.productId} onChange={e => { const next = [...lines]; next[index].productId = Number(e.target.value) || ''; setLines(next); }}><option value="">Chọn sản phẩm</option>{products.map(x => <option key={x.id} value={x.id}>{x.code} - {x.name}</option>)}</select><input required type="number" min="0.0001" step="0.0001" placeholder="Số lượng" value={line.quantity} onChange={e => { const next = [...lines]; next[index].quantity = Number(e.target.value) || ''; setLines(next); }}/><button type="button" aria-label="Xóa dòng" onClick={() => setLines(lines.filter((_, i) => i !== index))}><X size={17}/></button></div>)}<div className="dialog-actions"><button type="button" onClick={() => setShowCreate(false)}>Đóng</button><button className="primary" type="submit">Tạo phiếu</button></div></form></div>}

    {selected && <div className="transfer-modal"><div className="transfer-dialog detail"><div className="dialog-title"><div><h2>{selected.code}</h2><span className={`status ${status}`}>{statusLabel[status]}</span></div><button aria-label="Đóng" onClick={() => setSelected(null)}><X/></button></div><div className="timeline">{['Draft','Approved','InTransit','Received','Completed'].map((x, i) => <div className={statusNames.indexOf(status) >= i && status !== 'Cancelled' ? 'done' : ''} key={x}><span>{i + 1}</span><small>{statusLabel[x]}</small></div>)}</div><p className="route"><strong>{selected.sourceWarehouseName}</strong><ArrowRight/><strong>{selected.destinationWarehouseName}</strong></p><div className="transfer-table-wrap"><table><thead><tr><th>Sản phẩm</th><th>Yêu cầu</th><th>Đã xuất</th><th>Thực nhận</th><th>Thiếu</th><th>Hỏng</th></tr></thead><tbody>{selected.details.map(x => <tr key={x.productId}><td>{x.productCode} - {x.productName}</td><td>{x.requestedQuantity}</td><td>{x.dispatchedQuantity}</td>{status === 'InTransit' ? <><td><input type="number" min="0" step="0.0001" value={receive[x.productId]?.receivedQuantity ?? 0} onChange={e => setReceive({ ...receive, [x.productId]: { ...receive[x.productId], receivedQuantity: Number(e.target.value) } })}/></td><td><input type="number" min="0" step="0.0001" value={receive[x.productId]?.missingQuantity ?? 0} onChange={e => setReceive({ ...receive, [x.productId]: { ...receive[x.productId], missingQuantity: Number(e.target.value) } })}/></td><td><input type="number" min="0" step="0.0001" value={receive[x.productId]?.damagedQuantity ?? 0} onChange={e => setReceive({ ...receive, [x.productId]: { ...receive[x.productId], damagedQuantity: Number(e.target.value) } })}/></td></> : <><td>{x.receivedQuantity}</td><td>{x.missingQuantity}</td><td>{x.damagedQuantity}</td></>}</tr>)}</tbody></table></div><div className="dialog-actions">{canWrite && ['Draft','Approved'].includes(status) && <button disabled={actionInFlight} className="danger" onClick={() => void act('cancel')}>Hủy phiếu</button>}{canApprove && selected.createdBy !== userId && status === 'Draft' && <button disabled={actionInFlight} onClick={() => void act('approve')}><Check size={17}/> Duyệt</button>}{canWrite && status === 'Approved' && <button disabled={actionInFlight} onClick={() => void act('dispatch')}><Send size={17}/> Xuất kho</button>}{canWrite && status === 'InTransit' && <button disabled={actionInFlight} onClick={() => void submitReceive()}><PackageCheck size={17}/> Xác nhận nhận</button>}{canWrite && status === 'Received' && <button disabled={actionInFlight} className="primary" onClick={() => void act('complete')}>Hoàn tất</button>}</div></div></div>}
  </div>;
}
