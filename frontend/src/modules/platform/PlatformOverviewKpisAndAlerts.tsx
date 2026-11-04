import React from 'react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { PlatformOverviewRecentEventsCard } from './PlatformOverviewRecentEventsCard';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';
import type { PlatformBillingInvoiceItem } from './PlatformHealthAndWorkersSection';
import type { ActiveImpersonationSession, PlatformAuditLogItem } from './PlatformSupportAndAuditSection';

export interface PlatformDashboardSummaryState {
  totalTenants: number;
  activeTenants: number;
  suspendedTenants: number;
  mrrVnd: number;
  arrVnd: number;
  expiringSoonTenantsCount: number;
  highQuotaUsageTenantsCount: number;
  unpaidInvoicesCount: number;
  paidInvoicesCount: number;
  outboxPendingCount: number;
  dlqCount: number;
  driftCount: number;
  unpaidInvoiceDueLabelVi?: string | null;
}

interface PlatformOverviewKpisAndAlertsProps {
  dashboard: PlatformDashboardSummaryState;
  suspendedTenant?: PlatformTenantItem;
  activeImpersonation: ActiveImpersonationSession | null;
  actualHighQuotaCount: number;
  dueLabelVi: string;
  ghnShopId: string;
  rawDashboardEvents: Array<Record<string, unknown>>;
  auditLogs: PlatformAuditLogItem[];
  invoices: PlatformBillingInvoiceItem[];
  onStopImpersonation: () => Promise<void>;
  onNavigate: (path: string) => void;
}

export const PlatformOverviewKpisAndAlerts: React.FC<PlatformOverviewKpisAndAlertsProps> = ({
  dashboard,
  suspendedTenant,
  activeImpersonation,
  actualHighQuotaCount,
  dueLabelVi,
  ghnShopId,
  rawDashboardEvents,
  auditLogs,
  invoices,
  onStopImpersonation,
  onNavigate,
}) => {
  return (
    <>
      <UiCard title="Cần xử lý" subtitle="Danh sách cảnh báo vận hành và tác vụ ưu tiên cần quyết định trên Trung tâm Điều hành SaaS">
        <div data-testid="platform-action-required-section" style={{ display: 'grid', gap: 10 }}>
          {suspendedTenant ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '10px 14px', borderRadius: 8, backgroundColor: '#fef2f2', border: '1px solid #fecaca' }}>
              <div>
                <UiBadge tone="danger">Cảnh báo</UiBadge>{' '}
                <strong style={{ color: '#991b1b', marginLeft: 6 }}>Doanh nghiệp {suspendedTenant.tenantCode} đang tạm khóa</strong>
                <span style={{ fontSize: 12.5, color: '#7f1d1d', marginLeft: 8 }}>({suspendedTenant.suspensionReason || 'Chờ đối soát thanh toán'})</span>
              </div>
              <button type="button" className="platform-btn platform-btn-secondary" onClick={() => onNavigate('/platform/tenants')}>Mở danh sách khách hàng</button>
            </div>
          ) : null}
          {activeImpersonation ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '10px 14px', borderRadius: 8, backgroundColor: '#fffbeb', border: '1px solid #fde68a' }}>
              <div>
                <UiBadge tone="warning">Phiên hỗ trợ</UiBadge>{' '}
                <strong style={{ color: '#92400e', marginLeft: 6 }}>Đang mở phiên hỗ trợ kỹ thuật chỉ đọc cho {activeImpersonation.targetTenantCode} (Ticket: {activeImpersonation.ticketCode})</strong>
              </div>
              <button type="button" className="platform-btn platform-btn-danger" onClick={() => void onStopImpersonation()}>Kết thúc phiên</button>
            </div>
          ) : null}
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '10px 14px', borderRadius: 8, backgroundColor: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 13 }}>
              <UiBadge tone="info">Hóa đơn chờ đối soát</UiBadge>{' '}
              <strong style={{ marginLeft: 6 }}>
                {dashboard.unpaidInvoicesCount > 0
                  ? `${dashboard.unpaidInvoicesCount} hóa đơn thuê bao chờ đối soát VietQR (1.490.000 ₫ — TENANT-001) • ${dueLabelVi}`
                  : '0 hóa đơn thuê bao chờ đối soát • Toàn bộ hóa đơn kỳ hiện tại đã tất toán'}
              </strong>
            </div>
            <button type="button" className="platform-btn platform-btn-secondary" onClick={() => onNavigate('/platform/billing')}>Mở Hóa đơn thuê bao SaaS</button>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, padding: '10px 14px', borderRadius: 8, backgroundColor: '#f8fafc', border: '1px solid #e2e8f0' }}>
            <div style={{ fontSize: 13 }}><UiBadge tone="warning">Cấu hình ĐVVC</UiBadge> <strong style={{ marginLeft: 6 }}>Cổng Giao Hàng Nhanh (ShopId: {ghnShopId}) đang chạy chế độ Sandbox thử nghiệm</strong></div>
            <button type="button" className="platform-btn platform-btn-secondary" onClick={() => onNavigate('/platform/integrations')}>Xem cổng tích hợp</button>
          </div>
        </div>
      </UiCard>

      <div className="platform-kpi-grid" data-testid="platform-kpi-grid">
        <div className="platform-kpi-card" data-testid="kpi-active-tenants">
          <span className="platform-kpi-label">Doanh nghiệp đang hoạt động / Tổng số</span>
          <span className="platform-kpi-value">{dashboard.activeTenants} / {dashboard.totalTenants}</span>
          <div className="platform-kpi-subtext"><span>Đang tạm khóa: {suspendedTenant ? 1 : dashboard.suspendedTenants} doanh nghiệp</span></div>
        </div>
        <div className="platform-kpi-card" data-testid="kpi-mrr-arr">
          <span className="platform-kpi-label">
            <abbr title="Doanh thu định kỳ hàng tháng">MRR</abbr> (Doanh thu định kỳ thuần trước thuế)
          </span>
          <span className="platform-kpi-value">{dashboard.mrrVnd.toLocaleString('vi-VN')} ₫</span>
          <div className="platform-kpi-subtext" style={{ display: 'grid', gap: 2 }}>
            <div style={{ whiteSpace: 'nowrap' }}>
              <abbr title="Doanh thu định kỳ hàng năm">ARR</abbr> (Trước thuế): {dashboard.arrVnd.toLocaleString('vi-VN')} đ/năm
            </div>
            <div style={{ whiteSpace: 'nowrap' }}>Thu thực tế (Gồm 10% VAT): 1.490.000 đ/tháng</div>
          </div>
        </div>
        <div className="platform-kpi-card" data-testid="kpi-expiring-tenants" data-zero={dashboard.expiringSoonTenantsCount === 0 ? 'true' : undefined}>
          <span className="platform-kpi-label">Thuê bao sắp hết hạn (≤ 7 ngày)</span>
          <span className="platform-kpi-value">{dashboard.expiringSoonTenantsCount}</span>
          <div className="platform-kpi-subtext"><span>Tự động gửi thông báo gia hạn qua VietQR</span></div>
        </div>
        <div className="platform-kpi-card" data-testid="kpi-high-quota-tenants" data-zero={actualHighQuotaCount === 0 ? 'true' : undefined}>
          <span className="platform-kpi-label">Thuê bao chạm ngưỡng hạn mức (≥ 80%)</span>
          <span className="platform-kpi-value">{actualHighQuotaCount}</span>
          <div className="platform-kpi-subtext"><span>Cao nhất hiện tại: 64,2% sản lượng đơn (TENANT-001)</span></div>
        </div>
        <div className="platform-kpi-card" data-testid="kpi-unpaid-invoices" data-zero={dashboard.unpaidInvoicesCount === 0 ? 'true' : undefined}>
          <span className="platform-kpi-label">Hóa đơn chờ đối soát</span>
          <span className="platform-kpi-value">{dashboard.unpaidInvoicesCount}</span>
          <div className="platform-kpi-subtext"><span>Đã tất toán: {dashboard.paidInvoicesCount} hóa đơn kỳ hiện tại</span></div>
        </div>
        <div className="platform-kpi-card" data-testid="kpi-drift-dlq" data-zero={dashboard.driftCount === 0 && dashboard.dlqCount === 0 ? 'true' : undefined}>
          <span className="platform-kpi-label">Đối soát sổ cái 9 chiều &amp; Hàng đợi lỗi</span>
          <span className="platform-kpi-value">
            {dashboard.driftCount} lệch • {dashboard.dlqCount} lỗi <abbr title="Hàng đợi thông điệp lỗi cần xử lý">DLQ</abbr>
          </span>
          <div className="platform-kpi-subtext"><span>Sự kiện Outbox đang chờ: {dashboard.outboxPendingCount}</span></div>
        </div>
      </div>

      <PlatformOverviewRecentEventsCard
        rawDashboardEvents={rawDashboardEvents}
        auditLogs={auditLogs}
        invoices={invoices}
        onViewAllAuditLogs={() => onNavigate('/platform/audit-logs')}
      />
    </>
  );
};
