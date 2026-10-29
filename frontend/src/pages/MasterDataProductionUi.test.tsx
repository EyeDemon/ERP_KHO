// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Warehouses from './Warehouses';
import Units from './Units';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const put = vi.mocked(apiClient.put);

const warehouse = {
  id: 1,
  code: 'WH-HCM-01',
  name: 'DC Hồ Chí Minh',
  address: 'TP.HCM',
  isActive: true,
};

const unit = {
  id: 1,
  code: 'EA',
  name: 'Cái',
  isActive: true,
};

describe('master data production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async (url) => ({
      data: url === '/api/warehouses' ? [warehouse] : url === '/api/units' ? [unit] : [],
    }) as never);
    put.mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('keeps warehouse mutations hidden for read-only users', async () => {
    localStorage.setItem('role', 'Viewer');
    localStorage.setItem('permissions', '["warehouse.read"]');

    const view = render(<Warehouses />);

    expect(await view.findByText('WH-HCM-01')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách kho' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Thêm kho' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Sửa kho WH-HCM-01' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Xóa kho WH-HCM-01' })).toBeNull();
  });

  it('locks the warehouse code on edit and preserves the existing update contract', async () => {
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["warehouse.read","warehouse.manage"]');

    const view = render(<Warehouses />);
    await view.findByText('WH-HCM-01');
    fireEvent.click(view.getByRole('button', { name: 'Sửa kho WH-HCM-01' }));

    expect((view.getByLabelText('Mã kho') as HTMLInputElement).disabled).toBe(true);
    fireEvent.change(view.getByLabelText('Tên kho'), { target: { value: 'DC Hồ Chí Minh mới' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu kho' }));

    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/warehouses/1', {
      name: 'DC Hồ Chí Minh mới',
      address: 'TP.HCM',
      isActive: true,
    }));
  });

  it('keeps unit mutations hidden for read-only users', async () => {
    localStorage.setItem('role', 'Viewer');
    localStorage.setItem('permissions', '["uom.read"]');

    const view = render(<Units />);

    expect(await view.findByText('EA')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách đơn vị tính' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Thêm đơn vị' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Sửa đơn vị EA' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Xóa đơn vị EA' })).toBeNull();
  });

  it('locks the unit code on edit and preserves the existing update contract', async () => {
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["uom.read","uom.manage"]');

    const view = render(<Units />);
    await view.findByText('EA');
    fireEvent.click(view.getByRole('button', { name: 'Sửa đơn vị EA' }));

    expect((view.getByLabelText('Mã đơn vị tính') as HTMLInputElement).disabled).toBe(true);
    fireEvent.change(view.getByLabelText('Tên đơn vị tính'), { target: { value: 'Cái mới' } });
    fireEvent.click(view.getByRole('button', { name: 'Lưu đơn vị' }));

    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/units/1', {
      name: 'Cái mới',
      isActive: true,
    }));
  });
});
