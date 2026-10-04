import type { ReactNode } from 'react';
import { Ban, GitBranch, History, LockKeyhole, ScanLine } from 'lucide-react';
import './InventoryCapabilityMock.css';

type InventoryMockProps = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
  <span className={'inventory-mock-badge ' + tone}>{children}</span>
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
  <header className="inventory-mock-header">
    <div className="inventory-mock-title-row">
      <span className="inventory-mock-icon" aria-hidden="true">{icon}</span>
      <div>
        <span className="inventory-mock-kicker">INVENTORY CONTROL • {id} • FRONTEND MOCK</span>
        <h2 id={'inventory-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="inventory-mock-scope">
      <Badge tone="neutral">Read-only</Badge>
      <span>Không gọi API production • Không sửa ledger • Không ghi inventory</span>
    </div>
  </header>
);

const StatusControlMock = () => {
  const statuses = [
    { name: 'AVAILABLE', qty: '1.240', eligible: 'Reserve / Allocate / Pick', tone: 'success' as const },
    { name: 'QC_HOLD', qty: '96', eligible: 'No reserve / no pick', tone: 'warning' as const },
    { name: 'QUARANTINE', qty: '28', eligible: 'Blocked by policy', tone: 'warning' as const },
    { name: 'DAMAGED', qty: '14', eligible: 'Disposition required', tone: 'danger' as const },
    { name: 'EXPIRED', qty: '6', eligible: 'Not eligible', tone: 'danger' as const },
    { name: 'RECALL_BLOCKED', qty: '0', eligible: 'Recall scope only', tone: 'neutral' as const },
  ];

  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-05" aria-labelledby="inventory-inv-05-title">
      <Header
        id="INV-05"
        title="Inventory Status Eligibility Board"
        description="Mock trạng thái inventory và operational eligibility; status thay đổi phải có permission, reason, evidence và lock checks."
        icon={<Ban size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>WH-HCM-01</small></div>
        <div><span>Total On Hand</span><strong>1.384 Cái</strong><small>Across statuses</small></div>
        <div><span>Operationally eligible</span><strong>1.240 Cái</strong><small>AVAILABLE only</small></div>
        <div><span>Restricted</span><strong>144 Cái</strong><small>Hold / damage / expiry</small></div>
      </div>
      <div className="inventory-status-grid">
        {statuses.map(item => (
          <article key={item.name}>
            <div><strong>{item.name}</strong><Badge tone={item.tone}>{item.qty} Cái</Badge></div>
            <span>{item.eligible}</span>
          </article>
        ))}
      </div>
      <div className="inventory-mock-callout warning">
        <strong>Status transition guard</strong>
        <span>Không cho release/block/hold khi thiếu permission, reason/evidence hoặc inventory đang bị lock bởi count/incident/recall policy.</span>
      </div>
    </section>
  );
};

const LotSerialMock = () => {
  const identities = [
    { id: 'LOT-1001-260930', type: 'Lot', expiry: '30/09/2027', status: 'AVAILABLE', location: '3 bins', tone: 'success' as const },
    { id: 'SN-1001-884201', type: 'Serial', expiry: '—', status: 'AVAILABLE', location: 'A01-R02-L03-B04', tone: 'success' as const },
    { id: 'SN-1001-884202', type: 'Serial', expiry: '—', status: 'QC_HOLD', location: 'QC-01', tone: 'warning' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-06" aria-labelledby="inventory-inv-06-title">
      <Header
        id="INV-06"
        title="Lot / Serial / Expiry Explorer"
        description="Mock identity search, expiry eligibility và duplicate protection cho lot/serial-tracked inventory."
        icon={<ScanLine size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>Lot tracked</small></div>
        <div><span>Selected lot</span><strong>LOT-1001-260930</strong><small>Expiry 30/09/2027</small></div>
        <div><span>Locations</span><strong>3</strong><small>420 Cái total</small></div>
        <div><span>FEFO rank</span><strong>#1</strong><small>Among eligible lots</small></div>
      </div>
      <div className="inventory-mock-table-scroll">
        <table aria-label="Mock lot serial expiry identities">
          <thead><tr><th>Identity</th><th>Type</th><th>Expiry</th><th>Status</th><th>Location</th></tr></thead>
          <tbody>
            {identities.map(item => (
              <tr key={item.id}><td><strong>{item.id}</strong></td><td>{item.type}</td><td>{item.expiry}</td><td><Badge tone={item.tone}>{item.status}</Badge></td><td>{item.location}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="inventory-mock-callout danger">
        <strong>Duplicate identity guard</strong>
        <span>Serial đã tồn tại không được tạo lại; lot/expiry phải tuân tracking policy và không được dùng identity lookup để bypass status eligibility.</span>
      </div>
    </section>
  );
};

const LockFreezeMock = () => {
  const affected = [
    { item: 'Zone PICK-A', impact: 'Reservation / allocation checks', status: 'Soft freeze', tone: 'warning' as const },
    { item: '4 open pick tasks', impact: 'Cho complete task đang chạy; chặn task mới', status: 'Grandfathered', tone: 'neutral' as const },
    { item: 'Cycle count CT-1044', impact: 'Snapshot 09:00', status: 'Owner', tone: 'success' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-07" aria-labelledby="inventory-inv-07-title">
      <Header
        id="INV-07"
        title="Inventory Lock / Freeze Policy"
        description="Mock lock scope và freeze strategy để bảo vệ count/incident work mà không gây mutation mơ hồ."
        icon={<LockKeyhole size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Scope</span><strong>WH-HCM-01 / PICK-A</strong><small>Zone level</small></div>
        <div><span>Strategy</span><strong>SOFT_FREEZE</strong><small>Policy version 3</small></div>
        <div><span>Reason</span><strong>CYCLE_COUNT</strong><small>CT-1044</small></div>
        <div><span>Expires</span><strong>11:00</strong><small>04/10/2026</small></div>
      </div>
      <div className="inventory-lock-list">
        {affected.map(item => (
          <article key={item.item}>
            <div><strong>{item.item}</strong><span>{item.impact}</span></div>
            <Badge tone={item.tone}>{item.status}</Badge>
          </article>
        ))}
      </div>
      <div className="inventory-lock-strategies">
        <div><strong>HARD_FREEZE</strong><span>Chặn mọi inventory mutation trong scope trừ explicit recovery command.</span></div>
        <div><strong>SOFT_FREEZE</strong><span>Cho phép work đang active theo policy; chặn mutation mới không hợp lệ.</span></div>
        <div><strong>SNAPSHOT_ONLY</strong><span>Không block; variance engine phải xử lý movement sau snapshot.</span></div>
      </div>
    </section>
  );
};

const ReversalMock = () => {
  const chain = [
    { ref: 'LED-2026-882177', label: 'Original MOVE', delta: 'SRC -40 / DST +40', tone: 'neutral' as const, state: 'POSTED' },
    { ref: 'REV-2026-0108', label: 'Reversal', delta: 'SRC +40 / DST -40', tone: 'warning' as const, state: 'PROPOSED' },
    { ref: 'COR-2026-0109', label: 'Corrected MOVE', delta: 'SRC -40 / DST2 +40', tone: 'success' as const, state: 'NEXT' },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-09" aria-labelledby="inventory-inv-09-title">
      <Header
        id="INV-09"
        title="Inventory Reversal / Corrective Chain"
        description="Mock sửa sai bằng reversal và corrected transaction; ledger gốc luôn bất biến và giữ nguyên source/correlation."
        icon={<History size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Original txn</span><strong>LED-2026-882177</strong><small>MOVE • POSTED</small></div>
        <div><span>Reason</span><strong>WRONG_DESTINATION</strong><small>Evidence attached</small></div>
        <div><span>Correlation</span><strong>CORR-9A10F</strong><small>Original + reversal + correction</small></div>
        <div><span>Original ledger</span><strong>Immutable</strong><small>No edit / delete</small></div>
      </div>
      <div className="inventory-reversal-chain">
        {chain.map((item,index) => (
          <article key={item.ref}>
            <span className="inventory-chain-index">{index + 1}</span>
            <div><strong>{item.ref}</strong><span>{item.label}</span><small>{item.delta}</small></div>
            <Badge tone={item.tone}>{item.state}</Badge>
          </article>
        ))}
      </div>
      <div className="inventory-mock-callout warning">
        <strong>Reversibility guard</strong>
        <span>Transaction đã reversal hoặc có downstream dependency không thể đảo an toàn phải bị chặn và route sang controlled correction workflow.</span>
      </div>
    </section>
  );
};

const TraceabilityMock = () => {
  const nodes = [
    { ref: 'PO-2026-8831', type: 'Purchase Order', detail: 'Supplier SUP-0008', tone: 'neutral' as const },
    { ref: 'GR-2026-1045', type: 'Goods Receipt', detail: '+600 Cái', tone: 'success' as const },
    { ref: 'QC-2026-211', type: 'QC', detail: '590 accepted • 10 damaged', tone: 'warning' as const },
    { ref: 'MOV-2026-773', type: 'Internal Move', detail: 'RECEIVING → PICK-A', tone: 'neutral' as const },
    { ref: 'SHP-2026-5108', type: 'Shipment', detail: '180 Cái • 12 customers', tone: 'success' as const },
    { ref: 'RMA-2026-044', type: 'Return', detail: '2 open RMA', tone: 'warning' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-10" aria-labelledby="inventory-inv-10-title">
      <Header
        id="INV-10"
        title="Traceability & Genealogy Graph"
        description="Mock backward/forward trace theo lot/serial/document, giữ exposure và missing-link state rõ ràng."
        icon={<GitBranch size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Lot</span><strong>LOT-1001-260930</strong><small>SKU-1001</small></div>
        <div><span>Current stock</span><strong>420 Cái</strong><small>3 locations</small></div>
        <div><span>Shipped exposure</span><strong>180 Cái</strong><small>12 customers</small></div>
        <div><span>Open returns</span><strong>2</strong><small>RMA branches</small></div>
      </div>
      <div className="inventory-genealogy" aria-label="Mock genealogy trace">
        {nodes.map((node,index) => (
          <article key={node.ref}>
            <span className={'inventory-genealogy-node ' + node.tone} aria-hidden="true" />
            <div><strong>{node.ref}</strong><span>{node.type}</span><small>{node.detail}</small></div>
            {index < nodes.length - 1 ? <span className="inventory-genealogy-link" aria-hidden="true">→</span> : null}
          </article>
        ))}
      </div>
      <div className="inventory-mock-callout danger">
        <strong>Incomplete-chain guard</strong>
        <span>Nếu một movement thiếu source/correlation linkage, UI phải hiển thị trace incomplete; không được tự suy đoán genealogy hoàn chỉnh cho recall/exposure.</span>
      </div>
    </section>
  );
};

const InventoryCapabilityMock = ({ capabilityId }: InventoryMockProps) => {
  if (capabilityId === 'INV-05') return <StatusControlMock />;
  if (capabilityId === 'INV-06') return <LotSerialMock />;
  if (capabilityId === 'INV-07') return <LockFreezeMock />;
  if (capabilityId === 'INV-09') return <ReversalMock />;
  if (capabilityId === 'INV-10') return <TraceabilityMock />;
  return null;
};

export default InventoryCapabilityMock;
