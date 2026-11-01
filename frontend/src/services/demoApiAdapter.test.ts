// @vitest-environment jsdom
import type { InternalAxiosRequestConfig } from 'axios';
import { describe, expect, it } from 'vitest';
import { createReadOnlyDemoApiAdapter } from './demoApiAdapter';

const request = (url: string, method = 'get', params?: Record<string, unknown>) => ({
  url,
  method,
  params,
  headers: {},
} as InternalAxiosRequestConfig);

describe('Read-only demo API adapter', () => {
  it('serves product and warehouse reads without a network backend', async () => {
    const productConfig = request('/api/v1/products');
    const products = await createReadOnlyDemoApiAdapter(productConfig)(productConfig);
    expect(products.status).toBe(200);
    expect(Array.isArray(products.data)).toBe(true);
    expect((products.data as Array<{ code: string }>)[0].code).toBe('SKU-1001');

    const warehouseConfig = request('/api/v1/warehouses');
    const warehouses = await createReadOnlyDemoApiAdapter(warehouseConfig)(warehouseConfig);
    expect((warehouses.data as Array<{ code: string }>).some(item => item.code === 'WH-HCM-01')).toBe(true);
  });

  it('serves warehouse calendar data for the production screen', async () => {
    const config = request('/api/v1/warehouses/1/calendar');
    const response = await createReadOnlyDemoApiAdapter(config)(config);
    expect(response.status).toBe(200);
    const calendar = response.data as { warehouseId: number; timeZoneId: string; days: unknown[]; shifts: unknown[] };
    expect(calendar.warehouseId).toBe(1);
    expect(calendar.timeZoneId).toBe('Asia/Ho_Chi_Minh');
    expect(calendar.days).toHaveLength(7);
    expect(calendar.shifts.length).toBeGreaterThan(0);
  });

  it('serves Dock & Yard production reads without enabling writes', async () => {
    const warehouseConfig = request('/api/v1/dock-yard/warehouses');
    const warehouses = await createReadOnlyDemoApiAdapter(warehouseConfig)(warehouseConfig);
    expect((warehouses.data as Array<{ timeZoneId: string }>)[0].timeZoneId).toBe('Asia/Ho_Chi_Minh');

    const appointmentsConfig = request('/api/v1/dock-yard/appointments', 'get', { warehouseId: 1 });
    const appointments = await createReadOnlyDemoApiAdapter(appointmentsConfig)(appointmentsConfig);
    expect(appointments.status).toBe(200);
    expect((appointments.data as Array<{ code: string; status: number }>).some(item => item.code === 'APT-2026-1042' && item.status === 3)).toBe(true);

    const docksConfig = request('/api/v1/dock-yard/docks', 'get', { warehouseId: 1 });
    const docks = await createReadOnlyDemoApiAdapter(docksConfig)(docksConfig);
    expect((docks.data as Array<{ code: string }>).some(item => item.code === 'D-02')).toBe(true);

    const detailConfig = request('/api/v1/dock-yard/appointments/1042');
    const detail = await createReadOnlyDemoApiAdapter(detailConfig)(detailConfig);
    expect((detail.data as { events: unknown[] }).events.length).toBeGreaterThan(0);

    const writeConfig = request('/api/v1/dock-yard/appointments/1042/check-in', 'post');
    await expect(createReadOnlyDemoApiAdapter(writeConfig)(writeConfig)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('returns paged business partner and transfer shapes used by production pages', async () => {
    const partnerConfig = request('/api/v1/business-partners', 'get', { role: 'supplier', page: 1, pageSize: 10 });
    const partners = await createReadOnlyDemoApiAdapter(partnerConfig)(partnerConfig);
    expect((partners.data as { items: unknown[] }).items.length).toBeGreaterThan(0);
    expect((partners.data as { totalPages: number }).totalPages).toBeGreaterThan(0);

    const transferConfig = request('/api/v1/stock-transfers', 'get', { pageIndex: 1, pageSize: 15 });
    const transfers = await createReadOnlyDemoApiAdapter(transferConfig)(transferConfig);
    expect((transfers.data as { items: Array<{ code: string }> }).items[0].code).toMatch(/^TRF-/);
  });

  it('serves inventory, approval, receipt and putaway reads', async () => {
    const endpoints = [
      '/api/v1/inventory-stocks/current?',
      '/api/v1/approvals/queue?pageIndex=1&pageSize=20',
      '/api/v1/purchase-orders',
      '/api/v1/asns',
      '/api/v1/import-receipts',
      '/api/v1/export-receipts',
      '/api/v1/putaway-tasks',
      '/api/v1/stock-reservations?page=1&pageSize=20',
      '/api/v1/stocktakes',
    ];

    for (const url of endpoints) {
      const config = request(url);
      const response = await createReadOnlyDemoApiAdapter(config)(config);
      expect(response.status).toBe(200);
    }
  });

  it('serves location capacity data for the production warehouse page', async () => {
    const config = request('/api/v1/putaway-tasks/location-capacity', 'get', { warehouseId: 1 });
    const response = await createReadOnlyDemoApiAdapter(config)(config);
    const rows = response.data as Array<{ code: string; state: string; maxWeightKg?: number | null }>;

    expect(response.status).toBe(200);
    expect(rows.some(item => item.code === 'A01-R02-L03-B04' && item.state === 'NearCapacity')).toBe(true);
    expect(rows.find(item => item.code === 'A01-R02-L03-B04')?.maxWeightKg).toBe(1500);
  });

  it('serves warehouse map snapshots from demo data', async () => {
    const config = request('/api/v1/putaway-tasks/location-map', 'get', { warehouseId: 1 });
    const response = await createReadOnlyDemoApiAdapter(config)(config);
    const data = response.data as { warehouseId: number; generatedAtUtc: string; items: Array<{ code: string; activityLevel: string; mapX?: number | null }> };

    expect(response.status).toBe(200);
    expect(data.warehouseId).toBe(1);
    expect(data.generatedAtUtc).toBeTruthy();
    expect(data.items.some(item => item.code === 'A01-R02-L03-B04' && item.activityLevel === 'High' && item.mapX === 6)).toBe(true);
  });

  it('filters reversal candidates and serves reversal-aware inventory traceability', async () => {
    const moveConfig = request('/api/v1/inventory-transactions?transactionType=Move&page=1&pageSize=100');
    const moves = await createReadOnlyDemoApiAdapter(moveConfig)(moveConfig);
    const moveItems = (moves.data as { items: Array<{ transactionType: string; id: number }> }).items;
    expect(moveItems.length).toBeGreaterThan(0);
    expect(moveItems.every(item => item.transactionType === 'Move')).toBe(true);

    const reversalConfig = request('/api/v1/inventory-transactions?transactionType=Reversal&page=1&pageSize=100');
    const reversals = await createReadOnlyDemoApiAdapter(reversalConfig)(reversalConfig);
    expect((reversals.data as { items: Array<{ referenceType: string; referenceId: number }> }).items)
      .toContainEqual(expect.objectContaining({ referenceType: 'InventoryReversal', referenceId: 4 }));

    const traceConfig = request('/api/v1/inventory/traceability?productId=1&lotNumber=LOT-ARABICA-2609&limit=200');
    const trace = await createReadOnlyDemoApiAdapter(traceConfig)(traceConfig);
    const data = trace.data as {
      currentBuckets: Array<{ productId: number; inventoryStatus: string }>;
      events: Array<{ transactionId: number; transactionType: string; reversalOfTransactionId?: number | null; isReversed: boolean }>;
    };
    expect(data.currentBuckets.some(item => item.productId === 1 && item.inventoryStatus === 'AVAILABLE')).toBe(true);
    expect(data.events.find(item => item.transactionId === 4)?.isReversed).toBe(true);
    expect(data.events).toContainEqual(expect.objectContaining({
      transactionType: 'Reversal',
      reversalOfTransactionId: 4,
    }));
  });

  it('exposes a separate read-only warehouse list for the demo traceability selector',async()=>{
    const choiceConfig=request('/api/v1/inventory/traceability-warehouses');
    const response=await createReadOnlyDemoApiAdapter(choiceConfig)(choiceConfig);
    const options=response.data as Array<{id:number;code:string;name:string}>;
    expect(options.length).toBeGreaterThan(0);
    expect(options.every(item=>item.id>0 && item.code && item.name)).toBe(true);
    const mutation=request('/api/v1/inventory/traceability-warehouses','post');
    await expect(createReadOnlyDemoApiAdapter(mutation)(mutation)).rejects.toMatchObject({
      response:{status:405}
    });
  });

  it('keeps warehouse-only demo stock independent from ledger recency and supports empty next page',async()=>{
    const firstConfig=request('/api/v1/inventory/traceability?warehouseId=1&limit=1');
    const first=await createReadOnlyDemoApiAdapter(firstConfig)(firstConfig);
    const page=first.data as {
      currentBuckets:Array<{inventoryStockId:number}>;
      events:Array<{transactionId:number}>;
      eventsTruncated:boolean;bucketsTruncated:boolean;
    };
    expect(page.events).toHaveLength(1);
    expect(page.eventsTruncated).toBe(true);
    expect(page.currentBuckets.some(item=>item.inventoryStockId===7003)).toBe(true);
    expect(page.bucketsTruncated).toBe(false);
    const secondConfig=request('/api/v1/inventory/traceability?warehouseId=1&limit=1&bucketOffset=500');
    const second=await createReadOnlyDemoApiAdapter(secondConfig)(secondConfig);
    expect((second.data as typeof page).currentBuckets).toHaveLength(0);
    expect((second.data as typeof page).bucketsTruncated).toBe(false);
  });

  it('paginates demo ledger events independently and refuses invalid event offsets',async()=>{
    const firstUrl=request('/api/v1/inventory/traceability?warehouseId=1&limit=1');
    const first=(await createReadOnlyDemoApiAdapter(firstUrl)(firstUrl)).data as {
      events:Array<{transactionId:number}>;eventsTruncated:boolean;currentBuckets:unknown[];
    };
    const secondUrl=request('/api/v1/inventory/traceability?warehouseId=1&limit=1&eventOffset=1');
    const second=(await createReadOnlyDemoApiAdapter(secondUrl)(secondUrl)).data as typeof first;
    expect(first.events).toHaveLength(1);
    expect(second.events).toHaveLength(1);
    expect(second.events[0].transactionId).not.toBe(first.events[0].transactionId);
    expect(second.currentBuckets).toEqual(first.currentBuckets);
    for(const bad of ['-1','3','50500']){
      const config=request('/api/v1/inventory/traceability?warehouseId=1&limit=2&eventOffset='+bad);
      await expect(createReadOnlyDemoApiAdapter(config)(config))
        .rejects.toMatchObject({response:{status:400}});
    }
  });

  it('does not expand stock to all warehouse buckets for an unknown trace reference',async()=>{
    const config=request('/api/v1/inventory/traceability?warehouseId=1&referenceType=StockTransfer&referenceId=9999');
    const result=await createReadOnlyDemoApiAdapter(config)(config);
    const data=result.data as {events:unknown[];currentBuckets:unknown[]};
    expect(data.events).toHaveLength(0);
    expect(data.currentBuckets).toHaveLength(0);
    const invalid=request('/api/v1/inventory/traceability?warehouseId=1&bucketOffset=30');
    await expect(createReadOnlyDemoApiAdapter(invalid)(invalid)).rejects.toMatchObject({
      response:{status:400}
    });
  });

  it('does not expose warehouse structure pseudo-backend reads', async () => {
    for (const url of [
      '/api/v1/warehouses/1/structure',
      '/api/v1/warehouses/1/zones',
      '/api/v1/warehouses/1/locations',
      '/api/v1/locations?warehouseId=1',
    ]) {
      const config = request(url);
      await expect(createReadOnlyDemoApiAdapter(config)(config)).rejects.toMatchObject({
        response: { status: 404 },
      });
    }
  });

  it('fails closed for mutations instead of pretending a write succeeded', async () => {
    const config = request('/api/v1/products', 'post');
    await expect(createReadOnlyDemoApiAdapter(config)(config)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('serves newly mapped report, reconciliation, transfer, permission, and diagnostic endpoints', async () => {
    const getEndpoints = [
      '/api/v1/reports/expiry-aging',
      '/api/v1/reports/analytics-summary',
      '/api/v1/reports/productivity-and-bi-snapshot?warehouseId=1',
      '/api/v1/inventory-reconciliation/investigation?warehouseId=1&productId=1',
      '/api/v1/stock-transfers/in-transit',
      '/api/v1/stock-transfers/return-reasons',
      '/api/v1/permissions/audit-logs',
      '/api/v1/permissions/compliance-evidence-bundle',
      '/api/v1/auth/sessions',
      '/api/v1/users/1/security',
      '/api/v1/users/1/warehouse-access',
    ];
    for (const url of getEndpoints) {
      const config = request(url, 'get');
      const res = await createReadOnlyDemoApiAdapter(config)(config);
      expect(res.status).toBe(200);
    }

    const postDiagnostics = [
      '/api/v1/warehouses/verify-topology-capacity',
      '/api/v1/import-receipts/initiate-rma-inspection',
      '/api/v1/inventory-reconciliation/export-erp-gl-journal',
      '/api/v1/user-warehouse-access/verify-sod-isolation',
    ];
    for (const url of postDiagnostics) {
      const config = request(url, 'post');
      const res = await createReadOnlyDemoApiAdapter(config)(config);
      expect(res.status).toBe(200);
    }
  });

  it('fails visibly when a read endpoint has not been mapped', async () => {
    const config = request('/api/v1/not-mapped');
    await expect(createReadOnlyDemoApiAdapter(config)(config)).rejects.toMatchObject({
      response: { status: 404 },
    });
  });
});
