// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Inventory from './Inventory';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

describe('Inventory shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    get.mockImplementation(async (url) => {
      const value = String(url);
      if (value === '/api/warehouses') {
        return { data: [{ id: 1, name: 'DC Hồ Chí Minh', isActive: true }] } as never;
      }
      if (value.startsWith('/api/InventoryStocks/current?')) {
        return {
          data: [{
            productId: 10,
            productCode: 'SKU-10',
            productName: 'Sản phẩm 10',
            unitName: 'Cái',
            warehouseId: 1,
            warehouseName: 'DC Hồ Chí Minh',
            quantity: 12,
            onHandQuantity: 12,
            reservedQuantity: 2,
            availableQuantity: 10,
            lastUpdated: '2026-10-03T08:00:00Z',
          }],
        } as never;
      }
      if (value === '/api/inventory/statuses') {
        return { data: [{ code: 'AVAILABLE', name: 'Available', isAvailable: true, isReservable: true, isAllocatable: true, isPickable: true, isShippable: true }] } as never;
      }
      if (value.startsWith('/api/inventory/buckets?')) {
        return { data: [{
          inventoryStockId: 55,
          productId: 10,
          productCode: 'SKU-10',
          productName: 'Sản phẩm 10',
          warehouseId: 1,
          warehouseName: 'DC Hồ Chí Minh',
          locationId: 7,
          locationCode: 'A01-R01-B01',
          status: 'AVAILABLE',
          isReservable: true,
          isAllocatable: true,
          isPickable: true,
          isShippable: true,
          lotId: 20,
          lotNumber: 'LOT-PROD-10',
          expiryDate: '2027-01-01T00:00:00Z',
          serialId: null,
          serialNumber: null,
          onHandQuantity: 12,
          reservedQuantity: 2,
          availableQuantity: 10,
          lastUpdated: '2026-10-03T08:00:00Z',
        }] } as never;
      }
      if (value.startsWith('/api/InventoryTransactions?')) {
        return {
          data: {
            items: [{
              id: 100,
              productId: 10,
              productCode: 'SKU-10',
              productName: 'Sản phẩm 10',
              unitName: 'Cái',
              warehouseId: 1,
              warehouseName: 'DC Hồ Chí Minh',
              transactionType: 'Import',
              quantity: 5,
              referenceId: 21,
              referenceType: 'ImportReceipt',
              transactionDate: '2026-10-03T09:00:00Z',
              createdBy: 2,
              createdByName: 'Kho',
              note: 'Nhập hàng',
            }],
            totalRecords: 1,
            pageIndex: 1,
            pageSize: 20,
            totalPages: 1,
          },
        } as never;
      }
      if (value.startsWith('/api/Reports/inventory-in-out-stock?')) {
        return {
          data: [{
            productId: 10,
            productCode: 'SKU-10',
            productName: 'Sản phẩm 10',
            unitName: 'Cái',
            warehouseId: 1,
            warehouseName: 'DC Hồ Chí Minh',
            openingQuantity: 7,
            inQuantity: 5,
            outQuantity: 2,
            closingQuantity: 10,
          }],
        } as never;
      }
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders semantic tabs and keeps all three inventory views reachable', async () => {
    const view = render(<Inventory />);

    expect(await view.findByText('SKU-10')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Tồn kho hiện tại' })).toBeTruthy();
    expect(view.getByRole('tab', { name: 'Tồn kho hiện tại' }).getAttribute('aria-selected')).toBe('true');
    expect(view.getByRole('button', { name: 'Xuất Excel Tồn Kho' })).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Bucket / Lot / Serial' }));
    expect(await view.findByRole('table', { name: 'Inventory bucket' })).toBeTruthy();
    expect(await view.findByText('LOT-PROD-10')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Lịch sử giao dịch' }));
    expect(await view.findByRole('table', { name: 'Lịch sử giao dịch tồn kho' })).toBeTruthy();
    expect(await view.findByText('Nhập hàng')).toBeTruthy();

    fireEvent.click(view.getByRole('tab', { name: 'Xuất nhập tồn' }));
    expect(await view.findByRole('table', { name: 'Báo cáo xuất nhập tồn' })).toBeTruthy();
    expect(await view.findByText('7')).toBeTruthy();
  });
});
