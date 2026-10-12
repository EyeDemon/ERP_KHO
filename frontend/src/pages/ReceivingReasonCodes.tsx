import { useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { hasPermission, usePermissionSet } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';
import AccessibleDialog from '../components/AccessibleDialog';
import './StockReservations.css';
import './Approvals.css';

type Reason = { id: number; code: string; name: string; category: string; version: number; isActive: boolean;
  effectiveFromUtc: string; effectiveToUtc: string | null; effectivity: number;
  requiresNote: boolean; requiresAttachment: boolean; requiresApproval: boolean };
type Page = { items: Reason[]; totalRecords: number; totalPages: number };
const empty: Page = { items: [], totalRecords: 0, totalPages: 0 };
const effectivityLabels = ['Chưa hiệu lực', 'Đang hiệu lực', 'Hết hiệu lực'];
const categoryLabels: Record<string, string> = { Receiving: 'Nhận hàng', Quantity: 'Số lượng', Condition: 'Tình trạng', Identity: 'Nhận diện', Custody: 'Bàn giao', Uom: 'Đơn vị tính', Counting: 'Kiểm đếm' };
const utc = (date: string | null) => date ? new Intl.DateTimeFormat('vi-VN', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23', timeZone: 'UTC' }).format(new Date(date)) + ' UTC' : 'Không giới hạn';
const yes = (value: boolean) => value ? 'Có' : 'Không';

function ReasonTable({ items, onDetail }: { items: Reason[]; onDetail?: (item: Reason, trigger: HTMLElement) => void }) {
  return <div className="reservation-table" role="region" aria-label={onDetail ? 'Danh mục lý do nhận hàng' : 'Lịch sử phiên bản danh mục gốc'} tabIndex={0}>
    <table><thead><tr><th scope="col">Mã lý do</th><th scope="col">Tên lý do</th><th scope="col">Phiên bản</th><th scope="col">Trạng thái sử dụng</th><th scope="col">Thời gian hiệu lực</th><th scope="col">Bắt đầu (UTC)</th><th scope="col">Kết thúc (UTC, không bao gồm)</th>{onDetail && <th scope="col">Tra cứu</th>}</tr></thead>
      <tbody>{items.map(item => <tr key={item.id}><td>{item.code}</td><td>{item.name}</td><td>{item.version}</td><td>{item.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</td><td>{effectivityLabels[item.effectivity] || 'Chưa xác định'}</td><td>{utc(item.effectiveFromUtc)}</td><td>{utc(item.effectiveToUtc)}</td>{onDetail && <td><button type="button" onClick={event => onDetail(item, event.currentTarget)} aria-label={`Chi tiết ${item.code} phiên bản ${item.version}`}>Chi tiết</button></td>}</tr>)}</tbody>
    </table>
  </div>;
}

export default function ReceivingReasonCodes() {
  const permissions = usePermissionSet(), canRead = hasPermission('reason_code.read');
  const [search, setSearch] = useState(''), [keyword, setKeyword] = useState(''), [active, setActive] = useState(''), [effectivity, setEffectivity] = useState('');
  const [page, setPage] = useState(1), [reload, setReload] = useState(0), [data, setData] = useState(empty);
  const [loading, setLoading] = useState(true), [error, setError] = useState('');
  const [dialog, setDialog] = useState(false), [detail, setDetail] = useState<Reason | null>(null), [versions, setVersions] = useState<Reason[]>([]);
  const [detailLoading, setDetailLoading] = useState(false), [detailError, setDetailError] = useState('');
  const epoch = useRef(0), detailEpoch = useRef(0), opening = useRef(false), returnFocus = useRef<HTMLElement | null>(null);
  useEffect(() => { document.title = 'Lý do nhận hàng — ERP KHO'; }, []);
  useEffect(() => {
    const requests = epoch, detailRequests = detailEpoch;
    const generation = ++epoch.current, abort = new AbortController();
    detailEpoch.current++; opening.current = false;
    setData(empty); setDialog(false); setDetail(null); setVersions([]); setDetailError(''); setError(''); setLoading(canRead);
    if (canRead) apiClient.get('/api/receiving-reason-codes', { params: { keyword: keyword || undefined, active: active || undefined, effectivity: effectivity || undefined, page, pageSize: 20 }, signal: abort.signal })
      .then(response => { if (generation === epoch.current) setData(response.data); })
      .catch(failure => { if (generation === epoch.current && !abort.signal.aborted) setError(permissionError(failure, 'Không thể tải danh mục lý do nhận hàng.')); })
      .finally(() => { if (generation === epoch.current) setLoading(false); });
    return () => { requests.current++; detailRequests.current++; abort.abort(); };
  }, [permissions, canRead, keyword, active, effectivity, page, reload]);

  const close = () => { detailEpoch.current++; opening.current = false; setDialog(false); setDetail(null); setVersions([]); };
  const open = async (item: Reason, trigger: HTMLElement) => {
    if (opening.current || !hasPermission('reason_code.read')) return;
    const generation = epoch.current, request = ++detailEpoch.current;
    opening.current = true; returnFocus.current = trigger;
    setDialog(true); setDetail(null); setVersions([]); setDetailError(''); setDetailLoading(true);
    try {
      const [selected, history] = await Promise.all([apiClient.get(`/api/receiving-reason-codes/${item.id}`), apiClient.get(`/api/receiving-reason-codes/versions/${encodeURIComponent(item.code)}`)]);
      if (generation !== epoch.current || request !== detailEpoch.current) return;
      setDetail(selected.data); setVersions(history.data);
    } catch (failure) {
      if (generation === epoch.current && request === detailEpoch.current) setDetailError(permissionError(failure, 'Không thể tải chi tiết và lịch sử phiên bản.'));
    } finally {
      if (generation === epoch.current && request === detailEpoch.current) { opening.current = false; setDetailLoading(false); }
    }
  };

  return <section className="reservations" aria-label="Tra cứu lý do nhận hàng">
    <h1>Lý do nhận hàng</h1>
    {!canRead ? <p role="alert">Bạn không có quyền thực hiện thao tác này.</p> : <>
    <p>Danh mục dùng chung, chỉ đọc. Trạng thái hoạt động độc lập với thời gian hiệu lực. Tất cả thời điểm hiển thị theo UTC.</p>
    <form className="reservation-tools" style={{ flexWrap: 'wrap' }} onSubmit={event => { event.preventDefault(); setPage(1); setKeyword(search.trim()); setReload(value => value + 1); }}>
      <label htmlFor="reason-search">Mã hoặc tên lý do<input id="reason-search" value={search} maxLength={200} onChange={event => setSearch(event.target.value)} style={{ minHeight: 44, padding: 8, font: 'inherit' }} /></label>
      <label htmlFor="reason-active">Trạng thái sử dụng<select id="reason-active" value={active} onChange={event => { setPage(1); setActive(event.target.value); }}><option value="">Tất cả</option><option value="true">Đang hoạt động</option><option value="false">Ngừng hoạt động</option></select></label>
      <label htmlFor="reason-effectivity">Thời gian hiệu lực<select id="reason-effectivity" value={effectivity} onChange={event => { setPage(1); setEffectivity(event.target.value); }}><option value="">Tất cả</option>{effectivityLabels.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
      <button type="submit" disabled={loading}>Tìm kiếm</button><button type="button" disabled={loading} onClick={() => setReload(value => value + 1)}>Tải lại</button>
    </form>
    {error && <p role="alert">{error}</p>}
    {loading ? <p role="status">Đang tải danh mục lý do nhận hàng...</p> : !error && <>
      <p role="status">Tìm thấy {data.totalRecords} phiên bản.</p>
      {data.items.length ? <ReasonTable items={data.items} onDetail={(item, trigger) => void open(item, trigger)} /> : <p>Không có lý do nhận hàng phù hợp.</p>}
      <footer aria-label="Phân trang danh mục lý do"><button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Trang trước</button><span>Trang {page} / {Math.max(1, data.totalPages)}</span><button type="button" disabled={page >= data.totalPages} onClick={() => setPage(value => value + 1)}>Trang sau</button></footer>
    </>}
    {dialog && <AccessibleDialog titleId="reason-detail-title" onClose={close} returnFocusTo={returnFocus.current}>
      <section className="reservations"><h2 id="reason-detail-title">Chi tiết lý do và lịch sử phiên bản</h2><button type="button" onClick={close} data-initial-focus>Đóng</button>
        <p>Phiên bản danh mục gốc khác bản chụp đã lưu trên giao dịch. Tra cứu này không đọc chứng từ hoặc thay đổi bản chụp lịch sử.</p>
        {detailLoading && <p role="status">Đang tải chi tiết và lịch sử...</p>}{detailError && <p role="alert">{detailError}</p>}
        {detail && <><h3>{detail.code} — {detail.name}, phiên bản {detail.version}</h3><dl><dt>Nhóm lý do</dt><dd>{categoryLabels[detail.category] || 'Nhóm khác'}</dd><dt>Yêu cầu ghi chú</dt><dd>{yes(detail.requiresNote)}</dd><dt>Yêu cầu minh chứng</dt><dd>{yes(detail.requiresAttachment)}</dd><dt>Yêu cầu phê duyệt</dt><dd>{yes(detail.requiresApproval)}</dd></dl>
          <h3>Lịch sử phiên bản danh mục gốc</h3><p>Bắt đầu được bao gồm; kết thúc không được bao gồm. Các phiên bản chồng khoảng hiệu lực được hiển thị riêng, không chọn phiên bản thắng.</p><ReasonTable items={versions} /></>}
      </section>
    </AccessibleDialog>}
    </>}
  </section>;
}
