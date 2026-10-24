import { Link } from 'react-router-dom';

export const DashboardQuickActions = () => {
  const actions = [
    { to: '/cod-reconciliation', label: 'Đối soát COD 3 Chiều', testId: 'quick-action-cod-reconciliation' },
    { to: '/onboarding-wizard', label: 'Thiết lập Kho Nhanh 5 Bước', testId: 'quick-action-onboarding-wizard' },
    { to: '/handover-manifests', label: 'Biên bản Bàn giao Bưu tá', testId: 'quick-action-handover-manifests' },
    { to: '/cost-valuation', label: 'Giá vốn Bình quân & Kết xuất MISA', testId: 'quick-action-cost-valuation' },
    { to: '/saas-billing', label: 'Gói cước Thuê bao SaaS & VietQR', testId: 'quick-action-saas-billing' },
  ];

  return (
    <div className="ui-inline-wrap" style={{ gap: 8 }} data-testid="dashboard-specialized-quick-actions">
      {actions.map((item) => (
        <Link
          key={item.to}
          to={item.to}
          data-testid={item.testId}
          className="ui-btn-secondary"
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            padding: '6px 12px',
            borderRadius: 6,
            border: '1px solid #cbd5e1',
            background: '#f8fafc',
            color: '#0f172a',
            fontSize: 12.5,
            fontWeight: 600,
            textDecoration: 'none',
          }}
        >
          {item.label} →
        </Link>
      ))}
    </div>
  );
};

export default DashboardQuickActions;
