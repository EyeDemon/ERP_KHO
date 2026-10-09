// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const transfer = {
  id: 807, code: 'TRF-807', sourceWarehouseId: 1, sourceWarehouseName: 'Kho nguồn',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho đích',
  status: 'InTransit', createdBy: 1, createdAt: '2026-10-08T09:00:00Z',
  details: [{
    productId: 9, productCode: 'SKU-9', productName: 'Sản phẩm',
    requestedQuantity: 5, dispatchedQuantity: 5, receivedQuantity: 0,
    missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 5,
  }],
};

describe('Stock transfer command consistency', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'WarehouseStaff');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return { data: { items: [transfer], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/807') return { data: transfer } as never;
      if (url === '/api/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/products') return {
        data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }],
      } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('retries an unchanged receive payload with the same key but uses a new key after editing quantities', async () => {
    post.mockRejectedValueOnce(new Error('offline')).mockRejectedValueOnce(new Error('offline'));
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    const receiveButton = await view.findByRole('button', { name: 'Xác nhận nhận' });

    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    await view.findByRole('alert');
    const firstKey = (post.mock.calls[0][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];

    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
    const retryKey = (post.mock.calls[1][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(retryKey).toBe(firstKey);

    fireEvent.change(view.getByLabelText('Thực nhận SKU-9'), { target: { value: '3' } });
    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(3));
    const editedKey = (post.mock.calls[2][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(editedKey).not.toBe(firstKey);
    expect(post.mock.calls[2][1]).toEqual({
      details: [{ productId: 9, receivedQuantity: 3, missingQuantity: 0, damagedQuantity: 0 }],
    });
  });

  it('submits create only once while the first request is unresolved', async () => {
    let resolveCreate!: (response: unknown) => void;
    post.mockReturnValue(new Promise(resolve => { resolveCreate = resolve; }) as never);
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });

    fireEvent.change(view.getByLabelText('Kho nguồn'), { target: { value: '1' } });
    fireEvent.change(view.getByLabelText('Kho đích'), { target: { value: '2' } });
    fireEvent.change(view.getByLabelText('Sản phẩm dòng 1'), { target: { value: '9' } });
    fireEvent.change(view.getByLabelText('Số lượng dòng 1'), { target: { value: '5' } });

    fireEvent.submit(dialog);
    fireEvent.submit(dialog);
    expect(post).toHaveBeenCalledTimes(1);
    expect((view.getByRole('button', { name: 'Đang tạo phiếu...' }) as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByRole('button', { name: 'Đóng tạo phiếu' }) as HTMLButtonElement).disabled).toBe(true);

    await act(async () => { resolveCreate({ data: {} }); });
    await waitFor(() => expect(view.queryByRole('dialog', { name: 'Tạo phiếu điều chuyển' })).toBeNull());
  });

  it('ignores an older list response after a newer filtered query has completed', async () => {
    let resolveOld!: (response: unknown) => void;
    let listCalls = 0;
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') {
        listCalls += 1;
        if (listCalls === 1) return new Promise(resolve => { resolveOld = resolve; }) as never;
        return { data: { items: [{ ...transfer, id: 808, code: 'TRF-NEW' }], totalPages: 1 } } as never;
      }
      if (url === '/api/warehouses' || url === '/api/products') return { data: [] } as never;
      return { data: transfer } as never;
    });

    const view = render(<StockTransfers />);
    fireEvent.change(view.getByLabelText('Tìm mã phiếu điều chuyển'), { target: { value: 'TRF-NEW' } });
    fireEvent.click(view.getByRole('button', { name: 'Lọc' }));
    expect(await view.findByText('TRF-NEW')).toBeTruthy();

    await act(async () => {
      resolveOld({ data: { items: [transfer], totalPages: 1 } });
    });
    expect(view.queryByText('TRF-807')).toBeNull();
    expect(view.getByText('TRF-NEW')).toBeTruthy();
  });
});
