// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import ImportReceipts from './ImportReceipts';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
const get = vi.mocked(apiClient.get);
const receipt = { id: 8, code: 'QA_RECEIPT', status: 'ReadyToPost', createdBy: 2, details: [] };
describe('quyền độc lập trên phiếu nhập', () => {
  beforeEach(() => {
    vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('userId', '1');
    setCurrentPermissions(['receipt.read']);
    get.mockImplementation(async url => ({ data: url === '/api/importreceipts' ? [receipt] : receipt }) as never);
  });
  afterEach(cleanup);

  it('receipt-only reader xem chi tiết mà không gọi dependency không có quyền', async () => {
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    expect(await view.findByText(/Chi Tiết Phiếu Nhập:/)).toBeTruthy();
    expect(get.mock.calls.every(([url]) => url.startsWith('/api/importreceipts') && !url.includes('discrepanc'))).toBe(true);
    expect(view.queryByText('Lưu Phiếu Nháp')).toBeNull();
    expect(view.queryByText('Ghi nhận tồn kho')).toBeNull();
  });

  it('optional dependency failure không xóa core receipt', async () => {
    setCurrentPermissions(['receipt.read', 'receiving_discrepancy.read', 'reason_code.read']);
    get.mockImplementation(async url => {
      if (url.includes('discrepanc')) throw { response: { status: 403 } };
      return { data: url === '/api/importreceipts' ? [receipt] : receipt } as never;
    });
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    expect(await view.findByText(/Chi Tiết Phiếu Nhập:/)).toBeTruthy();
  });

  it('receipt.complete không cấp quyền ghi tồn và receipt.post không cấp quyền tạo phiếu', async () => {
    setCurrentPermissions(['receipt.read', 'receipt.complete']);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    expect(view.queryByText('Ghi nhận tồn kho')).toBeNull();
    act(() => setCurrentPermissions(['receipt.read', 'receipt.post']));
    expect(await view.findByText('Ghi nhận tồn kho')).toBeTruthy();
    expect(view.queryByText('Lưu Phiếu Nháp')).toBeNull();
  });

  it('response chi tiết đến sau revoke không khôi phục dữ liệu đã xóa', async () => {
    let finish!: (value: unknown) => void;
    get.mockImplementation(url => url === '/api/importreceipts' ? Promise.resolve({ data: [receipt] }) as never
      : new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<ImportReceipts />); await view.findByText('QA_RECEIPT');
    fireEvent.click(view.getByText('Chi tiết'));
    act(() => setCurrentPermissions([]));
    await act(async () => finish({ data: receipt }));
    await waitFor(() => expect(view.queryByText('QA_RECEIPT')).toBeNull());
    expect(view.queryByText(/Chi Tiết Phiếu Nhập:/)).toBeNull();
  });
});
