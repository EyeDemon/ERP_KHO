import React from 'react';
import type { PlatformTenantItem } from './PlatformTenantsAndQuotaSection';

interface PlatformQuotaComparisonBoxProps {
  drawerTenant: PlatformTenantItem;
  quotaWarehouses: number;
  quotaUsers: number;
  quotaOrders: number;
  hasQuotaChanged: boolean;
}

export const PlatformQuotaComparisonBox: React.FC<PlatformQuotaComparisonBoxProps> = ({
  drawerTenant,
  quotaWarehouses,
  quotaUsers,
  quotaOrders,
  hasQuotaChanged,
}) => {
  const deltaWh = quotaWarehouses - drawerTenant.maxWarehouses;
  const deltaUsr = quotaUsers - drawerTenant.maxUsers;
  const deltaOrd = quotaOrders - drawerTenant.monthlyOrderQuota;

  const renderDeltaBadge = (delta: number, label: string, testId: string) => (
    <span
      data-testid={testId}
      style={{
        display: 'inline-block',
        whiteSpace: 'nowrap',
        marginLeft: 6,
        padding: '1px 6px',
        borderRadius: 999,
        fontSize: 11.5,
        fontWeight: 600,
        color: delta > 0 ? '#1d4ed8' : delta < 0 ? '#b45309' : '#475569',
        background: delta > 0 ? '#eff6ff' : delta < 0 ? '#fffbeb' : '#f1f5f9',
        border: delta > 0 ? '1px solid #bfdbfe' : delta < 0 ? '1px solid #fde68a' : '1px solid #cbd5e1',
      }}
    >
      ({delta >= 0 ? '+' : ''}{label})
    </span>
  );

  const rowStyle = (delta: number): React.CSSProperties => ({
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 12,
    opacity: delta === 0 ? 0.6 : 1,
    padding: '2px 0',
  });

  const valueStyle = (delta: number): React.CSSProperties => ({
    fontSize: 12,
    fontWeight: delta === 0 ? 600 : 700,
    color: delta > 0 ? '#1d4ed8' : delta < 0 ? '#b45309' : '#475569',
    textAlign: 'right',
  });

  return (
    <div className="platform-diff-box" data-testid="quota-old-new-diff">
      <div style={{ fontWeight: 700, marginBottom: 4, color: '#0f172a' }}>
        Đối chiếu hạn mức Trước → Sau
      </div>
      <div style={{ display: 'grid', gap: 2, fontVariantNumeric: 'tabular-nums' }}>
        <div style={rowStyle(deltaWh)} data-changed={deltaWh !== 0 ? 'true' : 'false'}>
          <span style={{ color: '#334155', fontWeight: deltaWh !== 0 ? 600 : 500 }}>Số kho tối đa:</span>
          <span style={valueStyle(deltaWh)}>
            {drawerTenant.maxWarehouses} kho → {quotaWarehouses} kho
            {renderDeltaBadge(deltaWh, `${deltaWh} kho`, 'quota-delta-badge-wh')}
          </span>
        </div>

        <div style={rowStyle(deltaUsr)} data-changed={deltaUsr !== 0 ? 'true' : 'false'}>
          <span style={{ color: '#334155', fontWeight: deltaUsr !== 0 ? 600 : 500 }}>Tài khoản nhân sự:</span>
          <span style={valueStyle(deltaUsr)}>
            {drawerTenant.maxUsers} tài khoản nhân sự → {quotaUsers} tài khoản nhân sự
            {renderDeltaBadge(deltaUsr, `${deltaUsr} tài khoản nhân sự`, 'quota-delta-badge-usr')}
          </span>
        </div>

        <div style={rowStyle(deltaOrd)} data-changed={deltaOrd !== 0 ? 'true' : 'false'}>
          <span style={{ color: '#334155', fontWeight: deltaOrd !== 0 ? 600 : 500 }}>Sản lượng đơn / tháng:</span>
          <span style={valueStyle(deltaOrd)}>
            {drawerTenant.monthlyOrderQuota.toLocaleString('vi-VN')} đơn → {quotaOrders.toLocaleString('vi-VN')} đơn
            {renderDeltaBadge(deltaOrd, `${deltaOrd.toLocaleString('vi-VN')} đơn`, 'quota-delta-badge-ord')}
          </span>
        </div>
      </div>

      {!hasQuotaChanged ? (
        <div style={{ fontSize: 11.5, color: '#475569', marginTop: 4 }}>
          Chưa có thay đổi hạn mức (nhập giá trị mới bên dưới để xem chênh lệch)
        </div>
      ) : null}
    </div>
  );
};
