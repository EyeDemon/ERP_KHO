// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AxiosError } from 'axios';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import Permissions from './Permissions';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), delete: vi.fn() } }));
const get = vi.mocked(apiClient.get); const post = vi.mocked(apiClient.post);
const grants = ['permission.read', 'role.read', 'permission.assign'];
const catalog = [{ code: 'product_category.manage', description: '' }];
const role = { id: 7, roleName: 'Manager', rowVersion: 'AAAAAAAAAAE=', permissions: [] };

describe('quản trị quyền truy cập', () => {
  beforeEach(() => {
    vi.resetAllMocks(); localStorage.clear(); setCurrentPermissions(grants);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    get.mockImplementation(async url => ({ data: url === '/api/permissions' ? catalog : url === '/api/auth/me' ? { permissions: grants } : [role] }) as never);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it('hiển thị danh mục bằng tiếng Việt cho người chỉ có quyền đọc', async () => {
    setCurrentPermissions(['permission.read']);
    const view = render(<Permissions />);
    expect(await view.findByRole('table', { name: 'Danh mục quyền truy cập' })).toBeTruthy();
    expect(view.container.textContent).not.toContain('product_category.manage');
    expect(view.queryByText('Cấp quyền')).toBeNull();
    expect(get).not.toHaveBeenCalledWith('/api/permissions/roles');
  });

  it('double-click chỉ gửi một mutation với token của role và refresh quyền sau thành công', async () => {
    let finish!: (value: unknown) => void;
    post.mockReturnValue(new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<Permissions />);
    const button = await view.findByText('Cấp quyền');
    fireEvent.click(button); fireEvent.click(button);
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/api/permissions/roles/7/grants', {
      permissionCode: 'product_category.manage', rowVersion: role.rowVersion,
    }, expect.objectContaining({ headers: expect.any(Object) }));
    expect((view.getByText('Đang lưu...') as HTMLButtonElement).disabled).toBe(true);
    await act(async () => finish({ data: { changed: true } }));
    expect(await view.findByText('Đã cập nhật quyền truy cập.')).toBeTruthy();
    expect(get).toHaveBeenCalledWith('/api/auth/me');
  });

  it('409 xóa grants cũ và không hiển thị lỗi kỹ thuật', async () => {
    const error = new AxiosError('SQL provider detail');
    error.response = { status: 409, data: { message: 'SQL provider detail' } } as never;
    post.mockRejectedValue(error);
    const view = render(<Permissions />);
    fireEvent.click(await view.findByText('Cấp quyền'));
    expect(await view.findByRole('alert')).toHaveProperty('textContent', 'Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.');
    expect(view.queryByText('Cấp quyền')).toBeNull();
    expect(view.container.textContent).not.toContain('SQL provider');
  });

  it('thu hồi quyền đọc xóa dữ liệu quản trị đang hiển thị', async () => {
    const view = render(<Permissions />); await view.findByText('Quản lý');
    act(() => setCurrentPermissions([]));
    await waitFor(() => expect(view.queryByText('Quản lý')).toBeNull());
  });
});
