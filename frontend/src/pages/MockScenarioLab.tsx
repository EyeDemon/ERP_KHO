import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, FlaskConical, Play, RotateCcw, ShieldCheck } from 'lucide-react';
import { mockGoldenScenarios } from '../mocks/erpWmsMockScenarios';
import './MockScenarioLab.css';

const MockScenarioLab = () => {
  const [selectedId, setSelectedId] = useState(mockGoldenScenarios[0]?.id ?? '');
  const [executedSteps, setExecutedSteps] = useState(0);
  const selected = useMemo(
    () => mockGoldenScenarios.find((scenario) => scenario.id === selectedId) ?? mockGoldenScenarios[0],
    [selectedId],
  );

  return (
    <div className="scenario-page">
      <Link to="/system-blueprint" className="scenario-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>
      <section className="scenario-hero">
        <div>
          <span className="scenario-eyebrow"><FlaskConical size={16} /> ERP WMS • Golden Scenario Lab</span>
          <h1>Mock test nghiệp vụ end-to-end</h1>
          <p>10 scenario mô phỏng các boundary quan trọng nhất: inbound, outbound, transfer, count, QC, reversal, concurrency, idempotency, returns và offline sync.</p>
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
              onClick={() => { setSelectedId(scenario.id); setExecutedSteps(0); }}
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
                <span>SIMULATOR • READ ONLY</span>
                <strong>Step {executedSteps} / {selected.steps.length}</strong>
              </div>
              <div className="scenario-runner-actions">
                <button
                  type="button"
                  onClick={() => setExecutedSteps((value) => Math.min(value + 1, selected.steps.length))}
                  disabled={executedSteps >= selected.steps.length}
                >
                  <Play size={14} /> Chạy bước tiếp
                </button>
                <button type="button" onClick={() => setExecutedSteps(0)} disabled={executedSteps === 0}>
                  <RotateCcw size={14} /> Reset
                </button>
              </div>
              <div className="scenario-execution-log" data-testid="scenario-execution-log">
                {executedSteps === 0 && <p>Chưa chạy bước nào. Simulator chỉ thay đổi UI local.</p>}
                {selected.steps.slice(0, executedSteps).map((step, index) => (
                  <div key={'run-' + step.label}>
                    <CheckCircle2 size={14} />
                    <span><strong>{index + 1}. {step.label}</strong><small>{step.state} • {step.inventoryEffect}</small></span>
                  </div>
                ))}
              </div>
            </section>

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
