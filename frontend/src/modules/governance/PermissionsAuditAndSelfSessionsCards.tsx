import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { PermissionAuditLog, PermissionsSelfSession } from './PermissionsHelpersAndTypes';

const AUDIT_ACTION_LABELS: Record<string, string> = {
  Create: 'Tạo mới', Update: 'Cập nhật', Delete: 'Xóa', Approve: 'Phê duyệt', Reject: 'Từ chối',
  Complete: 'Hoàn tất', Cancel: 'Hủy', Dispatch: 'Xuất giao', Receive: 'Tiếp nhận', Login: 'Đăng nhập',
  Logout: 'Đăng xuất', Post: 'Ghi sổ', Lock: 'Khóa', Unlock: 'Mở khóa', Acknowledge: 'Tiếp nhận xử lý',
  'Authentication.RefreshRotated': 'Làm mới phiên đăng nhập',
  'Authentication.LoginSucceeded': 'Đăng nhập thành công',
  'Authentication.LogoutSucceeded': 'Đăng xuất',
  'QA.IamAndOperationalSeedApplied': 'Khởi tạo dữ liệu vận hành chuẩn',
};

const AUDIT_ENTITY_LABELS: Record<string, string> = {
  ImportReceipt: 'Phiếu nhập', ExportReceipt: 'Phiếu xuất', PutawayTask: 'Tác vụ cất hàng',
  PickingTask: 'Tác vụ lấy hàng', PackingSession: 'Phiên đóng gói', Shipment: 'Chuyến giao hàng',
  StockTransfer: 'Phiếu điều chuyển', Stocktake: 'Phiếu kiểm kê', Product: 'Sản phẩm', Warehouse: 'Kho',
  WarehouseLocation: 'Vị trí kho', InventoryLock: 'Khóa tồn kho', StockReservation: 'Giữ hàng',
  StockAllocation: 'Phân bổ tồn kho', BusinessPartner: 'Đối tác', User: 'Người dùng',
  UserSession: 'Phiên đăng nhập', LocalQaOperationalSeed: 'Bộ dữ liệu chuẩn',
  WarehouseCalendarException: 'Ngoại lệ lịch kho', Role: 'Vai trò', UserWarehouseAccess: 'Phân quyền kho',
};

export function PermissionsAuditAndSelfSessionsCards({
  auditLogs,
  auditLoaded,
  auditBusy,
  onLoadAuditLogs,
  onError,
}: {
  auditLogs: PermissionAuditLog[];
  auditLoaded: boolean;
  auditBusy: boolean;
  onLoadAuditLogs: () => void;
  onError: (msg: string) => void;
}) {
  const [selfSessions, setSelfSessions] = useState<PermissionsSelfSession[]>([]);
  const [selfSessionsBusy, setSelfSessionsBusy] = useState(false);

  return (
    <>
      <UiCard title="Phiên đăng nhập Thiết bị Cá nhân">
        <div className="ui-inline-actions">
          <button
            type="button"
            disabled={selfSessionsBusy}
            onClick={async () => {
              setSelfSessionsBusy(true);
              onError('');
              try {
                const res = await apiClient.get('/api/v1/auth/sessions');
                const rawList = Array.isArray(res?.data) ? res.data : [];
                setSelfSessions(
                  rawList.map((s: Record<string, unknown>, idx: number) => ({
                    sessionId: String(s.sessionId ?? s.id ?? `SES-${idx + 1}`),
                    deviceInfo: String(s.deviceInfo ?? s.userAgent ?? 'Trình duyệt web (Windows)'),
                    ipAddress: String(s.ipAddress ?? '127.0.0.1'),
                    isCurrent: Boolean(s.isCurrent ?? idx === 0),
                    createdAtUtc: String(s.createdAtUtc ?? s.createdAt ?? new Date().toISOString()),
                  })),
                );
              } catch (failure) {
                onError(permissionError(failure));
              } finally {
                setSelfSessionsBusy(false);
              }
            }}
          >
            Tải danh sách phiên đăng nhập hiện tại
          </button>
        </div>
        {selfSessions.length > 0 && (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Danh sách phiên thiết bị cá nhân">
              <thead>
                <tr>
                  <th>Mã phiên</th>
                  <th>Thiết bị / Trình duyệt</th>
                  <th>Địa chỉ IP</th>
                  <th>Trạng thái</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {selfSessions.map(ses => (
                  <tr key={ses.sessionId}>
                    <td><code>{ses.sessionId}</code></td>
                    <td>{ses.deviceInfo}</td>
                    <td>{ses.ipAddress}</td>
                    <td><UiBadge tone={ses.isCurrent ? 'success' : 'neutral'}>{ses.isCurrent ? 'Phiên hiện tại' : 'Phiên hoạt động'}</UiBadge></td>
                    <td>
                      <button
                        type="button"
                        disabled={selfSessionsBusy}
                        onClick={async () => {
                          setSelfSessionsBusy(true);
                          onError('');
                          try {
                            await apiClient.delete(`/api/v1/auth/sessions/${encodeURIComponent(ses.sessionId)}`);
                            setSelfSessions(prev => prev.filter(item => item.sessionId !== ses.sessionId));
                          } catch (failure) {
                            onError(permissionError(failure));
                          } finally {
                            setSelfSessionsBusy(false);
                          }
                        }}
                      >
                        Thu hồi phiên
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>

      <UiCard title="Nhật ký kiểm toán phân quyền & bảo mật">
        <div className="ui-inline-actions">
          <button type="button" disabled={auditBusy} onClick={onLoadAuditLogs}>
            {auditBusy ? 'Đang tải nhật ký...' : 'Tải nhật ký kiểm toán'}
          </button>
        </div>
        {auditLoaded && (
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Nhật ký kiểm toán phân quyền">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Người thực hiện</th>
                  <th>Hành động</th>
                  <th>Đối tượng</th>
                  <th>Chi tiết thay đổi</th>
                </tr>
              </thead>
              <tbody>
                {auditLogs.length === 0 ? (
                  <tr><td colSpan={5} className="ui-empty-cell">Chưa có bản ghi kiểm toán.</td></tr>
                ) : (
                  auditLogs.map(log => {
                    const auditActionLabel = AUDIT_ACTION_LABELS[log.action] || log.action;
                    const auditEntityLabel = AUDIT_ENTITY_LABELS[log.entityName] || log.entityName;
                    return (
                      <tr key={log.id}>
                        <td>{new Date(log.timestamp).toLocaleString('vi-VN')}</td>
                        <td>{log.userId ? `Người dùng #${log.userId}` : 'Hệ thống'}</td>
                        <td><UiBadge>{auditActionLabel}</UiBadge></td>
                        <td>{auditEntityLabel}{log.entityId ? ` #${log.entityId}` : ''}</td>
                        <td><small>{log.newValues || '—'}</small></td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </UiTableScroll>
        )}
      </UiCard>
    </>
  );
}
