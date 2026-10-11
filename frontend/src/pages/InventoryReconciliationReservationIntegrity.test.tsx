// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import InventoryReconciliation from './InventoryReconciliation';
import apiClient from '../services/apiClient';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../services/runtimeMode', () => ({
  isBlueprintDemoRuntime: vi.fn(() => false),
}));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

it('warns about invalid reserved buckets without certifying a ledger match', async () => {
  vi.mocked(isBlueprintDemoRuntime).mockReturnValue(false);
  const statuses = ['Available', 'QcHold', 'Quarantine', 'Damaged',
    'Rejected', 'Blocked', 'Expired', 'RecallBlocked'];
  const evidence = {
    warehouseId: 1, warehouseName: 'Kho HCM',
    productId: 10, productCode: 'SKU-RES', productName: 'Hàng kiểm tra',
    eventAnchorId: 20, bucketAnchorId: 5, allStatusBucketAnchorId: 5,
    eventCount: 0, bucketCount: 1, eventsTruncated: false, bucketsTruncated: false,
    ledgerHasEventsAfterAnchor: false, bucketHasRowsAfterAnchor: false,
    allStatusBucketHasRowsAfterAnchor: false, evidenceAnchorsStale: false,
    invalidReservationBucketCount: 1, availableLedgerExpectedIsPartial: true,
    allStatusStatus: 'Indeterminate', allStatusCurrentQuantity: 10,
    allStatusReservedQuantity: 11, allStatusExpectedQuantity: null,
    allStatusDifference: null, snapshotComparisonStatus: 'Indeterminate',
    snapshotComparisonExpectedQuantity: null, snapshotComparisonDifference: null,
    currentQuantity: 10, expectedQuantity: 10, difference: 0,
    statusBreakdown: statuses.map(status => ({
      status, currentQuantity: status === 'Available' ? 10 : 0,
      reservedQuantity: status === 'Available' ? 11 : 0,
      bucketCount: status === 'Available' ? 1 : 0,
      directLedgerNetQuantity: 0, statusChangeInQuantity: 0,
      statusChangeOutQuantity: 0, expectedQuantity: null, difference: null,
    })),
    isReadOnly: true, events: [], buckets: [],
  };
  vi.mocked(apiClient.get).mockImplementation((url: string) => {
    if (url.endsWith('/warehouses'))
      return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
    if (url.includes('/investigation?'))
      return Promise.resolve({ data: evidence });
    return Promise.resolve({ data: { items: [{
      productId: 10, productCode: 'SKU-RES', productName: 'Hàng kiểm tra',
      warehouseId: 1, warehouseName: 'Kho HCM',
      currentQuantity: 10, expectedQuantity: null, difference: null,
      invalidReservationBucketCount: 1, status: 'Indeterminate',
      allStatusStatus: 'Indeterminate',
    }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } });
  });
  const view = render(<InventoryReconciliation />);
  await view.findByText('SKU-RES');
  fireEvent.click(view.getByRole('button', {
    name: 'Xem bằng chứng SKU-RES tại Kho HCM',
  }));
  expect(await view.findByText(/1 bucket có lượng giữ chỗ/)).toBeTruthy();
  expect(view.getByText(/Kết luận 8 trạng thái/).textContent).toContain('Chưa xác định');
  expect(view.queryByText(/Mốc bằng chứng đã cũ/)).toBeNull();
});
