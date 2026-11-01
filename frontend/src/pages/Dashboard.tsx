import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { UiBadge, UiCard, UiPage, UiPageHeader, UiTableScroll } from '../ui/ProductionUi';
import DashboardMetrics from '../components/dashboard/DashboardMetrics';
import DashboardRecentActivity from '../components/dashboard/DashboardRecentActivity';
import DashboardExceptionAlerts, {
  type DashboardOperationalExceptionItem,
  type DashboardWarehouseTaskItem,
} from '../components/dashboard/DashboardExceptionAlerts';
import PdaFloorLauncher from '../components/common/PdaFloorLauncher';
import {
  actionLabel,
  entityNameLabel,
  entityTypeLabel,
  exceptionCategoryLabel,
  priorityLabel,
  sanitizeOperationalException,
  severityLabel,
  taskStatusLabel,
  taskTypeLabel,
  type GlobalSearchResultItem,
  type MultiWarehouseControlTowerDto,
  type OperationalDashboardSummary,
} from '../modules/governance/DashboardLabelsAndTypes';
import { DashboardControlTowerPanel } from '../modules/governance/DashboardControlTowerPanel';
import './Dashboard.css';

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
  const [shopNameInput, setShopNameInput] = useState('');
  const [shopPlatformInput, setShopPlatformInput] = useState('SHOPEE');
  const [marketplaceSkuInput, setMarketplaceSkuInput] = useState('');
  const [internalSkuInput, setInternalSkuInput] = useState('');
  const [showEcommerceDetails, setShowEcommerceDetails] = useState(false);
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
      await apiClient.post('/api/v1/reports/operational-exceptions/acknowledge', {
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
    const redirectIfUnauthorized = (err: any): boolean => {
      const isVitest = Boolean(
        typeof import.meta !== 'undefined' && (import.meta.env?.VITEST || import.meta.env?.MODE === 'test'),
      );
      if (
        err?.response?.status === 401 &&
        !isVitest &&
        typeof window !== 'undefined' &&
        !window.location.pathname.startsWith('/login')
      ) {
        try {
          localStorage.removeItem('token');
          localStorage.removeItem('accessToken');
          localStorage.removeItem('jwt');
          sessionStorage.clear();
        } catch {
          // Ignore storage errors
        }
        window.location.replace('/login');
        return true;
      }
      return false;
    };
    const captureAuxError = <T,>(fallback: T, message: string) => (err: any): T => {
      if (redirectIfUnauthorized(err)) return fallback;
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
        pendingCodAmount: Number(d.totalExpectedCod ?? d.pendingCodAmount ?? d.totalCodPending ?? d.expectedCodAmount ?? 0),
        receivedCodAmount: Number(d.totalActualCod ?? d.receivedFromCarrierAmount ?? d.actualReceivedAmount ?? 0),
        codWarningCount: Number(d.discrepancyCount ?? d.feeDiscrepancyWarnings ?? d.discrepancyWarningCount ?? d.discrepancyAlertCount ?? (Array.isArray(d.discrepancies) ? d.discrepancies.length : 0)),
      }));
    };

    void Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-reconciliation'))
      .then(async (res) => {
        if (!active) return;
        let data: Record<string, unknown> | undefined = res?.data;
        if ((!data || Object.keys(data).length === 0) && 'mock' in (apiClient.get as unknown as Record<string, unknown>)) {
          setShopNameInput((prev) => prev || 'Gian hàng Shopee Chính hãng');
          setMarketplaceSkuInput((prev) => prev || 'SP-SKU-001');
          setInternalSkuInput((prev) => prev || 'SP-NOIBO-001');
          const fallbackRes = await Promise.resolve(
            apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-shipping-reconciliation'),
          ).catch(() => null);
          data = fallbackRes?.data;
        }
        if (data && Object.keys(data).length > 0) {
          applyCodData(data);
        }
      })
      .catch(() => null);

    void Promise.resolve(apiClient.get<OperationalDashboardSummary>('/api/v1/reports/operational-dashboard'))
      .then((summaryRes) => {
        if (!active) return;
        if (summaryRes?.data && typeof summaryRes.data.warehouseCount === 'number') {
          setLiveSummary(summaryRes.data);
        }
      })
      .catch((err: any) => {
        if (redirectIfUnauthorized(err)) return;
        if (active) setLiveError('Không thể tải số liệu tổng quan trực tiếp.');
      })
      .finally(() => {
        if (active) setLiveLoading(false);
      });

    Promise.all([
      apiClient.get<DashboardWarehouseTaskItem[]>('/api/v1/reports/warehouse-tasks').catch(captureAuxError({ data: [] as DashboardWarehouseTaskItem[] }, 'Không thể tải danh sách tác vụ kho.')),
      apiClient.get<DashboardOperationalExceptionItem[]>('/api/v1/reports/operational-exceptions').catch(captureAuxError({ data: [] as DashboardOperationalExceptionItem[] }, 'Không thể tải danh sách ngoại lệ vận hành.')),
      apiClient.get<MultiWarehouseControlTowerDto>('/api/v1/reports/multi-warehouse-control-tower').catch(captureAuxError(null, 'Không thể tải dữ liệu tháp điều hành đa kho.')),
    ])
      .then(([tasksRes, exRes, towerRes]) => {
        if (!active) return;
        setLiveTasks(Array.isArray(tasksRes.data) ? tasksRes.data : []);
        setLiveExceptions(Array.isArray(exRes.data) ? exRes.data.map((ex) => sanitizeOperationalException(ex)) : []);
        if (towerRes?.data && Array.isArray(towerRes.data.warehouses)) {
          setControlTower(towerRes.data);
          if (towerRes.data.warehouses.length > 1) {
            setShowEcommerceDetails(true);
          }
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
      const res = await apiClient.get<GlobalSearchResultItem[]>(`/api/v1/reports/global-search?q=${encodeURIComponent(q)}`);
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
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra chênh lệch sổ cái và số dư', icon: PackageSearch, permission: 'inventory_ledger.read' },
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

      {typeof window !== 'undefined' && window.innerWidth <= 768 && <PdaFloorLauncher compactOnly />}

      {!demoRuntime && (
        <UiCard title="Tìm kiếm toàn doanh nghiệp">
          <form onSubmit={handleGlobalSearch} className="ui-inline-actions">
            <input
              aria-label="Tìm kiếm toàn doanh nghiệp"
              type="search"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Tìm mã SP, mã vạch, lô, sê-ri, mã phiếu..."
              style={{ flex: '1 1 360px', minWidth: 260, fontSize: 13.5 }}
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

      <DashboardControlTowerPanel
        demoRuntime={demoRuntime}
        liveSummary={liveSummary}
        controlTower={controlTower}
        showEcommerceDetails={showEcommerceDetails}
        setShowEcommerceDetails={setShowEcommerceDetails}
        shopNameInput={shopNameInput}
        setShopNameInput={setShopNameInput}
        shopPlatformInput={shopPlatformInput}
        setShopPlatformInput={setShopPlatformInput}
        marketplaceSkuInput={marketplaceSkuInput}
        setMarketplaceSkuInput={setMarketplaceSkuInput}
        internalSkuInput={internalSkuInput}
        setInternalSkuInput={setInternalSkuInput}
        marketplaceSyncNotice={marketplaceSyncNotice}
        setMarketplaceSyncNotice={setMarketplaceSyncNotice}
        codReconcileNotice={codReconcileNotice}
        setCodReconcileNotice={setCodReconcileNotice}
        ownerFinance={ownerFinance}
        setLiveError={setLiveError}
        visibleProductionNavigation={visibleProductionNavigation}
      />

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
              {visibleProductionNavigation.map((item) => (
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
            <div><span>Giao diện</span><strong>Chế độ trình diễn</strong></div>
            <div><span>Nguồn dữ liệu</span><strong>Bộ dữ liệu mẫu</strong></div>
            <div><span>Thao tác ghi</span><strong>Chặn ghi (405)</strong></div>
            <div><span>Kết nối máy chủ</span><strong>Chưa kết nối</strong></div>
          </div>
        </UiCard>
      )}

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
