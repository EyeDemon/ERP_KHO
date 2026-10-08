// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Dialog from './StockTransferDetailsDialog';
vi.mock('../services/apiClient', () => ({ default: { get: vi.fn() } }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it('invalidates a previously selected return reason on status transition', async () => {
  localStorage.setItem('permissions', '["inventory_reversal.create"]');
  let calls = 0;
  vi.mocked(apiClient.get).mockImplementation(() => {
    calls += 1;
    return calls === 1
      ? Promise.resolve({ data: [{ code: 'TRANSFER_DISPATCH_ERROR', name: 'Sai tuyến' }] }) as never
      : new Promise(() => {}) as never;
  });
  const props = {
    selected: { id: 81, code: 'TRF-81', sourceWarehouseId: 1, sourceWarehouseName: 'Nguồn',
      destinationWarehouseId: 2, destinationWarehouseName: 'Đích', status: 'InTransit',
      createdBy: 1, createdAt: '2026-10-08', details: [] },
    status: 'InTransit', statusNames: ['Draft', 'Approved', 'InTransit', 'Returned'],
    statusLabel: { Draft: 'Nháp', Approved: 'Duyệt', InTransit: 'Vận chuyển', Returned: 'Hoàn trả' },
    statusTone: () => 'warning' as const, canWrite: true, canReturn: true,
    canApprove: false, userId: 1, actionInFlight: false, receive: {},
    setReceive: vi.fn(), onClose: vi.fn(), onAction: vi.fn(async () => {}),
    onReceive: vi.fn(async () => {}),
  };
  const view = render(<Dialog {...props} />);
  await view.findByRole('option', { name: 'Sai tuyến' });
  fireEvent.change(view.getByLabelText('Mã lý do hoàn trả'), { target: { value: 'TRANSFER_DISPATCH_ERROR' } });
  fireEvent.change(view.getByLabelText('Diễn giải hoàn trả'), { target: { value: 'Chưa nhận' } });
  view.rerender(<Dialog {...props} status="Returned" />);
  view.rerender(<Dialog {...props} status="InTransit" />);
  expect((view.getByLabelText('Mã lý do hoàn trả') as HTMLSelectElement).value).toBe('');
  expect((view.getByRole('button', { name: 'Hoàn trả kho nguồn' }) as HTMLButtonElement).disabled).toBe(true);
});
