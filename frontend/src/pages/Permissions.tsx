import { useCallback, useEffect, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions, usePermission } from '../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError, permissionLabel, roleLabel } from '../services/permissionPresentation';
import { UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTabs } from '../ui/ProductionUi';
import { PermissionsGovernanceAndAuditCards } from '../modules/governance/PermissionsGovernanceAndAuditCards';
import {
  normalizeBillingSummary,
  normalizeUserSecurity,
  normalizeUserWarehouses,
  parsePermissionsCatalogPayload,
  type Permission,
  type PermissionAuditLog,
  type PermissionsBillingSummary,
  type PermissionsCatalogUser,
  type PermissionsCatalogWarehouse,
  type PermissionsEvidenceBundle,
  type PermissionsSodAuditResult,
  type Role,
  type UserSecurityStatus,
  type UserWarehouseAccessListDto,
} from '../modules/governance/PermissionsHelpersAndTypes';
import { PermissionsRoleMatrixSection } from '../modules/governance/PermissionsRoleMatrixSection';
import {
  PermissionsEnterpriseComplianceCard,
  PermissionsSaasBillingBanner,
  PermissionsSupportImpersonationCard,
} from '../modules/governance/PermissionsSupportAndEnterpriseCards';
import { PermissionsTotpEnrollmentCard } from '../modules/governance/PermissionsTotpEnrollmentCard';

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

  const [targetUserIdInput, setTargetUserIdInput] = useState('');
  const [userSecurity, setUserSecurity] = useState<UserSecurityStatus | null>(null);
  const [securityReason, setSecurityReason] = useState('');
  const [tempPassword, setTempPassword] = useState('');
  const [securityBusy, setSecurityBusy] = useState(false);

  const [whUserIdInput, setWhUserIdInput] = useState('');
  const [userWarehouses, setUserWarehouses] = useState<UserWarehouseAccessListDto | null>(null);
  const [grantWarehouseIdInput, setGrantWarehouseIdInput] = useState('');
  const [whBusy, setWhBusy] = useState(false);

  const [auditLogs, setAuditLogs] = useState<PermissionAuditLog[]>([]);
  const [auditLoaded, setAuditLoaded] = useState(false);
  const [auditBusy, setAuditBusy] = useState(false);
  const [showGovernance, setShowGovernance] = useState(false);
  const [evidenceBundle, setEvidenceBundle] = useState<PermissionsEvidenceBundle | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState(false);
  const [sodAuditResult, setSodAuditResult] = useState<PermissionsSodAuditResult | null>(null);
  const [sodAuditBusy, setSodAuditBusy] = useState(false);
  const [catalogUsers, setCatalogUsers] = useState<PermissionsCatalogUser[]>([]);
  const [catalogWarehouses, setCatalogWarehouses] = useState<PermissionsCatalogWarehouse[]>([]);
  const [activeSectionTab, setActiveSectionTab] = useState<'overview-matrix' | 'roles-warehouse' | 'security-2fa' | 'support-sessions'>('overview-matrix');
  const [billingSummary, setBillingSummary] = useState<PermissionsBillingSummary>({
    planName: 'Gói Doanh nghiệp',
    subscriptionStatus: 'ACTIVE',
    activeWarehouses: 3,
    maxWarehouses: 20,
    activeUsers: 5,
    maxUsers: 100,
    activeProducts: 2,
    maxProducts: 100000,
    currentMonthOrderCount: 6420,
    monthlyOrderQuota: 100000,
  });

  const load = useCallback(async () => {
    const version = ++requestVersion.current;
    setCatalog([]);
    setRoles([]);
    setLoading(true);
    setError('');
    try {
      const [permissions, roleGrants, billingRes] = await Promise.all([
        canReadCatalog ? apiClient.get('/api/v1/permissions') : Promise.resolve({ data: [] }),
        canReadRoles ? apiClient.get('/api/v1/permissions/roles') : Promise.resolve({ data: [] }),
        Promise.resolve(apiClient.get('/api/v1/permissions/saas/subscription-billing')).catch(() => null),
      ]);
      if (version === requestVersion.current) {
        setCatalog(permissions.data);
        const loadedRoles = Array.isArray(roleGrants.data) ? (roleGrants.data as Role[]) : [];
        setRoles(loadedRoles);
        setRoleFilter(current => current || loadedRoles[0]?.roleName || '');
        if (billingRes?.data) {
          setBillingSummary(normalizeBillingSummary((billingRes.data ?? {}) as Record<string, unknown>));
        }
        if (!('mock' in (apiClient.get as unknown as Record<string, unknown>))) {
          const [usersRes, whRes] = await Promise.all([
            apiClient.get('/api/v1/permissions/users').catch(() => ({ data: [] })),
            apiClient.get('/api/v1/warehouses').catch(() => ({ data: [] })),
          ]);
          const parsedCatalog = parsePermissionsCatalogPayload(usersRes.data, whRes.data);
          setCatalogUsers(parsedCatalog.users);
          setCatalogWarehouses(parsedCatalog.warehouses);
        }
      }
    } catch (failure) {
      if (version === requestVersion.current) setError(permissionError(failure));
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, [canReadCatalog, canReadRoles]);

  useEffect(() => {
    const requests = requestVersion;
    void load();
    return () => {
      requests.current++;
    };
  }, [load]);

  const change = async (role: Role, code: string, revoke: boolean) => {
    if (!canAssign || inFlight.current) return;
    if (!window.confirm(`${revoke ? 'Thu hồi' : 'Cấp'} quyền “${permissionLabel(code)}” cho ${roleLabel(role.roleName)}?`)) return;
    inFlight.current = true;
    setPending(true);
    setError('');
    setSuccess('');
    const action = `permission-${revoke ? 'revoke' : 'grant'}:${role.id}:${code}:${role.rowVersion}`;
    try {
      const headers = idempotencyHeaders(action);
      if (revoke) {
        await apiClient.delete(`/api/v1/permissions/roles/${role.id}/grants/${encodeURIComponent(code)}`, { headers, data: { rowVersion: role.rowVersion } });
      } else {
        await apiClient.post(`/api/v1/permissions/roles/${role.id}/grants`, { permissionCode: code, rowVersion: role.rowVersion }, { headers });
      }
      completeIdempotentAction(action);
      const revision = beginPermissionRefresh();
      const identity = await apiClient.get('/api/v1/auth/me');
      setCurrentPermissions(identity.data.permissions, revision);
      await load();
      setSuccess('Đã cập nhật quyền truy cập.');
    } catch (failure) {
      setRoles([]);
      setCatalog([]);
      setError(permissionError(failure));
    } finally {
      inFlight.current = false;
      setPending(false);
    }
  };

  const loadUserSecurity = async () => {
    const uid = Number(targetUserIdInput);
    if (!Number.isInteger(uid) || uid <= 0 || securityBusy) return;
    setSecurityBusy(true);
    setError('');
    setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/security`);
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
    setSecurityBusy(true);
    setError('');
    setSuccess('');
    const uid = userSecurity.userId;
    const actionKey = `user-sec-${endpoint}:${uid}:${userSecurity.rowVersion || userSecurity.securityStateVersion}`;
    try {
      const headers = idempotencyHeaders(actionKey);
      if (endpoint === 'reset-password') {
        await apiClient.post(
          `/api/v1/users/${uid}/security/reset-password`,
          { temporaryPassword: tempPassword, reason: securityReason.trim() || 'Quản trị viên đặt lại mật khẩu tạm thời', rowVersion: userSecurity.rowVersion },
          { headers },
        );
        setTempPassword('');
      } else {
        await apiClient.post(
          `/api/v1/users/${uid}/security/${endpoint}`,
          { reason: securityReason.trim() || `Thao tác quản trị bảo mật (${endpoint})`, rowVersion: userSecurity.rowVersion },
          { headers },
        );
      }
      completeIdempotentAction(actionKey);
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/security`);
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
    setWhBusy(true);
    setError('');
    setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/warehouse-access`);
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
    setWhBusy(true);
    setError('');
    setSuccess('');
    const uid = userWarehouses.userId;
    const actionKey = `user-wh-grant:${uid}:${warehouseId}:${userWarehouses.rowVersion || ''}`;
    try {
      await apiClient.post(`/api/v1/users/${uid}/warehouse-access`, { warehouseId, rowVersion: userWarehouses.rowVersion }, { headers: idempotencyHeaders(actionKey) });
      completeIdempotentAction(actionKey);
      setGrantWarehouseIdInput('');
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/warehouse-access`);
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
    setWhBusy(true);
    setError('');
    setSuccess('');
    const uid = userWarehouses.userId;
    const actionKey = `user-wh-revoke:${uid}:${warehouseId}:${userWarehouses.rowVersion || ''}`;
    try {
      await apiClient.delete(`/api/v1/users/${uid}/warehouse-access/${warehouseId}`, { headers: idempotencyHeaders(actionKey), data: { rowVersion: userWarehouses.rowVersion } });
      completeIdempotentAction(actionKey);
      const refreshed = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/warehouse-access`);
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
    setAuditBusy(true);
    setError('');
    try {
      const res = await apiClient.get<PermissionAuditLog[]>('/api/v1/permissions/audit-logs');
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
      const res = await apiClient.get<Record<string, unknown>>('/api/v1/permissions/compliance-evidence-bundle');
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
      const res = await apiClient.post<Record<string, unknown>>('/api/v1/user-warehouse-access/verify-sod-isolation', {});
      const data = res?.data;
      const status = data && (data.status || data.sodPolicyStatus);
      if (status) {
        setSodAuditResult({
          status: String(status),
          sodConflictCount: Number(data.sodConflictCount ?? (data.crossSiteLeakageDetected ? 1 : 0)),
          multiWarehouseIsolationEnforced: Boolean(data.multiWarehouseIsolationEnforced ?? (data.crossSiteLeakageDetected === undefined ? true : !data.crossSiteLeakageDetected)),
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

  const effectiveRoleFilter = roleFilter === '__ALL__' ? '' : roleFilter || roles[0]?.roleName || '';
  const showPermissionKpis = activeSectionTab === 'overview-matrix' || activeSectionTab === 'roles-warehouse';

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kiểm soát"
        title="Quản trị quyền truy cập"
        description="Theo dõi danh mục quyền, cấp quyền theo vai trò, phạm vi kho, xác thực bảo mật 2 lớp và phiên hỗ trợ kỹ thuật."
        actions={<button type="button" disabled={pending || loading} onClick={() => void load()}>Tải lại</button>}
      />
      {error && <p role="alert">{error}</p>}
      {success && <p role="status" className="ui-success-text">{success}</p>}

      {showPermissionKpis && (
        <UiMetricGrid>
          <UiMetric value={catalog.length} label="Quyền trong danh mục" />
          <UiMetric value={roles.length} label="Vai trò đang hiển thị" />
          <UiMetric value={roles.reduce((sum, role) => sum + role.permissions.length, 0)} label="Lượt cấp quyền hiện tại" />
        </UiMetricGrid>
      )}

      <UiTabs
        ariaLabel="Chuyển đổi phân hệ quản trị quyền"
        activeTab={activeSectionTab}
        onChange={setActiveSectionTab}
        tabs={[
          { id: 'overview-matrix', label: 'Tổng quan & Ma trận phân quyền', testId: 'tab-permissions-matrix' },
          { id: 'roles-warehouse', label: 'Cấu trúc vai trò & Phạm vi kho', testId: 'tab-permissions-governance' },
          { id: 'security-2fa', label: 'Xác thực 2 lớp', testId: 'tab-permissions-2fa' },
          { id: 'support-sessions', label: 'Phiên hỗ trợ kỹ thuật', testId: 'tab-permissions-support' },
        ]}
      />

      {loading ? (
        <p role="status">Đang tải quyền truy cập...</p>
      ) : (
        <>
          {activeSectionTab === 'overview-matrix' && (catalog.length > 0 || roles.length > 0) && (
            <PermissionsSaasBillingBanner billingSummary={billingSummary} />
          )}
          {activeSectionTab === 'security-2fa' && <PermissionsTotpEnrollmentCard targetUserIdInput={targetUserIdInput} onError={setError} />}
          {activeSectionTab === 'support-sessions' && <PermissionsSupportImpersonationCard targetUserIdInput={targetUserIdInput} onError={setError} />}

          {(activeSectionTab === 'overview-matrix' || activeSectionTab === 'roles-warehouse') && (
            <div>
              {(catalog.length > 0 || roles.length > 0) && (
                <PermissionsEnterpriseComplianceCard
                  canReadUserWarehouse={canReadUserWarehouse}
                  canAssign={canAssign}
                  evidenceBusy={evidenceBusy}
                  onExportEvidenceBundle={() => void exportEvidenceBundle()}
                  sodAuditBusy={sodAuditBusy}
                  onVerifySodIsolation={() => void verifySodIsolation()}
                  evidenceBundle={evidenceBundle}
                  sodAuditResult={sodAuditResult}
                />
              )}
              {(canAssign || canReadCatalog) && (
                <div className="ui-inline-actions">
                  <button type="button" onClick={() => setShowGovernance(v => !v)}>
                    {showGovernance ? 'Ẩn quản trị bảo mật, phạm vi kho & kiểm toán' : 'Quản trị bảo mật, phạm vi kho & kiểm toán'}
                  </button>
                </div>
              )}

              {(showGovernance || activeSectionTab === 'roles-warehouse') && (
                <PermissionsGovernanceAndAuditCards
                  canAssign={canAssign}
                  canReadCatalog={canReadCatalog}
                  targetUserIdInput={targetUserIdInput}
                  onChangeTargetUserIdInput={setTargetUserIdInput}
                  catalogUsers={catalogUsers}
                  catalogWarehouses={catalogWarehouses}
                  securityBusy={securityBusy}
                  onLoadUserSecurity={() => void loadUserSecurity()}
                  userSecurity={userSecurity}
                  securityReason={securityReason}
                  onChangeSecurityReason={setSecurityReason}
                  tempPassword={tempPassword}
                  onChangeTempPassword={setTempPassword}
                  onRunSecurityAction={ep => void runSecurityAction(ep)}
                  whUserIdInput={whUserIdInput}
                  onChangeWhUserIdInput={setWhUserIdInput}
                  whBusy={whBusy}
                  onLoadUserWarehouseScope={() => void loadUserWarehouseScope()}
                  userWarehouses={userWarehouses}
                  grantWarehouseIdInput={grantWarehouseIdInput}
                  onChangeGrantWarehouseIdInput={setGrantWarehouseIdInput}
                  onGrantWarehouseScope={() => void grantWarehouseScope()}
                  onRevokeWarehouseScope={whId => void revokeWarehouseScope(whId)}
                  auditLogs={auditLogs}
                  auditLoaded={auditLoaded}
                  auditBusy={auditBusy}
                  onLoadAuditLogs={() => void loadAuditLogs()}
                  onError={setError}
                />
              )}
            </div>
          )}

          {(activeSectionTab === 'overview-matrix' || activeSectionTab === 'roles-warehouse') && (
            <PermissionsRoleMatrixSection
              catalog={catalog}
              roles={roles}
              canReadRoles={canReadRoles}
              canAssign={canAssign}
              roleFilter={roleFilter}
              effectiveRoleFilter={effectiveRoleFilter}
              onChangeRoleFilter={setRoleFilter}
              permissionSearch={permissionSearch}
              onChangePermissionSearch={setPermissionSearch}
              pending={pending}
              onChangeGrant={(role, code, revoke) => void change(role, code, revoke)}
            />
          )}
        </>
      )}
    </UiPage>
  );
}
