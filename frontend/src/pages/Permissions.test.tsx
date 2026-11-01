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
    get.mockImplementation(async url => ({ data: url === '/api/v1/permissions' ? catalog : url === '/api/v1/auth/me' ? { permissions: grants } : [role] }) as never);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it('hiển thị danh mục bằng tiếng Việt cho người chỉ có quyền đọc', async () => {
    setCurrentPermissions(['permission.read']);
    const view = render(<Permissions />);
    expect(await view.findByRole('table', { name: 'Danh mục quyền truy cập' })).toBeTruthy();
    expect(view.container.textContent).not.toContain('product_category.manage');
    expect(view.queryByText('Cấp quyền')).toBeNull();
    expect(get).not.toHaveBeenCalledWith('/api/v1/permissions/roles');
  });

  it('double-click chỉ gửi một mutation với token của role và refresh quyền sau thành công', async () => {
    let finish!: (value: unknown) => void;
    post.mockReturnValue(new Promise(resolve => { finish = resolve; }) as never);
    const view = render(<Permissions />);
    const button = await view.findByText('Cấp quyền');
    fireEvent.click(button); fireEvent.click(button);
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/api/v1/permissions/roles/7/grants', {
      permissionCode: 'product_category.manage', rowVersion: role.rowVersion,
    }, expect.objectContaining({ headers: expect.any(Object) }));
    expect((view.getByText('Đang lưu...') as HTMLButtonElement).disabled).toBe(true);
    await act(async () => finish({ data: { changed: true } }));
    expect(await view.findByText('Đã cập nhật quyền truy cập.')).toBeTruthy();
    expect(get).toHaveBeenCalledWith('/api/v1/auth/me');
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
      if (url === '/api/v1/permissions') return { data: catalog } as never;
      if (url === '/api/v1/auth/me') return { data: { permissions: grants } } as never;
      if (url === '/api/v1/users/2/security') {
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
      if (url === '/api/v1/user-warehouse-access/verify-sod-isolation') {
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

    fireEvent.click(view.getByText('Cấu trúc vai trò & Phạm vi kho'));
    fireEvent.change(view.getByLabelText('ID người dùng cần kiểm tra bảo mật'), { target: { value: '2' } });
    fireEvent.click(view.getByText('Tra cứu bảo mật'));

    expect(await view.findByText('Đang khóa tạm thời')).toBeTruthy();
    expect((view.getByText('Khóa tài khoản') as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByText('Mở khóa tài khoản') as HTMLButtonElement).disabled).toBe(false);
  });

  it('tách 4 tab sạch không emoji, ẩn KPI trên tab 2FA/Hỗ trợ và thực thi vòng đời TOTP 3 giai đoạn (IDLE -> ENROLLING -> VERIFIED)', async () => {
    get.mockImplementation(async url => {
      if (url === '/api/v1/permissions') return { data: catalog } as never;
      if (url === '/api/v1/auth/me') return { data: { permissions: grants } } as never;
      if (url === '/api/v1/permissions/saas/subscription-billing') {
        return {
          data: {
            planCode: 'GROWTH',
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

    post.mockImplementation(async (url, body) => {
      if (url === '/api/v1/account-security/1/totp/setup') {
        return {
          data: {
            otpauthUri: 'otpauth://totp/ERPKHO:user-1?secret=K5X2M9P4Q7R8T3V6W2Y4Z6A8C3E5G7H9&issuer=ERPKHO',
            status: 'PENDING_OTP',
            recoveryCodes: [],
          },
        } as never;
      }
      if (url === '/api/v1/account-security/1/totp/reveal-secret') {
        return {
          data: {
            secret: 'K5X2M9P4Q7R8T3V6W2Y4Z6A8C3E5G7H9',
          },
        } as never;
      }
      if (url === '/api/v1/account-security/1/totp/verify') {
        const reqBody = (body ?? {}) as { code?: string };
        if (reqBody.code !== '654321') {
          const invalidErr = new AxiosError('Invalid TOTP code');
          invalidErr.response = {
            status: 400,
            data: {
              verified: false,
              code: 'INVALID_TOTP_CODE',
              errorCode: 'INVALID_TOTP_CODE',
              message: 'Mã xác thực 6 chữ số không chính xác hoặc đã hết hạn. Vui lòng kiểm tra lại trên ứng dụng Authenticator.',
            },
          } as never;
          throw invalidErr;
        }
        return {
          data: {
            verified: true,
            status: 'PENDING_RECOVERY_ACK',
            recoveryCodes: [
              'A1B2-C3D4',
              'E5F6-G7H8',
              '1122-3344',
              '5566-7788',
              '99AA-BBCC',
              'DDEE-FF00',
              '1357-2468',
              '8642-7531',
            ],
          },
        } as never;
      }
      if (url === '/api/v1/account-security/1/totp/confirm-activation') {
        return {
          data: {
            verified: true,
            status: 'ACTIVE',
          },
        } as never;
      }
      if (url === '/api/v1/account-security/1/totp/regenerate-recovery-codes') {
        return {
          data: {
            verified: true,
            status: 'ACTIVE',
            recoveryCodes: [
              'NEW1-1111',
              'NEW2-2222',
              'NEW3-3333',
              'NEW4-4444',
              'NEW5-5555',
              'NEW6-6666',
              'NEW7-7777',
              'NEW8-8888',
            ],
          },
        } as never;
      }
      return { data: {} } as never;
    });

    const view = render(<Permissions />);
    expect(await view.findByRole('tab', { name: 'Tổng quan & Ma trận phân quyền' })).toBeTruthy();
    expect(view.getByRole('tab', { name: 'Cấu trúc vai trò & Phạm vi kho' })).toBeTruthy();
    expect(view.getByRole('tab', { name: 'Xác thực 2 lớp' })).toBeTruthy();
    expect(view.getByRole('tab', { name: 'Phiên hỗ trợ kỹ thuật' })).toBeTruthy();

    // KPI cards and compact 1-line SaaS summary strip visible on Tab 1
    expect(view.getByText('Quyền trong danh mục')).toBeTruthy();
    expect(view.getByText('Vai trò đang hiển thị')).toBeTruthy();
    expect(view.getByText('Lượt cấp quyền hiện tại')).toBeTruthy();
    expect(await view.findByText('Tóm tắt gói dịch vụ thuê bao hiện tại')).toBeTruthy();
    expect(view.getByText('Gói Tăng trưởng Thương mại Điện tử')).toBeTruthy();
    expect(view.getByText('2 / 3 kho')).toBeTruthy();
    expect(view.getByText('11 / 20 người dùng')).toBeTruthy();
    expect(view.getByText('Đơn hàng tháng: 1.280 / 10.000')).toBeTruthy();
    expect(view.getByText('420 / 5.000 mặt hàng')).toBeTruthy();
    expect(view.container.textContent).not.toContain('Cô lập dữ liệu đa thuê bao');
    const billingLink = view.getByTestId('link-open-saas-billing');
    expect(billingLink.getAttribute('href')).toBe('/saas-billing');
    expect(billingLink.textContent).toContain('Mở trang Quản trị Gói dịch vụ & Thanh toán VietQR →');
    expect(view.queryByTestId('btn-confirm-vietqr-webhook')).toBeNull();

    // Switch to Tab 3: Xác thực 2 lớp -> KPI cards AND SaaS summary card are hidden
    fireEvent.click(view.getByRole('tab', { name: 'Xác thực 2 lớp' }));
    expect(view.queryByText('Quyền trong danh mục')).toBeNull();
    expect(view.queryByText('Vai trò đang hiển thị')).toBeNull();
    expect(view.queryByText('Lượt cấp quyền hiện tại')).toBeNull();
    expect(view.queryByText('Tóm tắt gói dịch vụ thuê bao hiện tại')).toBeNull();
    expect(view.getByTestId('totp-admin-policy-banner').textContent).toContain('Chính sách bắt buộc đối với tài khoản Quản trị viên');

    // Stage 1 (IDLE): 1-column sequential layout, 3-step guide visible, NO secret/QR/recovery codes shown before enrollment
    expect(view.getByTestId('totp-3step-guide').textContent).toContain('1. Quét mã QR bằng ứng dụng Google / Microsoft Authenticator');
    expect(view.getByTestId('totp-3step-guide').textContent).toContain('2. Nhập mã 6 chữ số từ ứng dụng để xác minh');
    expect(view.getByTestId('totp-3step-guide').textContent).toContain('3. Lưu 8 mã khôi phục dự phòng (dùng khi mất điện thoại)');
    expect(view.getByTestId('totp-idle-state').textContent).toContain(
      'Chưa kích hoạt xác thực 2 lớp — Bấm khởi tạo để sinh mã QR và khóa cá nhân hóa',
    );
    expect(view.queryByTestId('totp-secret-code')).toBeNull();
    expect(view.queryByTestId('totp-recovery-codes')).toBeNull();
    expect(view.queryByText('Mã QR TOTP')).toBeNull();

    // Stage 2 (ENROLLING): Clicking setup generates dynamic secret (default masked, NO data-raw-secret in DOM!), shows QR, and only Step 1 has "Đang thực hiện"
    fireEvent.click(view.getByTestId('btn-setup-totp'));
    expect(await view.findByTestId('totp-enrolling-secret-box')).toBeTruthy();
    expect(view.getByTestId('totp-secret-code').textContent).toBe('•••• •••• •••• •••• •••• •••• •••• ••••');
    expect(view.getByTestId('totp-secret-code').getAttribute('data-raw-secret')).toBeNull();
    expect(view.getByTestId('totp-step-badge-1').textContent).toBe('Đang thực hiện');
    expect(view.getByTestId('totp-step-badge-2').textContent).toBe('Bước tiếp theo');
    expect(view.getByTestId('toggle-totp-secret-visibility').textContent).toBe('Hiện khóa nhập tay');
    fireEvent.click(view.getByTestId('toggle-totp-secret-visibility'));
    expect(await view.findByText('K5X2 M9P4 Q7R8 T3V6 W2Y4 Z6A8 C3E5 G7H9')).toBeTruthy();
    expect(view.getByTestId('totp-secret-code').getAttribute('data-raw-secret')).toBeNull();
    expect(view.getByTestId('toggle-totp-secret-visibility').textContent).toBe('Ẩn khóa nhập tay');
    expect(view.getByTestId('btn-copy-totp-secret').textContent).toBe('Sao chép khóa');
    expect(view.getByText('Mã QR TOTP')).toBeTruthy();
    expect(view.queryByTestId('totp-recovery-codes')).toBeNull();
    expect(post).toHaveBeenCalledWith('/api/v1/account-security/1/totp/setup');

    // Negative Path in Stage 2: Entering wrong 6-digit OTP ('000000') transitions Step 1 -> Đã hoàn tất and Step 2 -> Đang thực hiện, returns 400, and stays in ENROLLING
    fireEvent.change(view.getByLabelText('Mã xác thực 6 chữ số'), { target: { value: '000000' } });
    expect(view.getByTestId('totp-step-badge-1').textContent).toBe('Đã hoàn tất');
    expect(view.getByTestId('totp-step-badge-2').textContent).toBe('Đang thực hiện');
    fireEvent.click(view.getByTestId('btn-verify-totp'));
    expect(await view.findByRole('alert')).toBeTruthy();
    expect(view.getByTestId('totp-enrolling-secret-box')).toBeTruthy();
    expect(view.queryByTestId('totp-verified-banner')).toBeNull();
    expect(view.queryByTestId('totp-recovery-codes')).toBeNull();

    // Stage 3 (RECOVERY_CODES_PENDING -> VERIFIED): Verifying valid 6-digit OTP ('654321') hides secret & QR, shows pending banner (not verified yet!), and masks last 4 chars of 8 recovery codes by default
    fireEvent.change(view.getByLabelText('Mã xác thực 6 chữ số'), { target: { value: '654321' } });
    fireEvent.click(view.getByTestId('btn-verify-totp'));
    expect(await view.findByTestId('totp-pending-recovery-banner')).toBeTruthy();
    expect(view.getByTestId('totp-pending-recovery-banner').textContent).toContain(
      '⚠️ Bạn đã xác minh mã OTP 6 chữ số thành công. Vui lòng sao chép hoặc tải 8 mã khôi phục dự phòng và bấm Hoàn tất để kích hoạt xác thực 2 lớp.',
    );
    expect(view.queryByTestId('totp-verified-banner')).toBeNull();
    expect(view.queryByTestId('totp-enrolling-secret-box')).toBeNull();
    expect(view.queryByTestId('totp-secret-code')).toBeNull();
    expect(view.queryByText('Mã QR TOTP')).toBeNull();
    expect(view.getByTestId('totp-recovery-codes-panel').textContent).toContain(
      'Mỗi mã định dạng XXXX-XXXX chỉ dùng 1 lần để đăng nhập khẩn cấp khi mất điện thoại (mặc định ẩn 4 ký tự cuối; hãy sao chép hoặc tải tệp .txt trước khi hoàn tất).',
    );
    expect(view.getByTestId('totp-recovery-codes')).toBeTruthy();
    expect(view.getByTestId('totp-recovery-codes').querySelectorAll('code').length).toBe(8);
    expect(view.getByTestId('totp-recovery-codes').textContent).toContain('A1B2-••••');
    fireEvent.click(view.getByTestId('btn-toggle-recovery-codes-mask'));
    expect(view.getByTestId('totp-recovery-codes').textContent).toContain('A1B2-C3D4');
    expect(view.getByTestId('btn-copy-recovery-codes').textContent).toContain('Sao chép 8 mã khôi phục');
    expect(view.getByTestId('btn-download-recovery-codes').textContent).toContain('Tải tệp .txt');
    expect(post).toHaveBeenCalledWith('/api/v1/account-security/1/totp/verify', expect.objectContaining({ code: '654321' }));

    // Confirm saving 8 recovery codes to complete enrollment -> transitions to VERIFIED
    const completeBtn = view.getByTestId('btn-complete-totp-enrollment') as HTMLButtonElement;
    expect(completeBtn.disabled).toBe(true);
    fireEvent.click(view.getByTestId('checkbox-confirm-recovery-saved'));
    expect(completeBtn.disabled).toBe(false);
    fireEvent.click(completeBtn);
    expect(await view.findByTestId('totp-verified-banner')).toBeTruthy();
    expect(view.getByTestId('totp-verified-banner').textContent).toContain(
      'Đã bật xác thực 2 lớp. Khóa cấu hình và mã khôi phục đã được ẩn an toàn.',
    );
    expect(view.queryByTestId('totp-recovery-codes')).toBeNull();
    expect(post).toHaveBeenCalledWith('/api/v1/account-security/1/totp/confirm-activation');

    // Regenerate recovery codes in VERIFIED state
    fireEvent.change(view.getByTestId('input-regenerate-recovery-otp'), { target: { value: '654321' } });
    fireEvent.click(view.getByTestId('btn-regenerate-recovery-codes'));
    expect(await view.findByTestId('totp-recovery-codes')).toBeTruthy();
    expect(view.getByTestId('totp-recovery-codes').textContent).toContain('NEW1-••••');
  });

  it('quản lý phiên hỗ trợ kỹ thuật tạm thời có kiểm soát (30 phút) trên tab Phiên hỗ trợ kỹ thuật', async () => {
    const view = render(<Permissions />);
    await view.findByText('Cấp quyền');
    fireEvent.click(view.getByRole('tab', { name: 'Phiên hỗ trợ kỹ thuật' }));

    // KPI cards remain hidden on Support tab
    expect(view.queryByText('Quyền trong danh mục')).toBeNull();

    post.mockResolvedValueOnce({
      data: {
        id: 501,
        supportAgentName: 'Chuyên viên Kỹ thuật ERP KHO',
        ticketReference: 'ZALO-SUP-2026-001',
        expiresAtUtc: '2026-10-22T12:30:00Z',
      },
    } as never);
    fireEvent.click(view.getByTestId('btn-grant-support-impersonation'));
    expect(await view.findByText(/Đã cấp phiên hỗ trợ kỹ thuật tạm thời \(#501, hiệu lực 30 phút\)/)).toBeTruthy();
    expect(post).toHaveBeenCalledWith(
      '/api/v1/security/support-impersonation/grant',
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
