import { useEffect, useState } from 'react';
import { RefreshCw, Unlock } from 'lucide-react';
import apiClient from '../services/apiClient';
import './StockReservations.css';

type Reservation = {
  id: number; reservationCode: string; productCode: string; productName: string;
  warehouseName: string; quantity: number; consumedQuantity: number; releasedQuantity: number;
  remainingQuantity: number; status: string; sourceType: string; sourceCode?: string;
  createdAt: string; expiresAt: string;
};
type Page = { items: Reservation[]; totalRecords: number; pageIndex: number; pageSize: number };

export default function StockReservations() {
  const [data, setData] = useState<Page>({ items: [], totalRecords: 0, pageIndex: 1, pageSize: 20 });
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = async (page = data.pageIndex) => {
    setLoading(true); setError('');
    try { setData((await apiClient.get('/api/stock-reservations', { params: { page, pageSize: 20, status: status || undefined } })).data); }
    catch { setError('Không thể tải danh sách giữ hàng.'); }
    finally { setLoading(false); }
  };
  useEffect(() => { void load(1); /* oxlint-disable-next-line react-hooks/exhaustive-deps */ }, [status]);

  const release = async (item: Reservation) => {
    if (!confirm(`Giải phóng ${item.remainingQuantity} của ${item.reservationCode}?`)) return;
    try { await apiClient.post(`/api/stock-reservations/${item.id}/release`, { reason: 'Released from reservation screen' }); await load(); }
    catch (e: any) { setError(e.response?.data?.message || 'Không thể giải phóng giữ hàng.'); }
  };
  const expire = async () => { try { await apiClient.post('/api/stock-reservations/expire'); await load(1); } catch { setError('Không thể dọn reservation hết hạn.'); } };
  const pages = Math.max(1, Math.ceil(data.totalRecords / data.pageSize));

  return <section className="reservations">
    <header><div><h1>Giữ hàng</h1><p>Theo dõi lượng tồn đã cam kết theo kho và chứng từ.</p></div><button title="Dọn reservation hết hạn" onClick={() => void expire()}><RefreshCw size={17}/> Dọn hết hạn</button></header>
    <div className="reservation-tools"><label>Trạng thái<select value={status} onChange={e => setStatus(e.target.value)}><option value="">Tất cả</option>{['Active','PartiallyConsumed','Consumed','Released','Expired','Cancelled'].map(x => <option key={x}>{x}</option>)}</select></label></div>
    {error && <div className="reservation-error">{error}</div>}
    <div className="reservation-table"><table><thead><tr><th>Mã giữ</th><th>Sản phẩm</th><th>Kho</th><th>Nguồn</th><th>Ban đầu</th><th>Còn giữ</th><th>Hết hạn</th><th>Trạng thái</th><th></th></tr></thead><tbody>
      {loading ? <tr><td colSpan={9}>Đang tải...</td></tr> : data.items.length === 0 ? <tr><td colSpan={9}>Không có reservation.</td></tr> : data.items.map(x => <tr key={x.id}><td>{x.reservationCode}</td><td>{x.productCode} - {x.productName}</td><td>{x.warehouseName}</td><td>{x.sourceType}{x.sourceCode ? ` / ${x.sourceCode}` : ''}</td><td>{x.quantity}</td><td><strong>{x.remainingQuantity}</strong></td><td>{new Date(x.expiresAt).toLocaleString()}</td><td><span className={`reservation-status ${x.status.toLowerCase()}`}>{x.status}</span></td><td>{['Active','PartiallyConsumed'].includes(x.status) && <button className="icon" title="Giải phóng" onClick={() => void release(x)}><Unlock size={17}/></button>}</td></tr>)}
    </tbody></table></div>
    <footer><button disabled={data.pageIndex <= 1} onClick={() => void load(data.pageIndex - 1)}>Trước</button><span>Trang {data.pageIndex}/{pages} · {data.totalRecords} bản ghi</span><button disabled={data.pageIndex >= pages} onClick={() => void load(data.pageIndex + 1)}>Sau</button></footer>
  </section>;
}
