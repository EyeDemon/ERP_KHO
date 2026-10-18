// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PartnerDebts from './PartnerDebts';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const mockSummary = {
  totalPayableBalance: 1100000,
  totalReceivableBalance: 880000,
  openApInvoiceCount: 1,
  openArInvoiceCount: 1,
  overdueInvoiceCount: 0,
};

const mockApInvoice = {
  id: 101,
  code: 'AP-IR-001',
  partnerId: 10,
  partnerCode: 'SUP-01',
  partnerName: 'Nhà cung cấp Thép Việt',
  debtType: 'AP',
  sourceType: 'ImportReceipt',
  sourceId: 501,
  sourceCode: 'NK-2026-001',
  currency: 'VND',
  subTotalAmount: 1000000,
  taxAmount: 100000,
  totalAmount: 1100000,
  paidAmount: 0,
  remainingAmount: 1100000,
  status: 'Open',
  issuedAtUtc: '2026-10-18T08:00:00Z',
  dueDateUtc: '2026-11-18T08:00:00Z',
  note: 'Công nợ phiếu nhập NK-2026-001',
  rowVersion: 'AAAAAAAAB9E=',
};

const mockLedger = [
  {
    id: 9001,
    partnerId: 10,
    partnerCode: 'SUP-01',
    partnerName: 'Nhà cung cấp Thép Việt',
    invoiceId: 101,
    invoiceCode: 'AP-IR-001',
    debtType: 'AP',
    transactionType: 'InvoiceIssued',
    amountDelta: 1100000,
    balanceAfter: 1100000,
    referenceType: 'ImportReceipt',
    referenceId: 501,
    referenceCode: 'NK-2026-001',
    paymentMethod: null,
    note: 'Hạch toán tự động khi duyệt phiếu nhập',
    createdBy: 1,
    createdAtUtc: '2026-10-18T08:00:00Z',
  },
];

describe('PartnerDebts & MobileBarcodeScannerBar', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', JSON.stringify(['partner.read', 'partner.update']));

    get.mockImplementation((url: string) => {
      if (url === '/api/partner-debts/summary') {
        return Promise.resolve({ data: mockSummary } as never);
      }
      if (url === '/api/partner-debts/invoices') {
        return Promise.resolve({ data: [mockApInvoice] } as never);
      }
      if (url === '/api/partner-debts/ledger') {
        return Promise.resolve({ data: mockLedger } as never);
      }
      return Promise.resolve({ data: {} } as never);
    });
  });

  it('renders AP/AR summary, invoice table with nowrap/numeric cells, and append-only ledger entries', async () => {
    const view = render(<PartnerDebts />);

    expect(await view.findAllByText('AP-IR-001')).toHaveLength(2);
    expect(view.getAllByText('NK-2026-001')).toHaveLength(2);
    expect(view.getByText('Ghi nhận công nợ')).toBeTruthy();
    expect(view.getByText('Thanh toán')).toBeTruthy();

    const headerCell = view.getByRole('columnheader', { name: 'Chứng từ kho' });
    expect((headerCell as HTMLElement).style.whiteSpace).toBe('nowrap');
  });

  it('hides payment action button when user lacks partner.update permission', async () => {
    localStorage.setItem('permissions', JSON.stringify(['partner.read']));
    const view = render(<PartnerDebts />);

    await view.findAllByText('AP-IR-001');
    expect(view.queryByText('Thanh toán')).toBeNull();
  });

  it('submits payment with rowVersion + Idempotency-Key and prevents double submit', async () => {
    let resolvePost!: () => void;
    post.mockReturnValue(
      new Promise(resolve => {
        resolvePost = () =>
          resolve({
            data: {
              ...mockApInvoice,
              paidAmount: 400000,
              remainingAmount: 700000,
              status: 'PartiallyPaid',
              rowVersion: 'AAAAAAAAB9I=',
            },
          } as never);
      }),
    );

    const view = render(<PartnerDebts />);
    await view.findAllByText('AP-IR-001');

    fireEvent.click(view.getByText('Thanh toán'));

    const amountInput = view.getByLabelText('Số tiền thanh toán công nợ') as HTMLInputElement;
    expect(amountInput.value).toBe('1100000');

    fireEvent.change(amountInput, { target: { value: '400000' } });
    fireEvent.change(view.getByLabelText('Mã tham chiếu thanh toán'), {
      target: { value: 'UNC-2026-999' },
    });

    const submitBtn = view.getByText('Xác nhận Thanh toán');
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);

    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(
      '/api/partner-debts/invoices/101/pay',
      expect.objectContaining({
        amount: 400000,
        paymentMethod: 'BANK_TRANSFER',
        referenceCode: 'UNC-2026-999',
        rowVersion: 'AAAAAAAAB9E=',
      }),
      expect.objectContaining({
        headers: expect.objectContaining({
          'Idempotency-Key': expect.any(String),
        }),
      }),
    );

    resolvePost();
    expect(await view.findByRole('status')).toBeTruthy();
  });

  it('sanitizes barcode scanner input, resolves entity via /api/barcode-lookup/resolve, and invokes onScan', async () => {
    get.mockResolvedValueOnce({
      data: {
        barcode: 'BIN-STAGE-01',
        entityType: 'Location',
        entityId: 77,
        code: 'BIN-STAGE-01',
        name: 'Vị trí Staging 01',
        warehouseId: 5,
      },
    } as never);

    const onScan = vi.fn();
    const view = render(<MobileBarcodeScannerBar warehouseId={5} sampleCode="BIN-STAGE-01" onScan={onScan} />);

    const input = view.getByRole('searchbox', {
      name: 'Quét mã vạch Mobile / RF (Camera hoặc súng quét)',
    });
    expect(view.getByPlaceholderText('Nhập hoặc quét mã Bin, SKU, Lot, Serial, LPN...')).toBeTruthy();

    fireEvent.change(input, { target: { value: '  BIN-STAGE-01\r\n ' } });
    fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' });

    await waitFor(() => {
      expect(get).toHaveBeenCalledWith('/api/barcode-lookup/resolve', {
        params: { code: 'BIN-STAGE-01', warehouseId: 5 },
      });
    });

    await waitFor(() => {
      expect(onScan).toHaveBeenCalledWith(
        'BIN-STAGE-01',
        expect.objectContaining({
          entityType: 'Location',
          entityId: 77,
          code: 'BIN-STAGE-01',
        }),
      );
    });

    expect(await view.findByText(/Vị trí kệ \(Bin\): BIN-STAGE-01 — Vị trí Staging 01/)).toBeTruthy();

    fireEvent.click(view.getByText('Bật Camera Quét'));
    expect(await view.findByText('Điền mã mẫu')).toBeTruthy();
    fireEvent.click(view.getByText('Đóng'));
    expect(view.queryByText('Điền mã mẫu')).toBeNull();
  });
});
