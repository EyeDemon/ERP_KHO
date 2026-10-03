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
    vi.resetAllMocks(); localStorage.clear(); localStorage.setItem('role', 'Admin'); localStorage.setItem('permissions','["product.read","product.update","product.create","product.deactivate","product_category.read","product_category.manage","product_barcode.manage","uom.read"]');
    vi.mocked(apiClient.get).mockImplementation(async (url) => ({ data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : url === '/api/product-barcodes/lookup' ? product : [] }));
  });

  it('submits Enter in scanner only to exact barcode lookup and preserves leading zeroes and case', async () => {
    const view = render(<Products />); await view.findByText('P001');
    fireEvent.change(view.getByLabelText('Tra mã vạch'), { target: { value: '001Ab' } });
    fireEvent.submit(view.getByLabelText('Tra mã vạch').closest('form')!);
    await waitFor(() => expect(apiClient.get).toHaveBeenCalledWith('/api/product-barcodes/lookup', { params: { value: '001Ab' } }));
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('shows lookup network/not-found errors and hides mutations from Viewer', async () => {
    localStorage.setItem('role', 'Viewer'); localStorage.setItem('permissions','["product.read"]');
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/product-barcodes/lookup') throw { response: { data: { message: 'Không tìm thấy mã vạch.' } } };
      return { data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : [] };
    });
    const view = render(<Products />); await view.findByText('P001');
    expect(view.queryByText('Thêm danh mục')).toBeNull(); expect(view.queryByText('Sửa')).toBeNull();
    fireEvent.submit(view.getByLabelText('Tra mã vạch').closest('form')!);
    await view.findByRole('alert');
  });

  it('clears a previous lookup result when the next barcode is not found', async () => {
    let lookupCount = 0;
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/product-barcodes/lookup' && lookupCount++ > 0) throw { response: { data: { message: 'Không tìm thấy mã vạch.' } } };
      return { data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : url === '/api/product-barcodes/lookup' ? product : [] };
    });
    const view = render(<Products />); await view.findByText('P001');
    const scanner = view.getByLabelText('Tra mã vạch');
    fireEvent.submit(scanner.closest('form')!); await view.findByText(/Mã vạch thuộc sản phẩm/);
    fireEvent.submit(scanner.closest('form')!); await view.findByRole('alert');
    expect(view.queryByText(/Mã vạch thuộc sản phẩm/)).toBeNull();
    expect((view.getByLabelText('Tìm sản phẩm') as HTMLInputElement).value).toBe('');
  });

  it('searches and edits a category without allowing its code to change', async () => {
    const category = { id: 3, code: 'CAT', name: 'Danh mục cũ', isActive: true };
    vi.mocked(apiClient.get).mockImplementation(async (url) => ({ data: url === '/api/products' ? [product] : url === '/api/units' ? [{ id: 1, code: 'EA', name: 'Cái', isActive: true }] : url === '/api/product-categories' ? [category] : [] }));
    const view = render(<Products />); await view.findByRole('button', { name: /Sửa danh mục/ });
    fireEvent.change(view.getByLabelText('Tìm danh mục'), { target: { value: 'cat' } });
    fireEvent.click(view.getByRole('button', { name: /Sửa danh mục/ }));
    expect((view.getByPlaceholderText('Mã danh mục') as HTMLInputElement).disabled).toBe(true);
    fireEvent.change(view.getByPlaceholderText('Tên danh mục'), { target: { value: 'Danh mục mới' } });
    fireEvent.submit(view.getByText('Lưu danh mục').closest('form')!);
    await waitFor(() => expect(apiClient.put).toHaveBeenCalledWith('/api/product-categories/3', { name: 'Danh mục mới', isActive: true }));
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

  it('product update does not expose category or barcode management', async () => {
    localStorage.setItem('permissions', '["product.read","product.update","uom.read"]');
    const view = render(<Products />); await view.findByText('P001');
    expect(view.queryByText('Thêm danh mục')).toBeNull();
    expect(apiClient.get).not.toHaveBeenCalledWith('/api/product-categories');
    fireEvent.click(view.getByText('Sửa'));
    expect(view.queryByText('Thêm mã vạch')).toBeNull();
    expect((view.getByLabelText('Danh mục') as HTMLSelectElement).disabled).toBe(true);
  });

  it('barcode management does not expose product save or category management', async () => {
    localStorage.setItem('permissions', '["product.read","product_barcode.manage","uom.read"]');
    const view = render(<Products />); await view.findByText('P001');
    fireEvent.click(view.getByText('Sửa'));
    expect(view.queryByText('Lưu')).toBeNull();
    expect(view.queryByText('Thêm danh mục')).toBeNull();
    expect(view.getByText('Thêm mã vạch')).toBeTruthy();
  });
});
