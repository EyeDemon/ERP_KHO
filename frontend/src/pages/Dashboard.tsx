import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData';
import { demoReconciliationRows } from '../mocks/inventoryReconciliationDemo';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
import { UiBadge, UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import './Dashboard.css';

type RecentActivity = {
  id: number;
  action: string;
  entityName: string;
  entityId: number;
  warehouseId?: number | null;
  result?: string | null;
  timestamp: string;
};

type OperationalDashboardSummary = {
  warehouseCount: number;
  productCount: number;
  inventoryBucketCount: number;
  invalidStockCount: number;
  openTaskCount: number;
  openInboundCount: number;
  openPutawayCount: number;
  openOutboundCount: number;
  openTransferCount: number;
  recentActivities: RecentActivity[];
};

type WarehouseTaskItem = {
  taskType: string;
  code: string;
  warehouseId: number;
  warehouseName: string;
  status: string;
  priority: string;
  createdAt: string;
  route: string;
};

type OperationalExceptionItem = {
  category: string;
  code: string;
  warehouseName: string;
  severity: string;
  summary: string;
  route: string;
};

type GlobalSearchResultItem = {
  entityType: string;
  code: string;
  title: string;
  subtitle: string;
  route: string;
};

type ControlTowerWarehouseItem = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  isActive: boolean;
  onHandQuantity: number;
  reservedQuantity: number;
  openPutawayCount: number;
  openPickingCount: number;
  activeLocksCount: number;
  healthStatus: string;
};

type TaskEngineQueueItem = {
  taskType: string;
  openCount: number;
  route: string;
};

type MultiWarehouseControlTowerDto = {
  totalWarehouses: number;
  healthyWarehousesCount: number;
  attentionWarehousesCount: number;
  warehouses: ControlTowerWarehouseItem[];
  taskEngineQueues: TaskEngineQueueItem[];
};

const Dashboard = () => {
  const demoRuntime = isBlueprintDemoRuntime();
  const [liveSummary, setLiveSummary] = useState<OperationalDashboardSummary | null>(null);
  const [liveTasks, setLiveTasks] = useState<WarehouseTaskItem[]>([]);
  const [liveExceptions, setLiveExceptions] = useState<OperationalExceptionItem[]>([]);
  const [acknowledgedCodes, setAcknowledgedCodes] = useState<string[]>([]);
  const [controlTower, setControlTower] = useState<MultiWarehouseControlTowerDto | null>(null);
  const [taskTypeFilter, setTaskTypeFilter] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<GlobalSearchResultItem[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [liveLoading, setLiveLoading] = useState(!demoRuntime);
  const [liveError, setLiveError] = useState('');

  const acknowledgeException = async (code: string) => {
    setLiveError('');
    try {
      await apiClient.post('/api/Reports/operational-exceptions/acknowledge', {
        exceptionCode: code,
        resolutionNote: 'Tiếp nhận từ Trung tâm điều hành',
      });
      setAcknowledgedCodes((prev) => (prev.includes(code) ? prev : [...prev, code]));
    } catch {
      setLiveError('Không thể xác nhận xử lý ngoại lệ vận hành.');
    }
  };

  useEffect(() => {
    if (demoRuntime) return;
    let active = true;
    setLiveLoading(true);
    setLiveError('');
    Promise.all([
      apiClient.get<OperationalDashboardSummary>('/api/Reports/operational-dashboard'),
      apiClient.get<WarehouseTaskItem[]>('/api/Reports/warehouse-tasks').catch(() => ({ data: [] as WarehouseTaskItem[] })),
      apiClient.get<OperationalExceptionItem[]>('/api/Reports/operational-exceptions').catch(() => ({ data: [] as OperationalExceptionItem[] })),
      apiClient.get<MultiWarehouseControlTowerDto>('/api/Reports/multi-warehouse-control-tower').catch(() => null),
    ])
      .then(([summaryRes, tasksRes, exRes, towerRes]) => {
        if (!active) return;
        if (summaryRes.data && typeof summaryRes.data.warehouseCount === 'number') {
          setLiveSummary(summaryRes.data);
        }
        setLiveTasks(Array.isArray(tasksRes.data) ? tasksRes.data : []);
        setLiveExceptions(Array.isArray(exRes.data) ? exRes.data : []);
        if (towerRes?.data && Array.isArray(towerRes.data.warehouses)) {
          setControlTower(towerRes.data);
        }
      })
      .catch(() => {
        if (active) setLiveError('Không thể tải số liệu tổng quan trực tiếp.');
      })
      .finally(() => {
        if (active) setLiveLoading(false);
      });
    return () => {
      active = false;
    };
  }, [demoRuntime]);

  const handleGlobalSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    const q = searchQuery.trim();
    if (q.length < 2) {
      setSearchResults([]);
      return;
    }
    setSearchLoading(true);
    setLiveError('');
    try {
      const res = await apiClient.get<GlobalSearchResultItem[]>(`/api/Reports/global-search?q=${encodeURIComponent(q)}`);
      setSearchResults(Array.isArray(res.data) ? res.data : []);
    } catch {
      setSearchResults([]);
      setLiveError('Không thể thực hiện tìm kiếm toàn doanh nghiệp.');
    } finally {
      setSearchLoading(false);
    }
  };

  usePermissionSet();
  const hasExplicitPermissions = typeof localStorage !== 'undefined' && localStorage.getItem('permissions') !== null;
  const canAccessNavItem = (item: { permission?: string; access?: 'always' | 'stocktake' | 'approvals' }) => {
    if (demoRuntime || !hasExplicitPermissions) return true;
    if (item.access === 'stocktake') return canViewStocktakes();
    if (item.access === 'approvals') return canViewApprovals();
    if (item.permission) return hasPermission(item.permission);
    return true;
  };

  const operationalRecords = ['inbound', 'outbound', 'inventory-control', 'transfer-replenishment']
    .flatMap((key) => mockWorkCenters[key]?.records ?? []);
  const mismatchCount = demoRuntime
    ? demoReconciliationRows.filter((row) => row.status !== 'Match').length
    : (liveSummary?.invalidStockCount ?? 0);

  const filteredTasks = taskTypeFilter
    ? liveTasks.filter((t) => t.taskType === taskTypeFilter)
    : liveTasks;

  const quickLinks = [
    { to: '/import-receipts', title: 'Inbound', detail: 'Nhận hàng, QC và posting', icon: ClipboardCheck, permission: 'receipt.read' },
    { to: '/inventory', title: 'Tồn kho', detail: 'Số dư, biến động và báo cáo', icon: Boxes, permission: 'inventory.read' },
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra ledger / balance mismatch', icon: PackageSearch },
    { to: '/stock-transfers', title: 'Điều chuyển', detail: 'Theo dõi hàng đang vận chuyển giữa các kho', icon: Warehouse },
  ].filter((link) => canAccessNavItem(link));

  const visibleProductionNavigation = productionNavigation.filter((item) => item.path !== '/' && canAccessNavItem(item));

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Tổng quan vận hành"
        description="Điểm vào nhanh tới các work center đã có frontend và trạng thái môi trường đang kiểm thử."
      />

      {liveError && <p role="alert">{liveError}</p>}

      {!demoRuntime && (
        <UiCard title="Tìm kiếm toàn doanh nghiệp (OV-07)">
          <form onSubmit={handleGlobalSearch} className="ui-inline-actions">
            <input
              aria-label="Tìm kiếm toàn doanh nghiệp"
              type="search"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm mã SKU, Barcode, Lô, Sê-ri, Vị trí, PO, ASN, Phiếu nhập/xuất, Lô giao..."
            />
            <button type="submit" disabled={searchLoading || searchQuery.trim().length < 2}>
              {searchLoading ? 'Đang tìm...' : 'Tìm kiếm'}
            </button>
            {searchResults.length > 0 && (
              <button type="button" onClick={() => { setSearchQuery(''); setSearchResults([]); }}>Xóa kết quả</button>
            )}
          </form>
          {searchResults.length > 0 && (
            <UiTableScroll>
              <table aria-label="Kết quả tìm kiếm toàn doanh nghiệp">
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
                      <td><UiBadge>{item.entityType}</UiBadge></td>
                      <td><strong>{item.code}</strong></td>
                      <td>{item.title}</td>
                      <td>{item.subtitle}</td>
                      <td><Link to={item.route}>Mở màn hình →</Link></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          )}
        </UiCard>
      )}

      <UiMetricGrid>
        <UiMetric
          value={demoRuntime ? mockWarehouses.length : (liveSummary ? liveSummary.warehouseCount : '—')}
          label={demoRuntime ? 'Kho trong demo dataset' : 'Kho đang hoạt động'}
        />
        <UiMetric
          value={demoRuntime ? mockProducts.length : (liveSummary ? liveSummary.productCount : '—')}
          label={demoRuntime ? 'SKU trong demo dataset' : 'SKU đang quản lý'}
        />
        <UiMetric
          value={demoRuntime ? mockInventoryBalances.length : (liveSummary ? liveSummary.inventoryBucketCount : '—')}
          label={demoRuntime ? 'Dòng tồn kho mẫu' : 'Nhóm tồn kho dương'}
        />
        <article className={'ui-metric' + (mismatchCount > 0 ? ' dashboard-metric-warning' : '')}>
          <strong>{demoRuntime ? mismatchCount : (liveSummary ? liveSummary.openTaskCount : '—')}</strong>
          <span>{demoRuntime ? 'Chênh lệch đối chiếu mẫu' : 'Phiếu / tác vụ đang mở'}</span>
        </article>
      </UiMetricGrid>

      <div className="dashboard-grid">
        <UiCard title="Tiếp tục công việc">
          <div className="dashboard-panel-meta">
            {demoRuntime
              ? operationalRecords.length + ' operational records mẫu'
              : liveSummary
                ? `Inbound mở: ${liveSummary.openInboundCount} • Putaway mở: ${liveSummary.openPutawayCount} • Outbound mở: ${liveSummary.openOutboundCount} • Điều chuyển mở: ${liveSummary.openTransferCount}`
                : 'Mở work center nghiệp vụ'}
          </div>
          <div className="dashboard-actions">
            {quickLinks.map(({ to, title, detail, icon: Icon }) => (
              <Link key={to} to={to}>
                <Icon size={18} aria-hidden="true" />
                <div><strong>{title}</strong><span>{detail}</span></div>
                <ArrowRight size={16} aria-hidden="true" />
              </Link>
            ))}
          </div>
        </UiCard>

        <UiCard title="Màn production UI đã có route">
          <div className="dashboard-screen-count">{visibleProductionNavigation.length + 1}</div>
          <div className="dashboard-screen-list">
            {visibleProductionNavigation.map((item) => (
              <Link key={item.path} to={item.path}>
                <span>{item.label}</span>
                <small>{item.section}</small>
              </Link>
            ))}
          </div>
        </UiCard>
      </div>

      {!demoRuntime && (
        <UiCard title="Trung tâm Điều hành Kho phân tán & Bộ máy Hàng đợi Nhiệm vụ (OV-05, OV-06)">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">{`Kho ổn định (HEALTHY): ${controlTower?.healthyWarehousesCount ?? 0}/${controlTower?.totalWarehouses ?? (liveSummary?.warehouseCount ?? 0)}`}</UiBadge>
            <UiBadge tone={(controlTower?.attentionWarehousesCount ?? 0) > 0 ? 'warning' : 'neutral'}>
              {`Kho cần chú ý (ATTENTION): ${controlTower?.attentionWarehousesCount ?? 0}`}
            </UiBadge>
            {(controlTower?.taskEngineQueues ?? [
              { taskType: 'PUTAWAY', openCount: liveSummary?.openPutawayCount ?? 0, route: '/putaway-tasks' },
              { taskType: 'PICK', openCount: 0, route: '/picking-tasks' },
              { taskType: 'PACK', openCount: 0, route: '/packing-sessions' },
              { taskType: 'STOCKTAKE', openCount: 0, route: '/stocktakes' },
              { taskType: 'TRANSFER', openCount: liveSummary?.openTransferCount ?? 0, route: '/stock-transfers' },
              { taskType: 'DOCK_YARD', openCount: 0, route: '/dock-yard' },
            ]).map((queue) => (
              <UiBadge key={queue.taskType} tone={queue.openCount > 0 ? 'warning' : 'neutral'}>
                {`Hàng đợi ${queue.taskType} (OV-06): ${queue.openCount}`}
              </UiBadge>
            ))}
          </div>
          {controlTower && controlTower.warehouses.length > 0 && (
            <UiTableScroll>
              <table aria-label="Bảng sức khỏe kho phân tán">
                <thead>
                  <tr>
                    <th>Kho</th>
                    <th>Tồn thực tế</th>
                    <th>Đã giữ</th>
                    <th>Putaway mở</th>
                    <th>Picking mở</th>
                    <th>Khóa tồn</th>
                    <th>Trạng thái sức khỏe</th>
                  </tr>
                </thead>
                <tbody>
                  {controlTower.warehouses.map((wh) => (
                    <tr key={wh.warehouseId}>
                      <td><strong>{wh.warehouseCode}</strong> — {wh.warehouseName}</td>
                      <td>{wh.onHandQuantity}</td>
                      <td>{wh.reservedQuantity}</td>
                      <td>{wh.openPutawayCount}</td>
                      <td>{wh.openPickingCount}</td>
                      <td>{wh.activeLocksCount}</td>
                      <td>
                        <UiBadge tone={wh.healthStatus === 'ATTENTION' ? 'warning' : 'success'}>
                          {wh.healthStatus}
                        </UiBadge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </UiTableScroll>
          )}
        </UiCard>
      )}

      {!demoRuntime && (
        <div className="dashboard-grid">
          <UiCard title={`Hàng đợi tác vụ kho đang mở (OV-02) • ${filteredTasks.length}`}>
            <div className="ui-inline-actions">
              <label>
                Loại tác vụ:{' '}
                <select aria-label="Lọc loại tác vụ kho" value={taskTypeFilter} onChange={(e) => setTaskTypeFilter(e.target.value)}>
                  <option value="">Tất cả tác vụ</option>
                  <option value="PUTAWAY">Cất hàng (Putaway)</option>
                  <option value="PICKING">Lấy hàng (Picking)</option>
                  <option value="PACKING">Đóng gói (Packing)</option>
                  <option value="INBOUND_RECEIPT">Phiếu nhập nháp</option>
                  <option value="STOCKTAKE">Kiểm kê nháp</option>
                  <option value="DOCK_YARD">Lịch hẹn Dock/Yard</option>
                </select>
              </label>
            </div>
            {filteredTasks.length === 0 ? (
              <p className="ui-muted-text">Không có tác vụ kho đang mở theo bộ lọc hiện tại.</p>
            ) : (
              <UiTableScroll>
                <table aria-label="Hàng đợi tác vụ kho đang mở">
                  <thead>
                    <tr>
                      <th>Loại</th>
                      <th>Mã phiếu/nhiệm vụ</th>
                      <th>Kho</th>
                      <th>Trạng thái</th>
                      <th>Ưu tiên</th>
                      <th>Xử lý</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredTasks.slice(0, 12).map((task, idx) => (
                      <tr key={`${task.taskType}-${task.code}-${idx}`}>
                        <td><UiBadge>{task.taskType}</UiBadge></td>
                        <td><strong>{task.code}</strong></td>
                        <td>{task.warehouseName}</td>
                        <td>{task.status}</td>
                        <td>
                          <UiBadge tone={task.priority === 'HIGH' ? 'danger' : 'neutral'}>
                            {task.priority}
                          </UiBadge>
                        </td>
                        <td><Link to={task.route}>Thực thi →</Link></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            )}
          </UiCard>

          <UiCard title={`Trung tâm ngoại lệ vận hành (OV-04) • ${liveExceptions.length}`}>
            {liveExceptions.length === 0 ? (
              <p className="ui-muted-text">Không có ngoại lệ vận hành mở trong phạm vi kho được phép.</p>
            ) : (
              <UiTableScroll>
                <table aria-label="Trung tâm ngoại lệ vận hành">
                  <thead>
                    <tr>
                      <th>Phân loại</th>
                      <th>Mã</th>
                      <th>Kho</th>
                      <th>Mức độ</th>
                      <th>Chi tiết ngoại lệ</th>
                      <th>Xử lý</th>
                    </tr>
                  </thead>
                  <tbody>
                    {liveExceptions.slice(0, 12).map((ex, idx) => (
                      <tr key={`${ex.category}-${ex.code}-${idx}`}>
                        <td><code>{ex.category}</code></td>
                        <td><strong>{ex.code}</strong></td>
                        <td>{ex.warehouseName}</td>
                        <td>
                          <UiBadge tone={ex.severity === 'HIGH' ? 'danger' : 'warning'}>
                            {ex.severity}
                          </UiBadge>
                        </td>
                        <td>{ex.summary}</td>
                        <td>
                          <div className="ui-inline-actions">
                            <Link to={ex.route}>Xử lý →</Link>
                            {acknowledgedCodes.includes(ex.code) ? (
                              <UiBadge tone="success">Đã tiếp nhận (Audit)</UiBadge>
                            ) : (
                              <button type="button" onClick={() => void acknowledgeException(ex.code)}>
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
            )}
          </UiCard>
        </div>
      )}

      {!demoRuntime && (
        <UiCard title="Hoạt động vận hành gần đây">
          {liveLoading ? (
            <p role="status">Đang tải nhật ký vận hành...</p>
          ) : !liveSummary || liveSummary.recentActivities.length === 0 ? (
            <p className="ui-muted-text">Chưa có hoạt động vận hành gần đây trong phạm vi kho được phép.</p>
          ) : (
            <UiTableScroll>
              <table aria-label="Hoạt động vận hành gần đây">
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
                  {liveSummary.recentActivities.map((item) => (
                    <tr key={item.id}>
                      <td>{new Date(item.timestamp).toLocaleString('vi-VN')}</td>
                      <td><code>{item.action}</code></td>
                      <td>{item.entityName} #{item.entityId}</td>
                      <td>{item.warehouseId ? `Kho #${item.warehouseId}` : 'Toàn hệ thống'}</td>
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
          )}
        </UiCard>
      )}

      <UiCard title="Trạng thái môi trường">
        <div className="dashboard-environment">
          <div><span>Frontend</span><strong>{demoRuntime ? 'Vercel READY' : 'Runtime hiện tại'}</strong></div>
          <div><span>Data source</span><strong>{demoRuntime ? 'Demo API adapter' : 'Backend API'}</strong></div>
          <div><span>Write operations</span><strong>{demoRuntime ? 'Blocked (405)' : 'Theo quyền người dùng'}</strong></div>
          <div><span>Backend staging</span><strong>{demoRuntime ? 'Chưa kết nối' : 'Đã kết nối Live API'}</strong></div>
        </div>
      </UiCard>

      {mismatchCount > 0 && (
        <div className="dashboard-attention" role="status">
          <AlertTriangle size={18} aria-hidden="true" />
          <span>{demoRuntime ? `Demo dataset hiện có ${mismatchCount} dòng reconciliation cần chú ý.` : `Phát hiện ${mismatchCount} dòng tồn kho bất thường cần đối chiếu.`} Mở work center Đối chiếu tồn kho để xem chi tiết.</span>
        </div>
      )}
    </UiPage>
  );
};

export default Dashboard;

