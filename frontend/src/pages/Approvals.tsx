import { useCallback, useEffect, useRef, useState } from 'react';
import { Check, Eye, X } from 'lucide-react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { validRejectReason } from '../services/approval';
import { hasPermission, usePermissionSet } from '../services/authorization';
import { UiBadge, UiCard, UiEmptyState, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import {
  ApprovalsDetailDrawer, documentLabels, errorText, slaLabels, slaTone, waitingText,
  type ApprovalDetail, type HistoryItem, type Page, type QueueItem,
} from '../modules/governance/ApprovalsDetailDrawer';
import { ApprovalsSlaAndFilterBar } from '../modules/governance/ApprovalsSlaAndFilterBar';
import { formatWarehouseLabel } from '../utils/warehouseLabel';
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

        <ApprovalsSlaAndFilterBar
          normalCount={normalCount}
          warningCount={warningCount}
          overdueCount={overdueCount}
          escalationResult={escalationResult}
          onTriggerEscalateOverdue={() => void triggerEscalateOverdue()}
          type={type}
          onChangeType={v => { setType(v); setPage(1); }}
          slaStatus={slaStatus}
          onChangeSlaStatus={v => { setSlaStatus(v); setPage(1); }}
          keyword={keyword}
          onChangeKeyword={v => { setKeyword(v); setPage(1); }}
          warehouseId={warehouseId}
          onChangeWarehouseId={v => { setWarehouseId(v); setPage(1); }}
          creatorId={creatorId}
          onChangeCreatorId={v => { setCreatorId(v); setPage(1); }}
          fromUtc={fromUtc}
          onChangeFromUtc={v => { setFromUtc(v); setPage(1); }}
          toUtc={toUtc}
          onChangeToUtc={v => { setToUtc(v); setPage(1); }}
        />

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
                    <th style={{ whiteSpace: 'nowrap', minWidth: 84 }}>Loại</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 92 }}>Mã</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 112 }}>Kho</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 92 }}>Người tạo</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 118 }}>Thời gian gửi</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 80, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Đã chờ</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 88 }}>Mức SLA</th>
                    <th style={{ whiteSpace: 'nowrap', minWidth: 108 }}>Hành động</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(item => (
                    <tr key={`${item.documentType}-${item.documentId}`}>
                      <td>{documentLabels[item.documentType] || 'Chứng từ'}</td>
                      <td className="ui-cell-nowrap"><strong style={{ whiteSpace: 'nowrap' }}>{item.documentCode}</strong></td>
                      <td>{formatWarehouseLabel(item.warehouseName)}{item.destinationWarehouseName && ` → ${formatWarehouseLabel(item.destinationWarehouseName)}`}</td>
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
                              title={busy ? 'Đang xử lý yêu cầu phê duyệt trước đó' : 'Duyệt'}
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
                              title={busy ? 'Đang xử lý yêu cầu phê duyệt trước đó' : 'Từ chối'}
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
