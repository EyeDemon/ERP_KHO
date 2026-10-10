// @vitest-environment jsdom
import type { InternalAxiosRequestConfig } from 'axios';
import { describe, expect, it } from 'vitest';
import { createBlueprintDemoApiAdapter } from './demoApiAdapter';

const request = (url: string, method = 'get', params?: Record<string, unknown>) => ({
  url,
  method,
  params,
  headers: {},
} as InternalAxiosRequestConfig);

describe('Blueprint demo API adapter', () => {
  it('serves product and warehouse reads without a network backend', async () => {
    const productConfig = request('/api/products');
    const products = await createBlueprintDemoApiAdapter(productConfig)(productConfig);
    expect(products.status).toBe(200);
    expect(Array.isArray(products.data)).toBe(true);
    expect((products.data as Array<{ code: string }>)[0].code).toBe('SKU-1001');

    const warehouseConfig = request('/api/warehouses');
    const warehouses = await createBlueprintDemoApiAdapter(warehouseConfig)(warehouseConfig);
    expect((warehouses.data as Array<{ code: string }>).some(item => item.code === 'WH-HCM-01')).toBe(true);
  });

  it('serves warehouse calendar data for the WH-05 production screen', async () => {
    const config = request('/api/warehouses/1/calendar');
    const response = await createBlueprintDemoApiAdapter(config)(config);
    expect(response.status).toBe(200);
    const calendar = response.data as { warehouseId: number; timeZoneId: string; days: unknown[]; shifts: unknown[] };
    expect(calendar.warehouseId).toBe(1);
    expect(calendar.timeZoneId).toBe('Asia/Ho_Chi_Minh');
    expect(calendar.days).toHaveLength(7);
    expect(calendar.shifts.length).toBeGreaterThan(0);
  });

  it('serves WH-06 Dock & Yard production reads without enabling writes', async () => {
    const warehouseConfig = request('/api/dock-yard/warehouses');
    const warehouses = await createBlueprintDemoApiAdapter(warehouseConfig)(warehouseConfig);
    expect((warehouses.data as Array<{ timeZoneId: string }>)[0].timeZoneId).toBe('Asia/Ho_Chi_Minh');

    const appointmentsConfig = request('/api/dock-yard/appointments', 'get', { warehouseId: 1 });
    const appointments = await createBlueprintDemoApiAdapter(appointmentsConfig)(appointmentsConfig);
    expect(appointments.status).toBe(200);
    expect((appointments.data as Array<{ code: string; status: number }>).some(item => item.code === 'APT-2026-1042' && item.status === 3)).toBe(true);

    const docksConfig = request('/api/dock-yard/docks', 'get', { warehouseId: 1 });
    const docks = await createBlueprintDemoApiAdapter(docksConfig)(docksConfig);
    expect((docks.data as Array<{ code: string }>).some(item => item.code === 'D-02')).toBe(true);

    const detailConfig = request('/api/dock-yard/appointments/1042');
    const detail = await createBlueprintDemoApiAdapter(detailConfig)(detailConfig);
    expect((detail.data as { events: unknown[] }).events.length).toBeGreaterThan(0);

    const writeConfig = request('/api/dock-yard/appointments/1042/check-in', 'post');
    await expect(createBlueprintDemoApiAdapter(writeConfig)(writeConfig)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('returns paged business partner and transfer shapes used by production pages', async () => {
    const partnerConfig = request('/api/business-partners', 'get', { role: 'supplier', page: 1, pageSize: 10 });
    const partners = await createBlueprintDemoApiAdapter(partnerConfig)(partnerConfig);
    expect((partners.data as { items: unknown[] }).items.length).toBeGreaterThan(0);
    expect((partners.data as { totalPages: number }).totalPages).toBeGreaterThan(0);

    const transferConfig = request('/api/stock-transfers', 'get', { pageIndex: 1, pageSize: 15 });
    const transfers = await createBlueprintDemoApiAdapter(transferConfig)(transferConfig);
    expect((transfers.data as { items: Array<{ code: string }> }).items[0].code).toMatch(/^TRF-/);
  });

  it('serves inventory, approval, receipt and putaway reads', async () => {
    const endpoints = [
      '/api/InventoryStocks/current?',
      '/api/approvals/queue?pageIndex=1&pageSize=20',
      '/api/purchase-orders',
      '/api/asns',
      '/api/importreceipts',
      '/api/exportreceipts',
      '/api/putaway-tasks',
      '/api/stock-reservations?page=1&pageSize=20',
      '/api/stocktakes',
    ];

    for (const url of endpoints) {
      const config = request(url);
      const response = await createBlueprintDemoApiAdapter(config)(config);
      expect(response.status).toBe(200);
    }
  });

  it('serves WH-03 location capacity data for the production warehouse page', async () => {
    const config = request('/api/putaway-tasks/location-capacity', 'get', { warehouseId: 1 });
    const response = await createBlueprintDemoApiAdapter(config)(config);
    const rows = response.data as Array<{ code: string; state: string; maxWeightKg?: number | null }>;

    expect(response.status).toBe(200);
    expect(rows.some(item => item.code === 'A01-R02-L03-B04' && item.state === 'NearCapacity')).toBe(true);
    expect(rows.find(item => item.code === 'A01-R02-L03-B04')?.maxWeightKg).toBe(1500);
  });

  it('serves WH-04 warehouse map snapshots from demo data', async () => {
    const config = request('/api/putaway-tasks/location-map', 'get', { warehouseId: 1 });
    const response = await createBlueprintDemoApiAdapter(config)(config);
    const data = response.data as { warehouseId: number; generatedAtUtc: string; items: Array<{ code: string; activityLevel: string; mapX?: number | null }> };

    expect(response.status).toBe(200);
    expect(data.warehouseId).toBe(1);
    expect(data.generatedAtUtc).toBeTruthy();
    expect(data.items.some(item => item.code === 'A01-R02-L03-B04' && item.activityLevel === 'High' && item.mapX === 6)).toBe(true);
  });

  it('filters reversal candidates and serves reversal-aware inventory traceability', async () => {
    const moveConfig = request('/api/InventoryTransactions?transactionType=Move&page=1&pageSize=100');
    const moves = await createBlueprintDemoApiAdapter(moveConfig)(moveConfig);
    const moveItems = (moves.data as { items: Array<{ transactionType: string; id: number }> }).items;
    expect(moveItems.length).toBeGreaterThan(0);
    expect(moveItems.every(item => item.transactionType === 'Move')).toBe(true);

    const reversalConfig = request('/api/InventoryTransactions?transactionType=Reversal&page=1&pageSize=100');
    const reversals = await createBlueprintDemoApiAdapter(reversalConfig)(reversalConfig);
    expect((reversals.data as { items: Array<{ referenceType: string; referenceId: number }> }).items)
      .toContainEqual(expect.objectContaining({ referenceType: 'InventoryReversal', referenceId: 4 }));

    const traceConfig = request('/api/inventory/traceability?productId=1&lotNumber=LOT-ARABICA-2609&limit=200');
    const trace = await createBlueprintDemoApiAdapter(traceConfig)(traceConfig);
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

  it('does not expose WH-02 pseudo-backend reads', async () => {
    for (const url of [
      '/api/warehouses/1/structure',
      '/api/warehouses/1/zones',
      '/api/warehouses/1/locations',
      '/api/locations?warehouseId=1',
    ]) {
      const config = request(url);
      await expect(createBlueprintDemoApiAdapter(config)(config)).rejects.toMatchObject({
        response: { status: 404 },
      });
    }
  });

  it('fails closed for mutations instead of pretending a write succeeded', async () => {
    const config = request('/api/products', 'post');
    await expect(createBlueprintDemoApiAdapter(config)(config)).rejects.toMatchObject({
      response: { status: 405 },
    });
  });

  it('fails visibly when a read endpoint has not been mapped', async () => {
    const config = request('/api/not-mapped');
    await expect(createBlueprintDemoApiAdapter(config)(config)).rejects.toMatchObject({
      response: { status: 404 },
    });
  });
});
