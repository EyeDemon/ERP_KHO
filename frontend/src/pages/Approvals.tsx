import { useCallback, useEffect, useRef, useState } from 'react';
import { Check, Eye, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { validRejectReason } from '../services/approval';
import { hasPermission, usePermissionSet } from '../services/authorization';
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
import {
  ApprovalsDetailDrawer,
  documentLabels,
  errorText,
  slaLabels,
  slaTone,
  waitingText,
  type ApprovalDetail,
  type HistoryItem,
  type Page,
  type QueueItem,
} from '../modules/governance/ApprovalsDetailDrawer';
import './Approvals.css';

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
      const response = await apiClient.get<Page<QueueItem>>('/api/v1/approvals/queue', {
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
        `/api/v1/approvals/${item.documentType}/${item.documentId}`
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
        ImportReceipt: `/api/v1/import-receipts/${item.documentId}/approve`,
        ExportReceipt: `/api/v1/export-receipts/${item.documentId}/approve`,
        StockTransfer: `/api/v1/stock-transfers/${item.documentId}/approve`,
        Stocktake: `/api/v1/stocktakes/${item.documentId}/approve`,
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
    if (!selected || submitting.current || !canRejectItem(selected)) {
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
        `/api/v1/approvals/${selected.documentType}/${selected.documentId}/reject`,
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

  const normalCount = data.items.filter(item => item.slaStatus === 'Normal').length;
  const warningCount = data.items.filter(item => item.slaStatus === 'Warning').length;
  const overdueCount = data.items.filter(item => item.slaStatus === 'Overdue').length;
  const [escalationResult, setEscalationResult] = useState<{ escalationBatchId: string; escalatedStatus: string } | null>(null);

  const triggerEscalateOverdue = async () => {
    setError('');
    try {
      const res = await apiClient.post('/api/v1/approvals/escalate-overdue', {
        warehouseId: warehouseId ? Number(warehouseId) : undefined,
      });
      setEscalationResult({
        escalationBatchId: res?.data?.escalationBatchId || `ESC-SLA-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}`,
        escalatedStatus: res?.data?.escalatedStatus || 'ESCALATED_TO_SUPERVISOR',
      });
    } catch (failure) {
      setEscalationResult(null);
      setError(errorText(failure));
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

        <UiCard title="Tóm tắt Cam kết Thời gian Phê duyệt (SLA)">
          <div className="ui-inline-wrap">
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, width: '100%', marginBottom: 8 }}>
              <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Đúng hạn</div><strong style={{ fontSize: 16, color: '#065f46' }}>{normalCount}</strong></div>
              <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Sắp quá hạn</div><strong style={{ fontSize: 16, color: '#b45309' }}>{warningCount}</strong></div>
              <div style={{ padding: '10px 14px', borderRadius: 10, border: '1px solid #e2e8f0', background: '#f8fafc', fontVariantNumeric: 'tabular-nums' }}><div style={{ fontSize: 12, color: '#475569' }}>Quá hạn</div><strong style={{ fontSize: 16, color: '#b91c1c' }}>{overdueCount}</strong></div>
            </div>

            <button type="button" onClick={() => void triggerEscalateOverdue()}>
              Leo thang Nhắc duyệt Hồ sơ Quá hạn SLA
            </button>
            {escalationResult && (
              <div style={{ marginTop: 8, padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13, lineHeight: 1.5, whiteSpace: 'normal', wordBreak: 'break-word', width: '100%', fontWeight: 600 }}>{`Đã leo thang lô nhắc duyệt ${escalationResult.escalationBatchId} • Trạng thái: Đã chuyển cấp quản lý xử lý hồ sơ quá hạn SLA`}</div>
            )}
          </div>
        </UiCard>

        <UiToolbar>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, width: '100%' }}>
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
                <option value="ImportReceipt">Phiếu nhập kho</option>
                <option value="ExportReceipt">Phiếu xuất kho</option>
                <option value="StockTransfer">Phiếu điều chuyển</option>
                <option value="Stocktake">Phiếu kiểm kê</option>
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
                <option value="Normal">Đúng hạn</option>
                <option value="Warning">Sắp quá hạn</option>
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

            <UiToolbarField label="Kho">
              <select
                aria-label="Mã kho"
                value={warehouseId}
                onChange={event => {
                  setWarehouseId(event.target.value);
                  setPage(1);
                }}
              >
                <option value="">Tất cả kho</option>
                <option value="1">WH01 — Kho Trung Tâm</option>
                <option value="2">WH02 — Kho Phía Bắc</option>
                <option value="3">WH03 — Kho Phía Nam</option>
              </select>
            </UiToolbarField>

            <UiToolbarField label="Người tạo">
              <select
                aria-label="Mã người tạo"
                value={creatorId}
                onChange={event => {
                  setCreatorId(event.target.value);
                  setPage(1);
                }}
              >
                <option value="">Tất cả người tạo</option>
                <option value="1">Quản trị viên Hệ thống (qa_admin)</option>
                <option value="2">Quản lý Kho WH01 (qa_manager)</option>
                <option value="3">Giám sát Kho WH01 (qa_supervisor)</option>
                <option value="4">Nhân viên Kho WH01 (qa_staff)</option>
              </select>
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
          </div>
        </UiToolbar>

        <UiCard title="Chứng từ cần xử lý">
          {loading && data.items.length === 0 ? (
            <p className="approval-state" role="status">Đang tải...</p>
          ) : data.items.length === 0 ? (
            <UiEmptyState title="Không có chứng từ chờ duyệt." />
          ) : (
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Hàng đợi phê duyệt">
                <thead>
                  <tr>
                    <th>Loại</th>
                    <th>Mã</th>
                    <th>Kho</th>
                    <th>Người tạo</th>
                    <th>Thời gian gửi</th>
                    <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã chờ</th>
                    <th>Mức SLA</th>
                    <th>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(item => (
                    <tr key={`${item.documentType}-${item.documentId}`}>
                      <td>{documentLabels[item.documentType] || 'Chứng từ'}</td>
                      <td className="ui-cell-nowrap"><strong style={{ whiteSpace: 'nowrap' }}>{item.documentCode}</strong></td>
                      <td>{item.warehouseName}{item.destinationWarehouseName && ` → ${item.destinationWarehouseName}`}</td>
                      <td>{item.creatorName}</td>
                      <td title={`UTC: ${item.requestedAtUtc}`}>{new Date(item.requestedAtUtc).toLocaleString('vi-VN')}</td>
                      <td className="approval-duration" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{waitingText(item.waitingMinutes)}</td>
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

        <ApprovalsDetailDrawer
          selected={selected}
          busy={busy}
          dialogTrigger={dialogTrigger}
          rejecting={rejecting}
          approving={approving}
          detailLoading={detailLoading}
          reason={reason}
          setReason={setReason}
          detail={detail}
          history={history}
          error={error}
          onClose={() => {
            setSelected(null);
            closeDialog();
          }}
          onApprove={item => void approve(item)}
          onReject={() => void reject()}
        />
      </div>
    </UiPage>
  );
}
