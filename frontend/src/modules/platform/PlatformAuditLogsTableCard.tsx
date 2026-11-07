import React from 'react';
import { formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import { sanitizePlatformJargonVi } from './platformBillingHelpers';
import type { PlatformAuditLogItem } from './PlatformSupportAndAuditSection';
import { useIsPlatformMobile768 } from './usePlatformMobileViewport';

interface PlatformAuditLogsTableCardProps {
  auditLogs: PlatformAuditLogItem[];
}

const isAuthNoiseLog = (log: PlatformAuditLogItem): boolean => {
  const act = (log.action || '').trim();
  const ent = (log.entityName || '').trim();
  const labelVi = (log.actionLabelVi || '').trim();
  if (/xác thực quản trị/i.test(labelVi) || /^PLATFORM_/i.test(act)) {
    return false;
  }
  return (
    /^(Authentication|Auth|Session|Token)\./i.test(act) ||
    /^(Login|Logout|Refresh|RefreshToken|TokenRefresh|LoginSucceeded|LoginFailed)/i.test(act) ||
    /^(Đăng nhập hệ thống|Làm mới phiên đăng nhập)$/i.test(act) ||
    /^(UserSession|RefreshToken)$/i.test(ent)
  );
};

const ACTION_LABEL_MAP_VI: Readonly<Record<string, string>> = {
  'Platform.TenantSuspended': 'Tạm khóa doanh nghiệp thuê bao',
  'Platform.TenantReactivated': 'Mở khóa doanh nghiệp thuê bao',
  'Platform.TenantActivated': 'Kích hoạt doanh nghiệp thuê bao',
  'Platform.TenantStatusUpdated': 'Cập nhật trạng thái doanh nghiệp',
  'Platform.TenantQuotaUpdated': 'Điều chỉnh hạn mức thuê bao',
  'Platform.QuotaUpdated': 'Điều chỉnh hạn mức thuê bao',
  'Platform.BillingInvoiceReconciled': 'Đối soát hóa đơn thuê bao SaaS',
  'Platform.InvoiceReconciled': 'Đối soát hóa đơn thuê bao SaaS',
  'Platform.ImpersonationStarted': 'Khởi tạo phiên hỗ trợ kỹ thuật',
  'Platform.ImpersonationStopped': 'Kết thúc phiên hỗ trợ kỹ thuật',
  Create: 'Tạo mới bản ghi',
  Update: 'Cập nhật dữ liệu',
  Delete: 'Xóa bản ghi',
};

const ENTITY_LABEL_MAP_VI: Readonly<Record<string, string>> = {
  Tenant: 'Doanh nghiệp thuê bao',
  SaasTenant: 'Doanh nghiệp thuê bao',
  TenantSubscription: 'Gói thuê bao SaaS',
  SaasInvoice: 'Hóa đơn SaaS',
  SaasBillingInvoice: 'Hóa đơn SaaS',
  BillingInvoice: 'Hóa đơn SaaS',
  SupportImpersonationSession: 'Phiên hỗ trợ kỹ thuật',
  ImpersonationSession: 'Phiên hỗ trợ kỹ thuật',
  UserSession: 'Phiên xác thực quản trị',
  InboundReceipt: 'Phiếu nhập kho',
  SalesOrder: 'Đơn xuất hàng',
  StockAdjustment: 'Phiếu điều chỉnh tồn',
  Product: 'Mặt hàng',
  Warehouse: 'Kho hàng',
};

const STATE_FIELD_LABELS_VI: Readonly<Record<string, string>> = {
  status: 'Trạng thái',
  lifecycleStatus: 'Trạng thái',
  subscriptionStatus: 'Trạng thái gói',
  monthlyOrderQuota: 'Đơn/tháng',
  monthlyOrderLimit: 'Đơn/tháng',
  maxWarehouses: 'Kho tối đa',
  maxUsers: 'Nhân sự tối đa',
  maxProducts: 'Mặt hàng tối đa',
  planCode: 'Gói cước',
  bankTransactionRef: 'Mã GD',
  ticketCode: 'Ticket',
  ttlMinutes: 'Thời hạn (phút)',
};

const STATE_VALUE_TOKENS_VI: Readonly<Record<string, string>> = {
  ACTIVE: 'Đang hoạt động',
  SUSPENDED: 'Tạm khóa',
  READ_ONLY: 'Chỉ đọc',
  PAID: 'Đã thanh toán',
  PENDING: 'Chờ đối soát',
  UNPAID: 'Chưa thanh toán',
  EXPIRED: 'Đã hết hạn',
  STARTER: 'Gói Khởi đầu',
  GROWTH: 'Gói Tăng trưởng',
  ENTERPRISE: 'Gói Doanh nghiệp',
  CUSTOMER_TENANT: 'Doanh nghiệp thuê bao',
  PLATFORM: 'Quản trị nền tảng',
};

const humanizeTokenVi = (val: unknown): string => {
  if (val === null || val === undefined || val === '') return '';
  if (typeof val === 'boolean') return val ? 'Bật' : 'Tắt';
  if (typeof val === 'number') return val.toLocaleString('vi-VN');
  const str = String(val).trim();
  const mapped = STATE_VALUE_TOKENS_VI[str.toUpperCase()];
  if (mapped) return mapped;
  return sanitizePlatformJargonVi(str);
};

const formatRawStateChunkVi = (raw?: string | null): string => {
  const trimmed = (raw ?? '').trim();
  if (!trimmed) return '';
  if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const parts = Object.entries(parsed as Record<string, unknown>)
          .filter(([, v]) => v !== null && v !== undefined && v !== '')
          .slice(0, 4)
          .map(([k, v]) => {
            const label = STATE_FIELD_LABELS_VI[k] || sanitizePlatformJargonVi(k);
            return `${label}: ${humanizeTokenVi(v)}`;
          });
        return parts.join(' • ');
      }
    } catch {
      // Fallback below removes raw braces/quotes if malformed JSON
    }
  }
  const cleaned = trimmed.replace(/[{}"]/g, '').replace(/,/g, ' • ').trim();
  return humanizeTokenVi(cleaned);
};

const formatActionVi = (log: PlatformAuditLogItem): string => {
  if (log.actionLabelVi?.trim()) return sanitizePlatformJargonVi(log.actionLabelVi);
  const act = (log.action || '').trim();
  if (ACTION_LABEL_MAP_VI[act]) return ACTION_LABEL_MAP_VI[act];
  if (/suspend/i.test(act)) return 'Tạm khóa doanh nghiệp thuê bao';
  if (/reactivat|unsuspend|unlock/i.test(act)) return 'Mở khóa doanh nghiệp thuê bao';
  if (/quota/i.test(act)) return 'Điều chỉnh hạn mức thuê bao';
  if (/reconcil/i.test(act)) return 'Đối soát hóa đơn thuê bao SaaS';
  if (/impersonat.*stop|stop.*impersonat/i.test(act)) return 'Kết thúc phiên hỗ trợ kỹ thuật';
  if (/impersonat|support/i.test(act)) return 'Khởi tạo phiên hỗ trợ kỹ thuật';
  return sanitizePlatformJargonVi(act.replace(/[._]/g, ' '));
};

const formatTargetVi = (log: PlatformAuditLogItem): string => {
  if (log.targetCodeVi?.trim()) return sanitizePlatformJargonVi(log.targetCodeVi);
  const ent = (log.entityName || '').trim();
  if (/^(Tenant|SaasTenant)$/i.test(ent)) {
    const tid = log.entityId || log.tenantId || 1;
    return `TENANT-${String(tid).padStart(3, '0')}`;
  }
  const label = ENTITY_LABEL_MAP_VI[ent] || sanitizePlatformJargonVi(ent) || 'Hệ thống';
  return log.entityId ? `${label} #${log.entityId}` : label;
};

const formatStateChangeVi = (log: PlatformAuditLogItem): string => {
  if (log.stateChangeVi?.trim()) return sanitizePlatformJargonVi(log.stateChangeVi);
  const beforeVi = formatRawStateChunkVi(log.oldValues);
  const afterVi = formatRawStateChunkVi(log.newValues);
  if (beforeVi && afterVi) return `${beforeVi} → ${afterVi}`;
  return afterVi || beforeVi || '—';
};

export const PlatformAuditLogsTableCard: React.FC<PlatformAuditLogsTableCardProps> = ({ auditLogs }) => {
  const isMobile768 = useIsPlatformMobile768();
  const displayLogs = auditLogs.filter((log) => !isAuthNoiseLog(log));

  return (
    <UiCard
      title="Nhật ký kiểm toán quản trị nền tảng"
      subtitle="Truy vết bất biến 100% thao tác tạm khóa/mở khóa thuê bao, điều chỉnh hạn mức (Trước / Sau) và phiên hỗ trợ kỹ thuật"
    >
      {displayLogs.length === 0 ? (
        <p style={{ margin: 0, fontSize: 13, color: '#475569' }} data-testid="platform-audit-empty">
          Chưa có sự kiện kiểm toán quản trị nền tảng nào được ghi nhận.
        </p>
      ) : isMobile768 ? (
        <div className="platform-mobile-cards" data-testid="platform-audit-mobile-cards">
          {displayLogs.slice(0, 15).map((log) => {
            const domainVi =
              log.securityDomainLabelVi?.trim() ||
              (log.securityDomain === 'PLATFORM' ? 'Quản trị nền tảng' : 'Doanh nghiệp thuê bao');
            const actorVi =
              log.actorDisplayVi?.trim() ||
              formatUserDisplayNameVi(log.username, log.securityDomain === 'PLATFORM' ? 'SuperAdmin' : undefined);
            const summaryVi =
              log.summaryVi?.trim() ||
              (log.reason ? sanitizePlatformJargonVi(formatRawStateChunkVi(log.reason)) : '—');
            return (
              <article key={log.id} className="platform-mobile-card" data-testid={`platform-audit-row-${log.id}`}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8, flexWrap: 'wrap' }}>
                  <span style={{ fontSize: 12, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
                    {new Date(log.timestamp).toLocaleString('vi-VN')}
                  </span>
                  <UiBadge tone={log.securityDomain === 'PLATFORM' ? 'info' : 'neutral'}>{domainVi}</UiBadge>
                </div>
                <div style={{ fontWeight: 700, fontSize: 13, color: '#0f172a', overflowWrap: 'anywhere' }}>
                  {formatActionVi(log)}
                </div>
                <div style={{ fontSize: 12.5, color: '#334155', display: 'flex', flexWrap: 'wrap', gap: '4px 10px' }}>
                  <span>Thực hiện: <strong>{actorVi}</strong></span>
                  <span>Đối tượng: <strong>{formatTargetVi(log)}</strong></span>
                </div>
                <div style={{ fontSize: 12, color: '#334155', overflowWrap: 'anywhere' }}>
                  Trước / Sau: {formatStateChangeVi(log)}
                </div>
                <div style={{ fontSize: 12, color: '#475569', overflowWrap: 'anywhere' }}>
                  Chi tiết: {summaryVi}
                </div>
                <div className="ui-code" style={{ fontSize: 11.5, color: '#64748b', whiteSpace: 'normal', overflowWrap: 'anywhere' }}>
                  Mã truy vết: {log.correlationId || `CORR-${log.id}`}
                </div>
              </article>
            );
          })}
        </div>
      ) : (
        <UiTableScroll ariaLabel="Bảng nhật ký kiểm toán quản trị nền tảng">
          <table className="ui-table" data-testid="platform-audit-table">
            <thead>
              <tr>
                <th style={{ minWidth: 112 }}>Thời gian</th>
                <th style={{ minWidth: 108 }}>Miền bảo mật</th>
                <th style={{ minWidth: 108 }}>Người thực hiện</th>
                <th style={{ minWidth: 124 }}>Hành động</th>
                <th style={{ minWidth: 104 }}>Đối tượng</th>
                <th style={{ minWidth: 150 }}>Trạng thái Trước / Sau</th>
                <th style={{ minWidth: 150 }}>Lý do &amp; Ticket</th>
                <th style={{ minWidth: 96 }}>Mã truy vết</th>
              </tr>
            </thead>
            <tbody>
              {displayLogs.slice(0, 15).map((log) => {
                const domainVi =
                  log.securityDomainLabelVi?.trim() ||
                  (log.securityDomain === 'PLATFORM' ? 'Quản trị nền tảng' : 'Doanh nghiệp thuê bao');
                const actorVi =
                  log.actorDisplayVi?.trim() ||
                  formatUserDisplayNameVi(log.username, log.securityDomain === 'PLATFORM' ? 'SuperAdmin' : undefined);
                const summaryVi =
                  log.summaryVi?.trim() ||
                  (log.reason ? sanitizePlatformJargonVi(formatRawStateChunkVi(log.reason)) : '—');
                return (
                  <tr key={log.id} data-testid={`platform-audit-row-${log.id}`}>
                    <td style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                      {new Date(log.timestamp).toLocaleString('vi-VN')}
                    </td>
                    <td>
                      <UiBadge tone={log.securityDomain === 'PLATFORM' ? 'info' : 'neutral'}>
                        {domainVi}
                      </UiBadge>
                    </td>
                    <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                      {actorVi}
                    </td>
                    <td style={{ fontWeight: 700, fontSize: 12.5, color: '#0f172a' }}>
                      {formatActionVi(log)}
                    </td>
                    <td style={{ fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>
                      {formatTargetVi(log)}
                    </td>
                    <td style={{ fontSize: 12, color: '#334155', maxWidth: 280, wordBreak: 'break-word' }}>
                      {formatStateChangeVi(log)}
                    </td>
                    <td>{summaryVi}</td>
                    <td className="ui-code" style={{ fontSize: 12, color: '#475569', whiteSpace: 'nowrap' }}>
                      {log.correlationId || `CORR-${log.id}`}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
};
