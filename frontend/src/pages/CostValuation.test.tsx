// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import CostValuation from './CostValuation';
import apiClient from '../services/apiClient';
import * as authorization from '../services/authorization';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

vi.mock('../services/authorization', async () => {
  const actual = await vi.importActual<typeof import('../services/authorization')>('../services/authorization');
  return {
    ...actual,
    usePermission: vi.fn(() => true),
  };
});

describe('CostValuation Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(authorization.usePermission).mockReturnValue(true);
  });

  afterEach(cleanup);

  it('loads moving average cost valuation & gross profit from API, exports MISA xlsx, and issues draft e-invoice ND70/2025', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/reports/owner-gross-profit') {
        return {
          data: {
            grossRevenue: 85000000,
            movingAverageCogs: 51000000,
            netPocketProfit: 24500000,
            netMarginPct: 28.8,
            costHiddenByRbac: false,
            items: [
              {
                productId: 10,
                productCode: 'SKU-VAL-01',
                productName: 'Cà Phê Rang Xay Đặc Biệt',
                onHandQty: 100,
                movingAverageUnitCost: 120000,
                totalInventoryValue: 12000000,
              },
            ],
          },
        } as never;
      }
      if (url === '/api/v1/export-receipts/misa-excel-export?format=xlsx') {
        return { data: new Uint8Array([80, 75, 3, 4]) } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/export-receipts/einvoice-nd70-draft') {
        return {
          data: {
            invoiceDraftCode: 'EINV-ND70-2026-009',
            buyerTaxCode: body?.buyerTaxCode,
          },
        } as never;
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    render(<CostValuation />);

    expect(await screen.findByText('85.000.000 đ')).toBeTruthy();
    expect(screen.getByText('51.000.000 đ')).toBeTruthy();
    expect(screen.getByText('24.500.000 đ')).toBeTruthy();
    expect(screen.getByText('28,8%')).toBeTruthy();
    expect(screen.getByText('SKU-VAL-01')).toBeTruthy();
    expect(screen.getByText('120.000 đ')).toBeTruthy();
    expect(screen.getByText('12.000.000 đ')).toBeTruthy();

    fireEvent.click(screen.getByTestId('btn-export-misa-xlsx'));
    await waitFor(() => {
      expect(apiClient.get).toHaveBeenCalledWith(
        '/api/v1/export-receipts/misa-excel-export?format=xlsx',
        expect.objectContaining({ responseType: 'blob' }),
      );
    });

    fireEvent.change(screen.getByLabelText('Mã số thuế người mua HĐĐT'), { target: { value: '0312345678' } });
    fireEvent.change(screen.getByLabelText('Tên đơn vị mua hàng HĐĐT'), { target: { value: 'Công ty TNHH Mua Hàng' } });
    fireEvent.click(screen.getByTestId('btn-issue-einvoice-nd70'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/export-receipts/einvoice-nd70-draft',
        expect.objectContaining({
          buyerTaxCode: '0312345678',
          buyerName: 'Công ty TNHH Mua Hàng',
          decreeStandard: 'ND70_2025_TT32_2025',
        }),
      );
    });
    expect(await screen.findByText(/EINV-ND70-2026-009/i)).toBeTruthy();
  });

  it('masks COGS & profit when RBAC returns 403 and displays error banner when e-invoice API fails with 500', async () => {
    vi.mocked(apiClient.get).mockRejectedValue({
      response: { status: 403 },
    });
    vi.mocked(apiClient.post).mockRejectedValue({
      response: {
        status: 500,
        data: {
          errorCode: 'EINVOICE_GATEWAY_ERROR',
          message: 'Không thể kết nối cổng HĐĐT Tổng cục Thuế.',
          correlationId: 'corr-einv-500',
        },
      },
    });

    render(<CostValuation />);

    expect(await screen.findByTestId('rbac-cost-hidden-banner')).toBeTruthy();
    expect(screen.getAllByText('Ẩn theo RBAC').length).toBeGreaterThanOrEqual(3);

    fireEvent.click(screen.getByTestId('btn-issue-einvoice-nd70'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Không thể kết nối cổng HĐĐT Tổng cục Thuế.');
    expect(alert.textContent).toContain('EINVOICE_GATEWAY_ERROR');
    expect(alert.textContent).toContain('corr-einv-500');
  });
});
