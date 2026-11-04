import React from 'react';
import { UiBadge, UiCard, UiEmptyState } from '../../ui/ProductionUi';
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

  for (let i = 0; i < rawDashboardEvents.length; i += 1) {
    const ev = rawDashboardEvents[i];
    const mapped = mapAuditActionCategoryVi(String(ev.category ?? ev.action ?? ev.eventType ?? ''));
    rows.push({
      id: String(ev.id ?? `dash-ev-${i}`),
      timestampVi: formatIsoToViDateTime(String(ev.timestampVi ?? ev.occurredAtUtc ?? ev.createdAtUtc ?? '')),
      categoryVi: String(ev.categoryVi ?? mapped.categoryVi),
      targetVi: String(ev.targetCode ?? ev.tenantCode ?? ev.entityCode ?? 'TENANT-001'),
      summaryVi: String(ev.summaryVi ?? ev.descriptionVi ?? ev.message ?? 'Ghi nhận sự kiện điều hành nền tảng SaaS'),
      statusLabelVi: String(ev.statusLabelVi ?? mapped.statusLabelVi),
      tone: mapped.tone,
    });
  }

  for (let i = 0; i < auditLogs.length && rows.length < 6; i += 1) {
    const log = auditLogs[i];
    const mapped = mapAuditActionCategoryVi(log.action);
    const targetCode = log.tenantId
      ? `TENANT-${String(log.tenantId).padStart(3, '0')}`
      : log.entityId
        ? `${log.entityName} #${log.entityId}`
        : log.entityName || 'HỆ THỐNG';
    rows.push({
      id: `audit-${log.id ?? i}`,
      timestampVi: formatIsoToViDateTime(log.timestamp),
      categoryVi: mapped.categoryVi,
      targetVi: targetCode,
      summaryVi: log.reason || `Thực thi tác vụ ${log.action} trên ${log.entityName}`,
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
    rows.push({
      id: `inv-${inv.id ?? inv.invoiceCode}`,
      timestampVi: isPaid
        ? (inv.confirmedAtVi || inv.bankCreditTimeVi || formatIsoToViDateTime(inv.createdAtUtc))
        : formatIsoToViDateTime(inv.createdAtUtc),
      categoryVi: 'Đối soát hóa đơn',
      targetVi: `${tenantDisplay} • ${inv.invoiceCode}`,
      summaryVi: isPaid
        ? `${inv.settlementSummaryVi || 'Đã đối soát thanh toán'} — Tổng thu: ${totalText} ₫`
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
                  <th style={{ whiteSpace: 'nowrap' }}>Thời gian</th>
                  <th style={{ whiteSpace: 'nowrap' }}>Trạng thái</th>
                  <th style={{ whiteSpace: 'nowrap' }}>Phân loại</th>
                  <th style={{ whiteSpace: 'nowrap' }}>Đối tượng</th>
                  <th>Nội dung sự kiện</th>
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
                    <td style={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', fontWeight: 600, color: '#1d4ed8', fontSize: 12.5 }}>
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
