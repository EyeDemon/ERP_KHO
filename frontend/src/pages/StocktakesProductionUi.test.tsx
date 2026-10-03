// @vitest-environment jsdom
import { cleanup, fireEvent, render, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Stocktakes from './Stocktakes';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

const summary = {
  id: 1,
  code: 'ST-2026-0001',
  warehouseId: 1,
  warehouseName: 'DC Hồ Chí Minh',
  status: 0,
  note: 'Kiểm kê cuối ngày',
  createdBy: 1,
  createdByName: 'Nhân viên kho',
  createdAt: '2026-10-03T08:00:00Z',
  detailCount: 1,
};

const detail = {
  ...summary,
  details: [{
    id: 11,
    stocktakeId: 1,
    productId: 10,
    productCode: 'SKU-10',
    productName: 'Sản phẩm 10',
    unitName: 'Cái',
    systemQuantity: 12,
    actualQuantity: null,
    differenceQuantity: 0,
    note: '',
  }],
};

describe('Stocktakes shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    get.mockImplementation(async (url) => {
      if (url === '/api/stocktakes') return { data: [summary] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'DC Hồ Chí Minh', isActive: true }] } as never;
      if (url === '/api/stocktakes/1') return { data: detail } as never;
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders the stocktake list semantically and exposes the create form', async () => {
    const view = render(<Stocktakes />);

    expect(await view.findByText('ST-2026-0001')).toBeTruthy();
    const table = view.getByRole('table', { name: 'Danh sách phiếu kiểm kê' });
    expect(table).toBeTruthy();
    expect(within(table).getByText('Bản nháp')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu kiểm kê' }));

    expect(await view.findByText('Tạo phiếu kiểm kê mới')).toBeTruthy();
    expect(view.getByLabelText('Kho hàng kiểm kê')).toBeTruthy();
    expect(view.getByLabelText('Ghi chú kiểm kê')).toBeTruthy();
  });

  it('keeps draft detail editing and approval controls reachable', async () => {
    const view = render(<Stocktakes />);

    await view.findByText('ST-2026-0001');
    fireEvent.click(view.getByRole('button', { name: 'Xem chi tiết' }));

    expect(await view.findByRole('table', { name: 'Chi tiết kiểm kê ST-2026-0001' })).toBeTruthy();
    expect(view.getByRole('button', { name: 'Duyệt phiếu' })).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Nhập SL' }));
    expect(view.getByLabelText('Số lượng thực tế SKU-10')).toBeTruthy();
    expect(view.getByLabelText('Ghi chú kiểm kê SKU-10')).toBeTruthy();
  });
});
