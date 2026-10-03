// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockReservations from './StockReservations';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const reservation = {
  id: 7,
  reservationCode: 'RSV-2026-0007',
  productCode: 'SKU-1001',
  productName: 'Cà phê Arabica 500g',
  warehouseName: 'DC Hồ Chí Minh',
  quantity: 20,
  consumedQuantity: 0,
  releasedQuantity: 0,
  remainingQuantity: 20,
  status: 'Active',
  sourceType: 'ExportReceipt',
  sourceCode: 'EX-2026-0012',
  createdAt: '2026-10-03T08:00:00Z',
  expiresAt: '2026-10-04T08:00:00Z',
};

describe('StockReservations production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockResolvedValue({
      data: { items: [reservation], totalRecords: 1, pageIndex: 1, pageSize: 20 },
    } as never);
    post.mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('renders semantic reservation data without mutation controls for Viewer', async () => {
    localStorage.setItem('role', 'Viewer');

    const view = render(<StockReservations />);

    expect(await view.findByText('RSV-2026-0007')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách giữ hàng' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Giải phóng RSV-2026-0007' })).toBeNull();
    expect(view.queryByRole('button', { name: /Dọn hết hạn/ })).toBeNull();
  });

  it('releases an active reservation once and refreshes the current page', async () => {
    localStorage.setItem('role', 'Manager');

    const view = render(<StockReservations />);
    await view.findByText('RSV-2026-0007');
    fireEvent.click(view.getByRole('button', { name: 'Giải phóng RSV-2026-0007' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith('/api/stock-reservations/7/release', {
      reason: 'Released from reservation screen',
    });
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
  });
});
