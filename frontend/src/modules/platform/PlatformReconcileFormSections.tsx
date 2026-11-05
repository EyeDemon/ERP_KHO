import React, { useRef } from 'react';
import { AlertCircle, Upload, X } from 'lucide-react';
import { formatMinLengthCounterVi } from '../../services/permissionPresentation';
import { UiVnDateInput } from '../../components/common/UiVnDateInput';

export interface PlatformReconcileFormSectionsProps {
  total: number;
  bankRef: string;
  onChangeBankRef: (val: string) => void;
  hasConflictError: boolean;
  creditTime: string;
  onChangeCreditTime: (val: string) => void;
  isFutureCreditTime: boolean;
  receivedAmount: string;
  onChangeReceivedAmount: (e: React.ChangeEvent<HTMLInputElement>) => void;
  numericAmount: number;
  hasVariance: boolean;
  shortfall: number;
  overpay: number;
  allowVarianceConfirmed: boolean;
  onChangeAllowVariance: (checked: boolean) => void;
  attachmentFile: File | null;
  onChangeAttachmentFile: (file: File | null) => void;
  requiresFourEyesApproval?: boolean;
  currentActor?: string;
  approverCode: string;
  onChangeApproverCode: (val: string) => void;
  isSodViolation: boolean;
  note: string;
  onChangeNote: (val: string) => void;
}

export const PlatformReconcileFormSections: React.FC<PlatformReconcileFormSectionsProps> = ({
  total,
  bankRef,
  onChangeBankRef,
  hasConflictError,
  creditTime,
  onChangeCreditTime,
  isFutureCreditTime,
  receivedAmount,
  onChangeReceivedAmount,
  numericAmount,
  hasVariance,
  shortfall,
  overpay,
  allowVarianceConfirmed,
  onChangeAllowVariance,
  attachmentFile,
  onChangeAttachmentFile,
  requiresFourEyesApproval = false,
  currentActor = 'billing_admin',
  approverCode,
  onChangeApproverCode,
  isSodViolation,
  note,
  onChangeNote,
}) => {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const noteLength = note.trim().length;

  return (
    <>
      <fieldset className="platform-fieldset">
        <legend>1. Thông tin giao dịch ngân hàng</legend>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
          <div className="platform-form-field">
            <label htmlFor="input-reconcile-bank-ref">Mã giao dịch / Số tham chiếu sao kê *</label>
            <input
              id="input-reconcile-bank-ref"
              type="text"
              autoFocus
              data-testid="input-reconcile-bank-ref"
              placeholder="VD: FT2630698418102"
              value={bankRef}
              onChange={(e) => onChangeBankRef(e.target.value)}
            />
            {hasConflictError ? (
              <div
                role="alert"
                data-testid="reconcile-conflict-error"
                style={{ marginTop: 4, padding: '5px 8px', borderRadius: 6, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 11.5, fontWeight: 600, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}
              >
                <span>Mã giao dịch ngân hàng đã tồn tại trên hệ thống.</span>
                <code className="ui-err-code">409 Conflict • BILLING_DUPLICATE_BANK_TRANSFER_REF</code>
              </div>
            ) : null}
          </div>

          <div className="platform-form-field">
            <label htmlFor="input-reconcile-credit-time" style={{ whiteSpace: 'nowrap' }}>
              Thời điểm ghi có (dd/MM/yyyy HH:mm) *
            </label>
            <UiVnDateInput
              id="input-reconcile-credit-time"
              testId="input-reconcile-credit-time"
              mode="datetime"
              verbalPrefix="Thời điểm ghi có"
              value={creditTime}
              onChange={onChangeCreditTime}
              required
            />
            {isFutureCreditTime ? (
              <div
                role="alert"
                data-testid="reconcile-future-time-error"
                style={{ marginTop: 4, padding: '5px 8px', borderRadius: 6, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 11.5, fontWeight: 600 }}
              >
                Thời điểm ngân hàng ghi có không được lớn hơn thời điểm hiện tại.
              </div>
            ) : null}
          </div>
        </div>

        <div className="platform-form-field">
          <label htmlFor="input-reconcile-received-amount">
            Số tiền thực nhận (VNĐ) * — đối chiếu với {total.toLocaleString('vi-VN')} ₫
          </label>
          <input
            id="input-reconcile-received-amount"
            type="text"
            inputMode="numeric"
            data-testid="input-reconcile-received-amount"
            placeholder={`VD: ${total.toLocaleString('vi-VN')}`}
            value={receivedAmount}
            onChange={onChangeReceivedAmount}
          />
          {numericAmount > 0 ? (
            <div
              data-testid="reconcile-amount-variance-banner"
              style={{
                marginTop: 5, padding: '6px 10px', borderRadius: 6, fontSize: 12, fontWeight: 600, fontVariantNumeric: 'tabular-nums',
                background: numericAmount === total ? '#ecfdf5' : numericAmount < total ? '#fffbeb' : '#F0F9FF',
                color: numericAmount === total ? '#047857' : numericAmount < total ? '#b45309' : '#0369A1',
                border: numericAmount === total ? '1px solid #a7f3d0' : numericAmount < total ? '1px solid #fde68a' : '1px solid #BAE6FD',
              }}
            >
              {numericAmount === total
                ? `Khớp chính xác số tiền hóa đơn (${total.toLocaleString('vi-VN')} ₫) — Tất toán đủ`
                : numericAmount < total
                  ? `Khách chuyển thiếu ${shortfall.toLocaleString('vi-VN')} ₫ — Sẽ ghi nhận: Thu một phần (Còn phải thu ${shortfall.toLocaleString('vi-VN')} ₫)`
                  : `Khách chuyển dư +${overpay.toLocaleString('vi-VN')} ₫ — Sẽ ghi nhận: Đã tất toán & Cộng +${overpay.toLocaleString('vi-VN')} ₫ vào số dư kỳ sau`}
            </div>
          ) : null}
          {hasVariance ? (
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, marginTop: 6, fontSize: 12, fontWeight: 600, color: '#92400e', cursor: 'pointer' }}>
              <input
                type="checkbox"
                data-testid="checkbox-allow-reconcile-variance"
                checked={allowVarianceConfirmed}
                onChange={(e) => onChangeAllowVariance(e.target.checked)}
              />
              <span>Tôi xác nhận cho phép ghi nhận thanh toán chênh lệch (thu một phần / chuyển dư)</span>
            </label>
          ) : null}
        </div>

        <div className="platform-form-field" style={{ marginBottom: 4 }}>
          <label htmlFor="input-reconcile-attachment-file">Tệp chứng từ đính kèm (Ủy nhiệm chi / Sao kê ngân hàng) *</label>
          <input
            ref={fileInputRef}
            id="input-reconcile-attachment-file"
            type="file"
            accept=".pdf,.png,.jpg,.jpeg"
            className="sr-only"
            data-testid="input-reconcile-attachment-file"
            onChange={(e) => {
              const f = e.target.files?.[0] ?? null;
              if (f && f.size > 10 * 1024 * 1024) return;
              onChangeAttachmentFile(f);
            }}
          />
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
            <button
              type="button"
              className="platform-btn platform-btn-secondary"
              data-testid="btn-upload-reconcile-attachment"
              onClick={() => fileInputRef.current?.click()}
              style={{ border: '1px dashed #1d4ed8', backgroundColor: '#eff6ff', color: '#1d4ed8' }}
            >
              <Upload size={14} strokeWidth={2} aria-hidden="true" />
              <span>Tải lên chứng từ ủy nhiệm chi / sao kê</span>
            </button>
            <span className="platform-field-hint" style={{ marginTop: 0 }}>
              Định dạng hỗ trợ: PDF, PNG, JPG • Tối đa 10 MB
            </span>
          </div>
          {attachmentFile ? (
            <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, marginTop: 4 }}>
              <span data-testid="reconcile-attached-filename" style={{ fontSize: 12, fontWeight: 600, color: '#047857' }}>
                Đã chọn: {attachmentFile.name}
              </span>
              <button
                type="button"
                className="platform-btn platform-btn-secondary"
                data-testid="btn-clear-reconcile-attachment"
                style={{ padding: '2px 8px', fontSize: 11.5, minHeight: 24 }}
                onClick={() => {
                  onChangeAttachmentFile(null);
                  if (fileInputRef.current) fileInputRef.current.value = '';
                }}
              >
                <X size={12} strokeWidth={2} aria-hidden="true" />
                <span>Xóa</span>
              </button>
            </div>
          ) : null}
        </div>
      </fieldset>

      <fieldset className="platform-fieldset">
        <legend>{requiresFourEyesApproval ? '2. Kiểm soát nội bộ (4 mắt)' : '2. Ghi chú đối soát kiểm toán'}</legend>
        {requiresFourEyesApproval ? (
          <>
            <div className="platform-form-field">
              <label htmlFor="select-reconcile-approver">Người phê duyệt độc lập *</label>
              <select
                id="select-reconcile-approver"
                data-testid="input-reconcile-approver"
                value={approverCode}
                onChange={(e) => onChangeApproverCode(e.target.value)}
              >
                <option value="">-- Chọn người phê duyệt độc lập --</option>
                <option value="ktt_nguyen_mai">Nguyễn Thị Mai (Kế toán trưởng)</option>
                <option value="fin_tran_hung">Trần Quốc Hùng (Kiểm soát viên thanh toán)</option>
                <option value={currentActor}>{currentActor} (Tài khoản phiên hiện tại — Kiểm chứng chặn SoD)</option>
              </select>
            </div>

            {isSodViolation ? (
              <div
                role="alert"
                data-testid="reconcile-sod-error"
                style={{ marginBottom: 8, padding: '8px 10px', borderRadius: 6, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 12, fontWeight: 700, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}
              >
                <AlertCircle size={15} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0 }} />
                <span>Vi phạm nguyên tắc kiểm soát 4 mắt: Người phê duyệt không được trùng với người lập đối soát.</span>
                <code className="ui-err-code">BILLING_SOD_SELF_APPROVAL_FORBIDDEN</code>
              </div>
            ) : null}
          </>
        ) : null}

        <div className="platform-form-field" style={{ marginBottom: 4 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
            <label htmlFor="input-reconcile-note">Ghi chú kiểm toán đối soát *</label>
            <span
              data-testid="reconcile-note-char-counter"
              style={{ fontSize: 11.5, fontWeight: 600, fontVariantNumeric: 'tabular-nums', color: noteLength >= 10 ? '#047857' : '#64748b' }}
            >
              {formatMinLengthCounterVi(noteLength, 10)}
            </span>
          </div>
          <textarea
            id="input-reconcile-note"
            rows={2}
            data-testid="input-reconcile-note"
            placeholder="Nhập lý do hoặc thông tin đối soát sao kê ngân hàng..."
            value={note}
            onChange={(e) => onChangeNote(e.target.value)}
          />
        </div>
      </fieldset>
    </>
  );
};
