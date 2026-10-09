import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import AccessibleDialog from '../components/AccessibleDialog';
import { hasPermission, usePermissionSet } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import './StockReservations.css';
import './Approvals.css';

type Reservation = {
  id: number; reservationCode: string; productCode: string; productName: string; warehouseName: string;
  quantity: number; consumedQuantity: number; releasedQuantity: number; remainingQuantity: number;
  status: string; sourceType: string; sourceCode?: string; expiresAt: string;
  baseUomCodeSnapshot?: string; baseUomNameSnapshot?: string; baseUomPrecisionSnapshot?: number; rowVersion?: string;
};
type Page = { items: Reservation[]; totalRecords: number; pageIndex: number; pageSize: number };
type Product = { id: number; code: string; name: string; unitName: string; unitDecimalPlaces: number; isActive: boolean };
type Warehouse = { id: number; name: string };
type Issue = { issue: string; reservationId?: number; productId: number; warehouseId: number; ledgerReserved?: number; stockReserved?: number };
const emptyPage: Page = { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20 };
const statuses: Record<string, string> = { Active: 'Đang giữ', PartiallyConsumed: 'Đã xử lý một phần', Consumed: 'Đã sử dụng', Released: 'Đã giải phóng', Expired: 'Đã hết hạn', Cancelled: 'Đã hủy' };
const issueLabels: Record<string, string> = { MissingStock: 'Thiếu số dư tồn kho', LedgerMismatch: 'Lượng giữ hàng không khớp', NegativeAvailable: 'Tồn khả dụng âm', ExpiredStillActive: 'Giữ hàng hết hạn chưa xử lý', ActiveWithoutRemaining: 'Giữ hàng không còn số lượng', InvalidSourceReference: 'Tham chiếu phiếu xuất không hợp lệ' };

export default function StockReservations() {
  const permissions = usePermissionSet();
  const canRead = hasPermission('reservation.read'), canCreate = hasPermission('reservation.create'), canRelease = hasPermission('reservation.release');
  const [data, setData] = useState(emptyPage), [status, setStatus] = useState(''), [page, setPage] = useState(1), [reload, setReload] = useState(0);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('');
  const [dialog, setDialog] = useState<'create' | 'detail' | 'release' | 'expire' | 'reconciliation' | null>(null);
  const [selected, setSelected] = useState<Reservation | null>(null), [issues, setIssues] = useState<Issue[]>([]);
  const [products, setProducts] = useState<Product[]>([]), [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [productId, setProductId] = useState(''), [warehouseId, setWarehouseId] = useState(''), [quantity, setQuantity] = useState(''), [reason, setReason] = useState('');
  const epoch = useRef(0), submitting = useRef(false), opening = useRef(false), returnFocus = useRef<HTMLElement | null>(null);
  useEffect(() => { document.title = 'Giữ hàng — ERP KHO'; }, []);
  useEffect(() => { setNotice(''); setReason(''); setQuantity(''); setProductId(''); setWarehouseId(''); }, [permissions]);
  useEffect(() => {
    const generation = ++epoch.current;
    const abort = new AbortController();
    setData(emptyPage); setSelected(null); setDialog(null); setProducts([]); setWarehouses([]); setIssues([]); setError(''); setLoading(canRead);
    if (canRead) apiClient.get('/api/stock-reservations', { params: { page, pageSize: 20, status: status || undefined }, signal: abort.signal })
      .then(response => { if (generation === epoch.current) setData(response.data); })
      .catch(failure => { if (generation === epoch.current && !abort.signal.aborted) setError(permissionError(failure, 'Không thể tải danh sách giữ hàng.')); })
      .finally(() => { if (generation === epoch.current) setLoading(false); });
    return () => { epoch.current = generation + 1; abort.abort(); };
  }, [permissions, canRead, status, page, reload]);

  const open = async (kind: NonNullable<typeof dialog>, trigger: HTMLElement, item?: Reservation) => {
    if (opening.current || submitting.current) return;
    const generation = epoch.current; returnFocus.current = trigger; setError(''); setNotice('');
    if (kind === 'expire') { setDialog(kind); return; }
    opening.current = true; setBusy(true);
    try {
      if (kind === 'create') {
        if (!hasPermission('product.read') || !hasPermission('warehouse.read')) { setError('Bạn cần quyền xem sản phẩm và kho để chọn dữ liệu tạo giữ hàng.'); return; }
        const [p, w] = await Promise.all([apiClient.get('/api/products'), apiClient.get('/api/warehouses')]);
        if (generation !== epoch.current) return;
        setProducts(p.data.filter((x: Product) => x.isActive)); setWarehouses(w.data); setProductId(''); setWarehouseId(''); setQuantity('');
      } else if (kind === 'reconciliation') {
        const response = await apiClient.get('/api/stock-reservations/reconciliation');
        if (generation !== epoch.current) return; setIssues(response.data);
      } else {
        const response = await apiClient.get('/api/stock-reservations/' + item!.id);
        if (generation !== epoch.current) return; setSelected(response.data); setQuantity(String(response.data.remainingQuantity)); setReason('');
      }
      setDialog(kind);
    } catch (failure) { if (generation === epoch.current) setError(permissionError(failure, 'Không thể tải thông tin giữ hàng.')); }
    finally { opening.current = false; setBusy(false); }
  };
  const submit = async () => {
    if (submitting.current) return;
    if ((dialog === 'create' && !hasPermission('reservation.create')) || (dialog !== 'create' && !hasPermission('reservation.release'))) return;
    if (dialog !== 'expire' && (!Number.isFinite(Number(quantity)) || Number(quantity) <= 0)) { setError('Số lượng phải lớn hơn không.'); return; }
    if (dialog === 'release' && (!reason.trim() || !selected?.rowVersion)) { setError('Vui lòng nhập lý do và tải lại thông tin giữ hàng.'); return; }
    const payload = dialog === 'create' ? { warehouseId: Number(warehouseId), productId: Number(productId), quantity: Number(quantity) }
      : dialog === 'release' ? { quantity: Number(quantity), reason: reason.trim(), rowVersion: selected!.rowVersion } : {};
    const url = dialog === 'create' ? '/api/stock-reservations' : dialog === 'release' ? '/api/stock-reservations/' + selected!.id + '/release' : '/api/stock-reservations/expire';
    const action = 'reservation:' + url + ':' + JSON.stringify(payload), generation = epoch.current;
    submitting.current = true; setBusy(true); setError('');
    try {
      const response = await apiClient.post(url, payload, { headers: idempotencyHeaders(action) });
      completeIdempotentAction(action);
      if (generation !== epoch.current) return;
      setNotice(dialog === 'expire' ? 'Đã xử lý ' + response.data.expired + ' giữ hàng thủ công hết hạn.' : dialog === 'create' ? 'Đã tạo giữ hàng thủ công.' : 'Đã giải phóng giữ hàng.');
      setDialog(null); setReload(x => x + 1);
    } catch (failure) { if (generation === epoch.current) setError(permissionError(failure, 'Không thể xử lý giữ hàng. Kiểm tra số lượng, đơn vị tính và tồn khả dụng.')); }
    finally { submitting.current = false; setBusy(false); }
  };
  const close = () => { if (!submitting.current) { setDialog(null); setSelected(null); setError(''); } };
  const pages = Math.max(1, Math.ceil(data.totalRecords / data.pageSize));
  if (!canRead) return <section className="reservations"><header><div><h1>Giữ hàng</h1><p role="alert">Bạn không có quyền thực hiện thao tác này.</p></div></header></section>;
  return <section className="reservations" aria-busy={loading}>
    <header><div><h1>Giữ hàng</h1><p>Theo dõi lượng tồn đã giữ. Giữ hàng của phiếu xuất chỉ được xử lý qua phiếu xuất.</p></div><div className="reservation-actions">
      {canCreate && <button disabled={busy || loading} onClick={e => void open('create', e.currentTarget)}>Tạo giữ hàng</button>}
      {canRelease && <button disabled={busy || loading} onClick={e => void open('expire', e.currentTarget)}>Xử lý hết hạn</button>}
      <button disabled={busy || loading} onClick={e => void open('reconciliation', e.currentTarget)}>Đối chiếu giữ hàng</button>
    </div></header>
    <div className="reservation-tools"><label>Trạng thái<select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }}><option value="">Tất cả</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><button disabled={busy || loading} onClick={() => setReload(x => x + 1)}>Tải lại</button></div>
    {notice && <p role="status">{notice}</p>}{error && !dialog && <div role="alert" className="reservation-error">{error}</div>}
    {busy && !dialog && <p role="status">Đang tải thông tin giữ hàng...</p>}
    <div className="reservation-table"><table><caption className="sr-only">Danh sách giữ hàng trong phạm vi kho được phép</caption><thead><tr><th>Mã giữ</th><th>Sản phẩm</th><th>Kho</th><th>Nguồn</th><th>Ban đầu</th><th>Còn giữ</th><th>Đơn vị</th><th>Hết hạn</th><th>Trạng thái</th><th>Thao tác</th></tr></thead><tbody>
      {loading ? <tr><td colSpan={10}><output>Đang tải giữ hàng...</output></td></tr> : !data.items.length ? <tr><td colSpan={10}>Chưa có giữ hàng phù hợp.</td></tr> : data.items.map(x => <tr key={x.id}>
        <td>{x.reservationCode}</td><td>{x.productCode} — {x.productName}</td><td>{x.warehouseName}</td><td>{x.sourceType === 'Manual' ? 'Thủ công' : x.sourceType === 'ExportReceipt' ? 'Phiếu xuất' : 'Nguồn khác'}{x.sourceCode ? ' / ' + x.sourceCode : ''}</td>
        <td>{x.quantity}</td><td><strong>{x.remainingQuantity}</strong></td><td>{x.baseUomNameSnapshot || 'Chưa lưu đơn vị lịch sử'}</td><td>{new Date(x.expiresAt).toLocaleString('vi-VN')}</td><td>{statuses[x.status] || 'Trạng thái chưa xác định'}</td><td className="reservation-actions">
          <button disabled={busy} onClick={e => void open('detail', e.currentTarget, x)} aria-label={'Chi tiết ' + x.reservationCode}>Chi tiết</button>
          {canRelease && x.sourceType === 'Manual' && x.rowVersion && ['Active', 'PartiallyConsumed'].includes(x.status) && <button disabled={busy} onClick={e => void open('release', e.currentTarget, x)} aria-label={'Giải phóng ' + x.reservationCode}>Giải phóng</button>}
        </td></tr>)}
    </tbody></table></div>
    <footer><button disabled={loading || page <= 1} onClick={() => setPage(x => x - 1)}>Trước</button><span>Trang {data.pageIndex}/{pages} · {data.totalRecords} bản ghi</span><button disabled={loading || page >= pages} onClick={() => setPage(x => x + 1)}>Sau</button></footer>
    {dialog && <AccessibleDialog titleId="reservation-dialog-title" busy={busy} onClose={close} returnFocusTo={returnFocus.current}>
      <h2 id="reservation-dialog-title">{dialog === 'create' ? 'Tạo giữ hàng thủ công' : dialog === 'release' ? 'Giải phóng giữ hàng thủ công' : dialog === 'expire' ? 'Xử lý giữ hàng hết hạn' : dialog === 'reconciliation' ? 'Đối chiếu giữ hàng' : 'Chi tiết giữ hàng'}</h2>
      {error && <p role="alert" className="reservation-error">{error}</p>}
      {dialog === 'create' && <div className="reservation-form"><div><label htmlFor="reservation-warehouse">Kho</label><select id="reservation-warehouse" required value={warehouseId} onChange={e => setWarehouseId(e.target.value)} disabled={busy}><option value="">Chọn kho</option>{warehouses.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}</select></div><div><label htmlFor="reservation-product">Sản phẩm</label><select id="reservation-product" required value={productId} onChange={e => setProductId(e.target.value)} disabled={busy}><option value="">Chọn sản phẩm</option>{products.map(p => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}</select></div><p>Đơn vị gốc: {products.find(p => p.id === Number(productId))?.unitName || 'Chọn sản phẩm để xem'}. Thời hạn mặc định do hệ thống xác định.</p></div>}
      {(dialog === 'create' || dialog === 'release') && <div className="reservation-form"><label>Số lượng<input type="number" step="any" value={quantity} onChange={e => setQuantity(e.target.value)} disabled={busy} /></label>{dialog === 'release' && <label>Lý do<textarea maxLength={500} value={reason} onChange={e => setReason(e.target.value)} disabled={busy} /></label>}</div>}
      {dialog === 'expire' && <p>Chỉ giải phóng giữ hàng thủ công đã hết hạn trong phạm vi kho của bạn. Giữ hàng của phiếu xuất không bị thay đổi.</p>}
      {dialog === 'detail' && selected && <dl><dt>Mã giữ</dt><dd>{selected.reservationCode}</dd><dt>Sản phẩm</dt><dd>{selected.productName}</dd><dt>Kho</dt><dd>{selected.warehouseName}</dd><dt>Còn giữ</dt><dd>{selected.remainingQuantity}</dd><dt>Đơn vị đã lưu</dt><dd>{selected.baseUomNameSnapshot || 'Chưa lưu đơn vị lịch sử'}</dd><dt>Đã sử dụng / giải phóng</dt><dd>{selected.consumedQuantity} / {selected.releasedQuantity}</dd><dt>Trạng thái</dt><dd>{statuses[selected.status] || 'Trạng thái chưa xác định'}</dd></dl>}
      {dialog === 'reconciliation' && <><p>Chỉ đọc và đối chiếu trong phạm vi kho; thao tác này không sửa số dư.</p>{issues.length ? <ul>{issues.map((x, i) => <li key={i}>{issueLabels[x.issue] || 'Chênh lệch cần kiểm tra'} · Kho #{x.warehouseId}, sản phẩm #{x.productId}: lượng giữ {x.ledgerReserved ?? 'chưa xác định'}, số dư ghi nhận {x.stockReserved ?? 'chưa xác định'}</li>)}</ul> : <p>Không phát hiện chênh lệch giữ hàng.</p>}</>}
      <div className="reservation-actions"><button disabled={busy} onClick={close}>Đóng</button>{['create', 'release', 'expire'].includes(dialog) && <button disabled={busy || (dialog === 'create' && (!productId || !warehouseId))} onClick={() => void submit()}>{busy ? 'Đang xử lý...' : dialog === 'create' ? 'Xác nhận tạo' : dialog === 'release' ? 'Xác nhận giải phóng' : 'Xác nhận xử lý hết hạn'}</button>}</div>
    </AccessibleDialog>}
  </section>;
}
