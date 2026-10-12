// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import ReceivingReasonCodes from './ReceivingReasonCodes';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn() } }));
const get = vi.mocked(apiClient.get);
const item = { id: 91, code: 'UNDER_RECEIPT', name: 'Nhận thiếu', category: 'Quantity', version: 2, isActive: false,
  effectiveFromUtc: '2026-10-11T08:00:00+00:00', effectiveToUtc: null, effectivity: 1, requiresNote: true, requiresAttachment: false, requiresApproval: true };
const page = { items: [item], totalRecords: 21, totalPages: 2 };
const read = ['reason_code.read'];
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin'); localStorage.setItem('permissions', JSON.stringify(read));
  get.mockImplementation(async url => ({ data: url === '/api/receiving-reason-codes' ? page : String(url).includes('/versions/') ? [item, { ...item, id: 90, version: 1 }] : item }) as never);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('tra cứu lý do nhận hàng chỉ đọc', () => {
  it('does not trust an Admin role without the grant', () => {
    localStorage.setItem('permissions', '[]'); const view = render(<ReceivingReasonCodes />);
    expect(view.getByRole('alert').textContent).toBe('Bạn không có quyền thực hiện thao tác này.'); expect(get).not.toHaveBeenCalled();
  });

  it('shows independent activity/effectivity and a named scroll region', async () => {
    const view = render(<ReceivingReasonCodes />); await view.findByText('Nhận thiếu');
    expect(view.getByText('Ngừng hoạt động', { selector: 'td' })).toBeTruthy(); expect(view.getByText('Đang hiệu lực', { selector: 'td' })).toBeTruthy();
    expect(view.getByText(/08:00:00.*11\/10\/2026.*UTC/)).toBeTruthy(); expect(view.getByText('Không giới hạn')).toBeTruthy();
    expect(view.getByRole('region', { name: 'Danh mục lý do nhận hàng' }).tabIndex).toBe(0);
    expect(view.queryByRole('button', { name: /Thêm|Sửa|Xóa|Phát hành/ })).toBeNull();
  });

  it('searches, filters, and pages through the server query', async () => {
    const view = render(<ReceivingReasonCodes />); await view.findByText('Nhận thiếu');
    fireEvent.change(view.getByLabelText('Mã hoặc tên lý do'), { target: { value: ' nhận ' } }); fireEvent.click(view.getByRole('button', { name: 'Tìm kiếm' }));
    await waitFor(() => expect(get).toHaveBeenLastCalledWith('/api/receiving-reason-codes', expect.objectContaining({ params: expect.objectContaining({ keyword: 'nhận', page: 1 }) })));
    fireEvent.change(view.getByLabelText('Trạng thái sử dụng'), { target: { value: 'false' } });
    await waitFor(() => expect(get).toHaveBeenLastCalledWith('/api/receiving-reason-codes', expect.objectContaining({ params: expect.objectContaining({ active: 'false' }) })));
    fireEvent.change(view.getByLabelText('Thời gian hiệu lực'), { target: { value: '2' } });
    await view.findByText('Nhận thiếu'); fireEvent.click(view.getByRole('button', { name: 'Trang sau' }));
    await waitFor(() => expect(get).toHaveBeenLastCalledWith('/api/receiving-reason-codes', expect.objectContaining({ params: expect.objectContaining({ page: 2, effectivity: '2' }) })));
  });

  it('shows a loading state and safe error, then can reload an empty result', async () => {
    get.mockRejectedValueOnce({ response: { status: 403, data: { message: 'SQL_PROVIDER_SECRET' } } });
    const view = render(<ReceivingReasonCodes />); expect(view.getByRole('status').textContent).toContain('Đang tải');
    await view.findByRole('alert'); expect(view.container.textContent).not.toContain('SQL_PROVIDER_SECRET');
    get.mockResolvedValueOnce({ data: { items: [], totalRecords: 0, totalPages: 0 } } as never); fireEvent.click(view.getByRole('button', { name: 'Tải lại' }));
    await view.findByText('Không có lý do nhận hàng phù hợp.');
  });

  it('loads real detail/history separately and restores focus on Escape', async () => {
    const view = render(<ReceivingReasonCodes />); const trigger = await view.findByRole('button', { name: 'Chi tiết UNDER_RECEIPT phiên bản 2' });
    trigger.focus(); fireEvent.click(trigger); const dialog = await view.findByRole('dialog');
    await within(dialog).findByRole('heading', { name: /UNDER_RECEIPT.*Nhận thiếu/ });
    expect(get).toHaveBeenCalledWith('/api/receiving-reason-codes/91'); expect(get).toHaveBeenCalledWith('/api/receiving-reason-codes/versions/UNDER_RECEIPT');
    expect(within(dialog).getByText('Yêu cầu ghi chú')).toBeTruthy(); expect(within(dialog).getByText('Số lượng')).toBeTruthy();
    const close = within(dialog).getByRole('button', { name: 'Đóng' }); close.focus(); fireEvent.keyDown(close, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(within(dialog).getByRole('region', { name: 'Lịch sử phiên bản danh mục gốc' }));
    fireEvent.keyDown(document.activeElement!, { key: 'Tab' }); expect(document.activeElement).toBe(close);
    fireEvent.keyDown(close, { key: 'Escape' }); expect(view.queryByRole('dialog')).toBeNull(); expect(document.activeElement).toBe(trigger);
  });

  it.each(['list', 'detail', 'history'])('late %s response cannot repopulate a mounted page after revoke/regrant', async kind => {
    let release!: (value: unknown) => void; const late = new Promise(resolve => { release = resolve; });
    if (kind === 'list') get.mockReturnValueOnce(late as never);
    const view = render(<ReceivingReasonCodes />); const heading = view.getByRole('heading', { name: 'Lý do nhận hàng' });
    if (kind !== 'list') {
      const trigger = await view.findByRole('button', { name: 'Chi tiết UNDER_RECEIPT phiên bản 2' });
      get.mockImplementation(url => String(url) === (kind === 'detail' ? '/api/receiving-reason-codes/91' : '/api/receiving-reason-codes/versions/UNDER_RECEIPT') ? late as never : Promise.resolve({ data: String(url).includes('/versions/') ? [item] : item }) as never);
      fireEvent.click(trigger);
    }
    await act(async () => setCurrentPermissions([])); expect(view.queryByText('Nhận thiếu')).toBeNull();
    get.mockImplementation(async () => ({ data: page }) as never);
    await act(async () => setCurrentPermissions(read)); await view.findByText('Nhận thiếu'); expect(heading.isConnected).toBe(true);
    await act(async () => release({ data: kind === 'list' ? { ...page, items: [{ ...item, name: 'OLD_SECRET' }] } : kind === 'detail' ? { ...item, name: 'OLD_SECRET' } : [{ ...item, name: 'OLD_SECRET' }] }));
    expect(view.queryByText('OLD_SECRET')).toBeNull(); expect(view.queryByRole('dialog')).toBeNull();
  });
});
