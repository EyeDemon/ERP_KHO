// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import StockTransfers from './StockTransfers';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));
const transfer = {
  id: 71, code: 'TRF-71', sourceWarehouseId: 1, sourceWarehouseName: 'Kho A',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho B',
  status: 'Draft', draftRevision: 3, createdBy: 1, createdAt: '2026-10-09T00:00:00Z',
  details: [{ productId: 9, productCode: 'SKU-9', productName: 'Sản phẩm',
    requestedQuantity: 5, dispatchedQuantity: 0, receivedQuantity: 0,
    missingQuantity: 0, damagedQuantity: 0, inTransitQuantity: 0 }],
};
beforeEach(() => {
  vi.resetAllMocks();
  localStorage.clear();
  localStorage.setItem('role', 'WarehouseStaff');
});
afterEach(cleanup);

it('refreshes a stale draft revision before the next PUT', async () => {
  let current = transfer;
  vi.mocked(apiClient.get).mockImplementation(async url => {
    if (url === '/api/stock-transfers') return { data: { items: [current], totalPages: 1 } } as never;
    if (url === '/api/stock-transfers/71') return { data: current } as never;
    if (url === '/api/warehouses') return { data: [{ id: 1, name: 'Kho A' }, { id: 2, name: 'Kho B' }] } as never;
    if (url === '/api/products') return { data: [{ id: 9, code: 'SKU-9', name: 'Sản phẩm' }] } as never;
    return { data: [] } as never;
  });
  const put = vi.mocked(apiClient.put);
  put.mockRejectedValueOnce({ response: { status: 409, data: { message: 'Phiếu đã thay đổi' } } })
    .mockResolvedValueOnce({ data: {} } as never);
  const view = render(<StockTransfers />);
  fireEvent.click(await view.findByRole('button', { name: 'Xem chi tiết TRF-71' }));
  fireEvent.click(await view.findByRole('button', { name: 'Chỉnh sửa phiếu nháp' }));
  const dialog = view.getByRole('dialog', { name: 'Chỉnh sửa phiếu nháp' });
  fireEvent.submit(dialog);
  await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
  expect(put.mock.calls[0][1]).toMatchObject({ expectedDraftRevision: 3 });
  current = { ...current, draftRevision: 4 };
  fireEvent.click(await within(dialog).findByRole('button', { name: 'Tải lại phiên bản mới' }));
  await waitFor(() => expect(within(dialog).getByText(/Phiên bản nháp: 4/)).toBeTruthy());
  fireEvent.submit(dialog);
  await waitFor(() => expect(put).toHaveBeenCalledTimes(2));
  expect(put.mock.calls[1][1]).toMatchObject({ expectedDraftRevision: 4 });
});
