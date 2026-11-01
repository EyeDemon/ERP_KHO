// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../services/apiClient';
import { setCurrentPermissions } from '../../services/authorization';
import StockCardS12DnPanel from '../../modules/inventory/StockCardS12DnPanel';
import CustomerReturns from '../CustomerReturns';

vi.mock('../../services/apiClient', async () => {
  const actual = await vi.importActual<typeof import('../../services/apiClient')>(
    '../../services/apiClient',
  );
  return {
    ...actual,
    default: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

const sampleRmaItems = [
  {
    id: 901,
    returnCode: 'RMA-2026-0901',
    trackingNumber: 'SPXVN06889911',
    exportReceiptCode: 'EX-2026-5108',
    salesOrderCode: 'SO-2026-101',
    warehouseId: 1,
    warehouseName: 'Kho Trung Tâm',
    quarantineLocationCode: 'QUARANTINE-01',
    productCode: 'SKU-1001',
    productName: 'Cà phê Arabica 500g',
    quantity: 2,
    unitCost: 85000,
    returnReason: 'BUYER_REJECTED_DELIVERY',
    condition: 'GOOD',
    disposition: 'PENDING',
    status: 'QUARANTINED',
    rowVersion: 'AAAAAAAARMA1',
  },
];

describe('TASK-W2-04: CustomerReturns (RMA 4-Disposition) & StockCardS12DnPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setCurrentPermissions([
      'return.read',
      'return.create',
      'return.receive',
      'return.inspect',
      'return.disposition',
      'return.post',
      'receipt.read',
      'inventory.cost.read',
    ]);

    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/warehouses') {
        return { data: [{ id: 1, code: 'WH01', name: 'Kho Trung Tâm' }] } as never;
      }
      if (url === '/api/v1/customer-returns') {
        return {
          data: {
            items: sampleRmaItems,
            totalCount: 1,
            costHiddenByRbac: false,
            summary: {
              quarantinedCount: 1,
              inspectedCount: 2,
              restockedCount: 5,
              refurbishRtvScrapCount: 3,
            },
          },
        } as never;
      }
      if (url === '/api/v1/customer-returns/901') {
        return { data: sampleRmaItems[0] } as never;
      }
      if (url === '/api/v1/reports/stock-card') {
        return {
          data: {
            templateCode: 'Mẫu số S12-DN',
            circularReference: 'Thông tư 200/2014/TT-BTC',
            warehouseId: 1,
            warehouseName: 'Kho Trung Tâm',
            productId: 1,
            productCode: 'SKU-1001',
            productName: 'Cà phê Arabica 500g',
            openingBalance: 100,
            totalInboundQuantity: 50,
            totalOutboundQuantity: 20,
            closingBalance: 130,
            lines: [
              {
                transactionId: 11,
                transactionDate: '2026-10-10T08:00:00Z',
                inboundDocumentCode: 'GR-2026-1048',
                description: 'Nhập kho mua hàng',
                inboundQuantity: 50,
                outboundQuantity: 0,
                runningBalance: 150,
              },
            ],
          },
        } as never;
      }
      if (url === '/api/v1/reports/stock-card/export') {
        return { data: new Blob(['PK-XLSX-BINARY']) } as never;
      }
      return { data: [] } as never;
    });

    vi.mocked(apiClient.post).mockResolvedValue({ data: { ok: true } } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('hiển thị 4 thẻ KPI RMA, quét tiếp nhận kiện hoàn vào Biệt trữ kèm Idempotency-Key và hiển thị giá vốn khi có quyền inventory.cost.read', async () => {
    render(<CustomerReturns />);

    await waitFor(() => {
      expect(screen.getByTestId('kpi-rma-quarantined').textContent).toBe('1');
      expect(screen.getByTestId('kpi-rma-inspected').textContent).toBe('2');
      expect(screen.getByTestId('kpi-rma-restocked').textContent).toBe('5');
      expect(screen.getByTestId('kpi-rma-other-dispositions').textContent).toBe('3');
    });

    expect(screen.getByTestId('rma-cost-visible').textContent).toContain('85.000');

    const scanInput = screen.getByTestId('input-rma-intake-scan');
    fireEvent.change(scanInput, { target: { value: 'GHN-AWB-998877' } });
    fireEvent.click(screen.getByTestId('btn-submit-rma-intake'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/customer-returns/intake',
        expect.objectContaining({
          warehouseId: 1,
          trackingNumber: 'GHN-AWB-998877',
          quarantineLocationCode: 'QUARANTINE-01',
        }),
        expect.objectContaining({
          headers: expect.objectContaining({
            'Idempotency-Key': expect.any(String),
          }),
        }),
      );
    });
  });

  it('ẩn tuyệt đối Giá vốn đơn vị khi thiếu quyền inventory.cost.read hoặc khi costHiddenByRbac = true', async () => {
    setCurrentPermissions(['return.read', 'receipt.read']);
    render(<CustomerReturns />);

    await waitFor(() => {
      expect(screen.getByTestId('rma-cost-redacted').textContent).toContain(
        'Đã ẩn theo phân quyền',
      );
    });
    expect(screen.queryByTestId('rma-cost-visible')).toBeNull();
  });

  it('mở ngăn kéo ReturnDispositionDrawer, thực thi định đoạt 4 luồng kèm Idempotency-Key và xử lý lỗi 409 Conflict', async () => {
    render(<CustomerReturns />);

    await waitFor(() => {
      expect(screen.getByTestId('btn-open-disposition-drawer')).toBeTruthy();
    });

    fireEvent.click(screen.getByTestId('btn-open-disposition-drawer'));

    await waitFor(() => {
      expect(screen.getByTestId('return-disposition-drawer')).toBeTruthy();
    });

    fireEvent.change(screen.getByTestId('select-rma-disposition'), {
      target: { value: 'REFURBISH_REPACK' },
    });
    fireEvent.change(screen.getByTestId('input-rma-restock-bin'), {
      target: { value: 'BIN-B02-01' },
    });
    fireEvent.change(screen.getByTestId('input-rma-packaging-code'), {
      target: { value: 'PKG-CARTON-M' },
    });
    fireEvent.change(screen.getByTestId('input-rma-packaging-qty'), {
      target: { value: '2' },
    });

    vi.mocked(apiClient.post).mockRejectedValueOnce({
      response: {
        status: 409,
        data: {
          errorCode: 'CONCURRENCY_CONFLICT',
          message: 'Phiếu RMA vừa được cập nhật bởi nhân viên khác.',
        },
      },
    });

    fireEvent.click(screen.getByTestId('btn-submit-rma-disposition'));

    await waitFor(() => {
      expect(screen.getByTestId('rma-disposition-conflict-409')).toBeTruthy();
    });

    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: { status: 'COMPLETED' } } as never);
    fireEvent.click(screen.getByTestId('btn-submit-rma-disposition'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/customer-returns/901/dispose',
        expect.objectContaining({
          disposition: 'REFURBISH_REPACK',
          restockLocationCode: 'BIN-B02-01',
          packagingQuantity: 2,
        }),
        expect.objectContaining({
          headers: expect.objectContaining({
            'Idempotency-Key': expect.any(String),
          }),
        }),
      );
    });
  });

  it('tải và xuất Thẻ Kho Kế Toán Mẫu S12-DN (Thông tư 200/2014/TT-BTC) từ StockCardS12DnPanel', async () => {
    render(
      <StockCardS12DnPanel
        warehouses={[{ id: 1, name: 'Kho Trung Tâm' }]}
        products={[{ productId: 1, productCode: 'SKU-1001', productName: 'Cà phê Arabica 500g' }]}
      />,
    );

    fireEvent.click(screen.getByTestId('btn-view-stock-card-s12dn'));

    await waitFor(() => {
      expect(screen.getByTestId('stock-card-s12dn-preview').textContent).toContain('Mẫu số S12-DN');
      expect(screen.getByTestId('stock-card-s12dn-preview').textContent).toContain('GR-2026-1048');
      expect(screen.getByTestId('stock-card-s12dn-preview').textContent).toContain('150');
    });

    fireEvent.click(screen.getByTestId('btn-export-stock-card-s12dn'));

    await waitFor(() => {
      expect(apiClient.get).toHaveBeenCalledWith(
        '/api/v1/reports/stock-card/export',
        expect.objectContaining({ responseType: 'blob' }),
      );
    });
  });
});
