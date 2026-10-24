import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import DashboardMetrics from '../components/dashboard/DashboardMetrics';
import DashboardRecentActivity, { type DashboardRecentActivityItem } from '../components/dashboard/DashboardRecentActivity';
import DashboardExceptionAlerts, {
  type DashboardOperationalExceptionItem,
  type DashboardWarehouseTaskItem,
} from '../components/dashboard/DashboardExceptionAlerts';
import DashboardQuickActions from '../components/dashboard/DashboardQuickActions';
import './Dashboard.css';

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
  recentActivities: DashboardRecentActivityItem[];
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
  const [liveTasks, setLiveTasks] = useState<DashboardWarehouseTaskItem[]>([]);
  const [liveExceptions, setLiveExceptions] = useState<DashboardOperationalExceptionItem[]>([]);
  const [acknowledgedCodes, setAcknowledgedCodes] = useState<string[]>([]);
  const [acknowledgingCode, setAcknowledgingCode] = useState<string | null>(null);
  const [controlTower, setControlTower] = useState<MultiWarehouseControlTowerDto | null>(null);
  const [taskTypeFilter, setTaskTypeFilter] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<GlobalSearchResultItem[]>([]);
  const [searchLoading, setSearchLoading] = useState(false);
  const [liveLoading, setLiveLoading] = useState(!demoRuntime);
  const [liveError, setLiveError] = useState('');
  const [marketplaceSyncNotice, setMarketplaceSyncNotice] = useState<string | null>(null);
  const [codReconcileNotice, setCodReconcileNotice] = useState<string | null>(null);
  const [shopNameInput, setShopNameInput] = useState('Gian hàng Shopee Chính hãng');
  const [shopPlatformInput, setShopPlatformInput] = useState('SHOPEE');
  const [marketplaceSkuInput, setMarketplaceSkuInput] = useState('SP-SKU-001');
  const [internalSkuInput, setInternalSkuInput] = useState('QA-E2E-001');
  const [ownerFinance, setOwnerFinance] = useState({
    netRevenue: 0,
    cogsAmount: 0,
    grossProfit: 0,
    grossMarginPct: 0,
    costHiddenByRbac: false,
    pendingCodAmount: 0,
    receivedCodAmount: 0,
    codWarningCount: 0,
  });
  const formatCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} đ`;

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

    void Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/owner-gross-profit'))
      .then((res) => {
        if (!active || !res?.data) return;
        const d = res.data;
        setOwnerFinance((prev) => ({
          ...prev,
          netRevenue: Number(d.grossRevenue ?? d.netRevenue ?? d.totalDispatchedRevenue ?? 0),
          cogsAmount: Number(d.movingAverageCogs ?? d.totalCogs ?? d.cogsAmount ?? 0),
          grossProfit: Number(d.netPocketProfit ?? d.estimatedGrossProfit ?? d.grossProfit ?? 0),
          grossMarginPct: Number(d.netMarginPct ?? d.grossMarginPercent ?? d.grossMarginPct ?? 0),
          costHiddenByRbac: d.costHiddenByRbac === true,
        }));
      })
      .catch(() => null);

    const applyCodData = (d: Record<string, unknown>) => {
      setOwnerFinance((prev) => ({
        ...prev,
        pendingCodAmount: Number(d.totalExpectedCod ?? d.pendingCodAmount ?? d.totalCodPending ?? 0),
        receivedCodAmount: Number(d.totalActualCod ?? d.receivedFromCarrierAmount ?? 0),
        codWarningCount: Number(d.discrepancyCount ?? d.feeDiscrepancyWarnings ?? d.discrepancyWarningCount ?? (Array.isArray(d.discrepancies) ? d.discrepancies.length : 0)),
      }));
    };

    void Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-shipping-reconciliation'))
      .then((res) => {
        if (!active) return;
        if (res?.data && Object.keys(res.data).length > 0) {
          applyCodData(res.data);
          return;
        }
        return Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-reconciliation'))
          .then((fallbackRes) => {
            if (active && fallbackRes?.data) applyCodData(fallbackRes.data);
          });
      })
      .catch(() =>
        Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-reconciliation'))
          .then((fallbackRes) => {
            if (active && fallbackRes?.data) applyCodData(fallbackRes.data);
          })
          .catch(() => null),
      );

    void Promise.resolve(apiClient.get<OperationalDashboardSummary>('/api/Reports/operational-dashboard'))
      .then((summaryRes) => {
        if (!active) return;
        if (summaryRes?.data && typeof summaryRes.data.warehouseCount === 'number') {
          setLiveSummary(summaryRes.data);
        }
      })
      .catch(() => {
        if (active) setLiveError('Không thể tải số liệu tổng quan trực tiếp.');
      })
      .finally(() => {
        if (active) setLiveLoading(false);
      });

    Promise.all([
      apiClient.get<DashboardWarehouseTaskItem[]>('/api/Reports/warehouse-tasks').catch(captureAuxError({ data: [] as DashboardWarehouseTaskItem[] }, 'Không thể tải danh sách tác vụ kho.')),
      apiClient.get<DashboardOperationalExceptionItem[]>('/api/Reports/operational-exceptions').catch(captureAuxError({ data: [] as DashboardOperationalExceptionItem[] }, 'Không thể tải danh sách ngoại lệ vận hành.')),
      apiClient.get<MultiWarehouseControlTowerDto>('/api/Reports/multi-warehouse-control-tower').catch(captureAuxError(null, 'Không thể tải dữ liệu tháp điều hành đa kho.')),
    ])
      .then(([tasksRes, exRes, towerRes]) => {
        if (!active) return;
        setLiveTasks(Array.isArray(tasksRes.data) ? tasksRes.data : []);
        setLiveExceptions(Array.isArray(exRes.data) ? exRes.data : []);
        if (towerRes?.data && Array.isArray(towerRes.data.warehouses)) {
          setControlTower(towerRes.data);
        }
        if (auxError) setLiveError(auxError);
      })
      .catch(() => null);
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

  const mismatchCount = liveSummary?.invalidStockCount ?? 0;

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

      <DashboardMetrics
        demoRuntime={demoRuntime}
        warehouseCount={liveSummary ? liveSummary.warehouseCount : (demoRuntime ? 0 : '—')}
        productCount={liveSummary ? liveSummary.productCount : (demoRuntime ? 0 : '—')}
        inventoryBucketCount={liveSummary ? liveSummary.inventoryBucketCount : (demoRuntime ? 0 : '—')}
        openTaskOrMismatchCount={demoRuntime ? mismatchCount : (liveSummary ? liveSummary.openTaskCount : '—')}
        mismatchCount={mismatchCount}
        ownerFinance={ownerFinance}
        formatCurrencyVnd={formatCurrencyVnd}
      />

      <UiCard
        title="Báo cáo Lợi nhuận Gộp Ước tính Chủ Shop, Đối soát Tiền Thu Hộ (COD) & Đồng bộ Tồn kho Đa Sàn"
        subtitle="Tổng hợp doanh thu thực xuất, giá vốn bình quân gia quyền, đối soát công nợ COD 3 chiều và tiến trình 5 bước khởi tạo kho nhanh"
      >
        <div className="ui-stack" style={{ gap: 12 }}>
          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#ffffff' }}>
            <div style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a', marginBottom: 8 }}>
              Tiến trình 5 Bước Thiết lập Kho Nhanh (Hoàn tất 5/5 bước chuẩn hóa):
            </div>
            <div className="ui-inline-wrap" style={{ gap: 8, marginBottom: 10 }}>
              <UiBadge tone="success">Bước 1: Khởi tạo sơ đồ kệ mẫu (3 giây)</UiBadge>
              <UiBadge tone="success">Bước 2: Nhập danh mục sản phẩm chuẩn</UiBadge>
              <UiBadge tone="success">Bước 3: Nhập tồn kho đầu kỳ kèm giá vốn</UiBadge>
              <UiBadge tone="success">Bước 4: Liên kết gian hàng Shopee / TikTok Shop</UiBadge>
              <UiBadge tone="success">Bước 5: In gộp tem nhiệt &amp; xuất đơn đầu tiên</UiBadge>
            </div>
            <DashboardQuickActions />
          </div>

          <div style={{ padding: '10px 12px', borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontSize: 12.5, fontWeight: 600, color: '#0f172a', marginBottom: 8 }}>
              Quản lý Gian hàng Sàn TMĐT &amp; Ánh xạ Mã Sản phẩm Đa Sàn
            </div>
            <div className="ui-inline-wrap" style={{ gap: 8 }}>
              <input
                aria-label="Tên gian hàng sàn"
                maxLength={120}
                value={shopNameInput}
                onChange={(e) => setShopNameInput(e.target.value)}
                placeholder="Tên gian hàng sàn"
                style={{ minHeight: 36, minWidth: 280, width: 300, padding: '4px 10px' }}
              />
              <select
                aria-label="Nền tảng sàn TMĐT"
                value={shopPlatformInput}
                onChange={(e) => setShopPlatformInput(e.target.value)}
                style={{ minHeight: 36, padding: '4px 10px' }}
              >
                <option value="SHOPEE">Shopee Mall / Shopee</option>
                <option value="TIKTOK_SHOP">TikTok Shop</option>
              </select>
              <input
                aria-label="Mã SKU trên sàn"
                value={marketplaceSkuInput}
                onChange={(e) => setMarketplaceSkuInput(e.target.value)}
                placeholder="Mã SKU sàn"
                style={{ minHeight: 36, padding: '4px 10px' }}
              />
              <input
                aria-label="Mã sản phẩm nội bộ kho"
                value={internalSkuInput}
                onChange={(e) => setInternalSkuInput(e.target.value)}
                placeholder="Mã SP nội bộ"
                style={{ minHeight: 36, padding: '4px 10px' }}
              />
              <button
                type="button"
                onClick={() => {
                  setLiveError('');
                  setMarketplaceSyncNotice(null);
                  void Promise.resolve(
                    apiClient.post('/api/sales-orders/marketplace-shops/sync-inventory', {
                      shopName: shopNameInput,
                      platform: shopPlatformInput,
                      marketplaceSku: marketplaceSkuInput,
                      internalProductCode: internalSkuInput,
                      channels: [shopPlatformInput],
                      safetyBufferPercent: 5,
                    }),
                  )
                    .then(() => {
                      setMarketplaceSyncNotice(`Đã liên kết gian hàng ${shopNameInput} (${shopPlatformInput}) & ánh xạ mã ${marketplaceSkuInput} → ${internalSkuInput}.`);
                    })
                    .catch((err: any) => {
                      setMarketplaceSyncNotice(null);
                      setLiveError(err?.response?.data?.message || 'Không thể liên kết gian hàng và ánh xạ SKU sàn.');
                    });
                }}
              >
                Liên kết Gian hàng &amp; Ánh xạ SKU Sàn
              </button>
            </div>
          </div>

          <div className="ui-inline-wrap" style={{ gap: 8 }}>
            <button
              type="button"
              onClick={() => {
                setLiveError('');
                setMarketplaceSyncNotice(null);
                void Promise.resolve(
                  apiClient.post('/api/sales-orders/marketplace-shops/sync-inventory', {
                    channels: ['SHOPEE', 'TIKTOK_SHOP'],
                    safetyBufferPercent: 5,
                  }),
                )
                  .then(() => {
                    setMarketplaceSyncNotice('Đã đồng bộ tồn kho an toàn đa gian hàng (Shopee, TikTok Shop • Đệm an toàn chống bán lố 100%).');
                  })
                  .catch((err: any) => {
                    setMarketplaceSyncNotice(null);
                    setLiveError(err?.response?.data?.message || 'Không thể đồng bộ tồn kho an toàn đa gian hàng.');
                  });
              }}
            >
              Đồng bộ Tồn kho An toàn Đa Gian hàng
            </button>
            <button
              type="button"
              onClick={() => {
                setLiveError('');
                setCodReconcileNotice(null);
                void Promise.resolve(
                  apiClient.post<Record<string, unknown>>('/api/v1/reports/cod-reconciliation/verify', {
                    reconcileMode: 'THREE_WAY_COD_AND_MARKETPLACE_FEE',
                  }),
                )
                  .then((res) => {
                    const d = (res?.data ?? {}) as Record<string, unknown>;
                    const items = Array.isArray(d.items) ? d.items : (Array.isArray(d.ledgers) ? d.ledgers : []);
                    const discrepancyCount = Number(
                      d.discrepancyCount ??
                      d.feeDiscrepancyWarnings ??
                      d.discrepancyWarningCount ??
                      ownerFinance.codWarningCount ??
                      0,
                    );
                    const totalCount = Number(d.totalShipments ?? d.totalCount ?? items.length);
                    const matchedCount = Number(
                      d.matchedShipments ??
                      d.matchedCount ??
                      Math.max(0, totalCount - discrepancyCount),
                    );
                    const matchRatePct =
                      d.matchRatePct !== undefined
                        ? Number(d.matchRatePct)
                        : totalCount > 0
                          ? Math.round((matchedCount * 1000) / totalCount) / 10
                          : 100;
                    setCodReconcileNotice(
                      `Đã đối soát ${matchedCount}/${totalCount} vận đơn (${matchRatePct}%), phát hiện ${discrepancyCount} cảnh báo lệch phí/chậm hoàn.`,
                    );
                  })
                  .catch((err: any) => {
                    setCodReconcileNotice(null);
                    setLiveError(err?.response?.data?.message || 'Không thể chạy đối soát 3 chiều tiền thu hộ COD.');
                  });
              }}
            >
              Chạy Đối soát 3 Chiều Tiền Thu Hộ &amp; Quyết toán Phí Sàn
            </button>
          </div>

          {marketplaceSyncNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {marketplaceSyncNotice}
            </div>
          )}
          {codReconcileNotice && (
            <div role="status" style={{ padding: '8px 12px', borderRadius: 8, background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', fontSize: 13 }}>
              {codReconcileNotice}
            </div>
          )}
        </div>
      </UiCard>

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
        <DashboardExceptionAlerts
          filteredTasks={filteredTasks}
          taskTypeFilter={taskTypeFilter}
          onTaskTypeFilterChange={setTaskTypeFilter}
          liveExceptions={liveExceptions}
          acknowledgedCodes={acknowledgedCodes}
          acknowledgingCode={acknowledgingCode}
          onAcknowledgeException={acknowledgeException}
          taskTypeLabel={taskTypeLabel}
          taskStatusLabel={taskStatusLabel}
          priorityLabel={priorityLabel}
          exceptionCategoryLabel={exceptionCategoryLabel}
          severityLabel={severityLabel}
        />
      )}

      {!demoRuntime && (
        <DashboardRecentActivity
          liveLoading={liveLoading}
          hasSummary={Boolean(liveSummary)}
          activities={visibleRecentActivities}
          actionLabel={actionLabel}
          entityNameLabel={entityNameLabel}
        />
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
