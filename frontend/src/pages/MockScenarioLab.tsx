import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, FlaskConical, ShieldCheck } from 'lucide-react';
import { mockGoldenScenarios } from '../mocks/erpWmsMockScenarios';
import './MockScenarioLab.css';

const MockScenarioLab = () => {
  const [selectedId, setSelectedId] = useState(mockGoldenScenarios[0]?.id ?? '');
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

            <section className="scenario-assertions">
              <div className="scenario-section-title"><ShieldCheck size={17} /><h3>Expected assertions</h3></div>
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
