import { mockInventoryBalances, mockPartners, mockProducts, mockWarehouses } from './erpWmsMockData';

export const demoUnits = [
  { id: 1, code: 'GOI', name: 'Gói', isActive: true },
  { id: 2, code: 'HOP', name: 'Hộp', isActive: true },
  { id: 3, code: 'CAI', name: 'Cái', isActive: true },
  { id: 4, code: 'CHAI', name: 'Chai', isActive: true },
  { id: 5, code: 'THUNG', name: 'Thùng', isActive: true },
];

const unitByName: Record<string, number> = { Gói: 1, Hộp: 2, Cái: 3, Chai: 4, Thùng: 5 };
const categories = [
  { id: 1, code: 'FOOD', name: 'Thực phẩm', isActive: true },
  { id: 2, code: 'ELEC', name: 'Điện tử', isActive: true },
  { id: 3, code: 'PACK', name: 'Bao bì', isActive: true },
  { id: 4, code: 'DRINK', name: 'Đồ uống', isActive: true },
];
const categoryByName: Record<string, number> = { 'Thực phẩm': 1, 'Điện tử': 2, 'Bao bì': 3, 'Đồ uống': 4 };

export const demoWarehouses = mockWarehouses.map((warehouse, index) => ({
  id: index + 1,
  code: warehouse.code,
  name: warehouse.name,
  address: warehouse.city,
  isActive: true,
}));

export const demoWarehouseStructures = demoWarehouses.map((warehouse, index) => {
  const base = (index + 1) * 1000;
  const rackCode = index === 0 ? 'R02' : 'R01';
  const levelNo = index === 0 ? 3 : 1;

  const location = (
    offset: number,
    code: string,
    name: string,
    overrides: Partial<{
      zoneId: number | null;
      zoneCode: string | null;
      rackLevelId: number | null;
      aisleCode: string | null;
      rackCode: string | null;
      levelNo: number | null;
      locationType: string;
      storageClass: string | null;
      maxWeightKg: number | null;
      maxVolumeM3: number | null;
      maxPalletEquivalent: number | null;
      mapX: number | null;
      mapY: number | null;
      mapWidth: number | null;
      mapHeight: number | null;
      isActive: boolean;
      isBlocked: boolean;
      isPickable: boolean;
      isReceivable: boolean;
      isSystemManaged: boolean;
    }> = {},
  ) => ({
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
    barcode: 'LOC-' + warehouse.code + '-' + code,
    pickPriority: 10,
    putawayPriority: 10,
    locationType: 'Storage',
    storageClass: null,
    maxWeightKg: null,
    maxVolumeM3: null,
    maxPalletEquivalent: null,
    mapX: null,
    mapY: null,
    mapWidth: null,
    mapHeight: null,
    isActive: true,
    isBlocked: false,
    isPickable: true,
    isReceivable: false,
    isSystemManaged: false,
    ...overrides,
  });

  const physicalLocations = index === 0
    ? [
        location(101, 'A01-R02-L03-B04', 'Ô lưu trữ chính B04', { storageClass: 'AMBIENT', maxWeightKg: 1500, maxVolumeM3: 10, maxPalletEquivalent: 5, mapX: 6, mapY: 10, mapWidth: 18, mapHeight: 20 }),
        location(102, 'A01-R02-L03-B05', 'Ô dự trữ B05', { isPickable: false, storageClass: 'AMBIENT', maxWeightKg: 1500, maxVolumeM3: 10, maxPalletEquivalent: 5, mapX: 28, mapY: 10, mapWidth: 18, mapHeight: 20 }),
        location(103, 'A01-R02-L03-B06', 'Ô đang khóa B06', { isBlocked: true, mapX: 50, mapY: 10, mapWidth: 18, mapHeight: 20 }),
        location(104, 'A01-R02-L03-B07', 'Ô ngừng hoạt động B07', { isActive: false, mapX: 72, mapY: 10, mapWidth: 18, mapHeight: 20 }),
        location(105, 'A01-R02-L03-B08', 'Ô hàng hư hỏng B08', { locationType: 'Damaged', isPickable: false }),
        location(106, 'A01-R02-L03-B09', 'Ô hàng bị từ chối B09', { locationType: 'Rejected', isPickable: false }),
      ]
    : [location(101, 'A01-R01-L01-B01', 'Ô lưu trữ 01')];

  const unmappedLocations = index === 0
    ? [location(901, 'LEGACY-UNMAPPED-01', 'Vị trí lịch sử chưa gắn cấu trúc', {
        zoneId: null,
        zoneCode: null,
        rackLevelId: null,
        aisleCode: null,
        rackCode: null,
        levelNo: null,
        isPickable: false,
      })]
    : [];

  return {
    warehouseId: warehouse.id,
    warehouseCode: warehouse.code,
    warehouseName: warehouse.name,
    zones: [{
      id: base + 10,
      warehouseId: warehouse.id,
      code: 'ZONE-A',
      name: index === 0 ? 'Khu lưu trữ A' : 'Khu lưu trữ chính',
      zoneType: 'STORAGE',
      pickPriority: 10,
      putawayPriority: 10,
      isActive: true,
      aisles: [{
        id: base + 20,
        zoneId: base + 10,
        code: 'A01',
        name: 'Dãy 01',
        racks: [{
          id: base + 30,
          aisleId: base + 20,
          code: rackCode,
          name: index === 0 ? 'Kệ 02' : 'Kệ 01',
          levels: [{
            id: base + 40,
            rackId: base + 30,
            levelNo,
            locations: physicalLocations,
          }],
        }],
      }],
      locations: [],
    }],
    unmappedLocations,
    systemLocations: [
      location(1, 'RECEIVING', 'Vị trí nhận hàng', {
        zoneId: null, zoneCode: null, rackLevelId: null, aisleCode: null, rackCode: null, levelNo: null,
        locationType: 'Receiving', isPickable: false, isReceivable: true, isSystemManaged: true,
      }),
      location(2, 'LEGACY', 'Tồn kho kế thừa', {
        zoneId: null, zoneCode: null, rackLevelId: null, aisleCode: null, rackCode: null, levelNo: null,
        locationType: 'Legacy', isPickable: true, isSystemManaged: true,
      }),
    ],
  };
});

export const demoProducts = mockProducts.map((product, index) => {
  const unitId = unitByName[product.baseUom] ?? 3;
  const categoryId = categoryByName[product.category] ?? null;
  const profiles = [
    { storageClass: 'AMBIENT', unitWeightKg: 0.5, unitVolumeM3: 0.002, unitPalletEquivalent: 0.02 },
    { storageClass: 'ELECTRONICS', unitWeightKg: 1.2, unitVolumeM3: 0.004, unitPalletEquivalent: 0.03 },
    { storageClass: 'AMBIENT', unitWeightKg: 0.08, unitVolumeM3: 0.001, unitPalletEquivalent: 0.005 },
    { storageClass: 'AMBIENT', unitWeightKg: 0.65, unitVolumeM3: 0.0015, unitPalletEquivalent: 0.015 },
  ];
  const profile = profiles[index % profiles.length];
  return {
    id: index + 1,
    code: product.code,
    name: product.name,
    description: product.tracking === 'None' ? 'Không theo dõi lot/serial' : 'Theo dõi ' + product.tracking,
    ...profile,
    unitId,
    unitCode: demoUnits.find(item => item.id === unitId)?.code ?? 'CAI',
    unitName: product.baseUom,
    unitDecimalPlaces: 0,
    categoryId,
    categoryName: categories.find(item => item.id === categoryId)?.name ?? null,
    barcodes: product.barcodes.map((value, barcodeIndex) => ({ id: index * 10 + barcodeIndex + 1, productId: index + 1, value })),
    isActive: true,
    uoms: [{ unitId, unitCode: demoUnits.find(item => item.id === unitId)?.code ?? 'CAI', unitName: product.baseUom, decimalPlaces: 0, conversionFactor: 1, version: 1 }],
  };
});

export const demoCategories = categories;

export const demoPartners = mockPartners.map((partner, index) => ({
  id: index + 1,
  code: partner.code,
  name: partner.name,
  isSupplier: partner.roles.includes('Supplier'),
  isCustomer: partner.roles.includes('Customer'),
  isActive: true,
  phone: index < 2 ? '090000000' + (index + 1) : undefined,
  email: 'demo' + (index + 1) + '@example.local',
  address: index < 3 ? 'Dữ liệu demo' : undefined,
  rowVersion: 'AAAAAAAAAA' + (index + 1),
}));

export const demoInventoryStocks = mockInventoryBalances.map((balance) => {
  const warehouse = demoWarehouses.find(item => item.code === balance.warehouse)!;
  const product = demoProducts.find(item => item.code === balance.productCode)!;
  return {
    productId: product.id,
    productCode: product.code,
    productName: product.name,
    unitName: product.unitName,
    warehouseId: warehouse.id,
    warehouseName: warehouse.name,
    currentQuantity: balance.onHand,
    reservedQuantity: balance.reserved,
    availableQuantity: balance.available,
    lastUpdated: '2026-10-03T12:30:00Z',
  };
});

export const demoInventoryTransactions = [
  { id: 1, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'DC Hồ Chí Minh', transactionType: 'Import', quantity: 600, referenceId: 1048, referenceType: 'ImportReceipt', transactionDate: '2026-10-03T08:20:00Z', createdBy: 1, createdByName: 'Demo Operator', note: 'Receipt posted' },
  { id: 2, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', warehouseId: 1, warehouseName: 'DC Hồ Chí Minh', transactionType: 'Export', quantity: -120, referenceId: 5108, referenceType: 'ExportReceipt', transactionDate: '2026-10-03T09:10:00Z', createdBy: 1, createdByName: 'Demo Operator', note: 'Shipment dispatched' },
];

export const demoInOut = demoInventoryStocks.map(stock => ({
  productId: stock.productId,
  productCode: stock.productCode,
  productName: stock.productName,
  unitName: stock.unitName,
  warehouseId: stock.warehouseId,
  warehouseName: stock.warehouseName,
  openingQuantity: Math.max(0, stock.currentQuantity - 100),
  inQuantity: 160,
  outQuantity: 60,
  closingQuantity: stock.currentQuantity,
}));

export const demoApprovalQueue = [
  { documentType: 'ImportReceipt', documentId: 1041, documentCode: 'GR-2026-1041', pendingState: 'ReadyToPost', creatorName: 'Lê Thu Hà', requestedAtUtc: '2026-10-03T11:05:00Z', waitingMinutes: 44, slaStatus: 'Warning', warehouseName: 'DC Hồ Chí Minh', totalQuantity: 2400, canApprove: false, canReject: false },
  { documentType: 'Stocktake', documentId: 142, documentCode: 'CC-2026-0142', pendingState: 'Review', creatorName: 'Trần Quốc Bảo', requestedAtUtc: '2026-10-03T10:10:00Z', waitingMinutes: 99, slaStatus: 'Overdue', warehouseName: 'DC Hồ Chí Minh', totalQuantity: 1248, canApprove: false, canReject: false },
];

export const demoTransfers = [
  { id: 18, code: 'TRF-2026-0018', sourceWarehouseId: 1, sourceWarehouseName: 'DC Hồ Chí Minh', destinationWarehouseId: 2, destinationWarehouseName: 'Kho Đà Nẵng', status: 'InTransit', note: 'Demo transfer', createdBy: 1, createdAt: '2026-10-03T07:30:00Z', dispatchedAt: '2026-10-03T08:00:00Z', details: [{ productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', requestedQuantity: 200, dispatchedQuantity: 200, receivedQuantity: 0, missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 200 }] },
  { id: 16, code: 'TRF-2026-0016', sourceWarehouseId: 1, sourceWarehouseName: 'DC Hồ Chí Minh', destinationWarehouseId: 2, destinationWarehouseName: 'Kho Đà Nẵng', status: 'Received', note: 'Demo transfer received', createdBy: 1, createdAt: '2026-10-02T10:30:00Z', receivedAt: '2026-10-03T06:40:00Z', details: [{ productId: 3, productCode: 'SKU-2001', productName: 'Tai nghe Bluetooth TWS', requestedQuantity: 10, dispatchedQuantity: 10, receivedQuantity: 10, missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 0 }] },
];

export const demoPutawayTasks = [
  { id: 3321, receiptCode: 'GR-2026-1038', warehouseName: 'DC Hồ Chí Minh', status: 'InProgress', assignedUserId: 101, requiredBaseQuantity: 240, movedBaseQuantity: 120, rowVersion: 'AAAAAAAAPUT1', items: [{ id: 1, productCode: 'SKU-1002', productName: 'Trà Ô Long 250g', inventoryStatus: 'Available', sourceLocationCode: 'RECV-01', operationUnitCode: 'HOP', baseUnitCode: 'HOP', requiredOperationQuantity: 240, requiredBaseQuantity: 240, movedBaseQuantity: 120, remainingBaseQuantity: 120 }] },
];

export const demoImportReceipts = [
  { id: 1048, code: 'GR-2026-1048', warehouseId: 1, warehouseName: 'DC Hồ Chí Minh', status: 'Received', note: 'PO-2026-4521', createdBy: 101, createdByName: 'Lê Thu Hà', createdAt: '2026-10-03T08:00:00Z', approvedBy: 0, approvedByName: '', approvedAt: '', supplierId: 1, supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên', requiresQc: false, details: [{ id: 1, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', operationUnitId: 1, operationUnitCode: 'GOI', baseUnitCode: 'GOI', conversionFactor: 1, conversionVersion: 1, expectedQuantity: 1200, receivedQuantity: 1200, acceptedQuantity: 1200, damagedQuantity: 0, rejectedQuantity: 0, postedQuantity: 0, baseExpectedQuantity: 1200, baseReceivedQuantity: 1200, baseAcceptedQuantity: 1200, basePostedQuantity: 0, note: '', requiresQc: false, qcState: 'NotRequired', observedQuantity: 1200, doorRejectedQuantity: 0, finalReceivedQuantity: 1200, baseFinalReceivedQuantity: 1200 }] },
  { id: 1041, code: 'GR-2026-1041', warehouseId: 2, warehouseName: 'Kho Đà Nẵng', status: 'ReadyToPost', note: 'QC balanced', createdBy: 102, createdByName: 'Đỗ Minh Khang', createdAt: '2026-10-03T07:10:00Z', approvedBy: 201, approvedByName: 'Vũ Ngọc Lan', approvedAt: '2026-10-03T08:35:00Z', supplierId: 1, supplierCode: 'SUP-001', supplierName: 'Công ty Nông Sản Cao Nguyên', requiresQc: true, details: [{ id: 2, productId: 6, productCode: 'SKU-4001', productName: 'Nước khoáng 500ml', operationUnitId: 4, operationUnitCode: 'CHAI', baseUnitCode: 'CHAI', conversionFactor: 1, conversionVersion: 1, expectedQuantity: 2400, receivedQuantity: 2400, acceptedQuantity: 2400, damagedQuantity: 0, rejectedQuantity: 0, postedQuantity: 0, baseExpectedQuantity: 2400, baseReceivedQuantity: 2400, baseAcceptedQuantity: 2400, basePostedQuantity: 0, note: '', requiresQc: true, qcState: 'Completed', observedQuantity: 2400, doorRejectedQuantity: 0, finalReceivedQuantity: 2400, baseFinalReceivedQuantity: 2400 }] },
];

export const demoExportReceipts = [
  { id: 5108, code: 'EX-2026-5108', warehouseName: 'DC Hồ Chí Minh', status: 'Approved', note: 'Chờ dispatch', createdBy: 101, createdByName: 'Trần Quốc Bảo', createdAt: '2026-10-03T08:40:00Z', approvedByName: 'Nguyễn Minh Anh', approvedAt: '2026-10-03T09:00:00Z', dispatchMode: 'ReserveThenDispatch', reservationStatus: 'Active', allowPerReceiptDispatchMode: true, allowWarehouseStaffDirectDispatch: false, writeEnabled: false, customerId: 3, customerCode: 'CUS-001', customerName: 'Chuỗi bán lẻ Minh Phúc', details: [{ id: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', quantity: 120, unitPrice: 125000, note: '' }] },
];

export const demoReservations = [
  { id: 1, reservationCode: 'RSV-2026-9031', productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', warehouseName: 'DC Hồ Chí Minh', quantity: 160, consumedQuantity: 40, releasedQuantity: 0, remainingQuantity: 120, status: 'PartiallyConsumed', sourceType: 'ExportReceipt', sourceCode: 'EX-2026-5108', createdAt: '2026-10-03T08:45:00Z', expiresAt: '2026-10-04T08:45:00Z' },
];

export const demoStocktakes = [
  { id: 142, code: 'CC-2026-0142', warehouseId: 1, warehouseName: 'DC Hồ Chí Minh', status: 0, note: 'Cycle count khu A', createdBy: 101, createdByName: 'Trần Quốc Bảo', createdAt: '2026-10-03T08:00:00Z', detailCount: 1, details: [{ id: 1, stocktakeId: 142, productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', systemQuantity: 1250, actualQuantity: 1248, differenceQuantity: -2, note: 'Recount accepted' }] },
];

export const demoPermissionCatalog = [
  { code: 'product.read', description: 'Xem sản phẩm' },
  { code: 'warehouse.read', description: 'Xem kho' },
  { code: 'warehouse_zone.manage', description: 'Quản lý cấu trúc khu vực kho' },
  { code: 'location.read', description: 'Xem vị trí kho' },
  { code: 'location.manage', description: 'Quản lý vị trí kho' },
  { code: 'receipt.read', description: 'Xem phiếu nhập' },
  { code: 'putaway.read', description: 'Xem nhiệm vụ cất hàng' },
];
