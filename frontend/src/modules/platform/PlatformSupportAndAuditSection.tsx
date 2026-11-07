import React, { useEffect, useState } from 'react';
import { Headset, Lock } from 'lucide-react';
import { formatMinLengthCounterVi, formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { CANONICAL_TENANT1_LEGAL_NAME, normalizeTenantLegalNameVi } from './platformBillingHelpers';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';
import { PlatformAuditLogsTableCard } from './PlatformAuditLogsTableCard';

export interface PlatformAuditLogItem {
  id: number;
  tenantId: number;
  userId?: number | null;
  username?: string | null;
  actorDisplayVi?: string | null;
  impersonatedBySupportUserId?: number | null;
  securityDomain: string;
  securityDomainLabelVi?: string | null;
  action: string;
  actionLabelVi?: string | null;
  entityName: string;
  entityId?: number | null;
  targetCodeVi?: string | null;
  oldValues?: string | null;
  newValues?: string | null;
  stateChangeVi?: string | null;
  reason?: string | null;
  summaryVi?: string | null;
  result?: string | null;
  correlationId?: string | null;
  timestamp: string;
}

export interface ActiveImpersonationSession {
  sessionId: string;
  targetTenantId: number;
  targetTenantCode: string;
  targetTenantName: string;
  ticketCode: string;
  reason: string;
  ttlMinutes: number;
  expiresAtUtc: string;
  startedByUsername?: string;
  startedAtUtc?: string;
}

interface Props {
  showSupport: boolean;
  showAudit: boolean;
  tenants: PlatformTenantItem[];
  selectedSupportTenantId: number;
  canImpersonate: boolean;
  currentActor?: string;
  auditLogs: PlatformAuditLogItem[];
  activeSession: ActiveImpersonationSession | null;
  onStartImpersonation: (
    targetTenantId: number,
    ticketCode: string,
    reason: string,
    ttlMinutes: number,
  ) => Promise<void>;
  onStopImpersonation: () => Promise<void>;
}

const formatStartTime = (iso?: string): string => {
  if (!iso) return '14:30:00';
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return '14:30:00';
  return parsed.toLocaleTimeString('vi-VN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
};

export const PlatformSupportAndAuditSection: React.FC<Props> = ({
  showSupport,
  showAudit,
  tenants,
  selectedSupportTenantId,
  canImpersonate,
  currentActor = 'SuperAdmin',
  auditLogs,
  activeSession,
  onStartImpersonation,
  onStopImpersonation,
}) => {
  const [targetTenantId, setTargetTenantId] = useState<number>(selectedSupportTenantId || 1);
  const [ticketCode, setTicketCode] = useState<string>('');
  const [reason, setReason] = useState<string>('');
  const [ttlMinutes, setTtlMinutes] = useState<number>(30);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [remainingSeconds, setRemainingSeconds] = useState<number>(1794);

  useEffect(() => {
    if (selectedSupportTenantId > 0) {
      setTargetTenantId(selectedSupportTenantId);
    }
  }, [selectedSupportTenantId]);

  useEffect(() => {
    if (!activeSession) return;
    const diff = activeSession.expiresAtUtc
      ? Math.floor((new Date(activeSession.expiresAtUtc).getTime() - Date.now()) / 1000)
      : activeSession.ttlMinutes * 60;
    setRemainingSeconds(!Number.isNaN(diff) && diff > 0 ? diff : Math.max(60, activeSession.ttlMinutes * 60));
    const timer = window.setInterval(() => {
      setRemainingSeconds((prev) => (prev > 0 ? prev - 1 : 0));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [activeSession]);

  const formatCountdown = (sec: number) => {
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  };

  const isDuplicateSession =
    activeSession !== null && Number(activeSession.targetTenantId) === Number(targetTenantId);

  const isImpersonationValid =
    canImpersonate &&
    !isDuplicateSession &&
    targetTenantId > 0 &&
    ticketCode.trim().length >= 4 &&
    reason.trim().length >= 10;

  const disabledImpersonationReason = !canImpersonate
    ? 'Tài khoản hiện tại không có quyền khởi tạo phiên hỗ trợ kỹ thuật.'
    : isDuplicateSession
      ? 'Đang có phiên hỗ trợ hoạt động cho doanh nghiệp này.'
      : ticketCode.trim().length < 4 && reason.trim().length < 10
        ? 'Nhập Mã Ticket (≥ 4 ký tự) và Lý do hỗ trợ (≥ 10 ký tự) để mở khóa.'
        : ticketCode.trim().length < 4
          ? 'Nhập Mã Ticket hỗ trợ tối thiểu 4 ký tự để mở khóa.'
          : reason.trim().length < 10
            ? 'Nhập Lý do truy cập hỗ trợ tối thiểu 10 ký tự để mở khóa.'
            : submitting
              ? 'Đang gửi yêu cầu khởi tạo phiên hỗ trợ...'
              : null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isImpersonationValid) return;
    setSubmitting(true);
    try {
      await onStartImpersonation(targetTenantId, ticketCode.trim(), reason.trim(), ttlMinutes);
      setTicketCode('');
      setReason('');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      {showSupport ? (
        <UiCard title="Trung tâm hỗ trợ kỹ thuật có kiểm soát">
          <div data-testid="platform-impersonation-card">
            <form
              onSubmit={handleSubmit}
              className="platform-impersonation-form-grid"
            >
              <div className="platform-form-field" style={{ marginBottom: 0 }}>
                <label htmlFor="select-impersonation-tenant" style={{ whiteSpace: 'nowrap', minHeight: 20 }}>
                  Chọn doanh nghiệp cần hỗ trợ
                </label>
                <select
                  id="select-impersonation-tenant"
                  data-testid="select-impersonation-tenant"
                  style={{ width: '100%' }}
                  value={targetTenantId}
                  onChange={(e) => setTargetTenantId(Number(e.target.value) || 1)}
                >
                  {tenants.length === 0 ? (
                    <option value={1}>TENANT-001 — Kho vận Đại Phát</option>
                  ) : (
                    tenants.map((t) => {
                      const fullLegal = normalizeTenantLegalNameVi(t.companyName, t.tenantId, t.tenantCode);
                      const shortName = fullLegal.includes('Đại Phát')
                        ? 'Kho vận Đại Phát'
                        : fullLegal.length > 20
                          ? fullLegal.slice(0, 20).trim()
                          : fullLegal;
                      return (
                        <option key={t.tenantId} value={t.tenantId}>
                          {t.tenantCode} — {shortName}
                        </option>
                      );
                    })
                  )}
                </select>
                <small style={{ fontSize: 11.5, color: '#475569' }}>
                  Pháp nhân: {tenants.find((t) => t.tenantId === targetTenantId) ? normalizeTenantLegalNameVi(tenants.find((t) => t.tenantId === targetTenantId)?.companyName, targetTenantId) : CANONICAL_TENANT1_LEGAL_NAME}
                </small>
                <input type="hidden" data-testid="input-impersonation-tenant-id" value={targetTenantId} readOnly />
              </div>

              <div className="platform-form-field" style={{ marginBottom: 0 }}>
                <label htmlFor="input-impersonation-ticket" style={{ whiteSpace: 'nowrap', minHeight: 20 }}>
                  Mã Ticket hỗ trợ (Bắt buộc)
                </label>
                <input
                  id="input-impersonation-ticket"
                  type="text"
                  data-testid="input-impersonation-ticket"
                  placeholder="VD: SUP-2026-108"
                  value={ticketCode}
                  onChange={(e) => setTicketCode(e.target.value)}
                />
              </div>

              <div className="platform-form-field" style={{ marginBottom: 0 }}>
                <label htmlFor="select-impersonation-ttl" style={{ whiteSpace: 'nowrap', minHeight: 20 }}>
                  Thời hạn phiên tự hủy (TTL)
                </label>
                <select
                  id="select-impersonation-ttl"
                  data-testid="select-impersonation-ttl"
                  value={ttlMinutes}
                  onChange={(e) => setTtlMinutes(Number(e.target.value) || 30)}
                >
                  <option value={15}>15 phút (Khẩn cấp)</option>
                  <option value={30}>30 phút (Tiêu chuẩn)</option>
                  <option value={60}>60 phút (Kiểm toán sâu)</option>
                </select>
              </div>

              <div className="platform-form-field" style={{ marginBottom: 0 }}>
                <label htmlFor="input-impersonation-reason" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 6, flexWrap: 'wrap', minHeight: 20 }}>
                  <span>Lý do truy cập hỗ trợ</span>
                  <span style={{ fontSize: 11, fontWeight: 600, color: reason.trim().length >= 10 ? '#047857' : '#64748b', fontVariantNumeric: 'tabular-nums' }}>
                    {formatMinLengthCounterVi(reason.trim().length, 10)}
                  </span>
                </label>
                <input
                  id="input-impersonation-reason"
                  type="text"
                  data-testid="input-impersonation-reason"
                  placeholder="Nhập lý do hỗ trợ kỹ thuật..."
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                />
              </div>

              {isDuplicateSession && activeSession ? (
                <div
                  role="alert"
                  data-testid="impersonation-duplicate-guard-msg"
                  style={{
                    gridColumn: '1 / -1',
                    padding: '8px 12px',
                    borderRadius: 8,
                    backgroundColor: '#fffbeb',
                    border: '1px solid #fde68a',
                    color: '#92400e',
                    fontSize: 12.5,
                    fontWeight: 600,
                  }}
                >
                  {`Doanh nghiệp này đang có phiên hỗ trợ hoạt động (${activeSession.sessionId}). Vui lòng kết thúc phiên hiện tại trước khi tạo phiên mới.`}
                </div>
              ) : null}

              <div style={{ gridColumn: '1 / -1', display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 2, gap: 12, flexWrap: 'wrap' }}>
                <span style={{ fontSize: 12, color: '#475569', display: 'inline-flex', alignItems: 'center', gap: 6, flex: '1 1 260px' }}>
                  <Lock size={13} strokeWidth={2} aria-hidden="true" style={{ flexShrink: 0 }} />
                  <span>Mọi thao tác tra cứu trong phiên hỗ trợ đều được ghi nhận tự động vào Nhật ký kiểm toán kèm danh tính kỹ sư thực hiện.</span>
                </span>
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 4 }}>
                  <button
                    type="submit"
                    className="platform-btn platform-btn-primary"
                    data-testid="btn-start-impersonation"
                    aria-describedby={disabledImpersonationReason ? 'impersonation-submit-disabled-reason' : undefined}
                    disabled={!isImpersonationValid || submitting || isDuplicateSession}
                    style={{ minHeight: 44 }}
                  >
                    <Headset size={15} strokeWidth={2} aria-hidden="true" />
                    <span>Khởi tạo phiên hỗ trợ</span>
                  </button>
                  {disabledImpersonationReason ? (
                    <span
                      id="impersonation-submit-disabled-reason"
                      data-testid="impersonation-submit-disabled-reason"
                      style={{ fontSize: 11.5, fontWeight: 600, color: '#64748b' }}
                    >
                      {disabledImpersonationReason}
                    </span>
                  ) : null}
                </div>
              </div>
            </form>

            {activeSession ? (
              <div style={{ marginTop: 18 }}>
                <div style={{ fontSize: 13, fontWeight: 700, marginBottom: 8, color: '#0f172a' }}>
                  Danh sách phiên hỗ trợ đang hoạt động
                </div>
                <UiTableScroll ariaLabel="Bảng phiên hỗ trợ kỹ thuật đang hoạt động">
                <table className="ui-table" data-testid="active-impersonation-sessions-table" style={{ fontSize: 12 }}>
                  <thead>
                    <tr>
                      <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Mã phiên</th>
                      <th style={{ minWidth: 160 }}>Doanh nghiệp</th>
                      <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Mã phiếu</th>
                      <th style={{ minWidth: 155 }}>Lý do hỗ trợ</th>
                      <th style={{ minWidth: 112, whiteSpace: 'nowrap' }}>Người mở</th>
                      <th style={{ minWidth: 80, whiteSpace: 'nowrap' }}>Bắt đầu</th>
                      <th style={{ minWidth: 80, whiteSpace: 'nowrap' }}>Chế độ</th>
                      <th style={{ minWidth: 76, whiteSpace: 'nowrap' }}>Còn lại</th>
                      <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td className="ui-code" style={{ fontWeight: 700, fontSize: 11.5, whiteSpace: 'nowrap' }}>
                        {activeSession.sessionId}
                      </td>
                      <td style={{ minWidth: 160 }}>
                        {activeSession.targetTenantCode} — {normalizeTenantLegalNameVi(activeSession.targetTenantName, activeSession.targetTenantId, activeSession.targetTenantCode)}
                      </td>
                      <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{activeSession.ticketCode}</td>
                      <td style={{ minWidth: 155 }}>{activeSession.reason}</td>
                      <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                        {formatUserDisplayNameVi(activeSession.startedByUsername || currentActor, 'SuperAdmin')}
                      </td>
                      <td style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                        {formatStartTime(activeSession.startedAtUtc)}
                      </td>
                      <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone="warning">Chỉ đọc</UiBadge></td>
                      <td style={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: '#b45309', whiteSpace: 'nowrap' }}>
                        {formatCountdown(remainingSeconds)}
                      </td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <button
                          type="button"
                          className="platform-btn platform-btn-danger"
                          data-testid="btn-end-session-row-1"
                          style={{ padding: '5px 10px', whiteSpace: 'nowrap' }}
                          onClick={onStopImpersonation}
                        >
                          Kết thúc
                        </button>
                      </td>
                    </tr>
                  </tbody>
                </table>
                </UiTableScroll>
              </div>
            ) : null}
          </div>
        </UiCard>
      ) : null}

      {showAudit ? <PlatformAuditLogsTableCard auditLogs={auditLogs} /> : null}
    </>
  );
};
