import type { ReactNode } from 'react';
import { CalendarDays, Gauge, MapPinned, ShieldAlert, Truck } from 'lucide-react';
import './WarehouseCapabilityMock.css';

type WarehouseMockProps = {
  capabilityId?: string;
};

type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const StatusBadge = ({ tone, children }: { tone: Tone; children: string }) => (
  <span className={'warehouse-mock-badge ' + tone}>{children}</span>
);

const MockHeader = ({
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
  <header className="warehouse-mock-header">
    <div className="warehouse-mock-title-row">
      <span className="warehouse-mock-icon" aria-hidden="true">{icon}</span>
      <div>
        <span className="warehouse-mock-kicker">KHO & VỊ TRÍ • {id} • FRONTEND MOCK</span>
        <h2 id={'warehouse-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="warehouse-mock-scope">
      <StatusBadge tone="neutral">Read-only</StatusBadge>
      <span>Không gọi API production • Không ghi database • Không thay đổi inventory</span>
    </div>
  </header>
);

const CapacityPreview = () => {
  const locations = [
    { code: 'A01-R02-L03-B04', weight: '1.260 / 1.500 kg', volume: '8,2 / 10 m³', pallet: '4 / 5', rule: 'Ambient • Food-safe', tone: 'warning' as const, status: 'Gần đầy' },
    { code: 'A01-R02-L03-B05', weight: '620 / 1.500 kg', volume: '4,1 / 10 m³', pallet: '2 / 5', rule: 'Ambient • Food-safe', tone: 'success' as const, status: 'Có thể cất' },
    { code: 'A01-R02-L03-B08', weight: '0 / 800 kg', volume: '0 / 6 m³', pallet: '0 / 3', rule: 'Damaged only', tone: 'danger' as const, status: 'Không tương thích' },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-03" aria-labelledby="warehouse-wh-03-title">
      <MockHeader
        id="WH-03"
        title="Sức chứa vị trí & Storage Constraints"
        description="Mô phỏng cách WMS kiểm tra weight, volume, pallet capacity và compatibility trước putaway hoặc internal move."
        icon={<Gauge size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Incoming load</span><strong>240 kg</strong><small>1 pallet • 1,6 m³</small></div>
        <div><span>Candidate locations</span><strong>3</strong><small>2 eligible • 1 rejected</small></div>
        <div><span>Best candidate</span><strong>B05</strong><small>41% volume used</small></div>
        <div><span>Constraint profile</span><strong>FOOD-AMBIENT</strong><small>Version 4 • mock</small></div>
      </div>

      <div className="warehouse-capacity-summary">
        <div>
          <div className="warehouse-capacity-label"><span>B04 projected weight</span><strong>100%</strong></div>
          <div className="warehouse-capacity-track" role="progressbar" aria-label="B04 projected weight usage" aria-valuemin={0} aria-valuemax={100} aria-valuenow={100}>
            <span className="danger" style={{ width: '100%' }} />
          </div>
          <small>1.260 + 240 = 1.500 kg • chạm giới hạn</small>
        </div>
        <div>
          <div className="warehouse-capacity-label"><span>B05 projected weight</span><strong>57%</strong></div>
          <div className="warehouse-capacity-track" role="progressbar" aria-label="B05 projected weight usage" aria-valuemin={0} aria-valuemax={100} aria-valuenow={57}>
            <span className="success" style={{ width: '57%' }} />
          </div>
          <small>620 + 240 = 860 / 1.500 kg • còn headroom</small>
        </div>
      </div>

      <div className="warehouse-mock-table-scroll">
        <table aria-label="Mock location capacity candidates">
          <thead><tr><th>Vị trí</th><th>Weight</th><th>Volume</th><th>Pallet</th><th>Storage rule</th><th>Kết quả</th></tr></thead>
          <tbody>
            {locations.map((location) => (
              <tr key={location.code}>
                <td><strong>{location.code}</strong></td>
                <td>{location.weight}</td>
                <td>{location.volume}</td>
                <td>{location.pallet}</td>
                <td>{location.rule}</td>
                <td><StatusBadge tone={location.tone}>{location.status}</StatusBadge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
};

const HeatmapPreview = () => {
  const cells = [
    { code: 'A01', utilization: 92, status: 'Critical', tone: 'danger' as const, detail: 'Congestion • 14 tasks' },
    { code: 'A02', utilization: 78, status: 'Busy', tone: 'warning' as const, detail: '8 tasks' },
    { code: 'A03', utilization: 54, status: 'Normal', tone: 'success' as const, detail: '4 tasks' },
    { code: 'B01', utilization: 68, status: 'Normal', tone: 'success' as const, detail: '5 tasks' },
    { code: 'B02', utilization: 84, status: 'Busy', tone: 'warning' as const, detail: '9 tasks' },
    { code: 'B03', utilization: 37, status: 'Normal', tone: 'success' as const, detail: '2 tasks' },
    { code: 'QC', utilization: 61, status: 'Attention', tone: 'warning' as const, detail: '6 HU waiting' },
    { code: 'STAGE', utilization: 88, status: 'Critical', tone: 'danger' as const, detail: 'Dock cutoff 45m' },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-04" aria-labelledby="warehouse-wh-04-title">
      <MockHeader
        id="WH-04"
        title="Bản đồ kho & Heatmap"
        description="Mock spatial overview để xem utilization, congestion và operational pressure theo zone/aisle mà không biến snapshot thành inventory truth."
        icon={<MapPinned size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Warehouse</span><strong>WH-HCM-01</strong><small>DC Hồ Chí Minh</small></div>
        <div><span>Snapshot age</span><strong>2 phút</strong><small>Mock freshness indicator</small></div>
        <div><span>Critical areas</span><strong>2</strong><small>A01 • STAGE</small></div>
        <div><span>Average utilization</span><strong>70%</strong><small>8 operational areas</small></div>
      </div>

      <div className="warehouse-heatmap" aria-label="Mock warehouse utilization heatmap">
        {cells.map((cell) => (
          <article key={cell.code} className={'warehouse-heatmap-cell ' + cell.tone}>
            <div><strong>{cell.code}</strong><span>{cell.utilization}% sử dụng</span></div>
            <StatusBadge tone={cell.tone}>{cell.status}</StatusBadge>
            <small>{cell.detail}</small>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-callout warning">
        <strong>Stale-data guard</strong>
        <span>Heatmap quá tuổi freshness phải yêu cầu refresh trước khi dùng để điều phối task hoặc đánh giá capacity.</span>
      </div>
    </section>
  );
};

const CalendarPreview = () => {
  const shifts = [
    { name: 'Ca sáng', time: '06:00–14:00', labor: '38 / 42 người', dock: '18 / 24 dock-hours', status: 'Đang chạy', tone: 'success' as const },
    { name: 'Ca chiều', time: '14:00–22:00', labor: '31 / 40 người', dock: '21 / 24 dock-hours', status: 'Sắp đầy', tone: 'warning' as const },
    { name: 'Ca đêm', time: '22:00–06:00', labor: '18 / 24 người', dock: '10 / 16 dock-hours', status: 'Đã lập kế hoạch', tone: 'neutral' as const },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-05" aria-labelledby="warehouse-wh-05-title">
      <MockHeader
        id="WH-05"
        title="Lịch vận hành & Ca làm việc"
        description="Mô phỏng warehouse timezone, giờ mở cửa, ca, cutoff và capacity theo ngày vận hành."
        icon={<CalendarDays size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Timezone</span><strong>Asia/Ho_Chi_Minh</strong><small>UTC+07:00</small></div>
        <div><span>Ngày vận hành</span><strong>04/10/2026</strong><small>Chủ nhật</small></div>
        <div><span>Receiving cutoff</span><strong>20:30</strong><small>Appointment check-in</small></div>
        <div><span>Dispatch cutoff</span><strong>21:15</strong><small>Carrier handoff</small></div>
      </div>

      <div className="warehouse-shift-grid">
        {shifts.map((shift) => (
          <article key={shift.name}>
            <div className="warehouse-shift-heading"><div><strong>{shift.name}</strong><span>{shift.time}</span></div><StatusBadge tone={shift.tone}>{shift.status}</StatusBadge></div>
            <dl>
              <div><dt>Labor</dt><dd>{shift.labor}</dd></div>
              <div><dt>Dock capacity</dt><dd>{shift.dock}</dd></div>
            </dl>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-table-scroll">
        <table aria-label="Mock operational cutoffs">
          <thead><tr><th>Boundary</th><th>Giờ</th><th>Rule</th><th>Recovery</th></tr></thead>
          <tbody>
            <tr><td>Receiving appointment</td><td>20:30</td><td>Không check-in mới sau cutoff</td><td>Supervisor override có audit</td></tr>
            <tr><td>Outbound release</td><td>20:45</td><td>Wave mới phải fit remaining shift</td><td>Defer sang ca sau</td></tr>
            <tr><td>Carrier dispatch</td><td>21:15</td><td>Load phải READY trước cutoff</td><td>Exception + reschedule</td></tr>
          </tbody>
        </table>
      </div>
    </section>
  );
};

const DockYardPreview = () => {
  const docks = [
    { dock: 'D01', vehicle: '51D-482.16', appointment: 'APT-1048', status: 'Đang dỡ hàng', tone: 'success' as const, dwell: '32 phút' },
    { dock: 'D02', vehicle: '43C-218.08', appointment: 'APT-1052', status: 'Chờ QC', tone: 'warning' as const, dwell: '48 phút' },
    { dock: 'D03', vehicle: '—', appointment: '—', status: 'Sẵn sàng', tone: 'neutral' as const, dwell: '—' },
    { dock: 'D04', vehicle: '51C-993.70', appointment: 'APT-1056', status: 'Quá SLA', tone: 'danger' as const, dwell: '76 phút' },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-06" aria-labelledby="warehouse-wh-06-title">
      <MockHeader
        id="WH-06"
        title="Dock & Yard Control Board"
        description="Mock điều phối vehicle từ gate arrival → check-in → yard queue → dock assignment → service completion."
        icon={<Truck size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Vehicles in yard</span><strong>7</strong><small>3 waiting • 4 at dock</small></div>
        <div><span>Dock occupancy</span><strong>75%</strong><small>3 / 4 dock doors</small></div>
        <div><span>Waiting &gt; 45m</span><strong>2</strong><small>Needs supervisor attention</small></div>
        <div><span>Next appointment</span><strong>09:40</strong><small>APT-1058 • inbound</small></div>
      </div>

      <div className="warehouse-dock-grid">
        {docks.map((dock) => (
          <article key={dock.dock}>
            <div className="warehouse-dock-id"><strong>{dock.dock}</strong><StatusBadge tone={dock.tone}>{dock.status}</StatusBadge></div>
            <dl>
              <div><dt>Vehicle</dt><dd>{dock.vehicle}</dd></div>
              <div><dt>Appointment</dt><dd>{dock.appointment}</dd></div>
              <div><dt>Dwell</dt><dd>{dock.dwell}</dd></div>
            </dl>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-callout danger">
        <strong>Double-assignment guard</strong>
        <span>Dock đã có appointment active không được assign cho vehicle khác; conflict phải giữ nguyên context và yêu cầu refresh/re-plan.</span>
      </div>
    </section>
  );
};

const CalendarExceptionPreview = () => {
  const rules = [
    { level: 'Company calendar', value: 'Chủ nhật: đóng cửa', priority: '1', tone: 'neutral' as const },
    { level: 'Warehouse override', value: 'WH-HCM-01 mở 06:00–22:00', priority: '2', tone: 'success' as const },
    { level: 'Shift exception', value: 'Ca chiều giảm 25% dock capacity', priority: '3', tone: 'warning' as const },
    { level: 'Emergency override', value: 'Dock D04 đóng đến 12:00', priority: '4', tone: 'danger' as const },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-07" aria-labelledby="warehouse-wh-07-title">
      <MockHeader
        id="WH-07"
        title="Operational Calendar Exception Precedence"
        description="Mô phỏng cách exception theo company, warehouse, shift và emergency override được resolve theo precedence rõ ràng."
        icon={<ShieldAlert size={20} />}
      />

      <div className="warehouse-exception-flow" aria-label="Mock calendar exception precedence">
        {rules.map((rule) => (
          <article key={rule.level}>
            <span className="warehouse-precedence-index">{rule.priority}</span>
            <div><strong>{rule.level}</strong><span>{rule.value}</span></div>
            <StatusBadge tone={rule.tone}>{rule.priority === '4' ? 'Ưu tiên cao nhất' : 'Rule layer'}</StatusBadge>
          </article>
        ))}
      </div>

      <div className="warehouse-effective-result">
        <div>
          <span>Effective result • 04/10/2026 10:15</span>
          <strong>Kho mở • Dock D01–D03 hoạt động • D04 tạm đóng</strong>
          <p>Emergency override chỉ thay đổi phạm vi D04; các dock khác vẫn theo warehouse override và shift capacity.</p>
        </div>
        <StatusBadge tone="warning">Partial capacity</StatusBadge>
      </div>

      <div className="warehouse-mock-callout warning">
        <strong>Audit requirement</strong>
        <span>Mỗi override cần reason, actor, effective window và audit trail; mock này không ghi cấu hình production.</span>
      </div>
    </section>
  );
};

const WarehouseCapabilityMock = ({ capabilityId }: WarehouseMockProps) => {
  if (capabilityId === 'WH-03') return <CapacityPreview />;
  if (capabilityId === 'WH-04') return <HeatmapPreview />;
  if (capabilityId === 'WH-05') return <CalendarPreview />;
  if (capabilityId === 'WH-06') return <DockYardPreview />;
  if (capabilityId === 'WH-07') return <CalendarExceptionPreview />;
  return null;
};

export default WarehouseCapabilityMock;
