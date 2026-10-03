// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import {
  currentPermissions,
  currentUserId,
  hasPermission,
  beginPermissionRefresh,
  setCurrentPermissions,
  usePermission,
} from './authorization';

describe('frontend authorization matrix', () => {
  beforeEach(() => localStorage.clear());

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

  it('uses explicit permission codes instead of role names', () => {
    localStorage.setItem('role', 'Admin');
    expect(hasPermission('location.manage')).toBe(false);
    expect(hasPermission('product.update')).toBe(false);

    setCurrentPermissions(['location.manage', 'product.update', 'putaway.execute', 'receipt.complete', 'receipt.update']);
    expect(hasPermission('location.manage')).toBe(true);
    expect(hasPermission('product.update')).toBe(true);
    expect(hasPermission('putaway.execute')).toBe(true);
    expect(hasPermission('receipt.complete')).toBe(true);
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

  it('reads only a valid positive integer user id from session metadata', () => {
    expect(currentUserId()).toBeNull();
    localStorage.setItem('userId', '17');
    expect(currentUserId()).toBe(17);
    localStorage.setItem('userId', '17.5');
    expect(currentUserId()).toBeNull();
  });
});
