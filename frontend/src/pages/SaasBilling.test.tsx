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

  it('renders dynamic 3-tier SaaS quota metrics from API and handles VietQR invoice creation & webhook confirmation', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/permissions/saas/subscription-billing') {
        return {
          data: {
            planCode: 'GROWTH',
            subscriptionStatus: 'ACTIVE',
            activeWarehouses: 2,
            maxWarehouses: 3,
            activeUsers: 18,
            maxUsers: 20,
            activeProducts: 420,
            maxProducts: 5000,
            currentMonthOrderCount: 9500,
            monthlyOrderQuota: 10000,
          },
        } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/permissions/saas/subscription-billing/create-vietqr-invoice') {
        return {
          data: {
            qrPayload: `VIETQR-PAYOS-${body?.planCode ?? 'GROWTH'}-999`,
            transferContent: `ERPKHO2026 ${body?.planCode ?? 'GROWTH'}`,
          },
        } as never;
      }
      if (url === '/api/v1/permissions/saas/subscription-billing/webhook-vietqr') {
        return {
          data: {
            subscriptionStatus: 'ACTIVE',
            message: 'Đã kích hoạt tự động Gói Tăng trưởng qua Webhook PayOS.',
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

    expect(await screen.findByText('2 / 3 kho')).toBeTruthy();
    expect(screen.getByText('18 / 20 người dùng')).toBeTruthy();
    expect(screen.getByText('420 / 5.000 mặt hàng')).toBeTruthy();
    expect(screen.getByText('Bảng Gói cước Thuê bao SaaS & Thanh toán Gia hạn VietQR')).toBeTruthy();
    expect(screen.getByText('Danh mục mặt hàng')).toBeTruthy();
    expect(view.container.textContent).not.toContain('(GROWTH)');
    expect(view.container.textContent).not.toContain('(STARTER)');
    expect(view.container.textContent).not.toContain('(ENTERPRISE)');
    expect(view.container.textContent).not.toContain('(SKU)');
    expect(screen.getByTestId('saas-quota-gauge-grid').textContent).toContain('9.500 / 10.000');

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
    expect(await screen.findByText(/Nội dung CK: ERPKHO2026 GROWTH/i)).toBeTruthy();

    fireEvent.click(screen.getByTestId('btn-confirm-vietqr-webhook'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/permissions/saas/subscription-billing/webhook-vietqr',
        expect.objectContaining({
          planCode: 'GROWTH',
          paidAmountVnd: 1490000,
        }),
      );
    });
    expect(await screen.findByText(/Đã kích hoạt tự động Gói Tăng trưởng qua Webhook PayOS/i)).toBeTruthy();
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
});
