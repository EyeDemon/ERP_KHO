import type { ReactNode } from 'react';
import { ArrowRightLeft, RefreshCw } from 'lucide-react';
import './TransferCapabilityMock.css';

type Props = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
  <span className={'transfer-mock-badge ' + tone}>{children}</span>
);

const Header = ({ id, title, description, icon }: { id: string; title: string; description: string; icon: ReactNode }) => (
  <header className="transfer-mock-header">
    <div className="transfer-mock-title-row">
      <span className="transfer-mock-icon" aria-hidden="true">{icon}</span>
      <div><span className="transfer-mock-kicker">TRANSFER & REPLENISHMENT • {id} • FRONTEND MOCK</span><h2>{title}</h2></div>
    </div>
    <p>{description}</p>
    <div className="transfer-mock-scope"><Badge tone="neutral">Read-only</Badge><span>Không gọi API production • Không ghi inventory • Không giả posting</span></div>
  </header>
);

const TransitMock = () => {
  const checkpoints = [
    { label: 'Source before dispatch', qty: '520 Gói', state: 'Recorded', tone: 'neutral' as const },
    { label: 'Source after dispatch', qty: '320 Gói', state: '-200', tone: 'warning' as const },
    { label: 'In transit', qty: '200 Gói', state: 'IN_TRANSIT', tone: 'success' as const },
    { label: 'Destination received', qty: '0 Gói', state: 'Pending', tone: 'neutral' as const },
  ];
  return (
    <section className="transfer-capability-mock" data-testid="transfer-capability-mock-TR-02">
      <Header id="TR-02" title="In-Transit Inventory Reconciliation" description="Mock quantity conservation qua source → transit → destination theo transfer reference và owner." icon={<ArrowRightLeft size={20} />} />
      <div className="transfer-mock-metrics">
        <div><span>Transfer</span><strong>TRF-2026-0024</strong><small>WH-HCM-01 → WH-DN-01</small></div>
        <div><span>Dispatched</span><strong>200 Gói</strong><small>Source posting complete</small></div>
        <div><span>In transit</span><strong>200 Gói</strong><small>Owner OWN-COMPANY-01</small></div>
        <div><span>Variance</span><strong>0</strong><small>Conservation green</small></div>
      </div>
      <div className="transfer-conservation-grid">
        {checkpoints.map((item,index) => (
          <article key={item.label}>
            <div><span>{item.label}</span><strong>{item.qty}</strong></div>
            <Badge tone={item.tone}>{item.state}</Badge>
            {index < checkpoints.length - 1 ? <span className="transfer-arrow" aria-hidden="true">→</span> : null}
          </article>
        ))}
      </div>
      <div className="transfer-mock-callout warning">
        <strong>Conservation rule</strong>
        <span>Transfer quantity phải bảo toàn giữa source, transit và destination. Mismatch không được “fix” bằng sửa balance trực tiếp; phải reconcile qua canonical transfer/inventory commands.</span>
      </div>
    </section>
  );
};

const ReplenishmentMock = () => {
  const sources = [
    { location: 'RESERVE-A-04', eligible: '240 Cái', distance: '28 m', score: 91, result: 'Selected', tone: 'success' as const },
    { location: 'RESERVE-B-01', eligible: '120 Cái', distance: '19 m', score: 76, result: 'Partial only', tone: 'warning' as const },
    { location: 'QC-HOLD-02', eligible: '0', distance: '17 m', score: 0, result: 'Rejected', tone: 'danger' as const },
  ];
  return (
    <section className="transfer-capability-mock" data-testid="transfer-capability-mock-TR-05">
      <Header id="TR-05" title="Replenishment Source & Pick-Face Plan" description="Mock min/max demand, eligible reserve sources, capacity và internal move semantics cho pick face." icon={<RefreshCw size={20} />} />
      <div className="transfer-mock-metrics">
        <div><span>Pick face</span><strong>PICK-A-01</strong><small>SKU-1001</small></div>
        <div><span>Current / Min / Max</span><strong>60 / 60 / 240</strong><small>Cái</small></div>
        <div><span>Suggested</span><strong>180 Cái</strong><small>15 Thùng × 12</small></div>
        <div><span>Destination headroom</span><strong>204 Cái</strong><small>Capacity allows move</small></div>
      </div>
      <div className="transfer-mock-table-scroll">
        <table aria-label="Mock replenishment source candidates">
          <thead><tr><th>Reserve source</th><th>Eligible</th><th>Travel</th><th>Score</th><th>Decision</th></tr></thead>
          <tbody>{sources.map(item => <tr key={item.location}><td><strong>{item.location}</strong></td><td>{item.eligible}</td><td>{item.distance}</td><td>{item.score || '—'}</td><td><Badge tone={item.tone}>{item.result}</Badge></td></tr>)}</tbody>
        </table>
      </div>
      <div className="transfer-replenish-flow">
        <div><span>1</span><strong>Demand detected</strong><small>Pick face at/below min.</small></div>
        <div><span>2</span><strong>Source eligible</strong><small>Status/lock/owner checked.</small></div>
        <div><span>3</span><strong>Capacity checked</strong><small>Destination can accept 180.</small></div>
        <div><span>4</span><strong>Create move task</strong><small>Posting preserves warehouse total.</small></div>
      </div>
    </section>
  );
};

const TransferCapabilityMock = ({ capabilityId }: Props) => {
  if (capabilityId === 'TR-02') return <TransitMock />;
  if (capabilityId === 'TR-05') return <ReplenishmentMock />;
  return null;
};

export default TransferCapabilityMock;
