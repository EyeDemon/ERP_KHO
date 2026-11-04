import { useCallback, useEffect, useRef, useState } from 'react';
import { useInRouterContext, useLocation } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { permissionError, permissionLabel, roleLabel } from '../services/permissionPresentation';
import { UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTabs } from '../ui/ProductionUi';
import {
  executeRoleGrantToggle,
  executeRoleModuleBatchGrant,
  executeUserSecurityMutation,
  executeWarehouseScopeMutation,
  fetchComplianceEvidenceBundleDto,
  postVerifySodIsolationDto,
  resolvePermissionsRouteMeta,
} from '../modules/governance/PermissionsGovernanceActions';
import { PermissionsGovernanceAndAuditCards } from '../modules/governance/PermissionsGovernanceAndAuditCards';
import {
  filterCustomerTenantCatalog,
  normalizeBillingSummary,
  normalizeUserSecurity,
  normalizeUserWarehouses,
  parsePermissionsCatalogPayload,
  sanitizeAndSortCustomerRoles,
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
import { formatWarehouseLabel } from '../utils/warehouseLabel';

export default function Permissions() {
  const inRouter = useInRouterContext();
  const location = inRouter ? useLocation() : null;
  const pathname = location?.pathname ?? (typeof window !== 'undefined' ? window.location.pathname : '/permissions');
  const { eyebrow, title, description, isAccountSecurityRoute, isGovernanceRoute, isCleanMatrixWorkbench } =
    resolvePermissionsRouteMeta(pathname);

  const canReadCatalog = usePermission('permission.read');
  const canReadRoles = usePermission('role.read');
  const canAssign = usePermission('permission.assign');
  const canReadUserWarehouse = usePermission('user_warehouse.read');
  const canReadUsers = usePermission('user.read');
  const canReadWarehouses = usePermission('warehouse.read');
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
  const [showGovernance, setShowGovernance] = useState(() => isGovernanceRoute);
  const [evidenceBundle, setEvidenceBundle] = useState<PermissionsEvidenceBundle | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState(false);
  const [sodAuditResult, setSodAuditResult] = useState<PermissionsSodAuditResult | null>(null);
  const [sodAuditBusy, setSodAuditBusy] = useState(false);
  const [catalogUsers, setCatalogUsers] = useState<PermissionsCatalogUser[]>([]);
  const [catalogWarehouses, setCatalogWarehouses] = useState<PermissionsCatalogWarehouse[]>([]);
  const [activeSectionTab, setActiveSectionTab] = useState<'overview-matrix' | 'roles-warehouse' | 'security-2fa' | 'support-sessions'>(() =>
    isAccountSecurityRoute ? 'security-2fa' : isGovernanceRoute ? 'roles-warehouse' : 'overview-matrix',
  );
  useEffect(() => {
    setActiveSectionTab(isAccountSecurityRoute ? 'security-2fa' : isGovernanceRoute ? 'roles-warehouse' : 'overview-matrix');
    setShowGovernance(isGovernanceRoute);
  }, [isAccountSecurityRoute, isGovernanceRoute]);
  const [billingSummary, setBillingSummary] = useState<PermissionsBillingSummary | null>(null);

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
        setCatalog(filterCustomerTenantCatalog(Array.isArray(permissions.data) ? (permissions.data as Permission[]) : []));
        const loadedRoles = sanitizeAndSortCustomerRoles(Array.isArray(roleGrants.data) ? (roleGrants.data as Role[]) : []);
        setRoles(loadedRoles);
        setRoleFilter(current => (current && loadedRoles.some(r => r.roleName === current) ? current : loadedRoles[0]?.roleName || ''));
        if (billingRes?.data && !Array.isArray(billingRes.data) && typeof billingRes.data === 'object') {
          setBillingSummary(normalizeBillingSummary(billingRes.data as Record<string, unknown>));
        }
        const [usersRes, whRes] = await Promise.all([
          canReadUsers
            ? apiClient.get('/api/v1/permissions/users').catch(() => ({ data: [] }))
            : Promise.resolve({ data: [] }),
          canReadWarehouses
            ? apiClient.get('/api/v1/warehouses').catch(() => ({ data: [] }))
            : Promise.resolve({ data: [] }),
        ]);
        const parsedCatalog = parsePermissionsCatalogPayload(usersRes.data, whRes.data);
        setCatalogUsers(parsedCatalog.users);
        setCatalogWarehouses(parsedCatalog.warehouses.map(w => ({ ...w, name: formatWarehouseLabel(w.name, w.code, w.id) })));
      }
    } catch (failure) {
      if (version === requestVersion.current) setError(permissionError(failure));
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, [canReadCatalog, canReadRoles, canReadUsers, canReadWarehouses]);

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
    inFlight.current = true; setPending(true); setError(''); setSuccess('');
    try {
      await executeRoleGrantToggle(role, code, revoke);
      await load();
      setSuccess('Đã cập nhật quyền truy cập.');
    } catch (failure) {
      setRoles([]); setCatalog([]); setError(permissionError(failure));
    } finally { inFlight.current = false; setPending(false); }
  };

  const batchGrantModule = async (role: Role, moduleName: string, codesToGrant: string[]) => {
    if (!canAssign || inFlight.current || codesToGrant.length === 0) return;
    if (!window.confirm(`Cấp đồng loạt ${codesToGrant.length} quyền chưa cấp thuộc phân hệ “${moduleName}” cho ${roleLabel(role.roleName)}?`)) return;
    inFlight.current = true; setPending(true); setError(''); setSuccess('');
    try {
      await executeRoleModuleBatchGrant(role, codesToGrant);
      await load();
      setSuccess(`Đã cấp đồng loạt ${codesToGrant.length} quyền thuộc phân hệ “${moduleName}”.`);
    } catch (failure) {
      setRoles([]); setCatalog([]); setError(permissionError(failure));
    } finally { inFlight.current = false; setPending(false); }
  };

  const loadUserSecurity = async () => {
    const uid = Number(targetUserIdInput);
    if (!Number.isInteger(uid) || uid <= 0 || securityBusy) return;
    setSecurityBusy(true); setError(''); setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/security`);
      setUserSecurity(normalizeUserSecurity(uid, res.data));
    } catch (failure) { setUserSecurity(null); setError(permissionError(failure)); }
    finally { setSecurityBusy(false); }
  };

  const runSecurityAction = async (endpoint: 'lock' | 'unlock' | 'revoke-sessions' | 'reset-password') => {
    if (!userSecurity || securityBusy) return;
    setSecurityBusy(true); setError(''); setSuccess('');
    try {
      const updated = await executeUserSecurityMutation(userSecurity, endpoint, securityReason, tempPassword);
      if (endpoint === 'reset-password') setTempPassword('');
      setUserSecurity(updated);
      setSuccess('Đã thực thi thao tác bảo mật tài khoản.');
    } catch (failure) { setError(permissionError(failure)); }
    finally { setSecurityBusy(false); }
  };

  const loadUserWarehouseScope = async () => {
    const uid = Number(whUserIdInput);
    if (!Number.isInteger(uid) || uid <= 0 || whBusy) return;
    setWhBusy(true); setError(''); setSuccess('');
    try {
      const res = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/warehouse-access`);
      setUserWarehouses(normalizeUserWarehouses(uid, res.data, userSecurity));
    } catch (failure) { setUserWarehouses(null); setError(permissionError(failure)); }
    finally { setWhBusy(false); }
  };

  const grantWarehouseScope = async () => {
    if (!userWarehouses || whBusy) return;
    const warehouseId = Number(grantWarehouseIdInput);
    if (!Number.isInteger(warehouseId) || warehouseId <= 0) return;
    setWhBusy(true); setError(''); setSuccess('');
    try {
      const updated = await executeWarehouseScopeMutation(userWarehouses, warehouseId, false, userSecurity);
      setGrantWarehouseIdInput('');
      setUserWarehouses(updated);
      setSuccess('Đã cấp quyền truy cập kho cho người dùng.');
    } catch (failure) { setError(permissionError(failure)); }
    finally { setWhBusy(false); }
  };

  const revokeWarehouseScope = async (warehouseId: number) => {
    if (!userWarehouses || whBusy) return;
    setWhBusy(true); setError(''); setSuccess('');
    try {
      const updated = await executeWarehouseScopeMutation(userWarehouses, warehouseId, true, userSecurity);
      setUserWarehouses(updated);
      setSuccess('Đã thu hồi quyền truy cập kho của người dùng.');
    } catch (failure) { setError(permissionError(failure)); }
    finally { setWhBusy(false); }
  };

  const loadAuditLogs = async () => {
    if (auditBusy) return;
    setAuditBusy(true); setError('');
    try {
      const res = await apiClient.get<PermissionAuditLog[]>('/api/v1/permissions/audit-logs');
      setAuditLogs(res.data); setAuditLoaded(true);
    } catch (failure) { setError(permissionError(failure)); }
    finally { setAuditBusy(false); }
  };

  const exportEvidenceBundle = async () => {
    if (evidenceBusy) return;
    setEvidenceBusy(true); setError('');
    try {
      const bundle = await fetchComplianceEvidenceBundleDto();
      setEvidenceBundle(bundle);
      if (!bundle) setError('Phản hồi gói bằng chứng kiểm toán không hợp lệ từ máy chủ.');
    } catch (failure) { setEvidenceBundle(null); setError(permissionError(failure)); }
    finally { setEvidenceBusy(false); }
  };

  const verifySodIsolation = async () => {
    if (sodAuditBusy) return;
    setSodAuditBusy(true); setError('');
    try {
      const sod = await postVerifySodIsolationDto();
      setSodAuditResult(sod);
      if (!sod) setError('Phản hồi kiểm toán SoD không hợp lệ từ máy chủ.');
    } catch (failure) { setSodAuditResult(null); setError(permissionError(failure)); }
    finally { setSodAuditBusy(false); }
  };

  const effectiveRoleFilter = roleFilter === '__ALL__' ? '' : roleFilter || roles[0]?.roleName || '';
  const showPermissionKpis = activeSectionTab === 'overview-matrix' || activeSectionTab === 'roles-warehouse';

  return (
    <UiPage>
      <UiPageHeader
        eyebrow={eyebrow}
        title={title}
        description={description}
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

      {!isCleanMatrixWorkbench && (
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
      )}

      {loading ? (
        <p role="status">Đang tải quyền truy cập...</p>
      ) : (
        <>
          {!isCleanMatrixWorkbench && billingSummary !== null && activeSectionTab === 'overview-matrix' && (catalog.length > 0 || roles.length > 0) && (
            <PermissionsSaasBillingBanner billingSummary={billingSummary} />
          )}
          {activeSectionTab === 'security-2fa' && <PermissionsTotpEnrollmentCard targetUserIdInput={targetUserIdInput} onError={setError} />}
          {activeSectionTab === 'support-sessions' && <PermissionsSupportImpersonationCard targetUserIdInput={targetUserIdInput} onError={setError} />}

          {(activeSectionTab === 'overview-matrix' || activeSectionTab === 'roles-warehouse') && (
            <div>
              {!isCleanMatrixWorkbench && (catalog.length > 0 || roles.length > 0) && (
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
              {!isCleanMatrixWorkbench && (canAssign || canReadCatalog) && (
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
              effectiveRoleFilter={effectiveRoleFilter}
              onChangeRoleFilter={setRoleFilter}
              permissionSearch={permissionSearch}
              onChangePermissionSearch={setPermissionSearch}
              pending={pending}
              onChangeGrant={(role, code, revoke) => void change(role, code, revoke)}
              onBatchGrantModule={(role, moduleName, codes) => void batchGrantModule(role, moduleName, codes)}
            />
          )}
        </>
      )}
    </UiPage>
  );
}
