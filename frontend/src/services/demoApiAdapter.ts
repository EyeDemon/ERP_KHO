import type { InternalAxiosRequestConfig } from 'axios';
import {
  demoApprovalQueue,
  demoCategories,
  demoExportReceipts,
  demoImportReceipts,
  demoImportReceiptInventoryIdentities,
  demoPurchaseOrders,
  demoAsns,
  demoInOut,
  demoInventoryStocks,
  demoInventoryStatuses,
  demoInventoryBuckets,
  demoInventoryLocks,
  demoInventoryTransactions,
  demoPartners,
  demoPermissionCatalog,
  demoProducts,
  demoPutawayTasks,
  demoReservations,
  demoAllocations,
  demoPickingTasks,
  demoPackingSessions,
  demoHandlingUnits,
  demoShipments,
  demoSalesOrders,
  demoBackorders,
  demoAllocatableReservations,
  demoAllocationCandidates,
  demoStocktakes,
  demoTransfers,
  demoUnits,
  demoWarehouses,
  demoWarehouseStructures,
  demoWarehouseCalendars,
  demoDockYardWarehouses,
  demoDocks,
  demoYardSlots,
  demoDockAppointments,
} from '../mocks/demoApiData';
import { demoReconciliationRows, demoReconciliationWarehouses } from '../mocks/inventoryReconciliationDemo';

const ok = (config: InternalAxiosRequestConfig, data: unknown) => Promise.resolve({
  data,
  status: 200,
  statusText: 'OK',
  headers: {},
  config,
});

const fail = (config: InternalAxiosRequestConfig, status: number, message: string) => Promise.reject(Object.assign(new Error(message), {
  response: { status, data: { message }, headers: {}, config },
  config,
}));

const normalizePath = (url?: string) => {
  const raw = url ?? '';
  const withoutOrigin = raw.replace(/^https?:\/\/[^/]+/i, '');
  return withoutOrigin.split('?')[0].replace(/\/+$/, '').toLowerCase();
};

const urlParams = (config: InternalAxiosRequestConfig) => {
  const params = new URLSearchParams((config.url ?? '').split('?')[1] ?? '');
  const configured = config.params as Record<string, unknown> | undefined;
  for (const [key, value] of Object.entries(configured ?? {})) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  return params;
};

const paged = <T,>(items: T[], page: number, pageSize: number) => {
  const safePage = Math.max(1, page || 1);
  const safeSize = Math.max(1, pageSize || 20);
  const start = (safePage - 1) * safeSize;
  return {
    items: items.slice(start, start + safeSize),
    totalRecords: items.length,
    pageIndex: safePage,
    pageSize: safeSize,
    totalPages: Math.ceil(items.length / safeSize),
  };
};

const findNumericId = (path: string) => Number(path.match(/\/(\d+)(?:\/|$)/)?.[1] ?? 0);

export const createBlueprintDemoApiAdapter = (request: InternalAxiosRequestConfig) => {
  return async (config: InternalAxiosRequestConfig = request) => {
    const method = (config.method ?? 'get').toLowerCase();
    if (method !== 'get') {
      return fail(config, 405, 'Vercel Blueprint demo là môi trường chỉ đọc. Thao tác ghi đã bị chặn.');
    }

    const path = normalizePath(config.url);
    const params = urlParams(config);

    if (path === '/api/auth/me') {
      return ok(config, {
        userId: 1,
        username: 'Blueprint Demo',
        role: 'Viewer',
        permissions: ['product.read', 'product_category.read', 'warehouse.read', 'location.read', 'dock.read', 'dock_appointment.read', 'yard.read', 'uom.read', 'partner.read', 'purchase_order.read', 'asn.read', 'receipt.read', 'export_receipt.read', 'allocation.read', 'picking.read', 'packing.read', 'handling_unit.read', 'shipment.read', 'sales_order.read', 'backorder.read', 'inventory.read', 'inventory_availability.read', 'inventory_ledger.read', 'inventory_traceability.read', 'inventory_lock.read', 'receiving_discrepancy.read', 'reason_code.read', 'putaway.read', 'permission.read', 'role.read'],
      });
    }

    if (path === '/api/warehouses') return ok(config, demoWarehouses);
    if (/^\/api\/warehouses\/\d+\/calendar$/.test(path)) {
      const warehouseId = findNumericId(path);
      const item = demoWarehouseCalendars.find(calendar => calendar.warehouseId === warehouseId);
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy lịch vận hành kho demo.');
    }
    if (path === '/api/dock-yard/warehouses') return ok(config, demoDockYardWarehouses);
    if (path === '/api/dock-yard/docks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoDocks.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/dock-yard/yard-slots') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoYardSlots.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/dock-yard/appointments') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      const direction = params.get('direction');
      const search = (params.get('search') ?? '').toLowerCase();
      return ok(config, demoDockAppointments.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!status || String(item.status) === status)
        && (!direction || String(item.direction) === direction)
        && (!search || [item.code, item.carrierName ?? '', item.vehiclePlate ?? '', item.driverName ?? '', item.dockCode ?? '', item.yardSlotCode ?? ''].join(' ').toLowerCase().includes(search))
      ));
    }
    if (/^\/api\/dock-yard\/appointments\/\d+$/.test(path)) {
      const item = demoDockAppointments.find(appointment => appointment.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy appointment Dock/Yard demo.');
    }

    if (path === '/api/units') return ok(config, demoUnits);
    if (path === '/api/product-categories') return ok(config, demoCategories);

    if (path === '/api/products') return ok(config, demoProducts);
    if (/^\/api\/products\/\d+$/.test(path)) {
      const item = demoProducts.find(product => product.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy sản phẩm demo.');
    }
    if (path === '/api/product-barcodes/lookup') {
      const value = params.get('value') ?? '';
      const item = demoProducts.find(product => product.barcodes.some(barcode => barcode.value === value));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy mã vạch demo.');
    }

    if (path === '/api/business-partners') {
      const role = (params.get('role') ?? '').toLowerCase();
      const search = (params.get('search') ?? '').toLowerCase();
      const active = params.get('active');
      let items = demoPartners.filter(item =>
        (!role || (role === 'supplier' ? item.isSupplier : role === 'customer' ? item.isCustomer : true))
        && (!search || item.code.toLowerCase().includes(search) || item.name.toLowerCase().includes(search))
        && (active === null || String(item.isActive) === active)
      );
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 10);
      return ok(config, paged(items, page, pageSize));
    }

    if (path === '/api/approvals/queue') {
      const type = params.get('documentType');
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const sla = params.get('slaStatus');
      const items = demoApprovalQueue.filter(item =>
        (!type || item.documentType === type)
        && (!keyword || item.documentCode.toLowerCase().includes(keyword))
        && (!sla || item.slaStatus === sla)
      );
      return ok(config, paged(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (/^\/api\/approvals\/[^/]+\/\d+$/.test(path)) {
      const parts = path.split('/');
      const documentType = parts[3];
      const id = Number(parts[4]);
      const item = demoApprovalQueue.find(entry => entry.documentType.toLowerCase() === documentType && entry.documentId === id);
      return item ? ok(config, {
        summary: item,
        note: 'Dữ liệu demo read-only',
        lines: [{ productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g', unitName: 'Gói', quantity: item.totalQuantity }],
        history: [{ id: 1, timestampUtc: item.requestedAtUtc, actorName: item.creatorName, displayAction: 'Submitted', newState: item.pendingState, result: 'Success', correlationId: 'DEMO-APPROVAL-001' }],
      }) : fail(config, 404, 'Không tìm thấy approval demo.');
    }

    if (path === '/api/stock-transfers') {
      const search = (params.get('search') ?? '').toLowerCase();
      const status = params.get('status') ?? '';
      const items = demoTransfers.filter(item =>
        (!search || item.code.toLowerCase().includes(search))
        && (!status || String(item.status) === status)
      );
      return ok(config, paged(items, Number(params.get('pageIndex') ?? 1), Number(params.get('pageSize') ?? 15)));
    }
    if (/^\/api\/stock-transfers\/\d+$/.test(path)) {
      const item = demoTransfers.find(transfer => transfer.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy transfer demo.');
    }

    if (path === '/api/putaway-tasks/location-warehouses') return ok(config, demoWarehouses);
    if (path === '/api/putaway-tasks/locations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, []);
      const physical = structure.zones.flatMap(zone =>
        zone.aisles.flatMap(aisle =>
          aisle.racks.flatMap(rack =>
            rack.levels.flatMap(level =>
              level.locations.map(location => ({
                ...location,
                structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
                rowVersion: null,
              }))
            )
          )
        )
      );
      return ok(config, [
        ...physical,
        ...structure.unmappedLocations.map(location => ({ ...location, structurePath: null, rowVersion: null })),
        ...structure.systemLocations.map(location => ({ ...location, structurePath: null, rowVersion: null })),
      ]);
    }

    if (path === '/api/putaway-tasks/location-capacity') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, []);
      const physical = structure.zones.flatMap(zone => zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level =>
        level.locations.map(location => {
          const usage = location.code.endsWith('B04')
            ? { usedWeightKg: 1300, usedVolumeM3: 8.2, usedPalletEquivalent: 4 }
            : location.code.endsWith('B05')
              ? { usedWeightKg: 620, usedVolumeM3: 4.1, usedPalletEquivalent: 2 }
              : { usedWeightKg: 0, usedVolumeM3: 0, usedPalletEquivalent: 0 };
          const ratios = [
            location.maxWeightKg ? usage.usedWeightKg / location.maxWeightKg : 0,
            location.maxVolumeM3 ? usage.usedVolumeM3 / location.maxVolumeM3 : 0,
            location.maxPalletEquivalent ? usage.usedPalletEquivalent / location.maxPalletEquivalent : 0,
          ];
          const state = !location.isActive ? 'Inactive' : location.isBlocked ? 'Blocked' : Math.max(...ratios) >= 0.85 ? 'NearCapacity' : 'Available';
          return {
            locationId: location.id,
            code: location.code,
            name: location.name,
            structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
            storageClass: location.storageClass,
            maxWeightKg: location.maxWeightKg,
            usedWeightKg: usage.usedWeightKg,
            maxVolumeM3: location.maxVolumeM3,
            usedVolumeM3: usage.usedVolumeM3,
            maxPalletEquivalent: location.maxPalletEquivalent,
            usedPalletEquivalent: usage.usedPalletEquivalent,
            profileIncomplete: false,
            compatibilityConflict: false,
            isActive: location.isActive,
            isBlocked: location.isBlocked,
            state,
          };
        })
      ))));
      return ok(config, physical);
    }

    if (path === '/api/putaway-tasks/location-map') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const structure = demoWarehouseStructures.find(item => item.warehouseId === warehouseId);
      if (!structure) return ok(config, { warehouseId, generatedAtUtc: new Date().toISOString(), items: [] });
      const items = structure.zones.flatMap(zone => zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level =>
        level.locations.map((location, index) => {
          const utilization = location.code.endsWith('B04') ? 86.67 : location.code.endsWith('B05') ? 41.33 : 0;
          const capacityState = !location.isActive ? 'Inactive' : location.isBlocked ? 'Blocked' : utilization >= 85 ? 'NearCapacity' : 'Available';
          const recentMovementCount = index === 0 ? 12 : index === 1 ? 5 : index === 2 ? 2 : 0;
          const activePutawayCount = index === 0 ? 5 : index === 1 ? 2 : 0;
          const activityLevel = recentMovementCount >= 10 || activePutawayCount >= 5 ? 'High' : recentMovementCount >= 4 || activePutawayCount >= 2 ? 'Medium' : 'Low';
          return {
            locationId: location.id,
            code: location.code,
            name: location.name,
            structurePath: [zone.code, aisle.code, rack.code, 'L' + String(level.levelNo).padStart(2, '0'), location.code.split('-').slice(-1)[0]].join('/'),
            storageClass: location.storageClass,
            mapX: location.mapX,
            mapY: location.mapY,
            mapWidth: location.mapWidth,
            mapHeight: location.mapHeight,
            utilizationPercent: utilization,
            capacityState,
            recentMovementCount,
            activePutawayCount,
            activityLevel,
            isActive: location.isActive,
            isBlocked: location.isBlocked,
            rowVersion: null,
          };
        })
      ))));
      return ok(config, { warehouseId, generatedAtUtc: new Date().toISOString(), items });
    }

    if (path === '/api/putaway-tasks') return ok(config, demoPutawayTasks);
    if (/^\/api\/putaway-tasks\/\d+$/.test(path)) {
      const item = demoPutawayTasks.find(task => task.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy nhiệm vụ cất hàng demo.');
    }
    if (/^\/api\/putaway-tasks\/\d+\/items\/\d+\/destinations$/.test(path)) {
      return ok(config, [
        { id: 101, code: 'A01-R02-L03-B04', name: 'Bin A01-R02-L03-B04', locationType: 'Bin', isPickable: true },
        { id: 102, code: 'A01-R02-L03-B05', name: 'Bin A01-R02-L03-B05', locationType: 'Bin', isPickable: true },
      ]);
    }

    if (path === '/api/inventory/reversal-candidates') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const reversedIds = new Set(demoInventoryTransactions
        .filter(item => item.transactionType === 'Reversal' && item.referenceType === 'InventoryReversal')
        .map(item => item.referenceId));
      const candidates = demoInventoryTransactions
        .filter(item => (item.transactionType === 'Move' || item.transactionType === 'StatusChange')
          && (!warehouseId || item.warehouseId === warehouseId))
        .map(item => ({ ...item, isReversed: reversedIds.has(item.id) }))
        .sort((a, b) => new Date(b.transactionDate).getTime() - new Date(a.transactionDate).getTime() || b.id - a.id);
      return ok(config, paged(candidates, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/inventory/statuses') return ok(config, demoInventoryStatuses);
    if (path === '/api/inventory/locks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = (params.get('status') ?? '').toLowerCase();
      return ok(config, demoInventoryLocks.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!status || item.status.toLowerCase() === status)
      ));
    }
    if (/^\/api\/inventory\/locks\/\d+$/.test(path)) {
      const item = demoInventoryLocks.find(lock => lock.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Inventory Lock demo.');
    }

    if (path === '/api/inventory/buckets') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const status = (params.get('status') ?? '').toUpperCase();
      const lotNumber = (params.get('lotNumber') ?? '').toLowerCase();
      const serialNumber = (params.get('serialNumber') ?? '').toLowerCase();
      return ok(config, demoInventoryBuckets.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!status || item.status === status)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase().includes(lotNumber))
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase().includes(serialNumber))
      ));
    }

    if (path === '/api/inventorystocks/current') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoInventoryStocks.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, items);
    }
    if (path === '/api/inventorytransactions') {
      const transactionType = params.get('transactionType');
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoInventoryTransactions.filter(item =>
        (!transactionType || item.transactionType.toLowerCase() === transactionType.toLowerCase())
        && (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }

    if (path === '/api/inventory/traceability') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const lotNumber = (params.get('lotNumber') ?? '').toLowerCase();
      const serialNumber = (params.get('serialNumber') ?? '').toLowerCase();
      const referenceType = params.get('referenceType');
      const referenceId = Number(params.get('referenceId') ?? 0);
      const limit = Math.min(500, Math.max(1, Number(params.get('limit') ?? 200)));
      const hasReference = Boolean(referenceType && referenceId);
      const events = demoInventoryTransactions.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase() === lotNumber)
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase() === serialNumber)
        && (!hasReference || (item.referenceType === referenceType && item.referenceId === referenceId))
      ).slice(0, limit);
      const reversed = new Set(events
        .filter(item => item.transactionType === 'Reversal' && item.referenceType === 'InventoryReversal' && typeof item.referenceId === 'number')
        .map(item => item.referenceId as number));
      const productIds = new Set(events.map(item => item.productId));
      const currentBuckets = demoInventoryBuckets.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!lotNumber || (item.lotNumber ?? '').toLowerCase() === lotNumber)
        && (!serialNumber || (item.serialNumber ?? '').toLowerCase() === serialNumber)
        && (productId || lotNumber || serialNumber || productIds.has(item.productId))
      ).map(item => ({
        ...item,
        inventoryStatus: item.status,
      }));
      return ok(config, {
        currentBuckets,
        events: events.map(item => ({
          ...item,
          transactionId: item.id,
          reversalOfTransactionId: item.transactionType === 'Reversal' && item.referenceType === 'InventoryReversal'
            ? item.referenceId
            : null,
          isReversed: reversed.has(item.id),
        })),
      });
    }
    if (path === '/api/reports/inventory-in-out-stock') return ok(config, demoInOut);
    if (path === '/api/reports/inventory/export' || path === '/api/reports/inventory-in-out-stock/export') {
      return ok(config, new Blob(['Blueprint demo export - no production data'], { type: 'text/plain' }));
    }

    if (path === '/api/purchase-orders') {
      const status = params.get('status');
      return ok(config, demoPurchaseOrders.filter(item => !status || item.status === status));
    }
    if (/^\/api\/purchase-orders\/\d+$/.test(path)) {
      const item = demoPurchaseOrders.find(order => order.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy đơn mua demo.');
    }

    if (path === '/api/asns') {
      const status = params.get('status');
      return ok(config, demoAsns.filter(item => !status || item.status === status));
    }
    if (/^\/api\/asns\/\d+$/.test(path)) {
      const item = demoAsns.find(asn => asn.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy ASN demo.');
    }

    if (path === '/api/importreceipts') return ok(config, demoImportReceipts);
    if (/^\/api\/importreceipts\/\d+$/.test(path)) {
      const item = demoImportReceipts.find(receipt => receipt.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu nhập demo.');
    }
    if (/^\/api\/importreceipts\/\d+\/inventory-identities$/.test(path)) {
      const id = findNumericId(path);
      return ok(config, demoImportReceiptInventoryIdentities[id] ?? []);
    }
    if (/^\/api\/importreceipts\/\d+\/discrepancies$/.test(path)) return ok(config, []);
    if (path === '/api/importreceipts/discrepancy-reasons') {
      return ok(config, [{ code: 'COUNT_VARIANCE', name: 'Chênh lệch kiểm đếm', version: 1, requiresNote: true, requiresAttachment: false, requiresApproval: false }]);
    }

    if (path === '/api/exportreceipts') return ok(config, demoExportReceipts);
    if (/^\/api\/exportreceipts\/\d+$/.test(path)) {
      const item = demoExportReceipts.find(receipt => receipt.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu xuất demo.');
    }

    if (path === '/api/stock-reservations') {
      const status = params.get('status');
      const items = demoReservations.filter(item => !status || item.status === status);
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }

    if (path === '/api/inventory/allocations/reservations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoAllocatableReservations.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (path === '/api/inventory/allocations/candidates') {
      const reservationId = Number(params.get('reservationId') ?? 0);
      return ok(config, demoAllocationCandidates
        .filter(item => !reservationId || item.reservationId === reservationId)
        .filter(item => item.allocatableQuantity > 0)
        .map(({ reservationId: _reservationId, ...item }) => item));
    }
    if (path === '/api/inventory/allocations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const reservationId = Number(params.get('reservationId') ?? 0);
      const status = params.get('status');
      const items = demoAllocations.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!reservationId || item.reservationId === reservationId)
        && (!status || item.status === status)
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (/^\/api\/inventory\/allocations\/\d+$/.test(path)) {
      const item = demoAllocations.find(allocation => allocation.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Allocation demo.');
    }

    if (path === '/api/picking-tasks') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoPickingTasks
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, shortPicks: _shortPicks, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/picking-tasks\/\d+$/.test(path)) {
      const item = demoPickingTasks.find(task => task.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy nhiệm vụ Picking demo.');
    }

    if (path === '/api/packing-sessions') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoPackingSessions
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, handlingUnits: _handlingUnits, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/packing-sessions\/\d+$/.test(path)) {
      const item = demoPackingSessions.find(session => session.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Packing session demo.');
    }
    if (path === '/api/handling-units') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      return ok(config, demoHandlingUnits.filter(item => !warehouseId || item.warehouseId === warehouseId));
    }
    if (/^\/api\/handling-units\/\d+$/.test(path)) {
      const item = demoHandlingUnits.find(hu => hu.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Handling Unit demo.');
    }

    if (path === '/api/shipments') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoShipments
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ handlingUnits: _handlingUnits, proofOfDelivery: _proof, trackingEvents: _events, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/shipments\/\d+\/tracking$/.test(path)) {
      const item = demoShipments.find(shipment => shipment.id === findNumericId(path));
      return item ? ok(config, {
        shipmentId: item.id,
        shipmentCode: item.shipmentCode,
        status: item.status,
        dispatchedAt: item.dispatchedAt ?? null,
        inTransitAt: item.inTransitAt ?? null,
        deliveryFailedAt: item.deliveryFailedAt ?? null,
        returnInitiatedAt: item.returnInitiatedAt ?? null,
        deliveredAt: item.deliveredAt ?? null,
        completedAt: item.completedAt ?? null,
        proofOfDelivery: item.proofOfDelivery ?? null,
        events: item.trackingEvents ?? [],
      }) : fail(config, 404, 'Không tìm thấy tracking Shipment demo.');
    }
    if (/^\/api\/shipments\/\d+$/.test(path)) {
      const item = demoShipments.find(shipment => shipment.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Shipment demo.');
    }

    if (path === '/api/sales-orders') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoSalesOrders
        .filter(item => (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status))
        .map(({ lines: _lines, rowVersion: _rowVersion, ...item }) => item));
    }
    if (/^\/api\/sales-orders\/\d+$/.test(path)) {
      const item = demoSalesOrders.find(order => order.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Sales Order demo.');
    }
    if (path === '/api/backorders') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const status = params.get('status');
      return ok(config, demoBackorders.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId) && (!status || item.status === status)
      ));
    }
    if (/^\/api\/backorders\/\d+$/.test(path)) {
      const item = demoBackorders.find(row => row.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy Backorder demo.');
    }

    if (path === '/api/stocktakes') return ok(config, demoStocktakes.map(item => ({
      id: item.id,
      code: item.code,
      warehouseId: item.warehouseId,
      warehouseName: item.warehouseName,
      status: item.status,
      note: item.note,
      createdBy: item.createdBy,
      createdByName: item.createdByName,
      createdAt: item.createdAt,
      detailCount: item.detailCount,
    })));
    if (/^\/api\/stocktakes\/\d+$/.test(path)) {
      const item = demoStocktakes.find(stocktake => stocktake.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu kiểm kê demo.');
    }

    if (path === '/api/permissions') return ok(config, demoPermissionCatalog);
    if (path === '/api/permissions/roles') {
      return ok(config, [
        { id: 1, roleName: 'Admin', rowVersion: 'AAAAAAAAROLE1', permissions: demoPermissionCatalog.map(item => item.code) },
        { id: 2, roleName: 'Viewer', rowVersion: 'AAAAAAAAROLE2', permissions: demoPermissionCatalog.map(item => item.code) },
      ]);
    }

    if (path === '/api/inventoryreconciliation') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const productId = Number(params.get('productId') ?? 0);
      const keyword = (params.get('keyword') ?? '').toLowerCase();
      const items = demoReconciliationRows.filter(item =>
        (!warehouseId || item.warehouseId === warehouseId)
        && (!productId || item.productId === productId)
        && (!keyword || item.productCode.toLowerCase().includes(keyword) || item.productName.toLowerCase().includes(keyword))
      );
      return ok(config, paged(items, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/inventoryreconciliation/warehouses') return ok(config, demoReconciliationWarehouses);

    return fail(config, 404, 'Endpoint demo chưa được ánh xạ: ' + (config.url ?? 'unknown'));
  };
};
