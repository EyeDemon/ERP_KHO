import { useCallback, useEffect, useRef, useState } from 'react';
import { Check, Eye, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { approvalDisplayAction, validRejectReason } from '../services/approval';
import AccessibleDialog from '../components/AccessibleDialog';
import { hasPermission, usePermissionSet } from '../services/authorization';
import { permissionError } from '../services/permissionPresentation';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';
import './Approvals.css';

type SlaStatus = 'Normal' | 'Warning' | 'Overdue';
type QueueItem = {
  documentType: string;
  documentId: number;
  documentCode: string;
  pendingState: string;
  creatorName: string;
  requestedAtUtc: string;
  waitingMinutes?: number | null;
  slaStatus?: SlaStatus | null;
  warehouseName: string;
  destinationWarehouseName?: string;
  totalQuantity: number;
  canApprove: boolean;
  canReject: boolean;
  deniedReasonCode?: string;
};
type HistoryItem = {
  id: number;
  timestampUtc: string;
  actorName: string;
  displayAction: string;
  oldState?: string;
  newState?: string;
  result?: string;
  reason?: string;
  correlationId?: string;
};
type ApprovalDetail = {
  summary: QueueItem;
  note?: string;
  lines: { productCode: string; productName: string; unitName: string; quantity: number }[];
  history: HistoryItem[];
};
type Page<T> = {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
};

const errorText = (error: unknown) => {
  const response = (error as {
    response?: {
      status?: number;
      data?: { correlationId?: string };
      headers?: Record<string, string>;
    };
  }).response;
  const messages: Record<number, string> = {
    401: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
    403: 'Bạn không có quyền thực hiện thao tác này.',
    404: 'Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.',
    409: 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.',
  };
  const message = messages[response?.status || 0] || permissionError(error);
  const correlation = response?.data?.correlationId || response?.headers?.['x-correlation-id'];
  return correlation && /^[a-zA-Z0-9-]{1,64}$/.test(correlation)
    ? `${message} Mã đối soát: ${correlation}`
    : message;
};

const stateLabels: Record<string, string> = {
  Draft: 'Nháp',
  Received: 'Đã nhận hàng',
  QcCompleted: 'Kiểm tra chất lượng đã hoàn tất — chờ duyệt',
  ReadyToPost: 'Chờ ghi nhận tồn kho',
  Posted: 'Đã ghi nhận tồn kho',
  Cancelled: 'Đã hủy',
  Approved: 'Đã duyệt',
};
const documentLabels: Record<string, string> = {
  ImportReceipt: 'Nhập kho',
  ExportReceipt: 'Xuất kho',
  StockTransfer: 'Điều chuyển',
  Stocktake: 'Kiểm kê',
};
const slaLabels: Record<SlaStatus, string> = {
  Normal: 'Bình thường',
  Warning: 'Sắp đến hạn',
  Overdue: 'Quá hạn',
};

const waitingText = (minutes?: number | null) => {
  if (minutes == null) return 'Không áp dụng';
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (days > 0) return `${days} ngày ${hours % 24} giờ`;
  if (hours > 0) return `${hours} giờ ${minutes % 60} phút`;
  return `${minutes} phút`;
};

const slaTone = (status: SlaStatus): 'success' | 'warning' | 'danger' => {
  if (status === 'Normal') return 'success';
  if (status === 'Warning') return 'warning';
  return 'danger';
};

export default function Approvals() {
  const permissionSnapshot = usePermissionSet();
  const [page, setPage] = useState(1);
  const [type, setType] = useState('');
  const [keyword, setKeyword] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [creatorId, setCreatorId] = useState('');
  const [fromUtc, setFromUtc] = useState('');
  const [toUtc, setToUtc] = useState('');
  const [slaStatus, setSlaStatus] = useState('');
  const [data, setData] = useState<Page<QueueItem>>({
    items: [],
    totalRecords: 0,
    pageIndex: 1,
    pageSize: 20,
    totalPages: 0,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [selected, setSelected] = useState<QueueItem | null>(null);
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const [detail, setDetail] = useState<ApprovalDetail | null>(null);
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const [approving, setApproving] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const submitting = useRef(false);
  const requestVersion = useRef(0);
  const [dialogTrigger, setDialogTrigger] = useState<HTMLElement | null>(null);

  const closeDialog = useCallback(() => {
    setSelected(null);
    setRejecting(false);
    setApproving(false);
    setReason('');
  }, []);

  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    setLoading(true);
    try {
      const response = await apiClient.get<Page<QueueItem>>('/api/approvals/queue', {
        params: {
          pageIndex: page,
          pageSize: 20,
          documentType: type || undefined,
          keyword: keyword || undefined,
          warehouseId: warehouseId || undefined,
          creatorId: creatorId || undefined,
          fromUtc: fromUtc ? new Date(fromUtc).toISOString() : undefined,
          toUtc: toUtc ? new Date(toUtc).toISOString() : undefined,
          slaStatus: slaStatus || undefined,
        },
      });
      if (version === requestVersion.current) setData(response.data);
    } catch (failure) {
      if (version !== requestVersion.current) return;
      setData({ items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 0 });
      setSelected(null);
      setDetail(null);
      setHistory([]);
      setError(errorText(failure));
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, [page, type, keyword, warehouseId, creatorId, fromUtc, toUtc, slaStatus]);

  useEffect(() => {
    const requests = requestVersion;
    setSelected(null);
    setDetail(null);
    setHistory([]);
    void load();
    return () => {
      requests.current++;
    };
  }, [load, permissionSnapshot]);

  const open = async (item: QueueItem, trigger: HTMLElement) => {
    const version = ++requestVersion.current;
    setError('');
    setApproving(false);
    setRejecting(false);
    setDialogTrigger(trigger);
    setSelected(item);
    setHistory([]);
    setDetail(null);
    setDetailLoading(true);
    try {
      const response = await apiClient.get<ApprovalDetail>(
        `/api/approvals/${item.documentType}/${item.documentId}`
      );
      if (version === requestVersion.current) {
        setDetail(response.data);
        setHistory(response.data.history);
      }
    } catch (failure) {
      if (version === requestVersion.current) {
        setSelected(null);
        setDetail(null);
        setHistory([]);
        setError(errorText(failure));
      }
    } finally {
      if (version === requestVersion.current) setDetailLoading(false);
    }
  };

  const canApproveItem = (item: QueueItem) =>
    item.canApprove &&
    (item.documentType === 'ExportReceipt'
      ? hasPermission('export_receipt.approve')
      : item.documentType !== 'ImportReceipt' ||
        (hasPermission('receipt.complete') &&
          (item.pendingState !== 'QcCompleted' || hasPermission('quality_disposition.approve'))));

  const canRejectItem = (item: QueueItem) =>
    item.canReject &&
    (item.documentType === 'ExportReceipt'
      ? hasPermission('approval.reject') && hasPermission('export_receipt.cancel')
      : item.documentType !== 'ImportReceipt' || hasPermission('approval.reject'));

  const approve = async (item: QueueItem) => {
    if (submitting.current || !canApproveItem(item)) return;
    submitting.current = true;
    setError('');
    setBusy(true);
    const key = `approve:${item.documentType}:${item.documentId}`;
    try {
      const routes: Record<string, string> = {
        ImportReceipt: `/api/ImportReceipts/${item.documentId}/approve`,
        ExportReceipt: `/api/ExportReceipts/${item.documentId}/approve`,
        StockTransfer: `/api/stock-transfers/${item.documentId}/approve`,
        Stocktake: `/api/Stocktakes/${item.documentId}/approve`,
      };
      await apiClient.post(routes[item.documentType], null, {
        headers: idempotencyHeaders(key),
      });
      completeIdempotentAction(key);
      setSelected(null);
      await load();
    } catch (failure) {
      setError(errorText(failure));
      if ((failure as { response?: { status?: number } }).response?.status === 409) {
        await load();
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  const reject = async () => {
    if (
      !selected ||
      submitting.current ||
      !canRejectItem(selected)
    ) {
      return;
    }

    const cleaned = reason.trim();
    if (!validRejectReason(reason)) return;
    submitting.current = true;
    setError('');
    setBusy(true);
    const key = `reject:${selected.documentType}:${selected.documentId}`;

    try {
      await apiClient.post(
        `/api/approvals/${selected.documentType}/${selected.documentId}/reject`,
        { reason: cleaned },
        { headers: idempotencyHeaders(key) }
      );
      completeIdempotentAction(key);
      setRejecting(false);
      setSelected(null);
      setReason('');
      await load();
    } catch (failure) {
      setError(errorText(failure));
      if ((failure as { response?: { status?: number } }).response?.status === 409) {
        await load();
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  return (
    <UiPage>
      <div className="approval-page">
        <UiPageHeader
          eyebrow="Kiểm soát"
          title="Hàng đợi phê duyệt"
          description={`${data.totalRecords} chứng từ đang chờ trong phạm vi của bạn. Theo dõi SLA và xử lý theo quyền được cấp.`}
        />

        {error && <div className="approval-error" role="alert">{error}</div>}

        <UiToolbar>
          <UiToolbarField label="Loại chứng từ">
            <select
              aria-label="Loại chứng từ"
              value={type}
              onChange={event => {
                setType(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả loại</option>
              <option value="ImportReceipt">Nhập kho</option>
              <option value="ExportReceipt">Xuất kho</option>
              <option value="StockTransfer">Điều chuyển</option>
              <option value="Stocktake">Kiểm kê</option>
            </select>
          </UiToolbarField>

          <UiToolbarField label="Mức SLA">
            <select
              aria-label="Mức SLA"
              value={slaStatus}
              onChange={event => {
                setSlaStatus(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả mức SLA</option>
              <option value="Normal">Bình thường</option>
              <option value="Warning">Sắp đến hạn</option>
              <option value="Overdue">Quá hạn</option>
            </select>
          </UiToolbarField>

          <UiToolbarField label="Mã chứng từ">
            <input
              aria-label="Tìm mã chứng từ"
              value={keyword}
              onChange={event => {
                setKeyword(event.target.value);
                setPage(1);
              }}
              placeholder="Tìm theo mã"
            />
          </UiToolbarField>

          <UiToolbarField label="Mã kho">
            <input
              aria-label="Mã kho"
              type="number"
              min="1"
              value={warehouseId}
              onChange={event => {
                setWarehouseId(event.target.value);
                setPage(1);
              }}
              placeholder="ID kho"
            />
          </UiToolbarField>

          <UiToolbarField label="Mã người tạo">
            <input
              aria-label="Mã người tạo"
              type="number"
              min="1"
              value={creatorId}
              onChange={event => {
                setCreatorId(event.target.value);
                setPage(1);
              }}
              placeholder="ID người tạo"
            />
          </UiToolbarField>

          <UiToolbarField label="Từ ngày">
            <input
              aria-label="Từ ngày"
              type="datetime-local"
              value={fromUtc}
              onChange={event => {
                setFromUtc(event.target.value);
                setPage(1);
              }}
            />
          </UiToolbarField>

          <UiToolbarField label="Đến ngày">
            <input
              aria-label="Đến ngày"
              type="datetime-local"
              value={toUtc}
              onChange={event => {
                setToUtc(event.target.value);
                setPage(1);
              }}
            />
          </UiToolbarField>
        </UiToolbar>

        <UiCard title="Chứng từ cần xử lý">
          {loading ? (
            <p className="approval-state" role="status">Đang tải...</p>
          ) : data.items.length === 0 ? (
            <UiEmptyState title="Không có chứng từ chờ duyệt." />
          ) : (
            <UiTableScroll>
              <table aria-label="Hàng đợi phê duyệt">
                <thead>
                  <tr>
                    <th>Loại</th>
                    <th>Mã</th>
                    <th>Kho</th>
                    <th>Người tạo</th>
                    <th>Thời gian gửi</th>
                    <th>Đã chờ</th>
                    <th>Mức SLA</th>
                    <th>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(item => (
                    <tr key={`${item.documentType}-${item.documentId}`}>
                      <td>{documentLabels[item.documentType] || 'Chứng từ'}</td>
                      <td><strong>{item.documentCode}</strong></td>
                      <td>{item.warehouseName}{item.destinationWarehouseName && ` → ${item.destinationWarehouseName}`}</td>
                      <td>{item.creatorName}</td>
                      <td title={`UTC: ${item.requestedAtUtc}`}>{new Date(item.requestedAtUtc).toLocaleString()}</td>
                      <td className="approval-duration">{waitingText(item.waitingMinutes)}</td>
                      <td>
                        {item.slaStatus && (
                          <span className={`approval-sla approval-sla-${item.slaStatus.toLowerCase()}`}>
                            <UiBadge tone={slaTone(item.slaStatus)}>{slaLabels[item.slaStatus]}</UiBadge>
                          </span>
                        )}
                      </td>
                      <td>
                        <div className="approval-row-actions">
                          <button
                            type="button"
                            title="Xem chi tiết"
                            aria-label={`Xem chi tiết ${item.documentCode}`}
                            onClick={event => void open(item, event.currentTarget)}
                          >
                            <Eye size={17} aria-hidden="true" />
                          </button>
                          {canApproveItem(item) && (
                            <button
                              type="button"
                              title="Duyệt"
                              aria-label={`Duyệt ${item.documentCode}`}
                              disabled={busy}
                              onClick={event => {
                                setError('');
                                setDialogTrigger(event.currentTarget);
                                setSelected(item);
                                setRejecting(false);
                                setApproving(true);
                              }}
                            >
                              <Check size={17} aria-hidden="true" />
                            </button>
                          )}
                          {canRejectItem(item) && (
                              <button
                                type="button"
                                title="Từ chối"
                                aria-label={`Từ chối ${item.documentCode}`}
                                disabled={busy}
                                onClick={event => {
                                  setError('');
                                  setApproving(false);
                                  setReason('');
                                  setDialogTrigger(event.currentTarget);
                                  setSelected(item);
                                  setRejecting(true);
                                }}
                              >
                                <X size={17} aria-hidden="true" />
                              </button>
                            )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          )}

          <nav className="ui-pagination" aria-label="Phân trang phê duyệt">
            <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(current => current - 1)}>
              Trước
            </button>
            <span>Trang {data.pageIndex}/{Math.max(1, data.totalPages)}</span>
            <button
              type="button"
              disabled={page >= data.totalPages || loading}
              onClick={() => setPage(current => current + 1)}
            >
              Sau
            </button>
          </nav>
        </UiCard>

        {selected && (
          <AccessibleDialog
            titleId="approval-title"
            busy={busy}
            onClose={closeDialog}
            returnFocusTo={dialogTrigger}
          >
            <h2 id="approval-title">{selected.documentCode}</h2>
            <p>{documentLabels[selected.documentType] || 'Chứng từ'} · {selected.warehouseName}</p>

            {rejecting ? (
              <>
                <label>
                  Lý do từ chối
                  <textarea
                    disabled={busy}
                    data-initial-focus
                    maxLength={500}
                    value={reason}
                    onChange={event => setReason(event.target.value)}
                  />
                </label>
                <small>{reason.length}/500</small>
              </>
            ) : approving ? (
              <p>Xác nhận duyệt chứng từ này?</p>
            ) : detailLoading ? (
              <p role="status">Đang tải chi tiết...</p>
            ) : (
              <>
                {detail?.note && <p><strong>Ghi chú:</strong> {detail.note}</p>}

                {detail && detail.lines.length > 0 && (
                  <div className="approval-detail-table">
                    <table aria-label={`Chi tiết ${selected.documentCode}`}>
                      <thead>
                        <tr>
                          <th>Sản phẩm</th>
                          <th>Đơn vị</th>
                          <th>Số lượng</th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.lines.map(line => (
                          <tr key={line.productCode}>
                            <td>{line.productCode} · {line.productName}</td>
                            <td>{line.unitName}</td>
                            <td className="approval-duration">{line.quantity}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}

                <h3>Lịch sử phê duyệt</h3>
                {history.length === 0 ? (
                  <p>Chưa có lịch sử.</p>
                ) : (
                  <ol className="approval-history">
                    {history.map(item => (
                      <li key={item.id}>
                        <strong>{approvalDisplayAction(item.displayAction)}</strong>
                        {' · '}{item.actorName}{' · '}{new Date(item.timestampUtc).toLocaleString()}
                        <div>
                          {stateLabels[item.oldState || ''] || 'Không áp dụng'} →{' '}
                          {stateLabels[item.newState || ''] || 'Không áp dụng'}
                        </div>
                        {item.reason && <p>{item.reason}</p>}
                        {item.correlationId && <code>{item.correlationId}</code>}
                      </li>
                    ))}
                  </ol>
                )}
              </>
            )}

            {error && <p role="alert">{error}</p>}

            <footer>
              <button
                type="button"
                data-initial-focus={approving || undefined}
                disabled={busy}
                onClick={closeDialog}
              >
                Đóng
              </button>
              {approving && (
                <button type="button" disabled={busy} onClick={() => void approve(selected)}>
                  Xác nhận duyệt
                </button>
              )}
              {rejecting && (
                <button
                  type="button"
                  className="danger"
                  disabled={busy || !validRejectReason(reason)}
                  onClick={() => void reject()}
                >
                  Xác nhận từ chối
                </button>
              )}
            </footer>
          </AccessibleDialog>
        )}
      </div>
    </UiPage>
  );
}
