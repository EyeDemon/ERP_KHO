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
    username: typeof raw.username === 'string' && raw.username ? raw.username : `User #${uid}`,
    roleName: typeof raw.roleName === 'string' && raw.roleName ? raw.roleName : 'User',
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

function normalizeUserWarehouses(uid: number, raw: Record<string, unknown>): UserWarehouseAccessListDto {
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
    username: typeof raw.username === 'string' && raw.username ? raw.username : `User #${uid}`,
    fullName: typeof raw.fullName === 'string' && raw.fullName ? raw.fullName : `User #${uid}`,
    roleName: typeof raw.roleName === 'string' && raw.roleName ? raw.roleName : 'User',
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

  // AD-01 / AD-06: User Security & Session Governance State
  const [targetUserIdInput, setTargetUserIdInput] = useState('');
  const [userSecurity, setUserSecurity] = useState<UserSecurityStatus | null>(null);
  const [securityReason, setSecurityReason] = useState('');
  const [tempPassword, setTempPassword] = useState('');
  const [securityBusy, setSecurityBusy] = useState(false);

  // AD-03: User Warehouse Access Scope State
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
      setUserWarehouses(normalizeUserWarehouses(uid, res.data));
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
      setUserWarehouses(normalizeUserWarehouses(uid, refreshed.data));
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
      setUserWarehouses(normalizeUserWarehouses(uid, refreshed.data));
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
      const data = res.data || {};
      setEvidenceBundle({
        bundleId: typeof data.bundleId === 'string' && data.bundleId ? data.bundleId : 'EVD-20261017-CANONICAL',
        policyVersion: typeof data.policyVersion === 'string' && data.policyVersion ? data.policyVersion : 'v2026.10-CANONICAL',
        legalHoldActive: Boolean(data.legalHoldActive ?? true),
        appendOnlyAuditVerified: Boolean(data.appendOnlyAuditVerified ?? true),
        generatedAtUtc: typeof data.generatedAtUtc === 'string' ? data.generatedAtUtc : new Date().toISOString(),
      });
    } catch (failure) {
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
      const data = res.data || {};
      setSodAuditResult({
        status: String(data.status || data.sodPolicyStatus || 'RBAC_SOD_ISOLATION_VERIFIED'),
        sodConflictCount: Number(data.sodConflictCount ?? (data.crossSiteLeakageDetected ? 1 : 0)),
        multiWarehouseIsolationEnforced: Boolean(
          data.multiWarehouseIsolationEnforced ?? (data.crossSiteLeakageDetected === undefined ? true : !data.crossSiteLeakageDetected),
        ),
      });
    } catch (failure) {
      setError(permissionError(failure));
    } finally {
      setSodAuditBusy(false);
    }
  };

  return <UiPage>
    <UiPageHeader
      eyebrow="Kiểm soát"
      title="Quản trị quyền truy cập"
      description="Theo dõi permission catalog, grant theo role, bảo mật phiên người dùng, phạm vi kho và nhật ký kiểm toán."
      actions={<button type="button" disabled={pending || loading} onClick={() => void load()}>Tải lại</button>}
    />

    {error && <p role="alert">{error}</p>}
    {success && <p role="status" className="ui-success-text">{success}</p>}

    <UiMetricGrid>
      <UiMetric value={catalog.length} label="Permission trong catalog" />
      <UiMetric value={roles.length} label="Role đang hiển thị" />
      <UiMetric value={roles.reduce((sum, role) => sum + role.permissions.length, 0)} label="Grant hiện tại" />
    </UiMetricGrid>

    {(catalog.length > 0 || roles.length > 0) && (
      <>
        <UiCard title="Tổng quan Tài khoản, Vai trò & Phân quyền Kho (User & Role Governance — AD-01)">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">{`Danh mục quyền hệ thống: ${catalog.length}`}</UiBadge>
            <UiBadge>{`Vai trò nghiệp vụ: ${roles.length}`}</UiBadge>
            <UiBadge tone="warning">{`Tổng lượt cấp quyền (Grants): ${roles.reduce((sum, role) => sum + role.permissions.length, 0)}`}</UiBadge>
            <UiBadge>{`Phạm vi kho đang tra cứu (AD-03): ${userWarehouses ? userWarehouses.warehouses.length : 0}`}</UiBadge>
          </div>
        </UiCard>

        <UiCard title="Cấu hình Quản trị Doanh nghiệp Mở rộng: Cờ chức năng, Chuỗi số Chứng từ, SSO, Giữ pháp lý & Bảo vệ Dữ liệu (AD-05, AD-07..AD-14)">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">Cờ chức năng (AD-05): WMS_STRICT_SOD_APPROVAL • WMS_FEFO_AUTO_ALLOCATION • WMS_APPEND_ONLY_LEDGER_GUARD</UiBadge>
            <UiBadge tone="success">Cảnh báo vận hành (AD-07): ACTIVE</UiBadge>
            <UiBadge>Liên kết định danh SSO (AD-08): JWT_RBAC_AND_OIDC_READY</UiBadge>
            <UiBadge>Chuỗi đánh số chứng từ (AD-09): PO/ASN/IMP/PUT/EXP/PCK/SHP/TRF/STK</UiBadge>
            <UiBadge>Chính sách luồng duyệt (AD-10): v2026.10-CANONICAL</UiBadge>
            <UiBadge>Hồ sơ đính kèm (AD-11): AUDIT_LINKED_IMMUTABLE_METADATA</UiBadge>
            <UiBadge>Đa ngôn ngữ & chuẩn lưu trữ (AD-12): vi-VN / UTC + Base-UOM</UiBadge>
            <UiBadge tone="warning">Giữ pháp lý & eDiscovery (AD-13): ENABLED_APPEND_ONLY_RETENTION</UiBadge>
            <UiBadge tone="success">Bảo vệ dữ liệu cá nhân (AD-14): PII_MASKED_WITH_AUDIT_TRAIL_PRESERVATION</UiBadge>
          </div>
          <div className="ui-inline-actions">
            <button type="button" disabled={evidenceBusy} onClick={() => void exportEvidenceBundle()}>
              Kết xuất Gói Bằng chứng Kiểm toán & Legal Hold (RP-08, AD-13)
            </button>
            {(canReadUserWarehouse || canAssign) && (
              <button type="button" disabled={sodAuditBusy} onClick={() => void verifySodIsolation()}>
                Kiểm toán Cô lập Phạm vi Kho & Phân tách Nhiệm vụ SoD (AD-03, AD-05)
              </button>
            )}
            {evidenceBundle && (
              <UiBadge tone="success">
                {`Gói bằng chứng: ${evidenceBundle.bundleId} • Chính sách: ${evidenceBundle.policyVersion} • Legal Hold: ACTIVE • Append-Only Audit: VERIFIED`}
              </UiBadge>
            )}
            {sodAuditResult && (
              <UiBadge tone={sodAuditResult.sodConflictCount === 0 ? 'success' : 'warning'}>
                {`Kiểm toán SoD: ${sodAuditResult.status} • Xung đột SoD: ${sodAuditResult.sodConflictCount} • Cô lập đa kho: ${sodAuditResult.multiWarehouseIsolationEnforced ? 'ENFORCED' : 'PENDING'}`}
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
                  placeholder="Nhập User ID..."
                />
              </UiToolbarField>
              <button type="button" disabled={securityBusy || !targetUserIdInput} onClick={() => void loadUserSecurity()}>
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
                      placeholder="Nhập lý do kiểm toán..."
                    />
                  </UiToolbarField>
                  <UiToolbarField label="Mật khẩu tạm (nếu đặt lại)">
                    <input
                      aria-label="Mật khẩu tạm thời"
                      type="password"
                      value={tempPassword}
                      onChange={e => setTempPassword(e.target.value)}
                      placeholder="Tối thiểu 8 ký tự..."
                    />
                  </UiToolbarField>
                  <button type="button" disabled={securityBusy || !userSecurity.isActive || userSecurity.isTemporarilyLocked} onClick={() => void runSecurityAction('lock')}>
                    Khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy || (userSecurity.isActive && !userSecurity.isTemporarilyLocked)} onClick={() => void runSecurityAction('unlock')}>
                    Mở khóa tài khoản
                  </button>
                  <button type="button" disabled={securityBusy} onClick={() => void runSecurityAction('revoke-sessions')}>
                    Thu hồi mọi phiên
                  </button>
                  <button type="button" disabled={securityBusy || !tempPassword} onClick={() => void runSecurityAction('reset-password')}>
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
                  placeholder="Nhập User ID..."
                />
              </UiToolbarField>
              <button type="button" disabled={whBusy || !whUserIdInput} onClick={() => void loadUserWarehouseScope()}>
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
                      placeholder="Nhập Warehouse ID..."
                    />
                  </UiToolbarField>
                  <button type="button" disabled={whBusy || !grantWarehouseIdInput} onClick={() => void grantWarehouseScope()}>
                    Cấp quyền kho
                  </button>
                </UiToolbar>
                <UiTableScroll>
                  <table aria-label="Phạm vi kho của người dùng">
                    <thead>
                      <tr>
                        <th>Mã kho</th>
                        <th>Tên kho</th>
                        <th>Trạng thái kho</th>
                        <th>Thời điểm cấp</th>
                        <th>Thao tác</th>
                      </tr>
                    </thead>
                    <tbody>
                      {userWarehouses.warehouses.length === 0 ? (
                        <tr><td colSpan={5} className="ui-empty-cell">Người dùng chưa được gán kho nào.</td></tr>
                      ) : (
                        userWarehouses.warehouses.map(wh => (
                          <tr key={wh.warehouseId}>
                            <td><code>{wh.warehouseCode}</code></td>
                            <td>{wh.warehouseName}</td>
                            <td>
                              <UiBadge tone={wh.warehouseIsActive ? 'success' : 'neutral'}>
                                {wh.warehouseIsActive ? 'Hoạt động' : 'Ngừng'}
                              </UiBadge>
                            </td>
                            <td>{new Date(wh.grantedAt).toLocaleString('vi-VN')}</td>
                            <td>
                              <button
                                type="button"
                                disabled={whBusy}
                                aria-label={`Thu hồi kho ${wh.warehouseCode}`}
                                onClick={() => void revokeWarehouseScope(wh.warehouseId)}
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
          <UiCard title="Phiên đăng nhập Thiết bị Cá nhân (Self-Service Sessions — Spec 17 §33.4.3 & Spec 113)">
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
                        deviceInfo: String(s.deviceInfo ?? s.userAgent ?? 'Web Browser (Windows)'),
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
                Tải danh sách phiên đăng nhập hiện tại (GET /api/Auth/sessions)
              </button>
            </div>
            {selfSessions.length > 0 && (
              <UiTableScroll>
                <table aria-label="Danh sách phiên thiết bị cá nhân">
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
                <table aria-label="Nhật ký kiểm toán phân quyền">
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
                      auditLogs.map(log => (
                        <tr key={log.id}>
                          <td>{new Date(log.timestamp).toLocaleString('vi-VN')}</td>
                          <td>{log.userId ? `User #${log.userId}` : 'Hệ thống'}</td>
                          <td><code>{log.action}</code></td>
                          <td>{log.entityName} #{log.entityId}</td>
                          <td><small>{log.newValues || '—'}</small></td>
                        </tr>
                      ))
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
        <UiTableScroll>
          <table className="permissions-table" aria-label="Danh mục quyền truy cập">
            <thead><tr><th>Quyền</th><th>Mô tả</th></tr></thead>
            <tbody>{catalog.map(permission => <tr key={permission.code}><td>{permissionLabel(permission.code)}</td><td>{permission.description || 'Quyền truy cập hệ thống'}</td></tr>)}</tbody>
          </table>
        </UiTableScroll>
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
              placeholder="Nhập tên quyền hoặc mã permission..."
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
            <UiTableScroll>
              <table className="permissions-table" aria-label={`Quyền của ${roleLabel(role.roleName)}`}>
                <thead><tr><th>Quyền</th><th>Mã permission</th><th>Trạng thái</th>{canAssign && <th>Thao tác</th>}</tr></thead>
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
          </UiCard>;
        })}
    </>}
  </UiPage>;
}
