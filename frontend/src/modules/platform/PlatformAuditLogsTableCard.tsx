import React from 'react';
import { UiBadge, UiCard } from '../../ui/ProductionUi';
import type { PlatformAuditLogItem } from './PlatformSupportAndAuditSection';

interface PlatformAuditLogsTableCardProps {
  auditLogs: PlatformAuditLogItem[];
}

export const PlatformAuditLogsTableCard: React.FC<PlatformAuditLogsTableCardProps> = ({ auditLogs }) => {
  return (
    <UiCard
      title="Nhật ký kiểm toán quản trị nền tảng"
      subtitle="Truy vết bất biến 100% thao tác tạm khóa/mở khóa thuê bao, điều chỉnh hạn mức (Trước / Sau) và phiên hỗ trợ kỹ thuật"
    >
      {auditLogs.length === 0 ? (
        <p style={{ margin: 0, fontSize: 13, color: '#475569' }} data-testid="platform-audit-empty">
          Chưa có sự kiện kiểm toán quản trị nền tảng nào được ghi nhận.
        </p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table className="ui-table" data-testid="platform-audit-table">
            <thead>
              <tr>
                <th>Thời gian (UTC)</th>
                <th>Miền bảo mật</th>
                <th>Hành động</th>
                <th>Đối tượng</th>
                <th>Trạng thái Trước / Sau</th>
                <th>Lý do &amp; Ticket</th>
                <th>Mã truy vết</th>
              </tr>
            </thead>
            <tbody>
              {auditLogs.slice(0, 15).map((log) => (
                <tr key={log.id} data-testid={`platform-audit-row-${log.id}`}>
                  <td style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                    {new Date(log.timestamp).toLocaleString('vi-VN')}
                  </td>
                  <td>
                    <UiBadge tone={log.securityDomain === 'PLATFORM' ? 'info' : 'neutral'}>
                      {log.securityDomain === 'PLATFORM' ? 'Quản trị nền tảng' : log.securityDomain}
                    </UiBadge>
                  </td>
                  <td style={{ fontWeight: 700, fontFamily: 'monospace', fontSize: 12 }}>
                    {log.action}
                  </td>
                  <td style={{ fontVariantNumeric: 'tabular-nums' }}>
                    {log.entityName} {log.entityId ? `#${log.entityId}` : ''}
                  </td>
                  <td style={{ fontSize: 12, color: '#334155', maxWidth: 280, wordBreak: 'break-word' }}>
                    {log.oldValues || log.newValues ? (
                      <code>
                        {log.oldValues ? `${log.oldValues} / ` : ''}
                        {log.newValues || ''}
                      </code>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td>{log.reason || '—'}</td>
                  <td style={{ fontFamily: 'monospace', fontSize: 12, color: '#475569' }}>
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
