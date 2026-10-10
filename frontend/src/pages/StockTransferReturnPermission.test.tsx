// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

afterEach(() => { cleanup(); vi.resetAllMocks(); localStorage.clear(); });

it('hides return controls and never fetches reason catalog without inventory reversal permission', async () => {
  localStorage.setItem('role', 'Manager');
  localStorage.setItem('permissions', '[]');
  vi.mocked(apiClient.get).mockImplementation(async url => {
    if (url === '/api/stock-transfers') return { data: { items: [{ id: 11, code: 'TRF-11', status: 'InTransit', sourceWarehouseName: 'Kho A', destinationWarehouseName: 'Kho B', createdBy: 1 }], totalPages: 1 } } as never;
    if (url === '/api/stock-transfers/11') return { data: { id: 11, code: 'TRF-11', status: 'InTransit', sourceWarehouseName: 'Kho A', destinationWarehouseName: 'Kho B', createdBy: 1, details: [] } } as never;
    return { data: [] } as never;
  });
  const view = render(<StockTransfers />);
  fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-11' }));
  expect(await view.findByRole('dialog', { name: 'TRF-11' })).toBeTruthy();
  expect(view.queryByRole('button', { name: 'Hoàn trả kho nguồn' })).toBeNull();
  expect(vi.mocked(apiClient.get).mock.calls.some(([url]) => url === '/api/stock-transfers/return-reasons')).toBe(false);
});
