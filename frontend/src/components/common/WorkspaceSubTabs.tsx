import React from 'react';
import { useInRouterContext, useLocation, useNavigate } from 'react-router-dom';

export interface WorkspaceSubTabItem {
  path: string;
  label: string;
  matchPaths?: string[];
}

export interface WorkspaceGroupConfig {
  groupId: string;
  groupTitle: string;
  tabs: WorkspaceSubTabItem[];
}

export const WORKSPACE_GROUPS: WorkspaceGroupConfig[] = [
  {
    groupId: 'inbound',
    groupTitle: 'Phân hệ Nhập kho',
    tabs: [
      { path: '/purchase-orders', label: 'Đơn mua hàng (PO)' },
      { path: '/asns', label: 'Thông báo nhận hàng (ASN)' },
      { path: '/dock-yard', label: 'Điều phối cổng bãi' },
      { path: '/import-receipts', label: 'Phiếu nhập kho' },
      { path: '/putaway-tasks', label: 'Nhiệm vụ cất hàng' },
    ],
  },
  {
    groupId: 'outbound',
    groupTitle: 'Phân hệ Xuất kho',
    tabs: [
      { path: '/sales-orders', label: 'Đơn bán hàng', matchPaths: ['/sales-orders', '/outbound-demand'] },
      { path: '/backorders', label: 'Đơn thiếu hàng' },
      { path: '/stock-reservations', label: 'Giữ hàng tồn kho' },
      { path: '/stock-allocations', label: 'Phân bổ tồn kho' },
      { path: '/picking-tasks', label: 'Nhiệm vụ lấy hàng' },
      { path: '/packing-sessions', label: 'Trạm đóng gói' },
      { path: '/kit-work-orders', label: 'Lắp ráp bộ Kit' },
      { path: '/export-receipts', label: 'Phiếu xuất kho' },
    ],
  },
  {
    groupId: 'shipping',
    groupTitle: 'Phân hệ Giao vận và Hàng hoàn',
    tabs: [
      { path: '/shipments', label: 'Thực hiện giao hàng' },
      { path: '/handover-manifests', label: 'Biên bản bàn giao' },
      { path: '/customer-returns', label: 'Quản lý hàng hoàn (RMA)' },
    ],
  },
  {
    groupId: 'inventory',
    groupTitle: 'Phân hệ Tồn kho',
    tabs: [
      { path: '/inventory', label: 'Tồn kho tức thời' },
      { path: '/inventory-locks', label: 'Phong tỏa tồn kho' },
      { path: '/inventory-movements', label: 'Di chuyển nội bộ' },
      { path: '/inventory-reversals', label: 'Đảo bút toán' },
      { path: '/stock-transfers', label: 'Điều chuyển liên kho' },
      { path: '/stocktakes', label: 'Kiểm kê định kỳ' },
      { path: '/inventory-traceability', label: 'Truy vết phả hệ lô' },
      { path: '/inventory-reconciliation', label: 'Đối chiếu sổ cái' },
    ],
  },
  {
    groupId: 'catalog',
    groupTitle: 'Phân hệ Sản phẩm',
    tabs: [
      { path: '/products', label: 'Danh mục sản phẩm' },
      { path: '/categories', label: 'Nhóm ngành hàng' },
      { path: '/units', label: 'Đơn vị tính' },
      { path: '/business-partners', label: 'Đối tác kinh doanh' },
    ],
  },
  {
    groupId: 'finance',
    groupTitle: 'Phân hệ Tài chính',
    tabs: [
      { path: '/cod-reconciliation', label: 'Đối soát COD' },
      { path: '/cost-valuation', label: 'Định giá tồn kho WAC' },
      { path: '/misa-exports', label: 'Kết xuất kế toán' },
      { path: '/partner-debts', label: 'Công nợ phải thu phải trả' },
    ],
  },
  {
    groupId: 'governance',
    groupTitle: 'Phân hệ Quản trị công ty',
    tabs: [
      { path: '/warehouses', label: 'Danh mục kho' },
      { path: '/warehouse-structure', label: 'Cấu trúc kệ bin' },
      { path: '/warehouse-map', label: 'Sơ đồ nhiệt kho' },
      { path: '/warehouse-calendar', label: 'Lịch làm việc kho' },
      { path: '/users', label: 'Người dùng' },
      { path: '/roles', label: 'Vai trò RBAC' },
      { path: '/warehouse-scopes', label: 'Phạm vi kho' },
      { path: '/permissions', label: 'Quản trị quyền truy cập' },
      { path: '/approvals', label: 'Hàng đợi phê duyệt' },
      { path: '/saas-billing', label: 'Gói cước thuê bao SaaS' },
      { path: '/onboarding-wizard', label: 'Thiết lập kho 5 bước' },
    ],
  },
];

export function resolveWorkspaceGroup(pathname: string): WorkspaceGroupConfig | null {
  for (const group of WORKSPACE_GROUPS) {
    for (const tab of group.tabs) {
      const candidates = tab.matchPaths ?? [tab.path];
      if (candidates.some((p) => pathname === p || pathname.startsWith(p + '/'))) {
        return group;
      }
    }
  }
  return null;
}

interface WorkspaceSubTabsCoreProps {
  pathname: string;
  onNavigate: (path: string) => void;
}

const WorkspaceSubTabsCore: React.FC<WorkspaceSubTabsCoreProps> = ({ pathname, onNavigate }) => {
  const group = resolveWorkspaceGroup(pathname);
  if (!group) return null;

  return (
    <div
      data-testid="workspace-sub-tabs"
      role="tablist"
      aria-label={`Phân hệ ${group.groupTitle}`}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        gap: 6,
        padding: '8px 12px',
        marginBottom: 12,
        background: 'var(--surface-card, #ffffff)',
        border: '1px solid var(--border-subtle, #e2e8f0)',
        borderRadius: 8,
      }}
    >
      <span
        style={{
          fontSize: 11.5,
          fontWeight: 700,
          color: '#475569',
          marginRight: 6,
          textTransform: 'uppercase',
          letterSpacing: '0.02em',
        }}
      >
        {group.groupTitle}:
      </span>
      {group.tabs.map((tab) => {
        const candidates = tab.matchPaths ?? [tab.path];
        const isActive = candidates.some((p) => pathname === p || pathname.startsWith(p + '/'));
        return (
          <button
            key={tab.path}
            type="button"
            role="tab"
            aria-selected={isActive}
            data-testid={`workspace-sub-tab-${tab.path.replace(/\//g, '')}`}
            onClick={() => onNavigate(tab.path)}
            style={{
              fontSize: 12.5,
              padding: '5px 11px',
              borderRadius: 6,
              border: isActive ? '1px solid #0f172a' : '1px solid #cbd5e1',
              background: isActive ? '#0f172a' : '#ffffff',
              color: isActive ? '#ffffff' : '#1e293b',
              fontWeight: isActive ? 600 : 500,
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
};

const WorkspaceSubTabsWithRouter: React.FC = () => {
  const location = useLocation();
  const navigate = useNavigate();
  return <WorkspaceSubTabsCore pathname={location.pathname} onNavigate={(path) => navigate(path)} />;
};

export const WorkspaceSubTabs: React.FC = () => {
  const inRouter = useInRouterContext();
  if (inRouter) {
    return <WorkspaceSubTabsWithRouter />;
  }
  const fallbackPathname = typeof window !== 'undefined' ? window.location.pathname : '/';
  return (
    <WorkspaceSubTabsCore
      pathname={fallbackPathname}
      onNavigate={(path) => {
        if (typeof window !== 'undefined' && window.history?.pushState) {
          window.history.pushState({}, '', path);
        }
      }}
    />
  );
};

export default WorkspaceSubTabs;
