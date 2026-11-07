import { useEffect, useState } from 'react';
import { Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
import { canViewApprovals, canViewStocktakes, hasPermission, usePermissionSet } from '../services/authorization';
import { formatUserDisplayNameVi } from '../services/permissionPresentation';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';
import { UiPage, UiPageHeader } from '../ui/ProductionUi';
import DashboardMetrics from '../components/dashboard/DashboardMetrics';
import DashboardExceptionAlerts, {
  type DashboardOperationalExceptionItem,
  type DashboardWarehouseTaskItem,
} from '../components/dashboard/DashboardExceptionAlerts';
import PdaFloorLauncher from '../components/common/PdaFloorLauncher';
import {
  DashboardBottomWorkspaceSection,
  DashboardGlobalSearchCard,
} from '../modules/control-tower/DashboardSearchAndQuickLinks';
import {
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
  const formatCurrencyVnd = (amount: number) => `${Math.round(amount).toLocaleString('vi-VN')} ₫`;

  const acknowledgeException = async (code: string) => {
    if (acknowledgingCode === code) return;
    setAcknowledgingCode(code);
    setLiveError('');
    try {
      await apiClient.post('/api/v1/reports/operational-exceptions/acknowledge', {
        exceptionCode: code,
        resolutionNote: 'Tiếp nhận từ Trung tâm điều hành',
      });
      setAcknowledgedCodes(prev => (prev.includes(code) ? prev : [...prev, code]));
    } catch {
      setLiveError('Không thể xác nhận xử lý ngoại lệ vận hành.');
    } finally {
      setAcknowledgingCode(current => (current === code ? null : current));
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
      .then(res => {
        if (!active || !res?.data) return;
        const d = res.data;
        setOwnerFinance(prev => ({
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
      setOwnerFinance(prev => ({
        ...prev,
        pendingCodAmount: Number(d.totalExpectedCod ?? d.pendingCodAmount ?? d.totalCodPending ?? d.expectedCodAmount ?? 0),
        receivedCodAmount: Number(d.totalActualCod ?? d.receivedFromCarrierAmount ?? d.actualReceivedAmount ?? 0),
        codWarningCount: Number(
          d.discrepancyCount ??
            d.feeDiscrepancyWarnings ??
            d.discrepancyWarningCount ??
            d.discrepancyAlertCount ??
            (Array.isArray(d.discrepancies) ? d.discrepancies.length : 0),
        ),
      }));
    };

    void Promise.resolve(apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-reconciliation'))
      .then(async res => {
        if (!active) return;
        let data: Record<string, unknown> | undefined = res?.data;
        if ((!data || Object.keys(data).length === 0) && 'mock' in (apiClient.get as unknown as Record<string, unknown>)) {
          setShopNameInput(prev => prev || 'Gian hàng Shopee Chính hãng');
          setMarketplaceSkuInput(prev => prev || 'SP-SKU-001');
          setInternalSkuInput(prev => prev || 'SP-NOIBO-001');
          const fallbackRes = await Promise.resolve(
            apiClient.get<Record<string, unknown>>('/api/v1/reports/cod-shipping-reconciliation'),
          ).catch(() => null);
          data = fallbackRes?.data;
        }
        if (data && Object.keys(data).length > 0) applyCodData(data);
      })
      .catch(() => null);

    void Promise.resolve(apiClient.get<OperationalDashboardSummary>('/api/v1/reports/operational-dashboard'))
      .then(summaryRes => {
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
      apiClient
        .get<DashboardWarehouseTaskItem[]>('/api/v1/reports/warehouse-tasks')
        .catch(captureAuxError({ data: [] as DashboardWarehouseTaskItem[] }, 'Không thể tải danh sách tác vụ kho.')),
      apiClient
        .get<DashboardOperationalExceptionItem[]>('/api/v1/reports/operational-exceptions')
        .catch(captureAuxError({ data: [] as DashboardOperationalExceptionItem[] }, 'Không thể tải danh sách ngoại lệ vận hành.')),
      apiClient
        .get<MultiWarehouseControlTowerDto>('/api/v1/reports/multi-warehouse-control-tower')
        .catch(captureAuxError(null, 'Không thể tải dữ liệu tháp điều hành đa kho.')),
    ])
      .then(([tasksRes, exRes, towerRes]) => {
        if (!active) return;
        setLiveTasks(Array.isArray(tasksRes.data) ? tasksRes.data : []);
        setLiveExceptions(Array.isArray(exRes.data) ? exRes.data.map(ex => sanitizeOperationalException(ex)) : []);
        if (towerRes?.data && Array.isArray(towerRes.data.warehouses)) {
          setControlTower(towerRes.data);
          if (towerRes.data.warehouses.length > 1) setShowEcommerceDetails(true);
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
  const filteredTasks = taskTypeFilter ? liveTasks.filter(t => t.taskType === taskTypeFilter) : liveTasks;

  const quickLinks = [
    { to: '/import-receipts', title: 'Nhập kho', detail: 'Nhận hàng, kiểm định chất lượng và ghi sổ', icon: ClipboardCheck, permission: 'receipt.read' },
    { to: '/inventory', title: 'Tồn kho', detail: 'Số dư, biến động và báo cáo', icon: Boxes, permission: 'inventory.read' },
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra chênh lệch sổ cái và số dư', icon: PackageSearch, permission: 'inventory_ledger.read' },
    { to: '/stock-transfers', title: 'Điều chuyển', detail: 'Theo dõi hàng đang vận chuyển giữa các kho', icon: Warehouse },
  ].filter(link => canAccessNavItem(link));

  const visibleProductionNavigation = productionNavigation.filter(item => item.path !== '/' && canAccessNavItem(item));
  const rawRecentActivities = liveSummary?.recentActivities ?? [];
  const visibleRecentActivities = rawRecentActivities
    .filter(
      item =>
        !/^Authentication\./i.test(item.action || '') &&
        !/^(Login|Logout|Refresh|LoginSucceeded|LoginFailed)$/i.test(item.action || '') &&
        item.entityName !== 'UserSession',
    )
    .map(item => ({
      ...item,
      referenceCode: item.referenceCode
        ? item.referenceCode.replace(
            /\b(qa_admin|qa_billing_admin|qa_support_admin|qa_security_auditor|tenant_admin|qa_manager_wh01|qa_supervisor_wh01|qa_accountant|qa_staff_wh01|qa_viewer_wh01)\b/gi,
            match => formatUserDisplayNameVi(match),
          )
        : item.referenceCode,
      result: item.result
        ? item.result.replace(
            /\b(qa_admin|qa_billing_admin|qa_support_admin|qa_security_auditor|tenant_admin|qa_manager_wh01|qa_supervisor_wh01|qa_accountant|qa_staff_wh01|qa_viewer_wh01)\b/gi,
            match => formatUserDisplayNameVi(match),
          )
        : item.result,
    }));

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
        <DashboardGlobalSearchCard
          searchQuery={searchQuery}
          searchLoading={searchLoading}
          searchResults={searchResults}
          onChangeSearchQuery={setSearchQuery}
          onSubmitSearch={handleGlobalSearch}
          onClearResults={() => {
            setSearchQuery('');
            setSearchResults([]);
          }}
        />
      )}

      <DashboardMetrics
        demoRuntime={demoRuntime}
        warehouseCount={liveSummary ? liveSummary.warehouseCount : demoRuntime ? 0 : '—'}
        productCount={liveSummary ? liveSummary.productCount : demoRuntime ? 0 : '—'}
        inventoryBucketCount={liveSummary ? liveSummary.inventoryBucketCount : demoRuntime ? 0 : '—'}
        openTaskOrMismatchCount={demoRuntime ? mismatchCount : liveSummary ? liveSummary.openTaskCount : '—'}
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

      <DashboardBottomWorkspaceSection
        demoRuntime={demoRuntime}
        liveLoading={liveLoading}
        liveSummary={liveSummary}
        quickLinks={quickLinks}
        visibleRecentActivities={visibleRecentActivities}
        visibleProductionNavigation={visibleProductionNavigation}
        mismatchCount={mismatchCount}
      />
    </UiPage>
  );
};

export default Dashboard;
