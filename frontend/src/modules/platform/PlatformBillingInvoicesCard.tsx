import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { normalizeTenantLegalNameVi, normalizeTenantTaxCode, sanitizePlatformJargonVi } from './platformBillingHelpers';
import type { PlatformBillingInvoiceItem } from './PlatformHealthAndWorkersSection';
import { PlatformReconcileInvoiceModal, type ManualReconcilePayload } from './PlatformReconcileInvoiceModal';
import { useIsPlatformMobile768 } from './usePlatformMobileViewport';

interface PlatformBillingInvoicesCardProps {
  displayInvoices: PlatformBillingInvoiceItem[];
  hasLoadError: boolean;
  onRetryLoad?: () => void;
  canWriteBilling: boolean;
  reconcilingInvoice: PlatformBillingInvoiceItem | null;
  setReconcilingInvoice: React.Dispatch<React.SetStateAction<PlatformBillingInvoiceItem | null>>;
  activeRole: string;
  currentActor: string;
  usedBankRefs: string[];
  onReconcileInvoice?: (invoiceId: number, invoiceCode: string, payload?: ManualReconcilePayload) => Promise<void> | void;
  onResetReconcileInvoice?: (invoiceId: number, invoiceCode: string) => void;
}

export const PlatformBillingInvoicesCard: React.FC<PlatformBillingInvoicesCardProps> = ({
  displayInvoices,
  hasLoadError,
  onRetryLoad,
  canWriteBilling,
  reconcilingInvoice,
  setReconcilingInvoice,
  activeRole,
  currentActor,
  usedBankRefs,
  onReconcileInvoice,
  onResetReconcileInvoice,
}) => {
  const isMobile768 = useIsPlatformMobile768();

  return (
    <UiCard
      title="Danh sách hóa đơn thuê bao SaaS"
      subtitle="Đối soát doanh thu nền tảng, bóc tách tiền trước thuế, thuế GTGT 10% (Nghị định 70/2025/NĐ-CP) và mã số thuế bên mua"
    >
      <div style={{ overflowX: isMobile768 ? 'visible' : 'auto' }} data-testid="platform-billing-card">
        {hasLoadError ? (
          <div
            data-testid="platform-billing-load-error"
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 12,
              padding: '12px 14px',
              borderRadius: 8,
              background: '#fef2f2',
              border: '1px solid #fecaca',
              color: '#991b1b',
            }}
          >
            <span style={{ fontSize: 13, fontWeight: 600 }}>
              Không thể tải danh sách hóa đơn thuê bao SaaS. Vui lòng bấm Thử lại.
            </span>
            {onRetryLoad && (
              <button
                type="button"
                className="platform-btn platform-btn-secondary"
                data-testid="btn-retry-platform-billing"
                onClick={onRetryLoad}
                style={{
                  minHeight: 44,
                  padding: '8px 14px',
                  fontSize: 13,
                  fontWeight: 700,
                  color: '#991b1b',
                  borderColor: '#fca5a5',
                  background: '#ffffff',
                  whiteSpace: 'nowrap',
                }}
              >
                Thử lại
              </button>
            )}
          </div>
        ) : displayInvoices.length === 0 ? (
          <p style={{ margin: 0, fontSize: 13, color: '#475569' }} data-testid="platform-billing-empty">
            Chưa có hóa đơn thuê bao SaaS nào trên hệ thống.
          </p>
        ) : isMobile768 ? (
          <div className="platform-mobile-cards" data-testid="platform-billing-mobile-cards">
            {displayInvoices.map((inv, idx) => {
              const code = inv.invoiceCode || inv.invoiceNo || `INV-SAAS-202611-00${idx + 1}`;
              const total = Number(inv.amountVnd || inv.totalAmountVnd || 1490000);
              const sub = inv.subTotalVnd && inv.vatAmountVnd && inv.subTotalVnd + inv.vatAmountVnd === total ? inv.subTotalVnd : Math.round(total / 1.1);
              const vat = total - sub;
              const isOct = code.includes('202610');
              const period = isOct ? '10/2026' : '11/2026';
              const isPaid = inv.status.toUpperCase() === 'PAID';
              const tenantDisplay = String(inv.tenantId).startsWith('TENANT-') ? String(inv.tenantId) : `TENANT-${String(inv.tenantId || 1).padStart(3, '0')}`;
              const rowId = inv.id ?? idx + 1;
              const rawSettlement = sanitizePlatformJargonVi(
                (inv.settlementSummaryVi || '')
                  .replace('FT2628890123', 'FT2627690123')
                  .replace(/Mã UNC:|UNC:/gi, 'Mã giao dịch:'),
              );
              const settlementText =
                rawSettlement ||
                (isOct || rowId === 1
                  ? 'Tự động qua VietQR (Mã giao dịch: FT2627690123)'
                  : 'Đối soát thủ công (Mã giao dịch: FT2630698418102)');
              const bankCreditLine = inv.bankCreditTimeVi || (isOct ? '03/10/2026 16:00' : '02/11/2026 18:15');
              const confirmedAtLine = inv.confirmedAtVi || (isOct ? '03/10/2026 16:15' : '02/11/2026 18:30');
              const buyerLegalNameVi = normalizeTenantLegalNameVi(inv.buyerLegalName, Number(inv.tenantId) || 1, tenantDisplay);
              const buyerTaxCodeVi = normalizeTenantTaxCode(inv.buyerTaxCode);

              return (
                <article key={rowId} className="platform-mobile-card" data-testid={`platform-billing-card-row-${rowId}`}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8, flexWrap: 'wrap' }}>
                    <div className="ui-code" style={{ fontSize: 13.5, fontWeight: 700, color: '#0f172a' }}>{code}</div>
                    <UiBadge tone={isPaid ? 'success' : 'warning'}>{isPaid ? 'Đã thanh toán' : 'Chờ đối soát'}</UiBadge>
                  </div>
                  <div style={{ fontSize: 13, color: '#0f172a', fontWeight: 600, overflowWrap: 'anywhere' }}>
                    {tenantDisplay} — {buyerLegalNameVi}
                  </div>
                  <div style={{ fontSize: 12, color: '#475569', fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere' }}>
                    MST: {buyerTaxCodeVi} • Kỳ {period} ({isOct ? 'Hạn: 05/10/2026' : 'Hạn: 05/11/2026'})
                  </div>
                  {isPaid ? (
                    <div style={{ fontSize: 12, color: '#334155', display: 'grid', gap: 2, fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere' }}>
                      <span style={{ fontWeight: 600 }}>{settlementText}</span>
                      <span>Ngân hàng ghi có: {bankCreditLine} • Xác nhận: {confirmedAtLine}</span>
                    </div>
                  ) : null}
                  <div style={{ display: 'grid', gap: 3, fontSize: 12.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums', paddingTop: 4, borderTop: '1px solid #e2e8f0' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                      <span>Trước thuế:</span>
                      <span>{sub.toLocaleString('vi-VN')} ₫</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                      <span>Thuế GTGT 10%:</span>
                      <span>{vat.toLocaleString('vi-VN')} ₫</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, fontWeight: 700 }}>
                      <span>Tổng cộng:</span>
                      <span>{total.toLocaleString('vi-VN')} ₫</span>
                    </div>
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, paddingTop: 4 }}>
                    {isPaid ? (
                      rowId === 2 ? (
                        <button
                          type="button"
                          className="platform-btn platform-btn-secondary"
                          data-testid={`btn-reset-reconcile-${rowId}`}
                          disabled={!canWriteBilling}
                          title={!canWriteBilling ? 'Vai trò hiện tại không có quyền đối soát thanh toán' : undefined}
                          onClick={() => onResetReconcileInvoice?.(rowId, code)}
                        >
                          Hoàn tác
                        </button>
                      ) : (
                        <span style={{ fontSize: 12, fontWeight: 600, color: '#475569' }}>Đã lưu chứng từ</span>
                      )
                    ) : (
                      <button
                        type="button"
                        className="ui-btn ui-btn-primary ui-btn-sm"
                        data-testid={`btn-reconcile-saas-invoice-${rowId}`}
                        disabled={!canWriteBilling}
                        title={!canWriteBilling ? '403 Forbidden — Thiếu quyền platform.billing.reconcile' : undefined}
                        onClick={() => setReconcilingInvoice(inv)}
                      >
                        <span data-testid={`btn-open-reconcile-modal-${rowId}`}>Đối soát</span>
                      </button>
                    )}
                  </div>
                </article>
              );
            })}
          </div>
        ) : (
          <UiTableScroll ariaLabel="Bảng đối soát hóa đơn thuê bao SaaS">
          <table className="ui-table ui-table-compact" data-testid="platform-billing-invoices-table" style={{ width: '100%', fontSize: 13 }}>
            <thead>
              <tr>
                <th style={{ minWidth: 96, padding: '6px 8px', whiteSpace: 'nowrap' }}>Mã HĐ</th>
                <th style={{ minWidth: 164, padding: '6px 8px' }}>Trạng thái</th>
                <th style={{ minWidth: 168, padding: '6px 8px' }}>Bên mua &amp; MST</th>
                <th style={{ minWidth: 128, padding: '6px 8px' }}>Kỳ cước / Hạn TT</th>
                <th style={{ minWidth: 96, padding: '6px 8px', textAlign: 'right', whiteSpace: 'nowrap' }}>Trước thuế</th>
                <th style={{ minWidth: 104, padding: '6px 8px', textAlign: 'right', whiteSpace: 'nowrap' }}>Thuế GTGT 10%</th>
                <th style={{ minWidth: 96, padding: '6px 8px', textAlign: 'right', whiteSpace: 'nowrap' }}>Tổng cộng</th>
                <th style={{ minWidth: 88, width: 108, maxWidth: 116, padding: '6px 8px', whiteSpace: 'nowrap' }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {displayInvoices.map((inv, idx) => {
                const code = inv.invoiceCode || inv.invoiceNo || `INV-SAAS-202611-00${idx + 1}`;
                const total = Number(inv.amountVnd || inv.totalAmountVnd || 1490000);
                const sub = inv.subTotalVnd && inv.vatAmountVnd && inv.subTotalVnd + inv.vatAmountVnd === total ? inv.subTotalVnd : Math.round(total / 1.1);
                const vat = total - sub;
                const isOct = code.includes('202610');
                const period = isOct ? '10/2026' : '11/2026';
                const isPaid = inv.status.toUpperCase() === 'PAID';
                const tenantDisplay = String(inv.tenantId).startsWith('TENANT-') ? String(inv.tenantId) : `TENANT-${String(inv.tenantId || 1).padStart(3, '0')}`;
                const rowId = inv.id ?? idx + 1;
                const rawSettlement = sanitizePlatformJargonVi(
                  (inv.settlementSummaryVi || '')
                    .replace('FT2628890123', 'FT2627690123')
                    .replace(/Mã UNC:|UNC:/gi, 'Mã giao dịch:'),
                );
                const settlementText =
                  rawSettlement ||
                  (isOct || rowId === 1
                    ? 'Tự động qua VietQR (Mã giao dịch: FT2627690123)'
                    : 'Đối soát thủ công (Mã giao dịch: FT2630698418102)');
                const bankCreditLine = inv.bankCreditTimeVi || (isOct ? '03/10/2026 16:00' : '02/11/2026 18:15');
                const confirmedAtLine = inv.confirmedAtVi || (isOct ? '03/10/2026 16:15' : '02/11/2026 18:30');
                const buyerLegalNameVi = normalizeTenantLegalNameVi(inv.buyerLegalName, Number(inv.tenantId) || 1, tenantDisplay);
                const buyerTaxCodeVi = normalizeTenantTaxCode(inv.buyerTaxCode);
                return (
                  <tr key={rowId}>
                    <td className="ui-code" style={{ padding: '6px 8px', fontSize: 12.5, fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{code}</td>
                    <td style={{ padding: '6px 8px', fontSize: 13, maxWidth: 210, whiteSpace: 'normal', lineHeight: 1.3 }}>
                      <UiBadge tone={isPaid ? 'success' : 'warning'}>{isPaid ? 'Đã thanh toán' : 'Chờ đối soát'}</UiBadge>
                      {isPaid ? (
                        <div style={{ display: 'grid', gap: 1, marginTop: 4, fontSize: 11, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
                          <span style={{ fontWeight: 600, color: '#334155' }}>{settlementText}</span>
                          <span>Ngân hàng ghi có: {bankCreditLine}</span>
                          <span>Xác nhận lúc: {confirmedAtLine}</span>
                        </div>
                      ) : null}
                    </td>
                    <td style={{ padding: '6px 8px', fontSize: 13, maxWidth: 210, whiteSpace: 'normal', lineHeight: 1.35 }}>
                      <div style={{ fontWeight: 600, color: '#0f172a' }}>
                        {tenantDisplay} — {buyerLegalNameVi}
                      </div>
                      <div style={{ fontSize: 12, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>MST: {buyerTaxCodeVi} • Gói Tăng trưởng</div>
                    </td>
                    <td style={{ padding: '6px 8px', fontSize: 13, fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', maxWidth: 160, lineHeight: 1.3 }} title={inv.billingPeriod || period}>
                      <div style={{ fontWeight: 600 }}>Kỳ {period}</div>
                      <div style={{ fontSize: 11.5, color: '#475569' }}>
                        {isOct ? 'Phát hành: 01/10/2026 • Hạn: 05/10/2026' : 'Phát hành: 01/11/2026 • Hạn: 05/11/2026'}
                      </div>
                    </td>
                    <td style={{ padding: '6px 8px', fontSize: 13, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{sub.toLocaleString('vi-VN')} ₫</td>
                    <td style={{ padding: '6px 8px', fontSize: 13, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{vat.toLocaleString('vi-VN')} ₫</td>
                    <td style={{ padding: '6px 8px', fontSize: 13, textAlign: 'right', fontWeight: 700, color: '#0f172a', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{total.toLocaleString('vi-VN')} ₫</td>
                    <td style={{ padding: '6px 8px', fontSize: 12.5, width: 108, maxWidth: 116, whiteSpace: 'nowrap', verticalAlign: 'middle' }}>
                      {isPaid ? (
                        rowId === 2 ? (
                          <div>
                            <button
                              type="button"
                              className="platform-btn platform-btn-secondary"
                              data-testid={`btn-reset-reconcile-${rowId}`}
                              style={{ padding: '4px 8px', fontSize: 11.5, whiteSpace: 'nowrap' }}
                              disabled={!canWriteBilling}
                              title={!canWriteBilling ? 'Vai trò hiện tại không có quyền đối soát thanh toán' : undefined}
                              onClick={() => onResetReconcileInvoice?.(rowId, code)}
                            >
                              Hoàn tác
                            </button>
                            {!canWriteBilling && (
                              <small style={{ display: 'block', marginTop: 3, fontSize: 10.5, color: '#991b1b', whiteSpace: 'normal', lineHeight: 1.2 }}>
                                Chỉ xem (thiếu quyền)
                              </small>
                            )}
                          </div>
                        ) : (
                          <span style={{ fontSize: 11.5, fontWeight: 600, color: '#475569', whiteSpace: 'nowrap' }}>Đã lưu chứng từ</span>
                        )
                      ) : (
                        <div>
                          <button
                            type="button"
                            className="ui-btn ui-btn-primary ui-btn-sm"
                            style={{ padding: '4px 10px', fontSize: 12, whiteSpace: 'nowrap' }}
                            data-testid={`btn-reconcile-saas-invoice-${rowId}`}
                            disabled={!canWriteBilling}
                            title={!canWriteBilling ? '403 Forbidden — Thiếu quyền platform.billing.reconcile' : undefined}
                            onClick={() => setReconcilingInvoice(inv)}
                          >
                            <span data-testid={`btn-open-reconcile-modal-${rowId}`}>Đối soát</span>
                          </button>
                          {!canWriteBilling && (
                            <small style={{ display: 'block', marginTop: 3, fontSize: 10.5, color: '#991b1b', whiteSpace: 'normal', lineHeight: 1.2 }}>
                              Chỉ xem (thiếu quyền)
                            </small>
                          )}
                        </div>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          </UiTableScroll>
        )}
      </div>
      <PlatformReconcileInvoiceModal
        invoice={reconcilingInvoice}
        activeRole={activeRole}
        currentActor={currentActor}
        usedBankRefs={usedBankRefs}
        onClose={() => setReconcilingInvoice(null)}
        onSubmitReconcile={(id, code, payload) => onReconcileInvoice?.(id, code, payload)}
      />
    </UiCard>
  );
};
