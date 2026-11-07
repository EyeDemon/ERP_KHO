// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const transfer = {
  id: 807, code: 'TRF-807', sourceWarehouseId: 1, sourceWarehouseName: 'Kho nguồn',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho đích',
  status: 'InTransit', draftRevision: 1, createdBy: 1, createdAt: '2026-10-08T09:00:00Z',
  details: [{
    productId: 9, productCode: 'SKU-9', productName: 'Sản phẩm',
    requestedQuantity: 5, dispatchedQuantity: 5, receivedQuantity: 0,
    missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 5,
  }],
};

describe('Stock transfer command consistency', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'WarehouseStaff');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return { data: { items: [transfer], totalPages: 1 } } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: transfer } as never;
      if (url === '/api/v1/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/v1/products') return {
        data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }],
      } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('edits an ordinary draft with real PUT, prefilled lines and payload-scoped idempotency', async () => {
    const draft = {
      ...transfer, status: 'Draft', note: 'Phiếu ban đầu',
      details: [{ ...transfer.details[0], requestedQuantity: 5, note: 'Dòng ban đầu' }],
    };
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/v1/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/v1/products') return {
        data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }],
      } as never;
      return { data: [] } as never;
    });
    vi.mocked(apiClient.put).mockResolvedValue({ data: {} } as never);
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));

    const dialog = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    expect((within(dialog).getByLabelText('Kho nguồn') as HTMLSelectElement).value).toBe('1');
    expect((within(dialog).getByLabelText('Kho đích') as HTMLSelectElement).value).toBe('2');
    expect((within(dialog).getByLabelText('Ghi chú điều chuyển') as HTMLTextAreaElement).value)
      .toBe('Phiếu ban đầu');
    expect((within(dialog).getByLabelText('Số lượng dòng 1') as HTMLInputElement).value)
      .toBe('5');
    expect((within(dialog).getByLabelText('Ghi chú dòng 1') as HTMLInputElement).value)
      .toBe('Dòng ban đầu');

    fireEvent.change(within(dialog).getByLabelText('Số lượng dòng 1'), {
      target: { value: '6' },
    });
    fireEvent.change(within(dialog).getByLabelText('Ghi chú dòng 1'), {
      target: { value: 'Đã chỉnh sửa ghi chú dòng' },
    });
    fireEvent.submit(dialog);

    await waitFor(() => expect(apiClient.put).toHaveBeenCalledWith(
      '/api/v1/stock-transfers/807',
      {
        sourceWarehouseId: 1, destinationWarehouseId: 2, note: 'Phiếu ban đầu',
        expectedDraftRevision: 1,
        details: [{ productId: 9, quantity: 6, note: 'Đã chỉnh sửa ghi chú dòng' }],
      },
      expect.objectContaining({ headers: expect.objectContaining({
        'Idempotency-Key': expect.any(String),
      }) }),
    ));
    expect(post).not.toHaveBeenCalled();
    await waitFor(() => expect(view.queryByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' }))
      .toBeNull());
    expect(await view.findByRole('dialog', { name: 'TRF-807' })).toBeTruthy();
  // Windows CI executes all UI tests under load; preserve every assertion but
  // allow this multi-step dialog/PUT/refresh integration test adequate time.
  }, 15000);

  it('keeps draft editor open after a network failure and reuses key only for identical retry', async () => {
    const draft = { ...transfer, status: 'Draft' };
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/v1/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/v1/products') return {
        data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }],
      } as never;
      return { data: [] } as never;
    });
    const put = vi.mocked(apiClient.put);
    put.mockRejectedValueOnce(new Error('offline'))
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ data: {} } as never);
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const dialog = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.submit(dialog);
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    await within(dialog).findByRole('alert');
    expect(within(dialog).getByRole('alert').textContent).toContain('Không thể cập nhật phiếu');
    const first = (put.mock.calls[0][2] as { headers: Record<string,string> }).headers['Idempotency-Key'];
    fireEvent.submit(dialog);
    await waitFor(() => expect(put).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(
      (within(dialog).getByRole('button', { name: 'Lưu thay đổi' }) as HTMLButtonElement).disabled
    ).toBe(false));
    const retry = (put.mock.calls[1][2] as { headers: Record<string,string> }).headers['Idempotency-Key'];
    expect(retry).toBe(first);
    fireEvent.change(within(dialog).getByLabelText('Ghi chú điều chuyển'), {
      target: { value: 'Thay đổi thông tin trước khi thử lại' },
    });
    fireEvent.submit(dialog);
    await waitFor(() => expect(put).toHaveBeenCalledTimes(3));
    const modified = (put.mock.calls[2][2] as { headers: Record<string,string> }).headers['Idempotency-Key'];
    expect(modified).not.toBe(first);
  });

  it('recovers from a 409 by loading the approved version without overwriting it', async () => {
    const draft = { ...transfer, status: 'Draft' };
    let latest = draft;
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return {
        data: { items: [latest], totalPages: 1 },
      } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: latest } as never;
      if (url === '/api/v1/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/v1/products') return { data: [
        { id: 9, code: 'SKU-9', name: 'Sản phẩm' },
      ] } as never;
      return { data: [] } as never;
    });
    vi.mocked(apiClient.put).mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Phiếu đã được duyệt.' } },
    });

    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const editor = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.submit(editor);
    expect(await within(editor).findByRole('alert')).toHaveProperty('textContent',
      'Phiếu đã được duyệt.');
    expect((within(editor).getByRole('button', { name: 'Lưu thay đổi' }) as HTMLButtonElement)
      .disabled).toBe(true);
    latest = { ...draft, status: 'Approved' };
    fireEvent.click(within(editor).getByRole('button', { name: 'Tải lại phiên bản mới' }));

    const refreshed = await view.findByRole('dialog', { name: 'TRF-807' });
    expect(within(refreshed).getAllByText('Đã duyệt').length).toBeGreaterThan(0);
    expect(view.queryByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
    expect(apiClient.put).toHaveBeenCalledTimes(1);
  });

  it('repopulates draft lines from the server after a conflicting update', async () => {
    const draft = {
      ...transfer, status: 'Draft', note: 'Phiếu ban đầu',
      details: [{ ...transfer.details[0], note: 'Ghi chú ban đầu' }],
    };
    let latest = draft;
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return {
        data: { items: [latest], totalPages: 1 },
      } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: latest } as never;
      if (url === '/api/v1/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/v1/products') return { data: [
        { id: 9, code: 'SKU-9', name: 'Sản phẩm' },
      ] } as never;
      return { data: [] } as never;
    });
    vi.mocked(apiClient.put).mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Phiếu vừa được cập nhật.' } },
    });

    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const editor = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.submit(editor);
    await within(editor).findByRole('alert');
    latest = {
      ...draft, note: 'Phiên bản đã cập nhật',
      details: [{ ...draft.details[0], requestedQuantity: 4, note: 'Kiểm tra lại' }],
    };
    fireEvent.click(within(editor).getByRole('button', { name: 'Tải lại phiên bản mới' }));

    await waitFor(() => expect(
      (within(editor).getByLabelText('Số lượng dòng 1') as HTMLInputElement).value,
    ).toBe('4'));
    expect((within(editor).getByLabelText('Ghi chú điều chuyển') as HTMLTextAreaElement).value)
      .toBe('Phiên bản đã cập nhật');
    expect((within(editor).getByLabelText('Ghi chú dòng 1') as HTMLInputElement).value)
      .toBe('Kiểm tra lại');
    expect(within(editor).queryByRole('button', { name: 'Tải lại phiên bản mới' }))
      .toBeNull();
    expect((within(editor).getByRole('button', { name: 'Lưu thay đổi' }) as HTMLButtonElement)
      .disabled).toBe(false);
    expect(apiClient.put).toHaveBeenCalledTimes(1);
  });

  it('rotates a rejected edit key only after refreshing the conflicted document', async () => {
    const draft = { ...transfer, status: 'Draft' };
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return {
        data: { items: [draft], totalPages: 1 },
      } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/v1/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/v1/products') return { data: [
        { id: 9, code: 'SKU-9', name: 'Sản phẩm' },
      ] } as never;
      return { data: [] } as never;
    });
    const put = vi.mocked(apiClient.put);
    put.mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Dữ liệu đã thay đổi.' } },
    }).mockResolvedValueOnce({ data: {} } as never);

    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const editor = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.submit(editor);
    await within(editor).findByRole('alert');
    const rejectedKey = (put.mock.calls[0][2] as { headers: Record<string, string> })
      .headers['Idempotency-Key'];
    expect((within(editor).getByRole('button', { name: 'Lưu thay đổi' }) as HTMLButtonElement)
      .disabled).toBe(true);
    fireEvent.submit(editor);
    expect(put).toHaveBeenCalledTimes(1);

    fireEvent.click(within(editor).getByRole('button', { name: 'Tải lại phiên bản mới' }));
    await waitFor(() => expect(
      (within(editor).getByRole('button', { name: 'Lưu thay đổi' }) as HTMLButtonElement).disabled
    ).toBe(false));
    fireEvent.submit(editor);
    await waitFor(() => expect(put).toHaveBeenCalledTimes(2));
    const refreshedKey = (put.mock.calls[1][2] as { headers: Record<string, string> })
      .headers['Idempotency-Key'];
    expect(refreshedKey).not.toBe(rejectedKey);
  });

  it('closes draft editing with Escape and returns keyboard focus to the new-document action', async () => {
    const draft = { ...transfer, status: 'Draft' };
    get.mockImplementation(async url => {
      if (url === '/api/v1/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/v1/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/v1/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/v1/products') return { data: [
        { id: 9, code: 'SKU-9', name: 'Sản phẩm' },
      ] } as never;
      return { data: [] } as never;
    });
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const dialog = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.keyDown(dialog, { key: 'Escape' });
    expect(view.queryByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
    expect(document.activeElement).toBe(view.getByRole('button', { name: 'Tạo phiếu' }));
  });

});
