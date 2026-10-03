export const isBlueprintDemoRuntime = (hostname?: string): boolean => {
  const value = hostname ?? (typeof window !== 'undefined' ? window.location.hostname : '');
  return value.startsWith('erp-wms-blueprint-demo') && value.endsWith('.vercel.app');
};

export const blueprintDemoReadPermissions = [
  'product.read',
  'warehouse.read',
  'uom.read',
  'partner.read',
  'receipt.read',
  'putaway.read',
] as const;
