import { erpWmsBlueprint } from '../config/erpWmsBlueprint';

export type MockTone = 'green' | 'blue' | 'orange' | 'red' | 'gray';

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

export const mockWarehouses: MockWarehouse[] = [
  { code: 'WH-HCM-01', name: 'DC Hồ Chí Minh', city: 'TP.HCM', type: 'DC' },
  { code: 'WH-DN-01', name: 'Kho Đà Nẵng', city: 'Đà Nẵng', type: 'Regional' },
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
];

export const mockPartners: MockPartner[] = [
  { code: 'SUP-001', name: 'Công ty Nông Sản Cao Nguyên', roles: ['Supplier'] },
  { code: 'SUP-002', name: 'Công ty Thiết Bị Việt', roles: ['Supplier'] },
  { code: 'CUS-001', name: 'Chuỗi bán lẻ Minh Phúc', roles: ['Customer'] },
  { code: 'CUS-002', name: 'Thương mại An Khang', roles: ['Customer'] },
  { code: 'LOG-001', name: 'Vận chuyển Nam Bắc', roles: ['Carrier'] },
];

export const mockUsers: MockUser[] = [
  { code: 'U-ADMIN', name: 'Nguyễn Minh Anh', role: 'Admin', warehouses: mockWarehouses.map((warehouse) => warehouse.code) },
  { code: 'U-HCM-MGR', name: 'Trần Quốc Bảo', role: 'Warehouse Manager', warehouses: ['WH-HCM-01'] },
  { code: 'U-HCM-RCV', name: 'Lê Thu Hà', role: 'Receiver', warehouses: ['WH-HCM-01'] },
  { code: 'U-HCM-PICK', name: 'Phạm Gia Huy', role: 'Picker', warehouses: ['WH-HCM-01'] },
  { code: 'U-QC', name: 'Vũ Ngọc Lan', role: 'QC', warehouses: ['WH-HCM-01', 'WH-DN-01'] },
  { code: 'U-DN-MGR', name: 'Đỗ Minh Khang', role: 'Warehouse Manager', warehouses: ['WH-DN-01'] },
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

const r = (
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
  id,
  reference,
  type,
  status,
  tone,
  warehouse,
  subject,
  owner,
  priority,
  updatedAt,
  ...extra,
});

const snapshotAt = '2026-10-02T09:30:00Z';

export const mockWorkCenters: Record<string, MockWorkCenterData> = {
  overview: {
    moduleKey: 'overview',
    snapshotAt,
    records: [
      r('OV-001', 'TASK-7821', 'Nhiệm vụ', 'Đang xử lý', 'blue', 'WH-HCM-01', 'Hoàn tất receiving GR-2026-1048', 'Lê Thu Hà', 'High', '2026-10-02T09:24:00Z', { partnerCode: 'SUP-001' }),
      r('OV-002', 'APR-1182', 'Phê duyệt', 'Sắp đến hạn', 'orange', 'WH-HCM-01', 'Điều chỉnh tồn kho vượt ngưỡng', 'Trần Quốc Bảo', 'Critical', '2026-10-02T09:10:00Z', { productCode: 'SKU-2001', quantity: 3, uom: 'Cái' }),
      r('OV-003', 'EXC-904', 'Ngoại lệ', 'Mới', 'red', 'WH-DN-01', 'Putaway không có location phù hợp', 'Đỗ Minh Khang', 'High', '2026-10-02T08:58:00Z', { productCode: 'SKU-4001', quantity: 120, uom: 'Chai' }),
      r('OV-004', 'SLA-221', 'SLA', 'Bình thường', 'green', 'WH-HCM-01', 'Outbound backlog dưới ngưỡng', 'Nguyễn Minh Anh', 'Normal', '2026-10-02T08:40:00Z'),
      r('OV-005', 'QUEUE-HCM-OPS', 'Work Queue', 'READY', 'blue', 'WH-HCM-01', '12 task • 3 high priority • 1 blocked', 'Task Engine', 'High', '2026-10-02T09:27:00Z'),
      r('OV-006', 'SEARCH-SCOPE-ADMIN', 'Global Search', 'READY', 'green', 'WH-HCM-01', 'Exact-first index • Product / Barcode / Document / Lot / Serial / Partner', 'Search Projection', 'Normal', '2026-10-02T09:26:00Z'),
    ],
  },
  'master-data': {
    moduleKey: 'master-data',
    snapshotAt,
    records: [
      r('MD-001', 'SKU-1001', 'Sản phẩm', 'Active', 'green', 'WH-HCM-01', 'Cà phê Arabica 500g', 'Nguyễn Minh Anh', 'Normal', '2026-10-01T15:20:00Z', { productCode: 'SKU-1001', quantity: 2, uom: 'Barcode' }),
      r('MD-002', 'SKU-2001', 'Sản phẩm', 'Active', 'green', 'WH-HCM-01', 'Tai nghe Bluetooth TWS • Serial tracked', 'Nguyễn Minh Anh', 'Normal', '2026-10-01T14:06:00Z', { productCode: 'SKU-2001' }),
      r('MD-003', 'SUP-001', 'Đối tác', 'Active', 'green', 'WH-HCM-01', 'Công ty Nông Sản Cao Nguyên', 'Nguyễn Minh Anh', 'Normal', '2026-09-30T10:30:00Z', { partnerCode: 'SUP-001' }),
      r('MD-004', 'UOM-CASE24', 'UOM', 'Review', 'orange', 'WH-HCM-01', 'Thùng 24 chai → 24 Chai', 'Nguyễn Minh Anh', 'Normal', '2026-09-29T08:11:00Z', { productCode: 'SKU-4001' }),
      r('MD-005', 'PKG-CARTON-M', 'Packaging Type', 'ACTIVE', 'green', 'WH-HCM-01', 'Carton M • 400×300×250 mm • max 15 kg', 'Nguyễn Minh Anh', 'Normal', '2026-10-01T11:40:00Z'),
      r('MD-006', 'SLA-SUP001-2026', 'Partner SLA', 'ACTIVE', 'green', 'WH-HCM-01', 'SUP-001 • delivery accuracy 95% • dock-to-stock target 4h', 'Master Data Admin', 'High', '2026-10-02T08:05:00Z', { partnerCode: 'SUP-001' }),
    ],
  },
  'warehouse-structure': {
    moduleKey: 'warehouse-structure',
    snapshotAt,
    records: [
      r('WH-001', 'WH-HCM-01', 'Warehouse', 'OPEN', 'green', 'WH-HCM-01', 'DC Hồ Chí Minh • 86% capacity', 'Trần Quốc Bảo', 'Normal', '2026-10-02T09:28:00Z', { location: 'HCM' }),
      r('WH-002', 'LOC-A01-R02-L03-B04', 'Bin', 'Available', 'green', 'WH-HCM-01', 'Zone A / Aisle 01 / Rack 02 / Level 03 / Bin 04', 'Trần Quốc Bảo', 'Normal', '2026-10-02T08:15:00Z', { location: 'A01-R02-L03-B04' }),
      r('WH-003', 'DOCK-HCM-03', 'Dock', 'Occupied', 'blue', 'WH-HCM-01', 'Dock 03 • xe 51C-882.41', 'Lê Thu Hà', 'High', '2026-10-02T09:12:00Z'),
      r('WH-004', 'CAP-DN-COLD', 'Capacity', 'Warning', 'orange', 'WH-DN-01', 'Khu lạnh đạt 92% usable capacity', 'Đỗ Minh Khang', 'High', '2026-10-02T09:05:00Z'),
      r('WH-005', 'CAL-EXC-HCM-20261010', 'Calendar Exception', 'APPROVED', 'green', 'WH-HCM-01', '10/10 extended shift 18:00–22:00 • emergency override none', 'Warehouse Admin', 'High', '2026-10-02T08:02:00Z'),
    ],
  },
  inbound: {
    moduleKey: 'inbound',
    snapshotAt,
    records: [
      r('IN-001', 'ASN-2026-0812', 'ASN', 'Expected', 'blue', 'WH-HCM-01', 'SUP-001 • 1.200 gói cà phê', 'Lê Thu Hà', 'Normal', '2026-10-02T08:00:00Z', { partnerCode: 'SUP-001', productCode: 'SKU-1001', quantity: 1200, uom: 'Gói' }),
      r('IN-002', 'GR-2026-1048', 'Goods Receipt', 'RECEIVING', 'blue', 'WH-HCM-01', 'Nhận PO-2026-4521', 'Lê Thu Hà', 'High', '2026-10-02T09:24:00Z', { partnerCode: 'SUP-001', productCode: 'SKU-1001', quantity: 1200, uom: 'Gói', location: 'RECV-01' }),
      r('IN-003', 'GR-2026-1045', 'Goods Receipt', 'QC_PENDING', 'orange', 'WH-HCM-01', 'Tai nghe cần QC serial', 'Vũ Ngọc Lan', 'High', '2026-10-02T08:48:00Z', { partnerCode: 'SUP-002', productCode: 'SKU-2001', quantity: 48, uom: 'Cái', location: 'QC-01' }),
      r('IN-004', 'GR-2026-1041', 'Goods Receipt', 'READY_TO_POST', 'orange', 'WH-DN-01', 'Nước khoáng đã cân bằng QC', 'Đỗ Minh Khang', 'High', '2026-10-02T08:35:00Z', { productCode: 'SKU-4001', quantity: 2400, uom: 'Chai', location: 'RECV-02' }),
      r('IN-005', 'PUT-2026-3321', 'Putaway', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'GR-2026-1038 → A01-R02-L03-B04', 'Lê Thu Hà', 'Normal', '2026-10-02T09:02:00Z', { productCode: 'SKU-1002', quantity: 240, uom: 'Hộp', location: 'A01-R02-L03-B04' }),
    ],
  },
  outbound: {
    moduleKey: 'outbound',
    snapshotAt,
    records: [
      r('OUT-001', 'SO-2026-22018', 'Sales Order', 'RELEASED', 'blue', 'WH-HCM-01', 'CUS-001 • giao TP.HCM', 'Trần Quốc Bảo', 'High', '2026-10-02T08:44:00Z', { partnerCode: 'CUS-001', productCode: 'SKU-1001', quantity: 160, uom: 'Gói' }),
      r('OUT-002', 'RSV-2026-9031', 'Reservation', 'ACTIVE', 'blue', 'WH-HCM-01', 'SO-2026-22018 • reserve 160', 'Trần Quốc Bảo', 'High', '2026-10-02T08:45:00Z', { productCode: 'SKU-1001', quantity: 160, uom: 'Gói' }),
      r('OUT-003', 'ALLOC-2026-7711', 'Allocation', 'ALLOCATED', 'blue', 'WH-HCM-01', 'A01-R02-L03-B04 • lot LOT-1001-260930', 'Phạm Gia Huy', 'High', '2026-10-02T08:47:00Z', { productCode: 'SKU-1001', quantity: 120, uom: 'Gói', location: 'A01-R02-L03-B04' }),
      r('OUT-004', 'PICK-2026-6110', 'Picking', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'Wave WV-2026-301 • 8 lines', 'Phạm Gia Huy', 'High', '2026-10-02T09:20:00Z', { productCode: 'SKU-1001', quantity: 120, uom: 'Gói' }),
      r('OUT-005', 'SHP-2026-5108', 'Shipment', 'LOADED', 'orange', 'WH-HCM-01', 'CUS-002 • chờ dispatch', 'Trần Quốc Bảo', 'Critical', '2026-10-02T09:25:00Z', { partnerCode: 'CUS-002', productCode: 'SKU-2002', quantity: 22, uom: 'Cái' }),
      r('OUT-006', 'TRK-SHP-5107', 'Shipment Tracking', 'IN_TRANSIT', 'blue', 'WH-HCM-01', 'SHP-2026-5107 • GHTK • ETA 14:30', 'Carrier Adapter', 'High', '2026-10-02T09:26:00Z', { partnerCode: 'CUS-001' }),
      r('OUT-007', 'POD-SHP-5088', 'Proof of Delivery', 'DELIVERED', 'green', 'WH-HCM-01', 'SHP-2026-5088 • receiver evidence attached', 'Carrier Adapter', 'Normal', '2026-10-02T08:32:00Z', { partnerCode: 'CUS-001' }),
    ],
  },
  'inventory-control': {
    moduleKey: 'inventory-control',
    snapshotAt,
    records: [
      r('INV-001', 'BAL-HCM-1001', 'Inventory Balance', 'AVAILABLE', 'green', 'WH-HCM-01', 'SKU-1001 • OnHand 1.250 / Available 1.030', 'System Projection', 'Normal', '2026-10-02T09:29:00Z', { productCode: 'SKU-1001', quantity: 1030, uom: 'Gói', location: 'MULTI' }),
      r('INV-002', 'LOT-1001-260930', 'Lot', 'AVAILABLE', 'green', 'WH-HCM-01', 'SKU-1001 • HSD 30/09/2027', 'System Projection', 'Normal', '2026-10-02T09:28:00Z', { productCode: 'SKU-1001', quantity: 720, uom: 'Gói', location: 'A01-R02-L03-B04' }),
      r('INV-003', 'SER-TWS-000128', 'Serial', 'QC_HOLD', 'orange', 'WH-HCM-01', 'SKU-2001 • chờ QC', 'Vũ Ngọc Lan', 'High', '2026-10-02T08:48:00Z', { productCode: 'SKU-2001', quantity: 1, uom: 'Cái', location: 'QC-01' }),
      r('INV-004', 'TXN-2026-98211', 'Ledger', 'POSTED', 'green', 'WH-HCM-01', 'RECEIPT +600 SKU-1002', 'Inventory Posting Engine', 'Normal', '2026-10-02T07:55:00Z', { productCode: 'SKU-1002', quantity: 600, uom: 'Hộp' }),
      r('INV-005', 'LOCK-2026-181', 'Inventory Lock', 'ACTIVE', 'red', 'WH-DN-01', 'SKU-4001 • freeze cycle count', 'Đỗ Minh Khang', 'High', '2026-10-02T09:00:00Z', { productCode: 'SKU-4001', quantity: 2400, uom: 'Chai' }),
      r('INV-006', 'RECON-HCM-0930', 'Inventory Reconciliation', 'HEALTHY', 'green', 'WH-HCM-01', 'Ledger = Balance • unexplained difference 0 • projection current', 'Integrity Monitor', 'Critical', '2026-10-02T09:30:00Z'),
    ],
  },
  'transfer-replenishment': {
    moduleKey: 'transfer-replenishment',
    snapshotAt,
    records: [
      r('TR-001', 'TRF-2026-0018', 'Transfer', 'IN_TRANSIT', 'blue', 'WH-HCM-01', 'HCM → Đà Nẵng • SKU-1001', 'Trần Quốc Bảo', 'High', '2026-10-02T07:30:00Z', { productCode: 'SKU-1001', quantity: 200, uom: 'Gói' }),
      r('TR-002', 'TRF-2026-0019', 'Transfer', 'IN_TRANSIT', 'blue', 'WH-DN-01', 'Đà Nẵng → Hà Nội • SKU-4001', 'Đỗ Minh Khang', 'High', '2026-10-02T08:20:00Z', { productCode: 'SKU-4001', quantity: 600, uom: 'Chai' }),
      r('TR-003', 'TRF-2026-0016', 'Transfer', 'RECEIVED', 'green', 'WH-DN-01', 'HCM → Đà Nẵng • SKU-2001', 'Đỗ Minh Khang', 'Normal', '2026-10-01T16:40:00Z', { productCode: 'SKU-2001', quantity: 10, uom: 'Cái' }),
      r('TR-004', 'REPL-2026-661', 'Replenishment', 'READY', 'orange', 'WH-HCM-01', 'Reserve A05 → Pick face P01', 'Phạm Gia Huy', 'High', '2026-10-02T09:17:00Z', { productCode: 'SKU-1001', quantity: 80, uom: 'Gói', location: 'PICK-P01' }),
    ],
  },
  'count-adjustment': {
    moduleKey: 'count-adjustment',
    snapshotAt,
    records: [
      r('CT-001', 'CC-2026-0142', 'Cycle Count', 'RECOUNT', 'orange', 'WH-HCM-01', 'SKU-1001 • Attempt 3 accepted 1.248', 'Trần Quốc Bảo', 'High', '2026-10-02T09:05:00Z', { productCode: 'SKU-1001', quantity: 1248, uom: 'Gói', location: 'A01-R02-L03-B04' }),
      r('CT-002', 'CC-2026-0143', 'Cycle Count', 'REVIEW', 'orange', 'WH-HCM-01', 'SKU-2001 • system 84 / counted 84', 'Trần Quốc Bảo', 'Normal', '2026-10-02T08:56:00Z', { productCode: 'SKU-2001', quantity: 84, uom: 'Cái' }),
      r('CT-003', 'FC-2026-0021', 'Full Count', 'COUNTING', 'blue', 'WH-DN-01', 'Zone C • blind count', 'Đỗ Minh Khang', 'High', '2026-10-02T09:18:00Z', { location: 'ZONE-C' }),
      r('CT-004', 'ADJ-2026-0088', 'Adjustment', 'PENDING_APPROVAL', 'orange', 'WH-HCM-01', 'Variance -2 SKU-1001', 'Trần Quốc Bảo', 'Critical', '2026-10-02T09:08:00Z', { productCode: 'SKU-1001', quantity: 2, uom: 'Gói', note: 'Reason: COUNT_VARIANCE' }),
    ],
  },
  'quality-returns': {
    moduleKey: 'quality-returns',
    snapshotAt,
    records: [
      r('QR-001', 'QC-2026-3301', 'QC Inspection', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'GR-2026-1045 • 48 serial tai nghe', 'Vũ Ngọc Lan', 'High', '2026-10-02T09:11:00Z', { productCode: 'SKU-2001', quantity: 48, uom: 'Cái' }),
      r('QR-002', 'QUA-2026-118', 'Quarantine', 'ACTIVE', 'red', 'WH-HCM-01', '20 hộp trà nghi lỗi bao bì', 'Vũ Ngọc Lan', 'Critical', '2026-10-02T08:22:00Z', { productCode: 'SKU-1002', quantity: 20, uom: 'Hộp', location: 'QUA-01' }),
      r('QR-003', 'RMA-2026-0711', 'Return', 'INSPECTION', 'orange', 'WH-HCM-01', 'CUS-001 trả 6 tai nghe', 'Vũ Ngọc Lan', 'High', '2026-10-02T08:40:00Z', { partnerCode: 'CUS-001', productCode: 'SKU-2001', quantity: 6, uom: 'Cái' }),
      r('QR-004', 'SCRAP-2026-0042', 'Scrap', 'PENDING_APPROVAL', 'orange', 'WH-DN-01', '12 chai vỡ • DAMAGE_FOUND', 'Đỗ Minh Khang', 'High', '2026-10-02T07:48:00Z', { productCode: 'SKU-4001', quantity: 12, uom: 'Chai' }),
    ],
  },
  'handling-packaging': {
    moduleKey: 'handling-packaging',
    snapshotAt,
    records: [
      r('HU-001', 'HU-00009182', 'Pallet', 'OPEN', 'blue', 'WH-HCM-01', 'Pallet receiving • 600 hộp trà', 'Lê Thu Hà', 'Normal', '2026-10-02T08:00:00Z', { productCode: 'SKU-1002', quantity: 600, uom: 'Hộp', location: 'RECV-02' }),
      r('HU-002', 'CTN-SHP-5108-01', 'Carton', 'PACKED', 'green', 'WH-HCM-01', 'SHP-2026-5108 • carton 1/2', 'Phạm Gia Huy', 'High', '2026-10-02T09:10:00Z', { productCode: 'SKU-2002', quantity: 12, uom: 'Cái', location: 'PACK-02' }),
      r('HU-003', 'LBL-2026-18821', 'Label', 'PRINTED', 'green', 'WH-HCM-01', 'SSCC 389385020000018821', 'Phạm Gia Huy', 'Normal', '2026-10-02T09:12:00Z'),
      r('HU-004', 'LBL-2026-18810', 'Label', 'REPRINT_REQUIRED', 'orange', 'WH-DN-01', 'Nhãn pallet mờ • yêu cầu reprint audit', 'Đỗ Minh Khang', 'Normal', '2026-10-02T08:50:00Z'),
    ],
  },
  'dock-yard-crossdock': {
    moduleKey: 'dock-yard-crossdock',
    snapshotAt,
    records: [
      r('DY-001', 'APT-2026-612', 'Appointment', 'CHECKED_IN', 'blue', 'WH-HCM-01', 'SUP-001 • xe 51C-882.41', 'Lê Thu Hà', 'High', '2026-10-02T08:50:00Z', { partnerCode: 'SUP-001' }),
      r('DY-002', 'DOCK-HCM-03', 'Dock Door', 'SERVICING', 'blue', 'WH-HCM-01', 'GR-2026-1048 • bắt đầu 09:05', 'Lê Thu Hà', 'High', '2026-10-02T09:05:00Z'),
      r('DY-003', 'YARD-HCM-Y07', 'Yard Slot', 'OCCUPIED', 'blue', 'WH-HCM-01', 'Trailer LOG-001 • chờ dock', 'Trần Quốc Bảo', 'Normal', '2026-10-02T08:42:00Z', { partnerCode: 'LOG-001' }),
      r('DY-004', 'XD-2026-0048', 'Cross-dock', 'MATCHED', 'green', 'WH-DN-01', 'ASN-812 → SO-22041 • không putaway', 'Đỗ Minh Khang', 'High', '2026-10-02T08:30:00Z', { productCode: 'SKU-4001', quantity: 240, uom: 'Chai' }),
    ],
  },
  'reports-analytics': {
    moduleKey: 'reports-analytics',
    snapshotAt,
    records: [
      r('RP-001', 'RPT-STOCK-HCM', 'Inventory Snapshot', 'READY', 'green', 'WH-HCM-01', 'Snapshot 09:30 • 6 SKU mẫu', 'System Report', 'Normal', '2026-10-02T09:30:00Z'),
      r('RP-002', 'RPT-IOT-202610', 'Xuất-Nhập-Tồn', 'READY', 'green', 'WH-HCM-01', 'Kỳ 01/10 → 02/10', 'System Report', 'Normal', '2026-10-02T09:30:00Z'),
      r('RP-003', 'RPT-AGING-001', 'Aging', 'WARNING', 'orange', 'WH-DN-01', '4 lot gần hạn trong 30 ngày', 'System Report', 'High', '2026-10-02T09:15:00Z'),
      r('RP-004', 'REC-LEDGER-0102', 'Reconciliation', 'MATCHED', 'green', 'WH-HCM-01', 'Ledger-derived balance = operational balance', 'Reconciliation Worker', 'Critical', '2026-10-02T09:00:00Z'),
      r('RP-005', 'KPI-TURNOVER-W40', 'Inventory Turnover', 'READY', 'green', 'WH-HCM-01', 'Stock turnover 4.8x • Days on Hand 23.4 ngày • grain 30d', 'Analytics Projection', 'Normal', '2026-10-02T09:05:00Z'),
      r('RP-006', 'BI-EXPORT-20261002', 'BI Export', 'PUBLISHED', 'green', 'WH-HCM-01', 'CDC lineage → semantic warehouse • snapshot 09:00', 'Analytics Pipeline', 'Normal', '2026-10-02T09:06:00Z'),
    ],
  },
  administration: {
    moduleKey: 'administration',
    snapshotAt,
    records: [
      r('AD-001', 'USR-U-HCM-MGR', 'User', 'ACTIVE', 'green', 'WH-HCM-01', 'Trần Quốc Bảo • Warehouse Manager', 'Nguyễn Minh Anh', 'Normal', '2026-10-01T13:00:00Z'),
      r('AD-002', 'PERM-receipt.post', 'Permission', 'CRITICAL', 'red', 'WH-HCM-01', 'receipt.post • requires warehouse scope', 'Nguyễn Minh Anh', 'Critical', '2026-10-01T12:20:00Z'),
      r('AD-003', 'APR-1182', 'Approval', 'PENDING', 'orange', 'WH-HCM-01', 'ADJ-2026-0088 • SoD enforced', 'Trần Quốc Bảo', 'Critical', '2026-10-02T09:10:00Z'),
      r('AD-004', 'AUD-2026-99182', 'Audit', 'RECORDED', 'green', 'WH-HCM-01', 'InventoryAdjustment.Submitted', 'Audit Service', 'Normal', '2026-10-02T09:10:01Z'),
      r('AD-005', 'SEQ-GR-HCM-2026', 'Number Sequence', 'ACTIVE', 'green', 'WH-HCM-01', 'GR-HCM-202610-000128 • next 000129', 'System Admin', 'Critical', '2026-10-02T08:00:00Z'),
      r('AD-006', 'RULE-PUTAWAY-COLD', 'Business Rule', 'ACTIVE', 'blue', 'WH-HCM-01', 'Cold-chain SKU → COLD zone • version 4', 'System Admin', 'High', '2026-10-01T16:30:00Z'),
      r('AD-007', 'ATT-QC-1048-01', 'Evidence', 'IMMUTABLE', 'green', 'WH-HCM-01', 'QC_PHOTO • GR-2026-1048 • checksum verified', 'Evidence Service', 'High', '2026-10-02T09:01:00Z'),
      r('AD-008', 'NTF-APPROVAL-1182', 'Notification', 'SENT', 'green', 'WH-HCM-01', 'Approval APR-1182 sắp quá SLA • Level 1', 'Notification Worker', 'High', '2026-10-02T09:11:00Z'),
      r('AD-009', 'LOC-VI-VN', 'Localization Pack', 'ACTIVE', 'green', 'WH-HCM-01', 'vi-VN • Asia/Ho_Chi_Minh • metric units • Unicode templates', 'Platform Admin', 'Normal', '2026-10-02T08:20:00Z'),
      r('AD-010', 'HOLD-LEGAL-2026-004', 'Legal Hold', 'ACTIVE', 'orange', 'WH-HCM-01', 'Shipment/POD + audit + attachments preserved until release', 'Compliance Officer', 'Critical', '2026-10-02T08:15:00Z'),
      r('AD-011', 'PRIV-REQ-2026-021', 'Privacy Request', 'UNDER_REVIEW', 'blue', 'WH-HCM-01', 'Partner contact export/redaction boundary • audit required', 'Privacy Officer', 'High', '2026-10-02T08:10:00Z'),
    ],
  },
  integration: {
    moduleKey: 'integration',
    snapshotAt,
    records: [
      r('IG-001', 'MSG-ERP-81192', 'Inbox', 'PROCESSED', 'green', 'WH-HCM-01', 'PurchaseOrder.Updated v3', 'Integration Worker', 'Normal', '2026-10-02T09:20:00Z'),
      r('IG-002', 'EVT-2026-77120', 'Outbox', 'PENDING', 'orange', 'WH-HCM-01', 'GoodsReceiptPosted v1', 'Outbox Worker', 'High', '2026-10-02T09:24:02Z'),
      r('IG-003', 'EVT-2026-77118', 'Outbox', 'SENT', 'green', 'WH-HCM-01', 'ShipmentDispatched v1', 'Outbox Worker', 'Normal', '2026-10-02T09:02:00Z'),
      r('IG-004', 'DLQ-2026-091', 'Dead Letter', 'FAILED', 'red', 'WH-DN-01', 'Carrier label callback • HTTP 503', 'Integration Worker', 'Critical', '2026-10-02T08:55:00Z'),
      r('IG-005', 'IMP-PRODUCT-20261002', 'Import Batch', 'VALIDATED', 'green', 'WH-HCM-01', 'Product import • 48/50 valid • preview ready', 'Import Worker', 'Normal', '2026-10-02T08:35:00Z'),
      r('IG-006', 'FIN-CUTOFF-202609', 'Finance / Costing Interface', 'RECONCILED', 'green', 'WH-HCM-01', 'Sep cutoff • movement totals exported • finance ack matched', 'Finance Adapter', 'Critical', '2026-10-02T08:30:00Z'),
      r('IG-007', 'INT-RECON-20261002', 'Integration Reconciliation', 'MISMATCH', 'orange', 'WH-DN-01', 'Source 428 • WMS 428 • target ack 427 • 1 retry pending', 'Integration Control', 'Critical', '2026-10-02T08:25:00Z'),
    ],
  },
  mobile: {
    moduleKey: 'mobile',
    snapshotAt,
    records: [
      r('MO-001', 'MOB-RCV-1048', 'Receiving Task', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'Quét GR-2026-1048 tại RECV-01', 'Lê Thu Hà', 'High', '2026-10-02T09:24:00Z', { productCode: 'SKU-1001', location: 'RECV-01' }),
      r('MO-002', 'MOB-PICK-6110', 'Picking Task', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'A01-R02-L03-B04 → PACK-02', 'Phạm Gia Huy', 'High', '2026-10-02T09:20:00Z', { productCode: 'SKU-1001', quantity: 120, uom: 'Gói' }),
      r('MO-003', 'MOB-COUNT-0142', 'Count Task', 'RECOUNT', 'orange', 'WH-HCM-01', 'Blind recount SKU-1001', 'Trần Quốc Bảo', 'High', '2026-10-02T09:05:00Z', { productCode: 'SKU-1001' }),
      r('MO-004', 'OFF-QUEUE-021', 'Offline Queue', 'PENDING_SYNC', 'orange', 'WH-DN-01', '2 low-risk confirmations chờ mạng', 'Đỗ Minh Khang', 'Normal', '2026-10-02T09:14:00Z'),
      r('MO-005', 'SYNC-FAIL-008', 'Sync Error', 'RETRYABLE', 'red', 'WH-DN-01', 'PUT-2026-3308 • version conflict', 'Đỗ Minh Khang', 'High', '2026-10-02T09:15:00Z'),
    ],
  },
  'operations-resilience': {
    moduleKey: 'operations-resilience',
    snapshotAt,
    records: [
      r('OP-001', 'HEALTH-HCM-0930', 'Health Check', 'HEALTHY', 'green', 'WH-HCM-01', 'API / SQL / Outbox / Integrity OK', 'Operations', 'Critical', '2026-10-02T09:30:00Z'),
      r('OP-002', 'BACKUP-20261002-0200', 'Backup', 'VERIFIED', 'green', 'WH-HCM-01', 'Full backup + restore verification', 'Operations', 'Critical', '2026-10-02T03:12:00Z'),
      r('OP-003', 'REC-DR-2026-004', 'Recovery Reconciliation', 'READY_TO_REOPEN', 'green', 'WH-DN-01', 'Ledger/balance mismatch = 0', 'Operations', 'Critical', '2026-10-01T18:00:00Z'),
      r('OP-004', 'ALERT-OUTBOX-002', 'Alert', 'WARNING', 'orange', 'WH-DN-01', 'Outbox backlog 18 messages', 'Operations', 'High', '2026-10-02T09:21:00Z'),
      r('OP-005', 'TEL-UX-PICK-W40', 'Feature Telemetry', 'HEALTHY', 'green', 'WH-HCM-01', 'Picking scan p95 420 ms • retry 1.3% • no business KPI use', 'Product Operations', 'Normal', '2026-10-02T09:18:00Z'),
      r('OP-006', 'OFFBOARD-WH-DL-01', 'Warehouse Offboarding', 'PLANNED', 'blue', 'WH-DL-01', 'Inventory zero/reconcile → integrations stop → evidence/data exit', 'Operations', 'Critical', '2026-10-02T07:40:00Z'),
      r('OP-007', 'REPAIR-CASE-2026-018', 'Controlled Repair', 'PENDING_APPROVAL', 'orange', 'WH-HCM-01', 'Posted receipt error → reversal + corrected transaction plan', 'Production Support', 'Critical', '2026-10-02T07:35:00Z'),
      r('OP-008', 'SUPPORT-CASE-2026-221', 'Support Tooling', 'INVESTIGATING', 'blue', 'WH-HCM-01', 'Trace document → ledger → outbox without direct DB write', 'Production Support', 'High', '2026-10-02T07:30:00Z'),
    ],
  },
  'advanced-planning': {
    moduleKey: 'advanced-planning',
    snapshotAt,
    records: [
      r('AX-001', 'SLOT-REC-441', 'Slotting', 'RECOMMENDED', 'blue', 'WH-HCM-01', 'SKU-1001 → Pick face P01', 'Planning Engine', 'Normal', '2026-10-02T08:00:00Z', { productCode: 'SKU-1001', location: 'PICK-P01' }),
      r('AX-002', 'CAP-FCST-DN-W41', 'Capacity Forecast', 'WARNING', 'orange', 'WH-DN-01', 'Khu lạnh dự báo 96% sau 3 ngày', 'Planning Engine', 'High', '2026-10-02T07:30:00Z'),
      r('AX-003', 'NET-REBAL-020', 'Network Planning', 'PROPOSED', 'blue', 'WH-HCM-01', 'HCM → Đà Nẵng 200 SKU-1001', 'Planning Engine', 'Normal', '2026-10-02T07:20:00Z', { productCode: 'SKU-1001', quantity: 200, uom: 'Gói' }),
      r('AX-004', '3PL-BILL-SEP', '3PL Billing', 'DRAFT', 'gray', 'WH-HCM-01', 'Owner ACME • storage/handling draft', 'Billing Projection', 'Low', '2026-10-01T23:00:00Z'),
      r('AX-005', 'MHE-FL-HCM-07', 'Equipment', 'AVAILABLE', 'green', 'WH-HCM-01', 'Forklift 07 • zone A/C • maintenance due 18/10', 'Asset Registry', 'Normal', '2026-10-02T07:10:00Z'),
      r('AX-006', 'KIT-2026-0042', 'Kitting Order', 'RELEASED', 'blue', 'WH-HCM-01', 'Bundle PROMO-COFFEE-02 • components reserved', 'Kitting Engine', 'Normal', '2026-10-02T07:00:00Z', { productCode: 'SKU-1001', quantity: 20, uom: 'Bộ' }),
      r('AX-007', 'VAS-2026-0018', 'VAS Order', 'IN_PROGRESS', 'blue', 'WH-HCM-01', 'Relabel 60 cartons • customer ACME', 'VAS Work Center', 'Normal', '2026-10-02T06:55:00Z'),
      r('AX-008', 'CARTON-REC-SHP5108', 'Cartonization', 'RECOMMENDED', 'blue', 'WH-HCM-01', '2 × PKG-CARTON-M • 84% cube utilization • no auto-pack', 'Optimization Engine', 'Normal', '2026-10-02T06:50:00Z'),
      r('AX-009', 'LOAD-PLAN-TRAILER12', 'Load Plan', 'PROPOSED', 'blue', 'WH-HCM-01', 'Trailer 12 • 78% cube • 4 load sequence groups', 'Optimization Engine', 'High', '2026-10-02T06:45:00Z'),
      r('AX-010', 'REVLOG-RMA-0288', 'Reverse Logistics', 'DISPOSITION_PENDING', 'orange', 'WH-HCM-01', 'RMA-2026-0288 • refurbish / RTV / scrap options', 'Returns Work Center', 'High', '2026-10-02T06:40:00Z'),
      r('AX-011', 'SAFE-RULE-HVY-20KG', 'Safety Rule', 'ACTIVE', 'green', 'WH-HCM-01', 'Heavy-lift >20 kg requires qualified equipment/operator', 'Safety Policy', 'Critical', '2026-10-02T06:35:00Z'),
      r('AX-012', 'WCS-JOB-8812', 'WCS / Robotics Job', 'READY', 'green', 'WH-HCM-01', 'PICK-2026-6110 → AMR job • callback command not inventory truth', 'Automation Gateway', 'High', '2026-10-02T06:30:00Z'),
      r('AX-013', 'RFID-EPC-301188', 'RFID Capture', 'CAPTURED', 'green', 'WH-HCM-01', 'Gate G02 • EPC deduped • device SCN-RFID-02', 'IoT Gateway', 'Normal', '2026-10-02T06:25:00Z'),
      r('AX-014', 'VOICE-PICK-6110', 'Assisted Picking', 'ENABLED', 'green', 'WH-HCM-01', 'Voice prompt + pick-to-light • task semantics unchanged', 'Assist Device Gateway', 'Normal', '2026-10-02T06:20:00Z'),
      r('AX-015', 'HAZMAT-UN1993', 'Hazmat Policy', 'ACTIVE', 'orange', 'WH-HCM-01', 'UN1993 • restricted zone HZ-01 • segregation required', 'Compliance Rules', 'Critical', '2026-10-02T06:15:00Z'),
      r('AX-016', 'COLD-EXC-2026-014', 'Cold Chain Excursion', 'QUARANTINE_REQUIRED', 'red', 'WH-DN-01', '8.9°C for 17 min • evidence captured • disposition pending', 'Cold Chain Monitor', 'Critical', '2026-10-02T06:10:00Z'),
      r('AX-017', 'CW-CAPTURE-7712', 'Catch Weight', 'CAPTURED', 'green', 'WH-HCM-01', '10 Thùng • 124.7 kg actual • dual-UOM tolerance valid', 'Warehouse Operator', 'Normal', '2026-10-02T06:05:00Z'),
      r('AX-018', 'OWN-CONSIGN-SUP001', 'Consignment Inventory', 'ACTIVE', 'green', 'WH-HCM-01', 'Owner SUP-001 • physical custody WMS • explicit reclassification only', 'Inventory Owner Service', 'High', '2026-10-02T06:00:00Z', { partnerCode: 'SUP-001' }),
      r('AX-019', '3PL-CONTRACT-ACME-2026', '3PL Contract / Rating', 'ACTIVE', 'green', 'WH-HCM-01', 'Storage + handling + VAS rating • reconciliation evidence ready', '3PL Billing', 'High', '2026-10-02T05:55:00Z'),
      r('AX-020', 'ATP-SKU1001-HCM', 'ATP / CTP', 'PROMISE_READY', 'green', 'WH-HCM-01', 'ATP 320 Gói • CTP 440 Gói • promise 04/10/2026', 'Promise Engine', 'High', '2026-10-02T05:50:00Z', { productCode: 'SKU-1001' }),
      r('AX-021', 'BAL-NET-W40', 'Network Balancing', 'RECOMMENDED', 'blue', 'WH-HCM-01', 'HCM surplus 240 → DN shortage 180 • transfer economics positive', 'Planning Engine', 'High', '2026-10-02T05:45:00Z', { productCode: 'SKU-1001' }),
      r('AX-022', 'REPL-NET-DN-W41', 'Inter-Warehouse Replenishment', 'PROPOSED', 'blue', 'WH-DN-01', 'Replenish 180 Gói from HCM • execution requires transfer', 'Planning Engine', 'High', '2026-10-02T05:40:00Z', { productCode: 'SKU-1001', quantity: 180, uom: 'Gói' }),
      r('AX-023', 'ROUTE-SO-9021', 'Fulfillment Routing', 'SELECTED', 'green', 'WH-HCM-01', 'SO-2026-9021 → WH-HCM-01 • availability/SLA/capacity score 92', 'Routing Engine', 'High', '2026-10-02T05:35:00Z'),
      r('AX-024', 'FCST-SKU1001-W42', 'Forecast Signal', 'INGESTED', 'green', 'WH-HCM-01', 'Demand plan W42 • 1,240 Gói • source ERP Planning', 'Forecast Adapter', 'Normal', '2026-10-02T05:30:00Z', { productCode: 'SKU-1001' }),
      r('AX-025', 'SS-ROP-SKU1001', 'Safety Stock Policy', 'ACTIVE', 'green', 'WH-HCM-01', 'Safety 300 • ROP 480 • Min/Max 360/960', 'Policy Engine', 'High', '2026-10-02T05:25:00Z', { productCode: 'SKU-1001' }),
      r('AX-026', 'ABCXYZ-SKU1001', 'ABC / XYZ Classification', 'A_X', 'green', 'WH-HCM-01', 'ABC=A • XYZ=X • velocity high • variability low', 'Classification Worker', 'Normal', '2026-10-02T05:20:00Z', { productCode: 'SKU-1001' }),
      r('AX-027', 'OPT-POL-SKU1001', 'Inventory Optimization', 'RECOMMENDED', 'blue', 'WH-HCM-01', 'Raise safety stock +12% • expiry/capacity constraints satisfied', 'Optimization Engine', 'Normal', '2026-10-02T05:15:00Z', { productCode: 'SKU-1001' }),
      r('AX-028', 'SIM-REBAL-2026-011', 'Network Rebalancing Simulation', 'SIMULATED', 'blue', 'WH-HCM-01', 'Scenario B reduces projected shortage 37% • no production mutation', 'Simulation Engine', 'Normal', '2026-10-02T05:10:00Z'),
      r('AX-029', 'FCST-ACC-W39', 'Forecast Accuracy', 'MEASURED', 'green', 'WH-HCM-01', 'WAPE 18.2% • Bias -3.1% • segment A/X', 'Analytics Projection', 'Normal', '2026-10-02T05:05:00Z'),
      r('AX-030', 'REPL-EXC-0041', 'Replenishment Exception', 'NO_SOURCE', 'orange', 'WH-DN-01', 'Pick face below min • no eligible source • escalation pending', 'Exception Engine', 'High', '2026-10-02T05:00:00Z'),
      r('AX-031', 'PROC-SUG-0088', 'Procurement Suggestion', 'PENDING_APPROVAL', 'orange', 'WH-HCM-01', 'Suggest +600 Gói • projected below reorder threshold', 'Planning Engine', 'High', '2026-10-02T04:55:00Z', { productCode: 'SKU-1001', quantity: 600, uom: 'Gói' }),
      r('AX-032', 'RISK-SKU1001-HCM', 'Inventory Risk Score', 'HIGH', 'orange', 'WH-HCM-01', 'Risk 78/100 • stockout 0.42 • supplier variability elevated', 'Risk Engine', 'High', '2026-10-02T04:50:00Z', { productCode: 'SKU-1001' }),
      r('AX-033', 'LEADTIME-SUP001', 'Supplier Lead-Time Intelligence', 'MEASURED', 'green', 'WH-HCM-01', 'Median 5.2d • P90 7.8d • reliability 91%', 'Supplier Analytics', 'Normal', '2026-10-02T04:45:00Z', { partnerCode: 'SUP-001' }),
      r('AX-034', 'EXPEDITE-PO-4412', 'Expedite / Defer Recommendation', 'EXPEDITE', 'blue', 'WH-HCM-01', 'Expedite PO-4412 by 2 days • service-risk reduction 14%', 'Planning Engine', 'High', '2026-10-02T04:40:00Z'),
      r('AX-035', 'ANOM-DEMAND-771', 'Demand Anomaly', 'SPIKE', 'orange', 'WH-HCM-01', 'Demand +240% vs baseline • classification BUSINESS_SPIKE', 'Anomaly Worker', 'High', '2026-10-02T04:35:00Z'),
      r('AX-036', 'FAIR-ALLOC-2026-19', 'Allocation Fairness', 'PROPOSED', 'blue', 'WH-HCM-01', '3 orders • service class + fairness weights • deterministic proposal', 'Allocation Policy', 'High', '2026-10-02T04:30:00Z'),
      r('AX-037', 'SVC-GOLD-01', 'Service-Level Segment', 'ACTIVE', 'green', 'WH-HCM-01', 'GOLD • fulfillment SLA 4h • target fill rate 98%', 'Policy Registry', 'Normal', '2026-10-02T04:25:00Z'),
      r('AX-038', 'WHATIF-CAP-W42', 'What-if Scenario', 'SIMULATED', 'blue', 'WH-HCM-01', 'Demand +25% / capacity -10% • no production mutation', 'Simulation Engine', 'Normal', '2026-10-02T04:20:00Z'),
      r('AX-039', 'POLICY-REPL-V7', 'Decision Policy', 'ACTIVE', 'green', 'WH-HCM-01', 'replenishment.minmax v7 • effective 01/10 • human approval required', 'Policy Registry', 'Critical', '2026-10-02T04:15:00Z'),
    ],
  },
};

export const getMockWorkCenter = (moduleKey?: string) =>
  moduleKey ? mockWorkCenters[moduleKey] : undefined;

export const mockRecordCount = Object.values(mockWorkCenters)
  .reduce((total, workCenter) => total + workCenter.records.length, 0);


export interface MockCapabilityFixture {
  fixtureId: string;
  capabilityId: string;
  moduleKey: string;
  capabilityName: string;
  implementationStatus: string;
  spec: string;
  sampleRecordId: string;
  sampleReference: string;
  sampleWarehouse: string;
  sampleStatus: string;
}

export const mockCapabilitySampleRecordIds: Record<string, string> = {
  'OV-06': 'OV-005',
  'OV-07': 'OV-006',
  'MD-08': 'MD-005',
  'OUT-10': 'OUT-006',
  'INV-11': 'INV-006',
  'AD-07': 'AD-008',
  'AD-09': 'AD-005',
  'AD-10': 'AD-006',
  'AD-11': 'AD-007',
  'IG-08': 'IG-005',
  'AX-08': 'AX-005',
  'AX-09': 'AX-006',
  'AX-10': 'AX-007',
  'RP-09': 'RP-005',
  'RP-10': 'RP-006',
  'AD-12': 'AD-009',
  'AD-13': 'AD-010',
  'AD-14': 'AD-011',
  'OP-08': 'OP-005',
  'OP-09': 'OP-006',
  'AX-11': 'AX-008',
  'AX-12': 'AX-009',
  'AX-13': 'AX-010',
  'AX-14': 'AX-011',
  'AX-15': 'AX-012',
  'AX-16': 'AX-013',
  'AX-17': 'AX-014',
  'AX-18': 'AX-015',
  'AX-19': 'AX-016',
  'AX-20': 'AX-017',
  'AX-21': 'AX-018',
  'AX-22': 'AX-019',
  'AX-23': 'AX-020',
  'AX-24': 'AX-021',
  'AX-25': 'AX-022',
  'AX-26': 'AX-023',
  'AX-27': 'AX-024',
  'AX-28': 'AX-025',
  'AX-29': 'AX-026',
  'AX-30': 'AX-027',
  'AX-31': 'AX-028',
  'AX-32': 'AX-029',
  'AX-33': 'AX-030',
  'AX-34': 'AX-031',
  'AX-35': 'AX-032',
  'AX-36': 'AX-033',
  'AX-37': 'AX-034',
  'AX-38': 'AX-035',
  'AX-39': 'AX-036',
  'AX-40': 'AX-037',
  'AX-41': 'AX-038',
  'AX-42': 'AX-039',
  'MD-09': 'MD-006',
  'WH-07': 'WH-005',
  'IG-09': 'IG-006',
  'IG-10': 'IG-007',
  'OP-10': 'OP-007',
  'OP-11': 'OP-008',
};

export const mockCapabilityFixtures: Record<string, MockCapabilityFixture> = Object.fromEntries(
  erpWmsBlueprint.flatMap((module) => {
    const records = mockWorkCenters[module.key]?.records ?? [];
    return module.capabilities.map((capability, index) => {
      const preferredRecordId = mockCapabilitySampleRecordIds[capability.id];
      const sample = records.find((record) => record.id === preferredRecordId)
        ?? records[index % Math.max(records.length, 1)];
      return [
        capability.id,
        {
          fixtureId: `FX-${capability.id}`,
          capabilityId: capability.id,
          moduleKey: module.key,
          capabilityName: capability.name,
          implementationStatus: capability.status,
          spec: capability.spec,
          sampleRecordId: sample?.id ?? 'NO-SAMPLE',
          sampleReference: sample?.reference ?? 'NO-SAMPLE',
          sampleWarehouse: sample?.warehouse ?? 'NO-SAMPLE',
          sampleStatus: sample?.status ?? 'NO-SAMPLE',
        },
      ];
    });
  }),
);

export const getMockCapabilityFixture = (capabilityId?: string) =>
  capabilityId ? mockCapabilityFixtures[capabilityId] : undefined;
