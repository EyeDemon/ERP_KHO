// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Inventory from './Inventory';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

describe('Inventory opening balance multipart FormData import & template download', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    get.mockImplementation(async (url) => {
      const value = String(url);
      if (value === '/api/warehouses') {
        return { data: [{ id: 1, name: 'DC Hồ Chí Minh', isActive: true }] } as never;
      }
      if (value.startsWith('/api/InventoryStocks/current?')) {
        return {
          data: [
            {
              productId: 10,
              productCode: 'SKU-10',
              productName: 'Sản phẩm 10',
              unitName: 'Cái',
              warehouseId: 1,
              warehouseName: 'DC Hồ Chí Minh',
              quantity: 12,
              onHandQuantity: 12,
              reservedQuantity: 2,
              availableQuantity: 10,
              lastUpdated: '2026-10-03T08:00:00Z',
            },
          ],
        } as never;
      }
      if (value === '/api/v1/import-templates/opening-balance') {
        return { data: new Blob(['xlsx-template']) } as never;
      }
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('submits real File via FormData to /api/v1/inventory/opening-balances/import and downloads .xlsx template', async () => {
    post.mockResolvedValueOnce({
      data: {
        importBatchId: 'OB-IMP-2026-88',
        importedRows: 15,
        status: 'Đã hạch toán tồn kho đầu kỳ thành công',
      },
    } as never);

    const view = render(<Inventory />);
    expect(await view.findByText('SKU-10')).toBeTruthy();

    const fileInput = view.getByLabelText('Tệp Excel tồn kho đầu kỳ') as HTMLInputElement;
    const excelFile = new File(['xlsx-bytes'], 'opening.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    fireEvent.change(fileInput, { target: { files: [excelFile] } });

    fireEvent.click(view.getByRole('button', { name: 'Nhập Excel Tồn kho Đầu kỳ' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    const [calledUrl, calledPayload, calledConfig] = post.mock.calls[0];
    expect(calledUrl).toBe('/api/v1/inventory/opening-balances/import');
    expect(calledPayload).toBeInstanceOf(FormData);
    const formData = calledPayload as FormData;
    expect(formData.get('file')).toBeInstanceOf(File);
    expect((formData.get('file') as File).name).toBe('opening.xlsx');
    expect(formData.get('fileName')).toBe('opening.xlsx');
    expect(calledConfig).toEqual(
      expect.objectContaining({
        headers: { 'Content-Type': 'multipart/form-data' },
      }),
    );

    expect(await view.findByText(/OB-IMP-2026-88/)).toBeTruthy();
    expect(view.getByText(/Đã nhập Excel tồn kho đầu kỳ từ tệp opening.xlsx\./)).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Tải file mẫu .xlsx' }));
    await waitFor(() =>
      expect(get).toHaveBeenCalledWith('/api/v1/import-templates/opening-balance', {
        responseType: 'blob',
      }),
    );
  });
});
