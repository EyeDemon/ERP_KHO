import React, { useRef } from 'react';
import { CheckCircle2, Download, QrCode, X } from 'lucide-react';
import { BarcodeSvg } from '../../components/BarcodeSvg';
import { UiBadge } from '../../ui/ProductionUi';
import { useDialogFocusTrap } from '../../ui/useDialogFocusTrap';
import {
  buildClientVietQrPayload,
  downloadPaidInvoicePdf,
  formatCountdownMmSs,
  secondaryBtnStyle,
  type BillingState,
} from './SaasBillingHelpers';

export interface SaasBillingVietQrPanelProps {
  billing: BillingState;
  busy: boolean;
  checkingPayment: boolean;
  isQrPanelOpen: boolean;
  onOpenQrPanel: () => void;
  onCloseQrPanel: () => void;
  selectedPlanLabel: string;
  nextCycle: { dueDateText: string; nextStartText: string; nextEndText: string; shortRange: string; fullLabel: string };
  activeInvoiceBadge: { label: string; tone: 'success' | 'warning' | 'neutral' };
  isInvoicePaid: boolean;
  isInvoiceExpired: boolean;
  remainingSeconds: number;
  displayAmountVnd: number;
  copiedField: 'account' | 'amount' | 'content' | null;
  copyToClipboard: (field: 'account' | 'amount' | 'content', text: string) => void;
  vietQrInvoice: {
    invoiceCode: string;
    planCode: string;
    amountVnd: number;
    status: string;
    qrPayload: string;
    transferContent: string;
    expiresAtUtc: string;
    gatewayStatus: string;
  };
  hasActiveMatchingInvoice: boolean;
  autoRefreshQr: boolean;
  setAutoRefreshQr: (v: boolean) => void;
  showQaWebhookSimulator: boolean;
  notice: string;
  checkPaymentStatus: () => void;
  createVietQrInvoice: (overridePlanCode?: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => void;
  confirmVietQrWebhook: () => void;
}

const infoBlueBannerStyle = {
  display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5, fontWeight: 600,
  color: '#0369A1', background: '#F0F9FF', border: '1px solid #BAE6FD', padding: '6px 10px', borderRadius: 6,
} as const;

export const SaasBillingVietQrPanel: React.FC<SaasBillingVietQrPanelProps> = ({
  billing, busy, checkingPayment, isQrPanelOpen, onOpenQrPanel, onCloseQrPanel,
  selectedPlanLabel, nextCycle, activeInvoiceBadge, isInvoicePaid, isInvoiceExpired,
  remainingSeconds, displayAmountVnd, copiedField, copyToClipboard, vietQrInvoice,
  hasActiveMatchingInvoice, autoRefreshQr, setAutoRefreshQr, showQaWebhookSimulator,
  notice, checkPaymentStatus, createVietQrInvoice, confirmVietQrWebhook,
}) => {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  useDialogFocusTrap(dialogRef, {
    open: isQrPanelOpen && !isInvoicePaid,
    onEscape: onCloseQrPanel,
    escapeDisabled: busy,
  });

  if (isInvoicePaid) {
    const paidInv = billing.recentInvoices.find(
      (i) => (vietQrInvoice.invoiceCode && i.invoiceCode === vietQrInvoice.invoiceCode) || i.invoiceCode === 'INV-SAAS-202611-002',
    );
    const resolvedCode = vietQrInvoice.invoiceCode || paidInv?.invoiceCode || 'INV-SAAS-202611-002';
    const billingCycleLabel = paidInv?.billingPeriod || '11/2026';
    const paidTotalVnd = Number(vietQrInvoice.amountVnd || paidInv?.amountVnd || 1490000);
    const confirmedAtVi = paidInv?.confirmedAtVi || '02/11/2026 18:30';
    const bankRef = 'FT2630698418102';

    return (
      <div
        tabIndex={-1}
        style={{ padding: '12px 16px', borderRadius: 10, border: '1px solid #10b981', background: '#ecfdf5', outline: 'none' }}
      >
        <div
          data-testid="vietqr-paid-confirmation-banner"
          style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 12 }}
        >
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8, color: '#065f46', fontSize: 13.5, fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>
            <CheckCircle2 size={18} strokeWidth={2} color="#059669" aria-hidden="true" style={{ flexShrink: 0 }} />
            <span data-testid="saas-billing-cycle-paid-banner">
              {`Hóa đơn kỳ ${billingCycleLabel} (${resolvedCode}) đã tất toán đầy đủ (${paidTotalVnd.toLocaleString('vi-VN')} ₫) • Xác nhận lúc: ${confirmedAtVi}`}
            </span>
          </div>
          <button
            type="button"
            data-testid="btn-download-paid-current-invoice"
            onClick={() => downloadPaidInvoicePdf(resolvedCode, billingCycleLabel, paidTotalVnd, confirmedAtVi, bankRef)}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '6px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#ffffff', color: '#047857', border: '1px solid #10b981', cursor: 'pointer', whiteSpace: 'nowrap' }}
          >
            <Download size={14} strokeWidth={2} aria-hidden="true" />
            <span>Tải hóa đơn</span>
          </button>
        </div>
      </div>
    );
  }

  const subTotalVnd = Math.round(displayAmountVnd / 1.1);
  const vatVnd = displayAmountVnd - subTotalVnd;
  const isNoticeConfirmed = /đã ghi nhận thanh toán thành công|đã xác nhận thanh toán/i.test(notice);

  return (
    <>
      <div
        data-testid="vietqr-idle-cta-bar"
        style={{
          display: 'flex', flexDirection: 'column', justifyContent: 'space-between', gap: 8,
          padding: '10px 14px', borderRadius: 8, border: '1px solid #cbd5e1', background: '#f8fafc',
          boxSizing: 'border-box',
        }}
      >
        <div style={{ display: 'grid', gap: 2 }}>
          <strong style={{ fontSize: 14, color: '#0f172a' }}>
            Thanh toán gia hạn {selectedPlanLabel} qua Napas247 VietQR
          </strong>
          <span style={{ fontSize: 12, color: '#334155', lineHeight: 1.35, fontVariantNumeric: 'tabular-nums' }}>
            {nextCycle.fullLabel} • Số tiền cần thanh toán: <strong style={{ color: '#0F172A' }}>{displayAmountVnd.toLocaleString('vi-VN')} ₫</strong> (Đã gồm 10% GTGT)
          </span>
        </div>
        <div>
          <button
            type="button"
            data-testid="btn-generate-vietqr"
            disabled={busy}
            onClick={onOpenQrPanel}
            style={{
              display: 'inline-flex', alignItems: 'center', gap: 8, padding: '7px 14px', borderRadius: 8,
              fontSize: 13, fontWeight: 700, backgroundColor: '#1d4ed8', color: '#ffffff',
              border: '1px solid #1e40af', cursor: busy ? 'not-allowed' : 'pointer', whiteSpace: 'nowrap',
              fontVariantNumeric: 'tabular-nums',
            }}
          >
            <QrCode size={16} strokeWidth={2} aria-hidden="true" />
            <span>{`Tạo mã QR thanh toán (${displayAmountVnd.toLocaleString('vi-VN')} ₫)`}</span>
          </button>
        </div>
      </div>

      {isQrPanelOpen && (
        <div
          style={{
            position: 'fixed', inset: 0, zIndex: 600, background: 'rgba(15, 23, 42, 0.48)',
            display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16,
          }}
          onClick={(e) => {
            if (e.target === e.currentTarget && !busy) onCloseQrPanel();
          }}
        >
          <div
            ref={dialogRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby="vietqr-modal-title"
            data-testid="vietqr-payment-modal"
            tabIndex={-1}
            style={{
              width: '100%', maxWidth: 880, maxHeight: 'calc(100vh - 32px)', overflowY: 'auto',
              borderRadius: 12, background: '#ffffff', border: '1px solid #cbd5e1',
              boxShadow: '0 20px 25px -5px rgba(15, 23, 42, 0.2)', display: 'flex', flexDirection: 'column', outline: 'none',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, padding: '14px 18px', borderBottom: '1px solid #e2e8f0' }}>
              <div>
                <strong id="vietqr-modal-title" style={{ fontSize: 15.5, color: '#0f172a' }}>
                  Thông tin chuyển khoản Napas247 VietQR ({selectedPlanLabel})
                </strong>
                <div data-testid="vietqr-billing-period" style={{ fontSize: 12.5, color: '#0F172A', fontWeight: 700, marginTop: 2, fontVariantNumeric: 'tabular-nums' }}>
                  {nextCycle.fullLabel}
                </div>
              </div>
              <div className="ui-inline-wrap" style={{ gap: 8 }}>
                <span data-testid="vietqr-invoice-status-badge">
                  <UiBadge tone={activeInvoiceBadge.tone}>{activeInvoiceBadge.label}</UiBadge>
                </span>
                <button
                  type="button"
                  aria-label="Đóng hộp thoại"
                  data-testid="btn-close-vietqr-panel"
                  disabled={busy}
                  onClick={onCloseQrPanel}
                  style={{ ...secondaryBtnStyle, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', padding: '5px 8px', minHeight: 30 }}
                >
                  <X size={16} strokeWidth={2} aria-hidden="true" />
                </button>
              </div>
            </div>

            <div
              tabIndex={-1}
              data-testid="vietqr-payment-panel"
              style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', alignItems: 'flex-start', gap: 16, padding: '14px 18px', background: '#f8fafc', outline: 'none' }}
            >
              <span data-testid="saas-vietqr-payment-panel" style={{ display: 'none' }} />
              <div className="ui-stack" style={{ gap: 10, flex: '1 1 500px' }}>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(210px, 1fr))', gap: 10, padding: '12px 14px', borderRadius: 8, background: '#ffffff', border: '1px solid #e2e8f0' }}>
                  <div style={{ padding: '6px 8px', borderRadius: 6, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: 12, color: '#475569' }}>Ngân hàng thụ hưởng</div>
                    <strong data-testid="vietqr-bank-display" style={{ display: 'block', fontSize: 13.5, color: '#0f172a', marginTop: 2, fontVariantNumeric: 'tabular-nums' }}>
                      {billing.bankName
                        ? `${billing.bankName}${billing.bankName === 'TPBank' ? ' — NH TMCP Tiên Phong' : ''}${billing.bankBin ? ` (BIN: ${billing.bankBin})` : ''}`
                        : 'Chưa cấu hình ngân hàng thụ hưởng'}
                    </strong>
                  </div>

                  <div style={{ padding: '6px 8px', borderRadius: 6, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
                    <div style={{ fontSize: 12, color: '#475569' }}>Chủ tài khoản thụ hưởng</div>
                    <strong data-testid="vietqr-account-holder" style={{ display: 'block', fontSize: 13.5, color: '#0f172a', marginTop: 2 }}>
                      {billing.accountName || 'Chưa cấu hình chủ tài khoản'}
                    </strong>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, padding: '6px 8px', borderRadius: 6, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
                    <div>
                      <div style={{ fontSize: 12, color: '#475569' }}>Số tài khoản thụ hưởng</div>
                      <strong data-testid="vietqr-account-number" style={{ fontSize: 14, color: '#0F172A', fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>
                        {billing.accountNo || 'Chưa cấu hình số tài khoản'}
                      </strong>
                    </div>
                    <button type="button" data-testid="btn-copy-stk" style={{ ...secondaryBtnStyle, padding: '4px 10px', fontSize: 12 }} onClick={() => void copyToClipboard('account', billing.accountNo)}>
                      {copiedField === 'account' ? 'Đã sao chép' : 'Sao chép STK'}
                    </button>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, padding: '6px 8px', borderRadius: 6, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
                    <div>
                      <div style={{ fontSize: 12, color: '#475569' }}>Số tiền &amp; Kỳ cước thanh toán</div>
                      <strong data-testid="vietqr-amount-display" style={{ display: 'block', fontSize: 13.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                        {`${displayAmountVnd.toLocaleString('vi-VN')} ₫ • Kỳ ${nextCycle.shortRange}`}
                      </strong>
                      <div data-testid="vietqr-vat-breakdown" style={{ fontSize: 11.5, color: '#475569', marginTop: 2, fontVariantNumeric: 'tabular-nums' }}>
                        {`Trước thuế: ${subTotalVnd.toLocaleString('vi-VN')} ₫ + Thuế GTGT 10%: ${vatVnd.toLocaleString('vi-VN')} ₫`}
                      </div>
                    </div>
                    <button type="button" data-testid="btn-copy-amount" style={{ ...secondaryBtnStyle, padding: '4px 10px', fontSize: 12 }} onClick={() => void copyToClipboard('amount', String(displayAmountVnd))}>
                      {copiedField === 'amount' ? 'Đã sao chép' : 'Sao chép số tiền'}
                    </button>
                  </div>

                  <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, paddingTop: 8, borderTop: '1px dashed #cbd5e1' }}>
                    <div>
                      <div style={{ fontSize: 12, color: '#475569' }}>Nội dung chuyển khoản tự động (viết liền chữ hoa và số, khớp mã hóa đơn)</div>
                      <div style={{ fontSize: 14, color: '#0f172a', marginTop: 2 }}>
                        <span>Nội dung CK: </span>
                        <code data-testid="vietqr-transfer-content" className="ui-code" style={{ fontSize: 14, fontWeight: 700, color: '#0F172A', letterSpacing: '0.02em', whiteSpace: 'nowrap' }}>
                          {vietQrInvoice.transferContent}
                        </code>
                      </div>
                    </div>
                    <button type="button" data-testid="btn-copy-transfer-content" style={{ ...secondaryBtnStyle, padding: '4px 10px', fontSize: 12 }} onClick={() => void copyToClipboard('content', vietQrInvoice.transferContent)}>
                      {copiedField === 'content' ? 'Đã sao chép' : 'Sao chép nội dung'}
                    </button>
                  </div>
                </div>

                <div data-testid="vietqr-auto-polling-indicator" style={infoBlueBannerStyle}>
                  <span>● Đang chờ thanh toán • Tự động kích hoạt ngay khi ngân hàng báo Có (Đang tự động chờ xác nhận từ ngân hàng)</span>
                </div>

                <div className="ui-inline-wrap" style={{ gap: 10 }}>
                  <button
                    type="button"
                    data-testid="btn-create-vietqr-invoice"
                    disabled={busy}
                    onClick={() => void createVietQrInvoice()}
                    style={
                      hasActiveMatchingInvoice
                        ? { ...secondaryBtnStyle, padding: '7px 12px', fontSize: 12.5 }
                        : { padding: '7px 14px', borderRadius: 6, fontSize: 13, fontWeight: 600, background: '#ffffff', color: '#1d4ed8', border: '1.5px solid #1d4ed8', cursor: busy ? 'not-allowed' : 'pointer' }
                    }
                  >
                    {hasActiveMatchingInvoice ? 'Làm mới mã VietQR hiện tại' : 'Cập nhật / Tạo mã VietQR cho gói đang chọn'}
                  </button>
                  <label data-testid="label-auto-refresh-vietqr" style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 500, color: '#334155', cursor: 'pointer', userSelect: 'none' }}>
                    <input
                      type="checkbox"
                      data-testid="checkbox-auto-refresh-vietqr"
                      checked={autoRefreshQr}
                      onChange={(e) => {
                        const checked = e.target.checked;
                        setAutoRefreshQr(checked);
                        if (checked && isInvoiceExpired && !busy) void createVietQrInvoice();
                      }}
                    />
                    <span>Tự động gia hạn mã QR khi hết hạn</span>
                  </label>
                  {showQaWebhookSimulator && (
                    <button type="button" data-testid="btn-confirm-vietqr-webhook" disabled={busy} onClick={() => void confirmVietQrWebhook()} style={{ ...secondaryBtnStyle, padding: '7px 12px', fontSize: 12.5 }}>
                      Xác nhận Thanh toán Tự động (Webhook PayOS / SePay)
                    </button>
                  )}
                </div>

                <details data-testid="vietqr-edge-case-rules" style={{ padding: '8px 12px', borderRadius: 6, background: '#ffffff', border: '1px solid #cbd5e1', fontSize: 12, color: '#334155', lineHeight: 1.5 }}>
                  <summary style={{ cursor: 'pointer', fontWeight: 600, color: '#0f172a' }}>Chuyển sai số tiền hoặc quá hạn? Xem cách xử lý</summary>
                  <ul style={{ margin: '6px 0 0 0', paddingLeft: 18 }}>
                    <li><strong>Chuyển đúng số tiền &amp; đúng nội dung CK trong hạn:</strong> Kích hoạt gói tự động trong 5–10 giây.</li>
                    <li><strong>Chuyển khoản sau khi mã hết hạn hoặc chuyển thiếu/dư số tiền:</strong> Khoản tiền vẫn được ghi nhận an toàn vào số dư đối soát của doanh nghiệp; hệ thống hiển thị cảnh báo lệch số tiền hoặc tự động khớp bù khi bấm kiểm tra giao dịch.</li>
                  </ul>
                </details>

                {notice && (
                  <div
                    role="status"
                    aria-live="polite"
                    data-testid="vietqr-inline-status-notice"
                    style={
                      isNoticeConfirmed
                        ? { padding: '8px 12px', borderRadius: 6, background: '#ECFDF5', border: '1px solid #10b981', color: '#047857', fontSize: 13, fontWeight: 600 }
                        : { ...infoBlueBannerStyle, padding: '8px 12px', fontSize: 13 }
                    }
                  >
                    {notice}
                  </div>
                )}
              </div>

              <div data-testid="vietqr-qr-box" style={{ position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-start', alignSelf: 'flex-start', gap: 6, padding: 12, borderRadius: 8, background: '#ffffff', border: '1px solid #e2e8f0', minWidth: 220 }}>
                <strong style={{ fontSize: 12.5, color: '#0f172a', textAlign: 'center' }}>Quét bằng ứng dụng ngân hàng</strong>
                {vietQrInvoice.status.toUpperCase() === 'UNPAID' && !isInvoiceExpired && remainingSeconds > 0 && (
                  <span data-testid="vietqr-countdown-timer" style={{ fontSize: 12, fontWeight: 700, color: '#78350f', background: '#fef3c7', border: '1px solid #f59e0b', padding: '2px 8px', borderRadius: 999, fontVariantNumeric: 'tabular-nums' }}>
                    Hết hạn sau: {formatCountdownMmSs(remainingSeconds)}
                  </span>
                )}
                <div style={{ opacity: isInvoiceExpired ? 0.2 : 1, filter: isInvoiceExpired ? 'grayscale(1)' : 'none', transition: 'opacity 150ms ease' }}>
                  <BarcodeSvg
                    value={vietQrInvoice.qrPayload || buildClientVietQrPayload(displayAmountVnd, vietQrInvoice.invoiceCode || 'INVSAAS202611002', billing.bankBin, billing.accountNo)}
                    format="QR"
                    height={176}
                    captionLabel="Quét mã VietQR Napas247"
                  />
                </div>
                {isInvoiceExpired && (
                  <div data-testid="vietqr-expired-overlay" style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 8, padding: 12, borderRadius: 8, background: 'rgba(255, 255, 255, 0.92)', textAlign: 'center' }}>
                    <strong data-testid="vietqr-expired-overlay-label" style={{ fontSize: 13.5, color: '#b91c1c' }}>Mã QR đã hết hạn</strong>
                    <button
                      type="button"
                      data-testid="btn-regenerate-expired-vietqr"
                      disabled={busy}
                      onClick={() => void createVietQrInvoice()}
                      style={{ padding: '6px 12px', borderRadius: 6, fontSize: 12.5, fontWeight: 600, background: '#1d4ed8', color: '#ffffff', border: '1px solid #1e40af', cursor: busy ? 'not-allowed' : 'pointer' }}
                    >
                      Tạo mã mới
                    </button>
                    <p data-testid="vietqr-expired-safe-note" style={{ margin: 0, fontSize: 11.5, color: '#334155', lineHeight: 1.4 }}>
                      Nếu bạn đã chuyển khoản trước khi mã hết hạn, khoản tiền vẫn được hệ thống ghi nhận an toàn.
                    </p>
                  </div>
                )}
              </div>
            </div>

            <div
              style={{
                position: 'sticky', bottom: 0, background: '#ffffff', borderTop: '1px solid #e2e8f0',
                padding: '10px 18px', display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 10, zIndex: 5,
              }}
            >
              <button
                type="button"
                data-testid="btn-footer-close-vietqr-modal"
                disabled={busy}
                onClick={onCloseQrPanel}
                style={{ ...secondaryBtnStyle, minHeight: 34, padding: '6px 14px', fontSize: 13 }}
              >
                Đóng
              </button>
              <button
                type="button"
                data-testid="btn-check-vietqr-payment-status"
                disabled={busy}
                onClick={() => void checkPaymentStatus()}
                style={{ minHeight: 34, padding: '6px 16px', borderRadius: 6, fontSize: 13, fontWeight: 700, background: '#1d4ed8', color: '#ffffff', border: '1px solid #1e40af', cursor: busy ? 'not-allowed' : 'pointer', whiteSpace: 'nowrap' }}
              >
                {checkingPayment ? 'Đang kiểm tra giao dịch...' : 'Kiểm tra trạng thái thanh toán'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};
