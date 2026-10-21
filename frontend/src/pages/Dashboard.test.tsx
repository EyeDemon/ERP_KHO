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
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
  });

  afterEach(cleanup);

  it('renders shop owner gross profit report, 4 financial KPIs, 5-step onboarding wizard, and executes marketplace sync & 3-way COD reconciliation', async () => {
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

    expect(view.getByText('148.500.000 đ')).toBeTruthy();
    expect(view.getByText('92.400.000 đ')).toBeTruthy();
    expect(view.getByText('41.250.000 đ • Biên lãi gộp 27,8%')).toBeTruthy();
    expect(view.getByText('36.800.000 đ • 3 cảnh báo lệch phí/chậm hoàn')).toBeTruthy();

    expect(view.getByText('Bước 1: Khởi tạo sơ đồ kệ mẫu (3 giây)')).toBeTruthy();
    expect(view.getByText('Bước 2: Nhập danh mục sản phẩm chuẩn')).toBeTruthy();
    expect(view.getByText('Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn')).toBeTruthy();
    expect(view.getByText('Bước 4: Liên kết gian hàng Shopee / TikTok Shop')).toBeTruthy();
    expect(view.getByText('Bước 5: In gộp tem nhiệt & xuất đơn đầu tiên')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Đồng bộ Tồn kho An toàn Đa Gian hàng' }));
    expect(
      await view.findByText(
        'Đã đồng bộ tồn kho an toàn đa gian hàng (Shopee, TikTok Shop • Đệm an toàn chống bán lố 100%).',
      ),
    ).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Chạy Đối soát 3 Chiều Tiền Thu Hộ & Quyết toán Phí Sàn' }));
    expect(
      await view.findByText(
        'Đã chạy đối soát 3 chiều tiền thu hộ COD & quyết toán phí sàn: Khớp 98,4% vận đơn, phát hiện 3 cảnh báo lệch phí vận chuyển.',
      ),
    ).toBeTruthy();
  });
});
