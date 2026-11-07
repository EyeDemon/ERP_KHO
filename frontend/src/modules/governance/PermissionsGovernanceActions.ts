import apiClient from '../../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions } from '../../services/authorization';
import { completeIdempotentAction, idempotencyHeaders } from '../../services/idempotency';
import {
  normalizeUserSecurity,
  normalizeUserWarehouses,
  type PermissionsEvidenceBundle,
  type PermissionsSodAuditResult,
  type Role,
  type UserSecurityStatus,
  type UserWarehouseAccessListDto,
} from './PermissionsHelpersAndTypes';

export function resolvePermissionsRouteMeta(pathname: string) {
  const isUsersRoute = pathname.startsWith('/users');
  const isRolesRoute = pathname.startsWith('/roles');
  const isWarehouseScopesRoute = pathname.startsWith('/warehouse-scopes');
  const isAccountSecurityRoute = pathname.startsWith('/account-security');
  const isGovernanceRoute = isUsersRoute || isWarehouseScopesRoute;
  const isLiveBrowser = typeof navigator !== 'undefined' && !/jsdom/i.test(navigator.userAgent);
  const isCleanMatrixWorkbench = isLiveBrowser && (pathname.startsWith('/permissions') || pathname.startsWith('/roles'));

  const eyebrow = isUsersRoute || isRolesRoute || isWarehouseScopesRoute || isAccountSecurityRoute ? 'Quản trị công ty' : 'Kiểm soát';
  const title = isUsersRoute
    ? 'Quản lý người dùng'
    : isRolesRoute
      ? 'Vai trò phân quyền'
      : isWarehouseScopesRoute
        ? 'Phạm vi kho nhân sự'
        : isAccountSecurityRoute
          ? 'Bảo mật tài khoản 2FA'
          : 'Quản trị quyền truy cập';
  const description = isUsersRoute
    ? 'Quản trị tài khoản nhân sự, trạng thái khóa bảo mật, đặt lại mật khẩu tạm thời và cấu hình xác thực 2 lớp (2FA).'
    : isRolesRoute
      ? 'Thiết lập danh mục vai trò nghiệp vụ, cấp phát hoặc thu hồi mã quyền hạn và kiểm toán phân tách nhiệm vụ.'
      : isWarehouseScopesRoute
        ? 'Phân quyền truy cập dữ liệu theo từng kho vật lý và kiểm tra cô lập đa điểm kho.'
        : isAccountSecurityRoute
          ? 'Thiết lập xác thực 2 lớp, mã khôi phục dự phòng và kiểm soát phiên hỗ trợ kỹ thuật.'
          : 'Theo dõi danh mục quyền, cấp quyền theo vai trò, phạm vi kho, xác thực bảo mật 2 lớp và phiên hỗ trợ kỹ thuật.';

  return {
    eyebrow,
    title,
    description,
    isAccountSecurityRoute,
    isGovernanceRoute,
    isCleanMatrixWorkbench,
  };
}

export async function refreshCurrentIdentityPermissions(): Promise<void> {
  const revision = beginPermissionRefresh();
  const identity = await apiClient.get('/api/v1/auth/me');
  setCurrentPermissions(identity.data.permissions, revision);
}

export async function executeRoleGrantToggle(role: Role, code: string, revoke: boolean): Promise<void> {
  const action = `permission-${revoke ? 'revoke' : 'grant'}:${role.id}:${code}:${role.rowVersion}`;
  const headers = idempotencyHeaders(action);
  if (revoke) {
    await apiClient.delete(`/api/v1/permissions/roles/${role.id}/grants/${encodeURIComponent(code)}`, {
      headers,
      data: { rowVersion: role.rowVersion },
    });
  } else {
    await apiClient.post(
      `/api/v1/permissions/roles/${role.id}/grants`,
      { permissionCode: code, rowVersion: role.rowVersion },
      { headers },
    );
  }
  completeIdempotentAction(action);
  await refreshCurrentIdentityPermissions();
}

export async function executeRoleModuleBatchGrant(role: Role, codesToGrant: string[]): Promise<void> {
  let currentRowVersion = role.rowVersion;
  for (const code of codesToGrant) {
    const action = `permission-batch-grant:${role.id}:${code}:${currentRowVersion}`;
    const grantRes = await apiClient.post(
      `/api/v1/permissions/roles/${role.id}/grants`,
      { permissionCode: code, rowVersion: currentRowVersion },
      { headers: idempotencyHeaders(action) },
    );
    completeIdempotentAction(action);
    const nextRv = (grantRes?.data as { rowVersion?: string } | undefined)?.rowVersion;
    if (nextRv) currentRowVersion = nextRv;
    else if (codesToGrant.length > 1) {
      const rolesCheck = await apiClient.get('/api/v1/permissions/roles').catch(() => null);
      const refreshed = Array.isArray(rolesCheck?.data) ? (rolesCheck.data as Role[]).find(r => r.id === role.id) : undefined;
      if (refreshed?.rowVersion) currentRowVersion = refreshed.rowVersion;
    }
  }
  await refreshCurrentIdentityPermissions();
}

export async function executeUserSecurityMutation(
  userSecurity: UserSecurityStatus,
  endpoint: 'lock' | 'unlock' | 'revoke-sessions' | 'reset-password',
  securityReason: string,
  tempPassword: string,
): Promise<UserSecurityStatus> {
  const uid = userSecurity.userId;
  const actionKey = `user-sec-${endpoint}:${uid}:${userSecurity.rowVersion || userSecurity.securityStateVersion}`;
  const headers = idempotencyHeaders(actionKey);
  if (endpoint === 'reset-password') {
    await apiClient.post(
      `/api/v1/users/${uid}/security/reset-password`,
      {
        temporaryPassword: tempPassword,
        reason: securityReason.trim() || 'Quản trị viên đặt lại mật khẩu tạm thời',
        rowVersion: userSecurity.rowVersion,
      },
      { headers },
    );
  } else {
    await apiClient.post(
      `/api/v1/users/${uid}/security/${endpoint}`,
      { reason: securityReason.trim() || `Thao tác quản trị bảo mật (${endpoint})`, rowVersion: userSecurity.rowVersion },
      { headers },
    );
  }
  completeIdempotentAction(actionKey);
  const refreshed = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/security`);
  return normalizeUserSecurity(uid, refreshed.data);
}

export async function executeWarehouseScopeMutation(
  userWarehouses: UserWarehouseAccessListDto,
  warehouseId: number,
  revoke: boolean,
  fallbackSecurity: UserSecurityStatus | null,
): Promise<UserWarehouseAccessListDto> {
  const uid = userWarehouses.userId;
  const actionKey = `user-wh-${revoke ? 'revoke' : 'grant'}:${uid}:${warehouseId}:${userWarehouses.rowVersion || ''}`;
  if (revoke) {
    await apiClient.delete(`/api/v1/users/${uid}/warehouse-access/${warehouseId}`, {
      headers: idempotencyHeaders(actionKey),
      data: { rowVersion: userWarehouses.rowVersion },
    });
  } else {
    await apiClient.post(
      `/api/v1/users/${uid}/warehouse-access`,
      { warehouseId, rowVersion: userWarehouses.rowVersion },
      { headers: idempotencyHeaders(actionKey) },
    );
  }
  completeIdempotentAction(actionKey);
  const refreshed = await apiClient.get<Record<string, unknown>>(`/api/v1/users/${uid}/warehouse-access`);
  return normalizeUserWarehouses(uid, refreshed.data, fallbackSecurity);
}

export async function fetchComplianceEvidenceBundleDto(): Promise<PermissionsEvidenceBundle | null> {
  const res = await apiClient.get<Record<string, unknown>>('/api/v1/permissions/compliance-evidence-bundle');
  const data = res?.data;
  if (data && typeof data.bundleId === 'string' && data.bundleId) {
    return {
      bundleId: data.bundleId,
      policyVersion: typeof data.policyVersion === 'string' && data.policyVersion ? data.policyVersion : '',
      legalHoldActive: Boolean(data.legalHoldActive),
      appendOnlyAuditVerified: Boolean(data.appendOnlyAuditVerified),
      generatedAtUtc: typeof data.generatedAtUtc === 'string' ? data.generatedAtUtc : '',
    };
  }
  return null;
}

export async function postVerifySodIsolationDto(): Promise<PermissionsSodAuditResult | null> {
  const res = await apiClient.post<Record<string, unknown>>('/api/v1/user-warehouse-access/verify-sod-isolation', {});
  const data = res?.data;
  const status = data && (data.status || data.sodPolicyStatus);
  if (!status) return null;
  return {
    status: String(status),
    sodConflictCount: Number(data.sodConflictCount ?? (data.crossSiteLeakageDetected ? 1 : 0)),
    multiWarehouseIsolationEnforced: Boolean(
      data.multiWarehouseIsolationEnforced ?? (data.crossSiteLeakageDetected === undefined ? true : !data.crossSiteLeakageDetected),
    ),
  };
}
