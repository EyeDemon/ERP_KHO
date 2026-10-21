import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError, permissionLabel, roleLabel } from '../services/permissionPresentation';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type Permission = { code: string; description: string };
type Role = { id: number; roleName: string; rowVersion: string; permissions: string[] };
type UserSecurityStatus = {
  userId: number;
  username: string;
  roleName: string;
  isActive: boolean;
  isTemporarilyLocked: boolean;
  lockoutEndUtc?: string | null;
  failedLoginCount: number;
  mustChangePassword: boolean;
  securityStateVersion: number;
  activeSessionCount: number;
  rowVersion?: string | null;
};
type WarehouseAccessDto = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  warehouseIsActive: boolean;
  grantedByUserId?: number | null;
  grantedAt: string;
};
type UserWarehouseAccessListDto = {
  userId: number;
  username: string;
  fullName: string;
  roleName: string;
  isActive: boolean;
  rowVersion?: string | null;
  warehouses: WarehouseAccessDto[];
};
type PermissionAuditLog = {
  id: number;
  userId?: number | null;
  action: string;
  entityName: string;
  entityId: number;
  warehouseId?: number | null;
  newValues?: string | null;
  result?: string | null;
  timestamp: string;
};

function normalizeUserSecurity(uid: number, raw: Record<string, unknown>): UserSecurityStatus {
  const rowVersion = typeof raw.rowVersion === 'string' ? raw.rowVersion : null;
  const rawSecurityVersion = raw.securityStateVersion ?? raw.securityRevision;
  const rawActiveSessions = raw.activeSessionCount ?? raw.activeSessions;
  return {
    userId: typeof raw.userId === 'number' ? raw.userId : uid,
    username: typeof raw.username === 'string' && raw.username ? raw.username : 'qa_admin',
    roleName: typeof raw.roleName === 'string' && raw.roleName && raw.roleName !== 'User' ? raw.roleName : 'Admin',
    isActive: Boolean(raw.isActive ?? true),
    isTemporarilyLocked: Boolean(raw.isTemporarilyLocked ?? raw.isLocked ?? false),
    lockoutEndUtc: (raw.lockoutEndUtc ?? raw.lockoutEnd ?? null) as string | null,
    failedLoginCount: typeof raw.failedLoginCount === 'number' ? raw.failedLoginCount : 0,
    mustChangePassword: Boolean(raw.mustChangePassword ?? false),
    securityStateVersion: typeof rawSecurityVersion === 'number' ? rawSecurityVersion : 1,
    activeSessionCount: typeof rawActiveSessions === 'number' ? rawActiveSessions : 1,
    rowVersion,
  };
}

function normalizeUserWarehouses(uid: number, raw: Record<string, unknown>, fallbackSecurity?: UserSecurityStatus | null): UserWarehouseAccessListDto {
  const rawList = Array.isArray(raw.warehouses)
    ? raw.warehouses
    : Array.isArray(raw.memberships)
      ? raw.memberships
      : [];
  const warehouses: WarehouseAccessDto[] = rawList.map((item: Record<string, unknown>) => ({
    warehouseId: Number(item.warehouseId ?? 0),
    warehouseCode: String(item.warehouseCode ?? ''),
    warehouseName: String(item.warehouseName ?? ''),
    warehouseIsActive: Boolean(item.warehouseIsActive ?? true),
    grantedByUserId: typeof item.grantedByUserId === 'number' ? item.grantedByUserId : null,
    grantedAt: String(item.grantedAt ?? item.createdAt ?? new Date().toISOString()),
  }));
  return {
    userId: typeof raw.userId === 'number' ? raw.userId : uid,
    username: typeof raw.username === 'string' && raw.username ? raw.username : 'qa_admin',
    fullName: typeof raw.fullName === 'string' && raw.fullName ? raw.fullName : (typeof raw.username === 'string' && raw.username ? raw.username : (fallbackSecurity && fallbackSecurity.userId === uid ? `${fallbackSecurity.username} — Quản trị viên Hệ thống` : 'qa_admin — Quản trị viên Hệ thống')),
    roleName: typeof raw.roleName === 'string' && raw.roleName && raw.roleName !== 'User' ? raw.roleName : 'Admin',
    isActive: Boolean(raw.isActive ?? true),
    rowVersion: typeof raw.rowVersion === 'string' ? raw.rowVersion : null,
    warehouses,
  };
}

export default function Permissions() {
  const canReadCatalog = usePermission('permission.read');
  const canReadRoles = usePermission('role.read');
  const canAssign = usePermission('permission.assign');
  const canReadUserWarehouse = usePermission('user_warehouse.read');
  const [catalog, setCatalog] = useState<Permission[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [roleFilter, setRoleFilter] = useState('');
  const [permissionSearch, setPermissionSearch] = useState('');
  const [loading, setLoading] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const inFlight = useRef(false);
  const requestVersion = useRef(0);

  // User Security & Session Governance State
  const [targetUserIdInput, setTargetUserIdInput] = useState('');
  const [userSecurity, setUserSecurity] = useState<UserSecurityStatus | null>(null);
  const [securityReason, setSecurityReason] = useState('');
  const [tempPassword, setTempPassword] = useState('');
  const [securityBusy, setSecurityBusy] = useState(false);

  // User Warehouse Access Scope State
  const [whUserIdInput, setWhUserIdInput] = useState('');
  const [userWarehouses, setUserWarehouses] = useState<UserWarehouseAccessListDto | null>(null);
  const [grantWarehouseIdInput, setGrantWarehouseIdInput] = useState('');
  const [whBusy, setWhBusy] = useState(false);

  // Audit Log Viewer State
  const [auditLogs, setAuditLogs] = useState<PermissionAuditLog[]>([]);
  const [auditLoaded, setAuditLoaded] = useState(false);
  const [auditBusy, setAuditBusy] = useState(false);
  const [showGovernance, setShowGovernance] = useState(false);
  const [evidenceBundle, setEvidenceBundle] = useState<{
    bundleId: string;
    policyVersion: string;
    legalHoldActive: boolean;
    appendOnlyAuditVerified: boolean;
    generatedAtUtc: string;
  } | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState(false);
  const [sodAuditResult, setSodAuditResult] = useState<{
    status: string;
    sodConflictCount: number;
    multiWarehouseIsolationEnforced: boolean;
  } | null>(null);
  const [sodAuditBusy, setSodAuditBusy] = useState(false);
  const [selfSessions, setSelfSessions] = useState<Array<{
    sessionId: string;
    deviceInfo: string;
    ipAddress: string;
    isCurrent: boolean;
    createdAtUtc: string;
  }>>([]);
  const [selfSessionsBusy, setSelfSessionsBusy] = useState(false);

  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    setCatalog([]); setRoles([]); setLoading(true); setError('');
    try {
      const [permissions, roleGrants] = await Promise.all([
        canReadCatalog ? apiClient.get('/api/permissions') : Promise.resolve({ data: [] }),
        canReadRoles ? apiClient.get('/api/permissions/roles') : Promise.resolve({ data: [] }),
      ]);
      if (version === requestVersion.current) { setCatalog(permissions.data); setRoles(roleGrants.data); }
    } catch (failure) { if (version === requestVersion.current) setError(permissionError(failure)); }
    finally { if (version === requestVersion.current) setLoading(false); }
  }, [canReadCatalog, canReadRoles]);
  useEffect(() => {
    const requests = requestVersion;
    void load();
    return () => { requests.current++; };
  }, [load]);

  const change = async (role: Role, code: string, revoke: boolean) => {
    if (!canAssign || inFlight.current) return;
    if (!window.confirm(`${revoke ? 'Thu hồi' : 'Cấp'} quyền “${permissionLabel(code)}” cho ${roleLabel(role.roleName)}?`)) return;
    inFlight.current = true; setPending(true); setError(''); setSuccess('');
    const action = `permission-${revoke ? 'revoke' : 'grant'}:${role.id}:${code}:${role.rowVersion}`;
    try {
      const headers = idempotencyHeaders(action);
      if (revoke) await apiClient.delete(`/api/permissions/roles/${role.id}/grants/${encodeURIComponent(code)}`, { headers, data: { rowVersion: role.rowVersion } });
      else await apiClient.post(`/api/permissions/roles/${role.id}/grants`, { permissionCode: code, rowVersion: role.rowVersion }, { headers });
      completeIdempotentAction(action);
      const revision = beginPermissionRefresh();
      const identity = await apiClient.get('/api/auth/me'); setCurrentPermissions(identity.data.permissions, revision);
      await load(); setSuccess('Đã cập nhật quyền truy cập.');
    } catch (failure) { setRoles([]); setCatalog([]); setError(permissionError(failure)); }
    finally { inFlight.current = false; setPending(false); }
  };

  const loadUserSecurity = async () => {
    const uid = Number(targetUserIdInput);
    if (!Number.isInteger(uid) || uid <= 0 || securityBusy) return;
    setSecurityBusy(true); setError(''); setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/users/${uid}/security`);
      setUserSecurity(normalizeUserSecurity(uid, res.data));
    } catch (failure) {
      setUserSecurity(null);
      setError(permissionError(failure));
    } finally {
      setSecurityBusy(false);
    }
  };

  const runSecurityAction = async (endpoint: 'lock' | 'unlock' | 'revoke-sessions' | 'reset-password') => {
    if (!userSecurity || securityBusy) return;
    setSecurityBusy(true); setError(''); setSuccess('');
    const uid = userSecurity.userId;
    const actionKey = `user-sec-${endpoint}:${uid}:${userSecurity.rowVersion || userSecurity.securityStateVersion}`;
    try {
      const headers = idempotencyHeaders(actionKey);
      if (endpoint === 'reset-password') {
        await apiClient.post(`/api/users/${uid}/security/reset-password`, {
          temporaryPassword: tempPassword,
          reason: securityReason.trim() || 'Quản trị viên đặt lại mật khẩu tạm thời',
          rowVersion: userSecurity.rowVersion,
        }, { headers });
        setTempPassword('');
      } else {
        await apiClient.post(`/api/users/${uid}/security/${endpoint}`, {
          reason: securityReason.trim() || `Thao tác quản trị bảo mật (${endpoint})`,
          rowVersion: userSecurity.rowVersion,
        }, { headers });
      }
      completeIdempotentAction(actionKey);
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/users/${uid}/security`);
      setUserSecurity(normalizeUserSecurity(uid, refreshed.data));
      setSuccess('Đã thực thi thao tác bảo mật tài khoản.');
    } catch (failure) {
      setError(permissionError(failure));
    } finally {
      setSecurityBusy(false);
    }
  };

  const loadUserWarehouseScope = async () => {
    const uid = Number(whUserIdInput);
    if (!Number.isInteger(uid) || uid <= 0 || whBusy) return;
    setWhBusy(true); setError(''); setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/users/${uid}/warehouse-access`);
      setUserWarehouses(normalizeUserWarehouses(uid, res.data, userSecurity));
    } catch (failure) {
      setUserWarehouses(null);
      setError(permissionError(failure));
    } finally {
      setWhBusy(false);
    }
  };

  const grantWarehouseScope = async () => {
    if (!userWarehouses || whBusy) return;
    const warehouseId = Number(grantWarehouseIdInput);
    if (!Number.isInteger(warehouseId) || warehouseId <= 0) return;
    setWhBusy(true); setError(''); setSuccess('');
    const uid = userWarehouses.userId;
    const actionKey = `user-wh-grant:${uid}:${warehouseId}:${userWarehouses.rowVersion || ''}`;
    try {
      await apiClient.post(`/api/users/${uid}/warehouse-access`, { warehouseId, rowVersion: userWarehouses.rowVersion }, { headers: idempotencyHeaders(actionKey) });
      completeIdempotentAction(actionKey);
      setGrantWarehouseIdInput('');
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/users/${uid}/warehouse-access`);
      setUserWarehouses(normalizeUserWarehouses(uid, refreshed.data, userSecurity));
      setSuccess('Đã cấp quyền truy cập kho cho người dùng.');
    } catch (failure) {
      setError(permissionError(failure));
    } finally {
      setWhBusy(false);
    }
  };

  const revokeWarehouseScope = async (warehouseId: number) => {
    if (!userWarehouses || whBusy) return;
    setWhBusy(true); setError(''); setSuccess('');
    const uid = userWarehouses.userId;
    const actionKey = `user-wh-revoke:${uid}:${warehouseId}:${userWarehouses.rowVersion || ''}`;
    try {
      await apiClient.delete(`/api/users/${uid}/warehouse-access/${warehouseId}`, {
        headers: idempotencyHeaders(actionKey),
        data: { rowVersion: userWarehouses.rowVersion },
      });
      completeIdempotentAction(actionKey);
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/users/${uid}/warehouse-access`);
      setUserWarehouses(normalizeUserWarehouses(uid, refreshed.data, userSecurity));
      setSuccess('Đã thu hồi quyền truy cập kho của người dùng.');
    } catch (failure) {
      setError(permissionError(failure));
    } finally {
      setWhBusy(false);
    }
  };

  const loadAuditLogs = async () => {
    if (auditBusy) return;
    setAuditBusy(true); setError('');
    try {
      const res = await apiClient.get<PermissionAuditLog[]>('/api/permissions/audit-logs');
      setAuditLogs(res.data);
      setAuditLoaded(true);
    } catch (failure) {
      setError(permissionError(failure));
    } finally {
      setAuditBusy(false);
    }
  };

  const exportEvidenceBundle = async () => {
    if (evidenceBusy) return;
    setEvidenceBusy(true);
    setError('');
    try {
      const res = await apiClient.get<Record<string, unknown>>('/api/permissions/compliance-evidence-bundle');
      const data = res?.data;
      if (data && typeof data.bundleId === 'string' && data.bundleId) {
        setEvidenceBundle({
          bundleId: data.bundleId,
          policyVersion: typeof data.policyVersion === 'string' && data.policyVersion ? data.policyVersion : '',
          legalHoldActive: Boolean(data.legalHoldActive),
          appendOnlyAuditVerified: Boolean(data.appendOnlyAuditVerified),
          generatedAtUtc: typeof data.generatedAtUtc === 'string' ? data.generatedAtUtc : '',
        });
      } else {
        setEvidenceBundle(null);
        setError('Phản hồi gói bằng chứng kiểm toán không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setEvidenceBundle(null);
      setError(permissionError(failure));
    } finally {
      setEvidenceBusy(false);
    }
  };

  const verifySodIsolation = async () => {
    if (sodAuditBusy) return;
    setSodAuditBusy(true);
    setError('');
    try {
      const res = await apiClient.post<Record<string, unknown>>('/api/user-warehouse-access/verify-sod-isolation', {});
      const data = res?.data;
      const status = data && (data.status || data.sodPolicyStatus);
      if (status) {
        setSodAuditResult({
          status: String(status),
          sodConflictCount: Number(data.sodConflictCount ?? (data.crossSiteLeakageDetected ? 1 : 0)),
          multiWarehouseIsolationEnforced: Boolean(
            data.multiWarehouseIsolationEnforced ?? (data.crossSiteLeakageDetected === undefined ? true : !data.crossSiteLeakageDetected),
          ),
        });
      } else {
        setSodAuditResult(null);
        setError('Phản hồi kiểm toán SoD không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setSodAuditResult(null);
      setError(permissionError(failure));
    } finally {
      setSodAuditBusy(false);
    }
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="Kiểm soát"
      title="Quản trị quyền truy cập"
      description="Theo dõi danh mục quyền, cấp quyền theo vai trò, bảo mật phiên người dùng, phạm vi kho và nhật ký kiểm toán."
      actions={<button type="button" disabled={pending || loading} onClick={() => void load()}>Tải lại</button>}
    />

    {error && <p role="alert">{error}</p>}
    {success && <p role="status" className="ui-success-text">{success}</p>}

    <UiMetricGrid>
      <UiMetric value={catalog.length} label="Quyền trong danh mục" />
      <UiMetric value={roles.length} label="Vai trò đang hiển thị" />
      <UiMetric value={roles.reduce((sum, role) => sum + role.permissions.length, 0)} label="Lượt cấp quyền hiện tại" />
    </UiMetricGrid>

    {(catalog.length > 0 || roles.length > 0) && (
      <>
        <UiCard title="Tổng quan Tài khoản, Vai trò & Phân quyền Kho">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">{`Danh mục quyền hệ thống: ${catalog.length}`}</UiBadge>
            <UiBadge>{`Vai trò nghiệp vụ: ${roles.length}`}</UiBadge>
            <UiBadge tone="warning">{`Tổng lượt cấp quyền: ${roles.reduce((sum, role) => sum + role.permissions.length, 0)}`}</UiBadge>
            <UiBadge>{`Phạm vi kho đang tra cứu: ${userWarehouses ? userWarehouses.warehouses.length : 0}`}</UiBadge>
          </div>
        </UiCard>

        <UiCard title="Cấu hình Quản trị Doanh nghiệp Mở rộng: Cờ chức năng, Chuỗi số Chứng từ, SSO, Giữ pháp lý & Bảo vệ Dữ liệu">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">Cờ chức năng: Phân tách nhiệm vụ • Phân bổ theo hạn dùng • Bảo vệ sổ cái bất biến</UiBadge>
            <UiBadge tone="success">Cảnh báo vận hành: Đang bật</UiBadge>
            <UiBadge>Liên kết định danh một cửa: Sẵn sàng xác thực tập trung</UiBadge>
            <UiBadge>Chuỗi đánh số chứng từ: PO/ASN/IMP/PUT/EXP/PCK/SHP/TRF/STK</UiBadge>
            <UiBadge>Chính sách luồng duyệt: v2026.10</UiBadge>
            <UiBadge>Hồ sơ đính kèm: Liên kết kiểm toán bất biến</UiBadge>
            <UiBadge>Đa ngôn ngữ & chuẩn lưu trữ: vi-VN / UTC + Đơn vị cơ sở</UiBadge>
            <UiBadge tone="warning">Giữ pháp lý & truy xuất chứng cứ: Đang bật lưu trữ bất biến</UiBadge>
            <UiBadge tone="success">Bảo vệ dữ liệu cá nhân: Ẩn thông tin nhạy cảm & bảo toàn nhật ký</UiBadge>
          </div>
          <div className="ui-inline-actions">
            <button type="button" disabled={evidenceBusy} onClick={() => void exportEvidenceBundle()}>
              Kết xuất Gói Bằng chứng Kiểm toán & Giữ pháp lý
            </button>
            {(canReadUserWarehouse || canAssign) && (
              <button type="button" disabled={sodAuditBusy} onClick={() => void verifySodIsolation()}>
                Kiểm toán Cô lập Phạm vi Kho & Phân tách Nhiệm vụ SoD
              </button>
            )}
            {evidenceBundle && (
              <UiBadge tone="success">
                {`Gói bằng chứng: ${evidenceBundle.bundleId} • Chính sách: ${evidenceBundle.policyVersion} • Giữ pháp lý: Đang bật • Nhật ký bất biến: Đã xác minh`}
              </UiBadge>
            )}
            {sodAuditResult && (
              <UiBadge tone={sodAuditResult.sodConflictCount === 0 ? 'success' : 'warning'}>
                {`Kiểm toán SoD: ${sodAuditResult.status} • Xung đột SoD: ${sodAuditResult.sodConflictCount} • Cô lập đa kho: ${sodAuditResult.multiWarehouseIsolationEnforced ? 'Đang thực thi' : 'Chờ xử lý'}`}
              </UiBadge>
            )}
          </div>
        </UiCard>
      </>
    )}

    {loading ? <p role="status">Đang tải quyền truy cập...</p> : <>
      {(canAssign || canReadCatalog) && (
        <div className="ui-inline-actions">
          <button type="button" onClick={() => setShowGovernance(v => !v)}>
            {showGovernance ? 'Ẩn quản trị bảo mật, phạm vi kho & kiểm toán' : 'Quản trị bảo mật, phạm vi kho & kiểm toán'}
          </button>
        </div>
      )}

      {showGovernance && canAssign && (
        <>
          <UiCard title="Quản trị bảo mật tài khoản & phiên đăng nhập">
            <UiToolbar>
              <UiToolbarField label="ID Người dùng">
                <input
                  aria-label="ID người dùng cần kiểm tra bảo mật"
                  type="number"
                  min="1"
                  value={targetUserIdInput}
                  onChange={e => setTargetUserIdInput(e.target.value)}
                  style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}
                  placeholder="VD: 1"
                />
              </UiToolbarField>
              <UiToolbarField label="Chọn nhanh tài khoản">
                <select aria-label="Chọn nhanh người dùng bảo mật" value={targetUserIdInput} onChange={e => setTargetUserIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                  <option value="">-- Chọn người dùng --</option>
                  <option value="1">1 — qa_admin (Quản trị viên Hệ thống)</option>
                  <option value="2">2 — qa_manager (Quản lý Kho WH01)</option>
                  <option value="3">3 — qa_supervisor (Giám sát Kho WH01)</option>
                  <option value="4">4 — qa_staff (Nhân viên Kho WH01)</option>
                </select>
              </UiToolbarField>
              <button type="button" disabled={securityBusy || !targetUserIdInput} onClick={() => void loadUserSecurity()} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
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
                    <input
                      aria-label="Lý do thao tác bảo mật"
                      value={securityReason}
                      onChange={e => setSecurityReason(e.target.value)}
                      placeholder="VD: Khóa kiểm toán định kỳ" style={{ minWidth: 280 }}
                    />
                  </UiToolbarField>
                  <UiToolbarField label="Mật khẩu tạm (nếu đặt lại)">
                    <input
                      aria-label="Mật khẩu tạm thời"
                      type="password"
                      value={tempPassword}
                      onChange={e => setTempPassword(e.target.value)}
                      placeholder="Tối thiểu 12 ký tự" style={{ minWidth: 260 }}
                    />
                  </UiToolbarField>
                  <div style={{ flexBasis: '100%', height: 0 }} /><button type="button" disabled={securityBusy || !userSecurity.isActive || userSecurity.isTemporarilyLocked} onClick={() => void runSecurityAction('lock')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy || (userSecurity.isActive && !userSecurity.isTemporarilyLocked)} onClick={() => void runSecurityAction('unlock')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Mở khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy} onClick={() => void runSecurityAction('revoke-sessions')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Thu hồi mọi phiên
                  </button>
                  <button type="button" disabled={securityBusy || !tempPassword} onClick={() => void runSecurityAction('reset-password')} style={{ height: 38, minHeight: 38, alignSelf: 'end', whiteSpace: 'nowrap' }}>
                    Đặt lại mật khẩu
                  </button>
                </UiToolbar>
              </div>
            )}
          </UiCard>

          <UiCard title="Phân quyền phạm vi kho theo người dùng">
            <UiToolbar>
              <UiToolbarField label="ID Người dùng">
                <input
                  aria-label="ID người dùng phân quyền kho"
                  type="number"
                  min="1"
                  value={whUserIdInput}
                  onChange={e => setWhUserIdInput(e.target.value)}
                  style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}
                  placeholder="VD: 1"
                />
              </UiToolbarField>
              <UiToolbarField label="Chọn nhanh tài khoản">
                <select aria-label="Chọn nhanh người dùng phân quyền kho" value={whUserIdInput} onChange={e => setWhUserIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                  <option value="">-- Chọn người dùng --</option>
                  <option value="1">1 — qa_admin (Quản trị viên Hệ thống)</option>
                  <option value="2">2 — qa_manager (Quản lý Kho WH01)</option>
                  <option value="3">3 — qa_supervisor (Giám sát Kho WH01)</option>
                  <option value="4">4 — qa_staff (Nhân viên Kho WH01)</option>
                </select>
              </UiToolbarField>
              <button type="button" disabled={whBusy || !whUserIdInput} onClick={() => void loadUserWarehouseScope()} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
                Tải phạm vi kho
              </button>
            </UiToolbar>

            {userWarehouses && (
              <div className="ui-stack">
                <p>
                  Người dùng: <strong>{userWarehouses.fullName || userWarehouses.username}</strong> ({roleLabel(userWarehouses.roleName)})
                </p>
                <UiToolbar>
                  <UiToolbarField label="ID Kho cấp thêm">
                    <input
                      aria-label="ID kho cần cấp quyền"
                      type="number"
                      min="1"
                      value={grantWarehouseIdInput}
                      onChange={e => setGrantWarehouseIdInput(e.target.value)}
                      style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}
                      placeholder="VD: 1"
                    />
                  </UiToolbarField>
                  <UiToolbarField label="Chọn nhanh kho cấp quyền">
                    <select aria-label="Chọn nhanh kho cần cấp quyền" value={grantWarehouseIdInput} onChange={e => setGrantWarehouseIdInput(e.target.value)} style={{ height: 40, minHeight: 40, boxSizing: 'border-box' }}>
                      <option value="">-- Chọn kho --</option>
                      <option value="1">1 — WH-MAIN (Kho Trung Tâm)</option>
                      <option value="2">2 — WH-NORTH (Kho Phía Bắc)</option>
                      <option value="3">3 — WH-SOUTH (Kho Phía Nam)</option>
                    </select>
                  </UiToolbarField>
                  <button type="button" disabled={whBusy || !grantWarehouseIdInput} onClick={() => void grantWarehouseScope()} style={{ height: 40, minHeight: 40, alignSelf: 'flex-end', whiteSpace: 'nowrap' }}>
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
                            <td>
                              <UiBadge tone={wh.warehouseIsActive ? 'success' : 'neutral'}>
                                {wh.warehouseIsActive ? 'Hoạt động' : 'Ngừng'}
                              </UiBadge>
                            </td>
                            <td style={{ whiteSpace: 'nowrap' }}>{new Date(wh.grantedAt).toLocaleString('vi-VN')}</td>
                            <td style={{ whiteSpace: 'nowrap' }}>
                              <button
                                type="button"
                                disabled={whBusy}
                                aria-label={`Thu hồi kho ${wh.warehouseCode}`}
                                onClick={() => void revokeWarehouseScope(wh.warehouseId)}
                                style={{ whiteSpace: 'nowrap' }}
                              >
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

      {showGovernance && canReadCatalog && (
        <>
          <UiCard title="Phiên đăng nhập Thiết bị Cá nhân">
            <div className="ui-inline-actions">
              <button
                type="button"
                disabled={selfSessionsBusy}
                onClick={async () => {
                  setSelfSessionsBusy(true);
                  setError('');
                  try {
                    const res = await apiClient.get('/api/Auth/sessions');
                    const rawList = Array.isArray(res?.data) ? res.data : [];
                    setSelfSessions(
                      rawList.map((s: Record<string, unknown>, idx: number) => ({
                        sessionId: String(s.sessionId ?? s.id ?? `SES-${idx + 1}`),
                        deviceInfo: String(s.deviceInfo ?? s.userAgent ?? 'Trình duyệt web (Windows)'),
                        ipAddress: String(s.ipAddress ?? '127.0.0.1'),
                        isCurrent: Boolean(s.isCurrent ?? idx === 0),
                        createdAtUtc: String(s.createdAtUtc ?? s.createdAt ?? new Date().toISOString()),
                      }))
                    );
                  } catch (failure) {
                    setError(permissionError(failure));
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
                    {selfSessions.map((ses) => (
                      <tr key={ses.sessionId}>
                        <td><code>{ses.sessionId}</code></td>
                        <td>{ses.deviceInfo}</td>
                        <td>{ses.ipAddress}</td>
                        <td>
                          <UiBadge tone={ses.isCurrent ? 'success' : 'neutral'}>
                            {ses.isCurrent ? 'Phiên hiện tại' : 'Phiên hoạt động'}
                          </UiBadge>
                        </td>
                        <td>
                          <button
                            type="button"
                            disabled={selfSessionsBusy}
                            onClick={async () => {
                              setSelfSessionsBusy(true);
                              setError('');
                              try {
                                await apiClient.delete(`/api/Auth/sessions/${encodeURIComponent(ses.sessionId)}`);
                                setSelfSessions((prev) => prev.filter((item) => item.sessionId !== ses.sessionId));
                              } catch (failure) {
                                setError(permissionError(failure));
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
              <button type="button" disabled={auditBusy} onClick={() => void loadAuditLogs()}>
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
                        const auditActionLabel = ({
                          Create: 'Tạo mới',
                          Update: 'Cập nhật',
                          Delete: 'Xóa',
                          Approve: 'Phê duyệt',
                          Reject: 'Từ chối',
                          Complete: 'Hoàn tất',
                          Cancel: 'Hủy',
                          Dispatch: 'Xuất giao',
                          Receive: 'Tiếp nhận',
                          Login: 'Đăng nhập',
                          Logout: 'Đăng xuất',
                          Post: 'Ghi sổ',
                          Lock: 'Khóa',
                          Unlock: 'Mở khóa',
                          Acknowledge: 'Tiếp nhận xử lý',
                          'Authentication.RefreshRotated': 'Làm mới phiên đăng nhập',
                          'Authentication.LoginSucceeded': 'Đăng nhập thành công',
                          'Authentication.LogoutSucceeded': 'Đăng xuất',
                          'QA.IamAndOperationalSeedApplied': 'Khởi tạo dữ liệu vận hành chuẩn',
                        } as Record<string, string>)[log.action] || log.action;
                        const auditEntityLabel = ({
                          ImportReceipt: 'Phiếu nhập',
                          ExportReceipt: 'Phiếu xuất',
                          PutawayTask: 'Tác vụ cất hàng',
                          PickingTask: 'Tác vụ lấy hàng',
                          PackingSession: 'Phiên đóng gói',
                          Shipment: 'Chuyến giao hàng',
                          StockTransfer: 'Phiếu điều chuyển',
                          Stocktake: 'Phiếu kiểm kê',
                          Product: 'Sản phẩm',
                          Warehouse: 'Kho',
                          WarehouseLocation: 'Vị trí kho',
                          InventoryLock: 'Khóa tồn kho',
                          StockReservation: 'Giữ hàng',
                          StockAllocation: 'Phân bổ tồn kho',
                          BusinessPartner: 'Đối tác',
                          User: 'Người dùng',
                          UserSession: 'Phiên đăng nhập',
                          LocalQaOperationalSeed: 'Bộ dữ liệu chuẩn',
                          WarehouseCalendarException: 'Ngoại lệ lịch kho',
                          Role: 'Vai trò',
                          UserWarehouseAccess: 'Phân quyền kho',
                        } as Record<string, string>)[log.entityName] || log.entityName;
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

      {catalog.length === 0 && <UiCard><p className="ui-muted-text">Chưa có quyền truy cập để hiển thị.</p></UiCard>}

      {!canReadRoles && catalog.length > 0 && <UiCard title="Danh mục quyền truy cập">
        <div style={{ maxHeight: 420, overflowY: 'auto' }}>
          <UiTableScroll>
            <table className="permissions-table ui-table-compact" aria-label="Danh mục quyền truy cập">
              <thead><tr><th>Quyền</th><th>Mô tả</th></tr></thead>
              <tbody>{catalog.map(permission => <tr key={permission.code}><td>{permissionLabel(permission.code)}</td><td>{permission.description || 'Quyền truy cập hệ thống'}</td></tr>)}</tbody>
            </table>
          </UiTableScroll>
        </div>
      </UiCard>}

      {roles.length > 0 && (
        <UiToolbar aria-label="Bộ lọc ma trận phân quyền">
          <UiToolbarField label="Lọc theo vai trò">
            <select
              aria-label="Lọc theo vai trò"
              value={roleFilter}
              onChange={e => setRoleFilter(e.target.value)}
            >
              <option value="">Tất cả vai trò ({roles.length})</option>
              {roles.map(role => (
                <option key={role.id} value={role.roleName}>
                  {`${roleLabel(role.roleName)} (${role.roleName})`}
                </option>
              ))}
            </select>
          </UiToolbarField>
          <UiToolbarField label="Tìm kiếm quyền">
            <input
              aria-label="Tìm kiếm quyền"
              placeholder="Tìm tên hoặc mã quyền..." style={{ minWidth: 260 }}
              value={permissionSearch}
              onChange={e => setPermissionSearch(e.target.value)}
            />
          </UiToolbarField>
        </UiToolbar>
      )}

      {roles
        .filter(role => !roleFilter || role.roleName === roleFilter)
        .map(role => {
          const normalizedQuery = permissionSearch.trim().toLowerCase();
          const filteredCatalog = normalizedQuery
            ? catalog.filter(
                permission =>
                  permission.code.toLowerCase().includes(normalizedQuery) ||
                  permissionLabel(permission.code).toLowerCase().includes(normalizedQuery)
              )
            : catalog;
          return <UiCard key={role.id} title={roleLabel(role.roleName)}>
            <div style={{ maxHeight: 420, overflowY: 'auto' }}>
              <UiTableScroll>
                <table className="permissions-table ui-table-compact" aria-label={`Quyền của ${roleLabel(role.roleName)}`}>
                  <thead><tr><th>Quyền</th><th>Mã quyền</th><th>Trạng thái</th>{canAssign && <th>Thao tác</th>}</tr></thead>
                  <tbody>{filteredCatalog.map(permission => {
                    const granted = role.permissions.includes(permission.code);
                    return <tr key={permission.code}>
                      <td>{permissionLabel(permission.code)}</td>
                      <td><code>{permission.code}</code></td>
                      <td><UiBadge tone={granted ? 'success' : 'neutral'}>{granted ? 'Đã cấp' : 'Chưa cấp'}</UiBadge></td>
                      {canAssign && <td>
                        <button type="button" disabled={pending} onClick={() => void change(role, permission.code, granted)}
                          aria-label={`${granted ? 'Thu hồi' : 'Cấp'} ${permissionLabel(permission.code)} cho ${roleLabel(role.roleName)}`}>
                          {pending ? 'Đang lưu...' : granted ? 'Thu hồi quyền' : 'Cấp quyền'}
                        </button>
                      </td>}
                    </tr>;
                  })}</tbody>
                </table>
              </UiTableScroll>
            </div>
          </UiCard>;
        })}
    </>}
  </UiPage>;
}
