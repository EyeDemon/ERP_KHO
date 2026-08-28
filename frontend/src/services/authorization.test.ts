import { describe, expect, it } from 'vitest';
import {
  canApproveExportImmediately,
  canManageCatalogs,
  canManageWarehouses,
  canOperateWarehouse,
  canRunExportMutation,
  canViewStocktakes,
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

  it('aligns immediate export approval with the backend staff feature flag', () => {
    expect(canApproveExportImmediately('Admin', false)).toBe(true);
    expect(canApproveExportImmediately('Manager', false)).toBe(true);
    expect(canApproveExportImmediately('WarehouseStaff', false)).toBe(false);
    expect(canApproveExportImmediately('WarehouseStaff', true)).toBe(true);
    expect(canApproveExportImmediately('Viewer', true)).toBe(false);
  });
});
