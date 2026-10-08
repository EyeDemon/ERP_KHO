// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
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
    expect(view.getByRole('dialog').querySelector('script')).toBeNull(); expect(view.getByRole('dialog').querySelector('img')).toBeNull();
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
    fireEvent.click(view.getByText('In / Lưu thành PDF')); expect(print).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(view.getByRole('dialog'), { key: 'Escape' }); expect(close).toHaveBeenCalledTimes(1);
  });
  it('contains export print keyboard focus and restores the background on close', async () => {
    const trigger = document.createElement('button');
    trigger.textContent = 'Mở bản in'; document.body.append(trigger); trigger.focus();
    const view = render(<ReceiptPrintPreview kind="export" receipt={receipt('Dispatched')} fetchedAt={new Date()} onClose={() => {}} />);
    try {
      const user = userEvent.setup(), dialog = view.getByRole('dialog');
      const print = view.getByRole('button', { name: 'In / Lưu thành PDF' });
      const close = view.getByRole('button', { name: 'Đóng' });
      expect(document.activeElement).toBe(close);
      await user.tab(); expect(document.activeElement).toBe(print);
      await user.tab({ shift: true }); expect(document.activeElement).toBe(close);
      trigger.focus(); expect(dialog.contains(document.activeElement)).toBe(true);
      expect(trigger.inert).toBe(true);
      view.unmount(); expect(document.activeElement).toBe(trigger); expect(trigger.inert).toBeFalsy();
    } finally { view.unmount(); trigger.remove(); }
  });
  it.each(['Approved', 'Dispatched', 'Received', 'ReadyToPost', 'Posted', 'QcPending', 'QcCompleted',
    'DiscrepancyPending', 'DiscrepancySubmitted', 'DiscrepancyPendingApproval', 'DiscrepancyResolved', 'DiscrepancyRejected', 'UnexpectedState'])(
    'keeps print copy and %s status in Vietnamese', status => {
      const view = render(<ReceiptPrintPreview kind="import" receipt={receipt(status)} fetchedAt={new Date()} onClose={() => {}} />);
      expect(view.getByText('Bản in dùng dữ liệu đã lưu trên hệ thống.')).toBeTruthy();
      expect(view.getByRole('button', { name: 'In / Lưu thành PDF' })).toBeTruthy();
      expect(view.getByRole('dialog').querySelector('.receipt-status')!.textContent).not.toBe(status);
      expect(view.getByRole('dialog').textContent).not.toMatch(/Preview|backend|Save as PDF/);
    });
});
