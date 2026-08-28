export type AppRole = 'Admin' | 'Manager' | 'WarehouseStaff' | 'Viewer';

export const currentRole = (): AppRole => {
  const role = localStorage.getItem('role');
  return role === 'Admin' || role === 'Manager' || role === 'WarehouseStaff' || role === 'Viewer'
    ? role
    : 'Viewer';
};

export const canManageWarehouses = (role: AppRole) => role === 'Admin';
export const canManageCatalogs = (role: AppRole) => role === 'Admin' || role === 'Manager';
export const canOperateWarehouse = (role: AppRole) => role !== 'Viewer';
export const canApproveExportImmediately = (
  role: AppRole,
  allowWarehouseStaffDirectDispatch: boolean,
) => role === 'Admin'
  || role === 'Manager'
  || (role === 'WarehouseStaff' && allowWarehouseStaffDirectDispatch);
export const canViewStocktakes = (role: AppRole) => role !== 'Viewer';
export const canRunExportMutation = (role: AppRole, writeEnabled: boolean) =>
  writeEnabled && canOperateWarehouse(role);
