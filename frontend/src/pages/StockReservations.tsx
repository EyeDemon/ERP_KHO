import { useCallback, useEffect, useState } from 'react';
import { Link, useInRouterContext } from 'react-router-dom';
import { RefreshCw, Unlock } from 'lucide-react';
import apiClient from '../services/apiClient';
import { currentRole } from '../services/authorization';
import { conflictErrorMessage, extractConflictCode } from '../services/permissionPresentation';
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
  allocatedQuantity: number;
  remainingQuantity: number;
  allocatableQuantity: number;
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
  const response = failure as { response?: { status?: number; data?: { message?: string; code?: string; errorCode?: string; error?: { code?: string } } } };
  if (response.response?.status === 403) return 'Bạn không có quyền thực hiện thao tác này.';
  if (response.response?.status === 409) return conflictErrorMessage(failure, response.response?.data?.message || fallback);
  return response.response?.data?.message || fallback;
};

const toneOf = (status: string): 'neutral' | 'success' | 'warning' | 'danger' => {
  if (status === 'Consumed') return 'success';
  if (status === 'Active' || status === 'PartiallyConsumed' || status === 'PartiallyAllocated' || status === 'Allocated') return 'warning';
  if (status === 'Cancelled' || status === 'Expired') return 'danger';
  return 'neutral';
};

const reservationStatusLabel = (status: string) => ({
  Active: 'Đang giữ',
  PartiallyConsumed: 'Đã xuất một phần',
  PartiallyAllocated: 'Đã phân bổ một phần',
  Allocated: 'Đã phân bổ',
  Consumed: 'Đã xuất kho',
  Released: 'Đã giải phóng',
  Expired: 'Đã hết hạn',
  Cancelled: 'Đã hủy',
}[status] ?? status);

const sourceTypeLabel = (sourceType: string) => ({
  ExportReceipt: 'Phiếu xuất kho',
  SalesOrder: 'Đơn bán hàng',
  StockTransfer: 'Phiếu điều chuyển',
  OutboundOrder: 'Đơn xuất hàng',
  Manual: 'Giữ hàng thủ công',
}[sourceType] ?? sourceType);

export default function StockReservations() {
  const inRouter = useInRouterContext();
  const canOperate = currentRole() !== 'Viewer';
  const [data, setData] = useState<Page>({ items: [], totalRecords: 0, pageIndex: 1, pageSize: 20 });
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [sweepPreview, setSweepPreview] = useState<{ sweepId: string; status: string } | null>(null);
  const [sweepBusy, setSweepBusy] = useState(false);

  const sweepExpiredPreview = async () => {
    if (sweepBusy) return;
    setSweepBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/stock-reservations/sweep-expired-preview', {});
      if (res?.data?.sweepId) {
        setSweepPreview({
          sweepId: String(res.data.sweepId),
          status: String(res.data.status ?? 'VERIFIED'),
        });
      } else {
        setSweepPreview(null);
        setError('Phản hồi rà soát giữ hàng sắp hết hạn không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setSweepPreview(null);
      setError(messageOf(failure, 'Không thể rà soát giữ hàng sắp hết hạn.'));
    } finally {
      setSweepBusy(false);
    }
  };

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
      if (['CONCURRENCY_CONFLICT', 'INVALID_STATE_TRANSITION'].includes(extractConflictCode(failure) || '')) {
        await load(data.pageIndex);
      }
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
      setError(messageOf(failure, 'Không thể dọn bản ghi giữ hàng hết hạn.'));
    } finally {
      setBusy(false);
    }
  };

  const pages = Math.max(1, Math.ceil(data.totalRecords / data.pageSize));
  const remainingOnPage = data.items.reduce((sum, item) => sum + item.remainingQuantity, 0);
  const isCommittedReservation = (item: Reservation) =>
    item.status === 'Active' || item.status === 'Allocated' || item.status === 'PartiallyConsumed' || item.status === 'PartiallyAllocated';
  const activeItems = data.items.filter(isCommittedReservation);
  const activeCount = activeItems.length;
  const activeRemainingQuantity = activeItems.reduce((sum, item) => sum + item.remainingQuantity, 0);
  const expiringSoonCount = activeItems.filter(
    (item) =>
      Boolean(item.expiresAt) &&
      new Date(item.expiresAt).getTime() <= Date.now() + 24 * 60 * 60 * 1000,
  ).length;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Xuất kho"
        title="Giữ hàng"
        description="Theo dõi lượng tồn đã cam kết theo kho, sản phẩm, chứng từ nguồn và thời hạn giữ hàng."
        actions={canOperate ? (
          <button type="button" disabled={busy} onClick={() => void expire()}>
            <RefreshCw size={16} aria-hidden="true" /> Dọn hết hạn
          </button>
        ) : undefined}
      />

      <div
        data-testid="reservation-allocation-tabs"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          marginBottom: 12,
          padding: 6,
          borderRadius: 10,
          background: '#f1f5f9',
          border: '1px solid #cbd5e1',
        }}
      >
        {[
          { to: '/stock-reservations', label: 'Tab 1: Giữ hàng tồn kho (/stock-reservations)', testId: 'tab-stock-reservations', active: true },
          { to: '/stock-allocations', label: 'Tab 2: Phân bổ vị trí kệ (/stock-allocations)', testId: 'tab-stock-allocations', active: false },
        ].map((tab) => {
          const style: React.CSSProperties = {
            padding: '8px 14px',
            borderRadius: 8,
            fontWeight: 700,
            fontSize: 13,
            textDecoration: 'none',
            background: tab.active ? '#0f172a' : '#ffffff',
            color: tab.active ? '#ffffff' : '#334155',
            border: tab.active ? '1px solid #0f172a' : '1px solid #cbd5e1',
          };
          return inRouter ? (
            <Link key={tab.to} to={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </Link>
          ) : (
            <a key={tab.to} href={tab.to} data-testid={tab.testId} aria-current={tab.active ? 'page' : undefined} style={style}>
              {tab.label}
            </a>
          );
        })}
      </div>

      {error && <p role="alert">{error}</p>}

      <UiMetricGrid>
        <UiMetric value={data.totalRecords} label="Bản ghi giữ hàng phù hợp" />
        <UiMetric value={data.items.length} label="Bản ghi trên trang" />
        <UiMetric value={remainingOnPage} label="Số lượng còn giữ trên trang" />
      </UiMetricGrid>

      <UiCard title="Tổng quan Cam kết Giữ hàng & Rủi ro Hết hạn">
        <UiMetricGrid>
          <UiMetric value={activeCount} label="Bản ghi giữ hàng đang hiệu lực" />
          <UiMetric value={activeRemainingQuantity} label="Tổng lượng đang giữ" />
          <UiMetric value={expiringSoonCount} label="Bản ghi sắp hết hạn (24h)" />
        </UiMetricGrid>
        <div className="ui-inline-actions" style={{ marginTop: 8 }}>
          <button type="button" disabled={sweepBusy || busy} onClick={() => void sweepExpiredPreview()}>
            Rà soát &amp; Cảnh báo Giữ hàng Sắp hết hạn
          </button>
          {sweepPreview && (
            <UiBadge tone="success">
              {sweepPreview.sweepId} • {sweepPreview.status === 'VERIFIED' ? 'ĐÃ XÁC MINH' : sweepPreview.status}
            </UiBadge>
          )}
        </div>
      </UiCard>

      <UiToolbar>
        <UiToolbarField label="Trạng thái">
          <select
            aria-label="Lọc trạng thái giữ hàng"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
          >
            <option value="">Tất cả</option>
            {['Active', 'PartiallyConsumed', 'PartiallyAllocated', 'Allocated', 'Consumed', 'Released', 'Expired', 'Cancelled'].map((value) => (
              <option key={value} value={value}>{reservationStatusLabel(value)}</option>
            ))}
          </select>
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">
          Trang {data.pageIndex}/{pages} • {data.totalRecords} bản ghi
        </div>
      </UiToolbar>

      <UiCard title="Danh sách giữ hàng">
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Danh sách giữ hàng">
            <thead>
              <tr>
                <th style={{ whiteSpace: 'nowrap', minWidth: 155 }}>Mã giữ</th>
                <th>Sản phẩm</th>
                <th>Kho</th>
                <th>Nguồn</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Ban đầu</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Còn giữ</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Đã phân bổ</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>Chưa phân bổ</th>
                <th>Hết hạn</th>
                <th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th>
                {canOperate && <th style={{ whiteSpace: 'nowrap', minWidth: 115 }}>Thao tác</th>}
              </tr>
            </thead>
            <tbody>
              {loading && data.items.length === 0 ? (
                <tr><td className="ui-empty-cell" colSpan={canOperate ? 11 : 10}>Đang tải...</td></tr>
              ) : data.items.length === 0 ? (
                <tr><td className="ui-empty-cell" colSpan={canOperate ? 11 : 10}>Không có bản ghi giữ hàng phù hợp.</td></tr>
              ) : data.items.map((item) => (
                <tr key={item.id}>
                  <td style={{ whiteSpace: 'nowrap' }}><strong style={{ whiteSpace: 'nowrap' }}>{item.reservationCode}</strong></td>
                  <td>{item.productCode} – {item.productName}</td>
                  <td>{item.warehouseName}</td>
                  <td>{sourceTypeLabel(item.sourceType)}{item.sourceCode ? ` / ${item.sourceCode}` : ''}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.quantity}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}><strong>{item.remainingQuantity}</strong></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.allocatedQuantity}</td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{item.allocatableQuantity}</td>
                  <td>{new Date(item.expiresAt).toLocaleString('vi-VN')}</td>
                  <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone={toneOf(item.status)}>{reservationStatusLabel(item.status)}</UiBadge></td>
                  {canOperate && <td style={{ whiteSpace: 'nowrap' }}>
                    {item.status === 'Active' && item.sourceType === 'Manual' ? (
                      <button
                        type="button"
                        disabled={busy}
                        style={{ whiteSpace: 'nowrap' }}
                        aria-label={`Giải phóng ${item.reservationCode}`}
                        onClick={() => void release(item)}
                      >
                        <Unlock size={16} aria-hidden="true" /> Giải phóng
                      </button>
                    ) : ['Active', 'PartiallyConsumed', 'PartiallyAllocated', 'Allocated'].includes(item.status) && item.sourceType !== 'Manual' ? (
                      <span title="Bản ghi giữ hàng này thuộc quyền quản lý của chứng từ gốc. Vui lòng thao tác trực tiếp trên Phiếu xuất hoặc Đơn hàng.">
                        <UiBadge tone="neutral">Quản lý theo chứng từ</UiBadge>
                      </span>
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
