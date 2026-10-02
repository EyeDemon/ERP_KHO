import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, CircleAlert, Filter, Search, ShieldCheck } from 'lucide-react';
import { blueprintStatusLabels, erpWmsBlueprint, type BlueprintStatus } from '../config/erpWmsBlueprint';
import {
  evidenceStatusLabels,
  getBlueprintGovernanceRows,
  platformStandards,
  type Applicability,
  type EvidenceStatus,
  type ReleaseWave,
} from '../config/capabilityGovernance';
import { getCanonicalDocumentationRegister } from '../config/documentationRegister';
import { coreInteractiveDemoIds } from '../config/capabilityDemoScreens';
import './SystemCoverage.css';

const applicabilityLabels: Record<Applicability, string> = {
  REQUIRED_CORE: 'Required Core',
  REQUIRED_WHEN_FEATURE_ENABLED: 'Required when enabled',
  INDUSTRY_OPTIONAL: 'Industry optional',
  IMPLEMENTATION_SPECIFIC: 'Implementation specific',
};

const evidenceClass = (status: EvidenceStatus) => 'evidence-badge evidence-' + status.replace('spec-only', 'spec');

const SystemCoverage = () => {
  const rows = useMemo(() => getBlueprintGovernanceRows(erpWmsBlueprint), []);
  const documentation = useMemo(() => getCanonicalDocumentationRegister(erpWmsBlueprint), []);
  const capabilityLinkedDocs = documentation.filter((item) => item.representation === 'Capability-linked');
  const platformDocs = documentation.filter((item) => item.representation === 'Platform / Governance');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState<BlueprintStatus | 'all'>('all');
  const [wave, setWave] = useState<ReleaseWave | 'all'>('all');
  const [applicability, setApplicability] = useState<Applicability | 'all'>('all');

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('vi');
    return rows.filter(({ module, capability, profile }) => {
      const queryMatch = !q || [
        capability.id,
        capability.name,
        capability.goal,
        capability.spec,
        module.name,
        profile.screenReference ?? '',
      ].join(' ').toLocaleLowerCase('vi').includes(q);
      return queryMatch
        && (status === 'all' || capability.status === status)
        && (wave === 'all' || profile.releaseWave === wave)
        && (applicability === 'all' || profile.applicability === applicability);
    });
  }, [applicability, query, rows, status, wave]);

  const reviewRequired = rows.filter((row) => row.profile.reviewStatus === 'Review Required');
  const traceabilityClosed = rows.filter((row) => row.profile.reviewStatus === 'Traceability Closed');
  const implemented = rows.filter((row) => row.capability.status === 'live').length;
  const foundation = rows.filter((row) => row.capability.status === 'foundation').length;
  const specOnly = rows.length - implemented - foundation;

  return (
    <div className="coverage-page">
      <Link to="/system-blueprint" className="coverage-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="coverage-hero">
        <div>
          <span className="coverage-eyebrow"><ShieldCheck size={16} /> Canonical Completion Chain</span>
          <h1>Coverage & Readiness</h1>
          <p>
            Tách rõ documentation coverage, UI blueprint coverage và production implementation evidence.
            Có spec không đồng nghĩa chức năng đã production-ready.
          </p>
        </div>
        <div className="coverage-score">
          <strong>{rows.length}</strong>
          <span>capabilities traced</span>
        </div>
      </section>

      <section className="coverage-kpis">
        <article><strong>{implemented}</strong><span>Live / implemented</span></article>
        <article><strong>{foundation}</strong><span>Foundation</span></article>
        <article><strong>{specOnly}</strong><span>Production spec-only / optional</span></article>
        <article><strong>{rows.length}</strong><span>Interactive Blueprint demos</span></article>
        <article className="traceability-kpi" title={coreInteractiveDemoIds.length + ' planned capability demos có workflow chuyên biệt'}><strong>{traceabilityClosed.length}</strong><span>Screen traceability closed</span></article>
        <article><strong>{documentation.length}</strong><span>Canonical specs indexed</span></article>
        <article><strong>{platformDocs.length}</strong><span>Platform / governance docs</span></article>
      </section>

      <section className="completion-chain">
        <span>Persona / Requirement</span><b>→</b><span>UX / Screen</span><b>→</b><span>Permission</span><b>→</b>
        <span>Command / API</span><b>→</b><span>State</span><b>→</b><span>Inventory Effect</span><b>→</b>
        <span>Event / Error</span><b>→</b><span>Test</span><b>→</b><span>Operations / Release Evidence</span>
      </section>

      <section className="coverage-toolbar">
        <label><Search size={15} /><input aria-label="Tìm coverage" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Capability, module, spec, screen..." /></label>
        <label><Filter size={15} /><select aria-label="Lọc implementation status" value={status} onChange={(e) => setStatus(e.target.value as BlueprintStatus | 'all')}>
          <option value="all">Tất cả implementation</option>
          <option value="live">Live</option>
          <option value="foundation">Foundation</option>
          <option value="planned">Spec only</option>
          <option value="optional">Optional</option>
        </select></label>
        <label><select aria-label="Lọc release wave" value={wave} onChange={(e) => setWave(e.target.value === 'all' ? 'all' : Number(e.target.value) as ReleaseWave)}>
          <option value="all">Tất cả wave</option>
          {[0,1,2,3,4,5,6].map((item) => <option key={item} value={item}>Wave {item}</option>)}
        </select></label>
        <label><select aria-label="Lọc applicability" value={applicability} onChange={(e) => setApplicability(e.target.value as Applicability | 'all')}>
          <option value="all">Tất cả applicability</option>
          {Object.entries(applicabilityLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select></label>
      </section>

      {traceabilityClosed.length > 0 && (
        <section className="traceability-closed-panel">
          <div className="traceability-title"><CheckCircle2 size={17} /><div><strong>Screen Matrix 229 • Traceability Closed</strong><span>4 legacy UNMAPPED screens đã map tới Blueprint route cụ thể. Trạng thái production/backend vẫn được theo dõi riêng.</span></div></div>
          <div className="traceability-grid">
            {traceabilityClosed.map(({ module, capability, profile }) => (
              <Link key={capability.id} to={'/system-blueprint/' + module.key + '/' + capability.id}>
                <strong>{capability.id} • {capability.name}</strong>
                <span>{profile.screenReference}</span>
                <small>{profile.reviewFinding}</small>
              </Link>
            ))}
          </div>
        </section>
      )}

      {reviewRequired.length > 0 && (
        <section className="review-required-panel">
          <div className="review-title"><CircleAlert size={17} /><div><strong>Screen review còn mở</strong><span>Các mục này vẫn cần mapping/evidence trước khi đóng traceability.</span></div></div>
          <div className="review-grid">
            {reviewRequired.map(({ module, capability, profile }) => (
              <Link key={capability.id} to={'/system-blueprint/' + module.key + '/' + capability.id}>
                <strong>{capability.id} • {capability.name}</strong>
                <span>{profile.screenReference}</span>
                <small>{profile.reviewFinding}</small>
              </Link>
            ))}
          </div>
        </section>
      )}

      <section className="platform-standards-panel">
        <div className="platform-standards-title">
          <div><strong>Canonical Documentation Register • Specs 1–282</strong><span>Toàn bộ tài liệu Notion được index; tài liệu không phải business capability vẫn hiện như platform/governance guardrail.</span></div>
          <span>{capabilityLinkedDocs.length} capability-linked • {platformDocs.length} platform/governance</span>
        </div>
        <details className="documentation-details">
          <summary>Xem toàn bộ {documentation.length} tài liệu canonical</summary>
          <div className="documentation-table-wrap">
            <table className="documentation-table">
              <thead><tr><th>Spec</th><th>Title</th><th>Category</th><th>Representation</th><th>Capability IDs</th></tr></thead>
              <tbody>
                {documentation.map((item) => (
                  <tr key={item.spec}>
                    <td>{item.spec}</td>
                    <td>{item.title}</td>
                    <td>{item.category}</td>
                    <td><span className={'doc-representation ' + (item.representation === 'Capability-linked' ? 'doc-linked' : 'doc-standard')}>{item.representation}</span></td>
                    <td>{item.capabilityIds.length ? item.capabilityIds.join(', ') : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
        <div className="platform-standards-grid">
          {platformStandards.map((item) => (
            <article key={item.spec}>
              <span>Spec {item.spec} • {item.category}</span>
              <strong>{item.title}</strong>
              <small>{item.representation}</small>
              {item.mappedCapabilityIds?.length ? <p>Mapped: {item.mappedCapabilityIds.join(', ')}</p> : null}
            </article>
          ))}
        </div>
      </section>

      <section className="coverage-table-wrap">
        <table className="coverage-table" data-testid="coverage-table">
          <thead>
            <tr>
              <th>Capability</th>
              <th>Owner</th>
              <th>Wave</th>
              <th>Applicability</th>
              <th>Maturity</th>
              <th>Implementation</th>
              <th>Specs</th>
              <th>Evidence chain</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map(({ module, capability, profile }) => (
              <tr key={capability.id}>
                <td>
                  <Link to={'/system-blueprint/' + module.key + '/' + capability.id}>{capability.id} • {capability.name}</Link>
                  {profile.reviewStatus === 'Review Required' && <span className="review-chip">Review Required</span>}
                  {profile.reviewStatus === 'Traceability Closed' && <span className="traceability-chip">Traceability Closed</span>}
                </td>
                <td>{profile.ownerModule}</td>
                <td>Wave {profile.releaseWave}</td>
                <td>{applicabilityLabels[profile.applicability]}</td>
                <td>{profile.maturity}</td>
                <td><span className={'impl-chip impl-' + capability.status}>{blueprintStatusLabels[capability.status]}</span></td>
                <td>{profile.referencedSpecs.join(', ')}</td>
                <td>
                  <div className="evidence-mini">
                    {profile.evidence.map((item) => (
                      <span title={item.label + ': ' + item.note} className={evidenceClass(item.status)} key={item.key}>
                        {item.label}: {evidenceStatusLabels[item.status]}
                      </span>
                    ))}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {filtered.length === 0 && <div className="coverage-empty">Không có capability phù hợp bộ lọc.</div>}
      </section>

      <section className="coverage-legend">
        <div><CheckCircle2 size={14} /><span><b>Covered</b> = có evidence phù hợp ở lớp blueprint/implementation hiện tại.</span></div>
        <div><CircleAlert size={14} /><span><b>Partial / Spec only</b> = tài liệu hoặc nền có, chưa được phép hiểu là production-ready.</span></div>
      </section>
    </div>
  );
};

export default SystemCoverage;
