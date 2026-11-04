export type MockTone = 'green' | 'blue' | 'orange' | 'red' | 'gray';
export type SecurityDomain = 'PLATFORM' | 'CUSTOMER_TENANT';

export type CanonicalRoleName =
  | 'SuperAdmin'
  | 'BillingAdmin'
  | 'SupportAdmin'
  | 'SecurityAuditor'
  | 'Admin'
  | 'Manager'
  | 'Supervisor'
  | 'Accountant'
  | 'WarehouseStaff'
  | 'Viewer';

export interface CanonicalRoleProfile {
  roleName: CanonicalRoleName;
  labelVi: string;
  securityDomain: SecurityDomain;
  permissionCount: number;
  permissions: readonly string[];
  canReadInventoryCost: boolean;
  warehouseScopeIds: readonly number[];
  defaultUsername: string;
}

export interface MockWarehouse {
  code: string;
  name: string;
  city: string;
  type: 'DC' | 'Regional' | 'Hub';
}

export interface MockProduct {
  code: string;
  name: string;
  category: string;
  baseUom: string;
  barcodes: string[];
  tracking: 'None' | 'Lot' | 'Serial';
}

export interface MockPartner {
  code: string;
  name: string;
  roles: Array<'Supplier' | 'Customer' | 'Carrier'>;
}

export interface MockUser {
  code: string;
  name: string;
  role: string;
  warehouses: string[];
}

export interface MockOperationalRecord {
  id: string;
  reference: string;
  type: string;
  status: string;
  tone: MockTone;
  warehouse: string;
  subject: string;
  productCode?: string;
  partnerCode?: string;
  quantity?: number;
  uom?: string;
  location?: string;
  owner: string;
  priority: 'Low' | 'Normal' | 'High' | 'Critical';
  updatedAt: string;
  note?: string;
}

export interface MockWorkCenterData {
  moduleKey: string;
  snapshotAt: string;
  records: MockOperationalRecord[];
}

export interface MockInventoryBalance {
  warehouse: string;
  productCode: string;
  onHand: number;
  reserved: number;
  allocated: number;
  available: number;
  qcHold: number;
  quarantine: number;
  inTransit: number;
}

export interface MockTransferConservation {
  reference: string;
  productCode: string;
  requested: number;
  source: number;
  transit: number;
  destination: number;
}

export interface MockRecountAttempt {
  countRef: string;
  productCode: string;
  systemQty: number;
  attempts: Array<{ attempt: number; countedQty: number; accepted: boolean }>;
  finalAccepted: number;
}

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

export interface CanonicalMockResolution {
  status: number;
  data: unknown;
  errorCode?: string;
  message?: string;
}

// ============================================================================
// 1. CANONICAL PERMISSION CATALOG (141 Customer WMS + 10 Platform = 151 Total)
// ============================================================================

export const CANONICAL_PLATFORM_PERMISSION_CODES = [
  'platform.audit.read',
  'platform.billing.manage',
  'platform.billing.reconcile',
  'platform.dashboard.read',
  'platform.integration.manage',
  'platform.quota.override',
  'platform.support.impersonate',
  'platform.tenant.provision',
  'platform.tenant.read',
  'platform.tenant.suspend',
] as const;

export const CANONICAL_CUSTOMER_PERMISSION_CODES = [
  'allocation.create',
  'allocation.read',
  'allocation.reallocate',
  'allocation.release',
  'approval.approve',
  'approval.reject',
  'asn.cancel',
  'asn.confirm',
  'asn.create',
  'asn.read',
  'asn.receive',
  'asn.update',
  'backorder.manage',
  'backorder.read',
  'cod_reconciliation.manage',
  'dock.manage',
  'dock.read',
  'dock_appointment.manage',
  'dock_appointment.read',
  'einvoice.issue',
  'export_receipt.approve',
  'export_receipt.cancel',
  'export_receipt.create',
  'export_receipt.dispatch',
  'export_receipt.read',
  'export_receipt.reprint_awb',
  'export_receipt.update',
  'handling_unit.create',
  'handling_unit.merge',
  'handling_unit.modify',
  'handling_unit.read',
  'handling_unit.repack',
  'handling_unit.split',
  'inventory.cost.read',
  'inventory.read',
  'inventory_availability.read',
  'inventory_ledger.read',
  'inventory_lock.manage',
  'inventory_lock.read',
  'inventory_movement.create',
  'inventory_opening_balance.import',
  'inventory_reversal.create',
  'inventory_status_change.create',
  'inventory_traceability.read',
  'loading.execute',
  'location.manage',
  'location.read',
  'packing.execute',
  'packing.read',
  'packing.reopen',
  'partner.create',
  'partner.deactivate',
  'partner.read',
  'partner.update',
  'permission.assign',
  'permission.read',
  'picking.assign',
  'picking.execute',
  'picking.override',
  'picking.read',
  'picking.short_pick',
  'product.create',
  'product.deactivate',
  'product.read',
  'product.update',
  'product_barcode.manage',
  'product_category.manage',
  'product_category.read',
  'product_uom.manage',
  'purchase_order.cancel',
  'purchase_order.close',
  'purchase_order.create',
  'purchase_order.read',
  'purchase_order.release',
  'purchase_order.update',
  'putaway.assign',
  'putaway.cancel',
  'putaway.execute',
  'putaway.read',
  'quality_disposition.approve',
  'quality_inspection.complete',
  'quality_inspection.execute',
  'quality_policy.manage',
  'quality_policy.read',
  'reason_code.manage',
  'reason_code.read',
  'receipt.cancel',
  'receipt.complete',
  'receipt.create',
  'receipt.post',
  'receipt.read',
  'receipt.receive',
  'receipt.update',
  'receiving_discrepancy.approve',
  'receiving_discrepancy.create',
  'receiving_discrepancy.read',
  'receiving_discrepancy.reject',
  'receiving_discrepancy.resolve',
  'receiving_discrepancy.submit',
  'receiving_tolerance_policy.manage',
  'receiving_tolerance_policy.read',
  'replenishment.execute',
  'replenishment.generate',
  'replenishment.read',
  'report.export',
  'return.create',
  'return.disposition',
  'return.inspect',
  'return.post',
  'return.read',
  'return.receive',
  'role.manage',
  'role.read',
  'sales_order.cancel',
  'sales_order.close',
  'sales_order.create',
  'sales_order.hold',
  'sales_order.read',
  'sales_order.release',
  'sales_order.update',
  'shipment.confirm_delivery',
  'shipment.dispatch',
  'shipment.load',
  'shipment.read',
  'shipment.stage',
  'shipment.update',
  'stocktake.manage',
  'stocktake.read',
  'uom.manage',
  'uom.read',
  'user.manage',
  'user.read',
  'user_warehouse.manage',
  'user_warehouse.read',
  'warehouse.manage',
  'warehouse.read',
  'warehouse_calendar.manage',
  'yard.assign_dock',
  'yard.checkin',
  'yard.checkout',
  'yard.read',
] as const;

export const CANONICAL_ALL_PERMISSION_CODES: readonly string[] = [
  ...CANONICAL_CUSTOMER_PERMISSION_CODES,
  ...CANONICAL_PLATFORM_PERMISSION_CODES,
].sort();

const VIEWER_18_PERMISSIONS: readonly string[] = [
  'asn.read',
  'backorder.read',
  'export_receipt.read',
  'inventory.read',
  'inventory_availability.read',
  'inventory_ledger.read',
  'inventory_traceability.read',
  'location.read',
  'partner.read',
  'product.read',
  'product_category.read',
  'purchase_order.read',
  'receipt.read',
  'sales_order.read',
  'shipment.read',
  'stocktake.read',
  'uom.read',
  'warehouse.read',
];

const WAREHOUSE_STAFF_36_PERMISSIONS: readonly string[] = [
  'asn.read',
  'asn.receive',
  'dock_appointment.read',
  'handling_unit.create',
  'handling_unit.merge',
  'handling_unit.modify',
  'handling_unit.read',
  'handling_unit.repack',
  'handling_unit.split',
  'inventory.read',
  'inventory_movement.create',
  'loading.execute',
  'location.read',
  'packing.execute',
  'packing.read',
  'picking.execute',
  'picking.read',
  'picking.short_pick',
  'product.read',
  'putaway.execute',
  'putaway.read',
  'receipt.complete',
  'receipt.read',
  'receipt.receive',
  'receiving_discrepancy.create',
  'replenishment.execute',
  'replenishment.read',
  'return.read',
  'return.receive',
  'shipment.load',
  'shipment.read',
  'shipment.stage',
  'uom.read',
  'warehouse.read',
  'yard.checkin',
  'yard.checkout',
];

const ACCOUNTANT_42_PERMISSIONS: readonly string[] = [
  'allocation.read',
  'asn.read',
  'backorder.read',
  'cod_reconciliation.manage',
  'dock.read',
  'dock_appointment.read',
  'einvoice.issue',
  'export_receipt.read',
  'handling_unit.read',
  'inventory.cost.read',
  'inventory.read',
  'inventory_availability.read',
  'inventory_ledger.read',
  'inventory_lock.read',
  'inventory_traceability.read',
  'location.read',
  'packing.read',
  'partner.create',
  'partner.read',
  'partner.update',
  'permission.read',
  'picking.read',
  'product.read',
  'product_category.read',
  'purchase_order.read',
  'putaway.read',
  'quality_policy.read',
  'reason_code.read',
  'receipt.read',
  'receiving_discrepancy.read',
  'receiving_tolerance_policy.read',
  'replenishment.read',
  'report.export',
  'return.read',
  'role.read',
  'sales_order.read',
  'shipment.read',
  'stocktake.read',
  'uom.read',
  'user.read',
  'warehouse.read',
  'yard.read',
];

const SUPERVISOR_54_PERMISSIONS: readonly string[] = [
  ...WAREHOUSE_STAFF_36_PERMISSIONS,
  'inventory_lock.manage',
  'inventory_lock.read',
  'inventory_status_change.create',
  'packing.reopen',
  'picking.assign',
  'picking.override',
  'putaway.assign',
  'quality_inspection.complete',
  'quality_inspection.execute',
  'receiving_discrepancy.read',
  'receiving_discrepancy.resolve',
  'receiving_discrepancy.submit',
  'replenishment.generate',
  'return.disposition',
  'return.inspect',
  'stocktake.manage',
  'stocktake.read',
  'yard.assign_dock',
].sort();

const MANAGER_98_PERMISSIONS: readonly string[] = [
  'allocation.create',
  'allocation.read',
  'allocation.reallocate',
  'allocation.release',
  'approval.approve',
  'approval.reject',
  'asn.confirm',
  'asn.create',
  'asn.read',
  'asn.receive',
  'asn.update',
  'backorder.manage',
  'backorder.read',
  'dock.read',
  'dock_appointment.manage',
  'dock_appointment.read',
  'export_receipt.approve',
  'export_receipt.cancel',
  'export_receipt.create',
  'export_receipt.dispatch',
  'export_receipt.read',
  'export_receipt.reprint_awb',
  'export_receipt.update',
  'handling_unit.create',
  'handling_unit.read',
  'inventory.cost.read',
  'inventory.read',
  'inventory_availability.read',
  'inventory_ledger.read',
  'inventory_lock.manage',
  'inventory_lock.read',
  'inventory_movement.create',
  'inventory_reversal.create',
  'inventory_status_change.create',
  'inventory_traceability.read',
  'location.manage',
  'location.read',
  'packing.execute',
  'packing.read',
  'packing.reopen',
  'partner.create',
  'partner.read',
  'partner.update',
  'permission.read',
  'picking.assign',
  'picking.override',
  'picking.read',
  'picking.short_pick',
  'product.create',
  'product.deactivate',
  'product.read',
  'product.update',
  'product_category.read',
  'purchase_order.read',
  'purchase_order.release',
  'putaway.assign',
  'putaway.cancel',
  'putaway.execute',
  'putaway.read',
  'quality_disposition.approve',
  'quality_inspection.complete',
  'quality_inspection.execute',
  'receipt.cancel',
  'receipt.complete',
  'receipt.create',
  'receipt.post',
  'receipt.read',
  'receipt.receive',
  'receipt.update',
  'receiving_discrepancy.approve',
  'receiving_discrepancy.read',
  'receiving_discrepancy.reject',
  'receiving_discrepancy.resolve',
  'replenishment.execute',
  'replenishment.generate',
  'replenishment.read',
  'report.export',
  'return.create',
  'return.disposition',
  'return.inspect',
  'return.post',
  'return.read',
  'return.receive',
  'sales_order.hold',
  'sales_order.read',
  'sales_order.release',
  'shipment.confirm_delivery',
  'shipment.dispatch',
  'shipment.load',
  'shipment.read',
  'shipment.stage',
  'shipment.update',
  'stocktake.manage',
  'stocktake.read',
  'uom.read',
  'warehouse.manage',
  'warehouse.read',
  'yard.read',
];

export const CANONICAL_ROLE_PROFILES: Record<CanonicalRoleName, CanonicalRoleProfile> = {
  SuperAdmin: {
    roleName: 'SuperAdmin',
    labelVi: 'Quản trị viên Tối cao Nền tảng',
    securityDomain: 'PLATFORM',
    permissionCount: 151,
    permissions: CANONICAL_ALL_PERMISSION_CODES,
    canReadInventoryCost: true,
    warehouseScopeIds: [1, 2],
    defaultUsername: 'qa_admin',
  },
  BillingAdmin: {
    roleName: 'BillingAdmin',
    labelVi: 'Quản trị Cước phí & Đối soát SaaS',
    securityDomain: 'PLATFORM',
    permissionCount: 7,
    permissions: [
      'permission.read',
      'platform.audit.read',
      'platform.billing.manage',
      'platform.billing.reconcile',
      'platform.dashboard.read',
      'platform.quota.override',
      'platform.tenant.read',
    ],
    canReadInventoryCost: false,
    warehouseScopeIds: [],
    defaultUsername: 'qa_billing_admin',
  },
  SupportAdmin: {
    roleName: 'SupportAdmin',
    labelVi: 'Chuyên viên Hỗ trợ Kỹ thuật SaaS',
    securityDomain: 'PLATFORM',
    permissionCount: 5,
    permissions: [
      'permission.read',
      'platform.audit.read',
      'platform.dashboard.read',
      'platform.support.impersonate',
      'platform.tenant.read',
    ],
    canReadInventoryCost: false,
    warehouseScopeIds: [],
    defaultUsername: 'qa_support_admin',
  },
  SecurityAuditor: {
    roleName: 'SecurityAuditor',
    labelVi: 'Kiểm toán viên An toàn Thông tin',
    securityDomain: 'PLATFORM',
    permissionCount: 4,
    permissions: [
      'permission.read',
      'platform.audit.read',
      'platform.dashboard.read',
      'platform.tenant.read',
    ],
    canReadInventoryCost: false,
    warehouseScopeIds: [],
    defaultUsername: 'qa_security_auditor',
  },
  Admin: {
    roleName: 'Admin',
    labelVi: 'Quản trị viên công ty',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 141,
    permissions: CANONICAL_CUSTOMER_PERMISSION_CODES,
    canReadInventoryCost: true,
    warehouseScopeIds: [1, 2],
    defaultUsername: 'tenant_admin',
  },
  Manager: {
    roleName: 'Manager',
    labelVi: 'Quản lý kho',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 98,
    permissions: MANAGER_98_PERMISSIONS,
    canReadInventoryCost: true,
    warehouseScopeIds: [1],
    defaultUsername: 'qa_manager_wh01',
  },
  Supervisor: {
    roleName: 'Supervisor',
    labelVi: 'Giám sát & QC',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 54,
    permissions: SUPERVISOR_54_PERMISSIONS,
    canReadInventoryCost: false,
    warehouseScopeIds: [1],
    defaultUsername: 'qa_supervisor_wh01',
  },
  Accountant: {
    roleName: 'Accountant',
    labelVi: 'Kế toán kho',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 42,
    permissions: ACCOUNTANT_42_PERMISSIONS,
    canReadInventoryCost: true,
    warehouseScopeIds: [1, 2],
    defaultUsername: 'qa_accountant',
  },
  WarehouseStaff: {
    roleName: 'WarehouseStaff',
    labelVi: 'Nhân viên kho',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 36,
    permissions: WAREHOUSE_STAFF_36_PERMISSIONS,
    canReadInventoryCost: false,
    warehouseScopeIds: [1],
    defaultUsername: 'qa_staff_wh01',
  },
  Viewer: {
    roleName: 'Viewer',
    labelVi: 'Chỉ xem / Kiểm toán',
    securityDomain: 'CUSTOMER_TENANT',
    permissionCount: 18,
    permissions: VIEWER_18_PERMISSIONS,
    canReadInventoryCost: false,
    warehouseScopeIds: [1],
    defaultUsername: 'qa_viewer_wh01',
  },
};

const resolveMockPermissionDescriptionVi = (code: string): string => {
  if (code === 'approval.approve') return 'Phê duyệt phiếu nhập kho';
  if (code === 'approval.reject') return 'Từ chối phê duyệt phiếu nhập kho';
  return `Quyền hệ thống ${code}`;
};

export const demoPermissionCatalog = CANONICAL_ALL_PERMISSION_CODES.map((code) => ({
  code,
  description: resolveMockPermissionDescriptionVi(code),
}));

export const demoCustomerPermissionCatalog = CANONICAL_CUSTOMER_PERMISSION_CODES.map((code) => ({
  code,
  description: resolveMockPermissionDescriptionVi(code),
}));

// ============================================================================
// 2. CANONICAL BASELINE DATASETS (TENANT-001, WH01/WH02, BILLING, 9D STOCK)
// ============================================================================

export const canonicalTenantSubscription = {
  tenantId: 1,
  tenantCode: 'TENANT-001',
  companyName: 'CÔNG TY TNHH THƯƠNG MẠI & DỊCH VỤ KHO VẬN ĐẠI PHÁT',
  taxCode: '0312345678',
  status: 'ACTIVE',
  lifecycleStatus: 'ACTIVE',
  planCode: 'GROWTH',
  planName: 'Gói Tăng trưởng',
  monthlyPriceVnd: 1_490_000,
  subTotalVnd: 1_354_545,
  vatRatePercent: 10,
  vatAmountVnd: 135_455,
  maxWarehouses: 3,
  activeWarehousesCount: 1,
  maxUsers: 20,
  activeUsersCount: 10,
  monthlyOrderQuota: 10_000,
  currentMonthOrderCount: 6_420,
  beneficiary: {
    bankName: 'TPBank',
    bankBin: '970423',
    accountNo: '98418102005',
    accountName: 'HUYNH PHI VAN',
  },
};

export const canonicalSaasInvoices = [
  {
    id: 2,
    invoiceId: 2,
    invoiceCode: 'INV-SAAS-202611-002',
    tenantId: 1,
    tenantCode: 'TENANT-001',
    companyName: canonicalTenantSubscription.companyName,
    billingPeriod: '11/2026',
    status: 'UNPAID',
    subTotalVnd: 1_354_545,
    vatRatePercent: 10,
    vatAmountVnd: 135_455,
    amountVnd: 1_490_000,
    totalAmountVnd: 1_490_000,
    transferContent: 'ERPKHOINVSAAS202611002',
    issuedDateVi: '01/11/2026',
    dueDateVi: '05/11/2026',
    paidAtUtc: null,
    bankCreditTimestampVi: null,
    bankTransactionRef: null,
    reconciliationAuditNote: null,
    vietQrGeneratedOnDemandOnly: true,
    beneficiaryBank: 'TPBank',
    beneficiaryAccountNo: '98418102005',
    beneficiaryAccountName: 'HUYNH PHI VAN',
  },
  {
    id: 1,
    invoiceId: 1,
    invoiceCode: 'INV-SAAS-202610-001',
    tenantId: 1,
    tenantCode: 'TENANT-001',
    companyName: canonicalTenantSubscription.companyName,
    billingPeriod: '10/2026',
    status: 'PAID',
    subTotalVnd: 1_354_545,
    vatRatePercent: 10,
    vatAmountVnd: 135_455,
    amountVnd: 1_490_000,
    totalAmountVnd: 1_490_000,
    transferContent: 'ERPKHOINVSAAS202610001',
    issuedDateVi: '01/10/2026',
    dueDateVi: '05/10/2026',
    paidAtUtc: '2026-10-03T09:15:00Z',
    bankCreditTimestampVi: '03/10/2026 16:00',
    bankTransactionRef: 'FT2627690123',
    reconciliationAuditNote: 'Xác nhận thủ công bởi Chủ dự án lúc 03/10/2026 16:15 (UNC: FT2627690123)',
    vietQrGeneratedOnDemandOnly: true,
    beneficiaryBank: 'TPBank',
    beneficiaryAccountNo: '98418102005',
    beneficiaryAccountName: 'HUYNH PHI VAN',
  },
];

export const canonicalWarehouses = [
  {
    id: 1,
    code: 'WH01',
    legacyCode: 'WH-HCM-01',
    name: 'Kho Trung tâm (WH01)',
    address: 'Quận 12, TP. Hồ Chí Minh',
    city: 'TP.HCM',
    timeZoneId: 'Asia/Ho_Chi_Minh',
    isActive: true,
  },
  {
    id: 2,
    code: 'WH02',
    legacyCode: 'WH-DN-01',
    name: 'Kho Phụ trợ (WH02)',
    address: 'Liên Chiểu, Đà Nẵng',
    city: 'Đà Nẵng',
    timeZoneId: 'Asia/Ho_Chi_Minh',
    isActive: false,
  },
];

export const demoWarehouses = canonicalWarehouses.map((w) => ({
  id: w.id,
  code: w.code,
  legacyCode: w.legacyCode,
  name: w.name,
  address: w.address,
  isActive: w.isActive,
}));

export const canonicalInventoryCurrentRows = [
  {
    inventoryStockId: 9001,
    tenantId: 1,
    warehouseId: 1,
    warehouseCode: 'WH01',
    warehouseName: 'Kho Trung tâm (WH01)',
    productId: 101,
    productCode: 'QA-E2E-001',
    productName: 'Thùng linh kiện điện tử QA-01',
    unitName: 'Thùng',
    status: 'AVAILABLE',
    locationId: 1101,
    locationCode: 'A01-R02-L03-B04',
    lotId: null,
    lotNumber: null,
    serialId: null,
    serialNumber: null,
    handlingUnitId: null,
    ownerId: 1,
    grainHash: '9d8f4a21c6e0731b8942d5a01f33e9b7c4d2e8a1065b9f3c7e1a2408d6b5c391',
    onHandQuantity: 162,
    reservedQuantity: 9,
    allocatedQuantity: 0,
    availableQuantity: 153,
    unitCost: 250_000,
    averageCost: 250_000,
    movingAverageCost: 250_000,
    totalValue: 40_500_000,
    inventoryValue: 40_500_000,
    costHiddenByRbac: false,
    lastUpdated: '2026-11-03T08:00:00Z',
  },
  {
    inventoryStockId: 9002,
    tenantId: 1,
    warehouseId: 1,
    warehouseCode: 'WH01',
    warehouseName: 'Kho Trung tâm (WH01)',
    productId: 102,
    productCode: 'QA-LOT-002',
    productName: 'Hộp cảm biến nhiệt độ QA-02',
    unitName: 'Thùng',
    status: 'AVAILABLE',
    locationId: 1102,
    locationCode: 'A01-R02-L03-B05',
    lotId: 8201,
    lotNumber: 'LOT-QA-202610',
    serialId: null,
    serialNumber: null,
    handlingUnitId: null,
    ownerId: 1,
    grainHash: '3b1c9e47f0a8265d4109e7c28a5f6b319d4e08a7c2b6f1538e90d4a27c1b8e64',
    onHandQuantity: 186,
    reservedQuantity: 0,
    allocatedQuantity: 0,
    availableQuantity: 186,
    unitCost: 420_000,
    averageCost: 420_000,
    movingAverageCost: 420_000,
    totalValue: 78_120_000,
    inventoryValue: 78_120_000,
    costHiddenByRbac: false,
    lastUpdated: '2026-11-03T08:05:00Z',
  },
  {
    inventoryStockId: 9003,
    tenantId: 1,
    warehouseId: 1,
    warehouseCode: 'WH01',
    warehouseName: 'Kho Trung tâm (WH01)',
    productId: 103,
    productCode: 'QA-PAPER',
    productName: 'QA Giấy A4',
    unitName: 'Hộp',
    status: 'AVAILABLE',
    locationId: 1101,
    locationCode: 'A01-R02-L03-B04',
    lotId: null,
    lotNumber: null,
    serialId: null,
    serialNumber: null,
    handlingUnitId: null,
    ownerId: 1,
    grainHash: '7a4c2d19e8b3056f1294a7c83e5d0b164f2a8c90e1b57d346a82c0f91d4e7b52',
    onHandQuantity: 40,
    reservedQuantity: 0,
    allocatedQuantity: 0,
    availableQuantity: 40,
    unitCost: 250_000,
    averageCost: 250_000,
    movingAverageCost: 250_000,
    totalValue: 10_000_000,
    inventoryValue: 10_000_000,
    costHiddenByRbac: false,
    lastUpdated: '2026-11-03T08:10:00Z',
  },
];

export const CANONICAL_TOTAL_INVENTORY_VALUE_VND = canonicalInventoryCurrentRows.reduce(
  (sum, item) => sum + item.totalValue,
  0,
); // 40_500_000 + 78_120_000 + 10_000_000 = 128_620_000 VND

export const canonicalKitWorkOrders = [
  {
    id: 601,
    workOrderCode: 'KWO-2026-001',
    kitProductId: 104,
    kitProductCode: 'QA-KIT-BUNDLE-01',
    kitProductName: 'Bộ Quà Tặng Doanh Nghiệp Cao Cấp',
    warehouseId: 1,
    warehouseCode: 'WH01',
    warehouseName: 'Kho Trung tâm (WH01)',
    mode: 'PRE_KIT',
    status: 'Completed',
    plannedKitQty: 20,
    completedKitQty: 20,
    baseUomCode: 'Bộ',
    rolledUpUnitCost: 500_000,
    totalCost: 10_000_000,
    earliestComponentExpiryDate: '2027-10-31T00:00:00Z',
    costHiddenByRbac: false,
    components: [
      {
        componentProductId: 102,
        componentProductCode: 'QA-LOT-002',
        componentProductName: 'Hộp cảm biến nhiệt độ QA-02',
        qtyPerKit: 1,
        requiredBaseQty: 20,
        availableBaseQty: 186,
        unitCost: 420_000,
        lineCostPerKit: 420_000,
      },
      {
        componentProductId: 105,
        componentProductCode: 'QA-AUX-003',
        componentProductName: 'Hộp Quà Cứng Nam Châm Sang Trọng',
        qtyPerKit: 1,
        requiredBaseQty: 20,
        availableBaseQty: 150,
        unitCost: 80_000,
        lineCostPerKit: 80_000,
      },
    ],
  },
  {
    id: 602,
    workOrderCode: 'KWO-2026-002',
    kitProductId: 104,
    kitProductCode: 'QA-KIT-BUNDLE-01',
    kitProductName: 'Bộ Quà Tặng Doanh Nghiệp Cao Cấp',
    warehouseId: 1,
    warehouseCode: 'WH01',
    warehouseName: 'Kho Trung tâm (WH01)',
    mode: 'PRE_KIT',
    status: 'PENDING',
    plannedKitQty: 10,
    completedKitQty: 0,
    baseUomCode: 'Bộ',
    rolledUpUnitCost: 500_000,
    totalCost: 5_000_000,
    earliestComponentExpiryDate: '2027-10-31T00:00:00Z',
    costHiddenByRbac: false,
    components: [
      {
        componentProductId: 102,
        componentProductCode: 'QA-LOT-002',
        componentProductName: 'Hộp cảm biến nhiệt độ QA-02',
        qtyPerKit: 1,
        requiredBaseQty: 10,
        availableBaseQty: 186,
        unitCost: 420_000,
        lineCostPerKit: 420_000,
      },
      {
        componentProductId: 105,
        componentProductCode: 'QA-AUX-003',
        componentProductName: 'Hộp Quà Cứng Nam Châm Sang Trọng',
        qtyPerKit: 1,
        requiredBaseQty: 10,
        availableBaseQty: 150,
        unitCost: 80_000,
        lineCostPerKit: 80_000,
      },
    ],
  },
];

// ============================================================================
// 3. OPERATIONAL & COMPATIBILITY DATASETS ACROSS ALL 20 CAPABILITIES
// ============================================================================

export const demoUnits = [
  { id: 1, code: 'GOI', name: 'Gói', isActive: true },
  { id: 2, code: 'HOP', name: 'Hộp', isActive: true },
  { id: 3, code: 'CAI', name: 'Cái', isActive: true },
  { id: 4, code: 'CHAI', name: 'Chai', isActive: true },
  { id: 5, code: 'THUNG', name: 'Thùng', isActive: true },
  { id: 6, code: 'BO', name: 'Bộ', isActive: true },
];

export const demoCategories = [
  { id: 1, code: 'FOOD', name: 'Thực phẩm', isActive: true },
  { id: 2, code: 'ELEC', name: 'Điện tử', isActive: true },
  { id: 3, code: 'PACK', name: 'Bao bì', isActive: true },
  { id: 4, code: 'DRINK', name: 'Đồ uống', isActive: true },
];

export const mockWarehouses: MockWarehouse[] = [
  { code: 'WH-HCM-01', name: 'Kho Trung tâm (WH01)', city: 'TP.HCM', type: 'DC' },
  { code: 'WH-DN-01', name: 'Kho Phụ trợ (WH02)', city: 'Đà Nẵng', type: 'Regional' },
  { code: 'WH-DL-01', name: 'Kho Đà Lạt', city: 'Lâm Đồng', type: 'Regional' },
  { code: 'WH-HN-01', name: 'Hub Hà Nội', city: 'Hà Nội', type: 'Hub' },
];

export const mockProducts: MockProduct[] = [
  { code: 'SKU-1001', name: 'Cà phê Arabica 500g', category: 'Thực phẩm', baseUom: 'Gói', barcodes: ['8938501000011', '8938501000012'], tracking: 'Lot' },
  { code: 'SKU-1002', name: 'Trà Ô Long 250g', category: 'Thực phẩm', baseUom: 'Hộp', barcodes: ['8938501000028'], tracking: 'Lot' },
  { code: 'SKU-2001', name: 'Tai nghe Bluetooth TWS', category: 'Điện tử', baseUom: 'Cái', barcodes: ['8938502000010'], tracking: 'Serial' },
  { code: 'SKU-2002', name: 'Bàn phím cơ Wireless', category: 'Điện tử', baseUom: 'Cái', barcodes: ['8938502000027'], tracking: 'Serial' },
  { code: 'SKU-3001', name: 'Thùng carton size M', category: 'Bao bì', baseUom: 'Cái', barcodes: ['8938503000019'], tracking: 'None' },
  { code: 'SKU-4001', name: 'Nước khoáng 500ml', category: 'Đồ uống', baseUom: 'Chai', barcodes: ['8938504000018'], tracking: 'Lot' },
  { code: 'QA-E2E-001', name: 'Thùng linh kiện điện tử QA-01', category: 'Điện tử', baseUom: 'Thùng', barcodes: ['8938509000101'], tracking: 'None' },
  { code: 'QA-LOT-002', name: 'Hộp cảm biến nhiệt độ QA-02', category: 'Điện tử', baseUom: 'Thùng', barcodes: ['8938509000102'], tracking: 'Lot' },
  { code: 'QA-PAPER', name: 'QA Giấy A4', category: 'Bao bì', baseUom: 'Hộp', barcodes: ['8938509000103'], tracking: 'None' },
];

export const mockPartners: MockPartner[] = [
  { code: 'SUP-001', name: 'Công ty Nông Sản Cao Nguyên', roles: ['Supplier'] },
  { code: 'SUP-002', name: 'Công ty Thiết Bị Việt', roles: ['Supplier'] },
  { code: 'CUS-001', name: 'Chuỗi bán lẻ Minh Phúc', roles: ['Customer'] },
  { code: 'CUS-002', name: 'Thương mại An Khang', roles: ['Customer'] },
  { code: 'LOG-001', name: 'Vận chuyển Nam Bắc', roles: ['Carrier'] },
];

export const mockUsers: MockUser[] = [
  { code: 'U-SUPER', name: 'Huỳnh Phi Vân', role: 'SuperAdmin', warehouses: ['WH01', 'WH02'] },
  { code: 'U-ADMIN', name: 'Nguyễn Minh Anh', role: 'Admin', warehouses: ['WH01', 'WH02'] },
  { code: 'U-HCM-MGR', name: 'Trần Quốc Bảo', role: 'Manager', warehouses: ['WH01'] },
  { code: 'U-HCM-SUP', name: 'Vũ Ngọc Lan', role: 'Supervisor', warehouses: ['WH01'] },
  { code: 'U-ACC', name: 'Đặng Thu Trang', role: 'Accountant', warehouses: ['WH01', 'WH02'] },
  { code: 'U-HCM-STF', name: 'Lê Thu Hà', role: 'WarehouseStaff', warehouses: ['WH01'] },
  { code: 'U-HCM-VW', name: 'Phạm Gia Huy', role: 'Viewer', warehouses: ['WH01'] },
];

export const mockInventoryBalances: MockInventoryBalance[] = [
  { warehouse: 'WH-HCM-01', productCode: 'SKU-1001', onHand: 1250, reserved: 180, allocated: 120, available: 1030, qcHold: 40, quarantine: 0, inTransit: 200 },
  { warehouse: 'WH-HCM-01', productCode: 'SKU-1002', onHand: 620, reserved: 90, allocated: 60, available: 510, qcHold: 0, quarantine: 20, inTransit: 0 },
  { warehouse: 'WH-HCM-01', productCode: 'SKU-2001', onHand: 84, reserved: 18, allocated: 12, available: 64, qcHold: 2, quarantine: 0, inTransit: 10 },
  { warehouse: 'WH-DN-01', productCode: 'SKU-1001', onHand: 460, reserved: 70, allocated: 40, available: 390, qcHold: 0, quarantine: 0, inTransit: 0 },
  { warehouse: 'WH-DN-01', productCode: 'SKU-4001', onHand: 2400, reserved: 360, allocated: 240, available: 1920, qcHold: 120, quarantine: 0, inTransit: 600 },
  { warehouse: 'WH-DL-01', productCode: 'SKU-1002', onHand: 310, reserved: 30, allocated: 20, available: 280, qcHold: 0, quarantine: 0, inTransit: 0 },
];

export const mockTransferConservation: MockTransferConservation[] = [
  { reference: 'TRF-2026-0018', productCode: 'SKU-1001', requested: 200, source: 0, transit: 200, destination: 0 },
  { reference: 'TRF-2026-0019', productCode: 'SKU-4001', requested: 600, source: 0, transit: 600, destination: 0 },
  { reference: 'TRF-2026-0016', productCode: 'SKU-2001', requested: 10, source: 0, transit: 0, destination: 10 },
];

export const mockRecountAttempts: MockRecountAttempt[] = [
  {
    countRef: 'CC-2026-0142',
    productCode: 'SKU-1001',
    systemQty: 1250,
    attempts: [
      { attempt: 1, countedQty: 1242, accepted: false },
      { attempt: 2, countedQty: 1248, accepted: false },
      { attempt: 3, countedQty: 1248, accepted: true },
    ],
    finalAccepted: 1248,
  },
  {
    countRef: 'CC-2026-0143',
    productCode: 'SKU-2001',
    systemQty: 84,
    attempts: [
      { attempt: 1, countedQty: 83, accepted: false },
      { attempt: 2, countedQty: 84, accepted: true },
    ],
    finalAccepted: 84,
  },
];

const unitByName: Record<string, number> = { Gói: 1, Hộp: 2, Cái: 3, Chai: 4, Thùng: 5, Bộ: 6 };
const categoryByName: Record<string, number> = { 'Thực phẩm': 1, 'Điện tử': 2, 'Bao bì': 3, 'Đồ uống': 4 };

export const demoProducts = mockProducts.map((product, index) => {
  const id =
    product.code === 'QA-E2E-001'
      ? 101
      : product.code === 'QA-LOT-002'
        ? 102
        : product.code === 'QA-PAPER'
          ? 103
          : index + 1;
  const unitId = unitByName[product.baseUom] ?? 3;
  const categoryId = categoryByName[product.category] ?? null;
  const unitCost =
    product.code === 'QA-E2E-001' || product.code === 'QA-PAPER'
      ? 250_000
      : product.code === 'QA-LOT-002'
        ? 420_000
        : 120_000;
  return {
    id,
    code: product.code,
    name: product.name,
    description: product.tracking === 'None' ? 'Không theo dõi lot/serial' : `Theo dõi ${product.tracking}`,
    storageClass: 'AMBIENT',
    unitWeightKg: 0.5,
    unitVolumeM3: 0.002,
    unitPalletEquivalent: 0.02,
    trackingType: product.tracking,
    expiryControl: product.tracking === 'Lot',
    shelfLifeDays: product.tracking === 'Lot' ? 365 : null,
    unitId,
    unitCode: demoUnits.find((item) => item.id === unitId)?.code ?? 'CAI',
    unitName: product.baseUom,
    unitDecimalPlaces: 0,
    categoryId,
    categoryName: demoCategories.find((item) => item.id === categoryId)?.name ?? null,
    unitCost,
    movingAverageCost: unitCost,
    barcodes: product.barcodes.map((value, barcodeIndex) => ({ id: id * 10 + barcodeIndex + 1, productId: id, value })),
    isActive: true,
    uoms: [{ unitId, unitCode: demoUnits.find((item) => item.id === unitId)?.code ?? 'CAI', unitName: product.baseUom, decimalPlaces: 0, conversionFactor: 1, version: 1 }],
  };
});

export const demoPartners = mockPartners.map((partner, index) => ({
  id: index + 1,
  code: partner.code,
  name: partner.name,
  isSupplier: partner.roles.includes('Supplier'),
  isCustomer: partner.roles.includes('Customer'),
  isActive: true,
  phone: index < 2 ? `090000000${index + 1}` : undefined,
  email: `partner${index + 1}@daiphat-wms.vn`,
  address: 'Quận 12, TP. Hồ Chí Minh',
  rowVersion: `AAAAAAAAAA${index + 1}`,
}));

export const demoWarehouseStructures = demoWarehouses.map((warehouse, index) => {
  const base = (index + 1) * 1000;
  const rackCode = index === 0 ? 'R02' : 'R01';
  const levelNo = index === 0 ? 3 : 1;
  const mkLoc = (offset: number, code: string, name: string, overrides: Record<string, unknown> = {}) => ({
    id: base + offset,
    warehouseId: warehouse.id,
    zoneId: base + 10,
    zoneCode: 'ZONE-A',
    rackLevelId: base + 40,
    aisleCode: 'A01',
    rackCode,
    levelNo,
    code,
    name,
    barcode: `LOC-${warehouse.code}-${code}`,
    pickPriority: 10,
    putawayPriority: 10,
    locationType: 'Storage',
    storageClass: null as string | null,
    maxWeightKg: null as number | null,
    maxVolumeM3: null as number | null,
    maxPalletEquivalent: null as number | null,
    mapX: null as number | null,
    mapY: null as number | null,
    mapWidth: null as number | null,
    mapHeight: null as number | null,
    isActive: true,
    isBlocked: false,
    isPickable: true,
    isReceivable: false,
    isSystemManaged: false,
    ...overrides,
  });

  const physicalLocations =
    index === 0
      ? [
          mkLoc(101, 'A01-R02-L03-B04', 'Ô lưu trữ chính B04', { storageClass: 'AMBIENT', maxWeightKg: 1500, maxVolumeM3: 10, maxPalletEquivalent: 5, mapX: 6, mapY: 10, mapWidth: 18, mapHeight: 20 }),
          mkLoc(102, 'A01-R02-L03-B05', 'Ô dự trữ B05', { isPickable: false, storageClass: 'AMBIENT', maxWeightKg: 1500, maxVolumeM3: 10, maxPalletEquivalent: 5, mapX: 28, mapY: 10, mapWidth: 18, mapHeight: 20 }),
          mkLoc(103, 'A01-R02-L03-B06', 'Ô đang khóa B06', { isBlocked: true, mapX: 50, mapY: 10, mapWidth: 18, mapHeight: 20 }),
          mkLoc(104, 'A01-R02-L03-B07', 'Ô ngừng hoạt động B07', { isActive: false, mapX: 72, mapY: 10, mapWidth: 18, mapHeight: 20 }),
        ]
      : [mkLoc(101, 'A01-R01-L01-B01', 'Ô lưu trữ phụ trợ 01')];

  return {
    warehouseId: warehouse.id,
    warehouseCode: warehouse.code,
    warehouseName: warehouse.name,
    zones: [
      {
        id: base + 10,
        warehouseId: warehouse.id,
        code: 'ZONE-A',
        name: index === 0 ? 'Khu lưu trữ A' : 'Khu lưu trữ phụ trợ',
        zoneType: 'STORAGE',
        pickPriority: 10,
        putawayPriority: 10,
        isActive: true,
        aisles: [
          {
            id: base + 20,
            zoneId: base + 10,
            code: 'A01',
            name: 'Dãy 01',
            racks: [
              {
                id: base + 30,
                aisleId: base + 20,
                code: rackCode,
                name: index === 0 ? 'Kệ 02' : 'Kệ 01',
                levels: [{ id: base + 40, rackId: base + 30, levelNo, locations: physicalLocations }],
              },
            ],
          },
        ],
        locations: [],
      },
    ],
    unmappedLocations: [],
    systemLocations: [
      mkLoc(1, 'RECEIVING', 'Vị trí nhận hàng', { zoneId: null, zoneCode: null, rackLevelId: null, aisleCode: null, rackCode: null, levelNo: null, locationType: 'Receiving', isPickable: false, isReceivable: true, isSystemManaged: true }),
    ],
  };
});

export const demoInventoryStocks = canonicalInventoryCurrentRows.map((row) => ({
  productId: row.productId,
  productCode: row.productCode,
  productName: row.productName,
  unitName: row.unitName,
  warehouseId: row.warehouseId,
  warehouseName: row.warehouseName,
  currentQuantity: row.onHandQuantity,
  quantity: row.onHandQuantity,
  onHandQuantity: row.onHandQuantity,
  reservedQuantity: row.reservedQuantity,
  availableQuantity: row.availableQuantity,
  unitCost: row.unitCost,
  averageCost: row.averageCost,
  totalValue: row.totalValue,
  inventoryValue: row.inventoryValue,
  lastUpdated: row.lastUpdated,
}));

export const demoInventoryStatuses = [
  { code: 'AVAILABLE', name: 'Khả dụng', isAvailable: true, isReservable: true, isAllocatable: true, isPickable: true, isShippable: true },
  { code: 'QC_HOLD', name: 'Chờ QC', isAvailable: false, isReservable: false, isAllocatable: false, isPickable: false, isShippable: false },
  { code: 'QUARANTINE', name: 'Biệt trữ', isAvailable: false, isReservable: false, isAllocatable: false, isPickable: false, isShippable: false },
  { code: 'DAMAGED', name: 'Hư hỏng', isAvailable: false, isReservable: false, isAllocatable: false, isPickable: false, isShippable: false },
  { code: 'REJECTED', name: 'Từ chối', isAvailable: false, isReservable: false, isAllocatable: false, isPickable: false, isShippable: false },
  { code: 'BLOCKED', name: 'Đang khóa', isAvailable: false, isReservable: false, isAllocatable: false, isPickable: false, isShippable: false },
];

export const demoInventoryBuckets = [
  {
    inventoryStockId: 7001, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', locationId: 1101, locationCode: 'A01-R02-L03-B04',
    status: 'AVAILABLE', isReservable: true, isAllocatable: true, isPickable: true, isShippable: true,
    lotId: 8101, lotNumber: 'LOT-ARABICA-2609', manufactureDate: '2026-09-01T00:00:00Z', expiryDate: '2027-09-01T00:00:00Z',
    serialId: null, serialNumber: null, onHandQuantity: 236, reservedQuantity: 16, availableQuantity: 220, lastUpdated: '2026-10-07T08:10:00Z',
  },
  {
    inventoryStockId: 7003, productId: 3, productCode: 'SKU-2001', productName: 'Tai nghe Bluetooth TWS',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', locationId: 1101, locationCode: 'A01-R02-L03-B04',
    status: 'AVAILABLE', isReservable: true, isAllocatable: true, isPickable: true, isShippable: true,
    lotId: null, lotNumber: null, manufactureDate: null, expiryDate: null,
    serialId: 9101, serialNumber: 'SN-TWS-000184', onHandQuantity: 1, reservedQuantity: 0, availableQuantity: 1, lastUpdated: '2026-10-07T08:12:00Z',
  },
  {
    inventoryStockId: 7004, productId: 102, productCode: 'QA-LOT-002', productName: 'Hạt Điều Rang Củi Bình Phước 500g (Theo Lô)',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', locationId: 1102, locationCode: 'A01-R02-L03-B05',
    status: 'AVAILABLE', isReservable: true, isAllocatable: true, isPickable: true, isShippable: true,
    lotId: 8201, lotNumber: 'LOT-QA-202610', manufactureDate: '2026-10-01T00:00:00Z', expiryDate: '2027-10-31T00:00:00Z',
    serialId: null, serialNumber: null, onHandQuantity: 188, reservedQuantity: 12, availableQuantity: 176, lastUpdated: '2026-10-07T08:13:00Z',
  },
];

export const demoInventoryLocks = [
  {
    id: 9701, lockType: 'CountFreeze', status: 'Active', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    locationId: 1101, locationCode: 'A01-R02-L03-B04', productId: 1, productCode: 'SKU-1001',
    inventoryStatus: 'AVAILABLE', lotId: 8101, lotNumber: 'LOT-ARABICA-2609', serialId: null, serialNumber: null,
    reason: 'Khóa kiểm kê định kỳ vị trí A01-R02-L03-B04', createdAt: '2026-10-07T09:00:00Z', createdBy: 1,
    expiresAt: '2026-10-08T09:00:00Z', releasedAt: null, releasedBy: null, releaseReason: null, rowVersion: 'AAAAAAAALOCK1',
  },
];

export const demoInventoryTransactions = [
  { id: 1, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', transactionType: 'Import', inventoryStatus: 'AVAILABLE', fromInventoryStatus: null, toInventoryStatus: null, lotId: 8101, lotNumber: 'LOT-ARABICA-2609', expiryDate: '2027-09-01T00:00:00Z', serialId: null, serialNumber: null, quantity: 600, referenceId: 1048, referenceType: 'ImportReceipt', transactionDate: '2026-10-03T08:20:00Z', createdBy: 1, createdByName: 'Lê Thu Hà', note: 'Ghi sổ phiếu nhập' },
  { id: 2, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', transactionType: 'Ship', inventoryStatus: 'AVAILABLE', fromInventoryStatus: null, toInventoryStatus: null, lotId: 8101, lotNumber: 'LOT-ARABICA-2609', expiryDate: '2027-09-01T00:00:00Z', serialId: null, serialNumber: null, quantity: 120, referenceId: 5108, referenceType: 'Shipment', transactionDate: '2026-10-03T09:10:00Z', createdBy: 1, createdByName: 'Trần Quốc Bảo', note: 'Xuất giao hàng' },
  { id: 4, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', transactionType: 'Move', inventoryStatus: 'AVAILABLE', fromInventoryStatus: null, toInventoryStatus: null, locationId: 1101, locationCode: 'A01-R02-L03-B04', fromLocationId: 1101, fromLocationCode: 'A01-R02-L03-B04', toLocationId: 1102, toLocationCode: 'A01-R02-L03-B05', lotId: 8101, lotNumber: 'LOT-ARABICA-2609', expiryDate: '2027-09-01T00:00:00Z', serialId: null, serialNumber: null, quantity: 24, referenceId: 9704, referenceType: 'InventoryMove', transactionDate: '2026-10-07T09:30:00Z', createdBy: 1, createdByName: 'Lê Thu Hà', note: 'Cân bằng vị trí nhặt hàng' },
  { id: 5, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', transactionType: 'Move', inventoryStatus: 'AVAILABLE', fromInventoryStatus: null, toInventoryStatus: null, locationId: 1102, locationCode: 'A01-R02-L03-B05', fromLocationId: 1102, fromLocationCode: 'A01-R02-L03-B05', toLocationId: 1101, toLocationCode: 'A01-R02-L03-B04', lotId: 8101, lotNumber: 'LOT-ARABICA-2609', expiryDate: '2027-09-01T00:00:00Z', serialId: null, serialNumber: null, quantity: 24, referenceId: 9705, referenceType: 'InventoryMove', transactionDate: '2026-10-07T10:00:00Z', createdBy: 1, createdByName: 'Lê Thu Hà', note: 'Bút toán đảo giao dịch 4' },
  { id: 6, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', transactionType: 'Reversal', inventoryStatus: 'AVAILABLE', fromInventoryStatus: null, toInventoryStatus: null, locationId: 1101, locationCode: 'A01-R02-L03-B04', fromLocationId: 1102, fromLocationCode: 'A01-R02-L03-B05', toLocationId: 1101, toLocationCode: 'A01-R02-L03-B04', lotId: 8101, lotNumber: 'LOT-ARABICA-2609', expiryDate: '2027-09-01T00:00:00Z', serialId: null, serialNumber: null, quantity: 24, referenceId: 4, correctiveTransactionId: 5, referenceType: 'InventoryReversal', transactionDate: '2026-10-07T10:00:01Z', createdBy: 1, createdByName: 'Trần Quốc Bảo', note: 'Đảo giao dịch 4 về vị trí gốc' },
];

export const demoInOut = demoInventoryStocks.map((stock) => ({
  productId: stock.productId,
  productCode: stock.productCode,
  productName: stock.productName,
  unitName: stock.unitName,
  warehouseId: stock.warehouseId,
  warehouseName: stock.warehouseName,
  openingQuantity: Math.max(0, stock.currentQuantity - 36),
  inQuantity: 60,
  outQuantity: 24,
  closingQuantity: stock.currentQuantity,
}));

export const demoApprovalQueue = [
  { documentType: 'ImportReceipt', documentId: 1041, documentCode: 'GR-2026-1041', pendingState: 'ReadyToPost', creatorName: 'Lê Thu Hà', requestedAtUtc: '2026-10-03T11:05:00Z', waitingMinutes: 44, slaStatus: 'Warning', warehouseName: 'Kho Trung tâm (WH01)', totalQuantity: 236, canApprove: false, canReject: false },
];

export const demoTransfers = [
  { id: 18, code: 'TRF-2026-0018', sourceWarehouseId: 1, sourceWarehouseName: 'Kho Trung tâm (WH01)', destinationWarehouseId: 2, destinationWarehouseName: 'Kho Phụ trợ (WH02)', status: 'InTransit', draftRevision: 1, note: 'Điều chuyển bổ sung tồn kho', createdBy: 1, createdAt: '2026-10-03T07:30:00Z', dispatchedAt: '2026-10-03T08:00:00Z', details: [{ productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', requestedQuantity: 200, dispatchedQuantity: 200, receivedQuantity: 0, missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 200 }] },
];

export const demoPurchaseOrders = [
  {
    id: 41, externalPoId: 'ERP-PO-4521', sourceSystem: 'ERP', code: 'PO-2026-4521',
    supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'Open',
    orderDate: '2026-10-03T00:00:00Z', expectedDate: '2026-10-06T00:00:00Z',
    currency: 'VND', externalVersion: 'v18', baseOrderedQuantity: 236, rowVersion: null,
    lines: [{ id: 401, externalLineId: '10', lineNo: 1, productId: 101, productCode: 'QA-E2E-001', productName: 'Cà Phê Rang Xay Đặc Sản Tây Nguyên 500g', orderedQuantity: 236, operationUnitCode: 'GOI', baseOrderedQuantity: 236, baseUnitCode: 'GOI', allowedOverReceiptPct: 5, allowedUnderReceiptPct: 0 }],
  },
];

export const demoAsns = [
  {
    id: 51, code: 'ASN-2026-1051', purchaseOrderId: 41, purchaseOrderCode: 'PO-2026-4521',
    supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'InTransit',
    expectedArrivalAtUtc: '2026-10-05T08:00:00Z', baseExpectedQuantity: 236,
    carrierName: 'Vận chuyển Nam Bắc', vehiclePlate: '51C-882.14', note: 'Lô hàng theo PO-2026-4521', rowVersion: null,
    lines: [{ id: 501, purchaseOrderLineId: 401, lineNo: 1, productId: 101, productCode: 'QA-E2E-001', productName: 'Cà Phê Rang Xay Đặc Sản Tây Nguyên 500g', expectedQuantity: 236, operationUnitCode: 'GOI', baseExpectedQuantity: 236, baseUnitCode: 'GOI' }],
  },
];

export const demoPutawayTasks = [
  { id: 3321, receiptCode: 'GR-2026-1048', warehouseName: 'Kho Trung tâm (WH01)', status: 'InProgress', assignedUserId: 101, requiredBaseQuantity: 188, movedBaseQuantity: 100, rowVersion: 'AAAAAAAAPUT1', items: [{ id: 1, productCode: 'QA-LOT-002', productName: 'Hạt Điều Rang Củi Bình Phước 500g (Theo Lô)', inventoryStatus: 'Available', sourceLocationCode: 'RECEIVING', operationUnitCode: 'HOP', baseUnitCode: 'HOP', requiredOperationQuantity: 188, requiredBaseQuantity: 188, movedBaseQuantity: 100, remainingBaseQuantity: 88 }] },
];

export const demoImportReceipts = [
  { id: 1049, code: 'PNK-202611-001', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'Received', note: 'PO-2026-4521', createdBy: 101, createdByName: 'Lê Thu Hà', createdAt: '2026-11-03T09:00:00Z', approvedBy: 0, approvedByName: '', approvedAt: '', supplierId: 1, supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên', subTotalAmount: 5_000_000, taxAmount: 500_000, totalAmount: 5_500_000, requiresQc: false, details: [{ id: 1, productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', operationUnitId: 5, operationUnitCode: 'THUNG', baseUnitCode: 'THUNG', conversionFactor: 1, conversionVersion: 1, expectedQuantity: 20, receivedQuantity: 20, acceptedQuantity: 20, damagedQuantity: 0, rejectedQuantity: 0, postedQuantity: 0, baseExpectedQuantity: 20, baseReceivedQuantity: 20, baseAcceptedQuantity: 20, baseDamagedQuantity: 0, baseRejectedQuantity: 0, basePostedQuantity: 0, trackingType: 'None', expiryControl: false, shelfLifeDays: null, unitPrice: 250_000, taxPercent: 10, lineSubTotal: 5_000_000, lineTaxAmount: 500_000, lineTotalAmount: 5_500_000, note: '', requiresQc: false, qcState: 'NotRequired', observedQuantity: 20, doorRejectedQuantity: 0, finalReceivedQuantity: 20, baseFinalReceivedQuantity: 20 }] },
  { id: 1048, code: 'GR-2026-1048', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'Received', note: 'PO-2026-4521', createdBy: 101, createdByName: 'Lê Thu Hà', createdAt: '2026-10-03T08:00:00Z', approvedBy: 0, approvedByName: '', approvedAt: '', supplierId: 1, supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên', subTotalAmount: 59_000_000, taxAmount: 5_900_000, totalAmount: 64_900_000, requiresQc: false, details: [{ id: 1, productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', operationUnitId: 5, operationUnitCode: 'THUNG', baseUnitCode: 'THUNG', conversionFactor: 1, conversionVersion: 1, expectedQuantity: 236, receivedQuantity: 236, acceptedQuantity: 236, damagedQuantity: 0, rejectedQuantity: 0, postedQuantity: 0, baseExpectedQuantity: 236, baseReceivedQuantity: 236, baseAcceptedQuantity: 236, baseDamagedQuantity: 0, baseRejectedQuantity: 0, basePostedQuantity: 0, trackingType: 'None', expiryControl: false, shelfLifeDays: null, unitPrice: 250_000, taxPercent: 10, lineSubTotal: 59_000_000, lineTaxAmount: 5_900_000, lineTotalAmount: 64_900_000, note: '', requiresQc: false, qcState: 'NotRequired', observedQuantity: 236, doorRejectedQuantity: 0, finalReceivedQuantity: 236, baseFinalReceivedQuantity: 236 }] },
];

export const demoImportReceiptInventoryIdentities: Record<number, Array<Record<string, unknown>>> = {
  1049: [{ id: 50102, lineId: 1, productId: 101, targetStatus: 'AVAILABLE', baseQuantity: 20, lotNumber: null, manufactureDate: null, expiryDate: null, serialNumber: null }],
  1048: [{ id: 50101, lineId: 1, productId: 101, targetStatus: 'AVAILABLE', baseQuantity: 236, lotNumber: null, manufactureDate: null, expiryDate: null, serialNumber: null }],
};

export const demoExportReceipts = [
  { id: 5108, code: 'EX-2026-5108', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'Approved', note: 'Chờ xuất kho', createdBy: 101, createdByName: 'Trần Quốc Bảo', createdAt: '2026-10-03T08:40:00Z', approvedByName: 'Nguyễn Minh Anh', approvedAt: '2026-10-03T09:00:00Z', dispatchMode: 'ReserveThenDispatch', reservationStatus: 'Active', allowPerReceiptDispatchMode: true, allowWarehouseStaffDirectDispatch: false, writeEnabled: false, customerId: 3, customerCode: 'CUS-001', customerName: 'Chuỗi bán lẻ Minh Phúc', details: [{ id: 1, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', quantity: 16, unitPrice: 360_000, note: '' }] },
];

export const demoReservations = [
  { id: 1, reservationCode: 'RSV-2026-9031', productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', quantity: 9, consumedQuantity: 0, releasedQuantity: 0, allocatedQuantity: 9, remainingQuantity: 9, allocatableQuantity: 0, status: 'Allocated', sourceType: 'SalesOrder', sourceCode: 'SO-202611-001', createdAt: '2026-11-01T08:45:00Z', expiresAt: '2026-11-06T08:45:00Z' },
];

export const demoAllocatableReservations = [
  { reservationId: 1, reservationCode: 'RSV-2026-9031', sourceType: 'SalesOrder', sourceCode: 'SO-202611-001', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', reservedQuantity: 9, allocatedQuantity: 0, allocatableQuantity: 9, expiresAt: '2026-11-06T08:45:00Z' },
];

export const demoAllocationCandidates = [
  { reservationId: 1, locationId: 1101, locationCode: 'A01-R02-L03-B04', locationName: 'Ô lưu trữ chính B04', reservedQuantity: 9, allocatedQuantity: 0, allocatableQuantity: 9, rank: 1, reason: 'Ưu tiên vị trí nhặt hàng chính theo FEFO.' },
];

export const demoAllocations = [
  { id: 7711, allocationCode: 'ALC-2026-7711', reservationId: 1, reservationCode: 'RSV-2026-9031', sourceType: 'SalesOrder', sourceId: 88001, sourceCode: 'SO-202611-001', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', locationId: 1101, locationCode: 'A01-R02-L03-B04', locationName: 'Ô lưu trữ chính B04', inventoryStatus: 'Available', quantity: 9, status: 'Active', strategy: 'FEFO', selectionReason: 'Phân bổ tự động theo FEFO.', allocatedAt: '2026-11-01T08:47:00Z', releasedAt: null, releaseReason: null },
];

export const demoPickingTasks = [
  {
    id: 8801, taskCode: 'PICK-2026-8801', sourceType: 'SalesOrder', sourceId: 88001, sourceCode: 'SO-202611-001',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', pickingType: 'STANDARD', status: 'InProgress', priority: 0,
    assignedUserId: 103, assignedUserName: 'Lê Thu Hà', requestedQuantity: 9, pickedQuantity: 4, remainingQuantity: 5,
    createdAt: '2026-11-02T01:00:00Z', startedAt: '2026-11-02T01:05:00Z', completedAt: null, rowVersion: 'AAAAAAAAPICK1',
    lines: [{ id: 8811, allocationId: 7711, allocationCode: 'ALC-2026-7711', productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', sourceLocationId: 1101, sourceLocationCode: 'A01-R02-L03-B04', sourceLocationName: 'Ô lưu trữ chính B04', requestedQuantity: 9, pickedQuantity: 4, remainingQuantity: 5, sequence: 1, status: 'InProgress' }],
    shortPicks: [],
  },
];

export const demoPackingSessions = [
  {
    id: 9901, sessionCode: 'PACK-2026-9901', pickingTaskId: 8801, pickingTaskCode: 'PICK-2026-8801',
    sourceType: 'SalesOrder', sourceId: 88001, sourceCode: 'SO-202611-001',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'Packed',
    requiredQuantity: 9, packedQuantity: 9, remainingQuantity: 0, handlingUnitCount: 1,
    createdAt: '2026-11-02T01:51:00Z', startedAt: '2026-11-02T01:52:00Z', packedAt: '2026-11-02T02:05:00Z', closedAt: null,
    rowVersion: 'AAAAAAAAPACK1',
    lines: [{ pickingTaskLineId: 8811, productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', pickedQuantity: 9, packedQuantity: 9, remainingQuantity: 0 }],
    handlingUnits: [
      {
        id: 9911, huCode: 'CARTON-9901-01', barcode: 'CARTON-9901-01', sscc: null,
        warehouseId: 1, packingSessionId: 9901, parentHandlingUnitId: null, parentHandlingUnitCode: null,
        type: 'Carton', status: 'Staged', grossWeightKg: 8.4, netWeightKg: 8.0, volumeM3: 0.04,
        sealedAt: '2026-11-02T02:04:00Z', createdAt: '2026-11-02T01:52:00Z', closedAt: '2026-11-02T02:04:00Z', rowVersion: 'AAAAAAAAHU01',
        contents: [{ id: 9921, pickingTaskLineId: 8811, productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', quantity: 9, packedAt: '2026-11-02T02:03:00Z' }],
      },
    ],
  },
];

export const demoHandlingUnits = demoPackingSessions.flatMap((s) => s.handlingUnits);

export const demoShipments = [
  {
    id: 7701, shipmentCode: 'SHIP-2026-7701', packingSessionId: 9901, packingSessionCode: 'PACK-2026-9901',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', sourceType: 'SalesOrder', sourceId: 88001, sourceCode: 'SO-202611-001',
    status: 'Staging', stagingLocationId: 1701, stagingLocationCode: 'STG-OUT-01', handlingUnitCount: 1, loadedHandlingUnitCount: 0,
    dockAppointmentId: null, dockAppointmentCode: null, dockId: null, dockCode: null,
    vehiclePlate: '50H-220.18', trailerPlate: null, sealNumber: 'SEAL-7701',
    createdAt: '2026-11-02T02:06:00Z', stagedAt: '2026-11-02T02:08:00Z', loadingStartedAt: null, loadedAt: null,
    dispatchedAt: null, dispatchedBy: null, dispatchedByName: null, inTransitAt: null, deliveryFailedAt: null,
    returnInitiatedAt: null, deliveredAt: null, completedAt: null, proofOfDelivery: null, trackingEvents: [],
    rowVersion: 'AAAAAAAASHIP1',
    handlingUnits: [{ id: 7711, handlingUnitId: 9911, huCode: 'CARTON-9901-01', barcode: 'CARTON-9901-01', sscc: null, type: 'Carton', status: 'Staged', sequence: 1, contentQuantity: 9, assignedAt: '2026-11-02T02:06:00Z', stagedAt: '2026-11-02T02:08:00Z', loadedAt: null }],
  },
];

export const demoSalesOrders = [
  {
    id: 88001, orderCode: 'SO-202611-001', externalOrderId: 'B2B-SO-202611-001',
    customerId: 3, customerCode: 'CUS-001', customerName: 'Chuỗi bán lẻ Minh Phúc',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', requestedShipDate: '2026-11-05T00:00:00Z',
    priority: 10, shippingMethod: 'Standard', status: 'Released',
    orderedQuantity: 9, reservedQuantity: 9, allocatedQuantity: 9, pickedQuantity: 4,
    shippedQuantity: 0, backorderQuantity: 0, cancelledQuantity: 0, openQuantity: 9,
    createdAt: '2026-11-01T04:00:00Z', releasedAt: '2026-11-01T04:05:00Z', rowVersion: 'AAAAAAAASO01',
    lines: [{ id: 88101, externalLineId: '1', productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', uomCode: 'THUNG', orderedQuantity: 9, reservedQuantity: 9, allocatedQuantity: 9, pickedQuantity: 4, shippedQuantity: 0, backorderQuantity: 0, cancelledQuantity: 0, openQuantity: 9 }],
  },
  {
    id: 88002, orderCode: 'SO-202611-002', externalOrderId: 'B2B-SO-202611-002',
    customerId: 4, customerCode: 'CUS-002', customerName: 'Thương mại An Khang',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', requestedShipDate: '2026-11-06T00:00:00Z',
    priority: 20, shippingMethod: 'Express', status: 'Released',
    orderedQuantity: 12, reservedQuantity: 0, allocatedQuantity: 0, pickedQuantity: 0,
    shippedQuantity: 0, backorderQuantity: 0, cancelledQuantity: 0, openQuantity: 12,
    createdAt: '2026-11-02T02:00:00Z', releasedAt: '2026-11-02T02:05:00Z', rowVersion: 'AAAAAAAASO02',
    lines: [{ id: 88102, externalLineId: '1', productId: 102, productCode: 'QA-LOT-002', productName: 'Hộp cảm biến nhiệt độ QA-02', uomCode: 'THUNG', orderedQuantity: 12, reservedQuantity: 0, allocatedQuantity: 0, pickedQuantity: 0, shippedQuantity: 0, backorderQuantity: 0, cancelledQuantity: 0, openQuantity: 12 }],
  },
];

export const canonicalBackorders: Array<Record<string, unknown>> = [];
export const demoBackorders = canonicalBackorders;

export const demoStocktakes = [
  { id: 142, code: 'CC-2026-0142', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 0, note: 'Kiểm kê định kỳ Khu A', createdBy: 101, createdByName: 'Trần Quốc Bảo', createdAt: '2026-10-03T08:00:00Z', detailCount: 1, details: [{ id: 1, stocktakeId: 142, productId: 101, productCode: 'QA-E2E-001', productName: 'Cà Phê Rang Xay Đặc Sản Tây Nguyên 500g', unitName: 'Gói', systemQuantity: 236, actualQuantity: 236, differenceQuantity: 0, note: 'Khớp 100%' }] },
];

export const demoWarehouseCalendars = demoWarehouses.map((warehouse) => ({
  warehouseId: warehouse.id,
  warehouseCode: warehouse.code,
  warehouseName: warehouse.name,
  isConfigured: true,
  timeZoneId: 'Asia/Ho_Chi_Minh',
  localNow: '2026-11-03T10:30:00+07:00',
  isOpenNow: true,
  currentShiftCode: 'CA-1',
  rowVersion: null,
  days: [
    { dayOfWeek: 0, dayName: 'Chủ nhật', isOpen: false, opensAtLocal: null, closesAtLocal: null, inboundCutoffLocal: null, outboundCutoffLocal: null, overnight: false },
    { dayOfWeek: 1, dayName: 'Thứ hai', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '20:00', outboundCutoffLocal: '19:30', overnight: false },
    { dayOfWeek: 2, dayName: 'Thứ ba', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '20:00', outboundCutoffLocal: '19:30', overnight: false },
    { dayOfWeek: 3, dayName: 'Thứ tư', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '20:00', outboundCutoffLocal: '19:30', overnight: false },
    { dayOfWeek: 4, dayName: 'Thứ năm', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '20:00', outboundCutoffLocal: '19:30', overnight: false },
    { dayOfWeek: 5, dayName: 'Thứ sáu', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '20:00', outboundCutoffLocal: '19:30', overnight: false },
    { dayOfWeek: 6, dayName: 'Thứ bảy', isOpen: true, opensAtLocal: '07:00', closesAtLocal: '17:00', inboundCutoffLocal: '15:00', outboundCutoffLocal: '14:30', overnight: false },
  ],
  shifts: [
    { id: warehouse.id * 10 + 1, warehouseId: warehouse.id, code: 'CA-1', name: 'Ca sáng', startTimeLocal: '06:00', endTimeLocal: '14:00', overnight: false, breakMinutes: 30, plannedHeadcount: 18, inboundPalletsPerHour: 24, outboundOrdersPerHour: 40, dockSlots: 6, laborHours: 126, stagingCapacity: 180, packingStations: 8, equipmentAvailable: 12, isActive: true, rowVersion: null },
    { id: warehouse.id * 10 + 2, warehouseId: warehouse.id, code: 'CA-2', name: 'Ca chiều', startTimeLocal: '14:00', endTimeLocal: '22:00', overnight: false, breakMinutes: 30, plannedHeadcount: 16, inboundPalletsPerHour: 20, outboundOrdersPerHour: 36, dockSlots: 5, laborHours: 112, stagingCapacity: 150, packingStations: 7, equipmentAvailable: 10, isActive: true, rowVersion: null },
  ],
}));

export const demoDockYardWarehouses = demoWarehouses.map((warehouse) => ({
  id: warehouse.id,
  code: warehouse.code,
  name: warehouse.name,
  timeZoneId: 'Asia/Ho_Chi_Minh',
  calendarConfigured: true,
}));

export const demoDocks = demoWarehouses.flatMap((warehouse, idx) => [
  { id: warehouse.id * 100 + 1, warehouseId: warehouse.id, code: 'D-01', name: 'Cửa nhập hàng 01', supportsInbound: true, supportsOutbound: false, allowedVehicleType: 'TRUCK', isTemperatureControlled: false, hazardAllowed: false, isActive: true, rowVersion: null },
  { id: warehouse.id * 100 + 2, warehouseId: warehouse.id, code: 'D-02', name: 'Cửa linh hoạt 02', supportsInbound: true, supportsOutbound: true, allowedVehicleType: null, isTemperatureControlled: idx === 0, hazardAllowed: false, isActive: true, rowVersion: null },
]);

export const demoYardSlots = demoWarehouses.flatMap((warehouse) => [
  { id: warehouse.id * 100 + 51, warehouseId: warehouse.id, code: 'Y-01', name: 'Bãi chờ 01', isActive: true, occupied: false, occupiedByAppointmentCode: null, rowVersion: null },
  { id: warehouse.id * 100 + 52, warehouseId: warehouse.id, code: 'Y-02', name: 'Bãi chờ 02', isActive: true, occupied: warehouse.id === 1, occupiedByAppointmentCode: warehouse.id === 1 ? 'APT-2026-1042' : null, rowVersion: null },
]);

export const demoDockAppointments = [
  {
    id: 1042, warehouseId: 1, warehouseCode: 'WH01', warehouseName: 'Kho Trung tâm (WH01)',
    code: 'APT-2026-1042', direction: 0, status: 3, plannedStartUtc: '2026-10-05T09:00:00Z', plannedEndUtc: '2026-10-05T10:00:00Z',
    carrierCode: 'LOG-001', carrierName: 'Vận chuyển Nam Bắc', vehiclePlate: '51C-882.14', trailerPlate: null, vehicleType: 'TRUCK',
    requiresTemperatureControl: false, hazardous: false, driverName: 'Nguyễn Văn Minh', driverPhone: '0900000001', sealNumber: 'SEAL-1042',
    yardSlotId: 152, yardSlotCode: 'Y-02', dockId: null, dockCode: null, arrivedAtUtc: '2026-10-05T08:46:00Z', checkedInAtUtc: '2026-10-05T08:52:00Z',
    dockAssignedAtUtc: null, serviceStartedAtUtc: null, serviceCompletedAtUtc: null, checkedOutAtUtc: null, exceptionCode: null,
    note: 'Chờ gọi vào cửa nhập', rowVersion: null,
    events: [
      { id: 1, eventType: 'Confirmed', eventAtUtc: '2026-10-04T09:00:00Z', actorUserId: 101, dockId: null, yardSlotId: null, note: null },
      { id: 2, eventType: 'CheckedIn', eventAtUtc: '2026-10-05T08:52:00Z', actorUserId: 101, dockId: null, yardSlotId: 152, note: 'Đã kiểm tra cổng an ninh' },
    ],
  },
];

export const demoReconciliationWarehouses = demoWarehouses.map((w) => ({
  id: w.id,
  name: w.name,
  isActive: w.isActive,
}));

export const canonicalReconciliationRows: DemoReconciliationRow[] = [
  {
    productId: 101, productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    currentQuantity: 162, expectedQuantity: 162, difference: 0,
    importQuantity: 186, exportQuantity: 24, transferInQuantity: 0, transferOutQuantity: 0,
    adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0, status: 'Match',
  },
  {
    productId: 102, productCode: 'QA-LOT-002', productName: 'Hộp cảm biến nhiệt độ QA-02',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    currentQuantity: 186, expectedQuantity: 186, difference: 0,
    importQuantity: 206, exportQuantity: 20, transferInQuantity: 0, transferOutQuantity: 0,
    adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0, status: 'Match',
  },
  {
    productId: 103, productCode: 'QA-PAPER', productName: 'QA Giấy A4',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    currentQuantity: 40, expectedQuantity: 40, difference: 0,
    importQuantity: 40, exportQuantity: 0, transferInQuantity: 0, transferOutQuantity: 0,
    adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0, status: 'Match',
  },
];

// Preserved backward-compatible rows for read-only demo banner tests on InventoryReconciliation
export const demoReconciliationRows: DemoReconciliationRow[] = [
  {
    productId: 1001, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    currentQuantity: 1350, expectedQuantity: 1350, difference: 0,
    importQuantity: 1800, exportQuantity: 420, transferInQuantity: 40, transferOutQuantity: 70,
    adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0, status: 'Match',
  },
  {
    productId: 1008, productCode: 'SKU-1008', productName: 'Sữa hộp 180ml',
    warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)',
    currentQuantity: 642, expectedQuantity: 640, difference: 2,
    importQuantity: 900, exportQuantity: 250, transferInQuantity: 0, transferOutQuantity: 10,
    adjustmentIncreaseQuantity: 0, adjustmentDecreaseQuantity: 0, status: 'Mismatch',
  },
];

const snapshotAt = '2026-11-01T08:00:00Z';
const rec = (
  id: string,
  reference: string,
  type: string,
  status: string,
  tone: MockTone,
  warehouse: string,
  subject: string,
  owner: string,
  priority: MockOperationalRecord['priority'],
  updatedAt: string,
  extra: Partial<MockOperationalRecord> = {},
): MockOperationalRecord => ({
  id, reference, type, status, tone, warehouse, subject, owner, priority, updatedAt, ...extra,
});

const WORK_CENTER_KEYS = [
  'overview', 'master-data', 'warehouse-structure', 'inbound', 'outbound',
  'inventory-control', 'transfer-replenishment', 'count-adjustment', 'quality-returns',
  'handling-packaging', 'dock-yard-crossdock', 'reports-analytics', 'administration',
  'integration', 'mobile', 'operations-resilience', 'advanced-planning',
] as const;

export const mockWorkCenters: Record<string, MockWorkCenterData> = Object.fromEntries(
  WORK_CENTER_KEYS.map((key, wcIdx) => {
    const baseRecords: MockOperationalRecord[] =
      key === 'count-adjustment'
        ? [
            rec('CT-001', 'CC-2026-0142', 'Cycle Count', 'RECOUNT', 'orange', 'WH-HCM-01', 'SKU-1001 • Lần đếm 3 chấp nhận 1.248', 'Trần Quốc Bảo', 'High', '2026-10-02T09:05:00Z', { productCode: 'SKU-1001', quantity: 1248, uom: 'Gói' }),
            rec('CT-002', 'CC-2026-0143', 'Cycle Count', 'REVIEW', 'orange', 'WH-HCM-01', 'SKU-2001 • Tồn sổ sách 84 / thực đếm 84', 'Trần Quốc Bảo', 'Normal', '2026-10-02T08:56:00Z', { productCode: 'SKU-2001', quantity: 84, uom: 'Cái' }),
            rec('CT-003', 'FC-2026-0021', 'Full Count', 'COUNTING', 'blue', 'WH-DN-01', 'Khu C • kiểm đếm mù', 'Đỗ Minh Khang', 'High', '2026-10-02T09:18:00Z'),
            rec('CT-004', 'ADJ-2026-0088', 'Adjustment', 'PENDING_APPROVAL', 'orange', 'WH-HCM-01', 'Chênh lệch -2 SKU-1001 chờ duyệt SoD', 'Trần Quốc Bảo', 'Critical', '2026-10-02T09:08:00Z', { productCode: 'SKU-1001', quantity: 2, uom: 'Gói' }),
          ]
        : [1, 2, 3, 4].map((n) =>
            rec(
              `WC-${String(wcIdx + 1).padStart(2, '0')}-${n}`,
              `REF-${wcIdx + 1}-${100 + n}`,
              key,
              n === 4 ? 'READY' : 'ACTIVE',
              n === 1 ? 'green' : 'blue',
              n % 2 === 0 ? 'WH-DN-01' : 'WH-HCM-01',
              `Bản ghi chuẩn ${key} #${n}`,
              'Nguyễn Minh Anh',
              n === 1 ? 'High' : 'Normal',
              snapshotAt,
              { productCode: 'SKU-1001', partnerCode: 'SUP-001', quantity: n * 10, uom: 'Gói' },
            ),
          );
    return [key, { moduleKey: key, snapshotAt, records: baseRecords }];
  }),
);

export const getMockWorkCenter = (moduleKey?: string): MockWorkCenterData | undefined =>
  moduleKey ? mockWorkCenters[moduleKey] : undefined;

export const mockRecordCount = Object.values(mockWorkCenters).reduce(
  (total, wc) => total + wc.records.length,
  0,
);

// ============================================================================
// 4. PURE STATELESS RESOLVER FOR DEMO / OFFLINE ADAPTER
// ============================================================================

const normalizeMockPath = (url?: string): string => {
  const raw = (url ?? '').replace(/^https?:\/\/[^/]+/i, '');
  const base = raw.split('?')[0].replace(/\/+$/, '').toLowerCase().replace(/^\/api\/v1\//, '/api/');
  return base
    .replace(/^\/api\/import-receipts(\/|$)/, '/api/importreceipts$1')
    .replace(/^\/api\/export-receipts(\/|$)/, '/api/exportreceipts$1')
    .replace(/^\/api\/inventory-stocks(\/|$)/, '/api/inventorystocks$1')
    .replace(/^\/api\/inventory-transactions(\/|$)/, '/api/inventorytransactions$1')
    .replace(/^\/api\/inventory-reconciliation(\/|$)/, '/api/inventoryreconciliation$1')
    .replace(/^\/api\/product-categories(\/|$)/, '/api/productcategories$1');
};

const toSearchParams = (url?: string, params?: Record<string, unknown>): URLSearchParams => {
  const sp = new URLSearchParams((url ?? '').split('?')[1] ?? '');
  for (const [key, value] of Object.entries(params ?? {})) {
    if (value !== undefined && value !== null && value !== '') {
      sp.set(key, String(value));
    }
  }
  return sp;
};

const pagedResult = <T,>(items: T[], page: number, pageSize: number) => {
  const safePage = Math.max(1, page || 1);
  const safeSize = Math.max(1, pageSize || 20);
  const start = (safePage - 1) * safeSize;
  return {
    items: items.slice(start, start + safeSize),
    totalRecords: items.length,
    totalCount: items.length,
    pageIndex: safePage,
    pageSize: safeSize,
    totalPages: Math.ceil(items.length / safeSize),
  };
};

const extractHeader = (headers: Record<string, unknown> | undefined, name: string): string => {
  if (!headers) return '';
  const target = name.toLowerCase();
  for (const [k, v] of Object.entries(headers)) {
    if (k.toLowerCase() === target && v !== undefined && v !== null) return String(v);
  }
  return '';
};

const resolveRoleContext = (headers?: Record<string, unknown>): {
  profile: CanonicalRoleProfile;
  explicitRoleHeader: boolean;
  previewRole: string;
} => {
  const previewRole = extractHeader(headers, 'x-platform-role-preview');
  const mockRoleHeader = extractHeader(headers, 'x-mock-role');
  const authHeader = extractHeader(headers, 'authorization');
  const bearerRoleMatch = authHeader.match(/^Bearer\s+mock-role:([A-Za-z]+)/i);
  const candidate = (mockRoleHeader || bearerRoleMatch?.[1] || '').trim();
  if (candidate && candidate in CANONICAL_ROLE_PROFILES) {
    return {
      profile: CANONICAL_ROLE_PROFILES[candidate as CanonicalRoleName],
      explicitRoleHeader: true,
      previewRole,
    };
  }
  return {
    profile: CANONICAL_ROLE_PROFILES.Viewer,
    explicitRoleHeader: false,
    previewRole,
  };
};

const parseBody = (data: unknown): Record<string, unknown> => {
  if (!data) return {};
  if (typeof data === 'string') {
    try {
      return JSON.parse(data) as Record<string, unknown>;
    } catch {
      return {};
    }
  }
  return typeof data === 'object' ? (data as Record<string, unknown>) : {};
};

const findNumericId = (path: string): number => Number(path.match(/\/(\d+)(?:\/|$)/)?.[1] ?? 0);

const maskCostRow = <T extends Record<string, unknown>>(row: T, canReadCost: boolean): T => {
  if (canReadCost) return { ...row, costHiddenByRbac: false };
  return {
    ...row,
    unitCost: 0,
    averageCost: 0,
    movingAverageCost: 0,
    totalValue: 0,
    inventoryValue: 0,
    costHiddenByRbac: true,
  };
};

const POST_DIAGNOSTIC_RESPONSES: Record<string, unknown> = {
  '/api/warehouses/verify-topology-capacity': { verificationId: 'TOP-CAP-DEMO-01', zoneBinHierarchyStatus: 'CANONICAL_VALIDATED', capacityGuardrailActive: true },
  '/api/warehouses/verify-virtual-locations': { verificationId: 'VLOC-AUD-DEMO', status: 'ALL_9_VIRTUAL_LOCATIONS_READY' },
  '/api/warehouses/evaluate-network-mhe': { evaluationId: 'NET-MHE-DEMO-01', networkTopologyMode: 'HUB_AND_SPOKE_BALANCED', capacityForecastStatus: 'OPTIMAL_HEADROOM_82PCT', mheFleetReadiness: 'FORKLIFT_CONVEYOR_SORTER_READY' },
  '/api/reports/sync-bi-snapshot': { snapshotId: 'BI-SNAP-DEMO', status: 'BI_HEATMAP_SYNCED' },
  '/api/reports/operational-exceptions/acknowledge': { status: 'ACKNOWLEDGED' },
  '/api/units/verify-uom-integrity': { verificationId: 'UOM-INT-DEMO-01', status: 'CANONICAL_UOM_VERIFIED' },
  '/api/units/verify-catch-weight-coldchain': { verificationId: 'CW-CC-HAZ-DEMO', status: 'SPECIALIZED_INVENTORY_RULES_VERIFIED' },
  '/api/products/auto-audit-governance': { auditId: 'PRD-GOV-DEMO', status: 'MASTER_DATA_GOVERNANCE_VERIFIED' },
  '/api/productcategories/verify-storage-policy': { verificationId: 'CAT-POL-DEMO', status: 'STORAGE_TEMP_HAZARD_FEFO_VERIFIED' },
  '/api/business-partners/calculate-3pl-billing': { statementId: '3PL-BILL-DEMO', status: '3PL_BILLING_RECONCILED' },
  '/api/purchase-orders/expedite-overdue': { expediteId: 'PO-EXP-DEMO', vendorNotificationStatus: 'DISPATCHED_TO_SUPPLIER' },
  '/api/purchase-orders/generate-procurement-recommendations': { recommendationId: 'PROC-REC-DEMO', status: 'PROCUREMENT_INTELLIGENCE_READY' },
  '/api/asns/pre-allocate-staging': { stagingReservationId: 'ASN-STG-DEMO', stagingLaneCode: 'STAGE-IN-01' },
  '/api/dock-yard/dispatch-crossdock': { dispatchId: 'XDOCK-DEMO-01', dockDoorCode: 'D-02', stagedHuCount: 4, status: 'CROSSDOCK_ROUTED' },
  '/api/importreceipts/initiate-rma-inspection': { rmaInspectionCode: 'RMA-QC-DEMO', rmaCode: 'RMA-QC-DEMO', status: 'INBOUND_QC_INSPECTION' },
  '/api/importreceipts/rma-intake-check': { rmaInspectionCode: 'RMA-QC-DEMO', rmaCode: 'RMA-QC-DEMO', status: 'INBOUND_QC_INSPECTION' },
  '/api/importreceipts/execute-rtv-refurbish': { dispositionCode: 'RTV-REF-DEMO', executionId: 'RTV-REF-DEMO', status: 'REFURBISH_AND_RTV_SPLIT' },
  '/api/putaway-tasks/evaluate-slotting': { evaluationId: 'SLOT-EVAL-DEMO', optimalBinMatchRatePct: 96.4, fefoHazardCompliance: 'VERIFIED' },
  '/api/exportreceipts/verify-dispatch-cutoff': { verificationId: 'EXP-CUT-DEMO', cutoffComplianceStatus: 'DISPATCH_CUTOFF_VERIFIED' },
  '/api/sales-orders/expedite-backorder': { expediteId: 'EXP-BO-DEMO', orderNumber: 'SO-202611-001', allocationWaveQueued: true },
  '/api/sales-orders/evaluate-atp-routing': { evaluationId: 'ATP-ROUTE-DEMO', status: 'ATP_ROUTING_FAIRNESS_READY' },
  '/api/stock-reservations/sweep-expired-preview': { sweepId: 'RSV-SWP-DEMO', status: 'COMMITMENT_TTL_VERIFIED' },
  '/api/inventory/allocations/verify-fefo-compliance': { verificationId: 'ALC-FEFO-DEMO', fefoComplianceStatus: 'STRICT_FEFO_FIFO_ALIGNED' },
  '/api/picking-tasks/mobile-scan-verify': { verified: true, status: 'SCAN_VERIFIED' },
  '/api/picking-tasks/sync-automation-telemetry': { telemetrySyncId: 'WCS-IOT-DEMO', wcsRoboticsState: 'AMR_ASRS_CONVEYOR_SYNCHRONIZED' },
  '/api/packing-sessions/verify-packaging-profile': { verificationId: 'PKG-VER-DEMO', cartonType: 'STD-CARTON-M', gs1SsccReady: true },
  '/api/packing-sessions/execute-kitting-vas': { workOrderId: 'KWO-2026-001', status: 'KITTING_VAS_READY', vasServiceType: 'LABELING_SHRINKWRAP_BUNDLE' },
  '/api/shipments/sync-tms-manifest': { manifestId: 'TMS-MNF-DEMO', carrierCode: 'GHN', syncedShipmentCount: 1, ediStatus: 'ACK_200_SYNCED' },
  '/api/shipments/carrier-configs/ghn/verify-and-sync': { carrierCode: 'GHN', gatewayMode: 'LIVE_GHN_DEV_SANDBOX', isSandbox: true, shopId: '235512', status: 'GHN_LIVE_SANDBOX_VERIFIED' },
  '/api/shipments/calculate-carrier-fee': { carrierCode: 'GHN', gatewayMode: 'LIVE_GHN_DEV_SANDBOX', shopId: '235512', totalFee: 22_000, serviceFee: 22_000, insuranceFee: 0 },
  '/api/handling-units/verify-intransit-seal': { sealVerificationId: 'HU-SEAL-DEMO', sealStatus: 'SEAL_INTACT_VERIFIED', transferCode: 'TRF-2026-0018' },
  '/api/stock-transfers/simulate-network-rebalancing': { simulationId: 'NET-BAL-DEMO', status: 'NETWORK_REBALANCING_SIMULATED' },
  '/api/stocktakes/adjustment-proposals': { proposalCode: 'ADJ-DEMO-001', status: 'PENDING_SOD_APPROVAL' },
  '/api/inventory/verify-bucket-invariants': { verificationId: 'BCK-INV-DEMO', warehouseId: 1, fiveBucketEquationStatus: 'BALANCED_EXACT', scrapApprovalGuard: 'SOD_ENFORCED' },
  '/api/inventory/verify-lock-precedence': { verificationId: 'LCK-PRC-DEMO', warehouseId: 1, overlappingHoldPrecedence: 'RECALL_GT_QC_GT_COUNT', leakCount: 0, status: 'LOCK_PRECEDENCE_VERIFIED' },
  '/api/inventory/reversals/verify-chain-integrity': { verificationId: 'REV-INT-DEMO', warehouseId: 1, duplicateReversalBlocked: true, appendOnlyLedgerStatus: 'IMMUTABLE_VERIFIED' },
  '/api/inventory/traceability/simulate-recall-tree': { simulationId: 'TRC-RCL-DEMO', query: 'LOT-ARABICA-2609', upstreamPoLinked: true, downstreamShipmentLinked: true, recallContainmentReady: true },
  '/api/inventory/what-if-simulation': { demandMultiplier: 1.5, totalAvailable: 396, simulatedDemand: 594, projectedNetBalance: -198, scenarioStatus: 'PROJECTED_SHORTAGE', immutableSnapshotGuarantee: true },
  '/api/inventory/run-what-if-simulation': { demandMultiplier: 1.5, totalAvailable: 396, simulatedDemand: 594, projectedNetBalance: -198, scenarioStatus: 'PROJECTED_SHORTAGE', immutableSnapshotGuarantee: true },
  '/api/inventory/evaluate-decision-policy-governance': { policyRegistryId: 'DEC-POL-DEMO', status: 'DECISION_POLICY_GOVERNANCE_VERIFIED' },
  '/api/approvals/escalate-overdue': { escalationBatchId: 'ESC-SLA-DEMO', escalatedStatus: 'ESCALATED_TO_SUPERVISOR' },
  '/api/auth/register-tenant': { tenantCode: 'TENANT-001', completedSteps: 5, totalSteps: 5, status: 'Đã cấp phát thuê bao & Sơ đồ kho chuẩn' },
  '/api/user-warehouse-access/verify-sod-isolation': { auditId: 'RBAC-SOD-DEMO-01', warehouseId: 1, activeMemberships: 7, crossSiteLeakageDetected: false, status: 'ZERO_PRIVILEGE_ESCALATION_VERIFIED', sodConflictCount: 0, multiWarehouseIsolationEnforced: true },
};

const okRes = (data: unknown): CanonicalMockResolution => ({ status: 200, data });
const errRes = (status: number, errorCode: string, message: string): CanonicalMockResolution => ({
  status,
  errorCode,
  message,
  data: { code: errorCode, errorCode, message },
});

export const resolveCanonicalMockResponse = (
  rawMethod = 'get',
  url = '',
  rawParams?: Record<string, unknown>,
  rawData?: unknown,
  headers?: Record<string, unknown>,
): CanonicalMockResolution => {
  const method = rawMethod.toLowerCase();
  const path = normalizeMockPath(url);
  const params = toSearchParams(url, rawParams);
  const body = parseBody(rawData);
  const { profile, explicitRoleHeader, previewRole } = resolveRoleContext(headers);
  const canReadCost = profile.canReadInventoryCost;

  // 1. Platform Control Center SecurityDomain Isolation (/api/platform/*)
  if (path.startsWith('/api/platform/') || path === '/api/platform') {
    if (profile.securityDomain !== 'PLATFORM') {
      return errRes(403, 'PLATFORM_DOMAIN_FORBIDDEN', 'Vai trò thuộc phạm vi khách hàng không được phép truy cập Trung tâm Điều hành Nền tảng.');
    }
    if (method === 'post' && /^\/api\/platform\/billing\/invoices\/\d+\/reconcile$/.test(path)) {
      if (previewRole === 'SupportReadOnly' || !profile.permissions.includes('platform.billing.reconcile')) {
        return errRes(403, 'BILLING_RECONCILE_FORBIDDEN', 'Vai trò hiện tại không có quyền xác nhận đối soát hóa đơn SaaS.');
      }
      const invoiceId = findNumericId(path);
      if (body.resetToPending === true) {
        return okRes({ id: invoiceId, invoiceId, invoiceCode: 'INV-SAAS-202611-002', status: 'UNPAID', resetToPending: true });
      }
      const bankRef = String(body.bankTransactionRef ?? 'FT2627716888').trim();
      if (bankRef === 'FT2627690123') {
        return errRes(409, 'BILLING_DUPLICATE_BANK_TRANSFER_REF', 'Mã giao dịch ngân hàng FT2627690123 đã được đối soát trước đó.');
      }
      const creditTime = String(body.bankCreditTimestampVi ?? '02/11/2026 18:15');
      return okRes({
        id: invoiceId,
        invoiceId,
        invoiceCode: invoiceId === 1 ? 'INV-SAAS-202610-001' : 'INV-SAAS-202611-002',
        status: 'PAID',
        subTotalVnd: 1_354_545,
        vatAmountVnd: 135_455,
        amountVnd: Number(body.receivedAmountVnd ?? 1_490_000),
        bankTransactionRef: bankRef,
        bankCreditTimestampVi: creditTime,
        reconciliationAuditNote: `Xác nhận thủ công bởi Chủ dự án lúc ${creditTime} (UNC: ${bankRef})`,
      });
    }
    if (method === 'post' && /^\/api\/platform\/tenants\/\d+\/status$/.test(path)) {
      if (!profile.permissions.includes('platform.tenant.suspend')) {
        return errRes(403, 'PLATFORM_TENANT_STATUS_FORBIDDEN', 'Vai trò hiện tại không có quyền thay đổi trạng thái thuê bao.');
      }
      return okRes({ tenantId: findNumericId(path), tenantCode: 'TENANT-001', status: String(body.status ?? 'ACTIVE').toUpperCase() });
    }
    if (method === 'get' && path === '/api/platform/dashboard') {
      return okRes({ activeTenantsCount: 1, suspendedTenantsCount: 0, mrrNetVnd: 1_354_545, grossBillingVnd: 1_490_000, unpaidInvoicesCount: 1, healthyWorkersCount: 4 });
    }
    if (method === 'get' && path === '/api/platform/tenants') {
      return okRes({ items: [canonicalTenantSubscription], totalCount: 1 });
    }
    if (method === 'get' && path === '/api/platform/billing/invoices') {
      return okRes({
        items: canonicalSaasInvoices,
        totalCount: canonicalSaasInvoices.length,
        mrrVnd: 1_354_545,
        arrVnd: 16_254_540,
        beneficiary: canonicalTenantSubscription.beneficiary,
        bankName: canonicalTenantSubscription.beneficiary.bankName,
        bankBin: canonicalTenantSubscription.beneficiary.bankBin,
        accountNo: canonicalTenantSubscription.beneficiary.accountNo,
        accountName: canonicalTenantSubscription.beneficiary.accountName,
        beneficiaryBankName: canonicalTenantSubscription.beneficiary.bankName,
        beneficiaryAccountNumber: canonicalTenantSubscription.beneficiary.accountNo,
        beneficiaryAccountName: canonicalTenantSubscription.beneficiary.accountName,
        displayAccountVi: `${canonicalTenantSubscription.beneficiary.bankName} • BIN ${canonicalTenantSubscription.beneficiary.bankBin} • STK: ${canonicalTenantSubscription.beneficiary.accountNo} (${canonicalTenantSubscription.beneficiary.accountName})`,
      });
    }
    if (method === 'get' && path === '/api/platform/integrations/health') {
      return okRes({
        gatewayStatus: 'HEALTHY',
        statusLabelVi: 'Hoạt động tốt',
        environmentBadgeLabel: 'Môi trường: Sandbox thử nghiệm',
        environmentNoticeVi: 'Thử nghiệm (Không phát sinh cước thật)',
        ghnSandboxShopId: '235512',
        maskedTokenPreview: '0c06****12be',
        fallbackAddressChain: 'Phường Bến Nghé, Quận 1, TP.HCM (1442 / 20102)',
        ghnGatewayHealth: { eventsLast24hSuccess: 24, eventsLast24hFailed: 0, latencyMs: 42, lastCheckedRelativeVi: '15:42:10 (6 giây trước)' },
        shopeeTiktokWebhookStatus: { eventsLast24hSuccess: 31, eventsLast24hFailed: 0, latencyMs: 19, lastCheckedRelativeVi: '15:42:02 (14 giây trước)' },
        vietQrWebhookStatus: {
          status: 'HEALTHY',
          bankCode: canonicalTenantSubscription.beneficiary.bankName,
          bankBin: canonicalTenantSubscription.beneficiary.bankBin,
          accountNo: canonicalTenantSubscription.beneficiary.accountNo,
          accountName: canonicalTenantSubscription.beneficiary.accountName,
          displayAccountVi: `${canonicalTenantSubscription.beneficiary.bankName} • BIN ${canonicalTenantSubscription.beneficiary.bankBin} • STK: ${canonicalTenantSubscription.beneficiary.accountNo} (${canonicalTenantSubscription.beneficiary.accountName})`,
          eventsLast24hSuccess: 12,
          eventsLast24hFailed: 0,
          latencyMs: 27,
          lastCheckedRelativeVi: '15:42:07 (9 giây trước)',
        },
      });
    }
    if (method === 'get' && path === '/api/platform/monitoring/workers') {
      return okRes({
        status: 'HEALTHY',
        totalCount: 3,
        workers: [
          {
            workerName: 'OutboxDispatcherWorker',
            titleVi: 'Tiến trình đồng bộ hóa đơn & sổ cái',
            descriptionVi: 'Đẩy sự kiện hóa đơn điện tử NĐ 70/2025 & bút toán kế toán sang MISA/Viettel và cổng vận chuyển',
            status: 'HEALTHY',
            scope: 'Multi-Tenant Outbox + Multi-Carrier Adapter Dispatch',
            intervalSeconds: 30,
            lastDurationMs: 42,
            lastRunRelativeVi: '8 giây trước',
            pendingOutboxCount: 0,
            deadLetterCount: 0,
            driftCount: 0,
          },
          {
            workerName: 'InventoryConsistencyWorker',
            titleVi: 'Tiến trình đối soát bất biến tồn kho 9 chiều',
            descriptionVi: 'Kiểm toán tự động tổng đại số sổ cái InventoryTransactions và số dư InventoryBalances theo GrainHash SHA-256',
            status: 'HEALTHY',
            scope: '9-Dimension GrainHash Ledger vs Balance Parity',
            intervalSeconds: 300,
            lastDurationMs: 68,
            lastRunRelativeVi: '47 giây trước',
            pendingOutboxCount: 0,
            deadLetterCount: 0,
            driftCount: 0,
          },
          {
            workerName: 'WebhookRetryAndCleanupWorker',
            titleVi: 'Tiến trình thử lại Webhook & dọn dẹp hàng đợi',
            descriptionVi: 'Xử lý lại sự kiện Shopee/TikTok/VietQR bị trễ mạng và thu hồi phiên Impersonation hết hạn TTL',
            status: 'HEALTHY',
            scope: 'IntegrationInboxMessages + Expired Support Sessions',
            intervalSeconds: 60,
            lastDurationMs: 29,
            lastRunRelativeVi: '23 giây trước',
            pendingOutboxCount: 0,
            deadLetterCount: 0,
            driftCount: 0,
          },
        ],
      });
    }
    if (method === 'get' && (path === '/api/platform/audit-logs' || path === '/api/platform/support/impersonation-sessions')) {
      return okRes({ items: [], totalCount: 0, status: 'HEALTHY' });
    }
  }

  // 2. Explicit Role Warehouse Scope & Mutation RBAC Guards
  const requestedWarehouseId = Number(params.get('warehouseId') ?? body.warehouseId ?? 0);
  if (explicitRoleHeader && method !== 'get') {
    if (requestedWarehouseId > 0 && !profile.warehouseScopeIds.includes(requestedWarehouseId)) {
      return errRes(403, 'WAREHOUSE_SCOPE_FORBIDDEN', 'Không có quyền ghi dữ liệu tại kho ngoài phạm vi được cấp.');
    }
    if (profile.roleName === 'Viewer') {
      return errRes(403, 'VIEWER_MUTATION_FORBIDDEN', 'Vai trò Người xem không được phép thực hiện thao tác ghi.');
    }
  }

  // 3. Read-only Demo Diagnostic POST Allowlist
  if (method === 'post') {
    if (path in POST_DIAGNOSTIC_RESPONSES) {
      return okRes(POST_DIAGNOSTIC_RESPONSES[path]);
    }
    if (/^\/api\/warehouses\/\d+\/calendar\/verify-shift-coverage$/.test(path)) return okRes({ verificationId: 'CAL-VER-DEMO-01', shiftCoverageStatus: 'FULL_COVERAGE_VERIFIED' });
    if (/^\/api\/warehouses\/\d+\/calendar\/optimize-labor-interleaving$/.test(path)) return okRes({ optimizationId: 'WLM-INT-DEMO-01', ergonomicSafetyStatus: 'LIFT_LIMIT_AND_FATIGUE_ROTATION_COMPLIANT' });
    if (/^\/api\/business-partners\/\d+\/verify-sla-contract$/.test(path)) return okRes({ partnerId: findNumericId(path), slaContractStatus: 'VERIFIED_ACTIVE' });
    if (/^\/api\/importreceipts\/\d+\/express-post$/.test(path)) return okRes({ receiptId: findNumericId(path), status: 'Posted', expressPosted: true });
    if (
      path === '/api/permissions/saas/subscription-billing/create-vietqr-invoice' ||
      path === '/api/saas-billing/create-vietqr-invoice'
    ) {
      return okRes({
        invoiceId: 2,
        invoiceCode: 'INV-SAAS-202611-002',
        billingPeriod: '11/2026',
        status: 'UNPAID',
        subTotalVnd: 1_354_545,
        vatRatePercent: 10,
        vatAmountVnd: 135_455,
        amountVnd: 1_490_000,
        totalAmountVnd: 1_490_000,
        transferContent: 'ERPKHOINVSAAS202611002',
        bankName: canonicalTenantSubscription.beneficiary.bankName,
        bankBin: canonicalTenantSubscription.beneficiary.bankBin,
        accountNo: canonicalTenantSubscription.beneficiary.accountNo,
        accountName: canonicalTenantSubscription.beneficiary.accountName,
        beneficiaryBank: canonicalTenantSubscription.beneficiary.bankName,
        beneficiaryAccountNo: canonicalTenantSubscription.beneficiary.accountNo,
        beneficiaryAccountName: canonicalTenantSubscription.beneficiary.accountName,
        beneficiary: canonicalTenantSubscription.beneficiary,
        expiresInSeconds: 900,
        vietQrUrl:
          'https://img.vietqr.io/image/970423-98418102005-compact2.png?amount=1490000&addInfo=ERPKHOINVSAAS202611002&accountName=HUYNH%20PHI%20VAN',
      });
    }
    if (path === '/api/inventoryreconciliation/export-erp-gl-journal' || path === '/api/inventoryreconciliation/verify-site-readiness') {
      const mismatchCount = demoReconciliationRows.filter((r) => r.status !== 'Match').length;
      return okRes({ readinessVerdict: mismatchCount === 0 ? 'APPROVED_FOR_OPEN_CLOSE' : 'BLOCKED_BY_MISMATCH', journalBatchId: 'GL-JRN-DEMO-01', checkedRows: demoReconciliationRows.length, mismatchCount, outboxSynced: true, verifiedAtUtc: snapshotAt });
    }
  }

  if (method !== 'get') {
    return errRes(405, 'DEMO_READ_ONLY', 'Vercel Demo là môi trường chỉ đọc. Thao tác ghi đã bị chặn.');
  }

  // 4. GET Endpoints Resolution
  if (path === '/api/auth/me') {
    return okRes({
      userId: 1,
      username: profile.defaultUsername,
      role: profile.roleName,
      securityDomain: profile.securityDomain,
      tenantId: 1,
      tenantCode: 'TENANT-001',
      warehouseScopeIds: profile.warehouseScopeIds,
      permissions: [...profile.permissions],
    });
  }

  if (path === '/api/warehouses') return okRes(demoWarehouses);
  if (/^\/api\/warehouses\/\d+$/.test(path)) {
    const id = findNumericId(path);
    if (explicitRoleHeader && !profile.warehouseScopeIds.includes(id)) {
      return errRes(404, 'WAREHOUSE_OUT_OF_SCOPE', 'Không tìm thấy kho hoặc ngoài phạm vi phân quyền kho.');
    }
    const item = demoWarehouses.find((w) => w.id === id);
    return item ? okRes(item) : errRes(404, 'WAREHOUSE_NOT_FOUND', 'Không tìm thấy kho.');
  }
  if (/^\/api\/warehouses\/\d+\/calendar$/.test(path)) {
    const item = demoWarehouseCalendars.find((c) => c.warehouseId === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'CALENDAR_NOT_FOUND', 'Không tìm thấy lịch vận hành kho.');
  }

  if (path === '/api/dock-yard/warehouses') return okRes(demoDockYardWarehouses);
  if (path === '/api/dock-yard/docks') {
    const wid = Number(params.get('warehouseId') ?? 0);
    return okRes(demoDocks.filter((d) => !wid || d.warehouseId === wid));
  }
  if (path === '/api/dock-yard/yard-slots') {
    const wid = Number(params.get('warehouseId') ?? 0);
    return okRes(demoYardSlots.filter((y) => !wid || y.warehouseId === wid));
  }
  if (path === '/api/dock-yard/appointments') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const status = params.get('status');
    const direction = params.get('direction');
    const search = (params.get('search') ?? '').toLowerCase();
    return okRes(demoDockAppointments.filter((a) => (!wid || a.warehouseId === wid) && (!status || String(a.status) === status) && (!direction || String(a.direction) === direction) && (!search || [a.code, a.carrierName, a.vehiclePlate, a.driverName].join(' ').toLowerCase().includes(search))));
  }
  if (/^\/api\/dock-yard\/appointments\/\d+$/.test(path)) {
    const item = demoDockAppointments.find((a) => a.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'APPOINTMENT_NOT_FOUND', 'Không tìm thấy lịch hẹn cửa kho.');
  }

  if (path === '/api/units') return okRes(demoUnits);
  if (path === '/api/productcategories') return okRes(demoCategories);
  if (path === '/api/products') return okRes(demoProducts.map((p) => maskCostRow(p, canReadCost)));
  if (path === '/api/products/kit-work-orders') {
    return okRes(
      canonicalKitWorkOrders.map((kwo) =>
        canReadCost
          ? kwo
          : { ...kwo, rolledUpUnitCost: 0, totalCost: 0, costHiddenByRbac: true, components: kwo.components.map((c) => ({ ...c, unitCost: 0, lineCostPerKit: 0 })) },
      ),
    );
  }
  if (/^\/api\/products\/\d+$/.test(path)) {
    const item = demoProducts.find((p) => p.id === findNumericId(path));
    return item ? okRes(maskCostRow(item, canReadCost)) : errRes(404, 'PRODUCT_NOT_FOUND', 'Không tìm thấy sản phẩm.');
  }
  if (path === '/api/product-barcodes/lookup') {
    const value = params.get('value') ?? '';
    const item = demoProducts.find((p) => p.barcodes.some((b) => b.value === value));
    return item ? okRes(maskCostRow(item, canReadCost)) : errRes(404, 'BARCODE_NOT_FOUND', 'Không tìm thấy mã vạch.');
  }

  if (path === '/api/business-partners') {
    const role = (params.get('role') ?? '').toLowerCase();
    const search = (params.get('search') ?? '').toLowerCase();
    const active = params.get('active');
    const items = demoPartners.filter((item) => (!role || (role === 'supplier' ? item.isSupplier : role === 'customer' ? item.isCustomer : true)) && (!search || item.code.toLowerCase().includes(search) || item.name.toLowerCase().includes(search)) && (active === null || String(item.isActive) === active));
    return okRes(pagedResult(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 10)));
  }
  if (path === '/api/partner-debts/summary') return okRes({ totalReceivableVnd: 12_500_000, totalPayableVnd: 24_800_000, overdueCount: 0 });
  if (path === '/api/partner-debts/invoices' || path === '/api/partner-debts/ledger') return okRes({ items: [], totalCount: 0 });

  if (path === '/api/approvals/queue') {
    const type = params.get('documentType');
    const keyword = (params.get('keyword') ?? '').toLowerCase();
    const sla = params.get('slaStatus');
    const items = demoApprovalQueue.filter((i) => (!type || i.documentType === type) && (!keyword || i.documentCode.toLowerCase().includes(keyword)) && (!sla || i.slaStatus === sla));
    return okRes(pagedResult(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (path === '/api/approvals/history') return okRes([]);
  if (/^\/api\/approvals\/[^/]+\/\d+$/.test(path)) {
    const [, , , docType, rawId] = path.split('/');
    const item = demoApprovalQueue.find((e) => e.documentType.toLowerCase() === docType && e.documentId === Number(rawId));
    return item
      ? okRes({ summary: item, note: 'Dữ liệu chuẩn chỉ đọc', lines: [{ productCode: 'QA-E2E-001', productName: 'Cà Phê Rang Xay Đặc Sản Tây Nguyên 500g', unitName: 'Gói', quantity: item.totalQuantity }], history: [{ id: 1, timestampUtc: item.requestedAtUtc, actorName: item.creatorName, displayAction: 'Đã gửi duyệt', newState: item.pendingState, result: 'Success', correlationId: 'CANONICAL-APPROVAL-001' }] })
      : errRes(404, 'APPROVAL_NOT_FOUND', 'Không tìm thấy yêu cầu phê duyệt.');
  }

  if (path === '/api/stock-transfers') {
    const search = (params.get('search') ?? '').toLowerCase();
    const status = params.get('status') ?? '';
    const items = demoTransfers.filter((t) => (!search || t.code.toLowerCase().includes(search)) && (!status || String(t.status) === status));
    return okRes(pagedResult(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 15)));
  }
  if (path === '/api/stock-transfers/in-transit') {
    const items = demoTransfers.filter((t) => t.status === 'InTransit').flatMap((t) => t.details.map((d) => ({ transferId: t.id, transferCode: t.code, sourceWarehouseName: t.sourceWarehouseName, destinationWarehouseName: t.destinationWarehouseName, productId: d.productId, productCode: d.productCode, productName: d.productName, unitName: 'Gói', inTransitQuantity: d.dispatchedQuantity, dispatchedAt: t.dispatchedAt })));
    return okRes({ items });
  }
  if (path === '/api/stock-transfers/return-reasons') {
    return okRes([{ code: 'DAMAGED_IN_TRANSIT', name: 'Hàng hư hỏng khi vận chuyển' }, { code: 'WRONG_DESTINATION', name: 'Điều chuyển nhầm kho nhận' }]);
  }
  if (/^\/api\/stock-transfers\/\d+$/.test(path)) {
    const item = demoTransfers.find((t) => t.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'TRANSFER_NOT_FOUND', 'Không tìm thấy phiếu điều chuyển.');
  }

  if (path === '/api/putaway-tasks/location-warehouses') return okRes(demoWarehouses);
  if (path === '/api/putaway-tasks/locations') {
    const structure = demoWarehouseStructures.find((s) => s.warehouseId === Number(params.get('warehouseId') ?? 0));
    if (!structure) return okRes([]);
    const physical = structure.zones.flatMap((z) => z.aisles.flatMap((a) => a.racks.flatMap((r) => r.levels.flatMap((l) => l.locations.map((loc) => ({ ...loc, structurePath: `${z.code}/${a.code}/${r.code}/L${String(l.levelNo).padStart(2, '0')}/${loc.code.split('-').slice(-1)[0]}`, rowVersion: null }))))));
    return okRes([...physical, ...structure.systemLocations.map((loc) => ({ ...loc, structurePath: null, rowVersion: null }))]);
  }
  if (path === '/api/putaway-tasks/location-capacity') {
    const structure = demoWarehouseStructures.find((s) => s.warehouseId === Number(params.get('warehouseId') ?? 0));
    if (!structure) return okRes([]);
    return okRes(
      structure.zones.flatMap((z) => z.aisles.flatMap((a) => a.racks.flatMap((r) => r.levels.flatMap((l) => l.locations.map((loc) => {
        const usage = loc.code.endsWith('B04') ? { usedWeightKg: 1300, usedVolumeM3: 8.2, usedPalletEquivalent: 4 } : loc.code.endsWith('B05') ? { usedWeightKg: 620, usedVolumeM3: 4.1, usedPalletEquivalent: 2 } : { usedWeightKg: 0, usedVolumeM3: 0, usedPalletEquivalent: 0 };
        const ratio = loc.maxWeightKg ? usage.usedWeightKg / loc.maxWeightKg : 0;
        const state = !loc.isActive ? 'Inactive' : loc.isBlocked ? 'Blocked' : ratio >= 0.85 ? 'NearCapacity' : 'Available';
        return { locationId: loc.id, code: loc.code, name: loc.name, structurePath: `${z.code}/${a.code}/${r.code}/L${String(l.levelNo).padStart(2, '0')}/${loc.code.split('-').slice(-1)[0]}`, storageClass: loc.storageClass, maxWeightKg: loc.maxWeightKg, usedWeightKg: usage.usedWeightKg, maxVolumeM3: loc.maxVolumeM3, usedVolumeM3: usage.usedVolumeM3, maxPalletEquivalent: loc.maxPalletEquivalent, usedPalletEquivalent: usage.usedPalletEquivalent, profileIncomplete: false, compatibilityConflict: false, isActive: loc.isActive, isBlocked: loc.isBlocked, state };
      }))))),
    );
  }
  if (path === '/api/putaway-tasks/location-map') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const structure = demoWarehouseStructures.find((s) => s.warehouseId === wid);
    if (!structure) return okRes({ warehouseId: wid, generatedAtUtc: snapshotAt, items: [] });
    const items = structure.zones.flatMap((z) => z.aisles.flatMap((a) => a.racks.flatMap((r) => r.levels.flatMap((l) => l.locations.map((loc, idx) => {
      const utilization = loc.code.endsWith('B04') ? 86.67 : loc.code.endsWith('B05') ? 41.33 : 0;
      return { locationId: loc.id, code: loc.code, name: loc.name, structurePath: `${z.code}/${a.code}/${r.code}/L${String(l.levelNo).padStart(2, '0')}/${loc.code.split('-').slice(-1)[0]}`, storageClass: loc.storageClass, mapX: loc.mapX, mapY: loc.mapY, mapWidth: loc.mapWidth, mapHeight: loc.mapHeight, utilizationPercent: utilization, capacityState: !loc.isActive ? 'Inactive' : loc.isBlocked ? 'Blocked' : utilization >= 85 ? 'NearCapacity' : 'Available', recentMovementCount: idx === 0 ? 12 : 4, activePutawayCount: idx === 0 ? 5 : 1, activityLevel: idx === 0 ? 'High' : 'Medium', isActive: loc.isActive, isBlocked: loc.isBlocked, rowVersion: null };
    })))));
    return okRes({ warehouseId: wid, generatedAtUtc: snapshotAt, items });
  }
  if (path === '/api/putaway-tasks') return okRes(demoPutawayTasks);
  if (/^\/api\/putaway-tasks\/\d+$/.test(path)) {
    const item = demoPutawayTasks.find((t) => t.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'PUTAWAY_NOT_FOUND', 'Không tìm thấy nhiệm vụ cất hàng.');
  }
  if (/^\/api\/putaway-tasks\/\d+\/items\/\d+\/destinations$/.test(path)) {
    return okRes([{ id: 1101, code: 'A01-R02-L03-B04', name: 'Ô lưu trữ chính B04', locationType: 'Bin', isPickable: true }, { id: 1102, code: 'A01-R02-L03-B05', name: 'Ô dự trữ B05', locationType: 'Bin', isPickable: false }]);
  }

  if (path === '/api/inventory/current') {
    const wid = Number(params.get('warehouseId') ?? 0);
    if (explicitRoleHeader && wid > 0 && !profile.warehouseScopeIds.includes(wid)) {
      return errRes(404, 'WAREHOUSE_OUT_OF_SCOPE', 'Không tìm thấy dữ liệu tồn kho ngoài phạm vi kho.');
    }
    const rows = canonicalInventoryCurrentRows.filter((r) => !wid || r.warehouseId === wid).map((r) => maskCostRow(r, canReadCost));
    return okRes({
      items: rows,
      totalCount: rows.length,
      costHiddenByRbac: !canReadCost,
      totalInventoryValueVnd: canReadCost ? CANONICAL_TOTAL_INVENTORY_VALUE_VND : 0,
    });
  }
  if (path === '/api/inventorystocks/current') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const pid = Number(params.get('productId') ?? 0);
    const kw = (params.get('keyword') ?? '').toLowerCase();
    return okRes(demoInventoryStocks.filter((s) => (!wid || s.warehouseId === wid) && (!pid || s.productId === pid) && (!kw || s.productCode.toLowerCase().includes(kw) || s.productName.toLowerCase().includes(kw))).map((s) => maskCostRow(s, canReadCost)));
  }
  if (path === '/api/inventory/statuses') return okRes(demoInventoryStatuses);
  if (path === '/api/inventory/locks') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const st = (params.get('status') ?? '').toLowerCase();
    return okRes(demoInventoryLocks.filter((l) => (!wid || l.warehouseId === wid) && (!st || l.status.toLowerCase() === st)));
  }
  if (/^\/api\/inventory\/locks\/\d+$/.test(path)) {
    const item = demoInventoryLocks.find((l) => l.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'LOCK_NOT_FOUND', 'Không tìm thấy bản ghi khóa tồn kho.');
  }
  if (path === '/api/inventory/buckets') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const pid = Number(params.get('productId') ?? 0);
    const st = (params.get('status') ?? '').toUpperCase();
    const lot = (params.get('lotNumber') ?? '').toLowerCase();
    const ser = (params.get('serialNumber') ?? '').toLowerCase();
    return okRes(demoInventoryBuckets.filter((b) => (!wid || b.warehouseId === wid) && (!pid || b.productId === pid) && (!st || b.status === st) && (!lot || (b.lotNumber ?? '').toLowerCase().includes(lot)) && (!ser || (b.serialNumber ?? '').toLowerCase().includes(ser))));
  }
  if (path === '/api/inventory/reversal-reasons') {
    return okRes([{ code: 'LOCATION_ERROR', name: 'Sai vị trí lưu kho', transactionType: 'Move' }, { code: 'STATUS_ERROR', name: 'Sai trạng thái tồn kho', transactionType: 'StatusChange' }]);
  }
  if (path === '/api/inventory/reversal-warehouses' || path === '/api/inventory/traceability-warehouses') {
    return okRes(demoWarehouses.map((w) => ({ id: w.id, code: w.code, name: w.name })));
  }
  if (path === '/api/inventory/reversal-candidates') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const tid = Number(params.get('transactionId') ?? 0);
    const reversedFilter = params.get('isReversed');
    const reversalMarkers = demoInventoryTransactions.filter((t) => t.transactionType === 'Reversal' && t.referenceType === 'InventoryReversal');
    const reversedIds = new Set(reversalMarkers.map((t) => t.referenceId));
    const correctionIds = new Set(reversalMarkers.map((t) => t.correctiveTransactionId));
    const candidates = demoInventoryTransactions
      .filter((t) => (t.transactionType === 'Move' || t.transactionType === 'StatusChange') && !correctionIds.has(t.id) && (!wid || t.warehouseId === wid) && (!tid || t.id === tid))
      .map((t) => ({ ...t, isReversed: reversedIds.has(t.id) }))
      .filter((t) => reversedFilter === null || t.isReversed === (reversedFilter === 'true'));
    return okRes(pagedResult(candidates, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (path === '/api/inventorytransactions') {
    const txType = params.get('transactionType');
    const wid = Number(params.get('warehouseId') ?? 0);
    const pid = Number(params.get('productId') ?? 0);
    const kw = (params.get('keyword') ?? '').toLowerCase();
    const items = demoInventoryTransactions.filter((t) => (!txType || t.transactionType.toLowerCase() === txType.toLowerCase()) && (!wid || t.warehouseId === wid) && (!pid || t.productId === pid) && (!kw || t.productCode.toLowerCase().includes(kw) || t.productName.toLowerCase().includes(kw)));
    return okRes(pagedResult(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (path === '/api/inventory/traceability') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const pid = Number(params.get('productId') ?? 0);
    const lotNumber = (params.get('lotNumber') ?? '').trim().toLowerCase();
    const serialNumber = (params.get('serialNumber') ?? '').trim().toLowerCase();
    const referenceType = params.get('referenceType')?.trim() ?? '';
    const referenceId = Number(params.get('referenceId') ?? 0);
    const limit = Math.min(500, Math.max(1, Number(params.get('limit') ?? 200)));
    const bucketOffset = Number(params.get('bucketOffset') ?? 0);
    const eventOffset = Number(params.get('eventOffset') ?? 0);
    const hasReference = Boolean(referenceType && referenceId);
    if (!Number.isSafeInteger(bucketOffset) || bucketOffset < 0 || bucketOffset > 50000 || bucketOffset % 500 !== 0) return errRes(400, 'INVALID_BUCKET_OFFSET', 'Trang nhóm tồn không hợp lệ.');
    if (!Number.isSafeInteger(eventOffset) || eventOffset < 0 || eventOffset > 50000 || eventOffset % limit !== 0) return errRes(400, 'INVALID_EVENT_OFFSET', 'Trang sự kiện sổ cái không hợp lệ.');
    if (Boolean(referenceType) !== Boolean(referenceId)) return errRes(400, 'INVALID_REFERENCE_PAIR', 'Loại tham chiếu và ID tham chiếu phải được nhập cùng nhau.');
    if (!wid && !pid && !lotNumber && !serialNumber && !hasReference) return errRes(400, 'MISSING_TRACE_FILTER', 'Cần ít nhất Kho, Sản phẩm, Lô, Sê-ri hoặc Tham chiếu.');
    if (wid && !demoWarehouses.some((w) => w.id === wid)) return errRes(404, 'WAREHOUSE_NOT_FOUND', 'Không tìm thấy kho trong dữ liệu mô phỏng.');
    const matchingEvents = demoInventoryTransactions
      .filter((t) => (!wid || t.warehouseId === wid) && (!pid || t.productId === pid) && (!lotNumber || (t.lotNumber ?? '').toLowerCase() === lotNumber) && (!serialNumber || (t.serialNumber ?? '').toLowerCase() === serialNumber) && (!hasReference || (t.referenceType === referenceType && t.referenceId === referenceId)))
      .sort((a, b) => new Date(b.transactionDate).getTime() - new Date(a.transactionDate).getTime() || b.id - a.id);
    const reversed = new Set(demoInventoryTransactions.filter((t) => t.transactionType === 'Reversal' && t.referenceType === 'InventoryReversal').map((t) => t.referenceId));
    const matchedBuckets = demoInventoryBuckets.filter((b) => (!wid || b.warehouseId === wid) && (!pid || b.productId === pid) && (!lotNumber || (b.lotNumber ?? '').toLowerCase() === lotNumber) && (!serialNumber || (b.serialNumber ?? '').toLowerCase() === serialNumber) && (!hasReference || matchingEvents.some((e) => e.productId === b.productId && e.warehouseId === b.warehouseId && (e.lotId ?? null) === (b.lotId ?? null) && (e.serialId ?? null) === (b.serialId ?? null))));
    const bucketPage = matchedBuckets.slice(bucketOffset, bucketOffset + 501);
    const eventsSlice = matchingEvents.slice(eventOffset, eventOffset + limit);
    return okRes({
      currentBuckets: bucketPage.slice(0, 500).map((b) => ({ ...b, inventoryStatus: b.status })),
      bucketsTruncated: bucketPage.length > 500,
      eventsTruncated: matchingEvents.length > eventOffset + limit,
      events: eventsSlice.map((e) => ({ ...e, transactionId: e.id, reversalOfTransactionId: e.transactionType === 'Reversal' && e.referenceType === 'InventoryReversal' ? e.referenceId : null, isReversed: reversed.has(e.id) })),
    });
  }

  if (path === '/api/purchase-orders') {
    const status = params.get('status');
    return okRes(demoPurchaseOrders.filter((p) => !status || p.status === status).map((p) => ({ ...p, lines: p.lines.map((l) => ({ ...l, lineNumber: l.lineNo, unitName: l.operationUnitCode, receivedQuantity: 0, remainingQuantity: l.orderedQuantity })) })));
  }
  if (/^\/api\/purchase-orders\/\d+$/.test(path)) {
    const item = demoPurchaseOrders.find((p) => p.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'PO_NOT_FOUND', 'Không tìm thấy đơn mua hàng.');
  }
  if (path === '/api/asns') return okRes(demoAsns);
  if (/^\/api\/asns\/\d+$/.test(path)) {
    const item = demoAsns.find((a) => a.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'ASN_NOT_FOUND', 'Không tìm thấy ASN.');
  }
  if (path === '/api/importreceipts') return okRes(demoImportReceipts);
  if (/^\/api\/importreceipts\/\d+$/.test(path)) {
    const item = demoImportReceipts.find((r) => r.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'RECEIPT_NOT_FOUND', 'Không tìm thấy phiếu nhập kho.');
  }
  if (/^\/api\/importreceipts\/\d+\/inventory-identities$/.test(path)) return okRes(demoImportReceiptInventoryIdentities[findNumericId(path)] ?? []);
  if (/^\/api\/importreceipts\/\d+\/discrepancies$/.test(path)) return okRes([]);
  if (path === '/api/importreceipts/discrepancy-reasons') return okRes([{ code: 'COUNT_VARIANCE', name: 'Chênh lệch kiểm đếm', version: 1, requiresNote: true, requiresAttachment: false, requiresApproval: false }]);

  if (path === '/api/exportreceipts') return okRes(demoExportReceipts);
  if (/^\/api\/exportreceipts\/\d+$/.test(path)) {
    const item = demoExportReceipts.find((r) => r.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'EXPORT_NOT_FOUND', 'Không tìm thấy phiếu xuất kho.');
  }

  if (path === '/api/stock-reservations') {
    const st = params.get('status');
    return okRes(pagedResult(demoReservations.filter((r) => !st || r.status === st), Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (path === '/api/inventory/allocations/reservations') {
    const wid = Number(params.get('warehouseId') ?? 0);
    return okRes(demoAllocatableReservations.filter((r) => !wid || r.warehouseId === wid));
  }
  if (path === '/api/inventory/allocations/candidates') {
    const rid = Number(params.get('reservationId') ?? 0);
    return okRes(demoAllocationCandidates.filter((c) => (!rid || c.reservationId === rid) && c.allocatableQuantity > 0).map(({ reservationId: _rid, ...rest }) => rest));
  }
  if (path === '/api/inventory/allocations') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const rid = Number(params.get('reservationId') ?? 0);
    const st = params.get('status');
    return okRes(pagedResult(demoAllocations.filter((a) => (!wid || a.warehouseId === wid) && (!rid || a.reservationId === rid) && (!st || a.status === st)), Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (/^\/api\/inventory\/allocations\/\d+$/.test(path)) {
    const item = demoAllocations.find((a) => a.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'ALLOCATION_NOT_FOUND', 'Không tìm thấy phân bổ tồn kho.');
  }

  if (path === '/api/picking-tasks') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const st = params.get('status');
    return okRes(demoPickingTasks.filter((t) => (!wid || t.warehouseId === wid) && (!st || t.status === st)).map(({ lines: _l, shortPicks: _s, rowVersion: _r, ...rest }) => rest));
  }
  if (/^\/api\/picking-tasks\/\d+$/.test(path)) {
    const item = demoPickingTasks.find((t) => t.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'PICKING_NOT_FOUND', 'Không tìm thấy nhiệm vụ lấy hàng.');
  }
  if (path === '/api/packing-sessions') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const st = params.get('status');
    return okRes(demoPackingSessions.filter((s) => (!wid || s.warehouseId === wid) && (!st || s.status === st)).map(({ lines: _l, handlingUnits: _h, rowVersion: _r, ...rest }) => rest));
  }
  if (/^\/api\/packing-sessions\/\d+$/.test(path)) {
    const item = demoPackingSessions.find((s) => s.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'PACKING_NOT_FOUND', 'Không tìm thấy phiên đóng gói.');
  }
  if (path === '/api/handling-units') {
    const wid = Number(params.get('warehouseId') ?? 0);
    return okRes(demoHandlingUnits.filter((h) => !wid || h.warehouseId === wid));
  }
  if (/^\/api\/handling-units\/\d+$/.test(path)) {
    const item = demoHandlingUnits.find((h) => h.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'HU_NOT_FOUND', 'Không tìm thấy đơn vị xử lý (HU).');
  }

  if (path === '/api/shipments/carrier-configs') {
    return okRes([{ carrierCode: 'GHN', carrierName: 'Giao Hàng Nhanh (GHN)', shopId: '235512', shopName: '0397354273', maskedApiToken: '0c06****12be', gatewayMode: 'LIVE_GHN_DEV_SANDBOX', isSandbox: true, fromDistrictId: 1454, fromDistrictName: 'Quận 12, TP. Hồ Chí Minh', baseShippingFeeVnd: 22_000, sortingHubPrefix: 'GHN-HCM-Q12' }]);
  }
  if (path === '/api/shipments') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const st = params.get('status');
    return okRes(demoShipments.filter((s) => (!wid || s.warehouseId === wid) && (!st || s.status === st)).map(({ handlingUnits: _h, proofOfDelivery: _p, trackingEvents: _e, rowVersion: _r, ...rest }) => rest));
  }
  if (/^\/api\/shipments\/\d+\/tracking$/.test(path)) {
    const item = demoShipments.find((s) => s.id === findNumericId(path));
    return item ? okRes({ shipmentId: item.id, shipmentCode: item.shipmentCode, status: item.status, dispatchedAt: item.dispatchedAt, inTransitAt: item.inTransitAt, deliveredAt: item.deliveredAt, completedAt: item.completedAt, proofOfDelivery: item.proofOfDelivery, events: item.trackingEvents }) : errRes(404, 'SHIPMENT_NOT_FOUND', 'Không tìm thấy hành trình vận đơn.');
  }
  if (/^\/api\/shipments\/\d+$/.test(path)) {
    const item = demoShipments.find((s) => s.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'SHIPMENT_NOT_FOUND', 'Không tìm thấy lô giao hàng.');
  }
  if (path === '/api/handover-manifests') return okRes({ items: [{ id: 301, manifestCode: 'MNF-202611-001', carrierCode: 'GHN', carrierName: 'Giao Hàng Nhanh (GHN)', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', status: 'OPEN', parcelCount: 1, createdAt: snapshotAt }], totalCount: 1 });

  if (path === '/api/sales-orders') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const st = params.get('status');
    return okRes(demoSalesOrders.filter((o) => (!wid || o.warehouseId === wid) && (!st || o.status === st)).map(({ lines: _l, rowVersion: _r, ...rest }) => rest));
  }
  if (/^\/api\/sales-orders\/\d+$/.test(path)) {
    const item = demoSalesOrders.find((o) => o.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'SO_NOT_FOUND', 'Không tìm thấy đơn bán hàng.');
  }
  if (path === '/api/backorders') return okRes(canonicalBackorders);
  if (/^\/api\/backorders\/\d+$/.test(path)) return errRes(404, 'BACKORDER_NOT_FOUND', 'Không có đơn thiếu hàng tồn đọng.');

  if (path === '/api/stocktakes') {
    return okRes(demoStocktakes.map(({ details: _d, ...rest }) => rest));
  }
  if (/^\/api\/stocktakes\/\d+$/.test(path)) {
    const item = demoStocktakes.find((s) => s.id === findNumericId(path));
    return item ? okRes(item) : errRes(404, 'STOCKTAKE_NOT_FOUND', 'Không tìm thấy phiếu kiểm kê.');
  }

  if (path === '/api/customer-returns') {
    const item = maskCostRow(
      { id: 901, returnCode: 'RMA-2026-0901', trackingNumber: 'GHNVN06889911', exportReceiptCode: 'EX-2026-5108', salesOrderCode: 'SO-202611-001', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', quarantineLocationCode: 'QUARANTINE-01', productCode: 'QA-E2E-001', productName: 'Thùng linh kiện điện tử QA-01', quantity: 2, unitCost: 250_000, returnReason: 'BUYER_REJECTED_DELIVERY', condition: 'GOOD', disposition: 'PENDING', status: 'QUARANTINED', rowVersion: 'AAAAAAAARMA1' },
      canReadCost,
    );
    return okRes({ items: [item], totalCount: 1, costHiddenByRbac: !canReadCost, summary: { quarantinedCount: 1, inspectedCount: 0, restockedCount: 0, refurbishRtvScrapCount: 0 } });
  }

  if (path === '/api/reports/owner-gross-profit' || path === '/api/inventoryreconciliation/valuation') {
    if (explicitRoleHeader && !canReadCost) {
      return errRes(403, 'MISSING_INVENTORY_COST_READ', 'Cần quyền inventory.cost.read để xem định giá tồn kho và lợi nhuận gộp.');
    }
    return okRes({
      warehouseId: 1,
      warehouseCode: 'WH01',
      warehouseName: 'Kho Trung tâm (WH01)',
      totalInventoryValueVnd: canReadCost ? CANONICAL_TOTAL_INVENTORY_VALUE_VND : 0,
      costHiddenByRbac: !canReadCost,
      items: canonicalInventoryCurrentRows.map((r) => maskCostRow(r, canReadCost)),
    });
  }
  if (path === '/api/reports/cod-reconciliation' || path === '/api/reports/cod-shipping-reconciliation') {
    if (explicitRoleHeader && !canReadCost) {
      return errRes(403, 'MISSING_INVENTORY_COST_READ', 'Cần quyền inventory.cost.read để xem đối soát COD.');
    }
    return okRes({ items: [], totalCount: 0, totalExpectedCodVnd: 0, totalSettledCodVnd: 0, varianceVnd: 0 });
  }

  if (path === '/api/inventoryreconciliation') {
    const wid = Number(params.get('warehouseId') ?? 0);
    const pid = Number(params.get('productId') ?? 0);
    const kw = (params.get('keyword') ?? '').toLowerCase();
    const items = canonicalReconciliationRows.filter((r) => (!wid || r.warehouseId === wid) && (!pid || r.productId === pid) && (!kw || r.productCode.toLowerCase().includes(kw) || r.productName.toLowerCase().includes(kw)));
    return okRes(pagedResult(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
  }
  if (path === '/api/inventoryreconciliation/warehouses') return okRes(demoReconciliationWarehouses);
  if (path === '/api/inventoryreconciliation/investigation') {
    const wid = Number(params.get('warehouseId') ?? 1);
    const pid = Number(params.get('productId') ?? 101);
    const row = canonicalReconciliationRows.find((r) => r.warehouseId === wid && r.productId === pid) ?? canonicalReconciliationRows[0];
    return okRes({ warehouseId: row.warehouseId, warehouseName: row.warehouseName, productId: row.productId, productCode: row.productCode, productName: row.productName, unitName: 'Gói', stockRowExists: true, currentQuantity: row.currentQuantity, reservedQuantity: 0, bucketTotalQuantity: row.currentQuantity, ledgerMovementNetQuantity: row.expectedQuantity, allStatusBucketQuantity: row.currentQuantity, allStatusEventDeltaQuantity: row.expectedQuantity, allStatusBucketCount: 1, allStatusBucketsTruncated: false, bucketsTruncated: false, eventsTruncated: false, buckets: [], events: [] });
  }

  if (path === '/api/reports/inventory-in-out-stock') return okRes(demoInOut);
  if (path === '/api/reports/inventory/export' || path === '/api/reports/inventory-in-out-stock/export') {
    return okRes(new Blob(['Bản xuất dữ liệu mẫu chỉ đọc'], { type: 'text/plain' }));
  }
  if (path === '/api/reports/stock-card') {
    return okRes({ templateCode: 'Mẫu số S12-DN', circularReference: 'Thông tư 200/2014/TT-BTC', warehouseId: 1, warehouseName: 'Kho Trung tâm (WH01)', productId: 101, productCode: 'QA-E2E-001', productName: 'Cà Phê Rang Xay Đặc Sản Tây Nguyên 500g', unitName: 'Gói', openingBalance: 200, totalInboundQuantity: 60, totalOutboundQuantity: 24, closingBalance: 236, lines: [] });
  }
  if (path === '/api/reports/expiry-aging') {
    const items = demoInventoryBuckets.map((b) => ({ ...b, bucketId: b.inventoryStockId, unitName: 'Gói', inventoryStatus: b.status, daysToExpiry: b.expiryDate ? 45 : null, expiryBucket: b.expiryDate ? '31_TO_90_DAYS' : 'NO_EXPIRY', expiryStatus: b.expiryDate ? 'GOOD' : 'NO_EXPIRY', receivedAt: b.lastUpdated, ageDays: 14, ageInDays: 14, agingBucket: '0_30_DAYS', isSlowMoving: false, quantity: b.onHandQuantity }));
    const summary = { expiredLotsCount: 0, expiringIn7DaysCount: 0, expiringIn30DaysCount: 0, expiringIn90DaysCount: items.filter((x) => x.expiryDate !== null).length, slowMovingLotsCount: 0, totalTrackedLots: items.filter((x) => x.lotNumber !== null).length };
    return okRes(Object.assign([...items], { summary, items }));
  }
  if (path === '/api/reports/analytics-summary') {
    return okRes({ windowDays: Number(params.get('days') ?? 30) || 30, inboundOutboundSla: { completedReceiptsCount: 12, avgInboundLeadTimeHours: 4.2, openPutawayBacklogCount: 1, dispatchedExportsCount: 18, avgOutboundLeadTimeHours: 3.5, openPickingBacklogCount: 1 }, stocktakeAccuracy: { approvedStocktakesCount: 4, totalLinesCounted: 32, exactMatchLinesCount: 32, varianceLinesCount: 0, accuracyRatePercent: 100, totalAbsoluteVarianceQty: 0 }, turnoverAndDaysOnHand: [] });
  }
  if (path === '/api/reports/productivity-and-bi-snapshot') {
    return okRes({ warehouseId: Number(params.get('warehouseId') ?? 1), activeTasksCount: 3, completedTodayCount: 12, heatmapHotspotsCount: 2, biExportStatus: 'READY', completedPutawaysCount: 5, completedPicksCount: 4, completedPacksCount: 3, totalMovesCount: 12, stockSnapshotRowCount: canonicalInventoryCurrentRows.length, ledgerWatermarkId: demoInventoryTransactions.length, auditWatermarkId: 108, readyForBiExport: true, generatedAtUtc: snapshotAt });
  }
  if (path === '/api/reports/operational-dashboard') {
    return okRes({ warehouseCount: demoWarehouses.length, productCount: demoProducts.length, inventoryBucketCount: canonicalInventoryCurrentRows.length, openTaskCount: 3, invalidStockCount: 0, openInboundCount: 1, openPutawayCount: 1, openOutboundCount: 2, openTransferCount: 1, recentActivities: [] });
  }
  if (path === '/api/reports/multi-warehouse-control-tower') {
    return okRes({ totalWarehouses: demoWarehouses.length, healthyWarehousesCount: demoWarehouses.length, attentionWarehousesCount: 0, warehouses: demoWarehouses, taskEngineQueues: [] });
  }
  if (path === '/api/reports/global-search') {
    const q = (params.get('q') ?? '').toLowerCase();
    return okRes(demoProducts.filter((p) => !q || p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q)).map((p) => ({ entityType: 'Product', code: p.code, title: p.name, subtitle: p.categoryName, route: '/products' })));
  }

  if (path === '/api/saas-billing/subscription' || path === '/api/permissions/saas/subscription-billing') {
    return okRes({ subscription: canonicalTenantSubscription, invoices: canonicalSaasInvoices, beneficiary: canonicalTenantSubscription.beneficiary });
  }
  if (path === '/api/saas-billing/tax-profile') {
    return okRes({ tenantCode: 'TENANT-001', companyName: canonicalTenantSubscription.companyName, taxCode: canonicalTenantSubscription.taxCode });
  }

  if (path === '/api/permissions') return okRes(demoCustomerPermissionCatalog);
  if (path === '/api/permissions/roles') {
    return okRes(
      Object.values(CANONICAL_ROLE_PROFILES)
        .filter((rp) => rp.securityDomain === 'CUSTOMER_TENANT')
        .map((rp, idx) => ({
          id: idx + 1,
          roleName: rp.roleName,
          labelVi: rp.labelVi,
          securityDomain: rp.securityDomain,
          rowVersion: `AAAAAAAAROLE${idx + 1}`,
          permissions: [...rp.permissions],
        })),
    );
  }
  if (path === '/api/permissions/audit-logs') {
    return okRes([{ id: 1, userId: 1, action: 'Permission.CatalogView', entityName: 'RolePermission', entityId: 1, newValues: 'Xem ma trận phân quyền chuẩn', timestamp: snapshotAt }]);
  }
  if (path === '/api/permissions/compliance-evidence-bundle') {
    return okRes({ bundleId: 'EVD-CANONICAL-202611', policyVersion: 'v19.0-CANONICAL', legalHoldActive: true, appendOnlyAuditVerified: true, generatedAtUtc: snapshotAt });
  }
  if (path === '/api/auth/sessions') {
    return okRes([{ sessionId: '00000000-0000-0000-0000-000000000001', deviceInfo: 'Phiên Trình duyệt Chuẩn', ipAddress: '127.0.0.1', isCurrent: true, createdAtUtc: snapshotAt }]);
  }
  if (/^\/api\/users\/\d+\/security$/.test(path)) {
    return okRes({ userId: findNumericId(path), username: profile.defaultUsername, roleName: profile.roleName, isActive: true, isTemporarilyLocked: false, failedLoginCount: 0, activeSessionCount: 1 });
  }
  if (/^\/api\/users\/\d+\/warehouse-access$/.test(path)) {
    return okRes({
      userId: findNumericId(path),
      username: profile.defaultUsername,
      fullName: profile.labelVi,
      roleName: profile.roleName,
      rowVersion: 'AAAAAAAADEMOWH',
      warehouses: demoWarehouses.filter((w) => profile.warehouseScopeIds.length === 0 || profile.warehouseScopeIds.includes(w.id)).map((w) => ({ warehouseId: w.id, warehouseCode: w.code, warehouseName: w.name, warehouseIsActive: w.isActive, grantedAt: '2026-10-01T08:00:00Z' })),
    });
  }

  if (
    path === '/api/reports/warehouse-tasks' ||
    path === '/api/reports/operational-exceptions' ||
    path === '/api/dock-yard/dwell-monitor' ||
    path === '/api/inventory/replenishment-suggestions' ||
    path === '/api/picking-tasks/wave-summary' ||
    /^\/api\/warehouses\/\d+\/calendar\/exceptions$/.test(path)
  ) {
    return okRes([]);
  }

  return errRes(404, 'DEMO_ENDPOINT_NOT_MAPPED', `Endpoint demo chưa được ánh xạ: ${url || 'unknown'}`);
};
