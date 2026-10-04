import type { ReactNode } from 'react';
import { EyeOff, LockKeyhole, History, Scale, ShieldCheck } from 'lucide-react';
import './CountAdjustmentCapabilityMock.css';

type Props = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
  <span className={'count-mock-badge ' + tone}>{children}</span>
);

const Header = ({ id, title, description, icon }: { id: string; title: string; description: string; icon: ReactNode }) => (
  <header className="count-mock-header">
    <div className="count-mock-title-row">
      <span className="count-mock-icon" aria-hidden="true">{icon}</span>
      <div>
        <span className="count-mock-kicker">COUNT & ADJUSTMENT • {id} • FRONTEND MOCK</span>
        <h2 id={'count-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="count-mock-scope">
      <Badge tone="neutral">Read-only</Badge>
      <span>Không gọi API production • Không ghi inventory • Không giả approval/posting</span>
    </div>
  </header>
);

const BlindCountMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-03" aria-labelledby="count-ct-03-title">
    <Header id="CT-03" title="Blind Count Execution" description="Counter chỉ thấy scope và stock identity cần đếm; system quantity bị ẩn để giảm bias." icon={<EyeOff size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Count</span><strong>CNT-2026-0098</strong><small>Attempt #1</small></div>
      <div><span>Location</span><strong>A01-R02-L03-B04</strong><small>WH-HCM-01</small></div>
      <div><span>System quantity</span><strong>••••</strong><small>Ẩn với counter</small></div>
      <div><span>Observed</span><strong>3 Thùng</strong><small>36 Cái base UOM</small></div>
    </div>
    <div className="count-blind-panel">
      <div><span>Stock identity</span><strong>SKU-1001 • LOT-261004-A</strong><small>UOM thao tác: Thùng • factor 12</small></div>
      <div><span>Counter instruction</span><strong>Đếm thực tế, không suy đoán theo hệ thống</strong><small>Scan location → scan SKU/lot → nhập observed qty</small></div>
    </div>
    <div className="count-mock-callout warning"><strong>Bias guard</strong><span>System quantity không được lộ qua helper text, placeholder, tooltip hay error message khi blind-count policy đang bật.</span></div>
  </section>
);

const FreezeStrategyMock = () => {
  const rows = [
    { strategy:'HARD_FREEZE', movement:'Chặn', picking:'Chặn', snapshot:'Có', status:'Strict', tone:'danger' as const },
    { strategy:'SOFT_FREEZE', movement:'Có kiểm soát', picking:'Có điều kiện', snapshot:'Có', status:'Recommended', tone:'success' as const },
    { strategy:'SNAPSHOT_ONLY', movement:'Cho phép', picking:'Cho phép', snapshot:'Có', status:'Low disruption', tone:'neutral' as const },
  ];
  return (
    <section className="count-capability-mock" data-testid="count-capability-mock-CT-04" aria-labelledby="count-ct-04-title">
      <Header id="CT-04" title="Count Freeze Strategy" description="So sánh Hard Freeze, Soft Freeze và Snapshot-only trước khi release count scope." icon={<LockKeyhole size={20} />} />
      <div className="count-mock-metrics">
        <div><span>Count</span><strong>CNT-2026-0098</strong><small>Zone PICK-A</small></div>
        <div><span>Open tasks</span><strong>7</strong><small>2 đang execution</small></div>
        <div><span>Selected policy</span><strong>SOFT_FREEZE</strong><small>Warehouse profile</small></div>
        <div><span>Snapshot</span><strong>16:10:24</strong><small>Versioned baseline</small></div>
      </div>
      <div className="count-mock-table-scroll"><table aria-label="Mock count freeze strategy comparison"><thead><tr><th>Strategy</th><th>Movement</th><th>Picking</th><th>Snapshot</th><th>Fit</th></tr></thead><tbody>
        {rows.map(r=><tr key={r.strategy}><td><strong>{r.strategy}</strong></td><td>{r.movement}</td><td>{r.picking}</td><td>{r.snapshot}</td><td><Badge tone={r.tone}>{r.status}</Badge></td></tr>)}
      </tbody></table></div>
      <div className="count-mock-callout warning"><strong>Conflict check</strong><span>Open work, inventory locks và active transfers trong scope phải được đánh giá trước khi áp freeze; không silently cancel task.</span></div>
    </section>
  );
};

const RecountMock = () => {
  const attempts = [
    { attempt:'#1', counter:'U-COUNTER-01', observed:'41 Cái', result:'Variance -7', tone:'warning' as const, state:'Submitted' },
    { attempt:'#2', counter:'U-COUNTER-02', observed:'43 Cái', result:'Variance -5', tone:'warning' as const, state:'Submitted' },
    { attempt:'Final', counter:'Supervisor', observed:'43 Cái', result:'Accepted', tone:'success' as const, state:'Final' },
  ];
  return (
    <section className="count-capability-mock" data-testid="count-capability-mock-CT-05" aria-labelledby="count-ct-05-title">
      <Header id="CT-05" title="Recount & Immutable Attempt History" description="Mỗi lần đếm là một attempt bất biến; final accepted count không overwrite lịch sử trước đó." icon={<History size={20} />} />
      <div className="count-attempt-list">
        {attempts.map(a=><article key={a.attempt}><div><span>{a.attempt}</span><strong>{a.observed}</strong><small>{a.counter}</small></div><div><span>{a.result}</span><Badge tone={a.tone}>{a.state}</Badge></div></article>)}
      </div>
      <div className="count-mock-callout warning"><strong>Recount policy</strong><span>Giữ actor, timestamp, observed quantity và reason cho từng attempt; không sửa attempt cũ để “khớp” final result.</span></div>
    </section>
  );
};

const VarianceMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-06" aria-labelledby="count-ct-06-title">
    <Header id="CT-06" title="Variance Resolution Workbench" description="Review snapshot vs final count, evidence và threshold để route sang recount, approval hoặc adjustment." icon={<Scale size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Snapshot</span><strong>48 Cái</strong><small>Freeze baseline</small></div>
      <div><span>Final count</span><strong>43 Cái</strong><small>Accepted recount</small></div>
      <div><span>Variance</span><strong>-5 Cái</strong><small>-10,42%</small></div>
      <div><span>Threshold</span><strong>±5%</strong><small>Approval required</small></div>
    </div>
    <div className="count-resolution-flow">
      <article><span>1</span><div><strong>Evidence review</strong><small>2 attempts • photo • movement trace</small></div><Badge tone="success">Complete</Badge></article>
      <article><span>2</span><div><strong>Threshold check</strong><small>|10,42%| &gt; 5%</small></div><Badge tone="danger">Exceeded</Badge></article>
      <article><span>3</span><div><strong>Decision</strong><small>Recount no longer required</small></div><Badge tone="warning">Approval</Badge></article>
      <article><span>4</span><div><strong>Adjustment draft</strong><small>Signed delta = -5</small></div><Badge tone="neutral">Not posted</Badge></article>
    </div>
  </section>
);

const AdjustmentMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-07" aria-labelledby="count-ct-07-title">
    <Header id="CT-07" title="Inventory Adjustment Approval & Posting" description="Mock segregation-of-duties, signed delta và posting boundary cho adjustment do count variance." icon={<ShieldCheck size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Adjustment</span><strong>ADJ-2026-0077</strong><small>COUNT_VARIANCE</small></div>
      <div><span>Signed delta</span><strong>-5 Cái</strong><small>SKU-1001 • B04</small></div>
      <div><span>Creator</span><strong>U-COUNTER-01</strong><small>Cannot self-approve</small></div>
      <div><span>Approver</span><strong>U-SUPERVISOR</strong><small>Different actor</small></div>
    </div>
    <div className="count-adjustment-chain" aria-label="Mock inventory adjustment state chain">
      <div><Badge tone="neutral">DRAFT</Badge><span>Reason + evidence attached</span></div>
      <div><Badge tone="warning">SUBMITTED</Badge><span>SoD / threshold validation</span></div>
      <div><Badge tone="success">APPROVED</Badge><span>Authorized approver only</span></div>
      <div><Badge tone="danger">POST</Badge><span>Only boundary that changes inventory</span></div>
    </div>
    <div className="count-mock-callout danger"><strong>Posting boundary</strong><span>Approve không đổi On Hand. Chỉ POST adjustment mới tạo immutable inventory movement; retry phải idempotent và audit được.</span></div>
  </section>
);

const CountAdjustmentCapabilityMock = ({ capabilityId }: Props) => {
  if (capabilityId === 'CT-03') return <BlindCountMock />;
  if (capabilityId === 'CT-04') return <FreezeStrategyMock />;
  if (capabilityId === 'CT-05') return <RecountMock />;
  if (capabilityId === 'CT-06') return <VarianceMock />;
  if (capabilityId === 'CT-07') return <AdjustmentMock />;
  return null;
};

export default CountAdjustmentCapabilityMock;
