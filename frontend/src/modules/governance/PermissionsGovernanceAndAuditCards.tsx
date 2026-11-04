import { roleLabel } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll, UiToolbar, UiToolbarField } from '../../ui/ProductionUi';
import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';
import { PermissionsAuditAndSelfSessionsCards } from './PermissionsAuditAndSelfSessionsCards';
import type {
  PermissionAuditLog,
  PermissionsCatalogUser,
  PermissionsCatalogWarehouse,
  UserSecurityStatus,
  UserWarehouseAccessListDto,
} from './PermissionsHelpersAndTypes';

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
  const renderUserSelectOptions = () =>
    catalogUsers.length > 0
      ? catalogUsers.map(u => (
          <option key={u.id} value={String(u.id)}>
            {u.username}{u.fullName ? ` (${u.fullName})` : ''}
          </option>
        ))
      : null;

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
                          <option key={w.id} value={String(w.id)}>{formatWarehouseLabel(w.name, w.code, w.id)}</option>
                        ))
                      ) : (
                        <>
                          <option value="1">{CANONICAL_WH01_LABEL}</option>
                          <option value="2">Kho Phía Bắc (WH02)</option>
                          <option value="3">Kho Phía Nam (WH03)</option>
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
                            <td>{formatWarehouseLabel(wh.warehouseName, wh.warehouseCode, wh.warehouseId)}</td>
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
        <PermissionsAuditAndSelfSessionsCards
          auditLogs={auditLogs}
          auditLoaded={auditLoaded}
          auditBusy={auditBusy}
          onLoadAuditLogs={onLoadAuditLogs}
          onError={onError}
        />
      )}
    </>
  );
}
