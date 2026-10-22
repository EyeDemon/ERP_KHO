// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import ExportReceipts from './ExportReceipts';
import { setCurrentPermissions } from '../services/authorization';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

const get = vi.mocked(apiClient.get);

const draftReceipt = {
  id: 21,
  code: 'ER-2026-0021',
  warehouseName: 'DC Hồ Chí Minh',
  status: 'Draft',
  note: '',
  createdBy: 5,
  createdByName: 'Nhân viên kho',
  createdAt: '2026-10-03T10:00:00Z',
  allowWarehouseStaffDirectDispatch: false,
  writeEnabled: true,
  details: [],
  customerId: 9,
  customerCode: 'CUS-09',
  customerName: 'Khách hàng A',
};

describe('ExportReceipts shared production UI', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    setCurrentPermissions([]);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [draftReceipt] } as never;
      if (url === '/api/warehouses') return { data: [] } as never;
      if (url === '/api/products') return { data: [] } as never;
      if (url === '/api/business-partners') return { data: { items: [] } } as never;
      return { data: [] } as never;
    });
  });

  afterEach(cleanup);

  it('renders the outbound list semantically without mutation actions for Viewer', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);

    const view = render(<ExportReceipts />);

    expect((await view.findAllByText('ER-2026-0021')).length).toBeGreaterThan(0);
    expect(view.getByRole('table', { name: 'Danh sách phiếu xuất' })).toBeTruthy();
    expect(view.getByText('Nháp')).toBeTruthy();
    expect(view.queryByText('Tạo Phiếu Xuất Kho')).toBeNull();
    expect(view.queryByRole('button', { name: 'Duyệt và giữ hàng' })).toBeNull();
    expect(view.queryByRole('button', { name: /Duyệt và xuất ngay/ })).toBeNull();
    expect(view.queryByRole('button', { name: 'Hủy' })).toBeNull();
  });

  it('aggregates availability across eligible locations before blocking draft creation', async () => {
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    setCurrentPermissions(['export_receipt.read', 'export_receipt.create']);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'Kho A' }] } as never;
      if (url === '/api/products') return { data: [{ id: 7, code: 'SKU-7', name: 'Sản phẩm 7' }] } as never;
      if (url === '/api/inventorystocks/current') return { data: [{ availableQuantity: 20 }, { availableQuantity: 30 }] } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findByText('Tạo Phiếu Xuất Kho');

    fireEvent.change(view.getByLabelText('Mã phiếu'), { target: { value: 'EX-ML-01' } });
    fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
    fireEvent.click(view.getByRole('button', { name: '+ Thêm dòng' }));
    fireEvent.change(view.getByLabelText('Sản phẩm dòng 1'), { target: { value: '7' } });
    fireEvent.change(view.getByLabelText('Số lượng dòng 1'), { target: { value: '40' } });
    fireEvent.change(view.getByLabelText('Đơn giá dòng 1'), { target: { value: '1' } });

    await view.findByText('Tồn: 50');
    await waitFor(() => expect((view.getByRole('button', { name: 'Tạo Phiếu Xuất' }) as HTMLButtonElement).disabled).toBe(false));
  });

  it('renders canonical Vietnamese states and never exposes raw dispatch mode', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    const dispatched = {
      ...draftReceipt,
      id: 22,
      code: 'ER-2026-0022',
      status: 'Dispatched',
      dispatchMode: 'RequireSeparateDispatch',
      approvedByName: 'Người duyệt',
      dispatchedByName: 'Thủ kho',
      approvedAt: '2026-10-03T11:00:00Z',
      dispatchedAt: '2026-10-03T12:00:00Z',
    };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [dispatched] } as never;
      if (url === '/api/exportreceipts/22') return { data: dispatched } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findAllByText('ER-2026-0022');
    fireEvent.click(view.getByRole('button', { name: 'Chi tiết' }));

    expect(await view.findByText('Duyệt và giữ hàng → xác nhận xuất kho')).toBeTruthy();
    expect(view.queryByText('RequireSeparateDispatch')).toBeNull();
  });

  it('fails closed without exposing backend error details', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    get.mockRejectedValue({ response: { status: 403, data: { message: 'SQL/internal secret' } } });

    const view = render(<ExportReceipts />);

    expect(await view.findByText('Bạn không còn quyền thực hiện thao tác này.')).toBeTruthy();
    expect(view.queryByText('SQL/internal secret')).toBeNull();
    expect(view.queryByText('ER-2026-0021')).toBeNull();
  });

  it('uses the canonical approved-and-reserved wording in export print preview', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    const approved = {
      ...draftReceipt,
      id: 23,
      code: 'ER-2026-0023',
      status: 'Approved',
      approvedByName: 'Người duyệt',
      approvedAt: '2026-10-03T11:00:00Z',
    };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [approved] } as never;
      if (url === '/api/exportreceipts/23') return { data: approved } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findAllByText('ER-2026-0023');
    fireEvent.click(view.getByRole('button', { name: 'Xem bản in' }));

    const printDialog = await view.findByRole('dialog', { name: 'Bản in ER-2026-0023' });
    expect(within(printDialog).getByText('Đã duyệt và giữ hàng')).toBeTruthy();
    expect(within(printDialog).queryByText(/^Đã duyệt$/)).toBeNull();
  });

  it('keeps write-gated workflow buttons disabled during maintenance', async () => {
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    setCurrentPermissions([
      'export_receipt.read',
      'export_receipt.create',
      'export_receipt.update',
      'export_receipt.approve',
      'export_receipt.dispatch',
      'export_receipt.cancel',
      'partner.read',
    ]);
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [{ ...draftReceipt, allowPerReceiptDispatchMode: true, writeEnabled: false }] } as never;
      if (url === '/api/warehouses') return { data: [] } as never;
      if (url === '/api/products') return { data: [] } as never;
      if (url === '/api/business-partners') return { data: { items: [] } } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);

    expect(await view.findByText('Luồng xuất kho đang tạm dừng để bảo trì. Dữ liệu vẫn có thể xem.')).toBeTruthy();
    expect((view.getByRole('button', { name: 'Duyệt và giữ hàng' }) as HTMLButtonElement).disabled).toBe(true);
    expect((view.getByRole('button', { name: 'Duyệt và xuất ngay (tương thích)' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('masks unitPrice and totalAmount as Ẩn theo phân quyền when null and displays MarketplaceOrderId, CarrierCode, TrackingNumber, and CancelPendingPutback', async () => {
    localStorage.setItem('role', 'Viewer');
    setCurrentPermissions(['export_receipt.read']);
    const maskedReceipt = {
      ...draftReceipt,
      id: 24,
      code: 'ER-2026-0024',
      status: 'CancelPendingPutback',
      marketplaceOrderId: 'MP-SHOPEE-9988',
      carrierCode: 'GHN-EXPRESS',
      trackingNumber: 'AWB-77665544',
      totalAmount: null,
      details: [
        {
          id: 501,
          productCode: 'SKU-501',
          productName: 'Hàng ẩn giá',
          unitName: 'THÙNG',
          quantity: 5,
          unitPrice: null,
          lineTotalAmount: null,
        },
      ],
    };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [maskedReceipt] } as never;
      if (url === '/api/exportreceipts/24') return { data: maskedReceipt } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    expect((await view.findAllByText('ER-2026-0024')).length).toBeGreaterThan(0);
    expect(view.getByText('Chờ cất trả hàng hủy về kệ')).toBeTruthy();

    fireEvent.click(view.getByRole('button', { name: 'Chi tiết' }));
    expect(await view.findByText('MP-SHOPEE-9988')).toBeTruthy();
    expect(view.getByText('GHN-EXPRESS')).toBeTruthy();
    expect(view.getByText('AWB-77665544')).toBeTruthy();
    expect(view.getAllByText('Ẩn theo phân quyền').length).toBeGreaterThanOrEqual(3);
  });

  it('submits MarketplaceOrderId, CarrierCode, TrackingNumber, preserves form inputs on 409 INSUFFICIENT_AVAILABLE_STOCK, and maps 409 ORDER_CANCELLED_PUTBACK_REQUIRED', async () => {
    const post = vi.mocked(apiClient.post);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    localStorage.setItem('role', 'Manager');
    localStorage.setItem('userId', '99');
    setCurrentPermissions(['export_receipt.read', 'export_receipt.create', 'export_receipt.approve', 'export_receipt.dispatch']);
    const approvedReceipt = { ...draftReceipt, id: 25, code: 'ER-2026-0025', status: 'Approved', approvedBy: 5 };
    get.mockImplementation(async (url) => {
      if (url === '/api/exportreceipts') return { data: [approvedReceipt] } as never;
      if (url === '/api/warehouses') return { data: [{ id: 1, name: 'Kho A' }] } as never;
      if (url === '/api/products') return { data: [{ id: 7, code: 'SKU-7', name: 'Sản phẩm 7' }] } as never;
      if (url === '/api/inventorystocks/current') return { data: [{ availableQuantity: 50 }] } as never;
      return { data: [] } as never;
    });

    const view = render(<ExportReceipts />);
    await view.findByText('Tạo Phiếu Xuất Kho');

    fireEvent.change(view.getByLabelText('Mã phiếu'), { target: { value: 'EX-MP-01' } });
    fireEvent.change(view.getByLabelText('Kho'), { target: { value: '1' } });
    fireEvent.change(view.getByLabelText('Mã đơn sàn TMĐT'), { target: { value: 'MP-TIKTOK-01' } });
    fireEvent.change(view.getByLabelText('Đơn vị vận chuyển'), { target: { value: 'GHTK' } });
    fireEvent.change(view.getByLabelText('Mã vận đơn (AWB)'), { target: { value: 'AWB-112233' } });
    fireEvent.click(view.getByRole('button', { name: '+ Thêm dòng' }));
    fireEvent.change(view.getByLabelText('Sản phẩm dòng 1'), { target: { value: '7' } });
    fireEvent.change(view.getByLabelText('Số lượng dòng 1'), { target: { value: '12' } });
    fireEvent.change(view.getByLabelText('Đơn giá dòng 1'), { target: { value: '10000' } });

    await view.findByText('Tồn: 50');
    post.mockRejectedValueOnce({
      response: { status: 409, data: { code: 'INSUFFICIENT_AVAILABLE_STOCK' } },
    });
    fireEvent.click(view.getByRole('button', { name: 'Tạo Phiếu Xuất' }));

    expect(
      await view.findByText(
        'Tồn kho khả dụng tại vị trí chọn không đủ để thực hiện thao tác. Vui lòng điều chỉnh số lượng hoặc chọn vị trí khác.',
      ),
    ).toBeTruthy();
    expect((view.getByLabelText('Mã phiếu') as HTMLInputElement).value).toBe('EX-MP-01');
    expect((view.getByLabelText('Số lượng dòng 1') as HTMLInputElement).value).toBe('12');
    expect((view.getByLabelText('Mã đơn sàn TMĐT') as HTMLInputElement).value).toBe('MP-TIKTOK-01');
    expect(post).toHaveBeenCalledWith(
      '/api/exportreceipts',
      expect.objectContaining({
        marketplaceOrderId: 'MP-TIKTOK-01',
        carrierCode: 'GHTK',
        trackingNumber: 'AWB-112233',
      }),
      expect.any(Object),
    );

    post.mockRejectedValueOnce({
      response: { status: 409, data: { error: { code: 'ORDER_CANCELLED_PUTBACK_REQUIRED' } } },
    });
    fireEvent.click(view.getByRole('button', { name: 'Xác nhận xuất kho' }));
    expect(
      await view.findByText(
        'Đơn hàng đã bị hủy giữa chừng và đang chờ cất trả hàng về kệ lưu trữ (Putback). Không thể tiếp tục đóng gói hoặc xuất kho.',
      ),
    ).toBeTruthy();
  });

  it('renders thermal 100x150mm batch AWB print, SLA badges, carrier handover manifest, and MISA NĐ 70/2025 e-invoice actions', async () => {
    localStorage.setItem('role', 'Manager');
    setCurrentPermissions(['export_receipt.read', 'export_receipt.create', 'export_receipt.dispatch']);
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} } as never);

    const view = render(<ExportReceipts />);
    expect(
      await view.findByText(
        'In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm Theo Vị Trí Kệ & Biên Bản Bàn Giao Bưu Tá Điện Tử',
      ),
    ).toBeTruthy();
    expect(view.getByText(/Đơn quá hạn bàn giao sàn:/)).toBeTruthy();
    expect(view.getByText(/Đơn sắp trễ hạn \(< 2 giờ\):/)).toBeTruthy();
    expect(view.getByText(/Đơn phải giao trong ngày:/)).toBeTruthy();
    expect(view.getByText('[Kệ: BIN-A01-01 | SKU: QA-E2E-001 x 5]')).toBeTruthy();
    expect(view.getByText('Số lần in: 1 (Bản gốc)')).toBeTruthy();
    expect(view.getByText('Cảnh báo in lại')).toBeTruthy();
    expect(view.getByText('Viettel SInvoice • Mẫu 1/001 • C26TAA')).toBeTruthy();
    expect(view.getAllByTestId('barcode-1d-svg').length).toBeGreaterThanOrEqual(2);
    expect(view.getAllByTestId('barcode-qr-svg').length).toBeGreaterThanOrEqual(2);
    expect(view.getAllByText('Mã QR vận đơn').length).toBeGreaterThanOrEqual(2);

    fireEvent.click(
      view.getByRole('button', { name: 'In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm (Sắp xếp theo Kệ A→Z)' }),
    );
    expect(
      await view.findByText(
        'Đã kết xuất lô phiếu gửi hàng nhiệt 100x150mm sắp xếp theo vị trí kệ (A → Z): GHN84920188, VTP99201122.',
      ),
    ).toBeTruthy();
    expect(vi.mocked(apiClient.post)).toHaveBeenCalledWith(
      '/api/export-receipts/batch-print-awb',
      expect.objectContaining({
        paperSize: '100x150mm',
        sortBy: 'BIN_LOCATION_ASC',
        reprintReason: 'In gộp phiếu gửi hàng nhiệt 100x150mm tại bàn đóng gói',
      }),
    );

    fireEvent.change(view.getByLabelText('Nhập hoặc quét mã vận đơn bàn giao'), {
      target: { value: 'GHN84920188, VTP99201122' },
    });
    fireEvent.click(view.getByRole('button', { name: 'Chốt Biên Bản Bàn Giao Bưu Tá Điện Tử' }));
    expect(
      await view.findByText(
        'Đã chốt Biên bản Bàn giao Bưu tá Điện tử (2 vận đơn: GHN84920188, VTP99201122 • Đã chặn cứng 0 đơn hủy trên sàn).',
      ),
    ).toBeTruthy();
    expect(vi.mocked(apiClient.post)).toHaveBeenCalledWith(
      '/api/ExportReceipts/confirm-carrier-handover',
      expect.objectContaining({
        trackingNumbers: ['GHN84920188', 'VTP99201122'],
      }),
    );

    fireEvent.click(
      view.getByRole('button', { name: 'Kết xuất Bảng kê Excel Chuẩn MISA & Hóa đơn Điện tử NĐ 70/2025' }),
    );
    expect(
      await view.findByText(
        'Đã kết xuất Bảng kê Excel chuẩn MISA & phát hành Hóa đơn điện tử NĐ 70/2025 (Viettel SInvoice • Mẫu 1/001 • C26TAA).',
      ),
    ).toBeTruthy();

    vi.mocked(apiClient.post).mockRejectedValueOnce({
      response: { status: 500, data: { message: 'Lỗi máy in nhiệt' } },
    });
    fireEvent.click(
      view.getByRole('button', { name: 'In Gộp Phiếu Gửi Hàng Nhiệt 100x150mm (Sắp xếp theo Kệ A→Z)' }),
    );
    expect(await view.findByRole('alert')).toBeTruthy();
  });
});

