import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Activity, Boxes, CheckCircle2, CircleDashed, Filter, Layers3, MonitorSmartphone,
  Network, Search, ShieldCheck, Smartphone, Sparkles, Workflow
} from 'lucide-react';
import {
  blueprintStatusLabels,
  blueprintTotals,
  erpWmsBlueprint,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import { mockRecordCount, mockWarehouses, mockProducts, mockPartners } from '../mocks/erpWmsMockData';
import './SystemBlueprint.css';

const statusIcon = (status: BlueprintStatus) => {
  if (status === 'live') return <CheckCircle2 size={15} />;
  if (status === 'foundation') return <Activity size={15} />;
  if (status === 'optional') return <Sparkles size={15} />;
  return <CircleDashed size={15} />;
};

const SystemBlueprint = () => {
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
          <div className="eyebrow"><Layers3 size={16} /> ERP WMS • Complete System Blueprint</div>
          <h1>Bản đồ chức năng ERP/WMS hoàn chỉnh</h1>
          <p>
            Màn hình minh họa tổng thể được dựng từ bộ đặc tả Notion. Mỗi capability được phân loại rõ:
            phần đã có thật, phần đã có nền, phần còn phải triển khai và phần nâng cao chỉ bật khi có nhu cầu.
          </p>
        </div>
        <div className="hero-badge">
          <ShieldCheck size={22} />
          <span>Ledger-first • Permission-aware • State-machine driven</span>
        </div>
      </section>

      <section className="blueprint-kpis">
        <article><strong>{blueprintTotals.modules}</strong><span>Nhóm hệ thống</span></article>
        <article><strong>{blueprintTotals.capabilities}</strong><span>Capability</span></article>
        <article className="kpi-live"><strong>{blueprintTotals.live}</strong><span>Đã có chức năng</span></article>
        <article className="kpi-foundation"><strong>{blueprintTotals.foundation}</strong><span>Đã có nền</span></article>
        <article><strong>{blueprintTotals.planned}</strong><span>Cần triển khai</span></article>
        <article><strong>{blueprintTotals.optional}</strong><span>Nâng cao</span></article>
        <article className="kpi-mock"><strong>{mockRecordCount}</strong><span>Mock records</span></article>
      </section>

      <section className="mock-dataset-summary">
        <strong>Demo dataset:</strong>
        <span>{mockWarehouses.length} kho</span>
        <span>{mockProducts.length} SKU</span>
        <span>{mockPartners.length} đối tác</span>
        <span>{mockRecordCount} operational records</span>
        <span>Lot / Serial / Inventory / Transfer / Count / Approval / Integration / Mobile</span>
        <div className="blueprint-lab-links">
          <Link className="scenario-lab-link" to="/system-blueprint/mock-data">Mở Mock Data Lab →</Link>
          <Link className="scenario-lab-link" to="/system-blueprint/scenarios">Mở Golden Scenario Lab →</Link>
        </div>
      </section>

      <section className="blueprint-principles">
        <div><Workflow size={18} /><span>Command → Authorization → State → Domain Rules → Posting</span></div>
        <div><Boxes size={18} /><span>Immutable Ledger → Balance Projection → Audit → Outbox</span></div>
        <div><Network size={18} /><span>Web ERP + Mobile WMS + Integration dùng chung business semantics</span></div>
      </section>

      <section className="blueprint-toolbar">
        <label className="blueprint-search">
          <Search size={17} />
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Tìm chức năng, mã capability, spec..."
          />
        </label>
        <label className="blueprint-select">
          <Filter size={16} />
          <select value={status} onChange={(event) => setStatus(event.target.value as BlueprintStatus | 'all')}>
            <option value="all">Tất cả trạng thái</option>
            <option value="live">Đã có chức năng</option>
            <option value="foundation">Đã có nền / đang hoàn thiện</option>
            <option value="planned">Theo đặc tả — chưa triển khai</option>
            <option value="optional">Nâng cao / bật theo nhu cầu</option>
          </select>
        </label>
        <label className="blueprint-select">
          <Layers3 size={16} />
          <select value={activeModule} onChange={(event) => setActiveModule(event.target.value)}>
            <option value="all">Tất cả module</option>
            {erpWmsBlueprint.map((module) => <option key={module.key} value={module.key}>{module.name}</option>)}
          </select>
        </label>
      </section>

      <section className="status-legend">
        {(Object.keys(blueprintStatusLabels) as BlueprintStatus[]).map((item) => (
          <span className={'status-pill ' + item} key={item}>{statusIcon(item)} {blueprintStatusLabels[item]}</span>
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
                <Link className="module-preview-link" to={'/system-blueprint/' + module.key}>Xem work center →</Link>
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
                      {statusIcon(capability.status)} {blueprintStatusLabels[capability.status]}
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
                    <span>Spec {capability.spec}</span>
                    {capability.route
                      ? <Link to={capability.route}>Mở chức năng →</Link>
                      : <Link to={'/system-blueprint/' + module.key}>Xem minh họa →</Link>}
                  </footer>
                </article>
              ))}
            </div>
          </section>
        ))}
        {filtered.length === 0 && <div className="blueprint-empty">Không có capability phù hợp bộ lọc.</div>}
      </div>
    </div>
  );
};

export default SystemBlueprint;
