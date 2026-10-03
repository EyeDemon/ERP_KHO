import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData';
import { demoReconciliationRows } from '../mocks/inventoryReconciliationDemo';
import { productionNavigation } from '../config/productionNavigation';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
import { UiCard, UiMetric, UiMetricGrid, UiPage, UiPageHeader } from '../ui/ProductionUi';
import './Dashboard.css';

const Dashboard = () => {
  const demoRuntime = isBlueprintDemoRuntime();
  const operationalRecords = ['inbound', 'outbound', 'inventory-control', 'transfer-replenishment']
    .flatMap((key) => mockWorkCenters[key]?.records ?? []);
  const mismatchCount = demoReconciliationRows.filter((row) => row.status !== 'Match').length;

  const quickLinks = [
    { to: '/import-receipts', title: 'Inbound', detail: 'Nhận hàng, QC và posting', icon: ClipboardCheck },
    { to: '/inventory', title: 'Tồn kho', detail: 'Balance, movement và báo cáo', icon: Boxes },
    { to: '/inventory-reconciliation', title: 'Đối chiếu', detail: 'Kiểm tra ledger / balance mismatch', icon: PackageSearch },
    { to: '/stock-transfers', title: 'Điều chuyển', detail: 'Theo dõi hàng in-transit giữa kho', icon: Warehouse },
  ];

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Tổng quan vận hành"
        description="Điểm vào nhanh tới các work center đã có frontend và trạng thái môi trường đang kiểm thử."
        actions={<Link className="dashboard-blueprint-link" to="/system-blueprint">Mở bản đồ hệ thống <ArrowRight size={16} aria-hidden="true" /></Link>}
      />

      <UiMetricGrid>
        <UiMetric value={demoRuntime ? mockWarehouses.length : '—'} label="Kho trong demo dataset" />
        <UiMetric value={demoRuntime ? mockProducts.length : '—'} label="SKU trong demo dataset" />
        <UiMetric value={demoRuntime ? mockInventoryBalances.length : '—'} label="Dòng tồn kho mẫu" />
        <article className={'ui-metric' + (demoRuntime && mismatchCount > 0 ? ' dashboard-metric-warning' : '')}>
          <strong>{demoRuntime ? mismatchCount : '—'}</strong>
          <span>Reconciliation mismatch mẫu</span>
        </article>
      </UiMetricGrid>

      <div className="dashboard-grid">
        <UiCard title="Tiếp tục công việc">
          <div className="dashboard-panel-meta">{demoRuntime ? operationalRecords.length + ' operational records mẫu' : 'Mở work center nghiệp vụ'}</div>
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

      <UiCard title="Trạng thái môi trường">
        <div className="dashboard-environment">
          <div><span>Frontend</span><strong>{demoRuntime ? 'Vercel READY' : 'Runtime hiện tại'}</strong></div>
          <div><span>Data source</span><strong>{demoRuntime ? 'Demo API adapter' : 'Backend API'}</strong></div>
          <div><span>Write operations</span><strong>{demoRuntime ? 'Blocked (405)' : 'Theo quyền người dùng'}</strong></div>
          <div><span>Backend staging</span><strong>{demoRuntime ? 'Chưa kết nối' : 'Theo environment'}</strong></div>
        </div>
      </UiCard>

      {demoRuntime && mismatchCount > 0 && (
        <div className="dashboard-attention" role="status">
          <AlertTriangle size={18} aria-hidden="true" />
          <span>Demo dataset hiện có {mismatchCount} dòng reconciliation cần chú ý. Mở work center Đối chiếu tồn kho để xem chi tiết.</span>
        </div>
      )}
    </UiPage>
  );
};

export default Dashboard;
