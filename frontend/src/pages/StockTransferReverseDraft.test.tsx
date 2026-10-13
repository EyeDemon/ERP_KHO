// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const received = {
  id: 91, code: 'TRF-91', sourceWarehouseId: 1, sourceWarehouseName: 'Kho A',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho B',
  status: 'Received', createdBy: 1, createdAt: '2026-10-08T09:00:00Z',
  details: [{
    productId: 9, productCode: 'SKU-9', productName: 'Sản phẩm',
    requestedQuantity: 8, dispatchedQuantity: 8, receivedQuantity: 8,
    missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 0,
  }],
};
const draft = {
  ...received, id: 92, code: 'TRF-92', status: 'Draft',
  sourceWarehouseId: 2, destinationWarehouseId: 1, reverseOfTransferId: 91,
};

describe('Native reverse transfer after receipt', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('permissions', '["inventory_reversal.create"]');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return { data: { items: [received], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/91') return { data: received } as never;
      if (url === '/api/stock-transfers/92') return { data: draft } as never;
      if (url === '/api/stock-transfers/return-reasons') return {
        data: [{ code: 'TRANSFER_ROUTE_ERROR', name: 'Sai tuyến điều chuyển' }],
      } as never;
      if (url === '/api/warehouses' || url === '/api/products') return { data: [] } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: draft } as never);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it('requires a server-provided reason and creates a new linked draft', async () => {
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-91' }));
    const button = await view.findByRole('button', { name: 'Tạo phiếu điều chuyển ngược' });
    expect((button as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(await view.findByLabelText('Mã lý do điều chuyển ngược'),
      { target: { value: 'TRANSFER_ROUTE_ERROR' } });
    fireEvent.change(view.getByLabelText('Diễn giải điều chuyển ngược'),
      { target: { value: 'Đã nhận sai tuyến' } });
    fireEvent.click(button);
    await waitFor(() => expect(post).toHaveBeenCalledWith(
      '/api/stock-transfers/91/reverse-draft',
      { reasonCode: 'TRANSFER_ROUTE_ERROR', reason: 'Đã nhận sai tuyến' },
      { headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }
    ));
    expect(await view.findByRole('dialog', { name: 'TRF-92' })).toBeTruthy();
  });

  it('hides reverse mutation without permission', async () => {
    localStorage.setItem('permissions', '[]');
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-91' }));
    expect(await view.findByRole('dialog', { name: 'TRF-91' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Tạo phiếu điều chuyển ngược' })).toBeNull();
    expect(get.mock.calls.some(([url]) => url === '/api/stock-transfers/return-reasons')).toBe(false);
  });

  it('fails closed when reason catalog cannot be fetched', async () => {
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers/return-reasons') throw new Error('offline');
      if (url === '/api/stock-transfers') return { data: { items: [received], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/91') return { data: received } as never;
      return { data: [] } as never;
    });
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-91' }));
    expect(await view.findByRole('alert')).toBeTruthy();
    expect((view.getByRole('button', { name: 'Tạo phiếu điều chuyển ngược' }) as HTMLButtonElement).disabled).toBe(true);
    expect(post).not.toHaveBeenCalled();
  });
});
