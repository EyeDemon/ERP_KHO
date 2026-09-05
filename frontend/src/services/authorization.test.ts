import { describe, expect, it } from 'vitest';
import {
  canApproveExportImmediately,
  canManageCatalogs,
  canManageWarehouses,
  canOperateWarehouse,
  canRunExportMutation,
  canViewStocktakes,
  currentUserId,
  type AppRole,
} from './authorization';

const roles: AppRole[] = ['Admin', 'Manager', 'WarehouseStaff', 'Viewer'];

describe('frontend authorization matrix', () => {
  it('limits warehouse catalog mutations to Admin', () => {
    expect(roles.filter(canManageWarehouses)).toEqual(['Admin']);
  });

  it('limits product and unit mutations to Admin and Manager', () => {
    expect(roles.filter(canManageCatalogs)).toEqual(['Admin', 'Manager']);
  });

  it('keeps Viewer read-only and outside stocktake operations', () => {
    expect(roles.filter(canOperateWarehouse)).toEqual(['Admin', 'Manager', 'WarehouseStaff']);
    expect(roles.filter(canViewStocktakes)).toEqual(['Admin', 'Manager', 'WarehouseStaff']);
  });

  it('disables export mutations for every role while the maintenance switch is off', () => {
    expect(roles.filter(role => canRunExportMutation(role, false))).toEqual([]);
    expect(roles.filter(role => canRunExportMutation(role, true)))
      .toEqual(['Admin', 'Manager', 'WarehouseStaff']);
  });

  it('limits immediate export approval to checker roles regardless of the legacy staff flag', () => {
    expect(canApproveExportImmediately('Admin', false)).toBe(true);
    expect(canApproveExportImmediately('Manager', false)).toBe(true);
    expect(canApproveExportImmediately('WarehouseStaff', false)).toBe(false);
    expect(canApproveExportImmediately('WarehouseStaff', true)).toBe(false);
    expect(canApproveExportImmediately('Viewer', true)).toBe(false);
  });

  it('reads only a valid positive integer user id from session metadata', () => {
    const values = new Map<string, string>();
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      value: {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => values.set(key, value),
      },
    });

    expect(currentUserId()).toBeNull();
    localStorage.setItem('userId', '17');
    expect(currentUserId()).toBe(17);
    localStorage.setItem('userId', '17.5');
    expect(currentUserId()).toBeNull();
  });
});
