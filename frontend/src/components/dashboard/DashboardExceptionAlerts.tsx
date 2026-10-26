import { Link } from 'react-router-dom';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';

export type DashboardWarehouseTaskItem = {
  taskType: string;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: string;
  priority: string;
  createdAt: string;
  route: string;
};

export type DashboardOperationalExceptionItem = {
  category: string;
  code: string;
  warehouseName: string;
  severity: string;
  summary: string;
  route: string;
};

type DashboardExceptionAlertsProps = {
  filteredTasks: DashboardWarehouseTaskItem[];
  taskTypeFilter: string;
  onTaskTypeFilterChange: (value: string) => void;
  liveExceptions: DashboardOperationalExceptionItem[];
  acknowledgedCodes: string[];
  acknowledgingCode: string | null;
  onAcknowledgeException: (code: string) => void;
  taskTypeLabel: (type: string) => string;
  taskStatusLabel: (status: string) => string;
  priorityLabel: (priority: string) => string;
  exceptionCategoryLabel: (category: string) => string;
  severityLabel: (severity: string) => string;
};

export const DashboardExceptionAlerts = ({
  filteredTasks,
  taskTypeFilter,
  onTaskTypeFilterChange,
  liveExceptions,
  acknowledgedCodes,
  acknowledgingCode,
  onAcknowledgeException,
  taskTypeLabel,
  taskStatusLabel,
  priorityLabel,
  exceptionCategoryLabel,
  severityLabel,
}: DashboardExceptionAlertsProps) => {
  return (
    <div className="dashboard-grid">
      <UiCard title={`Hàng đợi tác vụ kho đang mở • ${filteredTasks.length}`}>
        <div className="ui-inline-actions">
          <label>
            Loại tác vụ:{' '}
            <select aria-label="Lọc loại tác vụ kho" value={taskTypeFilter} onChange={(e) => onTaskTypeFilterChange(e.target.value)}>
              <option value="">Tất cả tác vụ</option>
              <option value="PUTAWAY">Cất hàng</option>
              <option value="PICKING">Lấy hàng</option>
              <option value="PACKING">Đóng gói</option>
              <option value="INBOUND_RECEIPT">Phiếu nhập nháp</option>
              <option value="STOCKTAKE">Kiểm kê nháp</option>
              <option value="DOCK_YARD">Lịch hẹn cửa nhận / bãi xe</option>
            </select>
          </label>
        </div>
        {filteredTasks.length === 0 ? (
          <p className="ui-muted-text">Không có tác vụ kho đang mở theo bộ lọc hiện tại.</p>
        ) : (
          <div style={{ maxHeight: 240, overflowY: 'auto' }}>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Hàng đợi tác vụ kho đang mở">
                <thead>
                  <tr>
                    <th>Loại</th>
                    <th>Mã phiếu/nhiệm vụ</th>
                    <th>Kho</th>
                    <th>Trạng thái</th>
                    <th>Ưu tiên</th>
                    <th style={{ whiteSpace: 'nowrap' }}>Xử lý</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredTasks.slice(0, 12).map((task, idx) => (
                    <tr key={`${task.taskType}-${task.code}-${idx}`}>
                      <td><UiBadge>{taskTypeLabel(task.taskType)}</UiBadge></td>
                      <td style={{ whiteSpace: 'nowrap' }}><strong>{task.code}</strong></td>
                      <td>{task.warehouseName}</td>
                      <td>{taskStatusLabel(task.status)}</td>
                      <td>
                        <UiBadge tone={task.priority === 'HIGH' ? 'danger' : 'neutral'}>
                          {priorityLabel(task.priority)}
                        </UiBadge>
                      </td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <Link
                          to={task.route}
                          style={{ display: 'inline-flex', alignItems: 'center', minHeight: 28, padding: '2px 8px', whiteSpace: 'nowrap' }}
                        >
                          Thực thi →
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}
      </UiCard>

      <UiCard title={`Trung tâm ngoại lệ vận hành • ${liveExceptions.length}`}>
        {liveExceptions.length === 0 ? (
          <p className="ui-muted-text">Không có ngoại lệ vận hành mở trong phạm vi kho được phép.</p>
        ) : (
          <div style={{ maxHeight: 240, overflowY: 'auto' }}>
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Trung tâm ngoại lệ vận hành" style={{ width: '100%' }}>
                <thead>
                  <tr>
                    <th>Phân loại</th>
                    <th>Mã</th>
                    <th>Kho</th>
                    <th>Mức độ</th>
                    <th>Chi tiết ngoại lệ</th>
                    <th style={{ whiteSpace: 'nowrap' }}>Xử lý</th>
                  </tr>
                </thead>
                <tbody>
                  {liveExceptions.slice(0, 12).map((ex, idx) => (
                    <tr key={`${ex.category}-${ex.code}-${idx}`}>
                      <td style={{ whiteSpace: 'nowrap' }}><UiBadge tone="warning">{exceptionCategoryLabel(ex.category)}</UiBadge></td>
                      <td style={{ whiteSpace: 'nowrap' }}><strong>{ex.code.replace(/QA-E2E-/g, 'SP-NOIBO-')}</strong></td>
                      <td>{ex.warehouseName}</td>
                      <td>
                        <UiBadge tone={ex.severity === 'HIGH' ? 'danger' : 'warning'}>
                          {severityLabel(ex.severity)}
                        </UiBadge>
                      </td>
                      <td style={{ minWidth: 260 }}>{ex.summary.replace(/QA-E2E-/g, 'SP-NOIBO-')}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <div className="ui-inline-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                          <Link
                            to={ex.route}
                            style={{ display: 'inline-flex', alignItems: 'center', minHeight: 28, padding: '2px 8px', whiteSpace: 'nowrap' }}
                          >
                            Xử lý →
                          </Link>
                          {acknowledgedCodes.includes(ex.code) ? (
                            <UiBadge tone="success">Đã tiếp nhận</UiBadge>
                          ) : (
                            <button type="button" disabled={acknowledgingCode === ex.code} onClick={() => void onAcknowledgeException(ex.code)} style={{ whiteSpace: 'nowrap' }}>
                              Tiếp nhận xử lý
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          </div>
        )}
      </UiCard>
    </div>
  );
};

export default DashboardExceptionAlerts;
