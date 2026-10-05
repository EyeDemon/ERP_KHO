import { useSyncExternalStore } from 'react';

export type AppRole = 'Admin' | 'Manager' | 'WarehouseStaff' | 'Viewer';
const storageKey = 'permissions';
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
export const usePermissionSet = () => useSyncExternalStore(subscribe, () => localStorage.getItem(storageKey) || '[]');
export const usePermission = (code: string) => {
  usePermissionSet();
  return hasPermission(code);
};
export const currentPermissions = (): string[] => {
  try { const value=JSON.parse(localStorage.getItem(storageKey) || '[]'); return Array.isArray(value) ? value.filter(x=>typeof x==='string') : []; }
  catch { return []; }
};
export const hasPermission = (code: string) => currentPermissions().includes(code);

export const currentRole = (): AppRole => {
  const role = localStorage.getItem('role');
  return role === 'Admin' || role === 'Manager' || role === 'WarehouseStaff' || role === 'Viewer'
    ? role
    : 'Viewer';
};

export const currentUserId = (): number | null => {
  const value = Number(localStorage.getItem('userId'));
  return Number.isInteger(value) && value > 0 ? value : null;
};

// Stocktake remains outside the inbound permission cutover.
export const canViewStocktakes = (role: AppRole = currentRole()) => role !== 'Viewer';
// Inbound reads use grants; other approval document types retain their compatibility role gate.
export const canViewApprovals = () => hasPermission('receipt.read') || hasPermission('export_receipt.read') || currentRole() === 'Admin' || currentRole() === 'Manager';
