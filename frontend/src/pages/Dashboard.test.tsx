// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Dashboard from './Dashboard';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

describe('Dashboard production UI & Wave-45 shop owner analytics', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    get.mockImplementation(async (url) => {
      if (url === '/api/Reports/operational-dashboard') {
        return {
          data: {
            warehouseCount: 2,
            productCount: 6,
            inventoryBucketCount: 8,
            invalidStockCount: 0,
            openTaskCount: 3,
            openInboundCount: 1,
            openPutawayCount: 1,
            openOutboundCount: 1,
            openTransferCount: 0,
            recentActivities: [],
          },
        } as never;
      }
      if (url === '/api/Reports/multi-warehouse-control-tower') {
        return {
          data: {
            totalWarehouses: 2,
            healthyWarehousesCount: 2,
            attentionWarehousesCount: 0,
            warehouses: [],
            taskEngineQueues: [],
          },
        } as never;
      }
      if (url === '/api/v1/reports/owner-gross-profit') {
        return {
          data: {
            grossRevenue: 85000000,
            movingAverageCogs: 51000000,
            netPocketProfit: 24500000,
            netMarginPct: 28.8,
            costHiddenByRbac: false,
          },
        } as never;
      }
      if (url === '/api/v1/reports/cod-shipping-reconciliation') {
        return {
          data: {
            totalExpectedCod: 19500000,
            totalActualCod: 18200000,
            discrepancyCount: 2,
          },
        } as never;
      }
      return { data: [] } as never;
    });
    post.mockImplementation(async (url: string) => {
      if (url === '/api/v1/reports/cod-reconciliation/verify') {
        return {
          data: {
            totalShipments: 25,
            matchedShipments: 23,
            discrepancyCount: 2,
            matchRatePct: 92,
          },
        } as never;
      }
      return { data: {} } as never;
    });
  });

  afterEach(cleanup);

  it('renders shop owner gross profit report, 4 financial KPIs, 5-step onboarding badges & quick actions, and executes marketplace sync & 3-way COD reconciliation', async () => {
    const view = render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    );

    expect(
      await view.findByText(
        'Báo cáo Lợi nhuận Gộp Ước tính Chủ Shop, Đối soát Tiền Thu Hộ (COD) & Đồng bộ Tồn kho Đa Sàn',
      ),
    ).toBeTruthy();

    expect(await view.findByText('85.000.000 đ')).toBeTruthy();
    expect(view.getByText('Giá vốn hàng bán bình quân')).toBeTruthy();
    expect(view.container.textContent).not.toContain('(COGS)');
    expect(view.getByText('51.000.000 đ')).toBeTruthy();
    expect(view.getByText('24.500.000 đ • Biên lãi gộp 28,8%')).toBeTruthy();
    expect(view.getByText('19.500.000 đ • 2 cảnh báo lệch phí/chậm hoàn')).toBeTruthy();
    expect(view.getByText('Thực nhận từ ĐVVC: 18.200.000 đ • Đối soát 3 chiều')).toBeTruthy();
    expect(view.container.textContent).not.toContain('148.500.000');
    expect(view.container.textContent).not.toContain('36.800.000');

    expect(view.getByText('Bước 1: Khởi tạo sơ đồ kệ mẫu (3 giây)')).toBeTruthy();
    expect(view.getByText('Bước 2: Nhập danh mục sản phẩm chuẩn')).toBeTruthy();
    expect(view.getByText('Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn')).toBeTruthy();
    expect(view.getByText('Bước 4: Liên kết gian hàng Shopee / TikTok Shop')).toBeTruthy();
    expect(view.getByText('Bước 5: In gộp tem nhiệt & xuất đơn đầu tiên')).toBeTruthy();

    // Quick action links replace duplicate tenant provisioning inputs on Dashboard
    expect(view.getByTestId('quick-action-cod-reconciliation')).toBeTruthy();
    expect(view.getByTestId('quick-action-onboarding-wizard')).toBeTruthy();
    expect(view.getByTestId('quick-action-handover-manifests')).toBeTruthy();
    expect(view.getByTestId('quick-action-cost-valuation')).toBeTruthy();
    expect(view.getByTestId('quick-action-saas-billing')).toBeTruthy();
    expect(view.queryByLabelText('Mã thuê bao doanh nghiệp mới')).toBeNull();

    expect(view.getByText('Quản lý Gian hàng Sàn TMĐT & Ánh xạ Mã Sản phẩm Đa Sàn')).toBeTruthy();
    expect(view.container.textContent).not.toContain('Mapping');
    const shopNameInput = view.getByLabelText('Tên gian hàng sàn') as HTMLInputElement;
    expect(shopNameInput.value).toBe('Gian hàng Shopee Chính hãng');
    expect(shopNameInput.maxLength).toBe(120);

    fireEvent.click(view.getByRole('button', { name: 'Liên kết Gian hàng & Ánh xạ SKU Sàn' }));
    expect(
      await view.findByText(/Đã liên kết gian hàng Gian hàng Shopee Chính hãng \(SHOPEE\)/),
    ).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Đồng bộ Tồn kho An toàn Đa Gian hàng' }));
    expect(
      await view.findByText(
        'Đã đồng bộ tồn kho an toàn đa gian hàng (Shopee, TikTok Shop • Đệm an toàn chống bán lố 100%).',
      ),
    ).toBeTruthy();
    expect(post).toHaveBeenCalledWith(
      '/api/sales-orders/marketplace-shops/sync-inventory',
      expect.objectContaining({ safetyBufferPercent: 5 }),
    );

    fireEvent.click(view.getByRole('button', { name: 'Chạy Đối soát 3 Chiều Tiền Thu Hộ & Quyết toán Phí Sàn' }));
    expect(
      await view.findByText(
        'Đã đối soát 23/25 vận đơn (92%), phát hiện 2 cảnh báo lệch phí/chậm hoàn.',
      ),
    ).toBeTruthy();
    expect(post).toHaveBeenCalledWith(
      '/api/v1/reports/cod-reconciliation/verify',
      expect.objectContaining({ reconcileMode: 'THREE_WAY_COD_AND_MARKETPLACE_FEE' }),
    );
  });

  it('shows error alert instead of fake success when marketplace sync or COD reconciliation fails', async () => {
    const view = render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    );
    await view.findByText(
      'Báo cáo Lợi nhuận Gộp Ước tính Chủ Shop, Đối soát Tiền Thu Hộ (COD) & Đồng bộ Tồn kho Đa Sàn',
    );

    post.mockRejectedValueOnce({ response: { data: { message: 'Lỗi kết nối cổng đồng bộ sàn.' } } });
    fireEvent.click(view.getByRole('button', { name: 'Đồng bộ Tồn kho An toàn Đa Gian hàng' }));
    expect((await view.findByRole('alert')).textContent).toContain('Lỗi kết nối cổng đồng bộ sàn.');
    expect(view.queryByText(/Đã đồng bộ tồn kho an toàn đa gian hàng/)).toBeNull();
  });
});
