import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, Boxes, CheckCircle2, CircleDashed, Database, ExternalLink,
  FileText, LockKeyhole, MonitorSmartphone, ShieldCheck, Smartphone, Workflow, CircleAlert, Gauge
} from 'lucide-react';
import {
  blueprintStatusLabels,
  findBlueprintModule,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import { getMockCapabilityFixture, getMockWorkCenter } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import { evidenceStatusLabels, getCapabilityGovernanceProfile } from '../config/capabilityGovernance';
import { getSpecializedScreenPreview } from '../config/reviewRequiredScreens';
import CapabilityInteractiveDemo from './CapabilityInteractiveDemo';
import './CapabilityPreview.css';

const statusIcon = (status: BlueprintStatus) =>
  status === 'live' ? <CheckCircle2 size={15} /> : <CircleDashed size={15} />;

const CapabilityPreview = () => {
  const { moduleKey, capabilityId } = useParams();
  const demo = useMockDemo();
  const module = findBlueprintModule(moduleKey);
  const capability = module?.capabilities.find((item) => item.id === capabilityId);
  const workCenter = getMockWorkCenter(moduleKey);
  const fixture = getMockCapabilityFixture(capabilityId);
  const governance = module && capability ? getCapabilityGovernanceProfile(module, capability) : undefined;
  const specializedPreview = getSpecializedScreenPreview(capabilityId);
  const records = (workCenter?.records ?? []).filter((record) => demo.canSeeWarehouse(record.warehouse));
  const fixtureVisible = fixture ? demo.canSeeWarehouse(fixture.sampleWarehouse) : false;

  if (!module || !capability) {
    return (
      <div className="capability-not-found">
        <h1>Không tìm thấy capability</h1>
        <Link to="/system-blueprint">Quay lại bản đồ hệ thống</Link>
      </div>
    );
  }

  return (
    <div className="capability-page">
      <Link to={'/system-blueprint/' + module.key} className="capability-back">
        <ArrowLeft size={16} /> {module.name}
      </Link>

      <section className="capability-hero">
        <div>
          <span className="capability-eyebrow">{capability.id} • Capability Preview</span>
          <h1>{capability.name}</h1>
          <p>{capability.goal}</p>
          <div className="capability-meta">
            <span className={'status-pill ' + capability.status}>{statusIcon(capability.status)} {blueprintStatusLabels[capability.status]}</span>
            <span>Spec {capability.spec}</span>
            {governance && <span>Wave {governance.releaseWave}</span>}
            {governance && <span>{governance.applicability}</span>}
            {governance && <span>{governance.maturity}</span>}
            {capability.surfaces.map((surface) => (
              <span key={surface}>
                {surface === 'Mobile' ? <Smartphone size={13} /> : <MonitorSmartphone size={13} />}
                {surface}
              </span>
            ))}
          </div>
        </div>
        {capability.route ? (
          <Link to={capability.route} className="capability-real-link"><ExternalLink size={16} /> Mở chức năng hiện có</Link>
        ) : (
          <span className="capability-preview-badge">MOCK / SPEC PREVIEW</span>
        )}
      </section>

      {module.flow && (
        <section className="capability-panel">
          <div className="capability-panel-title"><Workflow size={18} /><h2>Business flow liên quan</h2></div>
          <div className="capability-flow">
            {module.flow.map((step, index) => (
              <div key={step}><span>{index + 1}</span><strong>{step}</strong></div>
            ))}
          </div>
        </section>
      )}

      {specializedPreview && (
        <section className="capability-panel specialized-preview">
          <div className="capability-panel-title"><CircleAlert size={18} /><h2>{specializedPreview.title}</h2><span className="review-required-badge">Screen Matrix • Review Required</span></div>
          <p className="specialized-subtitle">{specializedPreview.subtitle}</p>
          <div className="specialized-fields">
            {specializedPreview.fields.map((field) => (
              <div key={field.label}><span>{field.label}</span><strong>{field.value}</strong>{field.helper ? <small>{field.helper}</small> : null}</div>
            ))}
          </div>
          <div className="specialized-columns">
            <div><h3>Execution steps</h3><ol>{specializedPreview.steps.map((step) => <li key={step}>{step}</li>)}</ol></div>
            <div><h3>Validation / policy</h3><ul>{specializedPreview.validations.map((rule) => <li key={rule}>{rule}</li>)}</ul></div>
          </div>
          <div className="specialized-boundary"><strong>Inventory boundary</strong><span>{specializedPreview.inventoryBoundary}</span></div>
          <div className="specialized-boundary"><strong>Authorization</strong><span>{specializedPreview.permissionNote}</span></div>
          <div className="specialized-screen-ref">{specializedPreview.screenReference}</div>
        </section>
      )}

      <CapabilityInteractiveDemo
        capability={capability}
        moduleName={module.name}
        moduleFlow={module.flow}
        sampleReference={fixtureVisible ? fixture?.sampleReference : records[0]?.reference}
        sampleWarehouse={fixtureVisible ? fixture?.sampleWarehouse : records[0]?.warehouse}
        recordCount={records.length}
      />

      <section className="capability-grid-layout">
        <div className="capability-main">
          <section className="capability-panel">
            <div className="capability-panel-title"><FileText size={18} /><h2>Screen preview</h2></div>
            <div className="preview-toolbar">
              <input aria-label="Tìm dữ liệu mock capability" placeholder="Tìm mã / SKU / chứng từ..." readOnly value="" />
              <select aria-label="Mock warehouse"><option>Tất cả kho được phép</option></select>
              <button type="button" disabled>Mock read-only</button>
            </div>

            <div className="capability-kpis">
              <div><span>Sample records</span><strong>{records.length}</strong></div>
              <div><span>Module</span><strong>{module.name}</strong></div>
              <div><span>Surface</span><strong>{capability.surfaces.join(' / ')}</strong></div>
              <div><span>Fixture</span><strong>{fixture?.fixtureId ?? '—'}</strong></div>
            </div>

            {fixture && (
              <div className="capability-fixture-trace">
                <strong>Capability fixture:</strong>
                <span>{fixture.fixtureId}</span>
                {fixtureVisible ? (
                  <>
                    <span>{fixture.sampleReference}</span>
                    <span>{fixture.sampleWarehouse}</span>
                    <span>{fixture.sampleStatus}</span>
                  </>
                ) : (
                  <span>Sample record ẩn bởi simulated warehouse scope</span>
                )}
              </div>
            )}

            <div className="capability-records">
              {records.slice(0, 5).map((record) => (
                <article key={record.id}>
                  <div>
                    <span>{record.type}</span>
                    <strong>{record.reference}</strong>
                    <p>{record.subject}</p>
                  </div>
                  <div>
                    <small>{record.warehouse}</small>
                    <span className={'cap-record-status tone-' + record.tone}>{record.status}</span>
                  </div>
                </article>
              ))}
            </div>
          </section>

          {capability.surfaces.includes('Mobile') && (
            <section className="capability-panel">
              <div className="capability-panel-title"><Smartphone size={18} /><h2>Mobile scan-first preview</h2></div>
              <div className="mobile-preview-shell">
                <div className="mobile-preview-top">
                  <strong>ERP WMS</strong>
                  <span>{fixtureVisible ? fixture?.sampleWarehouse : (records[0]?.warehouse ?? 'No warehouse in scope')}</span>
                </div>
                <div className="mobile-preview-body">
                  <span className="mobile-task-label">TASK / {capability.id}</span>
                  <h3>{capability.name}</h3>
                  <div className="mobile-scan-box">▣ Quét barcode / location / serial</div>
                  <div className="mobile-record-card">
                    <small>Reference</small>
                    <strong>{fixtureVisible ? fixture?.sampleReference : (records[0]?.reference ?? '—')}</strong>
                    <span>{records[0]?.subject ?? capability.goal}</span>
                  </div>
                  <div className="mobile-quantity-row">
                    <span className="mobile-quantity-label">Số lượng</span>
                    <div><strong>{records[0]?.quantity ?? '—'}</strong><span>{records[0]?.uom ?? 'UOM'}</span></div>
                  </div>
                  <button type="button" disabled>Xác nhận • Mock read-only</button>
                </div>
              </div>
            </section>
          )}

          {governance && (
            <section className="capability-panel">
              <div className="capability-panel-title"><Gauge size={18} /><h2>Capability governance & completeness</h2></div>
              <div className="governance-summary">
                <div><span>Owner</span><strong>{governance.ownerModule}</strong></div>
                <div><span>Release wave</span><strong>Wave {governance.releaseWave}</strong></div>
                <div><span>Applicability</span><strong>{governance.applicability}</strong></div>
                <div><span>Maturity</span><strong>{governance.maturity}</strong></div>
                <div><span>Screen Matrix</span><strong>{governance.reviewStatus}</strong></div>
                <div><span>Specs</span><strong>{governance.referencedSpecs.join(', ')}</strong></div>
              </div>
              <div className="governance-evidence">
                {governance.evidence.map((item) => (
                  <div key={item.key} className={'governance-evidence-item evidence-' + item.status.replace('spec-only', 'spec')}>
                    <strong>{item.label}</strong><span>{evidenceStatusLabels[item.status]}</span><small>{item.note}</small>
                  </div>
                ))}
              </div>
              {governance.reviewFinding ? <div className="governance-review-note"><CircleAlert size={14} /> {governance.reviewFinding}</div> : null}
            </section>
          )}

          <section className="capability-panel">
            <div className="capability-panel-title"><ShieldCheck size={18} /><h2>Expected UX states</h2></div>
            <div className="ux-state-grid">
              <div><strong>Default</strong><span>Danh sách/detail theo permission và warehouse scope.</span></div>
              <div><strong>Loading</strong><span>Giữ layout ổn định, không cho duplicate critical action.</span></div>
              <div><strong>Empty</strong><span>Giải thích vì sao không có dữ liệu và action hợp lệ tiếp theo.</span></div>
              <div><strong>Validation</strong><span>Field-level validation; quantity luôn rõ UOM/base UOM.</span></div>
              <div><strong>Conflict</strong><span>409/412 hiển thị recovery path, reload state mới.</span></div>
              <div><strong>Forbidden</strong><span>Server authorization là nguồn quyết định cuối.</span></div>
              <div><strong>Exception</strong><span>Giữ context/evidence/reason và route sang resolution workflow.</span></div>
              <div><strong>Success</strong><span>Reload canonical detail, hiển thị audit/event reference khi cần.</span></div>
            </div>
          </section>
        </div>

        <aside className="capability-contract">
          <span className="capability-eyebrow">Technical contract</span>
          <h3>Guardrails bắt buộc</h3>
          <div className="contract-chain">
            <div><ShieldCheck size={17} /><span><strong>Authorization</strong><small>Permission + warehouse scope</small></span></div>
            <div><Workflow size={17} /><span><strong>State machine</strong><small>Explicit command; không set status tùy ý</small></span></div>
            <div><LockKeyhole size={17} /><span><strong>Concurrency</strong><small>Version + inventory protection</small></span></div>
            <div><Boxes size={17} /><span><strong>Inventory integrity</strong><small>Không ad-hoc mutate balance</small></span></div>
            <div><Database size={17} /><span><strong>Ledger</strong><small>Immutable posting / reversal</small></span></div>
            <div><FileText size={17} /><span><strong>Audit & Outbox</strong><small>Atomic evidence khi mutation critical</small></span></div>
          </div>

          {governance && (
            <div className="technical-trace">
              <div><strong>Permission</strong><span>{governance.permissionModel}</span></div>
              <div><strong>Command / API</strong><span>{governance.commandApiModel}</span></div>
              <div><strong>State model</strong><span>{governance.stateModel}</span></div>
              <div><strong>Inventory effect</strong><span>{governance.inventoryEffect}</span></div>
            </div>
          )}

          <div className="capability-contract-note">
            <strong>Preview semantics:</strong> trang này minh họa UX/contract cho capability dựa trên spec.
            Nếu trạng thái là “Theo đặc tả” hoặc “Nâng cao”, nó chưa được coi là backend production-ready.
          </div>
        </aside>
      </section>
    </div>
  );
};

export default CapabilityPreview;
