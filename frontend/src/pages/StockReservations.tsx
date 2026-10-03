import { useCallback, useEffect, useState } from 'react';
import { RefreshCw, Unlock } from 'lucide-react';
import apiClient from '../services/apiClient';
import { currentRole } from '../services/authorization';
import {
  UiBadge,
  UiCard,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';

type Reservation = {
  id: number;
  reservationCode: string;
  productCode: string;
  productName: string;
  warehouseName: string;
  quantity: number;
  consumedQuantity: number;
  releasedQuantity: number;
  remainingQuantity: number;
  status: string;
  sourceType: string;
  sourceCode?: string;
  createdAt: string;
  expiresAt: string;
};

type Page = {
  items: Reservation[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
};

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { data?: { message?: string } } };
  return response.response?.data?.message || fallback;
};

const toneOf = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
  if (status === 'Consumed') return 'success';
  if (status === 'Active' || status === 'PartiallyConsumed') return 'warning';
  if (status === 'Cancelled' || status === 'Expired') return 'danger';
  return 'neutral';
};

export default function StockReservations() {
  const canOperate = currentRole() !== 'Viewer';
  const [data, setData] = useState<Page>({ items: [], totalRecords: 0, pageIndex: 1, pageSize: 20 });
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async (page = 1) => {
    setLoading(true);
    setError('');
    try {
      const response = await apiClient.get('/api/stock-reservations', {
        params: { page, pageSize: 20, status: status || undefined },
      });
      setData(response.data);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể tải danh sách giữ hàng.'));
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => {
    void load(1);
  }, [load]);

  const release = async (item: Reservation) => {
    if (busy || !window.confirm(`Giải phóng ${item.remainingQuantity} của ${item.reservationCode}?`)) return;
    setBusy(true);
    setError('');
    try {
      await apiClient.post(`/api/stock-reservations/${item.id}/release`, {
        reason: 'Released from reservation screen',
      });
      await load(data.pageIndex);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể giải phóng giữ hàng.'));
    } finally {
      setBusy(false);
    }
  };

  const expire = async () => {
    if (busy) return;
    setBusy(true);
    setError('');
    try {
      await apiClient.post('/api/stock-reservations/expire');
      await load(1);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể dọn reservation hết hạn.'));
    } finally {
      setBusy(false);
    }
  };

  const pages = Math.max(1, Math.ceil(data.totalRecords / data.pageSize));
  const remainingOnPage = data.items.reduce((sum, item) => sum + item.remainingQuantity, 0);

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Outbound"
        title="Giữ hàng"
        description="Theo dõi lượng tồn đã cam kết theo kho, sản phẩm, chứng từ nguồn và thời hạn reservation."
        actions={canOperate ? (
          <button type="button" disabled={busy} onClick={() => void expire()}>
            <RefreshCw size={16} aria-hidden="true" /> Dọn hết hạn
          </button>
        ) : undefined}
      />

      {error && <p role="alert">{error}</p>}

      <UiMetricGrid>
        <UiMetric value={data.totalRecords} label="Reservation phù hợp" />
        <UiMetric value={data.items.length} label="Bản ghi trên trang" />
        <UiMetric value={remainingOnPage} label="Số lượng còn giữ trên trang" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Trạng thái">
          <select
            aria-label="Lọc trạng thái giữ hàng"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
          >
            <option value="">Tất cả</option>
            {['Active', 'PartiallyConsumed', 'Consumed', 'Released', 'Expired', 'Cancelled'].map((value) => (
              <option key={value} value={value}>{value}</option>
            ))}
          </select>
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">
          Trang {data.pageIndex}/{pages} • {data.totalRecords} bản ghi
        </div>
      </UiToolbar>

      <UiCard title="Danh sách giữ hàng">
        <UiTableScroll>
          <table aria-label="Danh sách giữ hàng">
            <thead>
              <tr>
                <th>Mã giữ</th>
                <th>Sản phẩm</th>
                <th>Kho</th>
                <th>Nguồn</th>
                <th>Ban đầu</th>
                <th>Còn giữ</th>
                <th>Hết hạn</th>
                <th>Trạng thái</th>
                {canOperate && <th>Thao tác</th>}
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td className="ui-empty-cell" colSpan={canOperate ? 9 : 8}>Đang tải...</td></tr>
              ) : data.items.length === 0 ? (
                <tr><td className="ui-empty-cell" colSpan={canOperate ? 9 : 8}>Không có reservation phù hợp.</td></tr>
              ) : data.items.map((item) => (
                <tr key={item.id}>
                  <td><strong>{item.reservationCode}</strong></td>
                  <td>{item.productCode} – {item.productName}</td>
                  <td>{item.warehouseName}</td>
                  <td>{item.sourceType}{item.sourceCode ? ` / ${item.sourceCode}` : ''}</td>
                  <td>{item.quantity}</td>
                  <td><strong>{item.remainingQuantity}</strong></td>
                  <td>{new Date(item.expiresAt).toLocaleString('vi-VN')}</td>
                  <td><UiBadge tone={toneOf(item.status)}>{item.status}</UiBadge></td>
                  {canOperate && <td>
                    {['Active', 'PartiallyConsumed'].includes(item.status) ? (
                      <button
                        type="button"
                        disabled={busy}
                        aria-label={`Giải phóng ${item.reservationCode}`}
                        onClick={() => void release(item)}
                      >
                        <Unlock size={16} aria-hidden="true" /> Giải phóng
                      </button>
                    ) : '—'}
                  </td>}
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>

        {pages > 1 && (
          <div className="ui-pagination" aria-label="Phân trang giữ hàng">
            <button type="button" disabled={loading || data.pageIndex <= 1} onClick={() => void load(data.pageIndex - 1)}>Trước</button>
            <span>Trang {data.pageIndex} / {pages}</span>
            <button type="button" disabled={loading || data.pageIndex >= pages} onClick={() => void load(data.pageIndex + 1)}>Sau</button>
          </div>
        )}
      </UiCard>
    </UiPage>
  );
}
