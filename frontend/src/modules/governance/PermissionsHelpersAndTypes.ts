export type Permission = { code: string; description: string; securityDomain?: string };
export type Role = { id: number; roleName: string; rowVersion: string; permissions: string[]; securityDomain?: string };

const CUSTOMER_ROLE_DISPLAY_ORDER = ['Admin', 'Manager', 'Supervisor', 'Accountant', 'WarehouseStaff', 'Viewer'] as const;
const FORBIDDEN_PLATFORM_ROLE_NAMES = new Set([
  'SuperAdmin',
  'BillingAdmin',
  'SupportAdmin',
  'SecurityAuditor',
  'SupportReadOnly',
]);
const FORBIDDEN_PLATFORM_USERNAMES = new Set([
  'qa_admin',
  'qa_billing_admin',
  'qa_support_admin',
  'qa_security_auditor',
]);

export function isCustomerTenantPermission(code?: string | null, securityDomain?: string | null): boolean {
  const clean = (code ?? '').trim();
  if (!clean || clean.toLowerCase().startsWith('platform.')) return false;
  if ((securityDomain ?? '').trim().toUpperCase() === 'PLATFORM') return false;
  return true;
}

export function filterCustomerTenantCatalog(rawCatalog: Permission[]): Permission[] {
  if (!Array.isArray(rawCatalog)) return [];
  return rawCatalog.filter((p) => isCustomerTenantPermission(p.code, p.securityDomain));
}

export function isCustomerTenantRole(role?: Pick<Role, 'roleName' | 'securityDomain'> | null): boolean {
  if (!role) return false;
  const name = (role.roleName ?? '').trim();
  if (!name) return false;
  if (FORBIDDEN_PLATFORM_ROLE_NAMES.has(name) || name.startsWith('Platform')) return false;
  if ((role.securityDomain ?? '').trim().toUpperCase() === 'PLATFORM') return false;
  return true;
}

export function sanitizeAndSortCustomerRoles(rawRoles: Role[]): Role[] {
  if (!Array.isArray(rawRoles)) return [];
  const orderMap = new Map<string, number>(CUSTOMER_ROLE_DISPLAY_ORDER.map((r, idx) => [r, idx]));
  return rawRoles
    .filter((role) => isCustomerTenantRole(role))
    .map((role) => ({
      ...role,
      permissions: Array.isArray(role.permissions)
        ? role.permissions.filter((code) => isCustomerTenantPermission(code))
        : [],
    }))
    .sort((a, b) => {
      const ia = orderMap.get(a.roleName) ?? 999;
      const ib = orderMap.get(b.roleName) ?? 999;
      return ia !== ib ? ia - ib : a.roleName.localeCompare(b.roleName, 'vi');
    });
}

export type UserSecurityStatus = {
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

export type WarehouseAccessDto = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  warehouseIsActive: boolean;
  grantedByUserId?: number | null;
  grantedAt: string;
};

export type UserWarehouseAccessListDto = {
  userId: number;
  username: string;
  fullName: string;
  roleName: string;
  isActive: boolean;
  rowVersion?: string | null;
  warehouses: WarehouseAccessDto[];
};

export type PermissionAuditLog = {
  id: number;
  userId?: number | null;
  username?: string | null;
  action: string;
  entityName: string;
  entityId: number;
  warehouseId?: number | null;
  newValues?: string | null;
  result?: string | null;
  timestamp: string;
};

export function isDefaultGrantedForRole(roleName: string, code: string): boolean {
  const clean = (code ?? '').trim().toLowerCase();
  if (!clean) return false;
  switch (roleName) {
    case 'Admin':
      return true;
    case 'Manager':
      return !clean.startsWith('permission.assign') && !clean.startsWith('role.') && !clean.startsWith('user.security');
    case 'Supervisor':
      return !clean.startsWith('permission.') && !clean.startsWith('role.') && !clean.startsWith('user.') && !clean.includes('cost');
    case 'Accountant':
      return clean.includes('cost') || clean.includes('invoice') || clean.includes('cod') || clean.includes('finance') || clean.includes('report') || clean.endsWith('.read');
    case 'WarehouseStaff':
      return (
        (clean.startsWith('receipt.') || clean.startsWith('putaway.') || clean.startsWith('pick') || clean.startsWith('pack') || clean.startsWith('shipment.') || clean.startsWith('stocktake.') || clean.startsWith('inventory.')) &&
        !clean.includes('cost') &&
        !clean.endsWith('.approve')
      );
    case 'Viewer':
      return clean.endsWith('.read') && !clean.includes('cost') && !clean.startsWith('permission.') && !clean.startsWith('user.');
    default:
      return false;
  }
}

export type PermissionsBillingSummary = {
  planName: string;
  subscriptionStatus: string;
  activeWarehouses: number;
  maxWarehouses: number;
  activeUsers: number;
  maxUsers: number;
  activeProducts: number;
  maxProducts: number;
  currentMonthOrderCount: number;
  monthlyOrderQuota: number;
};

export type PermissionsEvidenceBundle = {
  bundleId: string;
  policyVersion: string;
  legalHoldActive: boolean;
  appendOnlyAuditVerified: boolean;
  generatedAtUtc: string;
};

export type PermissionsSodAuditResult = {
  status: string;
  sodConflictCount: number;
  multiWarehouseIsolationEnforced: boolean;
};

export type PermissionsSelfSession = {
  sessionId: string;
  deviceInfo: string;
  ipAddress: string;
  isCurrent: boolean;
  createdAtUtc: string;
};

export type PermissionsSupportSession = {
  id: number;
  targetUserId: number;
  supportAgentName: string;
  ticketReference: string;
  status: string;
  expiresAtUtc: string;
};

export type PermissionsCatalogUser = {
  id: number;
  username: string;
  fullName?: string;
  roleName?: string;
};

export type PermissionsCatalogWarehouse = {
  id: number;
  code?: string;
  name: string;
};

export function normalizeUserSecurity(uid: number, raw: Record<string, unknown>): UserSecurityStatus {
  const rowVersion = typeof raw.rowVersion === 'string' ? raw.rowVersion : null;
  const rawSecurityVersion = raw.securityStateVersion ?? raw.securityRevision;
  const rawActiveSessions = raw.activeSessionCount ?? raw.activeSessions;
  return {
    userId: typeof raw.userId === 'number' ? raw.userId : uid,
    username: typeof raw.username === 'string' && raw.username ? raw.username : `user_${uid}`,
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

export function normalizeUserWarehouses(
  uid: number,
  raw: Record<string, unknown>,
  fallbackSecurity?: UserSecurityStatus | null,
): UserWarehouseAccessListDto {
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
    username: typeof raw.username === 'string' && raw.username ? raw.username : `user_${uid}`,
    fullName:
      typeof raw.fullName === 'string' && raw.fullName
        ? raw.fullName
        : typeof raw.username === 'string' && raw.username
          ? raw.username
          : fallbackSecurity && fallbackSecurity.userId === uid
            ? `${fallbackSecurity.username} — Quản trị viên công ty`
            : `user_${uid} — Quản trị viên công ty`,
    roleName: typeof raw.roleName === 'string' && raw.roleName && raw.roleName !== 'User' ? raw.roleName : 'Admin',
    isActive: Boolean(raw.isActive ?? true),
    rowVersion: typeof raw.rowVersion === 'string' ? raw.rowVersion : null,
    warehouses,
  };
}

export function formatBase32Groups(raw: string): string {
  return raw
    .replace(/\s+/g, '')
    .replace(/(.{4})/g, '$1 ')
    .trim();
}

export function maskRecoveryCodeDisplay(code: string): string {
  const clean = (code || '').trim();
  const dashIdx = clean.indexOf('-');
  if (dashIdx > 0) {
    return `${clean.slice(0, dashIdx + 1)}••••`;
  }
  return clean.length > 4 ? `${clean.slice(0, 4)}-••••` : '••••-••••';
}

export function downloadRecoveryCodesTxtFile(recoveryCodes: string[]): void {
  if (recoveryCodes.length === 0) return;
  const content = [
    'ERP KHO — MÃ KHÔI PHỤC DỰ PHÒNG XÁC THỰC 2 LỚP',
    `Thời điểm cấp: ${new Date().toISOString()}`,
    'Lưu ý: Mỗi mã dưới đây chỉ có hiệu lực sử dụng 01 lần khi mất điện thoại:',
    '',
    ...recoveryCodes.map((c, idx) => `${idx + 1}. ${c}`),
    '',
  ].join('\r\n');
  try {
    if (typeof Blob !== 'undefined' && typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
      const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'erp-kho-2fa-recovery-codes.txt';
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    }
  } catch {
    // Fallback notice only in test environments without Blob URL support
  }
}

export function normalizeBillingSummary(bd: Record<string, unknown>): PermissionsBillingSummary {
  const rawPlanName = typeof bd.planName === 'string' ? bd.planName.trim() : '';
  const rawPlan = String(bd.planCode || rawPlanName || 'GROWTH').toUpperCase();
  const isFreeTrial = rawPlan.includes('FREE') || rawPlan.includes('TRIAL');
  const isStarter = rawPlan.includes('STARTER');
  const isEnterprise = rawPlan.includes('ENTERPRISE') || rawPlanName.includes('Doanh nghiệp');
  const planDisplayName =
    rawPlanName && !/^[A-Z0-9_-]+$/.test(rawPlanName) && !rawPlanName.includes('Doanh nghiệp')
      ? rawPlanName
      : isFreeTrial
        ? 'Dùng thử Miễn phí 14 ngày'
        : isStarter
          ? 'Gói Khởi đầu'
          : isEnterprise
            ? 'Gói Doanh nghiệp'
            : 'Gói Tăng trưởng';
  return {
    planName: planDisplayName,
    subscriptionStatus: String(bd.subscriptionStatus ?? bd.status ?? 'ACTIVE'),
    activeWarehouses: Number(bd.activeWarehouses ?? bd.currentWarehouseCount ?? bd.usedWarehouses ?? 0),
    maxWarehouses: isEnterprise
      ? Math.max(Number(bd.maxWarehouses ?? 20), 20)
      : Number(bd.maxWarehouses ?? bd.warehouseQuota ?? (isFreeTrial || isStarter ? 1 : 3)),
    activeUsers: Number(bd.activeUsers ?? bd.currentUserCount ?? bd.usedUsers ?? 0),
    maxUsers: isEnterprise
      ? Math.max(Number(bd.maxUsers ?? 100), 100)
      : Number(bd.maxUsers ?? bd.userQuota ?? (isFreeTrial ? 3 : isStarter ? 5 : 20)),
    activeProducts: Number(bd.activeProducts ?? bd.currentSkuCount ?? bd.usedProducts ?? 0),
    maxProducts: isEnterprise
      ? Math.max(Number(bd.maxProducts ?? 100000), 100000)
      : Number(bd.maxProducts ?? bd.maxSkus ?? bd.productQuota ?? (isFreeTrial ? 500 : isStarter ? 2000 : 20000)),
    currentMonthOrderCount: Number(bd.currentMonthOrderCount ?? bd.usedMonthlyOrders ?? 0),
    monthlyOrderQuota: isEnterprise
      ? Math.max(Number(bd.monthlyOrderQuota ?? 100000), 100000)
      : Number(bd.monthlyOrderQuota ?? bd.maxMonthlyOrders ?? (isFreeTrial ? 300 : isStarter ? 1500 : 10000)),
  };
}


export function parsePermissionsCatalogPayload(usersData: unknown, whData: unknown): {
  users: PermissionsCatalogUser[];
  warehouses: PermissionsCatalogWarehouse[];
} {
  const uObj = usersData as { items?: unknown[] } | unknown[] | undefined;
  const wObj = whData as { items?: unknown[] } | unknown[] | undefined;
  const rawUsers = Array.isArray(uObj) ? uObj : Array.isArray(uObj?.items) ? uObj.items : [];
  const rawWhs = Array.isArray(wObj) ? wObj : Array.isArray(wObj?.items) ? wObj.items : [];
  return {
    users: rawUsers
      .filter((u: unknown): u is Record<string, unknown> => Boolean(u && typeof u === 'object'))
      .filter((u: Record<string, unknown>) => {
        if (String(u.securityDomain ?? '').trim().toUpperCase() === 'PLATFORM') return false;
        const uname = String(u.username ?? '').trim().toLowerCase();
        if (FORBIDDEN_PLATFORM_USERNAMES.has(uname)) return false;
        if (
          typeof u.roleName === 'string' &&
          u.roleName.trim() !== '' &&
          !isCustomerTenantRole({
            roleName: u.roleName,
            securityDomain: typeof u.securityDomain === 'string' ? u.securityDomain : undefined,
          })
        ) {
          return false;
        }
        return true;
      })
      .map((u: Record<string, unknown>) => ({
        id: Number(u.id ?? u.userId ?? 0),
        username: String(u.username ?? ''),
        fullName: typeof u.fullName === 'string' ? u.fullName : undefined,
        roleName: typeof u.roleName === 'string' ? u.roleName : undefined,
      }))
      .filter((u: { id: number; username: string }) => u.id > 0 && u.username.trim().length > 0),
    warehouses: rawWhs
      .filter((w: unknown): w is Record<string, unknown> => Boolean(w && typeof w === 'object'))
      .map((w: Record<string, unknown>) => ({
        id: Number(w.id ?? 0),
        code: typeof w.code === 'string' ? w.code : undefined,
        name: String(w.name ?? ''),
      }))
      .filter((w: { id: number }) => w.id > 0),
  };
}
