// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import ExportReceipts from './ExportReceipts';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const get = vi.mocked(apiClient.get), post = vi.mocked(apiClient.post);
const receipt = { id: 3, code: 'X3', status: 'Draft', rowVersion: 'AAAAAAAAAAE=', warehouseName: 'Kho', createdBy: 2, createdAt: '2026-10-04T08:00:00Z', writeEnabled: true, details: [] };
const read = ['export_receipt.read'];
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin'); localStorage.setItem('userId', '1');
  localStorage.setItem('permissions', JSON.stringify(read)); vi.spyOn(window, 'confirm').mockReturnValue(true);
  get.mockImplementation(async url => ({ data: url === '/api/exportreceipts' ? [receipt] : url === '/api/exportreceipts/3' ? receipt : [] }) as never);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('phiếu xuất dùng capability riêng và response generation', () => {
  it('four-decimal base quantity stays usable when optional stock lookup fails', async () => {
    localStorage.setItem('permissions',JSON.stringify([...read,'export_receipt.create','warehouse.read','product.read']));
    get.mockImplementation(async url => {
      if(url.startsWith('/api/inventorystocks')) throw new Error('Optional stock lookup unavailable');
      return {data:url==='/api/warehouses'?[{id:1,name:'Kho'}]:url==='/api/products'?[{id:2,code:'SP',name:'Sản phẩm'}]:[]} as never;
    });
    post.mockResolvedValue({} as never);
    const view=render(<ExportReceipts />);
    await waitFor(()=>expect(view.getByLabelText('Kho').querySelectorAll('option').length).toBe(2));
    fireEvent.change(view.getByLabelText('Mã phiếu'),{target:{value:'BASE-PRECISION'}});
    fireEvent.change(view.getByLabelText('Kho'),{target:{value:'1'}});
    fireEvent.click(view.getByText('+ Thêm dòng'));
    fireEvent.change(view.getByLabelText('Sản phẩm dòng 1'),{target:{value:'2'}});
    const quantity=view.getByLabelText('Số lượng dòng 1') as HTMLInputElement;
    fireEvent.change(quantity,{target:{value:'0.0001'}});
    fireEvent.change(view.getByLabelText('Đơn giá dòng 1'),{target:{value:'1'}});
    expect(quantity.checkValidity()).toBe(true);
    await view.findByText(/Lỗi tải tồn/);
    const button=view.getByRole('button',{name:/^Tạo Phiếu Xuất$/}) as HTMLButtonElement;
    expect(button.disabled).toBe(false);fireEvent.click(button);
    await waitFor(()=>expect(post).toHaveBeenCalledTimes(1));
    expect(post.mock.calls[0][1]).toMatchObject({details:[{productId:2,quantity:0.0001}]});
  });
  it('receipt-only reader does not fetch optional masters or inherit Admin mutation rights', async () => {
    const view = render(<ExportReceipts />); await view.findByText('X3');
    expect(get.mock.calls.map(c => c[0])).toEqual(['/api/exportreceipts']);
    for (const label of ['Duyệt và giữ hàng', 'Duyệt và xuất ngay', 'Hủy', 'Tạo phiếu xuất']) expect(view.queryByText(label)).toBeNull();
    expect(document.title).toBe('Phiếu xuất kho — ERP KHO'); expect(view.getByText('Bản nháp')).toBeTruthy();
  });
  it('approve alone exposes reserve but not immediate dispatch or cancellation', async () => {
    localStorage.setItem('permissions', JSON.stringify([...read, 'export_receipt.approve']));
    const view = render(<ExportReceipts />); await view.findByText('X3');
    expect(view.getByText('Duyệt và giữ hàng')).toBeTruthy(); expect(view.queryByText('Duyệt và xuất ngay')).toBeNull(); expect(view.queryByText('Hủy')).toBeNull();
  });
  it('dispatcher differs from checker even with all grants', async () => {
    localStorage.setItem('permissions', JSON.stringify([...read, 'export_receipt.dispatch']));
    get.mockResolvedValue({ data: [{ ...receipt, status: 'Approved', approvedBy: 1 }] } as never);
    const view = render(<ExportReceipts />); await view.findByText('X3');
    expect(view.getByText('Đã duyệt và giữ hàng')).toBeTruthy(); expect(view.queryByText('Xác nhận xuất kho')).toBeNull();
  });
  it('double-click sends one command with the captured aggregate token', async () => {
    localStorage.setItem('permissions', JSON.stringify([...read, 'export_receipt.approve']));
    let release!: (value: never) => void; post.mockReturnValue(new Promise(resolve => { release = resolve; }));
    const view = render(<ExportReceipts />); await view.findByText('X3'); const button = view.getByText('Duyệt và giữ hàng');
    fireEvent.click(button); fireEvent.click(button); expect(post).toHaveBeenCalledTimes(1);
    expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(post.mock.calls[0][1]).toEqual({ rowVersion: receipt.rowVersion });
    await act(async () => release({} as never));
  });
  for (const kind of ['list', 'detail', 'print'] as const) it(`mounted revoke/regrant rejects old ${kind} response`, async () => {
    let release!: (value: never) => void;
    const old = new Promise(resolve => { release = resolve; });
    if (kind === 'list') get.mockImplementationOnce(() => old as never);
    const view = render(<ExportReceipts />);
    const heading=view.getByRole('heading',{name:'Quản Lý Phiếu Xuất Kho'});
    if (kind !== 'list') {
      await view.findByText('X3'); get.mockImplementationOnce(() => old as never);
      fireEvent.click(view.getByText(kind === 'detail' ? 'Chi tiết' : 'Xem bản in'));
    }
    await act(async () => setCurrentPermissions([]));
    expect(heading.isConnected).toBe(true);
    expect(view.getByRole('alert').textContent).toContain('Bạn không có quyền');
    get.mockResolvedValue({ data: [] } as never);
    await act(async () => setCurrentPermissions(read));
    await act(async () => release({ data: kind === 'list' ? [{ ...receipt, code: 'OLD_PRIVATE' }] : { ...receipt, code: 'OLD_PRIVATE' } } as never));
    expect(heading.isConnected).toBe(true);
    expect(view.queryByText(/OLD_PRIVATE/)).toBeNull(); expect(view.queryByRole('dialog')).toBeNull();
  });
  it('stale failure is presented safely and never auto-replayed', async () => {
    localStorage.setItem('permissions', JSON.stringify([...read, 'export_receipt.approve']));
    post.mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { message: 'SQL provider private' } } });
    const view = render(<ExportReceipts />); await view.findByText('X3'); fireEvent.click(view.getByText('Duyệt và giữ hàng'));
    await waitFor(() => expect(view.getByText('Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.')).toBeTruthy());
    expect(post).toHaveBeenCalledTimes(1); expect(view.queryByText(/SQL provider/)).toBeNull();
  });
});
