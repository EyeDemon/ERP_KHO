// @vitest-environment jsdom
import { act, cleanup, fireEvent, render as rtlRender, waitFor } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AxiosError } from 'axios';
import apiClient from '../services/apiClient';
import { setCurrentPermissions } from '../services/authorization';
import Permissions from './Permissions';

const render = (ui: ReactElement) => rtlRender(<MemoryRouter>{ui}</MemoryRouter>);

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

  it('hiển thị thẻ Hạn ngạch Thuê bao Đa Doanh nghiệp & Xác thực Đa yếu tố với số liệu động từ API', async () => {
    get.mockImplementation(async url => {
      if (url === '/api/permissions') return { data: catalog } as never;
      if (url === '/api/auth/me') return { data: { permissions: grants } } as never;
      if (url === '/api/v1/permissions/saas/subscription-billing') {
        return {
          data: {
            planName: 'Gói Tăng trưởng Thương mại Điện tử',
            subscriptionStatus: 'ACTIVE',
            activeWarehouses: 2,
            maxWarehouses: 3,
            activeUsers: 11,
            maxUsers: 20,
            activeProducts: 420,
            maxProducts: 5000,
            currentMonthOrderCount: 1280,
            monthlyOrderQuota: 10000,
          },
        } as never;
      }
      return { data: [role] } as never;
    });
    const view = render(<Permissions />);
    expect(await view.findByText('Hạn ngạch Thuê bao Đa Doanh nghiệp & Xác thực Đa yếu tố')).toBeTruthy();
    expect(await view.findByText('Gói Tăng trưởng Thương mại Điện tử')).toBeTruthy();
    expect(view.getByText('2 / 3 kho')).toBeTruthy();
    expect(view.getByText('11 / 20 người dùng')).toBeTruthy();
    expect(view.getByText('420 / 5.000 mặt hàng')).toBeTruthy();
    expect(view.getByText(/Đơn hàng tháng: 1[.,]?280 \/ 10[.,]?000/)).toBeTruthy();
    expect(view.getByText('JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP')).toBeTruthy();
    expect(view.getByText('Khởi tạo Khóa Bảo mật 2 Lớp')).toBeTruthy();
    expect(view.getByText('Xác minh Mã 6 Chữ số')).toBeTruthy();
    expect(view.getByText('Mã QR TOTP')).toBeTruthy();
  });

  it('hiển thị Quản trị Gói cước Thuê bao SaaS 3 Nấc Hạn mức & Thanh toán Tự động VietQR', async () => {
    post.mockResolvedValue({ data: {} } as never);
    const view = render(<Permissions />);
    expect(await view.findByText('Quản trị Gói cước Thuê bao SaaS 3 Nấc Hạn mức & Thanh toán Tự động VietQR')).toBeTruthy();
    expect(view.getByText('Gói Khởi đầu — 490.000 đ/tháng')).toBeTruthy();
    expect(view.getByText('Gói Tăng trưởng — 1.490.000 đ/tháng')).toBeTruthy();
    expect(view.getByText('Gói Doanh nghiệp — 3.490.000 đ/tháng')).toBeTruthy();
    expect(view.getByText('14 ngày dùng thử miễn phí • 7 ngày ân hạn gia hạn')).toBeTruthy();
    expect(view.getByText('Nấc 1 (80–100%): Cảnh báo sớm')).toBeTruthy();
    expect(view.getByText('Nấc 2 (100–115%): Đệm vượt hạn mức 15%')).toBeTruthy();
    expect(view.getByText('Nấc 3 (>115%): Chế độ Chỉ đọc (Bảo toàn 100% tra cứu & xuất Excel)')).toBeTruthy();
    expect(view.getByText('PayOS / SePay')).toBeTruthy();
    expect(view.getByText('Mã VietQR')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Tạo Mã Thanh Toán Gia Hạn Tự Động VietQR' }));
    expect(await view.findByText(/Đã khởi tạo mã thanh toán VietQR tự động/)).toBeTruthy();

    const webhookBtn = view.getByTestId('btn-confirm-vietqr-webhook');
    expect(webhookBtn.textContent).toContain('Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)');
    fireEvent.click(webhookBtn);
    expect(await view.findByText(/Trạng thái gói cước: Đang hoạt động/)).toBeTruthy();
    expect(view.getByText(/chuyển sang trạng thái Đang hoạt động/)).toBeTruthy();
    expect(view.container.textContent).not.toContain('ACTIVE');
    expect(post).toHaveBeenCalledWith(
      '/api/permissions/saas/subscription-billing/webhook-vietqr',
      expect.objectContaining({
        planCode: 'GROWTH',
        paidAmountVnd: 1490000,
      }),
    );

    post.mockResolvedValueOnce({
      data: {
        id: 501,
        supportAgentName: 'Chuyên viên Kỹ thuật ERP KHO',
        ticketReference: 'ZALO-SUP-2026-001',
        expiresAtUtc: '2026-10-22T12:30:00Z',
      },
    } as never);
    fireEvent.click(view.getByTestId('btn-grant-support-impersonation'));
    expect(await view.findByText(/Đã cấp phiên hỗ trợ kỹ thuật tạm thời \(#501, TTL 30 phút\)/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith(
      '/api/v1/security/support-impersonation/sessions',
      expect.objectContaining({
        ticketReference: 'ZALO-SUP-2026-001',
        ttlMinutes: 30,
      }),
    );

    get.mockResolvedValueOnce({
      data: [
        {
          id: 501,
          targetUserId: 1,
          supportAgentName: 'Chuyên viên Kỹ thuật ERP KHO',
          ticketReference: 'ZALO-SUP-2026-001',
          status: 'ACTIVE',
          expiresAtUtc: '2026-10-22T12:30:00Z',
        },
      ],
    } as never);
    fireEvent.click(view.getByTestId('btn-load-support-impersonation'));
    await waitFor(() =>
      expect(get).toHaveBeenCalledWith('/api/v1/security/support-impersonation/sessions'),
    );

    post.mockResolvedValueOnce({ data: {} } as never);
    fireEvent.click(view.getByTestId('btn-revoke-support-501'));
    expect(await view.findByText(/Đã thu hồi tức thì phiên hỗ trợ kỹ thuật #501/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith(
      '/api/v1/security/support-impersonation/501/revoke',
      expect.objectContaining({ reason: 'Chủ shop thu hồi phiên hỗ trợ kỹ thuật' }),
    );
  });
});
