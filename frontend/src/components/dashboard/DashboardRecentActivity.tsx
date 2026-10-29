import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';

export type DashboardRecentActivityItem = {
  id: number;
  action: string;
  actionLabel?: string | null;
  entityName: string;
  entityId: number;
  warehouseId?: number | null;
  warehouseName?: string | null;
  referenceCode?: string | null;
  result?: string | null;
  timestamp: string;
};

type DashboardRecentActivityProps = {
  liveLoading: boolean;
  hasSummary: boolean;
  activities: DashboardRecentActivityItem[];
  actionLabel: (action: string, serverLabel?: string | null) => string;
  entityNameLabel: (entityName: string) => string;
};

export const DashboardRecentActivity = ({
  liveLoading,
  hasSummary,
  activities,
  actionLabel,
  entityNameLabel,
}: DashboardRecentActivityProps) => {
  return (
    <UiCard title="Hoạt động vận hành gần đây">
      {liveLoading ? (
        <p role="status">Đang tải nhật ký vận hành...</p>
      ) : !hasSummary || activities.length === 0 ? (
        <p className="ui-muted-text">Chưa có hoạt động vận hành gần đây trong phạm vi kho được phép.</p>
      ) : (
        <div style={{ maxHeight: 240, overflowY: 'auto' }}>
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Hoạt động vận hành gần đây">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Hành động</th>
                  <th>Đối tượng</th>
                  <th>Kho</th>
                  <th>Kết quả</th>
                </tr>
              </thead>
              <tbody>
                {activities.map((item) => (
                  <tr key={item.id}>
                    <td>{new Date(item.timestamp).toLocaleString('vi-VN')}</td>
                    <td><UiBadge>{actionLabel(item.action, item.actionLabel)}</UiBadge></td>
                    <td>
                      {entityNameLabel(item.entityName)}
                      {item.referenceCode && !item.entityName.includes(`(${item.referenceCode})`)
                        ? ` (${item.referenceCode})`
                        : item.entityId
                          ? ` #${item.entityId}`
                          : ''}
                    </td>
                    <td>{item.warehouseName || (item.warehouseId ? `Kho #${item.warehouseId}` : 'Toàn hệ thống')}</td>
                    <td>
                      <UiBadge tone={item.result === 'Success' || !item.result ? 'success' : 'warning'}>
                        {item.result === 'Success' || !item.result ? 'Thành công' : item.result}
                      </UiBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
        </div>
      )}
    </UiCard>
  );
};

export default DashboardRecentActivity;
