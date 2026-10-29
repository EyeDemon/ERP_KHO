// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Warehouses from './Warehouses';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);

const warehouses = [
  { id: 1, code: 'WH-HCM-01', name: 'DC Hồ Chí Minh', address: 'TP.HCM', isActive: true },
];

describe('Warehouses page with UiDrawer', { timeout: 20000 }, () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["warehouse.read","warehouse.manage"]');
    get.mockResolvedValue({ data: warehouses } as never);
    post.mockResolvedValue({ data: {} } as never);
    put.mockResolvedValue({ data: {} } as never);
  });

  afterEach(cleanup);

  it('defaults showForm to false on initial load and opens UiDrawer when clicking + Thêm kho mới', async () => {
    const view = render(<Warehouses />);
    expect(await view.findByText('WH-HCM-01', {}, { timeout: 10000 })).toBeTruthy();
    expect(view.queryByTestId('ui-drawer')).toBeNull();

    fireEvent.click(view.getByRole('button', { name: '+ Thêm kho mới' }));
    expect(view.getByTestId('ui-drawer')).toBeTruthy();

    fireEvent.change(view.getByLabelText('Mã kho'), { target: { value: 'WH-HN-01' } });
    fireEvent.change(view.getByLabelText('Tên kho'), { target: { value: 'DC Hà Nội' } });
    fireEvent.change(view.getByLabelText('Địa chỉ kho'), { target: { value: 'Hà Nội' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu kho' }));

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/warehouses', {
        code: 'WH-HN-01',
        name: 'DC Hà Nội',
        address: 'Hà Nội',
      }),
    );
  });

  it('opens UiDrawer when clicking Sửa on a warehouse row and updates the warehouse', async () => {
    const view = render(<Warehouses />);
    await view.findByText('WH-HCM-01', {}, { timeout: 10000 });
    fireEvent.click(view.getByRole('button', { name: 'Sửa kho WH-HCM-01' }));
    expect(view.getByTestId('ui-drawer')).toBeTruthy();
    expect((view.getByLabelText('Mã kho') as HTMLInputElement).disabled).toBe(true);

    fireEvent.change(view.getByLabelText('Tên kho'), { target: { value: 'DC Hồ Chí Minh Mới' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu kho' }));

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith('/api/warehouses/1', {
        name: 'DC Hồ Chí Minh Mới',
        address: 'TP.HCM',
        isActive: true,
      }),
    );
  });
});
