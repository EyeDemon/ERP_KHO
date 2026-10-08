// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import InventoryTraceability from './InventoryTraceability';
import StockTransferDetailsDialog from './StockTransferDetailsDialog';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn() } }));

const selected = {
  id: 81, code: 'TRF-RETURN-81', status: 'Returned',
  sourceWarehouseId: 1, sourceWarehouseName: 'Kho nguồn',
  destinationWarehouseId: 2, destinationWarehouseName: 'Kho đích',
  createdBy: 7, createdAt: '2026-10-08T09:00:00Z',
  returnReasonCode: 'TRANSFER_DISPATCH_ERROR', returnReason: 'Chuyển nhầm',
  details: [],
};
const statusNames = ['Draft', 'Approved', 'InTransit', 'Received', 'Completed', 'Cancelled', 'Returned'];
const dialog = (
  <StockTransferDetailsDialog
    selected={selected}
    status="Returned"
    statusNames={statusNames}
    statusLabel={{ Returned: 'Đã hoàn trả' }}
    statusTone={() => 'warning'}
    canWrite={true}
    canReturn={true}
    canApprove={false}
    userId={7}
    actionInFlight={false}
    receive={{}}
    setReceive={() => {}}
    onClose={() => {}}
    onAction={async () => {}}
    onReceive={async () => {}}
    onOpenTransfer={async () => {}}
  />
);

describe('Native stock-transfer return traceability navigation', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    vi.mocked(apiClient.get).mockResolvedValue({
      data: { currentBuckets: [], events: [], eventsTruncated: false, bucketsTruncated: false }
    } as never);
  });
  afterEach(() => { cleanup(); localStorage.clear(); });

  it('uses SPA navigation and loads immutable document reference only with trace permission', async () => {
    localStorage.setItem('permissions', '["inventory_traceability.read"]');
    const view = render(
      <MemoryRouter initialEntries={['/stock-transfers']}>
        <Routes>
          <Route path="/stock-transfers" element={dialog} />
          <Route path="/inventory-traceability" element={<InventoryTraceability />} />
        </Routes>
      </MemoryRouter>
    );
    const link = view.getByRole('link', { name: 'Truy vết sổ cái hoàn trả' });
    expect(link.getAttribute('href')).toBe('/inventory-traceability?referenceType=StockTransfer&referenceId=81');
    fireEvent.click(link);
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith(
      '/api/inventory/traceability?referenceType=StockTransfer&referenceId=81&limit=200'
    ));
    expect((view.getByLabelText('Loại tham chiếu') as HTMLInputElement).value).toBe('StockTransfer');
    expect((view.getByLabelText('ID tham chiếu') as HTMLInputElement).value).toBe('81');
  });

  it('hides the trace link when the current user has no traceability permission', () => {
    const view = render(<MemoryRouter>{dialog}</MemoryRouter>);
    expect(view.queryByRole('link', { name: 'Truy vết sổ cái hoàn trả' })).toBeNull();
    expect(apiClient.get).not.toHaveBeenCalled();
  });
});
