import React from 'react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import {
  formatDateVi,
  resolveSubscriptionStatusLabel,
  secondaryBtnStyle,
  type BillingState,
} from './SaasBillingHelpers';

export interface QuotaGaugeItem {
  key: string;
  title: string;
  unit: string;
  used: number;
  max: number;
  tier: {
    label: string;
    tone: 'success' | 'warning' | 'danger';
    pct: number;
    atCeiling: boolean;
  };
}

export interface SaasBillingSummaryAndQuotasProps {
  billing: BillingState;
  busy: boolean;
  editingTaxCode: boolean;
  setEditingTaxCode: (v: boolean) => void;
  taxCodeDraft: string;
  setTaxCodeDraft: (v: string) => void;
  saveTaxCode: () => void;
  loadSubscription: () => void;
  quotaGaugeItems: QuotaGaugeItem[];
  createVietQrInvoice: (overridePlanCode?: 'STARTER' | 'GROWTH' | 'ENTERPRISE') => void;
}

export const SaasBillingSummaryAndQuotas: React.FC<SaasBillingSummaryAndQuotasProps> = ({
  billing,
  busy,
  editingTaxCode,
  setEditingTaxCode,
  taxCodeDraft,
  setTaxCodeDraft,
  saveTaxCode,
  loadSubscription,
  quotaGaugeItems,
  createVietQrInvoice,
}) => {
  return (
    <>
      <UiCard
        title="Thông tin gói dịch vụ hiện tại & chu kỳ thanh toán"
        subtitle="Tổng hợp trạng thái hoạt động, thời hạn gia hạn định kỳ và thông tin xuất hóa đơn GTGT (VAT) của doanh nghiệp"
      >
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 12 }} data-testid="saas-billing-period-summary">
          <div style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Gói dịch vụ đang áp dụng</div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginTop: 4 }}>
              <strong style={{ fontSize: 15, color: '#0f172a' }}>{billing.planName}</strong>
              <UiBadge tone="success">{resolveSubscriptionStatusLabel(billing.subscriptionStatus)}</UiBadge>
            </div>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Chu kỳ cước hiện tại</div>
            <strong style={{ display: 'block', fontSize: 14, color: '#0f172a', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {formatDateVi(billing.currentPeriodStartUtc)} → {formatDateVi(billing.currentPeriodEndUtc)}
            </strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Ngày gia hạn tiếp theo</div>
            <strong style={{ display: 'block', fontSize: 14, color: '#0369a1', marginTop: 4, fontVariantNumeric: 'tabular-nums' }}>
              {formatDateVi(billing.nextRenewalDateUtc)}
            </strong>
          </div>
          <div style={{ padding: '10px 12px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 12, color: '#475569' }}>Hóa đơn điện tử GTGT (VAT)</div>
            {editingTaxCode ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 4 }}>
                <input
                  aria-label="Mã số thuế doanh nghiệp"
                  data-testid="input-tax-code"
                  value={taxCodeDraft}
                  onChange={(e) => setTaxCodeDraft(e.target.value)}
                  style={{ height: 30, padding: '2px 8px', fontSize: 13, width: 120, borderRadius: 6, border: '1px solid #94a3b8' }}
                />
                <button
                  type="button"
                  data-testid="btn-save-tax-code"
                  onClick={saveTaxCode}
                  style={{ padding: '4px 8px', fontSize: 12, borderRadius: 6, background: '#0284c7', color: '#ffffff', border: '1px solid #0369a1', fontWeight: 600, cursor: 'pointer' }}
                >
                  Lưu
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setTaxCodeDraft(billing.taxCode);
                    setEditingTaxCode(false);
                  }}
                  style={{ ...secondaryBtnStyle, padding: '4px 8px', fontSize: 12 }}
                >
                  Hủy
                </button>
              </div>
            ) : (
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginTop: 4 }}>
                <strong style={{ fontSize: 13.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>MST: {billing.taxCode}</strong>
                <button
                  type="button"
                  data-testid="btn-edit-tax-code"
                  onClick={() => {
                    setTaxCodeDraft(billing.taxCode);
                    setEditingTaxCode(true);
                  }}
                  style={{ ...secondaryBtnStyle, padding: '2px 8px', fontSize: 12 }}
                >
                  Chỉnh sửa
                </button>
              </div>
            )}
          </div>
        </div>
      </UiCard>

      <UiCard
        title="Mức sử dụng trong chu kỳ"
        subtitle="Theo dõi sản lượng thực tế của kỳ cước hiện tại trên 4 chỉ số vận hành cốt lõi."
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
            <div className="ui-inline-wrap" style={{ gap: 8 }} data-testid="saas-quota-tier-legend">
              <UiBadge tone="success">Bình thường (&lt;80%)</UiBadge>
              <UiBadge tone="warning">Mức 1: Cảnh báo sớm (80–99%)</UiBadge>
              <UiBadge tone="warning">Đã đạt hạn mức (100%)</UiBadge>
              <UiBadge tone="warning">Mức 2: Đang vượt (101–115%)</UiBadge>
              <UiBadge tone="danger">Mức 3: Chế độ chỉ đọc (&gt;115%)</UiBadge>
            </div>
            <button
              type="button"
              data-testid="btn-refresh-saas-quota"
              disabled={busy}
              onClick={() => void loadSubscription()}
              style={{ ...secondaryBtnStyle, padding: '6px 12px', fontSize: 13 }}
            >
              Làm mới hạn mức
            </button>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12, alignItems: 'stretch' }} data-testid="saas-quota-gauge-grid">
            {quotaGaugeItems.map((item) => (
              <div
                key={item.key}
                data-testid={`quota-card-${item.key}`}
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'minmax(0, 1fr)',
                  gridTemplateRows: '50px 32px 10px 46px',
                  gap: 6,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: '#f8fafc',
                  border: '1px solid #cbd5e1',
                  minWidth: 0,
                  boxSizing: 'border-box',
                }}
              >
                <div
                  data-testid={item.tier.atCeiling ? `quota-ceiling-alert-${item.key}` : undefined}
                  style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', alignItems: 'flex-start', gap: 4, height: 50, minWidth: 0 }}
                >
                  <strong style={{ fontSize: 13.5, color: '#0f172a', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '100%' }}>
                    {item.title}
                  </strong>
                  <UiBadge tone={item.tier.tone}>{item.tier.label}</UiBadge>
                </div>

                <div
                  data-testid={`quota-value-row-${item.key}`}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'flex-end',
                    height: 32,
                    fontSize: 16,
                    fontWeight: 700,
                    color: '#0f172a',
                    textAlign: 'right',
                    fontVariantNumeric: 'tabular-nums',
                  }}
                >
                  {item.used.toLocaleString('vi-VN')} / {item.max.toLocaleString('vi-VN')} {item.unit}
                </div>

                <div
                  role="progressbar"
                  aria-label={item.title}
                  aria-valuenow={item.tier.pct}
                  aria-valuemin={0}
                  aria-valuemax={115}
                  style={{ position: 'relative', height: 10, alignSelf: 'center', borderRadius: 999, background: '#cbd5e1', overflow: 'hidden' }}
                >
                  <div title="Mốc cảnh báo sớm 80%" style={{ position: 'absolute', left: `${Math.round((80 / 115) * 100)}%`, top: 0, bottom: 0, width: 2, background: '#1e293b', zIndex: 2 }} />
                  <div title="Mốc trần hạn mức 100%" style={{ position: 'absolute', left: `${Math.round((100 / 115) * 100)}%`, top: 0, bottom: 0, width: 2, background: '#991b1b', zIndex: 2 }} />
                  <div
                    style={{
                      width: `${Math.min(100, Math.round((item.tier.pct / 115) * 100))}%`,
                      height: '100%',
                      background: item.tier.pct > 100 ? '#dc2626' : item.tier.pct === 100 ? '#ea580c' : item.tier.pct >= 80 ? '#d97706' : '#0284c7',
                    }}
                  />
                </div>

                <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', alignItems: 'flex-start', height: 46, fontSize: 12, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
                  <span>Đã dùng {item.tier.pct}% hạn mức</span>
                  {item.tier.atCeiling ? (
                    <button
                      type="button"
                      data-testid={`btn-upgrade-now-${item.key}`}
                      style={{
                        padding: '2px 8px',
                        minHeight: 22,
                        fontSize: 12,
                        fontWeight: 700,
                        color: '#0369a1',
                        background: '#e0f2fe',
                        border: '1px solid #38bdf8',
                        borderRadius: 6,
                        cursor: 'pointer',
                        whiteSpace: 'nowrap',
                      }}
                      onClick={() => {
                        const nextPlan = billing.planCode === 'STARTER' || billing.planCode === 'FREE_TRIAL' ? 'GROWTH' : 'ENTERPRISE';
                        void createVietQrInvoice(nextPlan);
                      }}
                    >
                      Nâng cấp ngay →
                    </button>
                  ) : (
                    <span style={{ fontSize: 11.5, color: '#64748b' }}>Trong ngưỡng an toàn</span>
                  )}
                </div>
              </div>
            ))}
          </div>

          <div
            data-testid="quota-grace-policy-note"
            style={{
              padding: '8px 12px',
              borderRadius: 6,
              background: '#f8fafc',
              border: '1px solid #e2e8f0',
              fontSize: 12,
              color: '#475569',
            }}
          >
            ⓘ Chính sách bảo vệ vận hành giờ cao điểm: Cho phép vượt tối đa 15% hạn mức đơn hàng (101–115%) trong thời gian ân hạn 72 giờ trước khi tạm khóa tạo mới.
          </div>
        </div>
      </UiCard>
    </>
  );
};
