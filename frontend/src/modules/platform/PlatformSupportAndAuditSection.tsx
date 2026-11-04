import React, { useEffect, useState } from 'react';
import { Headset, Lock } from 'lucide-react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';
import { PlatformAuditLogsTableCard } from './PlatformAuditLogsTableCard';

export interface PlatformAuditLogItem {
  id: number;
  tenantId: number;
  userId?: number | null;
  impersonatedBySupportUserId?: number | null;
  securityDomain: string;
  action: string;
  entityName: string;
  entityId?: number | null;
  oldValues?: string | null;
  newValues?: string | null;
  reason?: string | null;
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
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
                gap: 8,
                alignItems: 'end',
              }}
            >
              <div className="platform-form-field" style={{ marginBottom: 0 }}>
                <label htmlFor="select-impersonation-tenant" style={{ whiteSpace: 'nowrap', minHeight: 20 }}>
                  Chọn doanh nghiệp cần hỗ trợ
                </label>
                <select
                  id="select-impersonation-tenant"
                  data-testid="select-impersonation-tenant"
                  value={targetTenantId}
                  onChange={(e) => setTargetTenantId(Number(e.target.value) || 1)}
                >
                  {tenants.length === 0 ? (
                    <option value={1}>TENANT-001 — CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO</option>
                  ) : (
                    tenants.map((t) => (
                      <option key={t.tenantId} value={t.tenantId}>
                        {t.tenantCode} — {t.companyName === 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)' ? 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO' : t.companyName}
                      </option>
                    ))
                  )}
                </select>
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
                <label htmlFor="input-impersonation-reason" style={{ whiteSpace: 'nowrap', minHeight: 20 }}>
                  Lý do truy cập hỗ trợ (tối thiểu 10 ký tự)
                </label>
                <input
                  id="input-impersonation-reason"
                  type="text"
                  data-testid="input-impersonation-reason"
                  placeholder="Nhập lý do hỗ trợ kỹ thuật theo yêu cầu khách hàng (tối thiểu 10 ký tự)..."
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
                <span style={{ fontSize: 12, color: '#475569', display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <Lock size={13} strokeWidth={2} aria-hidden="true" />
                  <span>Mọi thao tác tra cứu trong phiên hỗ trợ đều được ghi nhận tự động vào Nhật ký kiểm toán kèm danh tính kỹ sư thực hiện.</span>
                </span>
                <button
                  type="submit"
                  className="platform-btn platform-btn-primary"
                  data-testid="btn-start-impersonation"
                  disabled={!isImpersonationValid || submitting || isDuplicateSession}
                >
                  <Headset size={15} strokeWidth={2} aria-hidden="true" />
                  <span>Khởi tạo phiên hỗ trợ</span>
                </button>
              </div>
            </form>

            {activeSession ? (
              <div style={{ marginTop: 18 }}>
                <div style={{ fontSize: 13, fontWeight: 700, marginBottom: 8, color: '#0f172a' }}>
                  Danh sách phiên hỗ trợ đang hoạt động
                </div>
                <table className="ui-table" data-testid="active-impersonation-sessions-table" style={{ fontSize: 12 }}>
                  <thead>
                    <tr>
                      <th style={{ whiteSpace: 'nowrap' }}>Mã phiên</th>
                      <th style={{ minWidth: 145, maxWidth: 165 }}>Doanh nghiệp</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Mã phiếu</th>
                      <th style={{ minWidth: 155, maxWidth: 175 }}>Lý do hỗ trợ</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Người mở</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Bắt đầu</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Chế độ</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Còn lại</th>
                      <th style={{ whiteSpace: 'nowrap' }}>Thao tác</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td style={{ fontWeight: 700, fontFamily: 'monospace', fontSize: 11.5, whiteSpace: 'nowrap' }}>
                        {activeSession.sessionId}
                      </td>
                      <td style={{ minWidth: 145, maxWidth: 165 }}>
                        {activeSession.targetTenantCode} — {!activeSession.targetTenantName || activeSession.targetTenantName === 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)' ? 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO' : activeSession.targetTenantName}
                      </td>
                      <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{activeSession.ticketCode}</td>
                      <td style={{ minWidth: 155, maxWidth: 175 }}>{activeSession.reason}</td>
                      <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{activeSession.startedByUsername || currentActor}</td>
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
              </div>
            ) : null}
          </div>
        </UiCard>
      ) : null}

      {showAudit ? <PlatformAuditLogsTableCard auditLogs={auditLogs} /> : null}
    </>
  );
};
