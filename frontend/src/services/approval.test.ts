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
    expect(approvalDisplayAction('Approval.Reject.Rejected')).toBe('Approval.Reject.Rejected');
  });
});
