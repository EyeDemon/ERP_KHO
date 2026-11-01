// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SaasBilling from './SaasBilling';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('SaasBilling Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(cleanup);

  it('renders unified 4-level quota thresholds, ceiling upgrade alert, TPBank HUYNH PHI VAN payment panel, alphanumeric transfer content, and invoice history', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/permissions/saas/subscription-billing') {
        return {
          data: {
            planCode: 'GROWTH',
            subscriptionStatus: 'ACTIVE',
            activeWarehouses: 3,
            maxWarehouses: 3,
            activeUsers: 18,
            maxUsers: 20,
            activeProducts: 420,
            maxProducts: 5000,
            currentMonthOrderCount: 6420,
            monthlyOrderQuota: 10000,
            billingTaxCode: '0318889999',
            vietQrBank: {
              bankCode: 'TPB',
              bankName: 'TPBank',
              bankBin: '970423',
              accountNo: '98418102005',
              accountName: 'HUYNH PHI VAN',
            },
            recentInvoices: [
              {
                invoiceCode: 'INV-VQR-20261030-999',
                planCode: 'GROWTH',
                amountVnd: 1490000,
                status: 'UNPAID',
                transferContent: 'ERPKHO INVVQR20261030999',
                createdAtUtc: '2026-10-30T10:00:00Z',
                paidAtUtc: null,
              },
            ],
          },
        } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice') {
        return {
          data: {
            invoiceCode: 'INV-VQR-20261030-999',
            planCode: body?.planCode ?? 'GROWTH',
            amountVnd: body?.amountVnd ?? 1490000,
            status: 'UNPAID',
            reusedPendingInvoice: true,
            bankBin: '970423',
            accountNo: '98418102005',
            accountName: 'HUYNH PHI VAN',
            qrPayload: `00020101021238550010A000000727012500069704230111984181020050208QRIBFTTA5303704540714900005802VN62210817INVVQR202610309996304ABCD`,
            transferContent: `ERPKHO INVVQR20261030999`,
            expiresAtUtc: '2026-10-30T10:30:00Z',
          },
        } as never;
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    const view = render(
      <MemoryRouter>
        <SaasBilling />
      </MemoryRouter>,
    );

    expect(await screen.findByText('3 / 3 kho')).toBeTruthy();
    expect(screen.getByText('18 / 20 người dùng')).toBeTruthy();
    expect(screen.getByText('420 / 5.000 mặt hàng')).toBeTruthy();
    expect(screen.getByText('Mức sử dụng trong chu kỳ')).toBeTruthy();
    expect(screen.getByText('Bảng gói cước thuê bao SaaS & thanh toán gia hạn VietQR')).toBeTruthy();
    expect(screen.getByTestId('vietqr-bank-display').textContent).toBe('TPBank — NH TMCP Tiên Phong (BIN: 970423)');
    expect(screen.getByTestId('vietqr-account-number').textContent).toBe('98418102005');
    expect(screen.getByTestId('vietqr-account-holder').textContent).toBe('HUYNH PHI VAN');
    expect(screen.getByText('MST: 0318889999')).toBeTruthy();
    expect(screen.getByText('Danh mục mặt hàng')).toBeTruthy();

    // Edit VAT tax code inline
    fireEvent.click(screen.getByTestId('btn-edit-tax-code'));
    fireEvent.change(screen.getByTestId('input-tax-code'), { target: { value: '0319998888' } });
    fireEvent.click(screen.getByTestId('btn-save-tax-code'));
    expect(screen.getByText('MST: 0319998888')).toBeTruthy();

    // Exact 100% semantic label (`Đã đạt hạn mức (100%)`) + Nâng cấp ngay → button at footer + Enterprise recommendation badge
    expect(screen.getByTestId('quota-ceiling-alert-warehouses').textContent).toContain('Đã đạt hạn mức (100%)');
    expect(screen.getByTestId('btn-upgrade-now-warehouses').textContent).toContain('Nâng cấp ngay →');
    expect(screen.getByTestId('plan-recommendation-badge-ENTERPRISE').textContent).toContain('★ Khuyên dùng khi cần mở rộng thêm kho');
    expect(screen.getByTestId('saas-quota-tier-legend').textContent).toContain('Bình thường (<80%)');
    expect(screen.getByTestId('saas-quota-tier-legend').textContent).toContain('Mức 1: Cảnh báo sớm (80–99%)');
    expect(screen.getByTestId('saas-quota-tier-legend').textContent).toContain('Đã đạt hạn mức (100%)');
    expect(screen.getByTestId('saas-quota-tier-legend').textContent).toContain('Mức 2: Đang vượt (101–115%)');
    expect(screen.getByTestId('saas-quota-tier-legend').textContent).toContain('Mức 3: Chế độ chỉ đọc (>115%)');
    expect(screen.getByTestId('vietqr-billing-period').textContent).toContain('Kỳ cước thanh toán:');
    expect(screen.getByTestId('vietqr-auto-polling-indicator').textContent).toContain('Đang tự động chờ xác nhận từ ngân hàng');
    expect(screen.getByTestId('vietqr-edge-case-rules').tagName.toLowerCase()).toBe('details');
    expect(screen.getByTestId('vietqr-edge-case-rules').textContent).toContain('Chuyển sai số tiền hoặc quá hạn? Xem cách xử lý');

    // Progress bars have role="progressbar", aria-valuemax="115", and both 80% and 100% markers
    const orderProgressBar = screen.getByTestId('quota-card-orders').querySelector('[role="progressbar"]');
    expect(orderProgressBar).toBeTruthy();
    expect(orderProgressBar?.getAttribute('aria-valuemax')).toBe('115');
    expect(orderProgressBar?.querySelector('[title="Mốc cảnh báo sớm 80%"]')).toBeTruthy();
    expect(orderProgressBar?.querySelector('[title="Mốc trần hạn mức 100%"]')).toBeTruthy();

    // FREE_TRIAL card hidden when active plan is GROWTH; STARTER downgrade locked because usage (3 kho & 6.420 đơn/tháng) exceeds STARTER limits (1 kho & 1.500 đơn)
    expect(screen.queryByTestId('plan-card-FREE_TRIAL')).toBeNull();
    expect(screen.getByTestId('plan-card-STARTER')).toBeTruthy();
    expect(screen.getByTestId('plan-card-GROWTH')).toBeTruthy();
    expect(screen.getByTestId('plan-card-ENTERPRISE')).toBeTruthy();
    expect(screen.getByTestId('btn-select-plan-GROWTH').textContent).toBe('✓ Gói dịch vụ đang áp dụng');
    expect(screen.getByTestId('btn-select-plan-STARTER').textContent).toBe('Không khả dụng để hạ cấp');
    expect((screen.getByTestId('btn-select-plan-STARTER') as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByTestId('plan-downgrade-warning-STARTER').textContent).toBe(
      'Không khả dụng để hạ cấp: Bạn đang dùng 3 kho & 6.420 đơn/tháng (vượt giới hạn 1 kho & 1.500 đơn của gói Khởi đầu).',
    );
    expect(screen.getByTestId('btn-select-plan-ENTERPRISE').textContent).toBe('Chọn gói Doanh nghiệp');

    // Initial load immediately syncs UNPAID invoice from recentInvoices (contiguous uppercase alphanumeric without space or hyphen) + countdown timer
    expect(screen.getByTestId('vietqr-transfer-content').textContent).toBe('ERPKHOINVVQR20261030999');
    expect(screen.getByTestId('vietqr-countdown-timer').textContent).toContain('Hết hạn sau:');

    // QA webhook simulator button hidden in production / default env
    expect(screen.queryByTestId('btn-confirm-vietqr-webhook')).toBeNull();

    expect(view.container.textContent).not.toContain('(GROWTH)');
    expect(view.container.textContent).not.toContain('(STARTER)');
    expect(view.container.textContent).not.toContain('(ENTERPRISE)');
    expect(view.container.textContent).not.toContain('(SKU)');
    expect(screen.getByTestId('saas-quota-gauge-grid').textContent).toContain('6.420 / 10.000');

    // Copy buttons present
    expect(screen.getByTestId('btn-copy-stk').textContent).toContain('Sao chép STK');
    expect(screen.getByTestId('btn-copy-amount').textContent).toContain('Sao chép số tiền');
    expect(screen.getByTestId('btn-copy-transfer-content').textContent).toContain('Sao chép nội dung');

    fireEvent.click(screen.getByTestId('btn-create-vietqr-invoice'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice',
        expect.objectContaining({
          planCode: 'GROWTH',
          amountVnd: 1490000,
        }),
      );
    });
    const transferContentCode = await screen.findByTestId('vietqr-transfer-content');
    expect(transferContentCode.textContent).toBe('ERPKHOINVVQR20261030999');
    expect(transferContentCode.textContent).not.toContain('-');
    expect(transferContentCode.textContent).not.toContain(' ');
    expect(screen.getByTestId('vietqr-invoice-status-badge').textContent).toBe('Chờ thanh toán');
    expect(screen.getByTestId('vietqr-inline-status-notice')).toBeTruthy();
    expect(screen.getByTestId('saas-invoice-history-table').textContent).toContain('INV-VQR-20261030-999');
    expect(screen.getByTestId('saas-invoice-history-table').textContent).toContain('ERPKHOINVVQR20261030999');
    expect(screen.getByTestId('saas-invoice-history-table').textContent).toMatch(/\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/);
    expect(screen.getByTestId('saas-invoice-history-table').innerHTML).toContain('• Hạn:');
    expect(screen.getByTestId('checkbox-auto-refresh-vietqr')).toBeTruthy();

    fireEvent.click(screen.getByTestId('btn-check-vietqr-payment-status'));

    await waitFor(() => {
      expect(apiClient.get).toHaveBeenCalledWith(
        '/api/v1/permissions/saas/subscription-billing',
      );
    });
    expect(await screen.findByText(/Chưa ghi nhận giao dịch chuyển khoản cho hóa đơn/i)).toBeTruthy();
  });

  it('displays server error banner honestly when SaaS billing API fails', async () => {
    vi.mocked(apiClient.get).mockRejectedValue({
      response: {
        status: 500,
        data: {
          errorCode: 'SAAS_BILLING_UNAVAILABLE',
          message: 'Không thể kết nối dịch vụ quản lý thuê bao SaaS.',
          correlationId: 'corr-saas-500',
        },
      },
    });

    render(
      <MemoryRouter>
        <SaasBilling />
      </MemoryRouter>,
    );

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Không thể kết nối dịch vụ quản lý thuê bao SaaS.');
    expect(alert.textContent).toContain('SAAS_BILLING_UNAVAILABLE');
    expect(alert.textContent).toContain('corr-saas-500');
  });

  it('renders EXPIRED (00:00) neutral badge, overlay with Tạo mã mới button, and PAID confirmation state hiding QR', async () => {
    let invoiceStatus = 'EXPIRED';
    vi.mocked(apiClient.get).mockImplementation(async () => ({
      data: {
        planCode: 'GROWTH',
        subscriptionStatus: 'ACTIVE',
        activeWarehouses: 2,
        maxWarehouses: 3,
        activeUsers: 10,
        maxUsers: 20,
        activeProducts: 400,
        maxProducts: 20000,
        currentMonthOrderCount: 1200,
        monthlyOrderQuota: 10000,
        recentInvoices: [
          {
            invoiceCode: 'INV-VQR-EXPIRED-001',
            planCode: 'GROWTH',
            amountVnd: 1490000,
            status: invoiceStatus,
            transferContent: 'ERPKHOINVVQREXPIRED001',
            createdAtUtc: '2026-10-30T10:00:00Z',
            paidAtUtc: invoiceStatus === 'PAID' ? '2026-10-30T10:05:00Z' : null,
          },
        ],
      },
    } as never));

    vi.mocked(apiClient.post).mockImplementation(async () => ({
      data: {
        invoiceCode: 'INV-VQR-EXPIRED-001',
        planCode: 'GROWTH',
        amountVnd: 1490000,
        status: 'EXPIRED',
        transferContent: 'ERPKHOINVVQREXPIRED001',
      },
    } as never));

    render(
      <MemoryRouter>
        <SaasBilling />
      </MemoryRouter>,
    );

    expect(await screen.findByText('2 / 3 kho')).toBeTruthy();

    // Trigger EXPIRED invoice returned by create-vietqr-invoice
    fireEvent.click(screen.getByTestId('btn-create-vietqr-invoice'));
    expect(await screen.findByTestId('vietqr-expired-overlay')).toBeTruthy();
    expect(screen.getByTestId('vietqr-invoice-status-badge').textContent).toBe('Đã hết hạn (00:00)');
    expect(screen.queryByTestId('vietqr-countdown-timer')).toBeNull();
    expect(screen.getByTestId('vietqr-expired-overlay-label').textContent).toBe('Mã QR đã hết hạn');
    expect(screen.getByTestId('btn-regenerate-expired-vietqr').textContent).toBe('Tạo mã mới');
    expect(screen.getByTestId('vietqr-expired-safe-note').textContent).toBe(
      'Nếu bạn đã chuyển khoản trước khi mã hết hạn, khoản tiền vẫn được hệ thống ghi nhận an toàn.',
    );

    // Transition to PAID when checking payment status
    invoiceStatus = 'PAID';
    fireEvent.click(screen.getByTestId('btn-check-vietqr-payment-status'));
    expect(await screen.findByTestId('vietqr-paid-confirmation-banner')).toBeTruthy();
    expect(screen.getByTestId('vietqr-paid-confirmation-banner').textContent).toContain(
      '✓ Đã nhận thanh toán & kích hoạt thành công Gói Tăng trưởng',
    );
    expect(screen.queryByTestId('vietqr-qr-box')).toBeNull();
  });
});
