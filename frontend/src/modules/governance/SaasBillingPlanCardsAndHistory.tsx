import React from 'react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import {
  buildPlanDowngradeWarning,
  formatDateTimeVi,
  resolveInvoiceStatusBadge,
  SAAS_PLAN_CATALOG,
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
  visiblePlans,
  billing,
  selectedPlanCode,
  whTierPct,
  busy,
  handleSelectPlan,
}) => {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12, alignItems: 'stretch' }} data-testid="saas-plan-cards-grid">
      {visiblePlans.map((plan) => {
        const isCurrentPlan = billing.planCode === plan.code;
        const isSelected = plan.code === 'FREE_TRIAL' ? billing.planCode === 'FREE_TRIAL' : selectedPlanCode === plan.code;
        const isRecommendedEnterprise = plan.code === 'ENTERPRISE' && whTierPct >= 100 && !isCurrentPlan;
        const downgradeWarning = buildPlanDowngradeWarning(plan, billing);
        return (
          <div
            key={plan.code}
            data-testid={`plan-card-${plan.code}`}
            onClick={() => {
              if (plan.code !== 'FREE_TRIAL' && !downgradeWarning) handleSelectPlan(plan.code);
            }}
            style={{
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'space-between',
              cursor: plan.code === 'FREE_TRIAL' || Boolean(downgradeWarning) ? 'default' : 'pointer',
              padding: '14px 16px',
              borderRadius: 8,
              border: isSelected ? '2px solid #0284c7' : '1px solid #cbd5e1',
              background: isSelected ? '#f0f9ff' : '#ffffff',
            }}
          >
            <div>
              {isRecommendedEnterprise && (
                <div
                  data-testid="plan-recommendation-badge-ENTERPRISE"
                  style={{
                    marginBottom: 8,
                    padding: '4px 10px',
                    borderRadius: 6,
                    background: '#f0f9ff',
                    border: '1px solid #bae6fd',
                    color: '#0369a1',
                    fontSize: 12,
                    fontWeight: 700,
                  }}
                >
                  ★ Khuyên dùng khi cần mở rộng thêm kho
                </div>
              )}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                <strong style={{ fontSize: 15, color: '#0f172a' }}>{plan.title}</strong>
                {isCurrentPlan ? <UiBadge tone="success">Đang sử dụng</UiBadge> : isSelected && <UiBadge tone="info">Đang chọn</UiBadge>}
              </div>
              <div style={{ fontSize: 16, fontWeight: 700, color: '#0369a1', marginTop: 6, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{plan.price}</div>
              <ul style={{ margin: '8px 0 0 0', paddingLeft: 16, fontSize: 12, color: '#334155', lineHeight: 1.6 }}>
                <li>Tối đa {plan.maxWarehouses.toLocaleString('vi-VN')} kho vận hành</li>
                <li>Tối đa {plan.maxUsers.toLocaleString('vi-VN')} tài khoản phân quyền</li>
                <li>{plan.monthlyOrderQuota.toLocaleString('vi-VN')} đơn hàng/tháng</li>
                <li>{plan.maxProducts.toLocaleString('vi-VN')} mã mặt hàng</li>
              </ul>
              {downgradeWarning && (
                <div
                  data-testid={`plan-downgrade-warning-${plan.code}`}
                  style={{
                    marginTop: 8,
                    padding: '6px 10px',
                    borderRadius: 6,
                    background: '#fef2f2',
                    border: '1px solid #fecaca',
                    color: '#991b1b',
                    fontSize: 12,
                    fontWeight: 600,
                    lineHeight: 1.45,
                  }}
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
                    style={{
                      width: '100%',
                      padding: '8px 12px',
                      borderRadius: 6,
                      background: '#ecfdf5',
                      border: '1px solid #a7f3d0',
                      color: '#065f46',
                      fontWeight: 600,
                      fontSize: 13,
                      textAlign: 'center',
                      boxSizing: 'border-box',
                    }}
                  >
                    ✓ Gói dịch vụ đang áp dụng
                  </div>
                ) : (
                  <button
                    type="button"
                    data-testid={`btn-select-plan-${plan.code}`}
                    disabled={busy || Boolean(downgradeWarning)}
                    style={{
                      width: '100%',
                      padding: '8px 12px',
                      borderRadius: 6,
                      fontSize: 13,
                      fontWeight: 600,
                      cursor: busy || Boolean(downgradeWarning) ? 'not-allowed' : 'pointer',
                      background: downgradeWarning ? '#e2e8f0' : isSelected ? '#0284c7' : '#ffffff',
                      color: downgradeWarning ? '#475569' : isSelected ? '#ffffff' : '#0284c7',
                      border: downgradeWarning ? '1px solid #cbd5e1' : '1.5px solid #0284c7',
                    }}
                    onClick={(e) => {
                      e.stopPropagation();
                      handleSelectPlan(plan.code);
                    }}
                  >
                    {downgradeWarning ? 'Không khả dụng để hạ cấp' : plan.buttonLabel}
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
  billing,
  currentCycleText,
  nextCycle,
}) => {
  return (
    <UiCard
      title="Lịch sử hóa đơn & đối soát thanh toán"
      subtitle="Danh sách các hóa đơn VietQR đã khởi tạo, kỳ cước áp dụng, nội dung chuyển khoản đối soát tự động và trạng thái kích hoạt gói dịch vụ"
    >
      {billing.recentInvoices.length === 0 ? (
        <p style={{ fontSize: 13, color: '#64748b', margin: 0 }} data-testid="saas-invoice-history-empty">
          Chưa có hóa đơn thanh toán nào trong kỳ hiện tại. Bấm &ldquo;Cập nhật / Tạo mã VietQR cho gói đang chọn&rdquo; để khởi tạo lệnh thanh toán mới.
        </p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table className="ui-table ui-table-compact" data-testid="saas-invoice-history-table">
            <thead>
              <tr>
                <th style={{ whiteSpace: 'nowrap' }}>Mã hóa đơn</th>
                <th style={{ whiteSpace: 'nowrap' }}>Gói dịch vụ</th>
                <th style={{ whiteSpace: 'nowrap' }}>Kỳ cước áp dụng</th>
                <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Số tiền</th>
                <th style={{ whiteSpace: 'nowrap' }}>Nội dung chuyển khoản</th>
                <th style={{ whiteSpace: 'nowrap' }}>Thời gian tạo</th>
                <th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {billing.recentInvoices.map((inv) => {
                const badge = resolveInvoiceStatusBadge(inv.status);
                const isPaidInv = inv.status.toUpperCase() === 'PAID';
                const periodCellText = isPaidInv ? currentCycleText : nextCycle.shortRange;
                const periodCellTooltip = isPaidInv
                  ? `Kỳ cước áp dụng: ${currentCycleText}`
                  : `${nextCycle.shortRange} • Hạn: ${nextCycle.dueDateText}`;
                return (
                  <tr key={inv.invoiceCode}>
                    <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{inv.invoiceCode}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>{inv.planName}</td>
                    <td
                      style={{ fontVariantNumeric: 'tabular-nums', fontSize: 12.5, color: '#334155', whiteSpace: 'nowrap' }}
                      title={periodCellTooltip}
                    >
                      {periodCellText}
                    </td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{inv.amountVnd.toLocaleString('vi-VN')} đ</td>
                    <td
                      style={{
                        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace',
                        fontWeight: 700,
                        color: '#0369a1',
                        letterSpacing: '0.02em',
                      }}
                    >
                      {inv.transferContent}
                    </td>
                    <td style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{formatDateTimeVi(inv.paidAtUtc || inv.createdAtUtc)}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <UiBadge tone={badge.tone}>{badge.label}</UiBadge>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </UiCard>
  );
};
