export const isBlueprintDemoRuntime = (hostname?: string): boolean => {
  const value = hostname ?? (typeof window !== 'undefined' ? window.location.hostname : '');
  return value.startsWith('erp-wms-blueprint-demo') && value.endsWith('.vercel.app');
};

export const blueprintDemoReadPermissions = [
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
  'receiving_discrepancy.read',
  'reason_code.read',
  'putaway.read',
  'permission.read',
  'role.read',
] as const;
