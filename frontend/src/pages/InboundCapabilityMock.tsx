import type { ReactNode } from 'react';
import { CalendarClock, ClipboardList, MapPin, Scale, ShieldCheck } from 'lucide-react';
import './InboundCapabilityMock.css';

type InboundMockProps = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: string }) => (
  <span className={'inbound-mock-badge ' + tone}>{children}</span>
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
  <header className="inbound-mock-header">
    <div className="inbound-mock-title-row">
      <span className="inbound-mock-icon" aria-hidden="true">{icon}</span>
      <div>
        <span className="inbound-mock-kicker">INBOUND • {id} • FRONTEND MOCK</span>
        <h2 id={'inbound-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="inbound-mock-scope">
      <Badge tone="neutral">Read-only</Badge>
      <span>Không gọi API production • Không ghi inventory • Không giả mutation thành công</span>
    </div>
  </header>
);

const PurchaseOrderAsnMock = () => {
  const lines = [
    { sku: 'SKU-1001', po: '80 Thùng', asn: '80 Thùng', delta: '0', status: 'Khớp', tone: 'success' as const },
    { sku: 'SKU-1002', po: '24 Thùng', asn: '22 Thùng', delta: '-2', status: 'Thiếu dự kiến', tone: 'warning' as const },
    { sku: 'SKU-2001', po: '16 Thùng', asn: '18 Thùng', delta: '+2', status: 'Vượt PO', tone: 'danger' as const },
  ];

  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-01" aria-labelledby="inbound-in-01-title">
      <Header
        id="IN-01"
        title="Purchase Order / ASN Reconciliation"
        description="Mô phỏng expected inbound từ PO và ASN, tách rõ dữ liệu dự kiến khỏi Receipt và On Hand."
        icon={<ClipboardList size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>PO</span><strong>PO-2026-8831</strong><small>Supplier SUP-0008</small></div>
        <div><span>ASN</span><strong>ASN-2026-4172</strong><small>ETA 16:30 • 04/10</small></div>
        <div><span>Expected</span><strong>120 Thùng</strong><small>3 SKU</small></div>
        <div><span>Inventory effect</span><strong>0</strong><small>Chỉ tăng khi Receipt POST</small></div>
      </div>
      <div className="inbound-mock-table-scroll">
        <table aria-label="Mock PO ASN reconciliation">
          <thead><tr><th>SKU</th><th>PO quantity</th><th>ASN quantity</th><th>Delta</th><th>Validation</th></tr></thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.sku}>
                <td><strong>{line.sku}</strong></td><td>{line.po}</td><td>{line.asn}</td><td>{line.delta}</td>
                <td><Badge tone={line.tone}>{line.status}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="inbound-mock-callout warning">
        <strong>Expected ≠ Received</strong>
        <span>PO/ASN chỉ tạo kỳ vọng nhận hàng. Không được dùng ASN arrival để tăng On Hand hoặc bỏ qua receiving/QC/posting boundary.</span>
      </div>
    </section>
  );
};

const AppointmentMock = () => {
  const appointments = [
    { time: '08:30–09:00', ref: 'APT-1048', vehicle: '51D-482.16', dock: 'D01', status: 'Checked in', tone: 'success' as const },
    { time: '09:10–09:40', ref: 'APT-1052', vehicle: '43C-218.08', dock: 'D02', status: 'Arrived', tone: 'warning' as const },
    { time: '09:40–10:10', ref: 'APT-1058', vehicle: '51C-778.21', dock: 'D03', status: 'Confirmed', tone: 'neutral' as const },
    { time: '10:00–10:30', ref: 'APT-1060', vehicle: '60C-113.84', dock: 'D03', status: 'Dock conflict', tone: 'danger' as const },
  ];

  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-02" aria-labelledby="inbound-in-02-title">
      <Header
        id="IN-02"
        title="Receiving Appointment Board"
        description="Mock lịch xe vào kho với arrival, gate check-in và dock assignment là các trạng thái riêng, có conflict/no-show rõ ràng."
        icon={<CalendarClock size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Window</span><strong>08:00–12:00</strong><small>WH-HCM-01</small></div>
        <div><span>Appointments</span><strong>4</strong><small>3 valid • 1 conflict</small></div>
        <div><span>Dock utilization</span><strong>67%</strong><small>2 / 3 đang dùng</small></div>
        <div><span>Late / no-show</span><strong>1</strong><small>Requires triage</small></div>
      </div>
      <div className="inbound-appointment-list" aria-label="Mock receiving appointment timeline">
        {appointments.map((item) => (
          <article key={item.ref}>
            <time>{item.time}</time>
            <div><strong>{item.ref}</strong><span>{item.vehicle} • Dock {item.dock}</span></div>
            <Badge tone={item.tone}>{item.status}</Badge>
          </article>
        ))}
      </div>
      <div className="inbound-mock-callout danger">
        <strong>Dock collision guard</strong>
        <span>D03 không thể nhận hai appointment chồng window nếu capacity chỉ là một vehicle. Conflict phải được re-plan, không tự ghi đè assignment.</span>
      </div>
    </section>
  );
};

const DiscrepancyMock = () => {
  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-05" aria-labelledby="inbound-in-05-title">
      <Header
        id="IN-05"
        title="Over / Under Receipt Resolution"
        description="Mock xử lý chênh lệch expected/observed theo tolerance, reason và approval boundary trước khi tiếp tục flow."
        icon={<Scale size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Expected</span><strong>100 Thùng</strong><small>PO / ASN snapshot</small></div>
        <div><span>Observed</span><strong>108 Thùng</strong><small>Receiving count</small></div>
        <div><span>Variance</span><strong>+8%</strong><small>+8 Thùng</small></div>
        <div><span>Tolerance</span><strong>±2%</strong><small>Approval required</small></div>
      </div>
      <div className="inbound-discrepancy-grid">
        <article>
          <span>1 • Detect</span><strong>OVER_RECEIPT</strong><p>Observed quantity vượt expected snapshot.</p><Badge tone="danger">Outside tolerance</Badge>
        </article>
        <article>
          <span>2 • Reason</span><strong>SUPPLIER_OVER_SHIP</strong><p>Gắn reason + note/evidence bắt buộc.</p><Badge tone="warning">Evidence needed</Badge>
        </article>
        <article>
          <span>3 • Approval</span><strong>PENDING_APPROVAL</strong><p>Không resolve tự động khi vượt threshold.</p><Badge tone="warning">Manager review</Badge>
        </article>
        <article>
          <span>4 • Resolution</span><strong>Accept / Reject excess</strong><p>Result phải reconcile với received quantity.</p><Badge tone="neutral">Not decided</Badge>
        </article>
      </div>
      <div className="inbound-mock-callout warning">
        <strong>Posting guard</strong>
        <span>Chênh lệch vượt tolerance phải có resolution hợp lệ trước boundary tiếp theo; mock không thay đổi Receipt hoặc Inventory.</span>
      </div>
    </section>
  );
};

const QcMock = () => {
  const disposition = [
    { bucket: 'Accepted', qty: 90, tone: 'success' as const },
    { bucket: 'Damaged', qty: 4, tone: 'warning' as const },
    { bucket: 'Rejected', qty: 2, tone: 'danger' as const },
  ];
  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-06" aria-labelledby="inbound-in-06-title">
      <Header
        id="IN-06"
        title="Inbound QC Inspection"
        description="Mock inspection checklist, evidence và disposition; tổng Accepted + Damaged + Rejected phải cân bằng Received."
        icon={<ShieldCheck size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Receipt</span><strong>GR-2026-1045</strong><small>SKU-1001</small></div>
        <div><span>Received</span><strong>96 Cái</strong><small>QC required</small></div>
        <div><span>Evidence</span><strong>3 items</strong><small>2 ảnh • 1 checklist</small></div>
        <div><span>Balance</span><strong>96 / 96</strong><small>Disposition balanced</small></div>
      </div>
      <div className="inbound-qc-layout">
        <div>
          <h3>Inspection criteria</h3>
          <ul>
            <li><span aria-hidden="true">✓</span><strong>Packaging integrity</strong><small>Pass • sample 12/12</small></li>
            <li><span aria-hidden="true">✓</span><strong>Label / lot match</strong><small>Pass • LOT-261004-A</small></li>
            <li><span aria-hidden="true">!</span><strong>Visual damage</strong><small>6 units require disposition</small></li>
          </ul>
        </div>
        <div>
          <h3>Disposition</h3>
          {disposition.map((item) => (
            <div className="inbound-disposition-row" key={item.bucket}>
              <span>{item.bucket}</span><strong>{item.qty} Cái</strong><Badge tone={item.tone}>{item.bucket}</Badge>
            </div>
          ))}
        </div>
      </div>
      <div className="inbound-mock-callout warning">
        <strong>QC balance rule</strong>
        <span>90 + 4 + 2 = 96. Nếu disposition không cân bằng Received thì READY_TO_POST phải bị chặn.</span>
      </div>
    </section>
  );
};

const PutawayRuleMock = () => {
  const candidates = [
    { code: 'A01-R02-L03-B04', compatibility: 'Pass', capacity: 'Tight', distance: '18 m', score: '92', result: 'Recommended', tone: 'success' as const },
    { code: 'A02-R01-L01-B02', compatibility: 'Pass', capacity: 'Pass', distance: '32 m', score: '78', result: 'Eligible', tone: 'neutral' as const },
    { code: 'B01-R03-L02-B07', compatibility: 'Fail', capacity: 'Pass', distance: '21 m', score: '—', result: 'Rejected', tone: 'danger' as const },
  ];

  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-09" aria-labelledby="inbound-in-09-title">
      <Header
        id="IN-09"
        title="Putaway Recommendation Explainability"
        description="Mock candidate filtering + ranking theo compatibility, status, capacity và travel cost; recommendation luôn hiển thị lý do."
        icon={<MapPin size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>Food • Ambient</small></div>
        <div><span>Source</span><strong>RECEIVING-01</strong><small>WH-HCM-01</small></div>
        <div><span>Quantity</span><strong>20 Thùng</strong><small>240 Cái base UOM</small></div>
        <div><span>Recommended</span><strong>B04</strong><small>Score 92 / 100</small></div>
      </div>
      <div className="inbound-mock-table-scroll">
        <table aria-label="Mock putaway candidates">
          <thead><tr><th>Candidate</th><th>Compatibility</th><th>Capacity</th><th>Travel</th><th>Score</th><th>Result</th></tr></thead>
          <tbody>
            {candidates.map((item) => (
              <tr key={item.code}>
                <td><strong>{item.code}</strong></td><td>{item.compatibility}</td><td>{item.capacity}</td><td>{item.distance}</td><td>{item.score}</td>
                <td><Badge tone={item.tone}>{item.result}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="inbound-recommendation-reason">
        <strong>Vì sao chọn B04?</strong>
        <span>Cùng storage profile + đúng status + đủ capacity sau projected load + gần source nhất trong nhóm score cao.</span>
      </div>
    </section>
  );
};

const InboundCapabilityMock = ({ capabilityId }: InboundMockProps) => {
  if (capabilityId === 'IN-01') return <PurchaseOrderAsnMock />;
  if (capabilityId === 'IN-02') return <AppointmentMock />;
  if (capabilityId === 'IN-05') return <DiscrepancyMock />;
  if (capabilityId === 'IN-06') return <QcMock />;
  if (capabilityId === 'IN-09') return <PutawayRuleMock />;
  return null;
};

export default InboundCapabilityMock;
