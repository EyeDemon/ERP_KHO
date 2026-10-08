import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Activity, Boxes, CheckCircle2, CircleDashed, Filter, Layers3, MonitorSmartphone,
  Network, Search, ShieldCheck, Smartphone, Sparkles, Workflow
} from 'lucide-react';
import {
  blueprintDemoStatusLabels,
  blueprintProductionSnapshot,
  blueprintTotals,
  erpWmsBlueprint,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import { mockRecordCount, mockWarehouses, mockProducts, mockPartners } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import './SystemBlueprint.css';

const statusIcon = (status: BlueprintStatus) => {
  if (status === 'live') return <CheckCircle2 size={15} />;
  if (status === 'foundation') return <Activity size={15} />;
  if (status === 'optional') return <Sparkles size={15} />;
  return <CircleDashed size={15} />;
};

const SystemBlueprint = () => {
  const mockDemo = useMockDemo();
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState<BlueprintStatus | 'all'>('all');
  const [activeModule, setActiveModule] = useState<string>('all');

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('vi');
    return erpWmsBlueprint
      .filter((module) => activeModule === 'all' || module.key === activeModule)
      .map((module) => ({
        ...module,
        capabilities: module.capabilities.filter((capability) => {
          const statusMatch = status === 'all' || capability.status === status;
          const textMatch = !q || [
            capability.id,
            capability.name,
            capability.goal,
            capability.spec,
            module.name,
          ].join(' ').toLocaleLowerCase('vi').includes(q);
          return statusMatch && textMatch;
        }),
      }))
      .filter((module) => module.capabilities.length > 0);
  }, [activeModule, query, status]);

  return (
    <div className="blueprint-page">
      <section className="blueprint-hero">
        <div>
          <div className="eyebrow"><Layers3 size={16} /> ERP WMS • Bản thiết kế hệ thống hoàn chỉnh</div>
          <h1>Bản đồ chức năng ERP/WMS hoàn chỉnh</h1>
          <p>
            Bản thiết kế dùng Notion chuẩn ở chế độ chỉ đọc để đối chiếu nghiệp vụ. Trạng thái chức năng bên dưới
            phản ánh hệ thống thật đã merge, triển khai và QA; bản xem trước mô phỏng không được dùng để tự nâng mức trưởng thành.
          </p>
        </div>
        <div className="hero-badge">
          <ShieldCheck size={22} />
          <span>Ưu tiên sổ cái • Theo quyền truy cập • Điều khiển bằng máy trạng thái</span>
        </div>
      </section>

      <section className="blueprint-kpis">
        <article><strong>{blueprintTotals.modules}</strong><span>Nhóm hệ thống</span></article>
        <article><strong>{blueprintTotals.capabilities}</strong><span>Chức năng có bản xem trước mô phỏng</span></article>
        <article className="kpi-live"><strong>{blueprintTotals.live}</strong><span>Hệ thống thật hoàn thiện phạm vi hiện tại</span></article>
        <article className="kpi-foundation"><strong>{blueprintTotals.foundation}</strong><span>Hệ thống thật đã có một phần</span></article>
        <article><strong>{blueprintTotals.planned}</strong><span>Chưa triển khai hệ thống thật</span></article>
        <article><strong>{blueprintTotals.optional}</strong><span>Tùy chọn / nâng cao</span></article>
        <article className="kpi-mock"><strong>{mockRecordCount}</strong><span>Bản ghi mô phỏng</span></article>
      </section>

      <section className="mock-dataset-summary">
        <strong>Mốc triển khai đã xác minh:</strong>
        <span>Nhánh: {blueprintProductionSnapshot.branch}</span>
        <span>Commit mốc: {blueprintProductionSnapshot.commit.slice(0, 12)}</span>
        <span>Triển khai: {blueprintProductionSnapshot.deployment}</span>
        <span>Xác minh: {blueprintProductionSnapshot.verifiedAt}</span>
        <span>Notion: tài liệu chuẩn • chỉ đọc</span>
      </section>

      <section className="mock-dataset-summary">
        <strong>Bộ dữ liệu mô phỏng:</strong>
        <span>Vai trò: {mockDemo.selectedUser.name}</span>
        <span>Phạm vi: {mockDemo.allowedWarehouses.join(', ')}</span>
        <span>{mockWarehouses.length} kho</span>
        <span>{mockProducts.length} SKU</span>
        <span>{mockPartners.length} đối tác</span>
        <span>{mockRecordCount} bản ghi vận hành</span>
        <span>Lô / Sê-ri / Tồn kho / Điều chuyển / Kiểm kê / Phê duyệt / Tích hợp / Di động</span>
        <div className="blueprint-lab-links">
          <Link className="scenario-lab-link" to="/system-blueprint/search">Mở Tìm kiếm toàn hệ thống →</Link>
          <Link className="scenario-lab-link" to="/system-blueprint/coverage">Mở Độ phủ & mức sẵn sàng →</Link>
          <Link className="scenario-lab-link" to="/system-blueprint/mock-data">Mở Phòng dữ liệu mô phỏng →</Link>
          <Link className="scenario-lab-link" to="/system-blueprint/scenarios">Mở Phòng kịch bản chuẩn →</Link>
        </div>
      </section>

      <section className="blueprint-principles">
        <div><Workflow size={18} /><span>Lệnh → Phân quyền → Trạng thái → Quy tắc nghiệp vụ → Ghi sổ</span></div>
        <div><Boxes size={18} /><span>Sổ cái bất biến → Số dư dự phóng → Kiểm toán → Outbox</span></div>
        <div><Network size={18} /><span>ERP trên web + WMS di động + Tích hợp dùng chung ngữ nghĩa nghiệp vụ</span></div>
      </section>

      <section className="blueprint-toolbar">
        <label className="blueprint-search">
          <Search size={17} />
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Tìm chức năng, mã chức năng, đặc tả..."
          />
        </label>
        <label className="blueprint-select">
          <Filter size={16} />
          <select value={status} onChange={(event) => setStatus(event.target.value as BlueprintStatus | 'all')}>
            <option value="all">Tất cả trạng thái</option>
            <option value="live">Hệ thống thật hoàn thiện phạm vi hiện tại</option>
            <option value="foundation">Hệ thống thật đã có một phần</option>
            <option value="planned">Chưa triển khai hệ thống thật</option>
            <option value="optional">Có mô phỏng nâng cao • bật khi cần</option>
          </select>
        </label>
        <label className="blueprint-select">
          <Layers3 size={16} />
          <select value={activeModule} onChange={(event) => setActiveModule(event.target.value)}>
            <option value="all">Tất cả phân hệ</option>
            {erpWmsBlueprint.map((module) => <option key={module.key} value={module.key}>{module.name}</option>)}
          </select>
        </label>
      </section>

      <section className="status-legend">
        {(Object.keys(blueprintDemoStatusLabels) as BlueprintStatus[]).map((item) => (
          <span className={'status-pill ' + item} key={item}>{statusIcon(item)} {blueprintDemoStatusLabels[item]}</span>
        ))}
      </section>

      <div className="blueprint-modules">
        {filtered.map((module) => (
          <section className="blueprint-module" key={module.key}>
            <header>
              <div>
                <h2>{module.name}</h2>
                <p>{module.description}</p>
              </div>
              <div className="module-actions">
                <span className="module-count">{module.capabilities.length} chức năng</span>
                <Link className="module-preview-link" to={'/system-blueprint/' + module.key}>Xem trung tâm công việc →</Link>
              </div>
            </header>

            {module.flow && (
              <div className="flow-strip" aria-label={'Luồng ' + module.name}>
                {module.flow.map((step, index) => (
                  <div className="flow-step" key={step}>
                    <span>{index + 1}</span>{step}
                  </div>
                ))}
              </div>
            )}

            <div className="capability-grid">
              {module.capabilities.map((capability) => (
                <article className="capability-card" key={capability.id}>
                  <div className="capability-top">
                    <span className="cap-id">{capability.id}</span>
                    <span className={'status-pill compact ' + capability.status}>
                      {statusIcon(capability.status)} {blueprintDemoStatusLabels[capability.status]}
                    </span>
                  </div>
                  <h3>{capability.name}</h3>
                  <p>{capability.goal}</p>
                  <div className="surface-row">
                    {capability.surfaces.map((surface) => (
                      <span key={surface}>
                        {surface === 'Mobile' ? <Smartphone size={13} /> : <MonitorSmartphone size={13} />}
                        {surface}
                      </span>
                    ))}
                  </div>
                  <footer>
                    <span>Đặc tả {capability.spec}</span>
                    <div className="capability-links">
                      <Link to={'/system-blueprint/' + module.key + '/' + capability.id}>Mở mô phỏng →</Link>
                      {capability.mockRoute ? <Link to={capability.mockRoute}>Mở mô phỏng chuyên biệt →</Link> : null}
                    </div>
                  </footer>
                </article>
              ))}
            </div>
          </section>
        ))}
        {filtered.length === 0 && <div className="blueprint-empty">Không có chức năng phù hợp bộ lọc.</div>}
      </div>
    </div>
  );
};

export default SystemBlueprint;
