export type PortalMode = 'PLATFORM_ADMIN' | 'CUSTOMER_WMS' | 'UNIFIED_DEV';

export const IMPERSONATION_STORAGE_KEY = 'erp_support_impersonation_session';
export const PLATFORM_ROLE_STORAGE_KEY = 'erp_platform_active_role';

export interface ActiveImpersonationState {
  sessionCode?: string;
  impersonationToken?: string;
  tenantId: number | string;
  tenantCode?: string;
  tenantName?: string;
  ticketCode: string;
  reason?: string;
  startedByUsername?: string;
  startedAtUtc?: string;
  readOnlyScope?: boolean;
  readOnlyMode?: boolean;
  expiresAtUtc: string;
  token?: string;
}

let inMemoryImpersonationState: ActiveImpersonationState | null = null;

function purgeLegacyLocalStorageImpersonation(): void {
  if (typeof window === 'undefined' || !window.localStorage) {
    return;
  }
  try {
    window.localStorage.removeItem(IMPERSONATION_STORAGE_KEY);
    window.localStorage.removeItem('erp_active_impersonation_session');
    window.localStorage.removeItem(PLATFORM_ROLE_STORAGE_KEY);
  } catch {
    // Ignore storage access errors
  }
}

export function resolvePortalMode(pathname?: string): PortalMode {
  const effectivePath =
    pathname ?? (typeof window !== 'undefined' ? window.location.pathname : '/');
  if (effectivePath.startsWith('/platform')) {
    return 'PLATFORM_ADMIN';
  }
  const envMode =
    typeof import.meta !== 'undefined' && import.meta.env?.VITE_PORTAL_MODE
      ? String(import.meta.env.VITE_PORTAL_MODE).trim().toUpperCase()
      : '';
  if (envMode === 'PLATFORM_ADMIN') {
    return 'PLATFORM_ADMIN';
  }
  if (envMode === 'CUSTOMER_WMS') {
    return 'CUSTOMER_WMS';
  }
  return 'UNIFIED_DEV';
}

export const CANONICAL_TENANT_DISPLAY_NAME = 'Công ty TNHH Thương mại & Kho vận Đại Phát';

export function normalizeTenantDisplayName(raw?: string | null): string {
  const trimmed = String(raw ?? '').trim();
  if (
    !trimmed ||
    trimmed === 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO' ||
    trimmed === 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)' ||
    /erp_kho/i.test(trimmed)
  ) {
    return CANONICAL_TENANT_DISPLAY_NAME;
  }
  return trimmed;
}

function normalizeImpersonationState(parsed: Partial<ActiveImpersonationState> | null): ActiveImpersonationState | null {
  const resolvedSessionCode = parsed?.sessionCode ?? parsed?.impersonationToken ?? parsed?.token;
  if (!parsed || !resolvedSessionCode || parsed.tenantId === undefined) {
    return null;
  }
  return {
    sessionCode: String(resolvedSessionCode),
    impersonationToken: String(parsed.impersonationToken ?? resolvedSessionCode),
    tenantId: parsed.tenantId,
    tenantCode: parsed.tenantCode ? String(parsed.tenantCode) : `TENANT-${String(parsed.tenantId).padStart(3, '0')}`,
    tenantName: normalizeTenantDisplayName(parsed.tenantName ? String(parsed.tenantName) : undefined),
    ticketCode: String(parsed.ticketCode ?? 'SUP-TICKET'),
    reason: parsed.reason ? String(parsed.reason) : undefined,
    startedByUsername: parsed.startedByUsername ? String(parsed.startedByUsername) : undefined,
    startedAtUtc: parsed.startedAtUtc ? String(parsed.startedAtUtc) : undefined,
    readOnlyScope: parsed.readOnlyScope !== false && parsed.readOnlyMode !== false,
    readOnlyMode: parsed.readOnlyScope !== false && parsed.readOnlyMode !== false,
    expiresAtUtc: String(parsed.expiresAtUtc ?? ''),
    token: String(parsed.token ?? resolvedSessionCode),
  };
}

export function getActiveImpersonationState(): ActiveImpersonationState | null {
  purgeLegacyLocalStorageImpersonation();
  if (typeof window === 'undefined' || !window.sessionStorage) {
    return inMemoryImpersonationState;
  }
  try {
    const raw = window.sessionStorage.getItem(IMPERSONATION_STORAGE_KEY);
    if (!raw) {
      return inMemoryImpersonationState;
    }
    const parsed = JSON.parse(raw) as Partial<ActiveImpersonationState>;
    const normalized = normalizeImpersonationState(parsed);
    inMemoryImpersonationState = normalized;
    return normalized;
  } catch {
    return inMemoryImpersonationState;
  }
}

export function setActiveImpersonationState(state: ActiveImpersonationState | null): void {
  purgeLegacyLocalStorageImpersonation();
  const normalized = state ? normalizeImpersonationState(state) : null;
  inMemoryImpersonationState = normalized;
  if (typeof window === 'undefined') {
    return;
  }
  try {
    if (window.sessionStorage) {
      if (!normalized) {
        window.sessionStorage.removeItem(IMPERSONATION_STORAGE_KEY);
      } else {
        window.sessionStorage.setItem(IMPERSONATION_STORAGE_KEY, JSON.stringify(normalized));
      }
    }
    window.dispatchEvent(new Event('erp-impersonation-changed'));
  } catch {
    // Ignore storage quota errors
  }
}

export function clearActiveImpersonationState(): void {
  setActiveImpersonationState(null);
}
