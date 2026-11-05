import React from 'react';
import { formatUserDisplayNameVi } from '../../services/permissionPresentation';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import { sanitizePlatformJargonVi } from './platformBillingHelpers';
import type { PlatformAuditLogItem } from './PlatformSupportAndAuditSection';

interface PlatformAuditLogsTableCardProps {
  auditLogs: PlatformAuditLogItem[];
}

export const PlatformAuditLogsTableCard: React.FC<PlatformAuditLogsTableCardProps> = ({ auditLogs }) => {
  const operationalLogs = auditLogs.filter(
    (log) =>
      !/^Authentication\./i.test(log.action || '') &&
      !/^(Login|Logout|Refresh)$/i.test(log.action || '') &&
      log.entityName !== 'UserSession',
  );
  const displayLogs = operationalLogs.length > 0 ? operationalLogs : auditLogs;

  return (
    <UiCard
      title="Nhật ký kiểm toán quản trị nền tảng"
      subtitle="Truy vết bất biến 100% thao tác tạm khóa/mở khóa thuê bao, điều chỉnh hạn mức (Trước / Sau) và phiên hỗ trợ kỹ thuật"
    >
      {displayLogs.length === 0 ? (
        <p style={{ margin: 0, fontSize: 13, color: '#475569' }} data-testid="platform-audit-empty">
          Chưa có sự kiện kiểm toán quản trị nền tảng nào được ghi nhận.
        </p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table className="ui-table" data-testid="platform-audit-table">
            <thead>
              <tr>
                <th style={{ minWidth: 112 }}>Thời gian</th>
                <th style={{ minWidth: 108 }}>Miền bảo mật</th>
                <th style={{ minWidth: 108 }}>Người thực hiện</th>
                <th style={{ minWidth: 112 }}>Hành động</th>
                <th style={{ minWidth: 100 }}>Đối tượng</th>
                <th style={{ minWidth: 140 }}>Trạng thái Trước / Sau</th>
                <th style={{ minWidth: 140 }}>Lý do &amp; Ticket</th>
                <th style={{ minWidth: 96 }}>Mã truy vết</th>
              </tr>
            </thead>
            <tbody>
              {displayLogs.slice(0, 15).map((log) => (
                <tr key={log.id} data-testid={`platform-audit-row-${log.id}`}>
                  <td style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {new Date(log.timestamp).toLocaleString('vi-VN')}
                  </td>
                  <td>
                    <UiBadge tone={log.securityDomain === 'PLATFORM' ? 'info' : 'neutral'}>
                      {log.securityDomain === 'PLATFORM' ? 'Quản trị nền tảng' : log.securityDomain}
                    </UiBadge>
                  </td>
                  <td style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                    {formatUserDisplayNameVi(log.username, log.securityDomain === 'PLATFORM' ? 'SuperAdmin' : undefined)}
                  </td>
                  <td className="ui-code" style={{ fontWeight: 700, fontSize: 12 }}>
                    {sanitizePlatformJargonVi(log.action)}
                  </td>
                  <td style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {sanitizePlatformJargonVi(log.entityName)} {log.entityId ? `#${log.entityId}` : ''}
                  </td>
                  <td style={{ fontSize: 12, color: '#334155', maxWidth: 280, wordBreak: 'break-word' }}>
                    {log.oldValues || log.newValues ? (
                      <code className="ui-code">
                        {log.oldValues ? `${sanitizePlatformJargonVi(log.oldValues)} / ` : ''}
                        {sanitizePlatformJargonVi(log.newValues || '')}
                      </code>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td>{log.reason ? sanitizePlatformJargonVi(log.reason) : '—'}</td>
                  <td className="ui-code" style={{ fontSize: 12, color: '#475569', whiteSpace: 'nowrap' }}>
                    {log.correlationId || `CORR-${log.id}`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </UiCard>
  );
};
