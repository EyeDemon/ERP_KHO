import { describe, expect, it } from 'vitest';
import { approvalDisplayAction, validRejectReason } from './approval';

describe('approval workflow presentation', () => {
  it('validates reject reasons safely', () => {
    expect(validRejectReason('  Sai số lượng  ')).toBe(true);
    expect(validRejectReason('ab')).toBe(false);
    expect(validRejectReason('Lỗi\r\nforged')).toBe(false);
    expect(validRejectReason('<script>alert(1)</script>')).toBe(false);
    expect(validRejectReason('<b>Không đạt</b>')).toBe(false);
    expect(validRejectReason('x'.repeat(501))).toBe(false);
  });
  it('keeps business rejection distinct from cancellation and security rejection', () => {
    expect(approvalDisplayAction('ApprovalRejected')).toBe('Bị từ chối');
    expect(approvalDisplayAction('ImportReceipt.Cancelled')).toBe('Đã hủy');
    expect(approvalDisplayAction('Approval.Reject.Rejected')).toBe('Thao tác không được chấp nhận');
    expect(approvalDisplayAction('ImportReceipt.Approved')).toBe('Đã duyệt');
    expect(approvalDisplayAction('Unknown.TechnicalAction')).toBe('Đã cập nhật chứng từ');
  });
  it('presents export reservation, dispatch and release as distinct Vietnamese actions', () => {
    expect(approvalDisplayAction('ExportReceipt.ApprovedAndReserved')).toBe('Đã duyệt và giữ hàng');
    expect(approvalDisplayAction('ExportReceipt.Dispatched')).toBe('Đã xuất kho');
    expect(approvalDisplayAction('ExportReceipt.CancelledAndReleased')).toBe('Đã hủy và giải phóng hàng giữ');
  });
});
