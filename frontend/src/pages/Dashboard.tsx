import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData';
import { demoReconciliationRows } from '../mocks/inventoryReconciliationDemo';
import { productionNavigation } from '../config/productionNavigation';
import apiClient from '../services/apiClient';
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

const Dashboard = () => {
  const demoRuntime = isBlueprintDemoRuntime();
  const [liveSummary, setLiveSummary] = useState<OperationalDashboardSummary | null>(null);
  const [liveLoading, setLiveLoading] = useState(!demoRuntime);
  const [liveError, setLiveError] = useState('');

  useEffect(() => {
    if (demoRuntime) return;
    let active = true;
    setLiveLoading(true);
    setLiveError('');
    apiClient.get<OperationalDashboardSummary>('/api/Reports/operational-dashboard')
      .then((res) => {
        if (active && res.data && typeof res.data.warehouseCount === 'number') {
          setLiveSummary(res.data);
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

  const operationalRecords = ['inbound', 'outbound', 'inventory-control', 'transfer-replenishment']
    .flatMap((key) => mockWorkCenters[key]?.records ?? []);
  const mismatchCount = demoRuntime
    ? demoReconciliationRows.filter((row) => row.status !== 'Match').length
    : (liveSummary?.invalidStockCount ?? 0);

  const quickLinks = [
    { to: '/import-receipts', title: 'Inbound', detail: 'Nhận hàng, QC và posting', icon: ClipboardCheck },
    { to: '/inventory', title: 'Tồn kho', detail: 'Số dư, biến động và báo cáo', icon: Boxes },
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra ledger / balance mismatch', icon: PackageSearch },
    { to: '/stock-transfers', title: 'Điều chuyển', detail: 'Theo dõi hàng đang vận chuyển giữa các kho', icon: Warehouse },
  ];

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Tổng quan vận hành"
        description="Điểm vào nhanh tới các work center đã có frontend và trạng thái môi trường đang kiểm thử."
        actions={<Link className="dashboard-blueprint-link" to="/system-blueprint">Mở bản đồ hệ thống <ArrowRight size={16} aria-hidden="true" /></Link>}
      />

      {liveError && <p role="alert">{liveError}</p>}

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
          <div className="dashboard-screen-count">{productionNavigation.length}</div>
          <div className="dashboard-screen-list">
            {productionNavigation.filter((item) => item.path !== '/').map((item) => (
              <Link key={item.path} to={item.path}>
                <span>{item.label}</span>
                <small>{item.section}</small>
              </Link>
            ))}
          </div>
        </UiCard>
      </div>

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
