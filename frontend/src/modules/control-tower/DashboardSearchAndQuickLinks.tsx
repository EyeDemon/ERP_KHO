import type { ComponentType, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight } from 'lucide-react';
import DashboardRecentActivity from '../../components/dashboard/DashboardRecentActivity';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  actionLabel,
  entityNameLabel,
  entityTypeLabel,
  type GlobalSearchResultItem,
  type OperationalDashboardSummary,
} from '../governance/DashboardLabelsAndTypes';

export interface DashboardQuickLinkItem {
  to: string;
  title: string;
  detail: string;
  icon: ComponentType<{ size?: number; 'aria-hidden'?: boolean | 'true' | 'false' }>;
  permission?: string;
}

export function DashboardGlobalSearchCard({
  searchQuery,
  searchLoading,
  searchResults,
  onChangeSearchQuery,
  onSubmitSearch,
  onClearResults,
}: {
  searchQuery: string;
  searchLoading: boolean;
  searchResults: GlobalSearchResultItem[];
  onChangeSearchQuery: (v: string) => void;
  onSubmitSearch: (e: FormEvent) => void;
  onClearResults: () => void;
}) {
  return (
    <UiCard title="Tìm kiếm toàn doanh nghiệp">
      <form onSubmit={onSubmitSearch} className="ui-inline-actions">
        <input
          aria-label="Tìm kiếm toàn doanh nghiệp"
          type="search"
          value={searchQuery}
          onChange={e => onChangeSearchQuery(e.target.value)}
          placeholder="Tìm mã SP, mã vạch, lô, sê-ri, mã phiếu..."
          style={{ flex: '1 1 360px', minWidth: 260, fontSize: 13.5 }}
        />
        <button
          type="submit"
          disabled={searchLoading || searchQuery.trim().length < 2}
          title={
            searchLoading
              ? 'Đang thực hiện tìm kiếm toàn doanh nghiệp'
              : searchQuery.trim().length < 2
                ? 'Nhập tối thiểu 2 ký tự mã SP, mã vạch hoặc mã phiếu để tìm kiếm'
                : undefined
          }
        >
          {searchLoading ? 'Đang tìm...' : 'Tìm kiếm'}
        </button>
        {searchResults.length > 0 && (
          <button type="button" onClick={onClearResults}>
            Xóa kết quả
          </button>
        )}
      </form>
      {searchResults.length > 0 && (
        <UiTableScroll>
          <table className="ui-table-compact" aria-label="Kết quả tìm kiếm toàn doanh nghiệp">
            <thead>
              <tr>
                <th>Loại</th>
                <th>Mã</th>
                <th>Thông tin</th>
                <th>Chi tiết</th>
                <th>Điều hướng</th>
              </tr>
            </thead>
            <tbody>
              {searchResults.map((item, idx) => (
                <tr key={`${item.entityType}-${item.code}-${idx}`}>
                  <td>
                    <UiBadge>{entityTypeLabel(item.entityType)}</UiBadge>
                  </td>
                  <td>
                    <strong>{item.code}</strong>
                  </td>
                  <td>{item.title}</td>
                  <td>{item.subtitle}</td>
                  <td>
                    <Link to={item.route}>Mở màn hình →</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
}

export function DashboardBottomWorkspaceSection({
  demoRuntime,
  liveLoading,
  liveSummary,
  quickLinks,
  visibleRecentActivities,
  visibleProductionNavigation,
  mismatchCount,
}: {
  demoRuntime: boolean;
  liveLoading: boolean;
  liveSummary: OperationalDashboardSummary | null;
  quickLinks: DashboardQuickLinkItem[];
  visibleRecentActivities: NonNullable<OperationalDashboardSummary['recentActivities']>;
  visibleProductionNavigation: Array<{ path: string; label: string; section: string }>;
  mismatchCount: number;
}) {
  return (
    <>
      <div className="dashboard-grid">
        <UiCard title="Tiếp tục công việc">
          <div className="dashboard-panel-meta">
            {liveSummary
              ? `Phiếu nhập mở: ${liveSummary.openInboundCount} • Cất hàng mở: ${liveSummary.openPutawayCount} • Phiếu xuất mở: ${liveSummary.openOutboundCount} • Điều chuyển mở: ${liveSummary.openTransferCount}`
              : 'Mở phân hệ nghiệp vụ'}
          </div>
          <div className="dashboard-actions">
            {quickLinks.map(({ to, title, detail, icon: Icon }) => (
              <Link key={to} to={to}>
                <Icon size={18} aria-hidden="true" />
                <div>
                  <strong>{title}</strong>
                  <span>{detail}</span>
                </div>
                <ArrowRight size={16} aria-hidden="true" />
              </Link>
            ))}
          </div>
        </UiCard>

        {!demoRuntime ? (
          <DashboardRecentActivity
            liveLoading={liveLoading}
            hasSummary={Boolean(liveSummary)}
            activities={visibleRecentActivities}
            actionLabel={actionLabel}
            entityNameLabel={entityNameLabel}
          />
        ) : (
          <UiCard title="Màn hình nghiệp vụ đã kích hoạt">
            <div className="dashboard-screen-count">{visibleProductionNavigation.length + 1}</div>
            <div className="dashboard-screen-list">
              {visibleProductionNavigation.map(item => (
                <Link key={item.path} to={item.path}>
                  <span>{item.label}</span>
                  <small>{item.section}</small>
                </Link>
              ))}
            </div>
          </UiCard>
        )}
      </div>

      {demoRuntime && (
        <UiCard title="Trạng thái môi trường">
          <div className="dashboard-environment">
            <div>
              <span>Giao diện</span>
              <strong>Chế độ trình diễn</strong>
            </div>
            <div>
              <span>Nguồn dữ liệu</span>
              <strong>Bộ dữ liệu mẫu</strong>
            </div>
            <div>
              <span>Thao tác ghi</span>
              <strong>Chặn ghi (405)</strong>
            </div>
            <div>
              <span>Kết nối máy chủ</span>
              <strong>Chưa kết nối</strong>
            </div>
          </div>
        </UiCard>
      )}

      {mismatchCount > 0 && (
        <div className="dashboard-attention" role="status">
          <AlertTriangle size={18} aria-hidden="true" />
          <span>
            {demoRuntime
              ? `Bộ dữ liệu mẫu hiện có ${mismatchCount} dòng đối chiếu cần chú ý.`
              : `Phát hiện ${mismatchCount} dòng tồn kho bất thường cần đối chiếu.`}{' '}
            Mở phân hệ Đối chiếu tồn kho để xem chi tiết.
          </span>
        </div>
      )}
    </>
  );
}
