import { useSyncExternalStore } from 'react';

export const CANONICAL_APP_ROLES = [
  'SuperAdmin',
  'BillingAdmin',
  'SupportAdmin',
  'SecurityAuditor',
  'Admin',
  'Manager',
  'Supervisor',
  'Accountant',
  'WarehouseStaff',
  'Viewer',
] as const;

export const PLATFORM_APP_ROLES = [
  'SuperAdmin',
  'BillingAdmin',
  'SupportAdmin',
  'SecurityAuditor',
] as const;

export type CanonicalAppRole = (typeof CANONICAL_APP_ROLES)[number];
export type AppRole = CanonicalAppRole | 'Staff';

const storageKey = 'permissions';
const validRoleSet = new Set<string>([...CANONICAL_APP_ROLES, 'Staff']);
const platformRoleSet = new Set<string>(PLATFORM_APP_ROLES);

let revision = 0;

export const beginPermissionRefresh = () => ++revision;

export const setCurrentPermissions = (permissions: string[], expectedRevision?: number) => {
  if (expectedRevision !== undefined && expectedRevision !== revision) return;
  revision++;
  localStorage.setItem(storageKey, JSON.stringify([...new Set(permissions)].sort()));
  window.dispatchEvent(new Event('permissions-changed'));
};

const subscribe = (changed: () => void) => {
  window.addEventListener('permissions-changed', changed);
  return () => window.removeEventListener('permissions-changed', changed);
};

export const usePermissionSet = () =>
  useSyncExternalStore(subscribe, () => localStorage.getItem(storageKey) || '[]');

export const currentPermissions = (): string[] => {
  try {
    const value = JSON.parse(localStorage.getItem(storageKey) || '[]');
    return Array.isArray(value) ? value.filter((x): x is string => typeof x === 'string') : [];
  } catch {
    return [];
  }
};

export const hasPermission = (code: string): boolean => {
  if (!code) return false;
  return currentPermissions().includes(code);
};

export const usePermission = (code: string): boolean => {
  usePermissionSet();
  return hasPermission(code);
};

export const canReadInventoryCost = (): boolean => hasPermission('inventory.cost.read');

export const isPlatformRole = (role: string | null | undefined = currentRole()): boolean =>
  Boolean(role && platformRoleSet.has(role));

export const currentRole = (): AppRole => {
  const role = localStorage.getItem('role');
  return role && validRoleSet.has(role) ? (role as AppRole) : 'Viewer';
};

export const currentUserId = (): number | null => {
  const value = Number(localStorage.getItem('userId'));
  return Number.isInteger(value) && value > 0 ? value : null;
};

// Stocktake checks explicit read grants first, then compatibility role gate (excluding Viewer & non-SuperAdmin platform roles).
export const canViewStocktakes = (role: AppRole | string = currentRole()): boolean =>
  hasPermission('stocktake.read') ||
  hasPermission('inventory.read') ||
  (Boolean(role) &&
    role !== 'Viewer' &&
    role !== 'BillingAdmin' &&
    role !== 'SupportAdmin' &&
    role !== 'SecurityAuditor');

// Inbound/outbound reads use grants; SuperAdmin/Admin/Manager retain compatibility approval gate.
export const canViewApprovals = (): boolean => {
  const role = currentRole();
  return (
    hasPermission('receipt.read') ||
    hasPermission('export_receipt.read') ||
    role === 'SuperAdmin' ||
    role === 'Admin' ||
    role === 'Manager'
  );
};
