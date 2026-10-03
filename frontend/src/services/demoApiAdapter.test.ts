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

  it('serves the WH-02 warehouse hierarchy and canonical location reads', async () => {
    const structureConfig = request('/api/warehouses/1/structure');
    const structure = await createBlueprintDemoApiAdapter(structureConfig)(structureConfig);
    const payload = structure.data as { zones: Array<{ code: string; aisles: unknown[] }>; systemLocations: Array<{ code: string }> };
    expect(payload.zones[0].code).toBe('ZONE-A');
    expect(payload.zones[0].aisles.length).toBeGreaterThan(0);
    expect(payload.systemLocations.some(item => item.code === 'RECEIVING')).toBe(true);

    const locationsConfig = request('/api/locations', 'get', { warehouseId: 1 });
    const locations = await createBlueprintDemoApiAdapter(locationsConfig)(locationsConfig);
    expect((locations.data as Array<{ code: string }>).some(item => item.code === 'A01-R02-L03-B04')).toBe(true);
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
