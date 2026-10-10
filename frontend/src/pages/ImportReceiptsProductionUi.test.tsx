// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import ImportReceipts from './ImportReceipts';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

const receipt = {
  id: 11,
  code: 'IR-2026-0011',
  warehouseId: 1,
  warehouseName: 'DC Hồ Chí Minh',
  status: 'Draft',
  note: '',
  createdBy: 7,
  createdByName: 'Nhân viên kho',
  createdAt: '2026-10-03T08:00:00Z',
  approvedBy: 0,
  approvedByName: '',
  approvedAt: '',
  supplierId: 3,
  supplierCode: 'SUP-01',
  supplierName: 'Nhà cung cấp A',
  requiresQc: false,
  details: [],
};

describe('ImportReceipts shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Viewer');
    localStorage.setItem('permissions', '["receipt.read"]');
    get.mockImplementation(async (url) => {
      if (url === '/api/importreceipts') return { data: [receipt] } as never;
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders the inbound list semantically for a read-only user', async () => {
    const view = render(<ImportReceipts />);

    expect(await view.findByText('IR-2026-0011')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách phiếu nhập' })).toBeTruthy();
    expect(view.getByText('Nháp')).toBeTruthy();
    expect(view.queryByText('Tạo Phiếu Nhập (Nháp)')).toBeNull();
    expect(view.queryByText('Hủy')).toBeNull();
    expect(view.queryByText('Ghi nhận số lượng thực tế')).toBeNull();
  });

  it('does not request reference master data without the matching read grants', async () => {
    const view = render(<ImportReceipts />);
    await view.findByText('IR-2026-0011');

    expect(get).toHaveBeenCalledWith('/api/importreceipts');
    expect(get).not.toHaveBeenCalledWith('/api/warehouses');
    expect(get).not.toHaveBeenCalledWith('/api/products');
    expect(get).not.toHaveBeenCalledWith('/api/business-partners', expect.anything());
  });
});
