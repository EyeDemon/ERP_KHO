// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import InventoryReconciliation from './InventoryReconciliation';
import apiClient from '../services/apiClient';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../services/runtimeMode', () => ({
  isReadOnlyDemoRuntime: vi.fn(() => false),
}));

const statuses = ['Available', 'QcHold', 'Quarantine', 'Damaged',
  'Rejected', 'Blocked', 'Expired', 'RecallBlocked'];

describe('InventoryReconciliation anchored snapshot provenance', () => {
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it.each(['ledger', 'bucket', 'other-status-bucket', 'aggregate-change'] as const)(
    'renders indeterminate comparison for stale %s anchor and rejects forged Match',
    async drift => {
      vi.mocked(isReadOnlyDemoRuntime).mockReturnValue(false);
      let forged = false;
      const evidence = {
        warehouseId: 1, warehouseName: 'Kho HCM',
        productId: 10, productCode: 'SKU-ANCHOR', productName: 'Hàng thử',
        eventAnchorId: 72, bucketAnchorId: 12, allStatusBucketAnchorId: 12,
        allStatusBucketAggregateFingerprint: 'a'.repeat(64),
        allStatusBucketAggregateChanged: drift === 'aggregate-change',
        eventCount: 0, bucketCount: 0,
        ledgerHasEventsAfterAnchor: drift === 'ledger',
        bucketHasRowsAfterAnchor: drift === 'bucket',
        allStatusBucketHasRowsAfterAnchor: drift === 'other-status-bucket',
        evidenceAnchorsStale: true,
        snapshotComparisonStatus: 'Indeterminate',
        snapshotComparisonExpectedQuantity: null,
        snapshotComparisonDifference: null,
        availableLedgerExpectedIsPartial: false,
        currentQuantity: 10, expectedQuantity: 10, difference: 0,
        allStatusCurrentQuantity: 10, allStatusReservedQuantity: 0,
        allStatusExpectedQuantity: 10, allStatusDifference: 0,
        allStatusStatus: 'Match', isReadOnly: true,
        events: [], buckets: [], eventsTruncated: false, bucketsTruncated: false,
        statusBreakdown: statuses.map(status => ({
          status, currentQuantity: status === 'Available' ? 10 : 0,
          reservedQuantity: 0, bucketCount: 0, directLedgerNetQuantity: 0,
          statusChangeInQuantity: 0, statusChangeOutQuantity: 0,
          expectedQuantity: status === 'Available' ? 10 : 0, difference: 0,
        })),
      };
      vi.mocked(apiClient.get).mockImplementation((url: string) => {
        if (url.endsWith('/warehouses'))
          return Promise.resolve({ data: [{ id: 1, code: 'HCM', name: 'Kho HCM' }] });
        if (url.includes('/investigation?')) {
          const bucketStatus = new URL(url, 'http://localhost').searchParams.get('bucketStatus') ?? 'Available';
          return Promise.resolve({ data: forged
            ? { ...evidence, snapshotComparisonStatus: 'Match',
              snapshotComparisonExpectedQuantity: 10, snapshotComparisonDifference: 0 }
            : { ...evidence, bucketStatus } });
        }
        return Promise.resolve({ data: { items: [{
          productId: 10, productCode: 'SKU-ANCHOR', productName: 'Hàng thử',
          warehouseId: 1, warehouseName: 'Kho HCM',
          currentQuantity: 10, expectedQuantity: 10, difference: 0,
          status: 'Match',
        }], totalRecords: 1, pageIndex: 1, pageSize: 20, totalPages: 1 } });
      });

      const view = render(<InventoryReconciliation />);
      await view.findByText('SKU-ANCHOR');
      const trigger = view.getByRole('button', {
        name: 'Xem bằng chứng SKU-ANCHOR tại Kho HCM',
      });
      fireEvent.click(trigger);
      expect(await view.findByText(/Mốc bằng chứng đã cũ/)).toBeTruthy();
      expect(view.getByText(/Kết luận 8 trạng thái/).textContent).toContain('Chưa xác định');
      expect(view.getByText('Tổng Ledger các trạng thái (tham khảo)').previousSibling?.textContent)
        .toBe('Chưa xác định');
      fireEvent.change(view.getByLabelText('Trạng thái bucket cần xem'),
        { target: { value: 'QcHold' } });
      await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.some(([url]) =>
        url.includes('bucketStatus=QcHold') &&
        url.includes('allStatusBucketAnchorId=12') &&
        url.includes('allStatusBucketAggregateFingerprint=' + 'a'.repeat(64)))).toBe(true));
      expect(await view.findByText(/Mốc bằng chứng đã cũ/)).toBeTruthy();
      fireEvent.click(view.getByRole('button', { name: 'Đóng hồ sơ' }));
      forged = true;
      fireEvent.click(trigger);
      expect(await view.findByText('Không thể tải bằng chứng điều tra. Hãy thử lại.')).toBeTruthy();
      expect(view.queryByText(/Mốc bằng chứng đã cũ/)).toBeNull();
    },
  );
});
