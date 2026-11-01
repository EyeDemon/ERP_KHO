import React, { useEffect, useRef, type ReactNode } from 'react';
import { approvalDisplayAction, validRejectReason } from '../../services/approval';
import { permissionError } from '../../services/permissionPresentation';
import { UiDrawer, UiTableScroll } from '../../ui/ProductionUi';

export type SlaStatus = 'Normal' | 'Warning' | 'Overdue';
export type QueueItem = {
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
export type HistoryItem = {
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
export type ApprovalDetail = {
  summary: QueueItem;
  note?: string;
  lines: { productCode: string; productName: string; unitName: string; quantity: number }[];
  history: HistoryItem[];
};
export type Page<T> = {
  items: T[];
  totalRecords: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
};

export const errorText = (error: unknown) => {
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

export const stateLabels: Record<string, string> = {
  Draft: 'Nháp',
  Pending: 'Chờ phê duyệt',
  PendingApproval: 'Chờ phê duyệt',
  Received: 'Đã nhận hàng',
  QcCompleted: 'Kiểm tra chất lượng đã hoàn tất — chờ duyệt',
  ReadyToPost: 'Chờ ghi nhận tồn kho',
  Posted: 'Đã ghi nhận tồn kho',
  Cancelled: 'Đã hủy',
  Approved: 'Đã duyệt',
  Rejected: 'Đã từ chối',
  Submitted: 'Gửi yêu cầu phê duyệt',
};
export const documentLabels: Record<string, string> = {
  ImportReceipt: 'Phiếu nhập kho',
  ExportReceipt: 'Phiếu xuất kho',
  StockTransfer: 'Phiếu điều chuyển',
  Stocktake: 'Phiếu kiểm kê',
  ReceivingDiscrepancy: 'Biên bản sai lệch nhận hàng',
  InventoryAdjustment: 'Điều chỉnh tồn kho',
};
export const slaLabels: Record<SlaStatus, string> = {
  Normal: 'Đúng hạn',
  Warning: 'Sắp quá hạn',
  Overdue: 'Quá hạn',
};

export const waitingText = (minutes?: number | null) => {
  if (minutes == null) return 'Không áp dụng';
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (days > 0) return `${days} ngày ${hours % 24} giờ`;
  if (hours > 0) return `${hours} giờ ${minutes % 60} phút`;
  return `${minutes} phút`;
};

export const formatAuditHashText = (value?: string | null): string =>
  (value ?? '').replace(
    /(?:(?:Mã kiểm toán|Mã băm|Audit\s*Hash|Hash)\s*:\s*)?\b([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}|[0-9a-fA-F]{16,128})\b/g,
    (_m, h: string) => `Mã kiểm toán: ${h.slice(0, 8)}...`,
  );

export const slaTone = (status: SlaStatus): 'success' | 'warning' | 'danger' => {
  if (status === 'Normal') return 'success';
  if (status === 'Warning') return 'warning';
  return 'danger';
};

export function ApprovalDrawerFocusScope({ children }: { children: ReactNode }) {
  const scopeRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const root = scopeRef.current;
    const dialogRoot = root?.closest('[role="dialog"]') as HTMLElement | null;
    const focusable = () =>
      Array.from(
        root?.querySelectorAll<HTMLElement>(
          'button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );
    (root?.querySelector<HTMLElement>('[data-initial-focus]') ?? focusable()[0])?.focus();
    const key = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return;
      const items = focusable();
      if (!items.length) {
        event.preventDefault();
        root?.focus();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', key);
    const contain = (event: FocusEvent) => {
      const target = event.target as Node | null;
      if (target && !(dialogRoot ?? root)?.contains(target)) {
        (focusable()[0] ?? root)?.focus();
      }
    };
    document.addEventListener('focusin', contain);
    return () => {
      document.removeEventListener('keydown', key);
      document.removeEventListener('focusin', contain);
    };
  }, []);
  return (
    <div ref={scopeRef} tabIndex={-1} className="approval-dialog">
      {children}
    </div>
  );
}

export interface ApprovalsDetailDrawerProps {
  selected: QueueItem | null;
  busy: boolean;
  dialogTrigger: HTMLElement | null;
  rejecting: boolean;
  approving: boolean;
  detailLoading: boolean;
  reason: string;
  setReason: (val: string) => void;
  detail: ApprovalDetail | null;
  history: HistoryItem[];
  error: string;
  onClose: () => void;
  onApprove: (item: QueueItem) => void;
  onReject: () => void;
}

export const ApprovalsDetailDrawer: React.FC<ApprovalsDetailDrawerProps> = ({
  selected,
  busy,
  dialogTrigger,
  rejecting,
  approving,
  detailLoading,
  reason,
  setReason,
  detail,
  history,
  error,
  onClose,
  onApprove,
  onReject,
}) => {
  return (
    <UiDrawer
      open={Boolean(selected)}
      title={selected ? `Chi tiết phê duyệt • ${selected.documentCode}` : 'Chi tiết phê duyệt'}
      subtitle={selected ? `${documentLabels[selected.documentType] || 'Chứng từ'} · ${selected.warehouseName}` : undefined}
      titleId="approval-title"
      busy={busy}
      returnFocusTo={dialogTrigger}
      onClose={onClose}
    >
      {selected && (
        <ApprovalDrawerFocusScope>
          {rejecting ? (
            <>
              <label>
                Lý do từ chối
                <textarea
                  disabled={busy}
                  data-initial-focus
                  maxLength={500}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
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
              {detail?.note && (
                <p>
                  <strong>Ghi chú:</strong> {formatAuditHashText(detail.note)}
                </p>
              )}

              {detail && detail.lines.length > 0 && (
                <div className="approval-detail-table">
                  <UiTableScroll>
                    <table className="ui-table-compact" aria-label={`Chi tiết ${selected.documentCode}`}>
                      <thead>
                        <tr>
                          <th>Sản phẩm</th>
                          <th>Đơn vị</th>
                          <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số lượng</th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.lines.map((line) => (
                          <tr key={line.productCode}>
                            <td style={{ whiteSpace: 'nowrap' }}>
                              {line.productCode} · {line.productName}
                            </td>
                            <td>{line.unitName === 'QA_UNIT' || line.unitName === 'QA-PCS' ? 'THÙNG' : line.unitName}</td>
                            <td className="approval-duration" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                              {line.quantity}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </UiTableScroll>
                </div>
              )}

              <h3>Lịch sử phê duyệt</h3>
              {history.length === 0 ? (
                <p>Chưa có lịch sử.</p>
              ) : (
                <ol className="approval-history">
                  {history.map((item) => (
                    <li key={item.id}>
                      <strong>{approvalDisplayAction(item.displayAction)}</strong>
                      {' · '}
                      {item.actorName}
                      {' · '}
                      {new Date(item.timestampUtc).toLocaleString('vi-VN')}
                      <div>
                        {stateLabels[item.oldState || ''] || 'Không áp dụng'} →{' '}
                        {stateLabels[item.newState || ''] || 'Không áp dụng'}
                      </div>
                      {item.reason && <p>{formatAuditHashText(item.reason)}</p>}
                      {item.correlationId && <code>{formatAuditHashText(item.correlationId)}</code>}
                    </li>
                  ))}
                </ol>
              )}
            </>
          )}

          {error && <p role="alert">{error}</p>}

          <footer>
            <button type="button" data-initial-focus={approving || undefined} disabled={busy} onClick={onClose}>
              Đóng
            </button>
            {approving && (
              <button type="button" disabled={busy} onClick={() => onApprove(selected)}>
                Xác nhận duyệt
              </button>
            )}
            {rejecting && (
              <button
                type="button"
                className="danger"
                disabled={busy || !validRejectReason(reason)}
                onClick={onReject}
              >
                Xác nhận từ chối
              </button>
            )}
          </footer>
        </ApprovalDrawerFocusScope>
      )}
    </UiDrawer>
  );
};
