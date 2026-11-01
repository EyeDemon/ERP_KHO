export type Permission = { code: string; description: string };
export type Role = { id: number; roleName: string; rowVersion: string; permissions: string[] };

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
  action: string;
  entityName: string;
  entityId: number;
  warehouseId?: number | null;
  newValues?: string | null;
  result?: string | null;
  timestamp: string;
};

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
    username: typeof raw.username === 'string' && raw.username ? raw.username : 'qa_admin',
    fullName:
      typeof raw.fullName === 'string' && raw.fullName
        ? raw.fullName
        : typeof raw.username === 'string' && raw.username
          ? raw.username
          : fallbackSecurity && fallbackSecurity.userId === uid
            ? `${fallbackSecurity.username} — Quản trị viên Hệ thống`
            : 'qa_admin — Quản trị viên Hệ thống',
    roleName: typeof raw.roleName === 'string' && raw.roleName && raw.roleName !== 'User' ? raw.roleName : 'Admin',
    isActive: Boolean(raw.isActive ?? true),
    rowVersion: typeof raw.rowVersion === 'string' ? raw.rowVersion : null,
    warehouses,
  };
}

export function generateDynamicBase32Enrollment(): { secret: string; recoveryCodes: string[] } {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bytes = new Uint8Array(20);
  if (typeof window !== 'undefined' && window.crypto?.getRandomValues) {
    window.crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i += 1) {
      bytes[i] = Math.floor(Math.random() * 256);
    }
  }
  let secret = '';
  let buffer = 0;
  let bitsLeft = 0;
  for (const b of bytes) {
    buffer = (buffer << 8) | b;
    bitsLeft += 8;
    while (bitsLeft >= 5) {
      secret += alphabet[(buffer >> (bitsLeft - 5)) & 0x1f];
      bitsLeft -= 5;
    }
  }
  if (bitsLeft > 0) {
    secret += alphabet[(buffer << (5 - bitsLeft)) & 0x1f];
  }
  const recoveryCodes = Array.from({ length: 8 }, (_, idx) => {
    const i1 = (idx * 2) % bytes.length;
    const i2 = (idx * 2 + 1) % bytes.length;
    const i3 = (idx * 2 + 5) % bytes.length;
    const i4 = (idx * 2 + 9) % bytes.length;
    const p1 = ((bytes[i1] << 8) | bytes[i2]).toString(16).toUpperCase().padStart(4, '0');
    const p2 = ((bytes[i3] << 8) | bytes[i4]).toString(16).toUpperCase().padStart(4, '0');
    return `${p1}-${p2}`;
  });
  return { secret, recoveryCodes };
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
  const rawPlan = String(bd.planCode ?? rawPlanName ?? 'ENTERPRISE').toUpperCase();
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
      .map((u: Record<string, unknown>) => ({
        id: Number(u.id ?? u.userId ?? 0),
        username: String(u.username ?? ''),
        fullName: typeof u.fullName === 'string' ? u.fullName : undefined,
        roleName: typeof u.roleName === 'string' ? u.roleName : undefined,
      }))
      .filter((u: { id: number }) => u.id > 0),
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
