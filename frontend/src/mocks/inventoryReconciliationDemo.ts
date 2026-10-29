export interface DemoReconciliationRow {
  productId: number;
  productCode: string;
  productName: string;
  warehouseId: number;
  warehouseName: string;
  currentQuantity: number;
  expectedQuantity: number;
  difference: number;
  importQuantity: number;
  exportQuantity: number;
  transferInQuantity: number;
  transferOutQuantity: number;
  adjustmentIncreaseQuantity: number;
  adjustmentDecreaseQuantity: number;
  status: string;
}

export const demoReconciliationWarehouses = [
  { id: 1, name: 'Kho TP.HCM', isActive: true },
  { id: 2, name: 'Kho Đà Nẵng', isActive: true },
];

export const demoReconciliationRows: DemoReconciliationRow[] = [
  {
    productId: 1001,
    productCode: 'SKU-1001',
    productName: 'Nước khoáng 500ml',
    warehouseId: 1,
    warehouseName: 'Kho TP.HCM',
    currentQuantity: 1350,
    expectedQuantity: 1350,
    difference: 0,
    importQuantity: 1800,
    exportQuantity: 420,
    transferInQuantity: 40,
    transferOutQuantity: 70,
    adjustmentIncreaseQuantity: 0,
    adjustmentDecreaseQuantity: 0,
    status: 'Match',
  },
  {
    productId: 1008,
    productCode: 'SKU-1008',
    productName: 'Sữa hộp 180ml',
    warehouseId: 1,
    warehouseName: 'Kho TP.HCM',
    currentQuantity: 642,
    expectedQuantity: 640,
    difference: 2,
    importQuantity: 900,
    exportQuantity: 250,
    transferInQuantity: 0,
    transferOutQuantity: 10,
    adjustmentIncreaseQuantity: 0,
    adjustmentDecreaseQuantity: 0,
    status: 'Mismatch',
  },
  {
    productId: 2012,
    productCode: 'SKU-2012',
    productName: 'Thùng carton M',
    warehouseId: 2,
    warehouseName: 'Kho Đà Nẵng',
    currentQuantity: 286,
    expectedQuantity: 290,
    difference: -4,
    importQuantity: 400,
    exportQuantity: 90,
    transferInQuantity: 0,
    transferOutQuantity: 20,
    adjustmentIncreaseQuantity: 0,
    adjustmentDecreaseQuantity: 0,
    status: 'Mismatch',
  },
];
