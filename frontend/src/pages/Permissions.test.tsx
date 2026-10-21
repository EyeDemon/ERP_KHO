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

  it('hiển thị đúng trạng thái Đang khóa tạm thời sau khi khóa tài khoản và đọc sodPolicyStatus từ backend', async () => {
    get.mockImplementation(async url => {
      if (url === '/api/permissions') return { data: catalog } as never;
      if (url === '/api/auth/me') return { data: { permissions: grants } } as never;
      if (url === '/api/users/2/security') {
        return {
          data: {
            userId: 2,
            username: 'staff01',
            roleName: 'WarehouseStaff',
            isActive: true,
            isLocked: true,
            lockoutEnd: '2026-10-17T15:00:00Z',
            securityRevision: 3,
            activeSessions: 0,
            rowVersion: 'v3',
          },
        } as never;
      }
      return { data: [role] } as never;
    });
    post.mockImplementation(async url => {
      if (url === '/api/user-warehouse-access/verify-sod-isolation') {
        return {
          data: {
            auditId: 'RBAC-SOD-01',
            sodPolicyStatus: 'ZERO_PRIVILEGE_ESCALATION_VERIFIED',
            crossSiteLeakageDetected: false,
          },
        } as never;
      }
      return { data: {} } as never;
    });

    const view = render(<Permissions />);
    const sodButton = await view.findByText(/Kiểm toán Cô lập Phạm vi Kho/i);
    fireEvent.click(sodButton);
    expect(await view.findByText(/ZERO_PRIVILEGE_ESCALATION_VERIFIED/)).toBeTruthy();

    fireEvent.click(view.getByText('Quản trị bảo mật, phạm vi kho & kiểm toán'));
    fireEvent.change(view.getByLabelText('ID người dùng cần kiểm tra bảo mật'), { target: { value: '2' } });
    fireEvent.click(view.getByText('Tra cứu bảo mật'));

    expect(await view.findByText('Đang khóa tạm thời')).toBeTruthy();
    expect((view.getByText('Khóa tài khoản') as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByText('Mở khóa tài khoản') as HTMLButtonElement).disabled).toBe(false);
  });

  it('hiển thị thẻ Hạn ngạch Thuê bao Đa Doanh nghiệp & Xác thực Đa yếu tố', async () => {
    const view = render(<Permissions />);
    expect(await view.findByText('Hạn ngạch Thuê bao Đa Doanh nghiệp & Xác thực Đa yếu tố')).toBeTruthy();
    expect(view.getByText('JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP')).toBeTruthy();
    expect(view.getByText('Khởi tạo Khóa Bảo mật 2 Lớp')).toBeTruthy();
    expect(view.getByText('Xác minh Mã 6 Chữ số')).toBeTruthy();
    expect(view.getByText('Mã QR TOTP')).toBeTruthy();
  });
});
