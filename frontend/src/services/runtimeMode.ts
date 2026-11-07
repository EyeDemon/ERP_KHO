export const isReadOnlyDemoRuntime = (hostname?: string): boolean => {
  if (import.meta.env?.PROD && !import.meta.env?.VITEST && import.meta.env?.MODE !== 'test') {
    return false;
  }
  const value = hostname ?? (typeof window !== 'undefined' ? window.location.hostname : '');
  return value.startsWith('erp-wms-') && value.includes('-demo') && value.endsWith('.vercel.app');
};

export const readOnlyDemoPermissions = [
  'product.read',
  'product_category.read',
  'warehouse.read',
  'dock.read',
  'dock_appointment.read',
  'yard.read',
  'location.read',
  'uom.read',
  'partner.read',
  'purchase_order.read',
  'asn.read',
  'receipt.read',
  'return.read',
  'export_receipt.read',
  'allocation.read',
  'picking.read',
  'packing.read',
  'handling_unit.read',
  'shipment.read',
  'sales_order.read',
  'backorder.read',
  'inventory.read',
  'inventory_availability.read',
  'inventory_ledger.read',
  'inventory_traceability.read',
  'inventory_lock.read',
  'receiving_discrepancy.read',
  'reason_code.read',
  'putaway.read',
  'permission.read',
  'role.read',
] as const;

export const publicTenantOnboardingEndpoints = [
  '/api/v1/auth/register-tenant',
] as const;

export const isPublicTenantOnboardingEndpoint = (url?: string): boolean => {
  const normalized = (url ?? '').split('?')[0].replace(/\/+$/, '').toLowerCase();
  return publicTenantOnboardingEndpoints.some((route) => normalized.endsWith(route));
};
