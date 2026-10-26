// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Units from './Units';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);

const units = [
  { id: 1, code: 'EA', name: 'Cái', isActive: true },
  { id: 2, code: 'BOX', name: 'Thùng', isActive: true },
];

describe('Units page with UiDrawer', { timeout: 20000 }, () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["uom.read","uom.manage"]');
    get.mockResolvedValue({ data: units } as never);
    post.mockResolvedValue({ data: {} } as never);
    put.mockResolvedValue({ data: {} } as never);
  });

  afterEach(cleanup);

  it('defaults showForm to false on initial load and opens UiDrawer when clicking + Thêm đơn vị tính', async () => {
    const view = render(<Units />);
    expect(await view.findByText('EA', {}, { timeout: 10000 })).toBeTruthy();
    expect(view.queryByTestId('ui-drawer')).toBeNull();

    fireEvent.click(view.getByRole('button', { name: '+ Thêm đơn vị tính' }));
    expect(view.getByTestId('ui-drawer')).toBeTruthy();

    fireEvent.change(view.getByLabelText('Mã đơn vị tính'), { target: { value: 'PAL' } });
    fireEvent.change(view.getByLabelText('Tên đơn vị tính'), { target: { value: 'Pallet' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu đơn vị' }));

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/units', {
        code: 'PAL',
        name: 'Pallet',
      }),
    );
  });

  it('opens UiDrawer when clicking Sửa on a unit row and updates the unit', async () => {
    const view = render(<Units />);
    await view.findByText('EA', {}, { timeout: 10000 });
    fireEvent.click(view.getByRole('button', { name: 'Sửa đơn vị EA' }));
    expect(view.getByTestId('ui-drawer')).toBeTruthy();
    expect((view.getByLabelText('Mã đơn vị tính') as HTMLInputElement).disabled).toBe(true);

    fireEvent.change(view.getByLabelText('Tên đơn vị tính'), { target: { value: 'Chiếc' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu đơn vị' }));

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith('/api/units/1', {
        name: 'Chiếc',
        isActive: true,
      }),
    );
  });
});
