import type { ReactNode } from 'react';
import { Boxes, Layers3, PackageCheck, Route, Truck, Waves } from 'lucide-react';
import './OutboundCapabilityMock.css';

type OutboundMockProps = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: string }) => (
  <span className={'outbound-mock-badge ' + tone}>{children}</span>
);

const Header = ({
  id,
  title,
  description,
  icon,
}: {
  id: string;
  title: string;
  description: string;
  icon: ReactNode;
}) => (
  <header className="outbound-mock-header">
    <div className="outbound-mock-title-row">
      <span className="outbound-mock-icon" aria-hidden="true">{icon}</span>
      <div>
        <span className="outbound-mock-kicker">OUTBOUND • {id} • FRONTEND MOCK</span>
        <h2 id={'outbound-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="outbound-mock-scope">
      <Badge tone="neutral">Read-only</Badge>
      <span>Không gọi API production • Không ghi inventory • Không giả mutation thành công</span>
    </div>
  </header>
);

const AllocationMock = () => {
  const candidates = [
    { location: 'A01-R02-L03-B04', lot: 'LOT-260930-A', eligible: '120 Gói', rule: 'FEFO + unlocked', score: 94, tone: 'success' as const, result: 'Allocated' },
    { location: 'A02-R01-L01-B02', lot: 'LOT-261001-B', eligible: '64 Gói', rule: 'FEFO + farther', score: 81, tone: 'neutral' as const, result: 'Fallback' },
    { location: 'QC-HOLD-02', lot: 'LOT-260928-Q', eligible: '0', rule: 'QC Hold', score: 0, tone: 'danger' as const, result: 'Rejected' },
  ];

  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-03" aria-labelledby="outbound-out-03-title">
      <Header
        id="OUT-03"
        title="Allocation Candidate Workbench"
        description="Mô phỏng cách reserved demand được gắn vào location/lot/serial eligible mà chưa làm giảm warehouse On Hand."
        icon={<Layers3 size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Sales order</span><strong>SO-2026-5108</strong><small>Released</small></div>
        <div><span>Reserved</span><strong>120 Gói</strong><small>Demand protected</small></div>
        <div><span>Allocated</span><strong>120 Gói</strong><small>1 location • 1 lot</small></div>
        <div><span>On Hand effect</span><strong>0</strong><small>Dispatch mới deduct</small></div>
      </div>
      <div className="outbound-mock-table-scroll">
        <table aria-label="Mock allocation candidates">
          <thead><tr><th>Location</th><th>Lot</th><th>Eligible</th><th>Rule</th><th>Score</th><th>Result</th></tr></thead>
          <tbody>
            {candidates.map(item => (
              <tr key={item.location}>
                <td><strong>{item.location}</strong></td><td>{item.lot}</td><td>{item.eligible}</td><td>{item.rule}</td><td>{item.score || '—'}</td>
                <td><Badge tone={item.tone}>{item.result}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="outbound-mock-callout warning">
        <strong>Eligibility guard</strong>
        <span>Allocation phải loại inventory không eligible theo status/lock/lot/serial; biết location ID không được phép bypass rule.</span>
      </div>
    </section>
  );
};

const WaveMock = () => {
  const buckets = [
    { label: 'Priority express', orders: 8, lines: 26, tasks: 14, tone: 'warning' as const },
    { label: 'Same-day', orders: 14, lines: 51, tasks: 22, tone: 'success' as const },
    { label: 'Standard', orders: 21, lines: 74, tasks: 31, tone: 'neutral' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-04" aria-labelledby="outbound-out-04-title">
      <Header
        id="OUT-04"
        title="Wave / Batch / Cluster Planning"
        description="Mock gom order thành execution batch theo priority, cutoff, zone và workload; release wave không tự làm thay đổi inventory."
        icon={<Waves size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Wave</span><strong>WV-2026-301</strong><small>Cutoff 14:30</small></div>
        <div><span>Orders</span><strong>43</strong><small>151 lines</small></div>
        <div><span>Projected tasks</span><strong>67</strong><small>5 picking zones</small></div>
        <div><span>Labor fit</span><strong>86%</strong><small>Within shift capacity</small></div>
      </div>
      <div className="outbound-wave-grid">
        {buckets.map(bucket => (
          <article key={bucket.label}>
            <div><strong>{bucket.label}</strong><Badge tone={bucket.tone}>{bucket.orders} orders</Badge></div>
            <dl>
              <div><dt>Lines</dt><dd>{bucket.lines}</dd></div>
              <div><dt>Projected tasks</dt><dd>{bucket.tasks}</dd></div>
            </dl>
          </article>
        ))}
      </div>
      <div className="outbound-wave-rules">
        <div><span>1</span><strong>Filter eligible released demand</strong><small>Warehouse scope + cutoff + hold state.</small></div>
        <div><span>2</span><strong>Cluster by zone / carrier / service</strong><small>Giảm travel nhưng không đổi allocation truth.</small></div>
        <div><span>3</span><strong>Capacity check</strong><small>Labor, equipment và remaining shift.</small></div>
        <div><span>4</span><strong>Release execution tasks</strong><small>Task creation phải idempotent.</small></div>
      </div>
    </section>
  );
};

const StagingLoadingMock = () => {
  const hus = [
    { hu: 'HU-5108-01', lane: 'STAGE-OUT-03', sequence: '1', status: 'Ready', tone: 'success' as const },
    { hu: 'HU-5108-02', lane: 'STAGE-OUT-03', sequence: '2', status: 'Ready', tone: 'success' as const },
    { hu: 'HU-5108-03', lane: 'STAGE-OUT-03', sequence: '3', status: 'Seal check', tone: 'warning' as const },
    { hu: 'HU-5108-08', lane: 'PACK-02', sequence: '8', status: 'Missing at stage', tone: 'danger' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-07" aria-labelledby="outbound-out-07-title">
      <Header
        id="OUT-07"
        title="Staging & Loading Control"
        description="Mock kiểm tra HU readiness, staging lane, vehicle, seal và loading sequence trước dispatch."
        icon={<Truck size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Shipment</span><strong>SHP-2026-5108</strong><small>8 HU expected</small></div>
        <div><span>Vehicle</span><strong>51C-882.14</strong><small>Dock D04</small></div>
        <div><span>Seal</span><strong>SEAL-028817</strong><small>Pending verify</small></div>
        <div><span>Ready HU</span><strong>7 / 8</strong><small>1 missing from stage</small></div>
      </div>
      <div className="outbound-mock-table-scroll">
        <table aria-label="Mock staging and loading units">
          <thead><tr><th>HU</th><th>Current lane</th><th>Load sequence</th><th>Status</th></tr></thead>
          <tbody>
            {hus.map(item => (
              <tr key={item.hu}><td><strong>{item.hu}</strong></td><td>{item.lane}</td><td>{item.sequence}</td><td><Badge tone={item.tone}>{item.status}</Badge></td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="outbound-mock-callout danger">
        <strong>Load completeness guard</strong>
        <span>Không được chuyển LOAD_READY/LOADED khi HU bắt buộc chưa ở đúng staging/load context hoặc vehicle/seal không khớp kế hoạch.</span>
      </div>
    </section>
  );
};

const DispatchMock = () => {
  const checks = [
    { label: 'Shipment state', detail: 'LOADED', tone: 'success' as const, status: 'Pass' },
    { label: 'Loaded HU', detail: '8 / 8', tone: 'success' as const, status: 'Pass' },
    { label: 'Inventory version', detail: 'v1842', tone: 'success' as const, status: 'Current' },
    { label: 'Idempotency key', detail: 'dispatch-SHP-5108-v3', tone: 'neutral' as const, status: 'Unique' },
    { label: 'Reservation / Allocation', detail: '120 / 120', tone: 'success' as const, status: 'Consumable' },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-08" aria-labelledby="outbound-out-08-title">
      <Header
        id="OUT-08"
        title="Shipment Dispatch Boundary"
        description="Mock pre-flight checks cho outbound posting. Đây là boundary duy nhất trong outbound flow làm giảm warehouse On Hand."
        icon={<PackageCheck size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Shipment</span><strong>SHP-2026-5108</strong><small>WH-HCM-01</small></div>
        <div><span>Dispatch quantity</span><strong>120 Gói</strong><small>Base quantity reconciled</small></div>
        <div><span>Current On Hand</span><strong>1.842</strong><small>Pre-dispatch snapshot</small></div>
        <div><span>Projected On Hand</span><strong>1.722</strong><small>Only after successful POST</small></div>
      </div>
      <div className="outbound-dispatch-checks">
        {checks.map(check => (
          <article key={check.label}><div><strong>{check.label}</strong><span>{check.detail}</span></div><Badge tone={check.tone}>{check.status}</Badge></article>
        ))}
      </div>
      <div className="outbound-boundary-box">
        <div><span>Before dispatch</span><strong>Reservation + allocation + pick/load state</strong><small>On Hand unchanged</small></div>
        <span className="outbound-boundary-arrow" aria-hidden="true">→</span>
        <div><span>Atomic dispatch</span><strong>Deduct physical stock exactly once</strong><small>Ledger + audit + outbox</small></div>
        <span className="outbound-boundary-arrow" aria-hidden="true">→</span>
        <div><span>After dispatch</span><strong>Reservation/allocation consumed</strong><small>Retry returns same canonical result</small></div>
      </div>
    </section>
  );
};

const BackorderMock = () => {
  const rows = [
    { label: 'Ordered', value: 300, tone: 'neutral' as const },
    { label: 'Reserved', value: 220, tone: 'success' as const },
    { label: 'Allocated', value: 200, tone: 'success' as const },
    { label: 'Picked', value: 180, tone: 'warning' as const },
    { label: 'Shipped', value: 160, tone: 'success' as const },
    { label: 'Backorder', value: 80, tone: 'danger' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-09" aria-labelledby="outbound-out-09-title">
      <Header
        id="OUT-09"
        title="Backorder & Promise Replanning"
        description="Mock quantity waterfall và promise-date replanning để tránh trộn ordered/reserved/allocated/picked/shipped/backorder semantics."
        icon={<Boxes size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Sales order</span><strong>SO-2026-5112</strong><small>Customer CUS-0044</small></div>
        <div><span>Next supply ETA</span><strong>05/10/2026</strong><small>120 Cái expected</small></div>
        <div><span>Backorder</span><strong>80 Cái</strong><small>26.7% of order</small></div>
        <div><span>Promise risk</span><strong>Medium</strong><small>Needs replan</small></div>
      </div>
      <div className="outbound-waterfall" aria-label="Mock outbound quantity waterfall">
        {rows.map(row => (
          <article key={row.label}>
            <div><span>{row.label}</span><strong>{row.value}</strong></div>
            <div className="outbound-waterfall-track"><span className={row.tone} style={{ width: Math.max(12, row.value / 3) + '%' }} /></div>
          </article>
        ))}
      </div>
      <div className="outbound-mock-callout warning">
        <strong>Promise-date guard</strong>
        <span>Replan phải kiểm supply ETA, allocation hiện tại và customer policy; không được tự tạo reservation hay hứa ngày mới vượt available supply.</span>
      </div>
    </section>
  );
};

const TrackingMock = () => {
  const events = [
    { time: '04/10 08:10', title: 'Dispatched', detail: 'WH-HCM-01 • Gate out', tone: 'success' as const },
    { time: '04/10 09:25', title: 'In transit', detail: 'HCM Hub', tone: 'success' as const },
    { time: '04/10 12:40', title: 'Out for delivery', detail: 'Route HCM-07', tone: 'warning' as const },
    { time: '—', title: 'Delivered / POD', detail: 'Pending signature + photo', tone: 'neutral' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-10" aria-labelledby="outbound-out-10-title">
      <Header
        id="OUT-10"
        title="Shipment Tracking / POD Timeline"
        description="Mock logistics tracking sau dispatch, POD và delivery failure/retry mà không tạo thêm outbound inventory movement."
        icon={<Route size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Shipment</span><strong>SHP-2026-5108</strong><small>Already dispatched</small></div>
        <div><span>Carrier</span><strong>CAR-FAST-01</strong><small>TRK-88941002</small></div>
        <div><span>Current node</span><strong>Route HCM-07</strong><small>Out for delivery</small></div>
        <div><span>POD</span><strong>Pending</strong><small>Signature + photo</small></div>
      </div>
      <div className="outbound-tracking-timeline">
        {events.map((event, index) => (
          <article key={event.title}>
            <span className={'outbound-tracking-dot ' + event.tone} aria-hidden="true" />
            <time>{event.time}</time>
            <div><strong>{event.title}</strong><span>{event.detail}</span></div>
            <Badge tone={event.tone}>{index < 3 ? 'Recorded' : 'Pending'}</Badge>
          </article>
        ))}
      </div>
      <div className="outbound-mock-callout warning">
        <strong>Delivery failure semantics</strong>
        <span>Delivery failure chỉ mở retry/return workflow. Stock không được tự cộng lại warehouse cho tới khi return receipt/posting hợp lệ.</span>
      </div>
    </section>
  );
};

const OutboundCapabilityMock = ({ capabilityId }: OutboundMockProps) => {
  if (capabilityId === 'OUT-03') return <AllocationMock />;
  if (capabilityId === 'OUT-04') return <WaveMock />;
  if (capabilityId === 'OUT-07') return <StagingLoadingMock />;
  if (capabilityId === 'OUT-08') return <DispatchMock />;
  if (capabilityId === 'OUT-09') return <BackorderMock />;
  if (capabilityId === 'OUT-10') return <TrackingMock />;
  return null;
};

export default OutboundCapabilityMock;
