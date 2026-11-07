import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, PlayCircle, X } from 'lucide-react';
import { formatMinLengthCounterVi } from '../../services/permissionPresentation';
import { useDialogFocusTrap } from '../../ui/useDialogFocusTrap';
import {
  UiVnDateInput,
  isoToVnDate,
  isValidVnCalendarDate,
  vnDateToIso,
} from '../../components/common/UiVnDateInput';
import { normalizeTenantLegalNameVi } from './platformBillingHelpers';
import { PlatformQuotaComparisonBox } from './PlatformQuotaComparisonBox';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';

interface PlatformQuotaDrawerAndModalsProps {
  modalTenant: PlatformTenantItem | null;
  modalTargetStatus: 'SUSPENDED' | 'ACTIVE';
  confirmTenantCode: string;
  setConfirmTenantCode: (val: string) => void;
  suspendCategory: string;
  setSuspendCategory: (val: string) => void;
  suspendReason: string;
  setSuspendReason: (val: string) => void;
  submittingStatus: boolean;
  isSuspendConfirmValid: boolean;
  onCloseSuspendModal: () => void;
  onConfirmStatusSubmit: (e: React.FormEvent) => Promise<void>;
  drawerTenant: PlatformTenantItem | null;
  quotaWarehouses: number;
  setQuotaWarehouses: (val: number) => void;
  quotaUsers: number;
  setQuotaUsers: (val: number) => void;
  quotaOrders: number;
  formattedQuotaOrders: string;
  handleQuotaOrdersChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  quotaContractRef: string;
  setQuotaContractRef: (val: string) => void;
  quotaEffectiveUntil: string;
  setQuotaEffectiveUntil: (val: string) => void;
  quotaReason: string;
  setQuotaReason: (val: string) => void;
  usedWh: number;
  usedUsr: number;
  usedOrd: number;
  canWriteQuota: boolean;
  submittingQuota: boolean;
  hasQuotaChanged: boolean;
  isBelowUsage: boolean;
  exceedsGrowthTier: boolean;
  onCloseQuotaDrawer: () => void;
  onQuotaSubmit: (e: React.FormEvent) => Promise<void>;
}

export const PlatformQuotaDrawerAndModals: React.FC<PlatformQuotaDrawerAndModalsProps> = ({
  modalTenant, modalTargetStatus, confirmTenantCode, setConfirmTenantCode,
  suspendCategory, setSuspendCategory, suspendReason, setSuspendReason,
  submittingStatus, isSuspendConfirmValid, onCloseSuspendModal, onConfirmStatusSubmit,
  drawerTenant, quotaWarehouses, setQuotaWarehouses, quotaUsers, setQuotaUsers,
  quotaOrders, formattedQuotaOrders, handleQuotaOrdersChange,
  quotaContractRef, setQuotaContractRef, quotaEffectiveUntil, setQuotaEffectiveUntil,
  quotaReason, setQuotaReason, usedWh, usedUsr, usedOrd,
  canWriteQuota, submittingQuota, hasQuotaChanged, isBelowUsage, exceedsGrowthTier,
  onCloseQuotaDrawer, onQuotaSubmit,
}) => {
  const [confirmingDrawerDiscard, setConfirmingDrawerDiscard] = useState<boolean>(false);
  const suspendModalRef = useRef<HTMLDivElement>(null);
  const quotaDrawerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setConfirmingDrawerDiscard(false);
  }, [drawerTenant]);

  const isDrawerDirty =
    Boolean(drawerTenant) &&
    (hasQuotaChanged || Boolean(quotaContractRef.trim() || quotaEffectiveUntil.trim() || quotaReason.trim()));

  const requestCloseDrawer = useCallback(() => {
    if (submittingQuota) return;
    if (isDrawerDirty) setConfirmingDrawerDiscard(true);
    else onCloseQuotaDrawer();
  }, [isDrawerDirty, onCloseQuotaDrawer, submittingQuota]);

  useDialogFocusTrap(suspendModalRef, {
    open: Boolean(modalTenant),
    onEscape: onCloseSuspendModal,
    escapeDisabled: submittingStatus,
  });

  useDialogFocusTrap(quotaDrawerRef, {
    open: Boolean(drawerTenant),
    onEscape: requestCloseDrawer,
    escapeDisabled: submittingQuota,
  });

  const effectiveUntilVn = isoToVnDate(quotaEffectiveUntil);
  const isEffectiveUntilValid = isValidVnCalendarDate(effectiveUntilVn, 'date');
  const suspendReasonLen = suspendReason.trim().length;
  const quotaReasonLen = quotaReason.trim().length;
  const isSaveQuotaDisabled =
    !canWriteQuota ||
    submittingQuota ||
    !hasQuotaChanged ||
    isBelowUsage ||
    (exceedsGrowthTier && quotaContractRef.trim().length === 0) ||
    !isEffectiveUntilValid ||
    quotaReasonLen < 10;

  return (
    <>
      {modalTenant ? (
        <div className="platform-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="modal-suspend-tenant-title" data-testid="modal-confirm-suspend-tenant">
          <div ref={suspendModalRef} tabIndex={-1} className="platform-modal-card" style={{ padding: '18px 20px' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, marginBottom: 10 }}>
              <h2 id="modal-suspend-tenant-title" style={{ margin: 0, fontSize: 16, fontWeight: 700, color: '#0f172a', display: 'flex', alignItems: 'center', gap: 8 }}>
                {modalTargetStatus === 'SUSPENDED' ? (
                  <><AlertTriangle size={20} strokeWidth={2} color="#dc2626" aria-hidden="true" /> Xác nhận tạm khóa doanh nghiệp {modalTenant.tenantCode}</>
                ) : (
                  <><PlayCircle size={20} strokeWidth={2} color="#15803d" aria-hidden="true" /> Xác nhận mở khóa doanh nghiệp {modalTenant.tenantCode}</>
                )}
              </h2>
              <button
                type="button"
                aria-label="Đóng hộp thoại"
                className="platform-btn platform-btn-secondary ui-icon-touch-target"
                style={{ padding: '4px 8px', minWidth: 40, minHeight: 40 }}
                onClick={onCloseSuspendModal}
              >
                <X size={15} strokeWidth={2} aria-hidden="true" />
              </button>
            </div>
            <div className="platform-alert-danger" data-testid="suspend-modal-impact-box">
              <div style={{ fontWeight: 700 }}>Tác động tức thời khi tạm khóa:</div>
              <div>1. <strong>Phiên người dùng:</strong> Toàn bộ {modalTenant.usedUsers ?? 6} nhân sự đang hoạt động của <strong>{normalizeTenantLegalNameVi(modalTenant.companyName, modalTenant.tenantId, modalTenant.tenantCode)}</strong> sẽ bị thu hồi phiên và đăng xuất ngay lập tức.</div>
              <div>2. <strong>Webhook Sàn TMĐT &amp; ĐVVC:</strong> Các sự kiện webhook từ Shopee/TikTok/GHN gửi tới trong lúc tạm khóa được <strong>xếp vào hàng đợi an toàn</strong> để không mất dữ liệu, nhưng tạm dừng tạo đơn xuất mới.</div>
              <div>3. <strong>Đơn đang giao &amp; Hóa đơn chờ:</strong> Các vận đơn đã bàn giao ĐVVC (Đang giao) vẫn được cập nhật trạng thái hoàn tất/hoàn kho; các hóa đơn SaaS đang chờ thanh toán vẫn mở để khách đối soát.</div>
              <div>4. <strong>Thông báo khách hàng:</strong> Hệ thống tự động gửi email thông báo tạm khóa kèm lý do tới {modalTenant.ownerEmail} và hiển thị thông báo trên màn hình của khách hàng.</div>
            </div>
            <div className="platform-alert-neutral" data-testid="suspend-modal-reassurance-box">
              <div>• <strong>An toàn dữ liệu:</strong> Dữ liệu sổ cái tồn kho và sổ cái kế toán được bảo toàn nguyên vẹn 100%.</div>
              <div>• <strong>Khôi phục dễ dàng:</strong> Có thể mở khóa lại bất cứ lúc nào từ menu hành động của doanh nghiệp.</div>
            </div>
            <form onSubmit={onConfirmStatusSubmit}>
              <div className="platform-form-field">
                <label htmlFor="confirm-tenant-code-input">Nhập mã <strong>{modalTenant.tenantCode}</strong> để bật nút xác nhận</label>
                <input id="confirm-tenant-code-input" type="text" autoFocus data-testid="input-confirm-tenant-code" placeholder="Nhập mã doanh nghiệp (VD: TENANT-001)" value={confirmTenantCode} onChange={(e) => setConfirmTenantCode(e.target.value)} />
              </div>
              <div className="platform-form-field">
                <label htmlFor="suspend-category-select">Phân loại lý do tạm khóa</label>
                <select id="suspend-category-select" data-testid="select-suspend-category" value={suspendCategory} onChange={(e) => setSuspendCategory(e.target.value)}>
                  <option value="">-- Chọn phân loại lý do --</option>
                  <option value="OVERDUE_PAYMENT">Quá hạn thanh toán cước SaaS</option>
                  <option value="TERMS_VIOLATION">Vi phạm điều khoản sử dụng</option>
                  <option value="CUSTOMER_REQUEST">Theo yêu cầu tạm ngưng của khách hàng</option>
                  <option value="SECURITY_MAINTENANCE">Rà soát an ninh / Kỹ thuật</option>
                  <option value="OTHER">Lý do khác</option>
                </select>
              </div>
              <div className="platform-form-field">
                <label htmlFor="suspend-reason-input" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                  <span>Lý do quản trị bắt buộc</span>
                  <span style={{ fontSize: 11.5, fontWeight: 600, color: suspendReasonLen >= 10 ? '#047857' : '#64748b', fontVariantNumeric: 'tabular-nums' }}>
                    {formatMinLengthCounterVi(suspendReasonLen, 10)}
                  </span>
                </label>
                <textarea id="suspend-reason-input" rows={2} data-testid="input-suspend-reason" placeholder="Nhập lý do tạm khóa cụ thể..." value={suspendReason} onChange={(e) => setSuspendReason(e.target.value)} />
              </div>
              {!isSuspendConfirmValid && !submittingStatus ? (
                <div
                  id="suspend-submit-disabled-helper"
                  data-testid="suspend-submit-disabled-helper"
                  style={{ fontSize: 11.5, fontWeight: 600, color: '#64748b', marginTop: 8, lineHeight: 1.35 }}
                >
                  Cần nhập đúng mã {modalTenant.tenantCode}, chọn phân loại lý do và ghi chú quản trị (≥ 10 ký tự) để bật nút xác nhận.
                </div>
              ) : null}
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 10 }}>
                <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-cancel-suspend-modal" onClick={onCloseSuspendModal}>Hủy bỏ</button>
                <button
                  type="submit"
                  className="platform-btn platform-btn-danger"
                  data-testid="btn-confirm-suspend-submit"
                  aria-describedby={!isSuspendConfirmValid && !submittingStatus ? 'suspend-submit-disabled-helper' : undefined}
                  disabled={!isSuspendConfirmValid || submittingStatus}
                >
                  {modalTargetStatus === 'SUSPENDED' ? 'Xác nhận tạm khóa doanh nghiệp' : 'Xác nhận mở khóa doanh nghiệp'}
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}

      {drawerTenant ? (
        <div
          className="platform-drawer-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="drawer-adjust-quota-title"
          data-testid="drawer-adjust-tenant-quota"
          onClick={(e) => {
            if (e.target === e.currentTarget) requestCloseDrawer();
          }}
        >
          <div ref={quotaDrawerRef} tabIndex={-1} className="platform-drawer-panel" style={{ padding: '16px 20px', gap: 10, width: 'min(480px, 100vw)', maxWidth: 480 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
              <h2 id="drawer-adjust-quota-title" style={{ margin: 0, fontSize: 16, fontWeight: 700, color: '#0f172a' }}>
                {drawerTenant.tenantCode} — {normalizeTenantLegalNameVi(drawerTenant.companyName, drawerTenant.tenantId, drawerTenant.tenantCode)}
              </h2>
              <button
                type="button"
                aria-label="Đóng hộp thoại"
                className="platform-btn platform-btn-secondary"
                data-testid="btn-close-quota-drawer"
                onClick={requestCloseDrawer}
              >
                <X size={15} strokeWidth={2} aria-hidden="true" />
                <span>Đóng</span>
              </button>
            </div>

            {confirmingDrawerDiscard ? (
              <div
                role="alertdialog"
                aria-label="Xác nhận hủy thay đổi hạn mức"
                data-testid="dirty-dismiss-confirm"
                style={{ padding: '10px 12px', borderRadius: 8, background: '#fffbeb', border: '1px solid #f59e0b', color: '#92400e', fontSize: 12.5, display: 'grid', gap: 8 }}
              >
                <span style={{ fontWeight: 600 }}>Bạn có thay đổi hạn mức chưa lưu. Hủy thay đổi và đóng ngăn điều chỉnh?</span>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                  <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-keep-editing-quota" onClick={() => setConfirmingDrawerDiscard(false)}>
                    Tiếp tục nhập
                  </button>
                  <button type="button" className="platform-btn platform-btn-danger" data-testid="btn-confirm-discard-quota" onClick={() => { setConfirmingDrawerDiscard(false); onCloseQuotaDrawer(); }}>
                    Hủy thay đổi & Đóng
                  </button>
                </div>
              </div>
            ) : null}

            <PlatformQuotaComparisonBox
              drawerTenant={drawerTenant}
              quotaWarehouses={quotaWarehouses}
              quotaUsers={quotaUsers}
              quotaOrders={quotaOrders}
              hasQuotaChanged={hasQuotaChanged}
            />

            {exceedsGrowthTier ? (
              <div
                role="note"
                data-testid="quota-pricing-impact-note"
                style={{ backgroundColor: '#FEF3C7', border: '1px solid #F59E0B', color: '#92400E', padding: '8px 10px', borderRadius: 8, fontSize: 12, fontWeight: 600 }}
              >
                Cảnh báo phụ trội hợp đồng: Hạn mức mới vượt trần tiêu chuẩn của Gói Tăng trưởng. Khoản phụ trội sẽ được ghi nhận theo phụ lục hợp đồng B2B.
              </div>
            ) : null}

            {isBelowUsage ? (
              <div role="alert" data-testid="quota-below-usage-error" style={{ backgroundColor: '#fef2f2', border: '1px solid #fecaca', color: '#991b1b', padding: '8px 10px', borderRadius: 8, fontSize: 12, fontWeight: 600 }}>
                Hạn mức mới không được thấp hơn mức đang sử dụng thực tế ({usedWh} kho • {usedUsr} tài khoản nhân sự • {usedOrd.toLocaleString('vi-VN')} đơn/tháng).
              </div>
            ) : null}

            <form onSubmit={onQuotaSubmit}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div className="platform-form-field">
                  <label htmlFor="drawer-quota-wh">Số kho tối đa</label>
                  <input id="drawer-quota-wh" type="number" data-testid="input-quota-max-warehouses" value={quotaWarehouses} onChange={(e) => setQuotaWarehouses(Number(e.target.value) || 0)} />
                  <div className="platform-field-hint">Đang dùng: {usedWh} kho • Giới hạn hiện tại: {drawerTenant.maxWarehouses} kho</div>
                </div>
                <div className="platform-form-field">
                  <label htmlFor="drawer-quota-users">Tài khoản nhân sự</label>
                  <input id="drawer-quota-users" type="number" data-testid="input-quota-max-users" value={quotaUsers} onChange={(e) => setQuotaUsers(Number(e.target.value) || 0)} />
                  <div className="platform-field-hint">Đang dùng: {usedUsr} tài khoản nhân sự • Giới hạn hiện tại: {drawerTenant.maxUsers} tài khoản nhân sự</div>
                </div>
              </div>
              <div className="platform-form-field">
                <label htmlFor="drawer-quota-orders">Sản lượng đơn / tháng</label>
                <input id="drawer-quota-orders" type="text" inputMode="numeric" data-testid="input-quota-monthly-orders" value={formattedQuotaOrders} onChange={handleQuotaOrdersChange} />
                <div className="platform-field-hint">Đang dùng tháng này: {usedOrd.toLocaleString('vi-VN')} đơn • Hiện tại: {drawerTenant.monthlyOrderQuota.toLocaleString('vi-VN')} đơn (Mới: {quotaOrders.toLocaleString('vi-VN')} đơn)</div>
              </div>
              <div className="platform-form-field">
                <label htmlFor="drawer-quota-contract-ref">
                  {exceedsGrowthTier ? 'Mã phụ lục hợp đồng B2B *' : 'Mã phụ lục hợp đồng B2B'}
                </label>
                <input
                  id="drawer-quota-contract-ref"
                  type="text"
                  required={exceedsGrowthTier}
                  data-testid="input-quota-contract-ref"
                  placeholder="VD: PLHĐ-2026-01"
                  value={quotaContractRef}
                  onChange={(e) => setQuotaContractRef(e.target.value)}
                />
              </div>
              <div className="platform-form-field">
                <label htmlFor="quota-effective-until">Hiệu lực đến ngày *</label>
                <UiVnDateInput
                  id="quota-effective-until"
                  testId="input-quota-effective-until"
                  mode="date"
                  verbalPrefix="Hiệu lực đến"
                  value={effectiveUntilVn}
                  onChange={(vnVal) => {
                    setQuotaEffectiveUntil(isValidVnCalendarDate(vnVal, 'date') ? vnDateToIso(vnVal) : vnVal);
                  }}
                  required
                />
              </div>
              <div className="platform-form-field">
                <label htmlFor="drawer-quota-reason" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                  <span>Lý do điều chỉnh bắt buộc</span>
                  <span style={{ fontSize: 11.5, fontWeight: 600, color: quotaReasonLen >= 10 ? '#047857' : '#64748b', fontVariantNumeric: 'tabular-nums' }}>
                    {formatMinLengthCounterVi(quotaReasonLen, 10)}
                  </span>
                </label>
                <textarea id="drawer-quota-reason" rows={2} data-testid="input-quota-reason" placeholder="Lý do điều chỉnh..." value={quotaReason} onChange={(e) => setQuotaReason(e.target.value)} />
              </div>
              <div
                style={{
                  position: 'sticky',
                  bottom: 0,
                  background: '#ffffff',
                  borderTop: '1px solid #e2e8f0',
                  zIndex: 5,
                  paddingTop: 12,
                  paddingBottom: 4,
                  marginTop: 8,
                  display: 'grid',
                  gap: 8,
                }}
              >
                {isSaveQuotaDisabled ? (
                  <div
                    data-testid="quota-save-disabled-helper"
                    style={{ fontSize: 11.5, fontWeight: 600, color: '#64748b', lineHeight: 1.35 }}
                  >
                    Cần thay đổi ít nhất 1 hạn mức và nhập Mã phụ lục + Lý do (≥ 10 ký tự)
                  </div>
                ) : null}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
                  <button type="button" className="platform-btn platform-btn-secondary" data-testid="btn-cancel-quota-drawer" onClick={requestCloseDrawer}>Hủy</button>
                  <button
                    type="submit"
                    className="platform-btn platform-btn-primary"
                    data-testid="btn-override-quota"
                    disabled={isSaveQuotaDisabled}
                  >
                    Lưu hạn mức mới
                  </button>
                </div>
              </div>
            </form>
          </div>
        </div>
      ) : null}
    </>
  );
};
