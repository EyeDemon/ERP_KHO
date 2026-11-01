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
    groupTitle: 'Nhập Kho & Tiếp Nhận (Inbound)',
    tabs: [
      { path: '/purchase-orders', label: 'Đơn mua PO' },
      { path: '/asns', label: 'Thông báo ASN' },
      { path: '/dock-yard', label: 'Điều phối Bãi xe' },
      { path: '/import-receipts', label: 'Phiếu nhập & Kiểm nhận' },
      { path: '/putaway-tasks', label: 'Tác vụ Cất hàng' },
      { path: '/customer-returns', label: 'Hoàn trả RMA' },
    ],
  },
  {
    groupId: 'outbound',
    groupTitle: 'Xuất Kho & Giao Vận (Outbound)',
    tabs: [
      {
        path: '/outbound-demand',
        label: 'Đơn bán & Giữ hàng',
        matchPaths: ['/outbound-demand', '/stock-reservations', '/stock-allocations', '/backorders'],
      },
      { path: '/picking-tasks', label: 'Tác vụ Lấy hàng' },
      { path: '/packing-sessions', label: 'Phiên Đóng gói' },
      { path: '/export-receipts', label: 'Phiếu xuất & Xuất nhanh' },
      { path: '/shipments', label: 'Vận đơn & Tem AWB' },
      { path: '/handover-manifests', label: 'Biên bản Bàn giao' },
    ],
  },
  {
    groupId: 'inventory',
    groupTitle: 'Tồn Kho & Kiểm Kê (Inventory)',
    tabs: [
      {
        path: '/inventory',
        label: 'Tồn kho 5 Nhóm & Lô/Date',
        matchPaths: ['/inventory', '/inventory-locks', '/inventory-movements', '/inventory-reversals'],
      },
      { path: '/stock-transfers', label: 'Điều chuyển nội bộ' },
      { path: '/stocktakes', label: 'Kiểm kê định kỳ' },
      { path: '/inventory-traceability', label: 'Truy vết Gia phả Lô' },
      { path: '/inventory-reconciliation', label: 'Đối soát Sổ cái' },
    ],
  },
  {
    groupId: 'catalog',
    groupTitle: 'Danh Mục & Đối Tác (Catalog & Partners)',
    tabs: [
      { path: '/products', label: 'Sản phẩm & Danh mục', matchPaths: ['/products', '/categories'] },
      {
        path: '/warehouses',
        label: 'Kho & Sơ đồ vị trí',
        matchPaths: ['/warehouses', '/warehouse-structure', '/warehouse-map', '/warehouse-calendar'],
      },
      { path: '/units', label: 'Đơn vị tính & Quy đổi' },
      { path: '/business-partners', label: 'Khách hàng & NCC' },
      { path: '/partner-debts', label: 'Công nợ & COD', matchPaths: ['/partner-debts', '/cod-reconciliation'] },
    ],
  },
  {
    groupId: 'governance',
    groupTitle: 'Quản Trị & Báo Cáo (Governance & Reports)',
    tabs: [
      { path: '/approvals', label: 'Phê duyệt 2 lớp' },
      { path: '/cost-valuation', label: 'Giá vốn & Báo cáo', matchPaths: ['/cost-valuation', '/misa-exports'] },
      { path: '/permissions', label: 'Phân quyền & Bảo mật' },
      { path: '/saas-billing', label: 'Gói cước & Hạn mức SaaS' },
      { path: '/onboarding-wizard', label: 'Thiết lập kho ban đầu' },
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
