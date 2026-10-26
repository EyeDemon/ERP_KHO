// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PartnerDebts from './PartnerDebts';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import {
  decodeBarcodeFromCanvas,
  encodeEan13Modules,
  renderEan13ToCanvas,
} from '../utils/cameraBarcodeDecoder';
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

const mockArInvoice = {
  id: 102,
  code: 'AR-ER-001',
  partnerId: 11,
  partnerCode: 'CUS-01',
  partnerName: 'Công ty Xây dựng Minh Long',
  debtType: 'AR',
  sourceType: 'ExportReceipt',
  sourceId: 601,
  sourceCode: 'PXK-2026-001',
  currency: 'VND',
  subTotalAmount: 800000,
  taxAmount: 80000,
  totalAmount: 880000,
  paidAmount: 0,
  remainingAmount: 880000,
  status: 'Open',
  issuedAtUtc: '2026-10-18T09:00:00Z',
  dueDateUtc: '2026-11-18T09:00:00Z',
  note: 'Công nợ phiếu xuất PXK-2026-001',
  rowVersion: 'AAAAAAAAB9I=',
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
        return Promise.resolve({ data: [mockApInvoice, mockArInvoice] } as never);
      }
      if (url === '/api/partner-debts/ledger') {
        return Promise.resolve({ data: mockLedger } as never);
      }
      return Promise.resolve({ data: {} } as never);
    });
  });

  it('renders AP/AR summary, localized source document labels, compact columns (Còn nợ, Trạng thái, Thao tác), and append-only ledger entries', async () => {
    const view = render(<PartnerDebts />);

    expect(await view.findAllByText('AP-IR-001')).toHaveLength(2);
    expect(view.getByText('AR-ER-001')).toBeTruthy();
    expect(view.getAllByText('NK-2026-001')).toHaveLength(2);
    expect(view.getByText('PXK-2026-001')).toBeTruthy();

    expect(view.getAllByText('Phiếu nhập kho')).toHaveLength(2);
    expect(view.getByText('Phiếu xuất kho')).toBeTruthy();
    expect(view.queryByText('ImportReceipt')).toBeNull();
    expect(view.queryByText('ExportReceipt')).toBeNull();

    expect(view.getByText('Phát hành hóa đơn')).toBeTruthy();
    expect(view.getByText(/Trước thuế: 1\.000\.000 • VAT: 100\.000/)).toBeTruthy();

    expect(view.getByRole('columnheader', { name: 'Còn nợ' })).toBeTruthy();
    expect(view.getByRole('columnheader', { name: 'Hạn TT' })).toBeTruthy();
    expect(view.getByRole('columnheader', { name: 'Trạng thái' })).toBeTruthy();
    expect(view.getByRole('columnheader', { name: 'Thao tác' })).toBeTruthy();

    expect(view.getAllByText('Sổ phụ')).toHaveLength(2);
    expect(view.getAllByText('Thanh toán')).toHaveLength(2);

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

    fireEvent.click(view.getAllByText('Thanh toán')[0]);

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
      name: /Quét mã vạch (Thiết bị Cầm tay|Mobile \/ RF)/i,
    });
    expect(view.getByPlaceholderText('Nhập hoặc quét mã kệ, mã SP, số lô, số sê-ri, mã GS1-128...')).toBeTruthy();
    expect(view.getByTestId('scanner-camera-skeleton-idle')).toBeTruthy();

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

    expect(await view.findByText(/Vị trí kệ: BIN-STAGE-01 — Vị trí Staging 01/)).toBeTruthy();

    fireEvent.click(view.getByTestId('btn-toggle-camera'));
    expect(await view.findByTestId('scanner-camera-skeleton-disconnected')).toBeTruthy();
    expect(await view.findByText('Điền mã mẫu')).toBeTruthy();
    fireEvent.click(view.getByText('Đóng'));
    expect(view.queryByText('Điền mã mẫu')).toBeNull();
  });

  it('captures global synthetic KeyboardEvent sequences from Chrome Extension Barcode Scan Simulator across all 3 steps when input is blurred', async () => {
    get.mockResolvedValue({
      data: {
        barcode: 'BIN-A01',
        entityType: 'Location',
        entityId: 88,
        code: 'BIN-A01',
        name: 'Kệ A01',
        warehouseId: 5,
      },
    } as never);

    const onScan = vi.fn();
    const view = render(<MobileBarcodeScannerBar warehouseId={5} sampleCode="BIN-A01" onScan={onScan} />);

    const dispatchExtensionScan = (barcode: string) => {
      document.body.focus();
      for (const ch of barcode) {
        window.dispatchEvent(
          new KeyboardEvent('keydown', {
            key: ch,
            keyCode: ch.charCodeAt(0),
            bubbles: true,
          }),
        );
      }
      window.dispatchEvent(
        new KeyboardEvent('keydown', {
          key: 'Enter',
          code: 'Enter',
          keyCode: 13,
          bubbles: true,
        }),
      );
    };

    dispatchExtensionScan('STAGE-IN-01');
    expect(await view.findByTestId('scanner-last-scan-feedback')).toBeTruthy();
    expect(view.getByTestId('scanner-last-scan-feedback').textContent).toContain(
      '✅ Đã nhận mã [Bước 1]: STAGE-IN-01 → Chuyển sang Bước 2',
    );
    expect(view.getByTestId('scanner-step-chip-1').textContent).toContain('STAGE-IN-01');

    dispatchExtensionScan('(01)08938505970012(10)LOT2026A(17)261231(21)SER001');
    await waitFor(() => {
      expect(view.getByTestId('scanner-last-scan-feedback').textContent).toContain('Chuyển sang Bước 3');
    });
    expect(view.getByTestId('gs1-parsed-summary').textContent).toContain('LOT2026A');
    expect(view.getByTestId('gs1-parsed-summary').textContent).toContain('31/12/2026');
    expect(view.getByTestId('gs1-parsed-summary').textContent).toContain('SER001');

    dispatchExtensionScan('BIN-A01');
    await waitFor(() => {
      expect(view.getByTestId('scanner-last-scan-feedback').textContent).toContain(
        '✅ Đã nhận mã [Bước 3]: BIN-A01 → Hoàn tất chu trình 3 bước',
      );
    });
    expect(view.getByTestId('scanner-step-chip-3').textContent).toContain('BIN-A01');
    await waitFor(() => {
      expect(onScan).toHaveBeenCalledTimes(3);
    });
  });

  it('decodes real 95-module EAN-13 8934804025766 from <canvas> with tilt/noise and auto-routes Step 1 -> Step 2 (STAGE-IN-01) -> Step 3', async () => {
    const modules = encodeEan13Modules('8934804025766');
    expect(modules.length).toBe(95);

    const canvas = document.createElement('canvas');
    canvas.width = 720;
    canvas.height = 400;
    renderEan13ToCanvas(canvas, '8934804025766', {
      tiltDegrees: 5,
      noiseLevel: 8,
      glareStrength: 28,
    });

    const decoded = decodeBarcodeFromCanvas(canvas);
    expect(decoded).not.toBeNull();
    expect(decoded?.code).toBe('8934804025766');
    expect(decoded?.format).toBe('ean_13');
    expect(decoded?.scanlineMatches).toBeGreaterThanOrEqual(2);

    get.mockResolvedValueOnce({
      data: {
        barcode: '8934804025766',
        entityType: 'Product',
        entityId: 501,
        code: '8934804025766',
        name: 'Sản phẩm EAN-13 Việt Nam',
        warehouseId: 5,
      },
    } as never);

    const onScan = vi.fn();
    const view = render(<MobileBarcodeScannerBar warehouseId={5} sampleCode="BIN-A01" onScan={onScan} />);

    // Đang ở Bước 1, người dùng đưa thẳng sản phẩm thật có mã EAN-13 8934804025766 vào quét
    document.body.focus();
    for (const ch of '8934804025766') {
      window.dispatchEvent(
        new KeyboardEvent('keydown', {
          key: ch,
          keyCode: ch.charCodeAt(0),
          bubbles: true,
        }),
      );
    }
    window.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        keyCode: 13,
        bubbles: true,
      }),
    );

    expect(await view.findByTestId('product-barcode-decoded-banner')).toBeTruthy();
    expect(view.getByTestId('product-barcode-decoded-banner').textContent).toBe(
      '📦 Đã nhận diện Mã vạch Sản phẩm (EAN-13 / GTIN): 8934804025766 • Quốc gia: 893 (Việt Nam) • Đã tự động gán Kệ nguồn STAGE-IN-01 & chuyển sang Bước 3 (Quét Kệ đích)',
    );
    expect(view.getByTestId('scanner-step-chip-1').textContent).toContain('STAGE-IN-01');
    expect(view.getByTestId('scanner-step-chip-2').textContent).toContain('8934804025766');
    expect(view.getByTestId('wizard-step-pill-3').getAttribute('data-step-active')).toBe('true');

    await waitFor(() => {
      expect(onScan).toHaveBeenCalledWith(
        '8934804025766',
        expect.objectContaining({
          entityType: 'Product',
          code: '8934804025766',
        }),
      );
    });
  });

  it('keeps invoice table mounted and filters Sổ phụ ledger strictly to AP-IR-QA0001 without leaking AR-ER-QA0001', async () => {
    const qaApInvoice = {
      ...mockApInvoice,
      id: 201,
      code: 'AP-IR-QA0001',
    };
    const qaArInvoice = {
      ...mockArInvoice,
      id: 202,
      code: 'AR-ER-QA0001',
    };
    const qaMixedLedger = [
      {
        ...mockLedger[0],
        id: 9101,
        invoiceId: 201,
        invoiceCode: 'AP-IR-QA0001',
        debtType: 'AP',
        referenceCode: 'NK-QA-001',
      },
      {
        ...mockLedger[0],
        id: 9102,
        partnerId: 11,
        partnerCode: 'CUS-01',
        partnerName: 'Công ty Xây dựng Minh Long',
        invoiceId: 202,
        invoiceCode: 'AR-ER-QA0001',
        debtType: 'AR',
        referenceType: 'ExportReceipt',
        referenceCode: 'PXK-QA-001',
      },
    ];

    get.mockImplementation((url: string) => {
      if (url === '/api/partner-debts/summary') {
        return Promise.resolve({ data: mockSummary } as never);
      }
      if (url === '/api/partner-debts/invoices') {
        return Promise.resolve({ data: [qaApInvoice, qaArInvoice] } as never);
      }
      if (url === '/api/partner-debts/ledger') {
        return Promise.resolve({ data: qaMixedLedger } as never);
      }
      return Promise.resolve({ data: {} } as never);
    });

    const view = render(<PartnerDebts />);
    await view.findAllByText('AP-IR-QA0001');

    fireEvent.click(view.getAllByText('Sổ phụ')[0]);

    expect(view.queryByText('Đang tải danh sách hóa đơn công nợ...')).toBeNull();
    const invoiceTable = view.getByRole('table', { name: 'Danh sách hóa đơn công nợ' });
    expect(invoiceTable.textContent).toContain('AP-IR-QA0001');
    expect(invoiceTable.textContent).toContain('AR-ER-QA0001');

    const ledgerTable = view.getByRole('table', { name: 'Sổ cái biến động công nợ đối tác' });
    expect(ledgerTable.textContent).toContain('AP-IR-QA0001');
    expect(ledgerTable.textContent).not.toContain('AR-ER-QA0001');
  });
});
