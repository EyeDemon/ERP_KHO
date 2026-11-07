import React from 'react';
import { FileText, History, MoreHorizontal, PauseCircle, PlayCircle } from 'lucide-react';
import { formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge } from '../../ui/ProductionUi';
import { normalizeTenantLegalNameVi, normalizeTenantTaxCode } from './platformBillingHelpers';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';

interface PlatformTenantsMobileCardsProps {
  tenants: PlatformTenantItem[];
  canWriteTenantStatus: boolean;
  canWriteQuota: boolean;
  canImpersonate: boolean;
  openMenuTenantId: number | null;
  setOpenMenuTenantId: React.Dispatch<React.SetStateAction<number | null>>;
  expandedReasonTenantId: number | null;
  setExpandedReasonTenantId: React.Dispatch<React.SetStateAction<number | null>>;
  openQuotaDrawer: (tenant: PlatformTenantItem) => void;
  openSuspendModal: (tenant: PlatformTenantItem, nextStatus: 'SUSPENDED' | 'ACTIVE') => void;
  onLaunchSupportForTenant: (tenantId: number) => void;
  onNavigatePath?: (path: string) => void;
}

export const PlatformTenantsMobileCards: React.FC<PlatformTenantsMobileCardsProps> = ({
  tenants,
  canWriteTenantStatus,
  canWriteQuota,
  canImpersonate,
  openMenuTenantId,
  setOpenMenuTenantId,
  expandedReasonTenantId,
  setExpandedReasonTenantId,
  openQuotaDrawer,
  openSuspendModal,
  onLaunchSupportForTenant,
  onNavigatePath,
}) => (
  <div className="platform-mobile-cards" data-testid="platform-tenants-mobile-cards">
    {tenants.map((t) => {
      const isSuspended = t.lifecycleStatus === 'SUSPENDED' || t.status === 'SUSPENDED';
      const rowUsedWh = t.usedWarehouses ?? 1;
      const rowUsedUsr = t.usedUsers ?? 6;
      const orderPct = Math.round((t.currentMonthOrderCount / Math.max(1, t.monthlyOrderQuota)) * 100);
      const planCodeUpper = t.planCode.toUpperCase();
      const planLabelVi =
        planCodeUpper === 'ENTERPRISE' || planCodeUpper === 'FULL_WMS'
          ? 'Gói Quản trị Kho Toàn diện'
          : 'Gói Tăng trưởng';
      const companyNameVi = normalizeTenantLegalNameVi(t.companyName, t.tenantId, t.tenantCode);
      const taxCodeVi = normalizeTenantTaxCode(t.taxCode);

      return (
        <article key={t.tenantId} className="platform-mobile-card" data-testid={`platform-tenant-row-${t.tenantId}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8, flexWrap: 'wrap' }}>
            <div style={{ minWidth: 0 }}>
              <div className="ui-code" style={{ fontWeight: 700, fontSize: 13.5, color: '#0f172a' }}>{t.tenantCode}</div>
              <div style={{ fontWeight: 600, fontSize: 13, color: '#0f172a', overflowWrap: 'anywhere' }}>{companyNameVi}</div>
              <div style={{ fontSize: 12, color: '#475569', overflowWrap: 'anywhere' }}>{t.ownerEmail}</div>
            </div>
            <UiBadge tone={isSuspended ? 'danger' : 'success'}>{isSuspended ? 'Đã tạm khóa' : 'Đang hoạt động'}</UiBadge>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px 12px', fontSize: 12.5, color: '#334155', fontVariantNumeric: 'tabular-nums' }}>
            <span>MST: <strong>{taxCodeVi}</strong> ({t.taxpayerType === 'HOUSEHOLD_INDIVIDUAL' ? 'Hộ kinh doanh' : 'Doanh nghiệp'})</span>
            <UiBadge tone="info">{planLabelVi}</UiBadge>
          </div>

          <div style={{ display: 'grid', gap: 4, fontSize: 12.5, color: '#0f172a', fontVariantNumeric: 'tabular-nums' }}>
            <div>Đơn tháng này: <strong>{t.currentMonthOrderCount.toLocaleString('vi-VN')} / {t.monthlyOrderQuota.toLocaleString('vi-VN')} ({orderPct}%)</strong></div>
            <div>Kho &amp; Nhân sự: <strong>{rowUsedWh}/{t.maxWarehouses} kho • {rowUsedUsr}/{t.maxUsers} người</strong></div>
          </div>

          {isSuspended ? (
            <div style={{ fontSize: 12, color: '#991b1b' }}>
              <div>Bởi {formatUserDisplayNameVi(t.suspendedBy, 'SuperAdmin')} • {t.suspendedAtVi || 'Vừa xong'}</div>
              <button
                type="button"
                data-testid={`btn-toggle-suspend-reason-${t.tenantId}`}
                onClick={() => setExpandedReasonTenantId((prev) => (prev === t.tenantId ? null : t.tenantId))}
                style={{ background: 'none', border: 0, padding: '6px 0', minHeight: 44, fontSize: 12, fontWeight: 600, color: '#991b1b', textDecoration: 'underline' }}
              >
                Xem lý do
              </button>
              {expandedReasonTenantId === t.tenantId ? (
                <div data-testid={`suspend-reason-detail-${t.tenantId}`} style={{ backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 6, padding: '6px 8px', overflowWrap: 'anywhere' }}>
                  {t.suspensionReason || 'Tạm khóa theo quyết định quản trị nền tảng'}
                </div>
              ) : null}
            </div>
          ) : null}

          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, position: 'relative' }}>
            <button
              type="button"
              className="platform-btn platform-btn-secondary"
              data-testid={`btn-open-quota-modal-${t.tenantId}`}
              disabled={!canWriteQuota || isSuspended}
              title={isSuspended ? 'Không thể đổi hạn mức khi đang tạm khóa' : !canWriteQuota ? 'Vai trò hiện tại không có quyền điều chỉnh hạn mức' : undefined}
              onClick={() => openQuotaDrawer(t)}
            >
              Đổi hạn mức
            </button>
            <button
              type="button"
              id={`btn-impersonate-${t.tenantCode}`}
              className="platform-btn platform-btn-secondary"
              data-testid={`btn-impersonate-row-${t.tenantId}`}
              disabled={!canImpersonate}
              title={!canImpersonate ? 'Vai trò hiện tại không có quyền khởi tạo phiên hỗ trợ' : isSuspended ? 'Truy cập chỉ đọc để chẩn đoán hỗ trợ doanh nghiệp đang tạm khóa' : 'Khởi tạo phiên hỗ trợ kỹ thuật chỉ đọc'}
              onClick={() => onLaunchSupportForTenant(t.tenantId)}
            >
              Mở phiên hỗ trợ
            </button>
            {isSuspended ? (
              <button
                type="button"
                className="platform-btn platform-btn-success"
                data-testid={`btn-activate-tenant-${t.tenantId}`}
                disabled={!canWriteTenantStatus}
                title={!canWriteTenantStatus ? 'Vai trò hiện tại không có quyền mở khóa doanh nghiệp' : undefined}
                onClick={() => openSuspendModal(t, 'ACTIVE')}
              >
                <PlayCircle size={14} strokeWidth={2} aria-hidden="true" />
                <span>Mở khóa lại</span>
              </button>
            ) : null}
            <button
              type="button"
              aria-label={`Mở menu thao tác nâng cao cho ${t.tenantCode}`}
              title={`Mở menu thao tác nâng cao cho ${t.tenantCode}`}
              className="platform-btn platform-btn-secondary"
              data-testid={`btn-tenant-more-menu-${t.tenantId}`}
              onClick={() => setOpenMenuTenantId((prev) => (prev === t.tenantId ? null : t.tenantId))}
            >
              <MoreHorizontal size={16} strokeWidth={2} aria-hidden="true" />
            </button>
            {openMenuTenantId === t.tenantId ? (
              <div className="platform-more-menu-popover" data-testid={`tenant-more-menu-popover-${t.tenantId}`}>
                <button type="button" className="tenant-more-menu-item" onClick={() => { setOpenMenuTenantId(null); onNavigatePath?.('/platform/billing'); }}>
                  <FileText size={15} strokeWidth={2} aria-hidden="true" color="#475569" />
                  <span>Xem hóa đơn thuê bao</span>
                </button>
                <button type="button" className="tenant-more-menu-item" onClick={() => { setOpenMenuTenantId(null); onNavigatePath?.('/platform/audit-logs'); }}>
                  <History size={15} strokeWidth={2} aria-hidden="true" color="#475569" />
                  <span>Xem nhật ký kiểm toán</span>
                </button>
                <hr style={{ border: 0, borderTop: '1px solid #e2e8f0', margin: '4px 0', width: '100%' }} />
                {isSuspended ? (
                  <button type="button" className="tenant-more-menu-item" data-testid={`btn-menu-activate-tenant-${t.tenantId}`} disabled={!canWriteTenantStatus} title={!canWriteTenantStatus ? 'Vai trò hiện tại không có quyền mở khóa doanh nghiệp' : undefined} onClick={() => openSuspendModal(t, 'ACTIVE')}>
                    <PlayCircle size={15} strokeWidth={2} aria-hidden="true" color="#15803d" />
                    <span style={{ color: '#15803d' }}>Mở khóa doanh nghiệp...</span>
                  </button>
                ) : (
                  <button type="button" className="tenant-more-menu-item tenant-more-menu-item-danger" data-testid={`btn-suspend-tenant-${t.tenantId}`} disabled={!canWriteTenantStatus} title={!canWriteTenantStatus ? 'Vai trò hiện tại không có quyền tạm khóa doanh nghiệp' : undefined} onClick={() => openSuspendModal(t, 'SUSPENDED')}>
                    <PauseCircle size={15} strokeWidth={2} aria-hidden="true" color="#dc2626" />
                    <span>Tạm khóa doanh nghiệp...</span>
                  </button>
                )}
              </div>
            ) : null}
          </div>
          {isSuspended ? (
            <span data-testid={`suspended-quota-lock-reason-${t.tenantId}`} style={{ fontSize: 11.5, color: '#991b1b', fontWeight: 600 }}>
              Khóa đổi hạn mức khi tạm ngưng
            </span>
          ) : null}
        </article>
      );
    })}
  </div>
);
