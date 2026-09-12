// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Products from './Products';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

const product = { id: 1, code: 'P001', name: 'Sản phẩm', unitId: 1, unitName: 'Cái', categoryId: null, barcodes: [{ id: 2, productId: 1, value: '001Ab' }], isActive: true };

describe('Products category and barcode UI (mocked API)', () => {
  afterEach(cleanup);
  beforeEach(() => {
    vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin');
    vi.mocked(apiClient.get).mockImplementation(async (url) => ({ data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : url === '/api/product-barcodes/lookup' ? product : [] }));
  });

  it('submits Enter in scanner only to exact barcode lookup and preserves leading zeroes and case', async () => {
    const view = render(<Products />); await view.findByText('P001');
    fireEvent.change(view.getByLabelText('Tra barcode'), { target: { value: '001Ab' } });
    fireEvent.submit(view.getByLabelText('Tra barcode').closest('form')!);
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/product-barcodes/lookup', { params: { value: '001Ab' } }));
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('shows lookup network/not-found errors and hides mutations from Viewer', async () => {
    localStorage.setItem('role', 'Viewer');
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/product-barcodes/lookup') throw { response: { data: { message: 'Không tìm thấy barcode.' } } };
      return { data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : [] };
    });
    const view = render(<Products />); await view.findByText('P001');
    expect(view.queryByText('Thêm danh mục')).toBeNull(); expect(view.queryByText('Sửa')).toBeNull();
    fireEvent.submit(view.getByLabelText('Tra barcode').closest('form')!);
    await view.findByRole('alert');
  });

  it('blocks a duplicate category submit while the first request is pending', async () => {
    let finish!: () => void;
    vi.mocked(apiClient.post).mockReturnValue(new Promise(resolve => { finish = () => resolve({ data: {} }); }) as never);
    const view = render(<Products />); await view.findByText('P001');
    fireEvent.change(view.getByPlaceholderText('Mã danh mục'), { target: { value: 'CAT' } });
    fireEvent.change(view.getByPlaceholderText('Tên danh mục'), { target: { value: 'Danh mục' } });
    const form = view.getByText('Thêm danh mục').closest('form')!;
    fireEvent.submit(form); fireEvent.submit(form);
    expect(apiClient.post).toHaveBeenCalledTimes(1); finish();
    await waitFor(() => expect(view.queryByText('Đang xử lý...')).toBeNull());
  });
});
