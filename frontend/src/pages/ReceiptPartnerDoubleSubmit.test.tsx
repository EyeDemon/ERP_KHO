// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import ImportReceipts from './ImportReceipts';
import ExportReceipts from './ExportReceipts';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const get = vi.mocked(apiClient.get); const post = vi.mocked(apiClient.post); const put = vi.mocked(apiClient.put);
const partner = { id: 9, code: 'BP-9', name: 'Partner', isActive: true };
const warehouse = { id: 1, code: 'WH1', name: 'Warehouse', isActive: true };
const product = { id: 2, code: 'P2', name: 'Product', isActive: true, unitId: 1, unitCode: 'EA', unitName: 'Each', unitDecimalPlaces: 4, uoms: [{ unitId: 1, unitCode: 'EA', unitName: 'Each', decimalPlaces: 4, conversionFactor: 1, version: 1 }] };
const pending = () => { let finish!: () => void; const promise = new Promise(resolve => { finish = () => resolve({ data: {} }); }); return { promise, finish }; };

describe('receipt mutation locks (mocked API)', () => {
  afterEach(cleanup);
  beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin'); localStorage.setItem('userId', '1'); });

  it('locks import create synchronously while the first request is pending', async () => {
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : [] }) as never);
    const request = pending(); post.mockReturnValue(request.promise as never); const view = render(<ImportReceipts />);
    await view.findByText('Chưa có phiếu nhập'); fireEvent.change(view.getAllByRole('textbox')[0], { target: { value: 'IMP-LOCK' } }); fireEvent.change(view.getAllByRole('combobox')[0], { target: { value: '1' } }); fireEvent.click(view.getByText('+ Thêm dòng'));
    fireEvent.change(view.getAllByRole('combobox')[2], { target: { value: '2' } }); fireEvent.change(view.getAllByRole('spinbutton')[0], { target: { value: '1' } }); fireEvent.change(view.getAllByRole('spinbutton')[1], { target: { value: '1' } });
    const save = view.getByText('Lưu Phiếu Nháp'); fireEvent.click(save); fireEvent.click(save); expect(post).toHaveBeenCalledTimes(1); expect((view.getByText('Đang lưu...') as HTMLButtonElement).disabled).toBe(true); request.finish(); await waitFor(() => expect(view.queryByText('Đang lưu...')).toBeNull());
  });

  it('locks inbound receive while the command is pending', async () => {
    const receipt = { id: 3, code: 'I3', status: 'Draft', createdBy: 2, details: [] };
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : [receipt] }) as never);
    const request = pending(); post.mockReturnValue(request.promise as never); const view = render(<ImportReceipts />); await view.findByText('I3');
    const receive = view.getByText('Hoàn tất nhận hàng'); fireEvent.click(receive); fireEvent.click(receive);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1)); expect(post).toHaveBeenCalledWith('/api/importreceipts/3/receive', expect.objectContaining({ lines: expect.any(Array) }), expect.objectContaining({ headers: expect.any(Object) }));
    expect((view.getByText('Đang xử lý...') as HTMLButtonElement).disabled).toBe(true); request.finish();
  });

  it('locks inbound post synchronously while the first request is pending', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const receipt = { id: 5, code: 'I5', status: 'ReadyToPost', createdBy: 2, details: [] };
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : [receipt] }) as never);
    const request = pending(); post.mockReturnValue(request.promise as never); const view = render(<ImportReceipts />); await view.findByText('I5');
    const postButton = view.getByText('Post ghi tồn'); fireEvent.click(postButton); fireEvent.click(postButton);
    expect(post).toHaveBeenCalledTimes(1); expect(post).toHaveBeenCalledWith('/api/importreceipts/5/post', undefined, expect.objectContaining({ headers: expect.any(Object) }));
    expect((view.getByText('Đang post...') as HTMLButtonElement).disabled).toBe(true); request.finish();
  });

  it('clears a selected receipt after a post conflict', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const receipt = { id: 6, code: 'I6', status: 'ReadyToPost', createdBy: 2, details: [] };
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : url === '/api/importreceipts/6' ? receipt : [receipt] }) as never);
    post.mockRejectedValue({ response: { status: 409, data: { message: 'conflict' } } });
    const view = render(<ImportReceipts />); await view.findByText('I6'); fireEvent.click(view.getByText('Chi tiết')); await view.findByText(/Chi Tiết Phiếu Nhập:/);
    fireEvent.click(view.getAllByText('Post ghi tồn')[0]);
    await view.findByText('Dữ liệu đã thay đổi. Vui lòng tải lại phiếu.');
    expect(view.queryByText(/Chi Tiết Phiếu Nhập:/)).toBeNull();
  });

  it('locks export create synchronously while the first request is pending', async () => {
    get.mockImplementation(async (url) => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : url.includes('inventorystocks') ? [{ availableQuantity: 10 }] : [] }) as never);
    const request = pending(); post.mockReturnValue(request.promise as never); const view = render(<ExportReceipts />);
    await view.findByText('Chưa có phiếu xuất nào'); fireEvent.change(view.getAllByRole('textbox')[0], { target: { value: 'EXP-LOCK' } }); fireEvent.change(view.getAllByRole('combobox')[0], { target: { value: '1' } }); fireEvent.click(view.getByText('+ Thêm dòng'));
    fireEvent.change(view.getAllByRole('combobox')[2], { target: { value: '2' } }); fireEvent.change(view.getAllByRole('spinbutton')[0], { target: { value: '1' } }); fireEvent.change(view.getAllByRole('spinbutton')[1], { target: { value: '1' } }); await waitFor(() => expect((view.getByText('Tạo Phiếu Xuất') as HTMLButtonElement).disabled).toBe(false));
    const save = view.getByText('Tạo Phiếu Xuất'); fireEvent.click(save); fireEvent.click(save); expect(post).toHaveBeenCalledTimes(1); expect((view.getByText('Đang lưu...') as HTMLButtonElement).disabled).toBe(true); request.finish(); await waitFor(() => expect(view.queryByText('Đang lưu...')).toBeNull());
  });

  it('locks supplier assignment while its request is pending', async () => {
    const receipt = { id: 3, code: 'I3', status: 'Draft', supplierId: null, details: [] };
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : url === '/api/importreceipts/3' ? receipt : [receipt] }) as never);
    const request = pending(); put.mockReturnValue(request.promise as never); const view = render(<ImportReceipts />); await view.findByText('I3'); fireEvent.click(view.getByText('Chi tiết')); const select = await view.findByLabelText('Đổi nhà cung cấp'); fireEvent.change(select, { target: { value: '9' } }); fireEvent.change(select, { target: { value: '' } }); expect(put).toHaveBeenCalledTimes(1); expect((select as HTMLSelectElement).disabled).toBe(true); request.finish(); await waitFor(() => expect((view.getByLabelText('Đổi nhà cung cấp') as HTMLSelectElement).disabled).toBe(false));
  });

  it('locks customer assignment while its request is pending', async () => {
    const receipt = { id: 4, code: 'E4', status: 'Draft', customerId: null, details: [], writeEnabled: true };
    get.mockImplementation(async url => ({ data: url === '/api/warehouses' ? [warehouse] : url === '/api/products' ? [product] : url === '/api/business-partners' ? { items: [partner] } : url === '/api/exportreceipts/4' ? receipt : [] }) as never);
    get.mockImplementationOnce(async () => ({ data: [warehouse] }) as never).mockImplementationOnce(async () => ({ data: [product] }) as never).mockImplementationOnce(async () => ({ data: { items: [partner] } }) as never).mockImplementationOnce(async () => ({ data: [receipt] }) as never).mockImplementation(async url => ({ data: url === '/api/exportreceipts/4' ? receipt : [receipt] }) as never);
    const request = pending(); put.mockReturnValue(request.promise as never); const view = render(<ExportReceipts />); await view.findByText('E4'); fireEvent.click(view.getByText('Chi tiết')); const select = await view.findByLabelText('Đổi khách hàng'); fireEvent.change(select, { target: { value: '9' } }); fireEvent.change(select, { target: { value: '' } }); expect(put).toHaveBeenCalledTimes(1); expect((select as HTMLSelectElement).disabled).toBe(true); request.finish(); await waitFor(() => expect((view.getByLabelText('Đổi khách hàng') as HTMLSelectElement).disabled).toBe(false));
  });
});
