import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData';
import { demoReconciliationRows } from '../mocks/inventoryReconciliationDemo';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
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

const entityTypeLabel = (type: string) => ({
  PRODUCT: 'Sản phẩm',
  LOCATION: 'Vị trí',
  IMPORT_RECEIPT: 'Phiếu nhập',
  EXPORT_RECEIPT: 'Phiếu xuất',
  SHIPMENT: 'Lô giao hàng',
  PURCHASE_ORDER: 'Đơn mua hàng',
  ASN: 'Thông báo giao trước',
  STOCK_TRANSFER: 'Phiếu điều chuyển',
  STOCKTAKE: 'Phiếu kiểm kê',
  HANDLING_UNIT: 'Kiện hàng',
}[type] || type);

const taskTypeLabel = (type: string) => ({
  PUTAWAY: 'Cất hàng',
  PICK: 'Lấy hàng',
  PICKING: 'Lấy hàng',
  PACK: 'Đóng gói',
  PACKING: 'Đóng gói',
  INBOUND_RECEIPT: 'Phiếu nhập nháp',
  STOCKTAKE: 'Kiểm kê',
  TRANSFER: 'Điều chuyển',
  DOCK_YARD: 'Cửa nhận / Bãi xe',
}[type] || type);

const taskStatusLabel = (status: string) => ({
  OPEN: 'Đang mở',
  Open: 'Đang mở',
  PENDING: 'Chờ xử lý',
  Pending: 'Chờ xử lý',
  IN_PROGRESS: 'Đang thực hiện',
  InProgress: 'Đang thực hiện',
  DRAFT: 'Nháp',
  Draft: 'Bản nháp',
  ASSIGNED: 'Đã phân công',
  Assigned: 'Đã phân công',
  ShortPick: 'Thiếu hàng',
  SCHEDULED: 'Đã lên lịch',
  Scheduled: 'Đã lên lịch',
  Arrived: 'Đã đến cổng',
  CHECKED_IN: 'Đã vào bãi',
  CheckedIn: 'Đã vào bãi',
  AT_DOCK: 'Tại cửa nhận/xuất',
  DockAssigned: 'Đã gán cửa kho',
  InService: 'Đang bốc dỡ',
}[status] || status);

const priorityLabel = (priority: string) => ({
  HIGH: 'Cao',
  NORMAL: 'Bình thường',
  MEDIUM: 'Trung bình',
  LOW: 'Thấp',
  URGENT: 'Khẩn cấp',
}[priority] || priority);

const healthStatusLabel = (status: string) => ({
  HEALTHY: 'Ổn định',
  ATTENTION: 'Cần chú ý',
  WARNING: 'Cảnh báo',
  CRITICAL: 'Nghiêm trọng',
}[status] || status);

const exceptionCategoryLabel = (category: string) => ({
  NEGATIVE_OR_INVALID_STOCK: 'Tồn kho bất thường',
  STALLED_PUTAWAY: 'Cất hàng tồn đọng',
  STALLED_PICKING: 'Lấy hàng tồn đọng',
  ACTIVE_INVENTORY_LOCK: 'Khóa tồn kho',
  INVENTORY_HOLD: 'Khóa tồn kho',
  PUTAWAY_EXCEPTION: 'Ngoại lệ cất hàng',
  SHORT_PICK: 'Lấy thiếu hàng',
  RECEIVING_DISCREPANCY: 'Chênh lệch nhận hàng',
  SHIPMENT_EXCEPTION: 'Sự cố giao hàng',
  OVERDUE_TRANSFER: 'Điều chuyển quá hạn',
  UNRESOLVED_DISCREPANCY: 'Chênh lệch chưa xử lý',
  CAPACITY_WARNING: 'Cảnh báo sức chứa',
  EXPIRY_RISK: 'Rủi ro hết hạn',
}[category] || category);

const severityLabel = (severity: string) => ({
  HIGH: 'Cao',
  MEDIUM: 'Trung bình',
  LOW: 'Thấp',
  CRITICAL: 'Nghiêm trọng',
  WARNING: 'Cảnh báo',
}[severity] || severity);

const actionLabel = (action: string) => ({
  Create: 'Tạo mới',
  Update: 'Cập nhật',
  Delete: 'Xóa',
  Approve: 'Phê duyệt',
  Reject: 'Từ chối',
  Complete: 'Hoàn tất',
  Cancel: 'Hủy',
  Dispatch: 'Xuất giao',
  Receive: 'Tiếp nhận',
  Login: 'Đăng nhập',
  Logout: 'Đăng xuất',
  Post: 'Ghi sổ',
  Lock: 'Khóa',
  Unlock: 'Mở khóa',
  Acknowledge: 'Tiếp nhận xử lý',
  'Authentication.RefreshRotated': 'Làm mới phiên đăng nhập',
  'Authentication.LoginSucceeded': 'Đăng nhập thành công',
  'Authentication.LogoutSucceeded': 'Đăng xuất',
  'QA.IamAndOperationalSeedApplied': 'Khởi tạo dữ liệu vận hành chuẩn',
}[action] || action);

const entityNameLabel = (entityName: string) => ({
  ImportReceipt: 'Phiếu nhập',
  ExportReceipt: 'Phiếu xuất',
  PutawayTask: 'Tác vụ cất hàng',
  PickingTask: 'Tác vụ lấy hàng',
  PackingSession: 'Phiên đóng gói',
  Shipment: 'Chuyến giao hàng',
  StockTransfer: 'Phiếu điều chuyển',
  Stocktake: 'Phiếu kiểm kê',
  Product: 'Sản phẩm',
  Warehouse: 'Kho',
  WarehouseLocation: 'Vị trí kho',
  InventoryLock: 'Khóa tồn kho',
  StockReservation: 'Giữ hàng',
  StockAllocation: 'Phân bổ tồn kho',
  BusinessPartner: 'Đối tác',
  User: 'Người dùng',
  UserSession: 'Phiên đăng nhập',
  LocalQaOperationalSeed: 'Bộ dữ liệu chuẩn',
  WarehouseCalendarException: 'Ngoại lệ lịch kho',
  InventoryLocationMovement: 'Di chuyển vị trí kho',
}[entityName] || entityName);

const Dashboard = () => {
  const demoRuntime = isReadOnlyDemoRuntime();
  const [liveSummary, setLiveSummary] = useState<OperationalDashboardSummary | null>(null);
  const [liveTasks, setLiveTasks] = useState<WarehouseTaskItem[]>([]);
  const [liveExceptions, setLiveExceptions] = useState<OperationalExceptionItem[]>([]);
  const [acknowledgedCodes, setAcknowledgedCodes] = useState<string[]>([]);
  const [acknowledgingCode, setAcknowledgingCode] = useState<string | null>(null);
  const [controlTower, setControlTower] = useState<MultiWarehouseControlTowerDto | null>(null);
  const [taskTypeFilter, setTaskTypeFilter] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<GlobalSearchResultItem[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [liveLoading, setLiveLoading] = useState(!demoRuntime);
  const [liveError, setLiveError] = useState('');

  const acknowledgeException = async (code: string) => {
    if (acknowledgingCode === code) return;
    setAcknowledgingCode(code);
    setLiveError('');
    try {
      await apiClient.post('/api/Reports/operational-exceptions/acknowledge', {
        exceptionCode: code,
        resolutionNote: 'Tiếp nhận từ Trung tâm điều hành',
      });
      setAcknowledgedCodes((prev) => (prev.includes(code) ? prev : [...prev, code]));
    } catch {
      setLiveError('Không thể xác nhận xử lý ngoại lệ vận hành.');
    } finally {
      setAcknowledgingCode((current) => (current === code ? null : current));
    }
  };

  useEffect(() => {
    if (demoRuntime) return;
    let active = true;
    setLiveLoading(true);
    setLiveError('');
    let auxError = '';
    const captureAuxError = <T,>(fallback: T, message: string) => (err: any): T => {
      if (err?.response?.status && err.response.status >= 400 && !auxError) {
        auxError = err.response?.data?.message || message;
      }
      return fallback;
    };
    Promise.all([
      apiClient.get<OperationalDashboardSummary>('/api/Reports/operational-dashboard'),
      apiClient.get<WarehouseTaskItem[]>('/api/Reports/warehouse-tasks').catch(captureAuxError({ data: [] as WarehouseTaskItem[] }, 'Không thể tải danh sách tác vụ kho.')),
      apiClient.get<OperationalExceptionItem[]>('/api/Reports/operational-exceptions').catch(captureAuxError({ data: [] as OperationalExceptionItem[] }, 'Không thể tải danh sách ngoại lệ vận hành.')),
      apiClient.get<MultiWarehouseControlTowerDto>('/api/Reports/multi-warehouse-control-tower').catch(captureAuxError(null, 'Không thể tải dữ liệu tháp điều hành đa kho.')),
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
        if (auxError) setLiveError(auxError);
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
    { to: '/import-receipts', title: 'Nhập kho', detail: 'Nhận hàng, kiểm định chất lượng và ghi sổ', icon: ClipboardCheck, permission: 'receipt.read' },
    { to: '/inventory', title: 'Tồn kho', detail: 'Số dư, biến động và báo cáo', icon: Boxes, permission: 'inventory.read' },
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra chênh lệch sổ cái và số dư', icon: PackageSearch },
    { to: '/stock-transfers', title: 'Điều chuyển', detail: 'Theo dõi hàng đang vận chuyển giữa các kho', icon: Warehouse },
  ].filter((link) => canAccessNavItem(link));

  const visibleProductionNavigation = productionNavigation.filter((item) => item.path !== '/' && canAccessNavItem(item));
  const rawRecentActivities = liveSummary?.recentActivities ?? [];
  const operationalRecentActivities = rawRecentActivities.filter((item) => item.action !== 'Authentication.RefreshRotated' && item.action !== 'Refresh' && item.entityName !== 'UserSession');
  const visibleRecentActivities = operationalRecentActivities.length > 0 ? operationalRecentActivities : rawRecentActivities;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Tổng quan vận hành"
        description="Điểm vào nhanh tới các phân hệ nghiệp vụ và trạng thái môi trường vận hành."
      />

      {liveError && <p role="alert">{liveError}</p>}

      {!demoRuntime && (
        <UiCard title="Tìm kiếm toàn doanh nghiệp">
          <form onSubmit={handleGlobalSearch} className="ui-inline-actions">
            <input
              aria-label="Tìm kiếm toàn doanh nghiệp"
              type="search"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm mã sản phẩm, mã vạch, lô, sê-ri, vị trí, đơn mua, thông báo giao, phiếu nhập/xuất, lô giao..."
              style={{ flex: '1 1 360px', minWidth: 260 }}
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
                      <td><UiBadge>{entityTypeLabel(item.entityType)}</UiBadge></td>
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
          label={demoRuntime ? 'Kho trong bộ dữ liệu mẫu' : 'Kho đang hoạt động'}
        />
        <UiMetric
          value={demoRuntime ? mockProducts.length : (liveSummary ? liveSummary.productCount : '—')}
          label={demoRuntime ? 'Sản phẩm trong bộ dữ liệu mẫu' : 'Sản phẩm đang quản lý'}
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
              ? operationalRecords.length + ' bản ghi vận hành mẫu'
              : liveSummary
                ? `Phiếu nhập mở: ${liveSummary.openInboundCount} • Cất hàng mở: ${liveSummary.openPutawayCount} • Phiếu xuất mở: ${liveSummary.openOutboundCount} • Điều chuyển mở: ${liveSummary.openTransferCount}`
                : 'Mở phân hệ nghiệp vụ'}
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

        <UiCard title="Màn hình nghiệp vụ đã kích hoạt">
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
        <UiCard title="Trung tâm Điều hành Kho phân tán & Bộ máy Hàng đợi Nhiệm vụ">
          <div className="ui-inline-wrap">
            <UiBadge tone="success">{`Kho ổn định: ${controlTower?.healthyWarehousesCount ?? 0}/${controlTower?.totalWarehouses ?? (liveSummary?.warehouseCount ?? 0)}`}</UiBadge>
            <UiBadge tone={(controlTower?.attentionWarehousesCount ?? 0) > 0 ? 'warning' : 'neutral'}>
              {`Kho cần chú ý: ${controlTower?.attentionWarehousesCount ?? 0}`}
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
                {`Hàng đợi ${taskTypeLabel(queue.taskType)}: ${queue.openCount}`}
              </UiBadge>
            ))}
          </div>
          {controlTower && controlTower.warehouses.length > 0 && (
            <UiTableScroll>
              <table className="ui-table-compact" aria-label="Bảng sức khỏe kho phân tán">
                <thead>
                  <tr>
                    <th>Kho</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Tồn thực tế</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Đã giữ</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Cất hàng mở</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Lấy hàng mở</th>
                    <th style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>Khóa tồn</th>
                    <th>Trạng thái sức khỏe</th>
                  </tr>
                </thead>
                <tbody>
                  {controlTower.warehouses.map((wh) => (
                    <tr key={wh.warehouseId}>
                      <td><strong>{wh.warehouseCode}</strong> — {wh.warehouseName}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.onHandQuantity}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.reservedQuantity}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.openPutawayCount}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.openPickingCount}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{wh.activeLocksCount}</td>
                      <td>
                        <UiBadge tone={wh.healthStatus === 'ATTENTION' ? 'warning' : 'success'}>
                          {healthStatusLabel(wh.healthStatus)}
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
          <UiCard title={`Hàng đợi tác vụ kho đang mở • ${filteredTasks.length}`}>
            <div className="ui-inline-actions">
              <label>
                Loại tác vụ:{' '}
                <select aria-label="Lọc loại tác vụ kho" value={taskTypeFilter} onChange={(e) => setTaskTypeFilter(e.target.value)}>
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
                        <td style={{ whiteSpace: 'nowrap' }}><Link to={task.route}>Thực thi →</Link></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </UiTableScroll>
            )}
          </UiCard>

          <UiCard title={`Trung tâm ngoại lệ vận hành • ${liveExceptions.length}`}>
            {liveExceptions.length === 0 ? (
              <p className="ui-muted-text">Không có ngoại lệ vận hành mở trong phạm vi kho được phép.</p>
            ) : (
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
                        <td style={{ whiteSpace: 'nowrap' }}><strong>{ex.code}</strong></td>
                        <td>{ex.warehouseName}</td>
                        <td>
                          <UiBadge tone={ex.severity === 'HIGH' ? 'danger' : 'warning'}>
                            {severityLabel(ex.severity)}
                          </UiBadge>
                        </td>
                        <td style={{ minWidth: 260 }}>{ex.summary}</td>
                        <td style={{ whiteSpace: 'nowrap' }}>
                          <div className="ui-inline-actions" style={{ display: 'inline-flex', flexWrap: 'nowrap', gap: 6, alignItems: 'center', whiteSpace: 'nowrap' }}>
                            <Link to={ex.route} style={{ whiteSpace: 'nowrap' }}>Xử lý →</Link>
                            {acknowledgedCodes.includes(ex.code) ? (
                              <UiBadge tone="success">Đã tiếp nhận</UiBadge>
                            ) : (
                              <button type="button" disabled={acknowledgingCode === ex.code} onClick={() => void acknowledgeException(ex.code)} style={{ whiteSpace: 'nowrap' }}>
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
          ) : !liveSummary || visibleRecentActivities.length === 0 ? (
            <p className="ui-muted-text">Chưa có hoạt động vận hành gần đây trong phạm vi kho được phép.</p>
          ) : (
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
                  {visibleRecentActivities.map((item) => (
                    <tr key={item.id}>
                      <td>{new Date(item.timestamp).toLocaleString('vi-VN')}</td>
                      <td><UiBadge>{actionLabel(item.action)}</UiBadge></td>
                      <td>{entityNameLabel(item.entityName)}{item.entityId ? ` #${item.entityId}` : ''}</td>
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
          <div><span>Giao diện</span><strong>{demoRuntime ? 'Chế độ trình diễn' : 'Môi trường vận hành'}</strong></div>
          <div><span>Nguồn dữ liệu</span><strong>{demoRuntime ? 'Bộ dữ liệu mẫu' : 'Dịch vụ máy chủ'}</strong></div>
          <div><span>Thao tác ghi</span><strong>{demoRuntime ? 'Chặn ghi (405)' : 'Theo quyền người dùng'}</strong></div>
          <div><span>Kết nối máy chủ</span><strong>{demoRuntime ? 'Chưa kết nối' : 'Đã kết nối trực tiếp'}</strong></div>
        </div>
      </UiCard>

      {mismatchCount > 0 && (
        <div className="dashboard-attention" role="status">
          <AlertTriangle size={18} aria-hidden="true" />
          <span>{demoRuntime ? `Bộ dữ liệu mẫu hiện có ${mismatchCount} dòng đối chiếu cần chú ý.` : `Phát hiện ${mismatchCount} dòng tồn kho bất thường cần đối chiếu.`} Mở phân hệ Đối chiếu tồn kho để xem chi tiết.</span>
        </div>
      )}
    </UiPage>
  );
};

export default Dashboard;

