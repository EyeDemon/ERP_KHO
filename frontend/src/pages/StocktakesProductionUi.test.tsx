// @vitest-environment jsdom
import { cleanup, fireEvent, render, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import Stocktakes from './Stocktakes';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

const summary = {
  id: 1,
  code: 'ST-2026-0001',
  warehouseId: 1,
  warehouseName: 'DC Hồ Chí Minh',
  status: 0,
  note: 'Kiểm kê cuối ngày',
  createdBy: 1,
  createdByName: 'Nhân viên kho',
  createdAt: '2026-10-03T08:00:00Z',
  detailCount: 1,
};

const detail = {
  ...summary,
  details: [{
    id: 11,
    stocktakeId: 1,
    productId: 10,
    productCode: 'SKU-10',
    productName: 'Sản phẩm 10',
    unitName: 'Cái',
    systemQuantity: 12,
    actualQuantity: null,
    differenceQuantity: 0,
    note: '',
  }],
};

describe('Stocktakes shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    get.mockImplementation(async (url) => {
      if (url === '/api/stocktakes') return { data: [summary] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'DC Hồ Chí Minh', isActive: true }] } as never;
      if (url === '/api/stocktakes/1') return { data: detail } as never;
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders the stocktake list semantically and exposes the create form', async () => {
    const view = render(<Stocktakes />);

    expect(await view.findByText('ST-2026-0001')).toBeTruthy();
    const table = view.getByRole('table', { name: 'Danh sách phiếu kiểm kê' });
    expect(table).toBeTruthy();
    expect(within(table).getByText('Nháp')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Tạo phiếu kiểm kê' }));

    expect(await view.findByText('Tạo phiếu kiểm kê mới')).toBeTruthy();
    expect(view.getByLabelText('Kho hàng kiểm kê')).toBeTruthy();
    expect(view.getByLabelText('Ghi chú kiểm kê')).toBeTruthy();
  });

  it('keeps draft detail editing and approval controls reachable', async () => {
    const view = render(<Stocktakes />);

    await view.findByText('ST-2026-0001');
    fireEvent.click(view.getByRole('button', { name: 'Xem chi tiết' }));

    expect(await view.findByRole('table', { name: 'Chi tiết kiểm kê ST-2026-0001' })).toBeTruthy();
    expect(view.getByRole('button', { name: 'Duyệt phiếu' })).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Nhập SL' }));
    expect(view.getByLabelText('Số lượng thực tế SKU-10')).toBeTruthy();
    expect(view.getByLabelText('Ghi chú kiểm kê SKU-10')).toBeTruthy();
  });

  it('evaluates stocktake auto-approval threshold (<1% & <=500.000đ) and renders recount round 1/2 table', async () => {
    const autoApprovalPayload = {
      stocktakeId: 1,
      stocktakeCode: 'ST-2026-0001',
      eligibleForAutoApproval: false,
      requiresDirectorApproval: true,
      autoApprovedLineCount: 1,
      directorReviewLineCount: 1,
      maxAllowedVariancePct: 1.0,
      maxAllowedVarianceValueVnd: 500000,
      lines: [
        {
          detailId: 11,
          productId: 10,
          productCode: 'SKU-10',
          productName: 'Sản phẩm 10',
          systemQuantity: 200,
          actualQuantity: 199,
          firstCountQty: 199,
          secondCountQty: 199,
          recountRound: 2,
          varianceQty: -1,
          variancePct: 0.5,
          unitCost: 100000,
          varianceValueVnd: 100000,
          autoApproved: true,
          decisionCode: 'AUTO_APPROVE_ELIGIBLE',
        },
        {
          detailId: 12,
          productId: 11,
          productCode: 'SKU-11',
          productName: 'Sản phẩm 11',
          systemQuantity: 50,
          actualQuantity: 45,
          firstCountQty: 44,
          secondCountQty: 45,
          recountRound: 2,
          varianceQty: -5,
          variancePct: 10.0,
          unitCost: 200000,
          varianceValueVnd: 1000000,
          autoApproved: false,
          decisionCode: 'REQUIRES_WAREHOUSE_DIRECTOR_APPROVAL',
        },
      ],
    };

    get.mockImplementation(async (url) => {
      if (url === '/api/stocktakes') return { data: [summary] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'DC Hồ Chí Minh', isActive: true }] } as never;
      if (url === '/api/stocktakes/1') return { data: detail } as never;
      if (url === '/api/v1/stocktakes/1/count-attempts-and-auto-approval') return { data: autoApprovalPayload } as never;
      return { data: [] } as never;
    });

    const post = vi.mocked(apiClient.post);
    post.mockImplementation(async (url) => {
      if (url === '/api/v1/stocktakes/1/evaluate-auto-approval') {
        return { data: autoApprovalPayload } as never;
      }
      return { data: {} } as never;
    });

    const view = render(<Stocktakes />);
    await view.findByText('ST-2026-0001');

    const evalBtn = view.getByTestId('btn-evaluate-stocktake-auto-approval');
    fireEvent.click(evalBtn);

    const recountTable = await view.findByRole('table', {
      name: 'Bảng đối chiếu lịch sử đếm lại và đánh giá tự động duyệt',
    });
    expect(recountTable).toBeTruthy();
    expect(within(recountTable).getByText('Đếm Lần 1')).toBeTruthy();
    expect(within(recountTable).getByText('Đếm Lại Lần 2')).toBeTruthy();
    expect(await within(recountTable).findByText('Tự động duyệt (<1% & ≤500k)')).toBeTruthy();
    expect(within(recountTable).getByText('Cần Giám đốc kho duyệt')).toBeTruthy();
  });
});
