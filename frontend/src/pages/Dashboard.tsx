import { Link } from 'react-router-dom';
import { AlertTriangle, ArrowRight, Boxes, ClipboardCheck, PackageSearch, Warehouse } from 'lucide-react';
import { mockInventoryBalances, mockProducts, mockWarehouses, mockWorkCenters } from '../mocks/erpWmsMockData';
import { demoReconciliationRows } from '../mocks/inventoryReconciliationDemo';
import { productionNavigation } from '../config/productionNavigation';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';
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
    <section className="dashboard-page">
      <header className="dashboard-hero">
        <div>
          <span className="dashboard-kicker">ERP WMS Operations</span>
          <h1>Tổng quan vận hành</h1>
          <p>Điểm vào nhanh tới các work center đã có frontend, cùng trạng thái demo/runtime hiện tại.</p>
        </div>
        <Link className="dashboard-blueprint-link" to="/system-blueprint">Mở bản đồ hệ thống <ArrowRight size={15} /></Link>
      </header>

      {demoRuntime && (
        <div className="dashboard-runtime-note">
          <strong>Đang ở Vercel Blueprint Demo.</strong>
          <span>Dữ liệu bên dưới là mock read-only để kiểm tra UI/UX; backend staging và persistence chưa được kết nối.</span>
        </div>
      )}

      <div className="dashboard-metrics" aria-label="Tóm tắt dữ liệu">
        <article><Warehouse size={18} /><div><strong>{demoRuntime ? mockWarehouses.length : '—'}</strong><span>Kho trong demo dataset</span></div></article>
        <article><Boxes size={18} /><div><strong>{demoRuntime ? mockProducts.length : '—'}</strong><span>SKU trong demo dataset</span></div></article>
        <article><PackageSearch size={18} /><div><strong>{demoRuntime ? mockInventoryBalances.length : '—'}</strong><span>Dòng tồn kho mẫu</span></div></article>
        <article className={demoRuntime && mismatchCount > 0 ? 'warning' : ''}><AlertTriangle size={18} /><div><strong>{demoRuntime ? mismatchCount : '—'}</strong><span>Reconciliation mismatch mẫu</span></div></article>
      </div>

      <div className="dashboard-grid">
        <section className="dashboard-panel">
          <div className="dashboard-panel-heading">
            <div><span>Work centers</span><h2>Tiếp tục công việc</h2></div>
            <small>{demoRuntime ? operationalRecords.length + ' operational records mẫu' : 'Mở màn nghiệp vụ'}</small>
          </div>
          <div className="dashboard-actions">
            {quickLinks.map(({ to, title, detail, icon: Icon }) => (
              <Link key={to} to={to}>
                <Icon size={18} />
                <div><strong>{title}</strong><span>{detail}</span></div>
                <ArrowRight size={15} />
              </Link>
            ))}
          </div>
        </section>

        <section className="dashboard-panel">
          <div className="dashboard-panel-heading">
            <div><span>Frontend coverage</span><h2>Màn đã có route production UI</h2></div>
            <strong>{productionNavigation.length}</strong>
          </div>
          <div className="dashboard-screen-list">
            {productionNavigation.filter((item) => item.path !== '/').map((item) => (
              <Link key={item.path} to={item.path}>
                <span>{item.label}</span>
                <small>{item.section}</small>
              </Link>
            ))}
          </div>
        </section>
      </div>

      <section className="dashboard-environment">
        <div><span>Frontend</span><strong>{demoRuntime ? 'Vercel READY' : 'Runtime hiện tại'}</strong></div>
        <div><span>Data source</span><strong>{demoRuntime ? 'Demo API adapter' : 'Backend API'}</strong></div>
        <div><span>Write operations</span><strong>{demoRuntime ? 'Blocked (405)' : 'Theo quyền người dùng'}</strong></div>
        <div><span>Backend staging</span><strong>{demoRuntime ? 'Chưa kết nối' : 'Theo environment'}</strong></div>
      </section>
    </section>
  );
};

export default Dashboard;
