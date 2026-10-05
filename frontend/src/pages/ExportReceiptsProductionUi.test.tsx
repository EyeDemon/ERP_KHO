// @vitest-environment jsdom
import { cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import ExportReceipts from './ExportReceipts';
import { setCurrentPermissions } from '../services/authorization';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

const draftReceipt = {
  id: 21,
  code: 'ER-2026-0021',
  warehouseName: 'DC Hồ Chí Minh',
  status: 'Draft',
  note: '',
  createdBy: 5,
  createdByName: 'Nhân viên kho',
  createdAt: '2026-10-03T10:00:00Z',
  allowWarehouseStaffDirectDispatch: false,
  writeEnabled: true,
  details: [],
  customerId: 9,
  customerCode: 'CUS-09',
  customerName: 'Khách hàng A',
};

describe('ExportReceipts shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    setCurrentPermissions([]);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [draftReceipt] } as never;
      if (url === '/api/warehouses') return { data: [] } as never;
      if (url === '/api/products') return { data: [] } as never;
      if (url === '/api/business-partners') return { data: { items: [] } } as never;
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders the outbound list semantically without mutation actions for Viewer', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);

    const view = render(<ExportReceipts />);

    expect(await view.findByText('ER-2026-0021')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách phiếu xuất' })).toBeTruthy();
    expect(view.getByText('Nháp')).toBeTruthy();
    expect(view.queryByText('Tạo Phiếu Xuất Kho')).toBeNull();
    expect(view.queryByRole('button', { name: 'Duyệt và giữ hàng' })).toBeNull();
    expect(view.queryByRole('button', { name: /Duyệt và xuất ngay/ })).toBeNull();
    expect(view.queryByRole('button', { name: 'Hủy' })).toBeNull();
  });

  it('keeps write-gated workflow buttons disabled during maintenance', async () => {
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    setCurrentPermissions([
      'export_receipt.read',
      'export_receipt.create',
      'export_receipt.update',
      'export_receipt.approve',
      'export_receipt.dispatch',
      'export_receipt.cancel',
      'partner.read',
    ]);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [{ ...draftReceipt, allowPerReceiptDispatchMode: true, writeEnabled: false }] } as never;
      if (url === '/api/warehouses') return { data: [] } as never;
      if (url === '/api/products') return { data: [] } as never;
      if (url === '/api/business-partners') return { data: { items: [] } } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);

    expect(await view.findByText('Workflow xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.')).toBeTruthy();
    expect((view.getByRole('button', { name: 'Duyệt và giữ hàng' }) as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByRole('button', { name: 'Duyệt và xuất ngay (tương thích)' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
