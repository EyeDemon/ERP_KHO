import { useState } from 'react';
import apiClient from '../../services/apiClient';
import { permissionError, roleLabel } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import type {
  PermissionAuditLog,
  PermissionsCatalogUser,
  PermissionsCatalogWarehouse,
  PermissionsSelfSession,
  UserSecurityStatus,
  UserWarehouseAccessListDto,
} from './PermissionsHelpersAndTypes';

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

export function PermissionsGovernanceAndAuditCards({
  canAssign,
  canReadCatalog,
  targetUserIdInput,
  onChangeTargetUserIdInput,
  catalogUsers,
  catalogWarehouses,
  securityBusy,
  onLoadUserSecurity,
  userSecurity,
  securityReason,
  onChangeSecurityReason,
  tempPassword,
  onChangeTempPassword,
  onRunSecurityAction,
  whUserIdInput,
  onChangeWhUserIdInput,
  whBusy,
  onLoadUserWarehouseScope,
  userWarehouses,
  grantWarehouseIdInput,
  onChangeGrantWarehouseIdInput,
  onGrantWarehouseScope,
  onRevokeWarehouseScope,
  auditLogs,
  auditLoaded,
  auditBusy,
  onLoadAuditLogs,
  onError,
}: {
  canAssign: boolean;
  canReadCatalog: boolean;
  targetUserIdInput: string;
  onChangeTargetUserIdInput: (v: string) => void;
  catalogUsers: PermissionsCatalogUser[];
  catalogWarehouses: PermissionsCatalogWarehouse[];
  securityBusy: boolean;
  onLoadUserSecurity: () => void;
  userSecurity: UserSecurityStatus | null;
  securityReason: string;
  onChangeSecurityReason: (v: string) => void;
  tempPassword: string;
  onChangeTempPassword: (v: string) => void;
  onRunSecurityAction: (endpoint: 'lock' | 'unlock' | 'revoke-sessions' | 'reset-password') => void;
  whUserIdInput: string;
  onChangeWhUserIdInput: (v: string) => void;
  whBusy: boolean;
  onLoadUserWarehouseScope: () => void;
  userWarehouses: UserWarehouseAccessListDto | null;
  grantWarehouseIdInput: string;
  onChangeGrantWarehouseIdInput: (v: string) => void;
  onGrantWarehouseScope: () => void;
  onRevokeWarehouseScope: (warehouseId: number) => void;
  auditLogs: PermissionAuditLog[];
  auditLoaded: boolean;
  auditBusy: boolean;
  onLoadAuditLogs: () => void;
  onError: (msg: string) => void;
}) {
  const [selfSessions, setSelfSessions] = useState<PermissionsSelfSession[]>([]);
  const [selfSessionsBusy, setSelfSessionsBusy] = useState(false);

  const renderUserSelectOptions = () =>
    catalogUsers.length > 0 ? (
      catalogUsers.map(u => (
        <option key={u.id} value={String(u.id)}>
          {u.username}{u.fullName ? ` (${u.fullName})` : ''}
        </option>
      ))
    ) : (
      <>
        <option value="1">qa_admin (Quản trị viên Hệ thống)</option>
        <option value="2">qa_manager (Quản lý Kho WH01)</option>
        <option value="3">qa_supervisor (Giám sát Kho WH01)</option>
        <option value="4">qa_staff (Nhân viên Kho WH01)</option>
      </>
    );

  return (
    <>
      {canAssign && (
        <>
          <UiCard title="Quản trị bảo mật tài khoản & phiên đăng nhập">
            <UiToolbar>
              <UiToolbarField label="Tài khoản người dùng">
                <select aria-label="ID người dùng cần kiểm tra bảo mật" value={targetUserIdInput} onChange={e => onChangeTargetUserIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                  <option value="">-- Chọn người dùng --</option>
                  {renderUserSelectOptions()}
                </select>
              </UiToolbarField>
              <button type="button" disabled={securityBusy || !targetUserIdInput} onClick={onLoadUserSecurity} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
                Tra cứu bảo mật
              </button>
            </UiToolbar>

            {userSecurity && (
              <div className="ui-stack">
                <p>
                  <strong>{userSecurity.username}</strong> ({roleLabel(userSecurity.roleName)}) •{' '}
                  <UiBadge tone={!userSecurity.isActive ? 'danger' : userSecurity.isTemporarilyLocked ? 'warning' : 'success'}>
                    {!userSecurity.isActive ? 'Đã khóa' : userSecurity.isTemporarilyLocked ? 'Đang khóa tạm thời' : 'Đang hoạt động'}
                  </UiBadge>{' '}
                  • Phiên hoạt động: <strong>{userSecurity.activeSessionCount}</strong> • Sai mật khẩu: <strong>{userSecurity.failedLoginCount}</strong>
                </p>
                <UiToolbar>
                  <UiToolbarField label="Lý do thao tác">
                    <input aria-label="Lý do thao tác bảo mật" value={securityReason} onChange={e => onChangeSecurityReason(e.target.value)} placeholder="VD: Khóa kiểm toán định kỳ" style={{ minWidth: 280 }} />
                  </UiToolbarField>
                  <UiToolbarField label="Mật khẩu tạm (nếu đặt lại)">
                    <input aria-label="Mật khẩu tạm thời" type="password" value={tempPassword} onChange={e => onChangeTempPassword(e.target.value)} placeholder="Tối thiểu 12 ký tự" style={{ minWidth: 260 }} />
                  </UiToolbarField>
                  <div style={{ flexBasis: '100%', height: 0 }} />
                  <button type="button" disabled={securityBusy || !userSecurity.isActive || userSecurity.isTemporarilyLocked} onClick={() => onRunSecurityAction('lock')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy || (userSecurity.isActive && !userSecurity.isTemporarilyLocked)} onClick={() => onRunSecurityAction('unlock')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Mở khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy} onClick={() => onRunSecurityAction('revoke-sessions')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Thu hồi mọi phiên
                  </button>
                  <button type="button" disabled={securityBusy || !tempPassword} onClick={() => onRunSecurityAction('reset-password')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Đặt lại mật khẩu
                  </button>
                </UiToolbar>
              </div>
            )}
          </UiCard>

          <UiCard title="Phân quyền phạm vi kho theo người dùng">
            <UiToolbar>
              <UiToolbarField label="Tài khoản người dùng">
                <select aria-label="ID người dùng phân quyền kho" value={whUserIdInput} onChange={e => onChangeWhUserIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                  <option value="">-- Chọn người dùng --</option>
                  {renderUserSelectOptions()}
                </select>
              </UiToolbarField>
              <button type="button" disabled={whBusy || !whUserIdInput} onClick={onLoadUserWarehouseScope} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
                Tải phạm vi kho
              </button>
            </UiToolbar>

            {userWarehouses && (
              <div className="ui-stack">
                <p>
                  Người dùng: <strong>{userWarehouses.fullName || userWarehouses.username}</strong> ({roleLabel(userWarehouses.roleName)})
                </p>
                <UiToolbar>
                  <UiToolbarField label="Kho cấp thêm">
                    <select aria-label="ID kho cần cấp quyền" value={grantWarehouseIdInput} onChange={e => onChangeGrantWarehouseIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                      <option value="">-- Chọn kho --</option>
                      {catalogWarehouses.length > 0 ? (
                        catalogWarehouses.map(w => (
                          <option key={w.id} value={String(w.id)}>
                            {w.code ? `${w.code} (${w.name})` : w.name}
                          </option>
                        ))
                      ) : (
                        <>
                          <option value="1">WH-MAIN (Kho Trung Tâm)</option>
                          <option value="2">WH-NORTH (Kho Phía Bắc)</option>
                          <option value="3">WH-SOUTH (Kho Phía Nam)</option>
                        </>
                      )}
                    </select>
                  </UiToolbarField>
                  <button type="button" disabled={whBusy || !grantWarehouseIdInput} onClick={onGrantWarehouseScope} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
                    Cấp quyền kho
                  </button>
                </UiToolbar>
                <UiTableScroll>
                  <table className="ui-table-compact" aria-label="Phạm vi kho của người dùng">
                    <thead>
                      <tr>
                        <th>Mã kho</th>
                        <th>Tên kho</th>
                        <th>Trạng thái kho</th>
                        <th>Thời điểm cấp</th>
                        <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>
                      </tr>
                    </thead>
                    <tbody>
                      {userWarehouses.warehouses.length === 0 ? (
                        <tr><td colSpan={5} className="ui-empty-cell">Người dùng chưa được gán kho nào.</td></tr>
                      ) : (
                        userWarehouses.warehouses.map(wh => (
                          <tr key={wh.warehouseId}>
                            <td style={{ whiteSpace: 'nowrap' }}><code>{wh.warehouseCode}</code></td>
                            <td>{wh.warehouseName}</td>
                            <td><UiBadge tone={wh.warehouseIsActive ? 'success' : 'neutral'}>{wh.warehouseIsActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                            <td style={{ whiteSpace: 'nowrap' }}>{new Date(wh.grantedAt).toLocaleString('vi-VN')}</td>
                            <td style={{ whiteSpace: 'nowrap' }}>
                              <button type="button" disabled={whBusy} aria-label={`Thu hồi kho ${wh.warehouseCode}`} onClick={() => onRevokeWarehouseScope(wh.warehouseId)} style={{ whiteSpace: 'nowrap' }}>
                                Thu hồi kho
                              </button>
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </UiTableScroll>
              </div>
            )}
          </UiCard>
        </>
      )}

      {canReadCatalog && (
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
      )}
    </>
  );
}
