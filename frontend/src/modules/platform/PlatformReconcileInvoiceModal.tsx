import React, { useCallback, useEffect, useRef, useState } from 'react';
import { CheckCircle2, X } from 'lucide-react';
import { useDialogFocusTrap } from '../../ui/useDialogFocusTrap';
import type { PlatformBillingInvoiceItem } from './PlatformHealthAndWorkersSection';
import { PlatformReconcileFormSections } from './PlatformReconcileFormSections';
import { parseViDateTimeStrict, requiresFourEyesBillingApproval } from './platformBillingHelpers';

export interface ManualReconcilePayload {
  bankTransactionRef: string;
  bankCreditTimestampVi: string;
  receivedAmountVnd: number;
  reasonNote: string;
  allowPartialOrOverpayment: boolean;
  attachmentReference: string;
  attachmentFile: File;
  approverCode: string;
  formData: FormData;
}

interface PlatformReconcileInvoiceModalProps {
  invoice: PlatformBillingInvoiceItem | null;
  activeRole?: string;
  currentActor?: string;
  usedBankRefs?: string[];
  onClose: () => void;
  onSubmitReconcile: (
    invoiceId: number,
    invoiceCode: string,
    payload: ManualReconcilePayload,
  ) => Promise<void> | void;
}

export const PlatformReconcileInvoiceModal: React.FC<PlatformReconcileInvoiceModalProps> = ({
  invoice,
  activeRole = 'SuperAdmin',
  currentActor = '',
  usedBankRefs = ['FT2627690123', 'FT26276110988'],
  onClose,
  onSubmitReconcile,
}) => {
  const [bankRef, setBankRef] = useState<string>('');
  const [creditTime, setCreditTime] = useState<string>('');
  const [receivedAmount, setReceivedAmount] = useState<string>('');
  const [allowVarianceConfirmed, setAllowVarianceConfirmed] = useState<boolean>(false);
  const [attachmentFile, setAttachmentFile] = useState<File | null>(null);
  const [approverCode, setApproverCode] = useState<string>('');
  const [note, setNote] = useState<string>('');
  const [serverConflictError, setServerConflictError] = useState<boolean>(false);
  const [submitError, setSubmitError] = useState<{ message: string; codeTag: string; correlationId?: string } | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [confirmingDiscard, setConfirmingDiscard] = useState<boolean>(false);
  const modalCardRef = useRef<HTMLDivElement>(null);

  const requiresFourEyes = requiresFourEyesBillingApproval(activeRole);

  useEffect(() => {
    if (invoice) {
      setBankRef('');
      setCreditTime('');
      setReceivedAmount('');
      setAllowVarianceConfirmed(false);
      setAttachmentFile(null);
      setApproverCode('');
      setNote('');
      setServerConflictError(false);
      setSubmitError(null);
      setSubmitting(false);
      setConfirmingDiscard(false);
    }
  }, [invoice]);

  const isDirty = Boolean(
    bankRef.trim() ||
      creditTime.trim() ||
      receivedAmount.trim() ||
      attachmentFile ||
      approverCode.trim() ||
      note.trim(),
  );

  const requestClose = useCallback(() => {
    if (submitting) return;
    if (isDirty) setConfirmingDiscard(true);
    else onClose();
  }, [isDirty, onClose, submitting]);

  useDialogFocusTrap(modalCardRef, {
    open: Boolean(invoice),
    onEscape: requestClose,
    escapeDisabled: submitting,
  });

  if (!invoice) return null;

  const code = invoice.invoiceCode || invoice.invoiceNo || 'INV-SAAS-202611-002';
  const total = Number(invoice.amountVnd || invoice.totalAmountVnd || 1490000);
  const sub =
    invoice.subTotalVnd && invoice.vatAmountVnd && invoice.subTotalVnd + invoice.vatAmountVnd === total
      ? invoice.subTotalVnd
      : Math.round(total / 1.1);
  const vat = total - sub;
  const buyerName =
    !invoice.buyerLegalName || invoice.buyerLegalName === 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)'
      ? 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO'
      : invoice.buyerLegalName;

  const handleReceivedAmountChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const rawDigits = e.target.value.replace(/\D/g, '');
    const nextFormatted = rawDigits === '' ? '' : Number(rawDigits).toLocaleString('vi-VN');
    setReceivedAmount(nextFormatted);
    if (Number(rawDigits || 0) === total) setAllowVarianceConfirmed(false);
  };

  const numericAmount = Number(receivedAmount.replace(/\D/g, '') || 0);
  const hasVariance = numericAmount > 0 && numericAmount !== total;
  const shortfall = Math.max(0, total - numericAmount);
  const overpay = Math.max(0, numericAmount - total);

  const normalizedBankRef = bankRef.trim().toUpperCase();
  const isDuplicateLocalRef =
    normalizedBankRef.length >= 6 &&
    usedBankRefs.some((r) => r.trim().toUpperCase() === normalizedBankRef);
  const hasConflictError = isDuplicateLocalRef || serverConflictError;

  const parsedCreditDate = parseViDateTimeStrict(creditTime);
  const isFutureCreditTime = parsedCreditDate !== null && parsedCreditDate.getTime() > Date.now();
  const isValidCreditTime = parsedCreditDate !== null && !isFutureCreditTime;
  const isSodViolation =
    requiresFourEyes && approverCode.trim().length > 0 && approverCode.trim() === currentActor;

  const missingFields: string[] = [];
  if (bankRef.trim().length < 6) missingFields.push('Mã giao dịch FT/UNC');
  else if (hasConflictError) missingFields.push('Mã giao dịch không trùng lặp');
  if (!isValidCreditTime) missingFields.push('Thời điểm ghi có hợp lệ');
  if (numericAmount <= 0) missingFields.push('Số tiền thực nhận');
  else if (hasVariance && !allowVarianceConfirmed) missingFields.push('Xác nhận chênh lệch số tiền');
  if (!attachmentFile) missingFields.push('Chứng từ đính kèm');
  if (requiresFourEyes) {
    if (!approverCode.trim()) missingFields.push('Người phê duyệt độc lập');
    else if (isSodViolation) missingFields.push('Người phê duyệt khác người lập');
  }
  if (note.trim().length < 10) missingFields.push('Ghi chú tối thiểu 10 ký tự');

  const isValid = missingFields.length === 0 && !submitting;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isValid || !attachmentFile) return;
    setSubmitting(true);
    setServerConflictError(false);
    setSubmitError(null);
    const formData = new FormData();
    formData.append('attachmentFile', attachmentFile);
    formData.append('attachmentReference', attachmentFile.name);
    formData.append('bankTransactionRef', bankRef.trim());
    formData.append('bankCreditTimestampVi', creditTime.trim());
    formData.append('receivedAmountVnd', String(numericAmount));
    if (requiresFourEyes && approverCode.trim()) {
      formData.append('approverCode', approverCode.trim());
      formData.append('approverUsername', approverCode.trim());
    }
    formData.append('allowPartialOrOverpayment', String(hasVariance));
    formData.append('reasonNote', note.trim());

    try {
      await onSubmitReconcile(invoice.id ?? 2, code, {
        bankTransactionRef: bankRef.trim(),
        bankCreditTimestampVi: creditTime.trim(),
        receivedAmountVnd: numericAmount,
        reasonNote: note.trim(),
        allowPartialOrOverpayment: hasVariance,
        attachmentReference: attachmentFile.name,
        attachmentFile,
        approverCode: requiresFourEyes ? approverCode.trim() : '',
        formData,
      });
      onClose();
    } catch (err) {
      const anyErr = err as {
        response?: {
          status?: number;
          headers?: Record<string, string>;
          data?: { errorCode?: string; code?: string; message?: string; title?: string; correlationId?: string };
        };
      };
      const errCode = anyErr?.response?.data?.errorCode || anyErr?.response?.data?.code || '';
      if (
        anyErr?.response?.status === 409 ||
        errCode === 'BILLING_DUPLICATE_BANK_TRANSFER_REF' ||
        errCode === 'DUPLICATE_BANK_TRANSACTION_REF'
      ) {
        setServerConflictError(true);
      } else {
        const d = anyErr?.response?.data;
        const corr = anyErr?.response?.headers?.['x-correlation-id'] || d?.correlationId || '';
        const msg = d?.message || d?.title || 'Không thể xác nhận đối soát hóa đơn từ máy chủ.';
        const codeTag = errCode || (anyErr?.response?.status ? String(anyErr.response.status) : 'SERVER_ERROR');
        setSubmitError({ message: msg, codeTag, correlationId: corr || undefined });
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      className="platform-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="modal-reconcile-invoice-title"
      data-testid="modal-confirm-invoice-reconcile"
      onClick={(e) => {
        if (e.target === e.currentTarget) requestClose();
      }}
    >
      <div ref={modalCardRef} tabIndex={-1} className="platform-modal-card" style={{ padding: '16px 20px', maxWidth: 640, maxHeight: '92vh', overflowY: 'auto' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
          <h2 id="modal-reconcile-invoice-title" style={{ margin: 0, fontSize: 16, fontWeight: 700, color: '#0f172a', display: 'flex', alignItems: 'center', gap: 8 }}>
            <CheckCircle2 size={20} strokeWidth={2} color="#1d4ed8" aria-hidden="true" />
            <span>Đối soát thanh toán chuyển khoản thủ công</span>
          </h2>
          <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-close-reconcile-modal" onClick={requestClose}>
            <X size={15} strokeWidth={2} aria-hidden="true" />
            <span>Đóng</span>
          </button>
        </div>

        <div className="platform-diff-box" data-testid="reconcile-invoice-summary-box" style={{ marginBottom: 10, lineHeight: 1.45 }}>
          <div>
            <strong>Mã hóa đơn:</strong> <code>{code}</code> • <strong>Doanh nghiệp:</strong>{' '}
            <code>TENANT-001</code> ({buyerName})
          </div>
          <div style={{ fontVariantNumeric: 'tabular-nums', marginTop: 2 }}>
            <strong>Số tiền hóa đơn:</strong>{' '}
            <strong style={{ color: '#1d4ed8' }}>{total.toLocaleString('vi-VN')} ₫</strong> (Trước thuế:{' '}
            {sub.toLocaleString('vi-VN')} ₫ + VAT 10%: {vat.toLocaleString('vi-VN')} ₫)
          </div>
          <div style={{ marginTop: 3, fontSize: 12, color: '#334155' }}>
            <strong>Người lập đối soát (Phiên hiện tại):</strong>{' '}
            <code data-testid="reconcile-actor-readonly">{currentActor}</code>
          </div>
        </div>

        {confirmingDiscard ? (
          <div
            role="alertdialog"
            aria-label="Xác nhận hủy dữ liệu đang nhập"
            data-testid="dirty-dismiss-confirm"
            style={{ marginBottom: 10, padding: '10px 12px', borderRadius: 8, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 12.5, display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}
          >
            <span style={{ fontWeight: 600 }}>Bạn có thông tin đối soát chưa lưu. Hủy thay đổi và đóng hộp thoại?</span>
            <div style={{ display: 'flex', gap: 8 }}>
              <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-keep-editing-reconcile" onClick={() => setConfirmingDiscard(false)}>
                Tiếp tục nhập
              </button>
              <button type="button" className="platform-btn platform-btn-danger" data-testid="btn-confirm-discard-reconcile" onClick={() => { setConfirmingDiscard(false); onClose(); }}>
                Hủy thay đổi &amp; Đóng
              </button>
            </div>
          </div>
        ) : null}

        <form onSubmit={handleSubmit}>
          <PlatformReconcileFormSections
            total={total}
            bankRef={bankRef}
            onChangeBankRef={(v) => { setBankRef(v); setServerConflictError(false); setSubmitError(null); }}
            hasConflictError={hasConflictError}
            creditTime={creditTime}
            onChangeCreditTime={setCreditTime}
            isFutureCreditTime={isFutureCreditTime}
            receivedAmount={receivedAmount}
            onChangeReceivedAmount={handleReceivedAmountChange}
            numericAmount={numericAmount}
            hasVariance={hasVariance}
            shortfall={shortfall}
            overpay={overpay}
            allowVarianceConfirmed={allowVarianceConfirmed}
            onChangeAllowVariance={setAllowVarianceConfirmed}
            attachmentFile={attachmentFile}
            onChangeAttachmentFile={setAttachmentFile}
            requiresFourEyesApproval={requiresFourEyes}
            currentActor={currentActor}
            approverCode={approverCode}
            onChangeApproverCode={setApproverCode}
            isSodViolation={isSodViolation}
            note={note}
            onChangeNote={setNote}
          />

          {missingFields.length > 0 ? (
            <div
              data-testid="reconcile-missing-fields-hint"
              style={{ marginTop: 6, padding: '7px 10px', borderRadius: 6, background: '#f8fafc', border: '1px solid #cbd5e1', color: '#475569', fontSize: 11.5 }}
            >
              <strong>Cần hoàn tất trước khi xác nhận:</strong> {missingFields.join(' • ')}
            </div>
          ) : null}

          {submitError ? (
            <div role="alert" data-testid="reconcile-server-error" style={{ marginTop: 6, padding: '8px 10px', borderRadius: 6, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 12, fontWeight: 700, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <span>{submitError.message}</span>
              <code className="ui-err-code">
                {submitError.codeTag}{submitError.correlationId ? ` • ${submitError.correlationId}` : ''}
              </code>
            </div>
          ) : null}

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 10 }}>
            <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-cancel-reconcile-modal" onClick={requestClose}>
              Hủy bỏ
            </button>
            <button type="submit" className="platform-btn platform-btn-primary" data-testid="btn-submit-manual-reconcile" disabled={!isValid}>
              Xác nhận đã thu {(numericAmount > 0 ? numericAmount : total).toLocaleString('vi-VN')} ₫
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
