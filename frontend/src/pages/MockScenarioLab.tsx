import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, FlaskConical, Play, RotateCcw, ShieldCheck } from 'lucide-react';
import { mockGoldenScenarios } from '../mocks/erpWmsMockScenarios';
import { getMockScenarioRuntimeDefinition } from '../mocks/mockScenarioRuntime';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import { useMockDemo } from '../context/MockDemoContext';
import './MockScenarioLab.css';

const MockScenarioLab = () => {
  const [selectedId, setSelectedId] = useState(mockGoldenScenarios[0]?.id ?? '');
  const mockDemo = useMockDemo();
  const selected = useMemo(
    () => mockGoldenScenarios.find((scenario) => scenario.id === selectedId) ?? mockGoldenScenarios[0],
    [selectedId],
  );
  const executedSteps = mockDemo.activeScenarioId === selected?.id ? mockDemo.activeScenarioStep : 0;
  const runtimeDefinition = getMockScenarioRuntimeDefinition(selected?.id);
  const capabilityRoutes = useMemo(
    () => new Map(erpWmsBlueprint.flatMap((module) =>
      module.capabilities.map((capability) => [capability.id, '/system-blueprint/' + module.key + '/' + capability.id] as const),
    )),
    [],
  );

  return (
    <div className="scenario-page">
      <Link to="/system-blueprint" className="scenario-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>
      <section className="scenario-hero">
        <div>
          <span className="scenario-eyebrow"><FlaskConical size={16} /> ERP WMS • Golden Scenario Lab</span>
          <h1>Mock test nghiệp vụ end-to-end</h1>
          <p>{mockGoldenScenarios.length} scenario bao phủ core inventory, outbound/inbound, returns, offline, advanced WMS, planning, finance, automation và safety.</p>
        </div>
        <div className="scenario-count"><strong>{mockGoldenScenarios.length}</strong><span>scenarios</span></div>
      </section>

      <section className="scenario-layout">
        <nav className="scenario-nav" aria-label="Golden scenarios">
          {mockGoldenScenarios.map((scenario) => (
            <button
              type="button"
              key={scenario.id}
              className={scenario.id === selected?.id ? 'active' : undefined}
              onClick={() => setSelectedId(scenario.id)}
            >
              <span>{scenario.id}</span>
              <strong>{scenario.title}</strong>
              <small>Spec {scenario.spec}</small>
            </button>
          ))}
        </nav>

        {selected && (
          <article className="scenario-detail">
            <header>
              <div>
                <span className="scenario-id">{selected.id}</span>
                <h2>{selected.title}</h2>
                <p>{selected.purpose}</p>
              </div>
              <span className="scenario-spec">Spec {selected.spec}</span>
            </header>

            <div className="scenario-precondition">
              <strong>Precondition</strong>
              <span>{selected.precondition}</span>
            </div>

            <section className="scenario-runner">
              <div>
                <span>PHIÊN KỊCH BẢN DÙNG CHUNG • LIÊN MÀN HÌNH</span>
                <strong>Step {executedSteps} / {selected.steps.length}</strong>
              </div>
              <div className="scenario-runner-actions">
                <button
                  type="button"
                  onClick={() => mockDemo.runScenarioStep(selected.id)}
                  disabled={executedSteps >= selected.steps.length}
                >
                  <Play size={14} /> Chạy bước tiếp
                </button>
                <button type="button" onClick={() => mockDemo.resetScenario(selected.id)} disabled={executedSteps === 0}>
                  <RotateCcw size={14} /> Reset
                </button>
                <Link className="scenario-runtime-link" to="/system-blueprint/mock-data">Xem trạng thái dùng chung →</Link>
              </div>
              <div className="scenario-execution-log" data-testid="scenario-execution-log">
                {executedSteps === 0 && <p>Chưa chạy bước nào. Khi chạy, trạng thái session sẽ dùng chung với Mock Data Lab và capability liên quan.</p>}
                {selected.steps.slice(0, executedSteps).map((step, index) => (
                  <div key={'run-' + step.label}>
                    <CheckCircle2 size={14} />
                    <span><strong>{index + 1}. {step.label}</strong><small>{step.state} • {step.inventoryEffect}</small></span>
                  </div>
                ))}
              </div>
            </section>

            {runtimeDefinition && (
              <section className="scenario-shared-state">
                <div>
                  <span>SHARED RUNTIME</span>
                  <strong>{runtimeDefinition.affectedCapabilities.length} capability liên quan</strong>
                </div>
                {mockDemo.activeScenarioId === selected.id && mockDemo.scenarioTotalBalanceCount > 0 && (
                  <div className="scenario-scope-note">
                    Persona thấy {mockDemo.scenarioBalances.length}/{mockDemo.scenarioTotalBalanceCount} inventory bucket
                    {mockDemo.scenarioHiddenBalanceCount > 0 ? ' • ' + mockDemo.scenarioHiddenBalanceCount + ' bucket bị ẩn bởi warehouse scope' : ' • toàn bộ bucket trong scope'}
                  </div>
                )}
                <div className="scenario-capability-chips">
                  {runtimeDefinition.affectedCapabilities.map((capabilityId) => {
                    const route = capabilityRoutes.get(capabilityId);
                    return route
                      ? <Link key={capabilityId} to={route}>{capabilityId}</Link>
                      : <span key={capabilityId}>{capabilityId}</span>;
                  })}
                </div>
                {mockDemo.activeScenarioId === selected.id && mockDemo.scenarioBalances.length > 0 && (
                  <div className="scenario-balance-strip">
                    {mockDemo.scenarioBalances.map((item) => (
                      <div key={item.warehouse + item.location + item.productCode}>
                        <small>{item.warehouse} • {item.location}</small>
                        <strong>{item.productCode}</strong>
                        <span>Tồn thực tế {item.onHand} • Khả dụng {item.available ?? '—'} • Đang vận chuyển {item.inTransit}</span>
                      </div>
                    ))}
                  </div>
                )}
              </section>
            )}

            <div className="scenario-steps">
              {selected.steps.map((step, index) => (
                <div className="scenario-step" key={`${selected.id}-${step.label}`}>
                  <span className="scenario-step-no">{index + 1}</span>
                  <div>
                    <strong>{step.label}</strong>
                    <span className="scenario-state">{step.state}</span>
                    <p>{step.inventoryEffect}</p>
                  </div>
                </div>
              ))}
            </div>

            <section className={'scenario-assertions ' + (executedSteps === selected.steps.length ? 'scenario-assertions-complete' : '')}>
              <div className="scenario-section-title"><ShieldCheck size={17} /><h3>Expected assertions</h3>{executedSteps === selected.steps.length && <span className="scenario-pass">SIMULATION COMPLETE</span>}</div>
              <div className="assertion-grid">
                {selected.assertions.map((assertion) => (
                  <div key={assertion}><CheckCircle2 size={15} /><span>{assertion}</span></div>
                ))}
              </div>
            </section>

            <section className="scenario-metrics">
              <h3>Structured mock metrics</h3>
              <div>
                {Object.entries(selected.metrics).map(([key, value]) => (
                  <span key={key}><small>{key}</small><strong>{String(value)}</strong></span>
                ))}
              </div>
            </section>
          </article>
        )}
      </section>
    </div>
  );
};

export default MockScenarioLab;
