// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
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
            skuValuations: [
              {
                productId: 10,
                productCode: 'QA-E2E-001',
                productName: 'Cà Phê Rang Xay Đặc Biệt',
                warehouseName: 'Kho Trung tâm WH01',
                unitName: 'Gói',
                onHandQty: 100,
                availableQty: 95,
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

    const view = render(<CostValuation />);

    expect(await screen.findByText('85.000.000 đ')).toBeTruthy();
    expect(screen.getByText('Giá vốn hàng bán bình quân gia quyền')).toBeTruthy();
    expect(screen.getByText('Bảng Định giá Tồn kho theo Giá vốn Bình quân Gia quyền • 1 mặt hàng')).toBeTruthy();
    expect(view.container.textContent).not.toContain('(COGS)');
    expect(view.container.textContent).not.toContain('(MovingAverageCost)');
    expect(view.container.textContent).not.toContain('0 SKU');
    expect(screen.getByText('51.000.000 đ')).toBeTruthy();
    expect(screen.getByText('24.500.000 đ')).toBeTruthy();
    expect(screen.getByText('28,8%')).toBeTruthy();
    expect(screen.getByText('QA-E2E-001')).toBeTruthy();
    expect(screen.getByText('Kho Trung tâm (WH01)')).toBeTruthy();
    expect(screen.getByRole('columnheader', { name: 'Đơn vị tính' })).toBeTruthy();
    expect(view.container.textContent).not.toContain('ĐVT');
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

  it('falls back to /api/v1/inventory-reconciliation/valuation when owner-gross-profit returns empty valuation items', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/reports/owner-gross-profit') {
        return {
          data: {
            grossRevenue: 10000000,
            movingAverageCogs: 6000000,
            netPocketProfit: 4000000,
            netMarginPct: 40,
          },
        } as never;
      }
      if (url === '/api/v1/inventory-reconciliation/valuation') {
        return {
          data: {
            items: [
              {
                productId: 2,
                productCode: 'QA-LOT-002',
                productName: 'Sữa Tươi Tiệt Trùng Lô Date',
                warehouseName: 'Kho Trung tâm WH01',
                unitName: 'Thùng',
                onHandQty: 50,
                availableQty: 48,
                movingAverageUnitCost: 200000,
                totalInventoryValue: 10000000,
              },
            ],
          },
        } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    render(<CostValuation />);

    expect(await screen.findByText('QA-LOT-002')).toBeTruthy();
    expect(screen.getByText('Bảng Định giá Tồn kho theo Giá vốn Bình quân Gia quyền • 1 mặt hàng')).toBeTruthy();
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

  it('renders Decree 70/2025/NĐ-CP 1.5% withheld tax, 3,000 VND fixed infrastructure fee, and net profit after tax', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/reports/owner-gross-profit') {
        return {
          data: {
            grossRevenue: 2000000,
            movingAverageCogs: 800000,
            netPocketProfit: 1048000,
            netMarginPct: 52.4,
            withheldVatAmount: 20000,
            withheldPitAmount: 10000,
            withheldTaxTotalAmount: 30000,
            fixedInfrastructureFeeTotal: 3000,
            netProfitAfterWithheldTax: 1015000,
            costHiddenByRbac: false,
            skuValuations: [],
          },
        } as never;
      }
      if (url === '/api/v1/inventory') {
        return { data: { items: [] } } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    render(<CostValuation />);

    expect(
      await screen.findByText(
        /Báo cáo Đối soát Lợi nhuận Gộp, Phí Vận chuyển\/Sàn & Khấu trừ Thuế Sàn TMĐT/i,
      ),
    ).toBeTruthy();
    expect(
      screen.getByText('Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)'),
    ).toBeTruthy();
    expect(screen.getByText('Phí Hạ tầng Đơn hàng Cố định (3.000đ/đơn)')).toBeTruthy();
    expect(screen.getByText('Lợi nhuận Ròng Sau Thuế Sàn & Phí')).toBeTruthy();
    expect(screen.getByText('30.000 đ')).toBeTruthy();
    expect(screen.getByText('3.000 đ')).toBeTruthy();
    expect(screen.getByText('1.015.000 đ')).toBeTruthy();
  });

  it('separates /misa-exports and /cost-valuation into dedicated single-function workbenches without dual-purpose toggle tabs (WAVE-12)', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        grossRevenue: 54000000,
        movingAverageCogs: 8350000,
        netPocketProfit: 43505000,
        netMarginPct: 80.56,
        withheldTaxTotalAmount: 810000,
        fixedInfrastructureFeeTotal: 6000,
        netProfitAfterWithheldTax: 42689000,
        costHiddenByRbac: false,
        skuValuations: [],
      },
    } as never);

    const misaView = render(
      <MemoryRouter initialEntries={['/misa-exports']}>
        <CostValuation />
      </MemoryRouter>,
    );

    expect(await misaView.findByTestId('misa-export-active-banner')).toBeTruthy();
    expect(misaView.getByTestId('misa-export-active-banner').textContent).not.toContain('(/misa-exports)');
    expect(misaView.queryByTestId('cost-valuation-mode-tabs')).toBeNull();
    expect(misaView.queryByTestId('wac-profit-reconciliation-grid')).toBeNull();
    expect(misaView.getByTestId('btn-export-misa-xlsx')).toBeTruthy();
    cleanup();

    const wacView = render(
      <MemoryRouter initialEntries={['/cost-valuation']}>
        <CostValuation />
      </MemoryRouter>,
    );
    expect(await wacView.findByTestId('wac-profit-reconciliation-grid')).toBeTruthy();
    expect(wacView.queryByTestId('btn-export-misa-xlsx')).toBeNull();
    expect(wacView.getByText('45.650.000 đ')).toBeTruthy();
    expect(wacView.getByText('84,54%')).toBeTruthy();
    expect(wacView.getByText('42.689.000 đ')).toBeTruthy();
    expect(wacView.queryByText('QA-SERIAL-001')).toBeNull();
  });

  it('renders pure WAC inventory KPI grid from live API data without hardcoded fallback rows (FE-FIX-4)', async () => {
    const uaSpy = vi.spyOn(window.navigator, 'userAgent', 'get').mockReturnValue('Mozilla/5.0 Chrome/130.0.0.0');
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        grossRevenue: 54000000,
        movingAverageCogs: 8350000,
        costHiddenByRbac: false,
        skuValuations: [
          { productId: 101, productCode: 'QA-SERIAL-001', productName: 'Thiết bị định vị GPS Hợp chuẩn', warehouseName: 'Kho Trung tâm WH01', unitName: 'Cái', onHandQty: 120, availableQty: 120, movingAverageUnitCost: 450000, totalInventoryValue: 54000000 },
          { productId: 102, productCode: 'QA-LOT-002', productName: 'Bộ Kit Quà tặng Doanh nghiệp', warehouseName: 'Kho Trung tâm WH01', unitName: 'Bộ', onHandQty: 20, availableQty: 20, movingAverageUnitCost: 500000, totalInventoryValue: 10000000 },
          { productId: 103, productCode: 'QA-LOT-002-BOX', productName: 'Vỏ hộp quà và Phụ kiện đóng gói', warehouseName: 'Kho Trung tâm WH01', unitName: 'Cái', onHandQty: 250, availableQty: 250, movingAverageUnitCost: 50000, totalInventoryValue: 12500000 },
        ],
      },
    } as never);

    const view = render(
      <MemoryRouter initialEntries={['/cost-valuation']}>
        <CostValuation />
      </MemoryRouter>,
    );

    const kpiGrid = await view.findByTestId('wac-inventory-kpi-grid');
    expect(kpiGrid.textContent).toContain('3 mặt hàng (Theo đơn vị tính chuẩn)');
    expect(kpiGrid.textContent).toContain('Mặt hàng giá trị tồn lớn nhất');
    expect(kpiGrid.textContent).toContain('Cập nhật giá vốn gần nhất');
    expect(kpiGrid.textContent).toContain('76.500.000 đ');
    expect(kpiGrid.textContent).toContain('QA-SERIAL-001');
    uaSpy.mockRestore();
  });
});
