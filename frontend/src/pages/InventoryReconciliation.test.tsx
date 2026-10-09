// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import InventoryReconciliation from './InventoryReconciliation';
import apiClient from '../services/apiClient';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn() },
}));

vi.mock('../services/runtimeMode', () => ({
  isBlueprintDemoRuntime: vi.fn(() => false),
}));

describe('InventoryReconciliation', () => {
  beforeEach(() => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(false);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('uses read-only demo data on the Vercel blueprint runtime without calling APIs', async () => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(true);
    const view = render(<InventoryReconciliation />);

    expect(await view.findByText('SKU-1001')).toBeTruthy();
    expect(view.getByText('Đối chiếu tồn kho & ledger')).toBeTruthy();
    expect(view.getByText(/chỉ đọc/)).toBeTruthy();
    expect(view.getByText(/Phạm vi hiện tại chỉ đối chiếu trạng thái AVAILABLE/)).toBeTruthy();
    expect(view.getAllByText('Lệch').length).toBeGreaterThan(0);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('renders reconciliation rows and highlights mismatches', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/warehouses') {
        return Promise.resolve({ data: [{ id: 1, name: 'Kho HCM', isActive: true }] });
      }
      return Promise.resolve({
        data: {
          items: [
            {
              productId: 10,
              productCode: 'SKU-010',
              productName: 'Sản phẩm test',
              warehouseId: 1,
              warehouseName: 'Kho HCM',
              currentQuantity: 12,
              expectedQuantity: 10,
              difference: 2,
              importQuantity: 20,
              exportQuantity: 10,
              transferInQuantity: 0,
              transferOutQuantity: 0,
              adjustmentIncreaseQuantity: 0,
              adjustmentDecreaseQuantity: 0,
              status: 'Mismatch',
            },
          ],
          totalRecords: 1,
          pageIndex: 1,
          pageSize: 20,
          totalPages: 1,
        },
      });
    });

    const view = render(<InventoryReconciliation />);

    expect(await view.findByText('SKU-010')).toBeTruthy();
    expect(view.getByText('Lệch')).toBeTruthy();
    expect(view.getByText('Mismatch trang hiện tại').previousSibling?.textContent).toBe('1');
    expect(view.getByText('Độ lệch tuyệt đối').previousSibling?.textContent).toBe('2');
  });

  it('applies warehouse and keyword filters to the reconciliation request', async () => {
    vi.mocked(apiClient.get).mockImplementation((url: string) => {
      if (url === '/api/warehouses') {
        return Promise.resolve({ data: [{ id: 1, name: 'Kho HCM', isActive: true }] });
      }
      return Promise.resolve({ data: { items: [], totalRecords: 0, pageIndex: 1, pageSize: 20, totalPages: 0 } });
    });

    const view = render(<InventoryReconciliation />);
    await waitFor(() => expect(vi.mocked(apiClient.get).mock.calls.some(([url]) => String(url).startsWith('/api/InventoryReconciliation?'))).toBe(true));

    fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
    fireEvent.change(view.getByLabelText('Mã / tên sản phẩm'), { target: { value: 'milk' } });
    fireEvent.click(view.getByRole('button', { name: 'Đối chiếu' }));

    await waitFor(() => {
      const urls = vi.mocked(apiClient.get).mock.calls.map(([url]) => String(url));
      expect(urls.some(url => url.includes('warehouseId=1') && url.includes('keyword=milk') && url.includes('page=1'))).toBe(true);
    });
  });
});
