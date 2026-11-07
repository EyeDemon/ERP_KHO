import React, { useState } from 'react';
import { FileText, History, MoreHorizontal, PauseCircle, PlayCircle } from 'lucide-react';
import { formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { normalizeTenantLegalNameVi, normalizeTenantTaxCode } from './platformBillingHelpers';
import { PlatformQuotaDrawerAndModals } from './PlatformQuotaDrawerAndModals';
import { PlatformQuotasUtilizationCard } from './PlatformQuotasUtilizationCard';
import { PlatformTenantsMobileCards } from './PlatformTenantsMobileCards';
import { useIsPlatformMobile768 } from './usePlatformMobileViewport';

export interface PlatformTenantItem {
  tenantId: number; tenantCode: string; companyName: string; taxCode: string; taxpayerType: string;
  ownerEmail: string; status: string; lifecycleStatus: string; suspensionReason?: string | null;
  suspendedBy?: string | null; suspendedAtVi?: string | null;
  allowSupportAccess: boolean; planCode: string; planName: string; monthlyOrderQuota: number;
  currentMonthOrderCount: number; usedWarehouses?: number; maxWarehouses: number; usedUsers?: number; maxUsers: number;
}

interface Props {
  tenants: PlatformTenantItem[]; showTenantsTable: boolean; showQuotaView: boolean;
  canWriteTenantStatus: boolean; canWriteQuota: boolean; canImpersonate: boolean;
  hasLoadError?: boolean; onRetryLoad?: () => void;
  onConfirmTenantStatus: (tenantId: number, nextStatus: string, reason: string, confirmationCode: string) => Promise<void>;
  onSaveTenantQuota: (
    tenantId: number,
    monthlyOrderQuota: number,
    maxWarehouses: number,
    maxUsers: number,
    reason: string,
    contractReference?: string,
    effectiveUntil?: string,
  ) => Promise<void>;
  onLaunchSupportForTenant: (tenantId: number) => void;
  onNavigatePath?: (path: string) => void;
}

export const PlatformTenantsAndQuotaSection: React.FC<Props> = ({
  tenants, showTenantsTable, showQuotaView, canWriteTenantStatus, canWriteQuota, canImpersonate,
  hasLoadError = false, onRetryLoad,
  onConfirmTenantStatus, onSaveTenantQuota, onLaunchSupportForTenant, onNavigatePath,
}) => {
  const isMobile768 = useIsPlatformMobile768();
  const [openMenuTenantId, setOpenMenuTenantId] = useState<number | null>(null);
  const [expandedReasonTenantId, setExpandedReasonTenantId] = useState<number | null>(null);
  const [modalTenant, setModalTenant] = useState<PlatformTenantItem | null>(null);
  const [modalTargetStatus, setModalTargetStatus] = useState<'SUSPENDED' | 'ACTIVE'>('SUSPENDED');
  const [confirmTenantCode, setConfirmTenantCode] = useState<string>('');
  const [suspendCategory, setSuspendCategory] = useState<string>('');
  const [suspendReason, setSuspendReason] = useState<string>('');
  const [submittingStatus, setSubmittingStatus] = useState<boolean>(false);

  const [drawerTenant, setDrawerTenant] = useState<PlatformTenantItem | null>(null);
  const [quotaOrders, setQuotaOrders] = useState<number>(10000);
  const [quotaWarehouses, setQuotaWarehouses] = useState<number>(3);
  const [quotaUsers, setQuotaUsers] = useState<number>(20);
  const [quotaContractRef, setQuotaContractRef] = useState<string>('');
  const [quotaEffectiveUntil, setQuotaEffectiveUntil] = useState<string>('');
  const [quotaReason, setQuotaReason] = useState<string>('');
  const [submittingQuota, setSubmittingQuota] = useState<boolean>(false);

  const formattedQuotaOrders = quotaOrders > 0 ? quotaOrders.toLocaleString('vi-VN') : '0';
  const handleQuotaOrdersChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const digits = e.target.value.replace(/\D/g, '');
    setQuotaOrders(digits ? Number(digits) : 0);
  };

  const openSuspendModal = (tenant: PlatformTenantItem, nextStatus: 'SUSPENDED' | 'ACTIVE') => {
    setOpenMenuTenantId(null); setModalTenant(tenant); setModalTargetStatus(nextStatus);
    setConfirmTenantCode(''); setSuspendCategory(''); setSuspendReason('');
  };

  const openQuotaDrawer = (tenant: PlatformTenantItem) => {
    setOpenMenuTenantId(null); setDrawerTenant(tenant);
    setQuotaOrders(tenant.monthlyOrderQuota); setQuotaWarehouses(tenant.maxWarehouses); setQuotaUsers(tenant.maxUsers);
    setQuotaContractRef(''); setQuotaEffectiveUntil(''); setQuotaReason('');
  };

  const isSuspendConfirmValid =
    modalTenant !== null &&
    confirmTenantCode.trim().toUpperCase() === modalTenant.tenantCode.trim().toUpperCase() &&
    suspendReason.trim().length >= 10;

  const handleConfirmStatusSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!modalTenant || !isSuspendConfirmValid) return;
    setSubmittingStatus(true);
    try {
      await onConfirmTenantStatus(modalTenant.tenantId, modalTargetStatus, suspendReason.trim(), confirmTenantCode.trim().toUpperCase());
      setModalTenant(null);
    } finally {
      setSubmittingStatus(false);
    }
  };

  const activeQuotaTarget = drawerTenant ?? tenants[0] ?? null;
  const usedWh = activeQuotaTarget?.usedWarehouses ?? 1;
  const usedUsr = activeQuotaTarget?.usedUsers ?? 6;
  const usedOrd = activeQuotaTarget?.currentMonthOrderCount ?? 0;
  const isBelowUsage = activeQuotaTarget !== null && (quotaWarehouses < usedWh || quotaUsers < usedUsr || quotaOrders < usedOrd);
  const hasQuotaChanged =
    activeQuotaTarget !== null &&
    (quotaWarehouses !== activeQuotaTarget.maxWarehouses || quotaUsers !== activeQuotaTarget.maxUsers || quotaOrders !== activeQuotaTarget.monthlyOrderQuota);
  const exceedsGrowthTier = quotaWarehouses > 3 || quotaUsers > 20 || quotaOrders > 10000;

  const handleQuotaSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeQuotaTarget || !hasQuotaChanged || isBelowUsage || quotaReason.trim().length < 10) return;
    if (drawerTenant && ((exceedsGrowthTier && quotaContractRef.trim().length === 0) || !/^\d{4}-\d{2}-\d{2}$/.test(quotaEffectiveUntil.trim()))) return;
    setSubmittingQuota(true);
    try {
      await onSaveTenantQuota(
        activeQuotaTarget.tenantId,
        quotaOrders,
        quotaWarehouses,
        quotaUsers,
        quotaReason.trim(),
        quotaContractRef.trim() || undefined,
        quotaEffectiveUntil.trim() || undefined,
      );
      setDrawerTenant(null);
    } finally {
      setSubmittingQuota(false);
    }
  };

  return (
    <>
      {showTenantsTable ? (
        <UiCard
          title="Quản lý vòng đời doanh nghiệp thuê bao"
          subtitle="Quản trị trạng thái hoạt động, tạm khóa/mở khóa có kiểm soát và khởi tạo phiên hỗ trợ kỹ thuật chỉ đọc"
        >
          <div style={{ overflow: 'visible', paddingBottom: openMenuTenantId ? 140 : 0 }}>
            {isMobile768 ? (
              <PlatformTenantsMobileCards
                tenants={tenants}
                canWriteTenantStatus={canWriteTenantStatus}
                canWriteQuota={canWriteQuota}
                canImpersonate={canImpersonate}
                openMenuTenantId={openMenuTenantId}
                setOpenMenuTenantId={setOpenMenuTenantId}
                expandedReasonTenantId={expandedReasonTenantId}
                setExpandedReasonTenantId={setExpandedReasonTenantId}
                openQuotaDrawer={openQuotaDrawer}
                openSuspendModal={openSuspendModal}
                onLaunchSupportForTenant={onLaunchSupportForTenant}
                onNavigatePath={onNavigatePath}
              />
            ) : (
            <UiTableScroll ariaLabel="Bảng quản lý vòng đời doanh nghiệp thuê bao">
              <div style={{ paddingBottom: openMenuTenantId ? 140 : 0 }}>
                <table className="ui-table" data-testid="platform-tenants-table">
                  <thead>
                    <tr>
                      <th style={{ minWidth: 80, whiteSpace: 'nowrap' }}>Mã DN</th>
                      <th style={{ minWidth: 148 }}>Doanh nghiệp &amp; Chủ sở hữu</th>
                      <th style={{ minWidth: 104, whiteSpace: 'nowrap' }}>MST / CCCD</th>
                      <th style={{ minWidth: 108, whiteSpace: 'nowrap' }}>Gói cước</th>
                      <th style={{ minWidth: 118, textAlign: 'right', whiteSpace: 'nowrap' }}>Đơn tháng này</th>
                      <th style={{ minWidth: 108, textAlign: 'right', whiteSpace: 'nowrap' }}>Kho &amp; Nhân sự</th>
                      <th style={{ minWidth: 108, whiteSpace: 'nowrap' }}>Trạng thái</th>
                      <th style={{ minWidth: 132, whiteSpace: 'nowrap' }}>Thao tác</th>
                    </tr>
                  </thead>
              <tbody>
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
                    <tr key={t.tenantId} data-testid={`platform-tenant-row-${t.tenantId}`}>
                      <td className="cell-nowrap ui-code" style={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{t.tenantCode}</td>
                      <td style={{ maxWidth: 230, whiteSpace: 'normal' }}>
                        <div style={{ fontWeight: 600, color: '#0f172a' }}>{companyNameVi}</div>
                        <div style={{ fontSize: 11.5, color: '#475569' }}>{t.ownerEmail}</div>
                      </td>
                      <td className="cell-nowrap" style={{ fontVariantNumeric: 'tabular-nums' }}>
                        <div style={{ fontWeight: 600 }}>{taxCodeVi}</div>
                        <div style={{ fontSize: 11.5, color: '#475569' }}>{t.taxpayerType === 'HOUSEHOLD_INDIVIDUAL' ? 'Hộ kinh doanh' : 'Doanh nghiệp'}</div>
                      </td>
                      <td className="cell-nowrap"><UiBadge tone="info">{planLabelVi}</UiBadge></td>
                      <td className="cell-nowrap" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
                        {t.currentMonthOrderCount.toLocaleString('vi-VN')} / {t.monthlyOrderQuota.toLocaleString('vi-VN')} ({orderPct}%)
                      </td>
                      <td className="cell-nowrap" style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {rowUsedWh}/{t.maxWarehouses} kho • {rowUsedUsr}/{t.maxUsers} người
                      </td>
                      <td className="cell-nowrap">
                        <UiBadge tone={isSuspended ? 'danger' : 'success'}>{isSuspended ? 'Đã tạm khóa' : 'Đang hoạt động'}</UiBadge>
                        {isSuspended ? (
                          <>
                            <div style={{ fontSize: 11, color: '#475569', marginTop: 2 }}>
                              Bởi {formatUserDisplayNameVi(t.suspendedBy, 'SuperAdmin')} • {t.suspendedAtVi || 'Vừa xong'}
                            </div>
                            <button type="button" data-testid={`btn-toggle-suspend-reason-${t.tenantId}`} onClick={() => setExpandedReasonTenantId((prev) => (prev === t.tenantId ? null : t.tenantId))} style={{ background: 'none', border: 0, padding: 0, marginTop: 2, fontSize: 11, fontWeight: 600, color: '#991b1b', cursor: 'pointer', textDecoration: 'underline' }}>
                              Xem lý do
                            </button>
                            {expandedReasonTenantId === t.tenantId ? (
                              <div data-testid={`suspend-reason-detail-${t.tenantId}`} style={{ fontSize: 11, color: '#991b1b', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: 6, padding: '4px 6px', marginTop: 3, whiteSpace: 'normal', maxWidth: 180 }}>
                                {t.suspensionReason || 'Tạm khóa theo quyết định quản trị nền tảng'}
                              </div>
                            ) : null}
                          </>
                        ) : null}
                      </td>
                      <td className="cell-nowrap">
                        <div className="platform-action-group">
                          <button type="button" className="platform-btn platform-btn-secondary" data-testid={`btn-open-quota-modal-${t.tenantId}`} disabled={!canWriteQuota || isSuspended} title={isSuspended ? 'Không thể đổi hạn mức khi đang tạm khóa' : undefined} onClick={() => openQuotaDrawer(t)}>
                            Đổi hạn mức
                          </button>
                          <button type="button" id={`btn-impersonate-${t.tenantCode}`} className="platform-btn platform-btn-secondary" data-testid={`btn-impersonate-row-${t.tenantId}`} style={{ whiteSpace: 'nowrap' }} disabled={!canImpersonate} title={isSuspended ? 'Truy cập chỉ đọc để chẩn đoán hỗ trợ doanh nghiệp đang tạm khóa' : 'Khởi tạo phiên hỗ trợ kỹ thuật chỉ đọc'} onClick={() => onLaunchSupportForTenant(t.tenantId)}>
                            Mở phiên hỗ trợ
                          </button>
                          {isSuspended ? (
                            <button type="button" className="platform-btn platform-btn-success" data-testid={`btn-activate-tenant-${t.tenantId}`} disabled={!canWriteTenantStatus} onClick={() => openSuspendModal(t, 'ACTIVE')}>
                              <PlayCircle size={13} strokeWidth={2} aria-hidden="true" />
                              <span>Mở khóa lại</span>
                            </button>
                          ) : null}
                          <button type="button" aria-label={`Mở menu thao tác nâng cao cho ${t.tenantCode}`} title={`Mở menu thao tác nâng cao cho ${t.tenantCode}`} className="platform-btn platform-btn-secondary" data-testid={`btn-tenant-more-menu-${t.tenantId}`} onClick={() => setOpenMenuTenantId((prev) => (prev === t.tenantId ? null : t.tenantId))}>
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
                                <button type="button" className="tenant-more-menu-item" data-testid={`btn-menu-activate-tenant-${t.tenantId}`} disabled={!canWriteTenantStatus} onClick={() => openSuspendModal(t, 'ACTIVE')}>
                                  <PlayCircle size={15} strokeWidth={2} aria-hidden="true" color="#15803d" />
                                  <span style={{ color: '#15803d' }}>Mở khóa doanh nghiệp...</span>
                                </button>
                              ) : (
                                <button type="button" className="tenant-more-menu-item tenant-more-menu-item-danger" data-testid={`btn-suspend-tenant-${t.tenantId}`} disabled={!canWriteTenantStatus} onClick={() => openSuspendModal(t, 'SUSPENDED')}>
                                  <PauseCircle size={15} strokeWidth={2} aria-hidden="true" color="#dc2626" />
                                  <span>Tạm khóa doanh nghiệp...</span>
                                </button>
                              )}
                            </div>
                          ) : null}
                        </div>
                        {isSuspended ? (
                          <span data-testid={`suspended-quota-lock-reason-${t.tenantId}`} style={{ fontSize: 11, color: '#991b1b', fontWeight: 600, display: 'block', marginTop: 3 }}>
                            Khóa đổi hạn mức khi tạm ngưng
                          </span>
                        ) : null}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
              </div>
            </UiTableScroll>
            )}
          </div>
        </UiCard>
      ) : null}

      {showQuotaView ? (
        <PlatformQuotasUtilizationCard
          tenants={tenants}
          activeQuotaTarget={activeQuotaTarget}
          hasDrawerOpen={Boolean(drawerTenant)}
          hasLoadError={hasLoadError}
          onRetryLoad={onRetryLoad}
          canWriteQuota={canWriteQuota}
          submittingQuota={submittingQuota}
          isBelowUsage={isBelowUsage}
          usedWh={usedWh}
          usedUsr={usedUsr}
          usedOrd={usedOrd}
          formattedQuotaOrders={formattedQuotaOrders}
          handleQuotaOrdersChange={handleQuotaOrdersChange}
          quotaWarehouses={quotaWarehouses}
          setQuotaWarehouses={setQuotaWarehouses}
          quotaUsers={quotaUsers}
          setQuotaUsers={setQuotaUsers}
          quotaReason={quotaReason}
          setQuotaReason={setQuotaReason}
          openQuotaDrawer={openQuotaDrawer}
          onQuotaSubmit={handleQuotaSubmit}
        />
      ) : null}

      <PlatformQuotaDrawerAndModals
        modalTenant={modalTenant} modalTargetStatus={modalTargetStatus}
        confirmTenantCode={confirmTenantCode} setConfirmTenantCode={setConfirmTenantCode}
        suspendCategory={suspendCategory} setSuspendCategory={setSuspendCategory}
        suspendReason={suspendReason} setSuspendReason={setSuspendReason}
        submittingStatus={submittingStatus} isSuspendConfirmValid={isSuspendConfirmValid}
        onCloseSuspendModal={() => setModalTenant(null)} onConfirmStatusSubmit={handleConfirmStatusSubmit}
        drawerTenant={drawerTenant}
        quotaWarehouses={quotaWarehouses} setQuotaWarehouses={setQuotaWarehouses}
        quotaUsers={quotaUsers} setQuotaUsers={setQuotaUsers}
        quotaOrders={quotaOrders} formattedQuotaOrders={formattedQuotaOrders} handleQuotaOrdersChange={handleQuotaOrdersChange}
        quotaContractRef={quotaContractRef} setQuotaContractRef={setQuotaContractRef}
        quotaEffectiveUntil={quotaEffectiveUntil} setQuotaEffectiveUntil={setQuotaEffectiveUntil}
        quotaReason={quotaReason} setQuotaReason={setQuotaReason}
        usedWh={usedWh} usedUsr={usedUsr} usedOrd={usedOrd}
        canWriteQuota={canWriteQuota} submittingQuota={submittingQuota}
        hasQuotaChanged={hasQuotaChanged} isBelowUsage={isBelowUsage} exceedsGrowthTier={exceedsGrowthTier}
        onCloseQuotaDrawer={() => setDrawerTenant(null)} onQuotaSubmit={handleQuotaSubmit}
      />
    </>
  );
};
