// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
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

  it('aggregates availability across eligible locations before blocking draft creation', async () => {
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    setCurrentPermissions(['export_receipt.read', 'export_receipt.create']);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'Kho A' }] } as never;
      if (url === '/api/products') return { data: [{ id: 7, code: 'SKU-7', name: 'Sản phẩm 7' }] } as never;
      if (url === '/api/inventorystocks/current') return { data: [{ availableQuantity: 20 }, { availableQuantity: 30 }] } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findByText('Tạo Phiếu Xuất Kho');

    fireEvent.change(view.getByLabelText('Mã phiếu'), { target: { value: 'EX-ML-01' } });
    fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
    fireEvent.click(view.getByRole('button', { name: '+ Thêm dòng' }));
    fireEvent.change(view.getByLabelText('Sản phẩm dòng 1'), { target: { value: '7' } });
    fireEvent.change(view.getByLabelText('Số lượng dòng 1'), { target: { value: '40' } });
    fireEvent.change(view.getByLabelText('Đơn giá dòng 1'), { target: { value: '1' } });

    await view.findByText('Tồn: 50');
    await waitFor(() => expect((view.getByRole('button', { name: 'Tạo Phiếu Xuất' }) as HTMLButtonElement).disabled).toBe(false));
  });

  it('renders canonical Vietnamese states and never exposes raw dispatch mode', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    const dispatched = {
      ...draftReceipt,
      id: 22,
      code: 'ER-2026-0022',
      status: 'Dispatched',
      dispatchMode: 'RequireSeparateDispatch',
      approvedByName: 'Người duyệt',
      dispatchedByName: 'Thủ kho',
      approvedAt: '2026-10-03T11:00:00Z',
      dispatchedAt: '2026-10-03T12:00:00Z',
    };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [dispatched] } as never;
      if (url === '/api/exportreceipts/22') return { data: dispatched } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findByText('ER-2026-0022');
    fireEvent.click(view.getByRole('button', { name: 'Chi tiết' }));

    expect(await view.findByText('Duyệt và giữ hàng → xác nhận xuất kho')).toBeTruthy();
    expect(view.queryByText('RequireSeparateDispatch')).toBeNull();
  });

  it('fails closed without exposing backend error details', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    get.mockRejectedValue({ response: { status: 403, data: { message: 'SQL/internal secret' } } });

    const view = render(<ExportReceipts />);

    expect(await view.findByText('Bạn không còn quyền thực hiện thao tác này.')).toBeTruthy();
    expect(view.queryByText('SQL/internal secret')).toBeNull();
    expect(view.queryByText('ER-2026-0021')).toBeNull();
  });

  it('uses the canonical approved-and-reserved wording in export print preview', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    const approved = {
      ...draftReceipt,
      id: 23,
      code: 'ER-2026-0023',
      status: 'Approved',
      approvedByName: 'Người duyệt',
      approvedAt: '2026-10-03T11:00:00Z',
    };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [approved] } as never;
      if (url === '/api/exportreceipts/23') return { data: approved } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findByText('ER-2026-0023');
    fireEvent.click(view.getByRole('button', { name: 'Xem bản in' }));

    expect((await view.findAllByText('Đã duyệt và giữ hàng')).length).toBeGreaterThanOrEqual(2);
    expect(view.queryByText(/^Đã duyệt$/)).toBeNull();
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

    expect(await view.findByText('Luồng xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.')).toBeTruthy();
    expect((view.getByRole('button', { name: 'Duyệt và giữ hàng' }) as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByRole('button', { name: 'Duyệt và xuất ngay (tương thích)' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
