import type { InternalAxiosRequestConfig } from 'axios';
import {
  demoApprovalQueue,
  demoCategories,
  demoExportReceipts,
  demoImportReceipts,
  demoInOut,
  demoInventoryStocks,
  demoInventoryTransactions,
  demoPartners,
  demoPermissionCatalog,
  demoProducts,
  demoPutawayTasks,
  demoReservations,
  demoStocktakes,
  demoTransfers,
  demoUnits,
  demoWarehouses,
  demoWarehouseStructures,
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
        permissions: ['product.read', 'product_category.read', 'warehouse.read', 'location.read', 'uom.read', 'partner.read', 'receipt.read', 'receiving_discrepancy.read', 'reason_code.read', 'putaway.read', 'permission.read', 'role.read'],
      });
    }

    if (path === '/api/warehouses') return ok(config, demoWarehouses);
    if (/^\/api\/warehouses\/\d+\/structure$/.test(path)) {
      const warehouseId = findNumericId(path);
      const item = demoWarehouseStructures.find(entry => entry.warehouseId === warehouseId);
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy cấu trúc kho demo.');
    }
    if (/^\/api\/warehouses\/\d+\/zones$/.test(path)) {
      const warehouseId = findNumericId(path);
      const item = demoWarehouseStructures.find(entry => entry.warehouseId === warehouseId);
      return item ? ok(config, item.zones) : fail(config, 404, 'Không tìm thấy khu vực kho demo.');
    }
    if (/^\/api\/warehouses\/\d+\/locations$/.test(path)) {
      const warehouseId = findNumericId(path);
      const item = demoWarehouseStructures.find(entry => entry.warehouseId === warehouseId);
      if (!item) return fail(config, 404, 'Không tìm thấy vị trí kho demo.');
      const nested = item.zones.flatMap(zone => [
        ...zone.locations,
        ...zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level => level.locations))),
      ]);
      return ok(config, [...item.systemLocations, ...item.unmappedLocations, ...nested]);
    }
    if (path === '/api/locations') {
      const warehouseId = Number(params.get('warehouseId') ?? 0);
      const item = demoWarehouseStructures.find(entry => entry.warehouseId === warehouseId);
      if (!item) return ok(config, []);
      const nested = item.zones.flatMap(zone => [
        ...zone.locations,
        ...zone.aisles.flatMap(aisle => aisle.racks.flatMap(rack => rack.levels.flatMap(level => level.locations))),
      ]);
      return ok(config, [...item.systemLocations, ...nested]);
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
      return ok(config, paged(demoInventoryTransactions, Number(params.get('page') ?? 1), Number(params.get('pageSize') ?? 20)));
    }
    if (path === '/api/reports/inventory-in-out-stock') return ok(config, demoInOut);
    if (path === '/api/reports/inventory/export' || path === '/api/reports/inventory-in-out-stock/export') {
      return ok(config, new Blob(['Blueprint demo export - no production data'], { type: 'text/plain' }));
    }

    if (path === '/api/importreceipts') return ok(config, demoImportReceipts);
    if (/^\/api\/importreceipts\/\d+$/.test(path)) {
      const item = demoImportReceipts.find(receipt => receipt.id === findNumericId(path));
      return item ? ok(config, item) : fail(config, 404, 'Không tìm thấy phiếu nhập demo.');
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
