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
const transfer = {
  id: 81, code: 'TRF-RETURN-81', sourceWarehouseId: 1, sourceWarehouseName: 'Kho nguồn',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho đích',
  status: 'InTransit', createdBy: 1, createdAt: '2026-10-08T09:00:00Z',
  details: [{
    productId: 9, productCode: 'SKU-9', productName: 'Sản phẩm',
    requestedQuantity: 5, dispatchedQuantity: 5, receivedQuantity: 0,
    missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 5,
  }],
};

describe('StockTransfer return-to-source UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'WarehouseStaff');
    localStorage.setItem('permissions', '["inventory_reversal.create"]');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return { data: { items: [transfer], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/81') return { data: transfer } as never;
      if (url === '/api/stock-transfers/return-reasons') return {
        data: [{ code: 'TRANSFER_DISPATCH_ERROR', name: 'Xuất điều chuyển sai' }],
      } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }] } as never;
      if (url === '/api/products') return { data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }] } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it('requires controlled reason code and explanation before idempotent return', async () => {
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-RETURN-81' }));
    const button = await view.findByRole('button', { name: 'Hoàn trả kho nguồn' });
    expect((button as HTMLButtonElement).disabled).toBe(true);
    const code = await view.findByLabelText('Mã lý do hoàn trả');
    await view.findByRole('option', { name: 'Xuất điều chuyển sai' });
    fireEvent.change(code, { target: { value: 'TRANSFER_DISPATCH_ERROR' } });
    fireEvent.change(view.getByLabelText('Diễn giải hoàn trả'), { target: { value: 'Kho đích chưa tiếp nhận' } });
    fireEvent.click(button);
    await waitFor(() => expect(post).toHaveBeenCalledWith(
      '/api/stock-transfers/81/return',
      { reasonCode: 'TRANSFER_DISPATCH_ERROR', reason: 'Kho đích chưa tiếp nhận' },
      { headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }
    ));
  });

  it('fails closed when authoritative reason catalog cannot be loaded', async () => {
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers/return-reasons') throw new Error('offline');
      if (url === '/api/stock-transfers') return { data: { items: [transfer], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/81') return { data: transfer } as never;
      return { data: [] } as never;
    });
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-RETURN-81' }));
    expect(await view.findByRole('alert')).toBeTruthy();
    expect((view.getByRole('button', { name: 'Hoàn trả kho nguồn' }) as HTMLButtonElement).disabled).toBe(true);
    expect(post).not.toHaveBeenCalled();
  });
});
