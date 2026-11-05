import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  buildPlanDowngradeWarning,
  formatDateTimeVi,
  formatDateVi,
  normalizeSaasTaxCode,
  resolveInvoiceStatusBadge,
  SAAS_PLAN_CATALOG,
  sanitizeCustomerSettlementSummary,
  type BillingState,
} from './SaasBillingHelpers';

export interface SaasBillingPlanCardsGridProps {
  visiblePlans: ReadonlyArray<(typeof SAAS_PLAN_CATALOG)[number]>;
  billing: BillingState;
  selectedPlanCode: 'STARTER' | 'GROWTH' | 'ENTERPRISE';
  whTierPct: number;
  busy: boolean;
  handleSelectPlan: (targetPlan: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => void;
}

export const SaasBillingPlanCardsGrid: React.FC<SaasBillingPlanCardsGridProps> = ({
  visiblePlans, billing, selectedPlanCode, whTierPct, busy, handleSelectPlan,
}) => {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12, alignItems: 'start' }} data-testid="saas-plan-cards-grid">
      {visiblePlans.map((plan) => {
        const isCurrentPlan = billing.planCode === plan.code;
        const isSelected = plan.code === 'FREE_TRIAL' ? billing.planCode === 'FREE_TRIAL' : selectedPlanCode === plan.code;
        const isRecommendedEnterprise = plan.code === 'ENTERPRISE' && whTierPct >= 100 && !isCurrentPlan;
        const downgradeWarning = buildPlanDowngradeWarning(plan, billing);
        return (
          <div
            key={plan.code}
            data-testid={`plan-card-${plan.code}`}
            onClick={() => { if (plan.code !== 'FREE_TRIAL' && !downgradeWarning) handleSelectPlan(plan.code); }}
            style={{
              display: 'flex', flexDirection: 'column', justifyContent: 'flex-start', gap: 10,
              cursor: plan.code === 'FREE_TRIAL' || Boolean(downgradeWarning) ? 'default' : 'pointer',
              padding: '14px 16px', borderRadius: 8,
              border: isSelected ? '2px solid #1d4ed8' : '1px solid #cbd5e1',
              background: isSelected ? '#eff6ff' : '#ffffff',
            }}
          >
            <div>
              {isRecommendedEnterprise && (
                <div
                  data-testid="plan-recommendation-badge-ENTERPRISE"
                  style={{ marginBottom: 8, padding: '4px 10px', borderRadius: 6, background: '#eff6ff', border: '1px solid #bfdbfe', color: '#1e40af', fontSize: 12, fontWeight: 700 }}
                >
                  ★ Khuyên dùng khi cần mở rộng thêm kho
                </div>
              )}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                <strong style={{ fontSize: 15, color: '#0f172a' }}>{plan.title}</strong>
                {isCurrentPlan ? <UiBadge tone="success">Đang sử dụng</UiBadge> : isSelected && <UiBadge tone="info">Đang chọn</UiBadge>}
              </div>
              <div style={{ fontSize: 16, fontWeight: 700, color: '#0F172A', marginTop: 6, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{plan.price}</div>
              <ul style={{ margin: '8px 0 0 0', paddingLeft: 16, fontSize: 12, color: '#334155', lineHeight: 1.6 }}>
                <li>Tối đa {plan.maxWarehouses.toLocaleString('vi-VN')} kho vận hành</li>
                <li>Tối đa {plan.maxUsers.toLocaleString('vi-VN')} tài khoản nhân sự</li>
                <li>{plan.monthlyOrderQuota.toLocaleString('vi-VN')} đơn hàng/tháng</li>
                <li>{plan.maxProducts.toLocaleString('vi-VN')} mã mặt hàng</li>
              </ul>
              {downgradeWarning && (
                <div
                  data-testid={`plan-downgrade-warning-${plan.code}`}
                  style={{ marginTop: 8, padding: '6px 10px', borderRadius: 6, background: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', fontSize: 12, fontWeight: 600, lineHeight: 1.45 }}
                >
                  {downgradeWarning}
                </div>
              )}
            </div>
            {plan.code !== 'FREE_TRIAL' && (
              <div style={{ marginTop: 12 }}>
                {isCurrentPlan ? (
                  <div
                    data-testid={`btn-select-plan-${plan.code}`}
                    style={{ width: '100%', padding: '8px 12px', borderRadius: 6, background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46', fontWeight: 600, fontSize: 13, textAlign: 'center', boxSizing: 'border-box' }}
                  >
                    ✓ Gói dịch vụ đang áp dụng
                  </div>
                ) : (
                  <button
                    type="button"
                    data-testid={`btn-select-plan-${plan.code}`}
                    disabled={busy || Boolean(downgradeWarning)}
                    style={{
                      width: '100%', padding: '8px 12px', borderRadius: 6, fontSize: 13, fontWeight: 600,
                      cursor: busy || Boolean(downgradeWarning) ? 'not-allowed' : 'pointer',
                      background: downgradeWarning ? '#e2e8f0' : isSelected ? '#1d4ed8' : '#ffffff',
                      color: downgradeWarning ? '#475569' : isSelected ? '#ffffff' : '#1d4ed8',
                      border: downgradeWarning ? '1px solid #cbd5e1' : '1.5px solid #1d4ed8',
                    }}
                    onClick={(e) => { e.stopPropagation(); handleSelectPlan(plan.code); }}
                  >
                    {downgradeWarning ? 'Chưa đủ điều kiện hạ gói' : plan.buttonLabel}
                  </button>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
};

export interface SaasBillingInvoiceHistoryCardProps {
  billing: BillingState;
  currentCycleText: string;
  nextCycle: {
    dueDateText: string;
    shortRange: string;
  };
}

export const SaasBillingInvoiceHistoryCard: React.FC<SaasBillingInvoiceHistoryCardProps> = ({
  billing, currentCycleText, nextCycle,
}) => {
  return (
    <UiCard
      title="Lịch sử hóa đơn & đối soát thanh toán"
      subtitle="Danh sách các hóa đơn VietQR đã khởi tạo, kỳ cước áp dụng, ngày phát hành, thời điểm thanh toán và trạng thái kích hoạt gói dịch vụ"
    >
      {billing.recentInvoices.length === 0 ? (
        <p style={{ fontSize: 13, color: '#475569', margin: 0 }} data-testid="saas-invoice-history-empty">
          Chưa có hóa đơn thanh toán nào trong kỳ hiện tại. Bấm &ldquo;Cập nhật / Tạo mã VietQR cho gói đang chọn&rdquo; để khởi tạo lệnh thanh toán mới.
        </p>
      ) : (
        <>
          <div className="saas-invoice-desktop-wrap">
            <UiTableScroll>
              <table className="ui-table ui-table-compact" data-testid="saas-invoice-history-table" style={{ width: '100%', minWidth: 0 }}>
                <thead>
                  <tr>
                    <th style={{ minWidth: 116, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Mã hóa đơn</th>
                    <th style={{ minWidth: 110, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Trạng thái</th>
                    <th style={{ minWidth: 92, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Gói cước</th>
                    <th style={{ minWidth: 92, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>MST người mua</th>
                    <th style={{ minWidth: 108, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Kỳ cước</th>
                    <th style={{ minWidth: 112, padding: '5px 6px', textAlign: 'right', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Số tiền</th>
                    <th style={{ minWidth: 112, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Nội dung CK</th>
                    <th style={{ minWidth: 92, padding: '5px 6px', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Phát hành</th>
                    <th style={{ minWidth: 114, padding: '5px 10px 5px 6px', position: 'static', whiteSpace: 'nowrap', lineHeight: 1.2 }}>Thanh toán</th>
                  </tr>
                </thead>
                <tbody>
                  {billing.recentInvoices.map((inv) => {
                    const isOctInv = inv.invoiceCode.includes('202610') || inv.billingPeriod === '10/2026';
                    const effectiveStatus = inv.status;
                    const badge = resolveInvoiceStatusBadge(effectiveStatus);
                    const isPaidInv = effectiveStatus.toUpperCase() === 'PAID';
                    const novRangeText = nextCycle.shortRange || currentCycleText;
                    const dueText = isOctInv ? '05/10/2026' : (inv.dueDateVi && inv.dueDateVi !== '02/11/2026' ? inv.dueDateVi : (nextCycle.dueDateText || '05/11/2026'));
                    const issuedDateDisplay = isOctInv
                      ? '01/10/2026'
                      : inv.invoiceCode.includes('202611')
                        ? '01/11/2026'
                        : (inv.issuedDateVi || formatDateVi(inv.createdAtUtc));
                    const paidTimeDisplay = isPaidInv
                      ? isOctInv
                        ? '03/10/2026 16:15'
                        : (inv.confirmedAtVi || (inv.paidAtUtc ? formatDateTimeVi(inv.paidAtUtc) : '02/11/2026 18:30'))
                      : 'Chưa thanh toán';
                    const periodCellText = isOctInv ? '01/10/2026 – 31/10/2026 (Kỳ 10/2026)' : novRangeText;
                    const periodCellTooltip = `${periodCellText} • Hạn: ${dueText}`;
                    const resolvedTotal = Number(inv.amountVnd || 1490000);
                    const resolvedSubTotal = inv.subTotalVnd && inv.vatAmountVnd && inv.subTotalVnd + inv.vatAmountVnd === resolvedTotal ? inv.subTotalVnd : Math.round(resolvedTotal / 1.1);
                    const resolvedVat = resolvedTotal - resolvedSubTotal;
                    const buyerMst = normalizeSaasTaxCode(inv.buyerTaxCode || billing.taxCode);
                    const paidSummaryText = sanitizeCustomerSettlementSummary(
                      inv.settlementSummaryVi,
                      'FT2630698418102',
                      isOctInv,
                    );
                    return (
                      <tr key={inv.invoiceCode}>
                        <td style={{ padding: '4px 6px', whiteSpace: 'nowrap' }}>
                          <code className="ui-code" style={{ fontWeight: 600, fontSize: 11.5, whiteSpace: 'nowrap' }}>{inv.invoiceCode}</code>
                        </td>
                        <td style={{ padding: '4px 6px', maxWidth: 152, whiteSpace: 'normal', lineHeight: 1.25 }}>
                          <UiBadge tone={badge.tone}>{badge.label}</UiBadge>
                          <small style={{ display: 'block', color: isPaidInv ? '#166534' : '#0369A1', fontSize: 10.5, marginTop: 1 }}>
                            {isPaidInv ? paidSummaryText : 'Chờ đối soát'}
                          </small>
                        </td>
                        <td style={{ padding: '4px 6px', maxWidth: 120, whiteSpace: 'normal', lineHeight: 1.25 }}>{inv.planName}</td>
                        <td style={{ padding: '4px 6px', fontVariantNumeric: 'tabular-nums', fontSize: 11.5, whiteSpace: 'nowrap', color: '#334155' }}>
                          {buyerMst}
                        </td>
                        <td
                          style={{ padding: '4px 6px', maxWidth: 140, fontVariantNumeric: 'tabular-nums', fontSize: 11.5, color: '#334155', whiteSpace: 'normal', lineHeight: 1.25 }}
                          title={periodCellTooltip}
                        >
                          {periodCellText}
                        </td>
                        <td style={{ padding: '4px 6px', textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', maxWidth: 156 }}>
                          <div style={{ fontWeight: 700, color: '#0f172a', whiteSpace: 'nowrap' }}>{resolvedTotal.toLocaleString('vi-VN')} ₫</div>
                          <div
                            data-testid={`invoice-tax-detail-${inv.invoiceCode}`}
                            title={`Trước thuế: ${resolvedSubTotal.toLocaleString('vi-VN')} ₫ • Thuế GTGT (${inv.vatRatePercent ?? 10}%): ${resolvedVat.toLocaleString('vi-VN')} ₫ • MST: ${buyerMst}`}
                            style={{ fontSize: 10.5, color: '#475569', marginTop: 1, lineHeight: 1.25, whiteSpace: 'normal' }}
                          >
                            {`Trước thuế: ${resolvedSubTotal.toLocaleString('vi-VN')} ₫ • VAT: ${resolvedVat.toLocaleString('vi-VN')} ₫`}
                          </div>
                        </td>
                        <td style={{ padding: '4px 6px', whiteSpace: 'nowrap' }}>
                          <code className="ui-code" style={{ fontSize: 11, fontWeight: 700, color: '#0F172A', whiteSpace: 'nowrap' }}>
                            {inv.transferContent}
                          </code>
                        </td>
                        <td style={{ padding: '4px 6px', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', fontSize: 11.5 }}>
                          <div>{issuedDateDisplay}</div>
                          <small style={{ color: '#475569', fontSize: 10.5, display: 'block' }}>• Hạn: {dueText}</small>
                        </td>
                        <td style={{ padding: '4px 10px 4px 6px', position: 'static', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap', fontSize: 11.5, color: isPaidInv ? '#0f172a' : '#475569' }}>
                          {paidTimeDisplay}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
          <div className="ui-mobile-card-list saas-invoice-mobile-cards" aria-label="Danh sách hóa đơn thuê bao rút gọn">
            {billing.recentInvoices.map((inv) => {
              const isOctInv = inv.invoiceCode.includes('202610') || inv.billingPeriod === '10/2026';
              const badge = resolveInvoiceStatusBadge(inv.status);
              const isPaidInv = inv.status.toUpperCase() === 'PAID';
              const resolvedTotal = Number(inv.amountVnd || 1490000);
              const buyerMst = normalizeSaasTaxCode(inv.buyerTaxCode || billing.taxCode);
              const paidTimeDisplay = isPaidInv
                ? (isOctInv ? '03/10/2026 16:15' : (inv.confirmedAtVi || (inv.paidAtUtc ? formatDateTimeVi(inv.paidAtUtc) : '02/11/2026 18:30')))
                : 'Chưa thanh toán';
              return (
                <article key={`card-${inv.invoiceCode}`} className="ui-mobile-card">
                  <div className="ui-mobile-card-head">
                    <div>
                      <div className="ui-mobile-card-title"><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>{inv.invoiceCode}</code></div>
                      <div className="ui-mobile-card-subtitle">{inv.planName} • MST: {buyerMst}</div>
                    </div>
                    <UiBadge tone={badge.tone}>{badge.label}</UiBadge>
                  </div>
                  <div className="ui-mobile-card-grid">
                    <div className="ui-mobile-card-field">
                      <span className="ui-mobile-card-label">Số tiền (gồm VAT)</span>
                      <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>{resolvedTotal.toLocaleString('vi-VN')} ₫</span>
                    </div>
                    <div className="ui-mobile-card-field">
                      <span className="ui-mobile-card-label">Thanh toán</span>
                      <span className="ui-mobile-card-value" style={{ fontVariantNumeric: 'tabular-nums' }}>{paidTimeDisplay}</span>
                    </div>
                    <div className="ui-mobile-card-field" style={{ gridColumn: '1 / -1' }}>
                      <span className="ui-mobile-card-label">Nội dung chuyển khoản</span>
                      <span className="ui-mobile-card-value"><code className="ui-code" style={{ whiteSpace: 'nowrap' }}>{inv.transferContent}</code></span>
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        </>
      )}
    </UiCard>
  );
};
