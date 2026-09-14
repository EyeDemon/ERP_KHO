// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ReceiptPrintPreview, { type PrintableReceipt } from './ReceiptPrintPreview';

const receipt = (status = 'Draft'): PrintableReceipt => ({
  code: 'IMP-01', status, note: '<script>alert(1)</script>\nGhi chú tiếng Việt', warehouseName: 'Kho Bắc',
  createdAt: '2026-09-14T08:00:00Z', createdByName: 'Người lập', supplierCode: 'NCC-01', supplierName: 'Nhà cung cấp',
  details: [{ id: 1, productCode: 'SP-01', productName: '<img src=x onerror=alert(1)>', unitName: 'Cái', quantity: 12.5 }]
});

describe('ReceiptPrintPreview', () => {
  afterEach(cleanup);
  it('maps draft data and renders untrusted content as text', () => {
    const view = render(<ReceiptPrintPreview kind="import" receipt={receipt()} fetchedAt={new Date('2026-09-14T09:00:00Z')} onClose={() => {}} />);
    expect(view.getByText('BẢN NHÁP — CHƯA DUYỆT')).toBeTruthy();
    expect(view.getByText('<script>alert(1)</script>', { exact: false })).toBeTruthy();
    expect(view.container.querySelector('script')).toBeNull(); expect(view.container.querySelector('img')).toBeNull();
    expect(view.getByText('12,5')).toBeTruthy(); expect(view.getByText('Cái')).toBeTruthy();
  });
  it('shows cancelled and missing historical fields without master-data fallback', () => {
    const value = receipt('Cancelled'); value.supplierCode = null; value.supplierName = null; value.approvedByName = null;
    const view = render(<ReceiptPrintPreview kind="import" receipt={value} fetchedAt={new Date()} onClose={() => {}} />);
    expect(view.getByText('ĐÃ HỦY')).toBeTruthy(); expect(view.getAllByText('Chưa ghi nhận').length).toBeGreaterThan(1);
  });
  it('invokes browser print and supports keyboard close', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {}); const close = vi.fn();
    const view = render(<ReceiptPrintPreview kind="export" receipt={receipt('Dispatched')} fetchedAt={new Date()} onClose={close} />);
    fireEvent.click(view.getByText('In / Save as PDF')); expect(print).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(view.getByRole('dialog'), { key: 'Escape' }); expect(close).toHaveBeenCalledTimes(1);
  });
});
