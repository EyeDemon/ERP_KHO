import React from 'react';
import { formatMinLengthCounterVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { normalizeTenantLegalNameVi } from './platformBillingHelpers';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';
import { useIsPlatformMobile768 } from './usePlatformMobileViewport';

interface PlatformQuotasUtilizationCardProps {
  tenants: PlatformTenantItem[];
  activeQuotaTarget: PlatformTenantItem | null;
  hasDrawerOpen: boolean;
  hasLoadError?: boolean;
  onRetryLoad?: () => void;
  canWriteQuota: boolean;
  submittingQuota: boolean;
  isBelowUsage: boolean;
  usedWh: number;
  usedUsr: number;
  usedOrd: number;
  formattedQuotaOrders: string;
  handleQuotaOrdersChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  quotaWarehouses: number;
  setQuotaWarehouses: (v: number) => void;
  quotaUsers: number;
  setQuotaUsers: (v: number) => void;
  quotaReason: string;
  setQuotaReason: (v: string) => void;
  openQuotaDrawer: (tenant: PlatformTenantItem) => void;
  onQuotaSubmit: (e: React.FormEvent) => void;
}

export const PlatformQuotasUtilizationCard: React.FC<PlatformQuotasUtilizationCardProps> = ({
  tenants,
  activeQuotaTarget,
  hasDrawerOpen,
  hasLoadError = false,
  onRetryLoad,
  canWriteQuota,
  submittingQuota,
  isBelowUsage,
  usedWh,
  usedUsr,
  usedOrd,
  formattedQuotaOrders,
  handleQuotaOrdersChange,
  quotaWarehouses,
  setQuotaWarehouses,
  quotaUsers,
  setQuotaUsers,
  quotaReason,
  setQuotaReason,
  openQuotaDrawer,
  onQuotaSubmit,
}) => {
  const isMobile768 = useIsPlatformMobile768();
  const overrideDisabledReason = !canWriteQuota
    ? 'Vai trò hiện tại không có quyền điều chỉnh hạn mức thuê bao'
    : submittingQuota
      ? 'Đang lưu cấu hình hạn mức lên máy chủ'
      : isBelowUsage
        ? 'Không thể hạ hạn mức thấp hơn mức đang sử dụng thực tế'
        : quotaReason.trim().length < 10
          ? 'Nhập lý do điều chỉnh tối thiểu 10 ký tự trước khi lưu hạn mức'
          : undefined;

  return (
    <>
      <UiCard
        title="Giám sát mức sử dụng tài nguyên 3 chiều (Kho, Nhân sự, Sản lượng đơn/tháng)"
        subtitle="Theo dõi tỷ lệ tiêu thụ tài nguyên thực tế theo từng gói cước và điều chỉnh hạn mức theo phụ lục hợp đồng B2B"
      >
        {hasLoadError && tenants.length === 0 ? (
          <div
            data-testid="platform-quotas-load-error"
            style={{
              padding: '12px 14px',
              borderRadius: 8,
              background: '#f8fafc',
              border: '1px solid #cbd5e1',
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 10,
              color: '#475569',
              fontSize: 13,
            }}
          >
            <span>Không thể tải dữ liệu hạn mức thuê bao từ máy chủ — các chỉ số tạm thời hiển thị <strong>—</strong>.</span>
            {onRetryLoad ? (
              <button
                type="button"
                className="platform-btn platform-btn-secondary"
                data-testid="btn-retry-platform-quotas"
                onClick={onRetryLoad}
                style={{ minHeight: 44 }}
              >
                Thử lại
              </button>
            ) : null}
          </div>
        ) : isMobile768 ? (
          <div className="platform-mobile-cards" data-testid="platform-quotas-mobile-cards">
            {tenants.map((t) => {
              const isSuspended = t.lifecycleStatus === 'SUSPENDED' || t.status === 'SUSPENDED';
              const rowUsedWh = t.usedWarehouses ?? 1;
              const rowUsedUsr = t.usedUsers ?? 6;
              const whPct = Math.min(100, Math.round((rowUsedWh / Math.max(1, t.maxWarehouses)) * 100));
              const usrPct = Math.min(100, Math.round((rowUsedUsr / Math.max(1, t.maxUsers)) * 100));
              const rawOrdPct = (t.currentMonthOrderCount / Math.max(1, t.monthlyOrderQuota)) * 100;
              const orderPctDisplay = rawOrdPct.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
              const planCodeUpper = t.planCode.toUpperCase();
              const planLabelVi =
                planCodeUpper === 'ENTERPRISE' || planCodeUpper === 'FULL_WMS'
                  ? 'Gói Quản trị Kho Toàn diện'
                  : 'Gói Tăng trưởng';

              return (
                <article key={t.tenantId} className="platform-mobile-card" data-testid={`platform-quota-row-${t.tenantId}`}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8, flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                      <div className="ui-code" style={{ fontWeight: 700, fontSize: 13.5, color: '#0f172a' }}>{t.tenantCode}</div>
                      <div style={{ fontSize: 12.5, color: '#475569', overflowWrap: 'anywhere' }}>{normalizeTenantLegalNameVi(t.companyName, t.tenantId, t.tenantCode)}</div>
                    </div>
                    <UiBadge tone="info">{planLabelVi}</UiBadge>
                  </div>
                  <div style={{ display: 'grid', gap: 6, fontSize: 12.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
                    <div>Kho vận hành: <strong>{rowUsedWh} / {t.maxWarehouses} kho ({whPct}%)</strong></div>
                    <div>Tài khoản nhân sự: <strong>{rowUsedUsr} / {t.maxUsers} người ({usrPct}%)</strong></div>
                    <div>Sản lượng đơn / tháng: <strong>{t.currentMonthOrderCount.toLocaleString('vi-VN')} / {t.monthlyOrderQuota.toLocaleString('vi-VN')} đơn ({orderPctDisplay}%)</strong></div>
                    <div>Phụ lục hợp đồng: <strong className="ui-code">PLHD-2026-001</strong></div>
                  </div>
                  <div style={{ paddingTop: 4 }}>
                    <button
                      type="button"
                      className="platform-btn platform-btn-secondary"
                      data-testid={`btn-open-quota-modal-${t.tenantId}`}
                      disabled={!canWriteQuota || isSuspended}
                      title={isSuspended ? 'Không thể đổi hạn mức khi đang tạm khóa' : !canWriteQuota ? 'Vai trò hiện tại không có quyền điều chỉnh hạn mức' : undefined}
                      onClick={() => openQuotaDrawer(t)}
                    >
                      Điều chỉnh hạn mức
                    </button>
                  </div>
                </article>
              );
            })}
          </div>
        ) : (
        <UiTableScroll ariaLabel="Bảng theo dõi hạn mức doanh nghiệp thuê bao">
          <table className="ui-table" data-testid="platform-quotas-utilization-table">
            <thead>
              <tr>
                <th style={{ minWidth: 160 }}>Mã DN &amp; Doanh nghiệp</th>
                <th style={{ minWidth: 112 }}>Gói cước hiện tại</th>
                <th style={{ minWidth: 132 }}>Hạn mức Kho vận hành</th>
                <th style={{ minWidth: 140 }}>Hạn mức Tài khoản nhân sự</th>
                <th style={{ minWidth: 160 }}>Hạn mức Sản lượng đơn / tháng</th>
                <th style={{ minWidth: 112 }}>Phụ lục hợp đồng</th>
                <th style={{ minWidth: 120 }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {tenants.map((t) => {
                const isSuspended = t.lifecycleStatus === 'SUSPENDED' || t.status === 'SUSPENDED';
                const rowUsedWh = t.usedWarehouses ?? 1;
                const rowUsedUsr = t.usedUsers ?? 6;
                const whPct = Math.min(100, Math.round((rowUsedWh / Math.max(1, t.maxWarehouses)) * 100));
                const usrPct = Math.min(100, Math.round((rowUsedUsr / Math.max(1, t.maxUsers)) * 100));
                const rawOrdPct = (t.currentMonthOrderCount / Math.max(1, t.monthlyOrderQuota)) * 100;
                const orderPctDisplay = rawOrdPct.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
                const planCodeUpper = t.planCode.toUpperCase();
                const planLabelVi =
                  planCodeUpper === 'ENTERPRISE' || planCodeUpper === 'FULL_WMS'
                    ? 'Gói Quản trị Kho Toàn diện'
                    : 'Gói Tăng trưởng';
                return (
                  <tr key={t.tenantId} data-testid={`platform-quota-row-${t.tenantId}`}>
                    <td>
                      <div className="ui-code" style={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{t.tenantCode}</div>
                      <div style={{ fontSize: 12, color: '#475569' }}>{normalizeTenantLegalNameVi(t.companyName, t.tenantId, t.tenantCode)}</div>
                    </td>
                    <td className="cell-nowrap"><UiBadge tone="info">{planLabelVi}</UiBadge></td>
                    <td style={{ fontVariantNumeric: 'tabular-nums', minWidth: 145 }}>
                      <div style={{ fontWeight: 600 }}>{rowUsedWh} / {t.maxWarehouses} kho ({whPct}%)</div>
                      <div style={{ height: 6, borderRadius: 999, backgroundColor: '#e2e8f0', marginTop: 4, overflow: 'hidden' }}>
                        <div style={{ width: `${whPct}%`, height: '100%', backgroundColor: '#1d4ed8' }} />
                      </div>
                    </td>
                    <td style={{ fontVariantNumeric: 'tabular-nums', minWidth: 145 }}>
                      <div style={{ fontWeight: 600 }}>{rowUsedUsr} / {t.maxUsers} người ({usrPct}%)</div>
                      <div style={{ height: 6, borderRadius: 999, backgroundColor: '#e2e8f0', marginTop: 4, overflow: 'hidden' }}>
                        <div style={{ width: `${usrPct}%`, height: '100%', backgroundColor: '#1d4ed8' }} />
                      </div>
                    </td>
                    <td style={{ fontVariantNumeric: 'tabular-nums', minWidth: 185 }}>
                      <div style={{ fontWeight: 600 }}>
                        {t.currentMonthOrderCount.toLocaleString('vi-VN')} / {t.monthlyOrderQuota.toLocaleString('vi-VN')} đơn ({orderPctDisplay}%)
                      </div>
                      <div style={{ height: 6, borderRadius: 999, backgroundColor: '#e2e8f0', marginTop: 4, overflow: 'hidden' }}>
                        <div style={{ width: `${Math.min(100, Math.round(rawOrdPct))}%`, height: '100%', backgroundColor: rawOrdPct >= 80 ? '#f59e0b' : '#1d4ed8' }} />
                      </div>
                    </td>
                    <td className="cell-nowrap" style={{ fontFamily: 'monospace', fontSize: 12, fontWeight: 600 }}>PLHD-2026-001</td>
                    <td className="cell-nowrap">
                      <button type="button" className="platform-btn platform-btn-secondary" data-testid={`btn-open-quota-modal-${t.tenantId}`} disabled={!canWriteQuota || isSuspended} title={isSuspended ? 'Không thể đổi hạn mức khi đang tạm khóa' : undefined} onClick={() => openQuotaDrawer(t)}>
                        Điều chỉnh hạn mức
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
        )}
      </UiCard>

      {activeQuotaTarget && !hasDrawerOpen ? (
        <UiCard
          title={`Điều chỉnh hạn ngạch tài nguyên — ${activeQuotaTarget.tenantCode} (${normalizeTenantLegalNameVi(activeQuotaTarget.companyName, activeQuotaTarget.tenantId, activeQuotaTarget.tenantCode)})`}
          subtitle="So sánh trực quan hạn mức Trước / Sau và chặn hạ hạn mức thấp hơn mức đang sử dụng thực tế"
        >
          <form onSubmit={onQuotaSubmit} style={{ display: 'grid', gridTemplateColumns: isMobile768 ? 'minmax(0, 1fr)' : 'repeat(4, minmax(0, 1fr))', gap: 12 }}>
            <div className="platform-form-field">
              <label htmlFor="inline-quota-orders">Sản lượng đơn / tháng (Đang dùng: {usedOrd.toLocaleString('vi-VN')} đơn)</label>
              <input id="inline-quota-orders" type="text" inputMode="numeric" data-testid="input-quota-monthly-orders" value={formattedQuotaOrders} onChange={handleQuotaOrdersChange} />
            </div>
            <div className="platform-form-field">
              <label htmlFor="inline-quota-wh">Số kho tối đa (Đang dùng: {usedWh} kho)</label>
              <input id="inline-quota-wh" type="number" data-testid="input-quota-max-warehouses" value={quotaWarehouses} onChange={(e) => setQuotaWarehouses(Number(e.target.value) || 0)} />
            </div>
            <div className="platform-form-field">
              <label htmlFor="inline-quota-users">Tài khoản nhân sự (Đang dùng: {usedUsr} người)</label>
              <input id="inline-quota-users" type="number" data-testid="input-quota-max-users" value={quotaUsers} onChange={(e) => setQuotaUsers(Number(e.target.value) || 0)} />
            </div>
            <div className="platform-form-field">
              <label htmlFor="inline-quota-reason" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 6, flexWrap: 'wrap' }}>
                <span>Lý do điều chỉnh</span>
                <span style={{ fontSize: 11, fontWeight: 600, color: quotaReason.trim().length >= 10 ? '#047857' : '#64748b' }}>
                  {formatMinLengthCounterVi(quotaReason.trim().length, 10)}
                </span>
              </label>
              <input id="inline-quota-reason" type="text" data-testid="input-quota-reason" placeholder="Lý do điều chỉnh..." style={{ minWidth: isMobile768 ? 0 : 210, width: '100%' }} value={quotaReason} onChange={(e) => setQuotaReason(e.target.value)} />
            </div>
            <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'flex-end' }}>
              <button type="submit" className="platform-btn platform-btn-primary" data-testid="btn-override-quota" title={overrideDisabledReason} disabled={!canWriteQuota || submittingQuota || isBelowUsage || quotaReason.trim().length < 10}>
                Lưu hạn mức &amp; Ghi nhật ký kiểm toán
              </button>
            </div>
          </form>
        </UiCard>
      ) : null}
    </>
  );
};
