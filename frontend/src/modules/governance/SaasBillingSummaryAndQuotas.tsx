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

const LEGEND_ITEMS: Array<{ color: string; label: string }> = [
  { color: '#16a34a', label: 'Bình thường (<80%)' },
  { color: '#d97706', label: 'Mức 1: Cảnh báo sớm (80–99%)' },
  { color: '#ea580c', label: 'Đã đạt hạn mức (100%)' },
  { color: '#dc2626', label: 'Mức 2: Đang vượt (101–115%)' },
  { color: '#991b1b', label: 'Mức 3: Chế độ chỉ đọc (>115%)' },
];

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
    <UiCard
      title="Mức sử dụng trong chu kỳ"
      subtitle="Tổng hợp gói cước hiện tại, chu kỳ thanh toán và sản lượng thực tế trên 4 chỉ số vận hành cốt lõi"
    >
      <div className="ui-stack" style={{ gap: 6 }}>
        <div
          style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 6 }}
          data-testid="saas-billing-period-summary"
        >
          <div style={{ padding: '4px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 11, color: '#475569' }}>Gói dịch vụ đang áp dụng</div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6, marginTop: 1 }}>
              <strong style={{ fontSize: 13.5, color: '#0f172a' }}>{billing.planName}</strong>
              <UiBadge tone="success">{resolveSubscriptionStatusLabel(billing.subscriptionStatus)}</UiBadge>
            </div>
          </div>
          <div style={{ padding: '4px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 11, color: '#475569' }}>Chu kỳ cước hiện tại</div>
            <strong style={{ display: 'block', fontSize: 13, color: '#0f172a', marginTop: 1, fontVariantNumeric: 'tabular-nums' }}>
              {formatDateVi(billing.currentPeriodStartUtc)} → {formatDateVi(billing.currentPeriodEndUtc)}
            </strong>
          </div>
          <div style={{ padding: '4px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 11, color: '#475569' }}>Ngày gia hạn tiếp theo</div>
            <strong style={{ display: 'block', fontSize: 13, color: '#1d4ed8', marginTop: 1, fontVariantNumeric: 'tabular-nums' }}>
              {formatDateVi(billing.nextRenewalDateUtc)}
            </strong>
          </div>
          <div style={{ padding: '4px 10px', borderRadius: 8, background: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 11, color: '#475569' }}>Hóa đơn điện tử GTGT (VAT)</div>
            {editingTaxCode ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 1 }}>
                <input
                  aria-label="Mã số thuế doanh nghiệp"
                  data-testid="input-tax-code"
                  value={taxCodeDraft}
                  onChange={(e) => setTaxCodeDraft(e.target.value)}
                  style={{ padding: '2px 8px', minHeight: 26, fontSize: 12, width: 115, borderRadius: 6, border: '1px solid #94a3b8' }}
                />
                <button
                  type="button"
                  data-testid="btn-save-tax-code"
                  onClick={saveTaxCode}
                  style={{ padding: '2px 8px', minHeight: 26, fontSize: 11.5, borderRadius: 6, background: '#1d4ed8', color: '#ffffff', border: '1px solid #1e40af', fontWeight: 600, cursor: 'pointer' }}
                >
                  Lưu
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setTaxCodeDraft(billing.taxCode);
                    setEditingTaxCode(false);
                  }}
                  style={{ ...secondaryBtnStyle, padding: '2px 8px', minHeight: 26, fontSize: 11.5 }}
                >
                  Hủy
                </button>
              </div>
            ) : (
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6, marginTop: 1 }}>
                <strong style={{ fontSize: 12.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>MST: {billing.taxCode}</strong>
                <button
                  type="button"
                  data-testid="btn-edit-tax-code"
                  onClick={() => {
                    setTaxCodeDraft(billing.taxCode);
                    setEditingTaxCode(true);
                  }}
                  style={{ ...secondaryBtnStyle, padding: '1px 8px', minHeight: 24, fontSize: 11.5 }}
                >
                  Chỉnh sửa
                </button>
              </div>
            )}
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 6 }}>
          <div className="ui-inline-wrap" style={{ gap: 10 }} data-testid="saas-quota-tier-legend">
            {LEGEND_ITEMS.map((lg) => (
              <span key={lg.label} className="ui-legend-item">
                <span className="ui-legend-dot" style={{ backgroundColor: lg.color }} aria-hidden="true" />
                <span>{lg.label}</span>
              </span>
            ))}
          </div>
          <button
            type="button"
            data-testid="btn-refresh-saas-quota"
            disabled={busy}
            onClick={() => void loadSubscription()}
            style={{ ...secondaryBtnStyle, minHeight: 26, padding: '2px 10px', fontSize: 11.5 }}
          >
            Làm mới hạn mức
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 6, alignItems: 'stretch' }} data-testid="saas-quota-gauge-grid">
          {quotaGaugeItems.map((item) => (
            <div
              key={item.key}
              data-testid={`quota-card-${item.key}`}
              style={{
                display: 'grid',
                gridTemplateColumns: 'minmax(0, 1fr)',
                gridTemplateRows: '24px 20px 7px 22px',
                gap: 3,
                padding: '6px 10px',
                borderRadius: 8,
                background: '#f8fafc',
                border: '1px solid #cbd5e1',
                minWidth: 0,
                boxSizing: 'border-box',
              }}
            >
              <div
                data-testid={item.tier.atCeiling ? `quota-ceiling-alert-${item.key}` : undefined}
                style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, height: 24, minWidth: 0 }}
              >
                <strong style={{ fontSize: 12.5, color: '#0f172a', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', minWidth: 0 }}>
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
                  height: 20,
                  fontSize: 14,
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
                style={{ position: 'relative', height: 7, alignSelf: 'center', borderRadius: 999, background: '#cbd5e1', overflow: 'hidden' }}
              >
                <div title="Mốc cảnh báo sớm 80%" style={{ position: 'absolute', left: `${Math.round((80 / 115) * 100)}%`, top: 0, bottom: 0, width: 2, background: '#1e293b', zIndex: 2 }} />
                <div title="Mốc trần hạn mức 100%" style={{ position: 'absolute', left: `${Math.round((100 / 115) * 100)}%`, top: 0, bottom: 0, width: 2, background: '#991b1b', zIndex: 2 }} />
                <div
                  style={{
                    width: `${Math.min(100, Math.round((item.tier.pct / 115) * 100))}%`,
                    height: '100%',
                    background: item.tier.pct > 100 ? '#dc2626' : item.tier.pct === 100 ? '#ea580c' : item.tier.pct >= 80 ? '#d97706' : '#1d4ed8',
                  }}
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, height: 22, fontSize: 11.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
                <span>Đã dùng {item.tier.pct}%</span>
                {item.tier.atCeiling ? (
                  <button
                    type="button"
                    data-testid={`btn-upgrade-now-${item.key}`}
                    style={{
                      padding: '1px 7px',
                      minHeight: 22,
                      fontSize: 11,
                      fontWeight: 700,
                      color: '#1d4ed8',
                      background: '#eff6ff',
                      border: '1px solid #93c5fd',
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
                  <span style={{ fontSize: 11.5, color: '#475569' }}>An toàn</span>
                )}
              </div>
            </div>
          ))}
        </div>

        <div
          data-testid="quota-grace-policy-note"
          style={{
            padding: '4px 8px',
            borderRadius: 6,
            background: '#f8fafc',
            border: '1px solid #e2e8f0',
            fontSize: 11,
            lineHeight: 1.3,
            color: '#475569',
          }}
        >
          ⓘ Chính sách bảo vệ vận hành giờ cao điểm: Cho phép vượt tối đa 15% hạn mức đơn hàng (101–115%) trong thời gian ân hạn 72 giờ trước khi tạm khóa tạo mới.
        </div>
      </div>
    </UiCard>
  );
};
