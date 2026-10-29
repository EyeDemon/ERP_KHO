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
  allocatedQuantity: 0,
  remainingQuantity: 20,
  allocatableQuantity: 20,
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

  it('releases an active Manual reservation once and refreshes the current page', async () => {
    localStorage.setItem('role', 'Manager');
    get.mockResolvedValue({
      data: {
        items: [{ ...reservation, sourceType: 'Manual', sourceCode: undefined }],
        totalRecords: 1,
        pageIndex: 1,
        pageSize: 20,
      },
    } as never);

    const view = render(<StockReservations />);
    await view.findByText('RSV-2026-0007');
    expect(view.getByText('Giữ hàng thủ công')).toBeTruthy();
    fireEvent.click(view.getByRole('button', { name: 'Giải phóng RSV-2026-0007' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith('/api/stock-reservations/7/release', {
      reason: 'Released from reservation screen',
    });
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
  });

  it('shows Theo chứng từ gốc badge for document-owned reservations, hides Giải phóng button, and maps 409 DOCUMENT_OWNED_RESERVATION', async () => {
    localStorage.setItem('role', 'Manager');
    get.mockResolvedValue({
      data: {
        items: [
          reservation,
          { ...reservation, id: 8, reservationCode: 'RSV-2026-0008', sourceType: 'OutboundOrder', sourceCode: 'OB-2026-01' },
        ],
        totalRecords: 2,
        pageIndex: 1,
        pageSize: 20,
      },
    } as never);

    const view = render(<StockReservations />);
    await view.findByText('RSV-2026-0007');
    expect(view.queryByRole('button', { name: 'Giải phóng RSV-2026-0007' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Giải phóng RSV-2026-0008' })).toBeNull();
    expect(view.getAllByText('Quản lý theo chứng từ').length).toBe(2);
    expect(view.getByText(/Đơn xuất hàng \/ OB-2026-01/)).toBeTruthy();

    post.mockRejectedValueOnce({
      response: {
        status: 409,
        data: { code: 'DOCUMENT_OWNED_RESERVATION' },
      },
    });
    fireEvent.click(view.getByRole('button', { name: /Dọn hết hạn/ }));
    expect(
      await view.findByText(
        'Bản ghi giữ hàng này thuộc quyền quản lý của chứng từ gốc. Vui lòng thao tác trực tiếp trên Phiếu xuất hoặc Đơn hàng.',
      ),
    ).toBeTruthy();
  });
});

