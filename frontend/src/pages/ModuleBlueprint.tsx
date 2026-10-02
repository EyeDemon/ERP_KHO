import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, ArrowRight, Boxes, CheckCircle2, CircleDashed, Database, ExternalLink,
  FileCheck2, LockKeyhole, MonitorSmartphone, Route, ShieldCheck, Smartphone, Workflow
} from 'lucide-react';
import {
  blueprintStatusLabels,
  findBlueprintModule,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import './SystemBlueprint.css';
import './ModuleBlueprint.css';

const statusIcon = (status: BlueprintStatus) => {
  if (status === 'live') return <CheckCircle2 size={15} />;
  return <CircleDashed size={15} />;
};

const ModuleBlueprint = () => {
  const { moduleKey } = useParams();
  const module = findBlueprintModule(moduleKey);

  if (!module) {
    return (
      <div className="module-blueprint-not-found">
        <h1>Không tìm thấy module</h1>
        <Link to="/system-blueprint">Quay lại bản đồ hệ thống</Link>
      </div>
    );
  }

  const liveCount = module.capabilities.filter((item) => item.status === 'live').length;
  const foundationCount = module.capabilities.filter((item) => item.status === 'foundation').length;
  const plannedCount = module.capabilities.filter((item) => item.status === 'planned').length;
  const optionalCount = module.capabilities.filter((item) => item.status === 'optional').length;

  return (
    <div className="module-blueprint-page">
      <Link to="/system-blueprint" className="module-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="module-hero">
        <div>
          <span className="eyebrow"><Workflow size={16} /> Module Blueprint</span>
          <h1>{module.name}</h1>
          <p>{module.description}</p>
        </div>
        <div className="module-stat-grid">
          <div><strong>{module.capabilities.length}</strong><span>Capability</span></div>
          <div><strong>{liveCount}</strong><span>Đã có</span></div>
          <div><strong>{foundationCount}</strong><span>Đã có nền</span></div>
          <div><strong>{plannedCount + optionalCount}</strong><span>Còn lại</span></div>
        </div>
      </section>

      {module.flow && (
        <section className="module-panel">
          <div className="module-panel-title"><Route size={18} /><h2>Luồng nghiệp vụ chuẩn</h2></div>
          <div className="module-flow">
            {module.flow.map((step, index) => (
              <div className="module-flow-node" key={step}>
                <span>{index + 1}</span>
                <strong>{step}</strong>
                {index < module.flow!.length - 1 && <ArrowRight size={16} />}
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="module-workbench">
        <div className="workbench-main">
          <div className="workbench-header">
            <div>
              <span className="workbench-kicker">WORK CENTER PREVIEW</span>
              <h2>{module.name}</h2>
              <p>Minh họa bố cục vận hành theo Design System 217 và IA 216.</p>
            </div>
            <button type="button" className="demo-primary">+ Tạo tác vụ</button>
          </div>

          <div className="demo-kpis">
            <article><span>Đang xử lý</span><strong>{Math.max(4, module.capabilities.length * 3)}</strong><small>Trong warehouse scope</small></article>
            <article><span>Chờ xử lý</span><strong>{Math.max(2, module.capabilities.length)}</strong><small>Ưu tiên theo SLA</small></article>
            <article><span>Ngoại lệ</span><strong>{Math.max(1, Math.floor(module.capabilities.length / 3))}</strong><small>Cần triage</small></article>
            <article><span>Hoàn tất hôm nay</span><strong>{module.capabilities.length * 7}</strong><small>Operational sample</small></article>
          </div>

          <div className="demo-filterbar">
            <input aria-label="Tìm trong work center" placeholder="Tìm mã, sản phẩm, chứng từ, lot/serial..." />
            <select aria-label="Kho"><option>Tất cả kho được phép</option></select>
            <select aria-label="Trạng thái"><option>Tất cả trạng thái</option></select>
            <button type="button">Lọc</button>
          </div>

          <div className="demo-table-wrap">
            <table className="demo-table">
              <thead>
                <tr>
                  <th>Mã / Capability</th>
                  <th>Mục tiêu vận hành</th>
                  <th>Surface</th>
                  <th>Trạng thái</th>
                  <th>Spec</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {module.capabilities.map((capability) => (
                  <tr key={capability.id}>
                    <td><strong>{capability.id}</strong><span>{capability.name}</span></td>
                    <td>{capability.goal}</td>
                    <td>
                      <div className="surface-row compact-surfaces">
                        {capability.surfaces.map((surface) => (
                          <span key={surface}>
                            {surface === 'Mobile' ? <Smartphone size={12} /> : <MonitorSmartphone size={12} />}
                            {surface}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td>
                      <span className={'status-pill compact ' + capability.status}>
                        {statusIcon(capability.status)} {blueprintStatusLabels[capability.status]}
                      </span>
                    </td>
                    <td>{capability.spec}</td>
                    <td>
                      {capability.route
                        ? <Link className="table-open-link" to={capability.route}><ExternalLink size={14} /> Mở</Link>
                        : <span className="demo-only">Minh họa</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <aside className="trace-panel">
          <span className="workbench-kicker">TRACEABILITY</span>
          <h3>Đường kiểm soát bắt buộc</h3>
          <div className="trace-chain">
            <div><ShieldCheck size={17} /><span><strong>Permission</strong><small>Role + warehouse scope</small></span></div>
            <div><Workflow size={17} /><span><strong>Command / API</strong><small>Explicit business action</small></span></div>
            <div><LockKeyhole size={17} /><span><strong>State + Concurrency</strong><small>Transition + version/idempotency</small></span></div>
            <div><Boxes size={17} /><span><strong>Domain Rules</strong><small>UOM, status, lot/serial, capacity</small></span></div>
            <div><Database size={17} /><span><strong>Ledger / Balance</strong><small>Posting boundary khi có inventory effect</small></span></div>
            <div><FileCheck2 size={17} /><span><strong>Audit / Outbox</strong><small>Evidence và integration event</small></span></div>
          </div>
          <div className="trace-note">
            Màn hình này là <strong>minh họa hệ thống</strong>. Capability chưa có backend được giữ ở trạng thái
            “Theo đặc tả” hoặc “Nâng cao”, không giả lập là đã production-ready.
          </div>
        </aside>
      </section>

      <section className="module-panel">
        <div className="module-panel-title"><ShieldCheck size={18} /><h2>Nguyên tắc triển khai module</h2></div>
        <div className="module-rule-grid">
          <div><strong>01</strong><span>Không dùng UI state thay cho server authorization.</span></div>
          <div><strong>02</strong><span>Không update balance trực tiếp từ business document.</span></div>
          <div><strong>03</strong><span>Mutation critical phải concurrency-safe và idempotent.</span></div>
          <div><strong>04</strong><span>Ledger đã post là immutable; sửa sai bằng reversal/correction.</span></div>
          <div><strong>05</strong><span>UI phải giữ đúng state-machine semantics và error code contract.</span></div>
          <div><strong>06</strong><span>Chỉ đánh dấu hoàn tất khi có test + QA + release evidence.</span></div>
        </div>
      </section>
    </div>
  );
};

export default ModuleBlueprint;
