// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const transfer = {
  id: 7,
  code: 'TRF-2026-0007',
  sourceWarehouseId: 1,
  sourceWarehouseName: 'Kho nguồn',
  destinationWarehouseId: 2,
  destinationWarehouseName: 'Kho đích',
  status: 'Draft',
  note: '',
  createdBy: 1,
  createdAt: '2026-10-03T08:00:00Z',
  details: [{
    productId: 10,
    productCode: 'SKU-10',
    productName: 'Sản phẩm 10',
    requestedQuantity: 5,
    dispatchedQuantity: 0,
    receivedQuantity: 0,
    missingQuantity: 0,
    damagedQuantity: 0,
    inTransitQuantity: 0,
  }],
};

describe('StockTransfers shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async (url) => {
      if (url === '/api/stock-transfers') {
        return { data: { items: [transfer], totalPages: 1 } } as never;
      }
      if (url === '/api/warehouses') {
        return { data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }] } as never;
      }
      if (url === '/api/products') {
        return { data: [{ id: 10, code: 'SKU-10', name: 'Sản phẩm 10' }] } as never;
      }
      if (url === '/api/stock-transfers/7') {
        return { data: transfer } as never;
      }
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders a semantic list and keeps mutation controls hidden for Viewer', async () => {
    localStorage.setItem('role', 'Viewer');

    const view = render(<StockTransfers />);

    expect(await view.findByText('TRF-2026-0007')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách phiếu điều chuyển' })).toBeTruthy();
    expect(view.queryByRole('button', { name: /Tạo phiếu/ })).toBeNull();

    fireEvent.click(view.getByRole('button', { name: 'Xem chi tiết TRF-2026-0007' }));
    expect(await view.findByRole('dialog', { name: 'TRF-2026-0007' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Duyệt' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Hủy phiếu' })).toBeNull();
  });


  it('opens an accessible create dialog for warehouse staff', async () => {
    localStorage.setItem('role', 'WarehouseStaff');

    const view = render(<StockTransfers />);

    await view.findByText('TRF-2026-0007');
    fireEvent.click(view.getByRole('button', { name: /Tạo phiếu/ }));

    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });
    expect(dialog).toBeTruthy();
    expect(within(dialog).getByLabelText('Kho nguồn')).toBeTruthy();
    expect(within(dialog).getByLabelText('Kho đích')).toBeTruthy();
    expect(within(dialog).getByLabelText('Sản phẩm dòng 1')).toBeTruthy();
    expect(within(dialog).getByLabelText('Số lượng dòng 1')).toBeTruthy();
  });

  it('preserves manager approval and idempotency from the detail dialog', async () => {
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');

    const view = render(<StockTransfers />);

    await view.findByText('TRF-2026-0007');
    fireEvent.click(view.getByRole('button', { name: 'Xem chi tiết TRF-2026-0007' }));
    await view.findByRole('dialog', { name: 'TRF-2026-0007' });

    fireEvent.click(view.getByRole('button', { name: 'Duyệt' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith(
      '/api/stock-transfers/7/approve',
      undefined,
      { headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }
    );
  });
});
