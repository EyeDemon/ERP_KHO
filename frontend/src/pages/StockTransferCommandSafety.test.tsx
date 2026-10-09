// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
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
  status: 'InTransit', createdBy: 1, createdAt: '2026-10-08T09:00:00Z',
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
      if (url === '/api/stock-transfers') return { data: { items: [transfer], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/807') return { data: transfer } as never;
      if (url === '/api/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/products') return {
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
      if (url === '/api/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/products') return {
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
      '/api/stock-transfers/807',
      {
        sourceWarehouseId: 1, destinationWarehouseId: 2, note: 'Phiếu ban đầu',
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
  });

  it('keeps draft editor open after conflicting approval and reuses key only for identical retry', async () => {
    const draft = { ...transfer, status: 'Draft' };
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/warehouses') return {
        data: [{ id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' }],
      } as never;
      if (url === '/api/products') return {
        data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }],
      } as never;
      return { data: [] } as never;
    });
    const put = vi.mocked(apiClient.put);
    put.mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Phiếu đã được duyệt ở phiên khác.' } },
    }).mockRejectedValueOnce({
      response: { status: 409, data: { message: 'Phiếu đã được duyệt ở phiên khác.' } },
    }).mockResolvedValueOnce({ data: {} } as never);
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
    const dialog = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
    fireEvent.submit(dialog);
    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    await within(dialog).findByRole('alert');
    expect(within(dialog).getByRole('alert').textContent).toContain('Phiếu đã được duyệt');
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
      if (url === '/api/stock-transfers') return {
        data: { items: [latest], totalPages: 1 },
      } as never;
      if (url === '/api/stock-transfers/807') return { data: latest } as never;
      if (url === '/api/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/products') return { data: [
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
    latest = { ...draft, status: 'Approved' };
    fireEvent.click(within(editor).getByRole('button', { name: 'Tải lại phiên bản mới' }));

    const refreshed = await view.findByRole('dialog', { name: 'TRF-807' });
    expect(within(refreshed).getByText('Đã duyệt')).toBeTruthy();
    expect(view.queryByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
    expect(apiClient.put).toHaveBeenCalledTimes(1);
  });

  it('repopulates draft lines from the server after a conflicting update', async () => {
    const draft = { ...transfer, status: 'Draft' };
    let latest = draft;
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return {
        data: { items: [latest], totalPages: 1 },
      } as never;
      if (url === '/api/stock-transfers/807') return { data: latest } as never;
      if (url === '/api/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/products') return { data: [
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
    expect(apiClient.put).toHaveBeenCalledTimes(1);
  });

  it('closes draft editing with Escape and returns keyboard focus to the new-document action', async () => {
    const draft = { ...transfer, status: 'Draft' };
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return { data: { items: [draft], totalPages: 1 } } as never;
      if (url === '/api/stock-transfers/807') return { data: draft } as never;
      if (url === '/api/warehouses') return { data: [
        { id: 1, name: 'Kho nguồn' }, { id: 2, name: 'Kho đích' },
      ] } as never;
      if (url === '/api/products') return { data: [
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

  it('does not expose editing on an approved or reverse-document transfer', async () => {
    const approved = { ...transfer, status: 'Approved' };
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return {
        data: { items: [approved], totalPages: 1 },
      } as never;
      if (url === '/api/stock-transfers/807') return { data: approved } as never;
      if (url === '/api/warehouses' || url === '/api/products') return { data: [] } as never;
      return { data: [] } as never;
    });
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    expect(view.queryByRole('button', { name: 'Chỉnh sửa phiếu nháp' })).toBeNull();
  });

  it('retries an unchanged receive payload with the same key but uses a new key after editing quantities', async () => {
    post.mockRejectedValueOnce(new Error('offline')).mockRejectedValueOnce(new Error('offline'));
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    const receiveButton = await view.findByRole('button', { name: 'Xác nhận nhận' });

    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    await view.findByRole('alert');
    const firstKey = (post.mock.calls[0][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];

    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
    const retryKey = (post.mock.calls[1][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(retryKey).toBe(firstKey);

    fireEvent.change(view.getByLabelText('Thực nhận SKU-9'), { target: { value: '3' } });
    fireEvent.change(view.getByLabelText('Thiếu SKU-9'), { target: { value: '2' } });
    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(3));
    const editedKey = (post.mock.calls[2][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(editedKey).not.toBe(firstKey);
    expect(post.mock.calls[2][1]).toEqual({
      details: [{ productId: 9, receivedQuantity: 3, missingQuantity: 2, damagedQuantity: 0 }],
    });
  });

  it('explains receive reconciliation in Vietnamese before the operator posts', async () => {
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    expect(await view.findByText(/thực nhận \+ thiếu \+ hỏng phải đúng bằng số đã xuất/)).toBeTruthy();
  });

  it('rejects a short receive without posting a command, then permits a fully reconciled receipt', async () => {
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    const receiveButton = await view.findByRole('button', { name: 'Xác nhận nhận' });
    fireEvent.change(view.getByLabelText('Thực nhận SKU-9'), { target: { value: '3' } });
    fireEvent.click(receiveButton);
    expect(await within(view.getByRole('dialog', { name: 'TRF-807' })).findByRole('alert'))
      .toHaveProperty('textContent',
      'Sản phẩm SKU-9: tổng thực nhận, thiếu và hỏng phải bằng số lượng đã xuất (5).');
    expect(post).not.toHaveBeenCalled();
    fireEvent.change(view.getByLabelText('Thiếu SKU-9'), { target: { value: '1' } });
    fireEvent.change(view.getByLabelText('Hỏng SKU-9'), { target: { value: '1' } });
    fireEvent.click(receiveButton);
    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/stock-transfers/807/receive', {
      details: [{ productId: 9, receivedQuantity: 3, missingQuantity: 1, damagedQuantity: 1 }],
    }, expect.anything()));
  });

  it('rejects a negative or over-precision receive amount before sending to SQL', async () => {
    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    const receiveButton = await view.findByRole('button', { name: 'Xác nhận nhận' });
    fireEvent.change(view.getByLabelText('Thực nhận SKU-9'), { target: { value: '-1' } });
    fireEvent.click(receiveButton);
    expect(await view.findByRole('alert')).toHaveProperty('textContent',
      'Sản phẩm SKU-9: số lượng thực nhận, thiếu và hỏng phải không âm, tối đa 4 chữ số thập phân.');
    fireEvent.change(view.getByLabelText('Thực nhận SKU-9'), { target: { value: '4.00001' } });
    fireEvent.change(view.getByLabelText('Thiếu SKU-9'), { target: { value: '0.99999' } });
    fireEvent.click(receiveButton);
    expect(post).not.toHaveBeenCalled();
  });

  it('rejects programmatic create without both authorized warehouse choices', async () => {
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });

    fireEvent.change(within(dialog).getByLabelText('Kho đích'), { target: { value: '2' } });
    fireEvent.change(within(dialog).getByLabelText('Sản phẩm dòng 1'), { target: { value: '9' } });
    fireEvent.change(within(dialog).getByLabelText('Số lượng dòng 1'), { target: { value: '5' } });
    fireEvent.submit(dialog);

    expect(await within(dialog).findByRole('alert')).toHaveProperty('textContent',
      'Phải chọn kho nguồn và kho đích hợp lệ.');
    expect(post).not.toHaveBeenCalled();
  });

  it('clears an outdated destination when source changes', async () => {
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });
    const source = within(dialog).getByLabelText('Kho nguồn') as HTMLSelectElement;
    const destination = within(dialog).getByLabelText('Kho đích') as HTMLSelectElement;

    fireEvent.change(source, { target: { value: '1' } });
    fireEvent.change(destination, { target: { value: '2' } });
    expect(destination.value).toBe('2');
    fireEvent.change(source, { target: { value: '2' } });
    expect(destination.value).toBe('');
    expect(destination.querySelector('option[value="2"]')).toBeNull();
  });

  it('rejects a create with no detail lines before sending an API command', async () => {
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });
    fireEvent.change(within(dialog).getByLabelText('Kho nguồn'), { target: { value: '1' } });
    fireEvent.change(within(dialog).getByLabelText('Kho đích'), { target: { value: '2' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Xóa dòng 1' }));
    fireEvent.submit(dialog);
    expect(await view.findByRole('alert')).toHaveProperty('textContent',
      'Mỗi dòng phải có sản phẩm và số lượng lớn hơn 0.');
    expect(post).not.toHaveBeenCalled();
  });

  it('submits create only once while the first request is unresolved', async () => {
    let resolveCreate!: (response: unknown) => void;
    post.mockReturnValue(new Promise(resolve => { resolveCreate = resolve; }) as never);
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });

    fireEvent.change(within(dialog).getByLabelText('Kho nguồn'), { target: { value: '1' } });
    fireEvent.change(within(dialog).getByLabelText('Kho đích'), { target: { value: '2' } });
    fireEvent.change(within(dialog).getByLabelText('Sản phẩm dòng 1'), { target: { value: '9' } });
    fireEvent.change(within(dialog).getByLabelText('Số lượng dòng 1'), { target: { value: '5' } });

    fireEvent.submit(dialog);
    fireEvent.submit(dialog);
    expect(post).toHaveBeenCalledTimes(1);
    expect((view.getByRole('button', { name: 'Đang tạo phiếu...' }) as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByRole('button', { name: 'Đóng tạo phiếu' }) as HTMLButtonElement).disabled).toBe(true);

    await act(async () => { resolveCreate({ data: {} }); });
    await waitFor(() => expect(view.queryByRole('dialog', { name: 'Tạo phiếu điều chuyển' })).toBeNull());
  });

  it('reuses the create key after failure and rotates it when the payload changes', async () => {
    post.mockRejectedValueOnce(new Error('offline')).mockRejectedValueOnce(new Error('offline'));
    const view = render(<StockTransfers />);
    await view.findByText('TRF-807');
    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu' }));
    const dialog = view.getByRole('dialog', { name: 'Tạo phiếu điều chuyển' });

    fireEvent.change(within(dialog).getByLabelText('Kho nguồn'), { target: { value: '1' } });
    fireEvent.change(within(dialog).getByLabelText('Kho đích'), { target: { value: '2' } });
    fireEvent.change(within(dialog).getByLabelText('Sản phẩm dòng 1'), { target: { value: '9' } });
    fireEvent.change(within(dialog).getByLabelText('Số lượng dòng 1'), { target: { value: '5' } });

    fireEvent.submit(dialog);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    await view.findByRole('alert');
    await waitFor(() => expect((within(dialog).getByRole('button', { name: 'Tạo phiếu' }) as HTMLButtonElement).disabled).toBe(false));
    const firstKey = (post.mock.calls[0][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];

    fireEvent.submit(dialog);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));
    await waitFor(() => expect((within(dialog).getByRole('button', { name: 'Tạo phiếu' }) as HTMLButtonElement).disabled).toBe(false));
    const retryKey = (post.mock.calls[1][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(retryKey).toBe(firstKey);

    fireEvent.change(within(dialog).getByLabelText('Số lượng dòng 1'), { target: { value: '6' } });
    fireEvent.submit(dialog);
    await waitFor(() => expect(post).toHaveBeenCalledTimes(3));
    const changedKey = (post.mock.calls[2][2] as { headers: Record<string, string> }).headers['Idempotency-Key'];
    expect(changedKey).not.toBe(firstKey);
    expect((post.mock.calls[2][1] as { details: Array<{ quantity: number }> }).details[0].quantity).toBe(6);
    await waitFor(() => expect(view.queryByRole('dialog', { name: 'Tạo phiếu điều chuyển' })).toBeNull());
  });

  it('ignores an outdated detail response after another transfer was opened', async () => {
    let resolveOld!: (response: unknown) => void;
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') return {
        data: { items: [transfer, { ...transfer, id: 808, code: 'TRF-808' }], totalPages: 1 },
      } as never;
      if (url === '/api/stock-transfers/807') {
        return new Promise(resolve => { resolveOld = resolve; }) as never;
      }
      if (url === '/api/stock-transfers/808') return {
        data: { ...transfer, id: 808, code: 'TRF-808' },
      } as never;
      if (url === '/api/warehouses' || url === '/api/products') return { data: [] } as never;
      return { data: [] } as never;
    });

    const view = render(<StockTransfers />);
    fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-807' }));
    fireEvent.click(view.getByRole('button', { name: 'Xem chi tiết TRF-808' }));
    expect(await view.findByRole('dialog', { name: 'TRF-808' })).toBeTruthy();

    await act(async () => { resolveOld({ data: transfer }); });
    expect(view.queryByRole('dialog', { name: 'TRF-807' })).toBeNull();
    expect(view.getByRole('dialog', { name: 'TRF-808' })).toBeTruthy();
  });

  it('ignores an older list response after a newer filtered query has completed', async () => {
    let resolveOld!: (response: unknown) => void;
    let listCalls = 0;
    get.mockImplementation(async url => {
      if (url === '/api/stock-transfers') {
        listCalls += 1;
        if (listCalls === 1) return new Promise(resolve => { resolveOld = resolve; }) as never;
        return { data: { items: [{ ...transfer, id: 808, code: 'TRF-NEW' }], totalPages: 1 } } as never;
      }
      if (url === '/api/warehouses' || url === '/api/products') return { data: [] } as never;
      return { data: transfer } as never;
    });

    const view = render(<StockTransfers />);
    fireEvent.change(view.getByLabelText('Tìm mã phiếu điều chuyển'), { target: { value: 'TRF-NEW' } });
    fireEvent.click(view.getByRole('button', { name: 'Lọc' }));
    expect(await view.findByText('TRF-NEW')).toBeTruthy();

    await act(async () => {
      resolveOld({ data: { items: [transfer], totalPages: 1 } });
    });
    expect(view.queryByText('TRF-807')).toBeNull();
    expect(view.getByText('TRF-NEW')).toBeTruthy();
  });
});
