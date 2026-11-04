// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import {
  CANONICAL_APP_ROLES,
  PLATFORM_APP_ROLES,
  beginPermissionRefresh,
  canReadInventoryCost,
  canViewApprovals,
  canViewStocktakes,
  currentPermissions,
  currentRole,
  currentUserId,
  hasPermission,
  isPlatformRole,
  setCurrentPermissions,
  usePermission,
} from './authorization';
import { isPlatformRoleName, roleChipLabel, roleLabel } from './permissionPresentation';

describe('frontend authorization matrix & 10-role RBAC helpers', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('ignores late identity responses after a newer refresh or access clearing', () => {
    const oldRequest = beginPermissionRefresh();
    const authoritativeRequest = beginPermissionRefresh();
    setCurrentPermissions(['receipt.read'], oldRequest);
    expect(hasPermission('receipt.read')).toBe(false);
    setCurrentPermissions([], authoritativeRequest);
    setCurrentPermissions(['receipt.read'], oldRequest);
    expect(hasPermission('receipt.read')).toBe(false);
    const pendingRequest = beginPermissionRefresh();
    setCurrentPermissions([]);
    setCurrentPermissions(['permission.assign'], pendingRequest);
    expect(hasPermission('permission.assign')).toBe(false);
    const freshRequest = beginPermissionRefresh();
    setCurrentPermissions(['receipt.read'], freshRequest);
    expect(hasPermission('receipt.read')).toBe(true);
  });

  it('uses explicit permission codes instead of role names and fails closed on canReadInventoryCost', () => {
    for (const highRole of ['SuperAdmin', 'Admin', 'Manager', 'Accountant']) {
      localStorage.setItem('role', highRole);
      setCurrentPermissions(['inventory.read']);
      expect(hasPermission('location.manage')).toBe(false);
      expect(canReadInventoryCost()).toBe(false);
    }

    setCurrentPermissions([
      'inventory.cost.read',
      'location.manage',
      'product.update',
      'putaway.execute',
      'receipt.complete',
      'receipt.update',
    ]);
    expect(canReadInventoryCost()).toBe(true);
    expect(hasPermission('location.manage')).toBe(true);
    expect(hasPermission('product.update')).toBe(true);
    expect(hasPermission('putaway.execute')).toBe(true);
    expect(hasPermission('receipt.complete')).toBe(true);
  });

  it('recognizes all 10 canonical roles, separates PLATFORM vs CUSTOMER_TENANT roles, and localizes labels in Vietnamese', () => {
    expect(CANONICAL_APP_ROLES).toHaveLength(10);
    expect(PLATFORM_APP_ROLES).toEqual(['SuperAdmin', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor']);

    for (const role of CANONICAL_APP_ROLES) {
      localStorage.setItem('role', role);
      expect(currentRole()).toBe(role);
      const expectedPlatform = ['SuperAdmin', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor'].includes(role);
      expect(isPlatformRole()).toBe(expectedPlatform);
      expect(isPlatformRole(role)).toBe(expectedPlatform);
      expect(isPlatformRoleName(role)).toBe(expectedPlatform);

      expect(roleLabel(role)).not.toContain(`Vai trò ${role}`);
      expect(roleChipLabel(role, 10)).toContain(role);
    }

    expect(roleChipLabel('Admin', 141)).toBe('Quản trị viên công ty (Admin · 141)');
    expect(roleChipLabel('Manager', 98)).toBe('Quản lý kho (Manager · 98)');
    expect(roleChipLabel('Supervisor', 54)).toBe('Giám sát & QC (Supervisor · 54)');
    expect(roleChipLabel('Accountant', 42)).toBe('Kế toán kho (Accountant · 42)');
    expect(roleChipLabel('WarehouseStaff', 36)).toBe('Nhân viên kho (WarehouseStaff · 36)');
    expect(roleChipLabel('Viewer', 18)).toBe('Chỉ xem / Kiểm toán (Viewer · 18)');

    localStorage.setItem('role', 'UnknownBogusRole');
    expect(currentRole()).toBe('Viewer');
    expect(isPlatformRole()).toBe(false);
  });

  it('gates canViewStocktakes safely across Viewer and non-SuperAdmin platform sub-roles', () => {
    setCurrentPermissions([]);
    for (const deniedRole of ['Viewer', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor']) {
      localStorage.setItem('role', deniedRole);
      expect(canViewStocktakes()).toBe(false);
    }

    for (const allowedRole of ['SuperAdmin', 'Admin', 'Manager', 'Supervisor', 'Accountant', 'WarehouseStaff']) {
      localStorage.setItem('role', allowedRole);
      expect(canViewStocktakes()).toBe(true);
    }
  });

  it('deduplicates permissions and fails closed for malformed storage', () => {
    setCurrentPermissions(['putaway.read', 'putaway.read', 'receipt.read']);
    expect(currentPermissions()).toEqual(['putaway.read', 'receipt.read']);
    expect(hasPermission('putaway.read')).toBe(true);
    localStorage.setItem('permissions', '{bad json');
    expect(currentPermissions()).toEqual([]);
    expect(hasPermission('putaway.read')).toBe(false);
  });

  it('updates mounted action visibility when effective permissions change', () => {
    const view = renderHook(() => usePermission('putaway.execute'));
    expect(view.result.current).toBe(false);
    act(() => setCurrentPermissions(['putaway.execute']));
    expect(view.result.current).toBe(true);
    act(() => setCurrentPermissions([]));
    expect(view.result.current).toBe(false);
    view.unmount();
  });

  it('allows Approval Center visibility from outbound read or SuperAdmin/Admin/Manager', () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    expect(canViewApprovals()).toBe(true);
    setCurrentPermissions([]);
    expect(canViewApprovals()).toBe(false);

    localStorage.setItem('role', 'SuperAdmin');
    expect(canViewApprovals()).toBe(true);
  });

  it('reads only a valid positive integer user id from session metadata', () => {
    expect(currentUserId()).toBeNull();
    localStorage.setItem('userId', '17');
    expect(currentUserId()).toBe(17);
    localStorage.setItem('userId', '17.5');
    expect(currentUserId()).toBeNull();
  });
});
