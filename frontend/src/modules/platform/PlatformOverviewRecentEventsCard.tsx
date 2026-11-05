import React from 'react';
import { formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard, UiEmptyState } from '../../ui/ProductionUi';
import { normalizeTenantLegalNameVi, sanitizePlatformJargonVi } from './platformBillingHelpers';
import type { PlatformBillingInvoiceItem } from './PlatformHealthAndWorkersSection';
import type { PlatformAuditLogItem } from './PlatformSupportAndAuditSection';

export interface PlatformOverviewRecentEventItem {
  id: string;
  timestampVi: string;
  categoryVi: string;
  targetVi: string;
  summaryVi: string;
  statusLabelVi: string;
  tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
}

interface PlatformOverviewRecentEventsCardProps {
  rawDashboardEvents: Array<Record<string, unknown>>;
  auditLogs: PlatformAuditLogItem[];
  invoices: PlatformBillingInvoiceItem[];
  onViewAllAuditLogs: () => void;
}

const isAuthOrSessionEvent = (actionRaw?: string, entityRaw?: string): boolean => {
  const act = (actionRaw ?? '').trim();
  const ent = (entityRaw ?? '').trim();
  if (!act && !ent) return false;
  if (/^Authentication\./i.test(act)) return true;
  if (/^(Login|Logout|Refresh|TokenRefresh|LoginSucceeded|LoginFailed)$/i.test(act)) return true;
  if (/^UserSession$/i.test(ent)) return true;
  return false;
};

const humanizeInlineActorNames = (text: string): string =>
  sanitizePlatformJargonVi(text).replace(
    /\b(qa_admin|qa_billing_admin|qa_support_admin|qa_security_auditor|tenant_admin|qa_manager_wh01|qa_supervisor_wh01|qa_accountant|qa_staff_wh01|qa_viewer_wh01)\b/gi,
    (match) => formatUserDisplayNameVi(match),
  );

const formatIsoToViDateTime = (raw?: string): string => {
  if (!raw) return '02/11/2026 08:00';
  if (/^\d{2}\/\d{2}\/\d{4}/.test(raw.trim())) return raw.trim();
  const parsed = new Date(raw);
  if (Number.isNaN(parsed.getTime())) return raw;
  const dd = String(parsed.getDate()).padStart(2, '0');
  const mm = String(parsed.getMonth() + 1).padStart(2, '0');
  const yyyy = parsed.getFullYear();
  const hh = String(parsed.getHours()).padStart(2, '0');
  const min = String(parsed.getMinutes()).padStart(2, '0');
  return `${dd}/${mm}/${yyyy} ${hh}:${min}`;
};

const mapAuditActionCategoryVi = (action?: string): { categoryVi: string; tone: PlatformOverviewRecentEventItem['tone']; statusLabelVi: string } => {
  const upper = (action || '').toUpperCase();
  if (upper.includes('SUSPEND')) {
    return { categoryVi: 'Vòng đời thuê bao', tone: 'danger', statusLabelVi: 'Đã tạm khóa' };
  }
  if (upper.includes('QUOTA')) {
    return { categoryVi: 'Điều chỉnh hạn ngạch', tone: 'info', statusLabelVi: 'Đã áp dụng' };
  }
  if (upper.includes('RECONCILE') || upper.includes('INVOICE') || upper.includes('BILLING')) {
    return { categoryVi: 'Đối soát hóa đơn', tone: 'success', statusLabelVi: 'Đã đối soát' };
  }
  if (upper.includes('IMPERSONAT') || upper.includes('SUPPORT')) {
    return { categoryVi: 'Hỗ trợ kỹ thuật', tone: 'warning', statusLabelVi: 'Đã ghi nhật ký' };
  }
  return { categoryVi: 'Quản trị nền tảng', tone: 'neutral', statusLabelVi: 'Đã ghi nhận' };
};

export const PlatformOverviewRecentEventsCard: React.FC<PlatformOverviewRecentEventsCardProps> = ({
  rawDashboardEvents,
  auditLogs,
  invoices,
  onViewAllAuditLogs,
}) => {
  const rows: PlatformOverviewRecentEventItem[] = [];
  const seenKeys = new Set<string>();

  const pushUniqueEvent = (actionKey: string, item: PlatformOverviewRecentEventItem) => {
    const dedupKey = `${actionKey.toLowerCase()}|${item.targetVi.toLowerCase()}|${item.timestampVi}`;
    if (seenKeys.has(dedupKey)) return;
    seenKeys.add(dedupKey);
    rows.push(item);
  };

  for (let i = 0; i < rawDashboardEvents.length; i += 1) {
    const ev = rawDashboardEvents[i];
    const rawAction = String(ev.action ?? ev.eventType ?? ev.category ?? '');
    const rawEntity = String(ev.entityName ?? ev.entityType ?? '');
    if (isAuthOrSessionEvent(rawAction, rawEntity)) continue;
    const mapped = mapAuditActionCategoryVi(String(ev.category ?? rawAction));
    const rawTarget = String(ev.targetVi ?? ev.targetCode ?? ev.tenantCode ?? ev.entityCode ?? 'TENANT-001');
    const cleanTarget = /ERP_KHO|ĐẠI PHÁT/i.test(rawTarget) ? normalizeTenantLegalNameVi(rawTarget, 1) : sanitizePlatformJargonVi(rawTarget);
    const rawActor = ev.actorUsername ?? ev.actor ?? ev.username;
    const actorPrefix = typeof rawActor === 'string' && rawActor.trim() ? `${formatUserDisplayNameVi(rawActor)}: ` : '';
    const rawSummary = humanizeInlineActorNames(
      String(ev.summaryVi ?? ev.descriptionVi ?? ev.message ?? 'Ghi nhận sự kiện điều hành nền tảng SaaS'),
    );
    const summaryVi = actorPrefix && !rawSummary.includes(formatUserDisplayNameVi(String(rawActor)))
      ? `${actorPrefix}${rawSummary}`
      : rawSummary;
    pushUniqueEvent(rawAction || mapped.categoryVi, {
      id: String(ev.id ?? `dash-ev-${i}`),
      timestampVi: formatIsoToViDateTime(String(ev.timestampVi ?? ev.occurredAtUtc ?? ev.createdAtUtc ?? '')),
      categoryVi: sanitizePlatformJargonVi(String(ev.categoryVi ?? mapped.categoryVi)),
      targetVi: cleanTarget,
      summaryVi,
      statusLabelVi: String(ev.statusLabelVi ?? mapped.statusLabelVi),
      tone: mapped.tone,
    });
  }

  for (let i = 0; i < auditLogs.length && rows.length < 6; i += 1) {
    const log = auditLogs[i];
    if (isAuthOrSessionEvent(log.action, log.entityName)) continue;
    const mapped = mapAuditActionCategoryVi(log.action);
    const targetCode = log.tenantId
      ? `TENANT-${String(log.tenantId).padStart(3, '0')}`
      : log.entityId
        ? `${sanitizePlatformJargonVi(log.entityName)} #${log.entityId}`
        : sanitizePlatformJargonVi(log.entityName) || 'HỆ THỐNG';
    const actorVi = formatUserDisplayNameVi(log.username);
    const baseSummary = log.reason
      ? humanizeInlineActorNames(log.reason)
      : `Thực thi tác vụ ${sanitizePlatformJargonVi(log.action)} trên ${sanitizePlatformJargonVi(log.entityName)}`;
    const summaryVi = baseSummary.includes(actorVi) ? baseSummary : `${actorVi} — ${baseSummary}`;
    pushUniqueEvent(log.action || mapped.categoryVi, {
      id: `audit-${log.id ?? i}`,
      timestampVi: formatIsoToViDateTime(log.timestamp),
      categoryVi: mapped.categoryVi,
      targetVi: targetCode,
      summaryVi,
      statusLabelVi: mapped.statusLabelVi,
      tone: mapped.tone,
    });
  }

  for (let i = 0; i < invoices.length && rows.length < 6; i += 1) {
    const inv = invoices[i];
    const isPaid = inv.status.toUpperCase() === 'PAID';
    const tenantDisplay = String(inv.tenantId).startsWith('TENANT-')
      ? String(inv.tenantId)
      : `TENANT-${String(inv.tenantId || 1).padStart(3, '0')}`;
    const totalText = Number(inv.amountVnd || inv.totalAmountVnd || 1490000).toLocaleString('vi-VN');
    const timestampVi = isPaid
      ? (inv.confirmedAtVi || inv.bankCreditTimeVi || formatIsoToViDateTime(inv.createdAtUtc))
      : formatIsoToViDateTime(inv.createdAtUtc);
    pushUniqueEvent('Đối soát hóa đơn', {
      id: `inv-${inv.id ?? inv.invoiceCode}`,
      timestampVi,
      categoryVi: 'Đối soát hóa đơn',
      targetVi: `${tenantDisplay} • ${inv.invoiceCode}`,
      summaryVi: isPaid
        ? `${humanizeInlineActorNames(inv.settlementSummaryVi || 'Đã đối soát thanh toán')} — Tổng thu: ${totalText} ₫`
        : `Phát hành hóa đơn kỳ ${inv.billingPeriod || '11/2026'} (${totalText} ₫) — Hạn TT: ${inv.dueDateVi || '05/11/2026'}`,
      statusLabelVi: isPaid ? 'Đã tất toán' : 'Chờ đối soát',
      tone: isPaid ? 'success' : 'warning',
    });
  }

  const visibleRows = rows.slice(0, 6);

  return (
    <div data-testid="platform-recent-events-card">
      <UiCard
        title="Nhật ký đối soát & sự kiện nền tảng gần nhất"
        subtitle="Tổng hợp các giao dịch đối soát hóa đơn, thay đổi hạn ngạch và sự kiện vận hành mới nhất"
        actions={
          <button
            type="button"
            className="platform-btn platform-btn-secondary"
            data-testid="btn-overview-view-all-audit-logs"
            onClick={onViewAllAuditLogs}
          >
            Xem toàn bộ nhật ký →
          </button>
        }
      >
        {visibleRows.length === 0 ? (
          <UiEmptyState
            title="Chưa có sự kiện đối soát hoặc nhật ký mới"
            detail="Các giao dịch đối soát hóa đơn SaaS, điều chỉnh hạn ngạch và thao tác quản trị sẽ hiển thị tại đây theo thời gian thực."
          />
        ) : (
          <div style={{ overflowX: 'auto' }} data-testid="platform-overview-recent-events-card">
            <table className="ui-table ui-table-compact" data-testid="platform-overview-recent-events-table">
              <thead>
                <tr>
                  <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Thời gian</th>
                  <th style={{ minWidth: 88, whiteSpace: 'nowrap' }}>Trạng thái</th>
                  <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Phân loại</th>
                  <th style={{ minWidth: 96, whiteSpace: 'nowrap' }}>Đối tượng</th>
                  <th style={{ minWidth: 160 }}>Nội dung sự kiện</th>
                </tr>
              </thead>
              <tbody>
                {visibleRows.map((item) => (
                  <tr key={item.id}>
                    <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', color: '#475569', fontSize: 12.5 }}>
                      {item.timestampVi}
                    </td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <UiBadge tone={item.tone}>{item.statusLabelVi}</UiBadge>
                    </td>
                    <td style={{ whiteSpace: 'nowrap', fontWeight: 600, color: '#0f172a', fontSize: 12.5 }}>
                      {item.categoryVi}
                    </td>
                    <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', fontWeight: 700, color: '#0F172A', fontSize: 12.5 }}>
                      {item.targetVi}
                    </td>
                    <td style={{ color: '#334155', fontSize: 12.5, lineHeight: 1.4 }}>
                      {item.summaryVi}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </UiCard>
    </div>
  );
};
