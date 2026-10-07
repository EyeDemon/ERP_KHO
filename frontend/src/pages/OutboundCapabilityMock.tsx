import type { ReactNode } from 'react';
import { Boxes, Layers3, PackageCheck, Route, Truck, Đợts } from 'lucide-react';
import './OutboundCapabilityMock.css';

type OutboundMockProps = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
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
        <span className="outbound-mock-kicker">XUẤT KHO • {id} • MÔ PHỎNG GIAO DIỆN</span>
        <h2 id={'outbound-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="outbound-mock-scope">
      <Badge tone="neutral">Chỉ đọc</Badge>
      <span>Không gọi API hệ thống thật • Không ghi tồn kho • Không giả thao tác thay đổi thành công</span>
    </div>
  </header>
);

const AllocationMock = () => {
  const candidates = [
    { location: 'A01-R02-L03-B04', lot: 'LOT-260930-A', eligible: '120 Gói', rule: 'FEFO + không bị khóa', score: 94, tone: 'success' as const, result: 'Đã phân bổ' },
    { location: 'A02-R01-L01-B02', lot: 'LOT-261001-B', eligible: '64 Gói', rule: 'FEFO + xa hơn', score: 81, tone: 'neutral' as const, result: 'Dự phòng' },
    { location: 'QC-HOLD-02', lot: 'LOT-260928-Q', eligible: '0', rule: 'QC Hold', score: 0, tone: 'danger' as const, result: 'Bị loại' },
  ];

  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-03" aria-labelledby="outbound-out-03-title">
      <Header
        id="OUT-03"
        title="Bàn làm việc ứng viên phân bổ"
        description="Mô phỏng cách nhu cầu đã giữ được gắn vào vị trí/lô/sê-ri đủ điều kiện mà chưa làm giảm OnHand toàn kho."
        icon={<Layers3 size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Đơn bán</span><strong>SO-2026-5108</strong><small>Đã mở</small></div>
        <div><span>Đã giữ</span><strong>120 Gói</strong><small>Nhu cầu đã được giữ</small></div>
        <div><span>Đã phân bổ</span><strong>120 Gói</strong><small>1 vị trí • 1 lô</small></div>
        <div><span>Ảnh hưởng OnHand</span><strong>0</strong><small>Chỉ khấu trừ khi xác nhận giao hàng</small></div>
      </div>
      <div className="outbound-mock-table-scroll">
        <table aria-label="Các ứng viên phân bổ mô phỏng">
          <thead><tr><th>Vị trí</th><th>Lô</th><th>Đủ điều kiện</th><th>Quy tắc</th><th>Điểm</th><th>Kết quả</th></tr></thead>
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
        <strong>Rào chắn điều kiện</strong>
        <span>Phân bổ phải loại tồn không đủ điều kiện theo trạng thái/khóa/lô/sê-ri; biết ID vị trí không được phép bỏ qua quy tắc.</span>
      </div>
    </section>
  );
};

const ĐợtMock = () => {
  const buckets = [
    { label: 'Ưu tiên nhanh', đơn: 8, lines: 26, tasks: 14, tone: 'warning' as const },
    { label: 'Trong ngày', đơn: 14, lines: 51, tasks: 22, tone: 'success' as const },
    { label: 'Tiêu chuẩn', đơn: 21, lines: 74, tasks: 31, tone: 'neutral' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-04" aria-labelledby="outbound-out-04-title">
      <Header
        id="OUT-04"
        title="Lập kế hoạch đợt / lô / cụm"
        description="Mô phỏng gom đơn thành lô thực hiện theo ưu tiên, giờ chốt, khu và khối lượng công việc; mở đợt không tự làm thay đổi tồn kho."
        icon={<Đợts size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Đợt</span><strong>WV-2026-301</strong><small>Giờ chốt 14:30</small></div>
        <div><span>Đơn hàng</span><strong>43</strong><small>151 dòng</small></div>
        <div><span>Nhiệm vụ dự kiến</span><strong>67</strong><small>5 khu lấy hàng</small></div>
        <div><span>Mức phù hợp nhân lực</span><strong>86%</strong><small>Trong sức chứa của ca</small></div>
      </div>
      <div className="outbound-wave-grid">
        {buckets.map(bucket => (
          <article key={bucket.label}>
            <div><strong>{bucket.label}</strong><Badge tone={bucket.tone}>{bucket.orders} đơn</Badge></div>
            <dl>
              <div><dt>Dòng</dt><dd>{bucket.lines}</dd></div>
              <div><dt>Nhiệm vụ dự kiến</dt><dd>{bucket.tasks}</dd></div>
            </dl>
          </article>
        ))}
      </div>
      <div className="outbound-wave-rules">
        <div><span>1</span><strong>Lọc nhu cầu đã mở và đủ điều kiện</strong><small>Phạm vi kho + giờ chốt + trạng thái giữ.</small></div>
        <div><span>2</span><strong>Gom cụm theo khu / đơn vị vận chuyển / dịch vụ</strong><small>Giảm di chuyển nhưng không thay đổi sự thật phân bổ.</small></div>
        <div><span>3</span><strong>Kiểm tra sức chứa</strong><small>Nhân lực, thiết bị và thời gian ca còn lại.</small></div>
        <div><span>4</span><strong>Mở nhiệm vụ thực hiện</strong><small>Tạo nhiệm vụ phải idempotent.</small></div>
      </div>
    </section>
  );
};

const StagingLoadingMock = () => {
  const hus = [
    { hu: 'HU-5108-01', lane: 'STAGE-OUT-03', sequence: '1', status: 'Sẵn sàng', tone: 'success' as const },
    { hu: 'HU-5108-02', lane: 'STAGE-OUT-03', sequence: '2', status: 'Sẵn sàng', tone: 'success' as const },
    { hu: 'HU-5108-03', lane: 'STAGE-OUT-03', sequence: '3', status: 'Kiểm tra niêm phong', tone: 'warning' as const },
    { hu: 'HU-5108-08', lane: 'PACK-02', sequence: '8', status: 'Thiếu tại khu chờ', tone: 'danger' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-07" aria-labelledby="outbound-out-07-title">
      <Header
        id="OUT-07"
        title="Kiểm soát khu chờ & xếp hàng"
        description="Mô phỏng kiểm tra mức sẵn sàng HU, làn chờ, phương tiện, niêm phong và thứ tự xếp hàng trước khi xác nhận giao."
        icon={<Truck size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Lô giao hàng</span><strong>SHP-2026-5108</strong><small>Dự kiến 8 HU</small></div>
        <div><span>Phương tiện</span><strong>51C-882.14</strong><small>Cửa D04</small></div>
        <div><span>Niêm phong</span><strong>SEAL-028817</strong><small>Chờ xác minh</small></div>
        <div><span>Sẵn sàng HU</span><strong>7 / 8</strong><small>Thiếu 1 HU tại khu chờ</small></div>
      </div>
      <div className="outbound-mock-table-scroll">
        <table aria-label="Các HU tại khu chờ/xếp hàng mô phỏng">
          <thead><tr><th>HU</th><th>Làn hiện tại</th><th>Thứ tự xếp</th><th>Trạng thái</th></tr></thead>
          <tbody>
            {hus.map(item => (
              <tr key={item.hu}><td><strong>{item.hu}</strong></td><td>{item.lane}</td><td>{item.sequence}</td><td><Badge tone={item.tone}>{item.status}</Badge></td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="outbound-mock-callout danger">
        <strong>Rào chắn đủ hàng xếp</strong>
        <span>Không được chuyển LOAD_READY/LOADED khi HU bắt buộc chưa ở đúng ngữ cảnh khu chờ/xếp hàng hoặc phương tiện/niêm phong không khớp kế hoạch.</span>
      </div>
    </section>
  );
};

const DispatchMock = () => {
  const checks = [
    { label: 'Lô giao hàng state', detail: 'LOADED', tone: 'success' as const, status: 'Đạt' },
    { label: 'HU đã xếp', detail: '8 / 8', tone: 'success' as const, status: 'Đạt' },
    { label: 'Phiên bản tồn kho', detail: 'v1842', tone: 'success' as const, status: 'Hiện hành' },
    { label: 'Khóa idempotency', detail: 'dispatch-SHP-5108-v3', tone: 'neutral' as const, status: 'Duy nhất' },
    { label: 'Giữ hàng / Phân bổ', detail: '120 / 120', tone: 'success' as const, status: 'Có thể tiêu thụ' },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-08" aria-labelledby="outbound-out-08-title">
      <Header
        id="OUT-08"
        title="Lô giao hàng Dispatch Boundary"
        description="Mô phỏng kiểm tra trước khi ghi sổ xuất kho. Đây là ranh giới duy nhất trong luồng xuất kho làm giảm OnHand toàn kho."
        icon={<PackageCheck size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Lô giao hàng</span><strong>SHP-2026-5108</strong><small>WH-HCM-01</small></div>
        <div><span>Số lượng giao</span><strong>120 Gói</strong><small>Số lượng cơ sở đã đối chiếu</small></div>
        <div><span>Hiện hành On Hand</span><strong>1.842</strong><small>Ảnh chụp trước giao hàng</small></div>
        <div><span>OnHand dự kiến</span><strong>1.722</strong><small>Chỉ sau khi POST thành công</small></div>
      </div>
      <div className="outbound-dispatch-checks">
        {checks.map(check => (
          <article key={check.label}><div><strong>{check.label}</strong><span>{check.detail}</span></div><Badge tone={check.tone}>{check.status}</Badge></article>
        ))}
      </div>
      <div className="outbound-boundary-box">
        <div><span>Trước giao hàng</span><strong>Trạng thái giữ + phân bổ + lấy/xếp hàng</strong><small>OnHand không đổi</small></div>
        <span className="outbound-boundary-arrow" aria-hidden="true">→</span>
        <div><span>Giao hàng nguyên tử</span><strong>Khấu trừ tồn vật lý đúng một lần</strong><small>Sổ cái + kiểm toán + outbox</small></div>
        <span className="outbound-boundary-arrow" aria-hidden="true">→</span>
        <div><span>Sau giao hàng</span><strong>Giữ hàng/phân bổ đã được tiêu thụ</strong><small>Thử lại trả cùng kết quả chuẩn</small></div>
      </div>
    </section>
  );
};

const Đơn thiếu hàngMock = () => {
  const rows = [
    { label: 'Đã đặt', value: 300, tone: 'neutral' as const },
    { label: 'Đã giữ', value: 220, tone: 'success' as const },
    { label: 'Đã phân bổ', value: 200, tone: 'success' as const },
    { label: 'Picked', value: 180, tone: 'warning' as const },
    { label: 'Đã giao', value: 160, tone: 'success' as const },
    { label: 'Đơn thiếu hàng', value: 80, tone: 'danger' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-09" aria-labelledby="outbound-out-09-title">
      <Header
        id="OUT-09"
        title="Đơn thiếu hàng & Promise Replanning"
        description="Mô phỏng thác số lượng và lập lại ngày cam kết để tránh trộn ngữ nghĩa đã đặt/đã giữ/đã phân bổ/đã lấy/đã giao/đơn thiếu."
        icon={<Boxes size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Đơn bán</span><strong>SO-2026-5112</strong><small>Khách hàng CUS-0044</small></div>
        <div><span>ETA nguồn cung tiếp theo</span><strong>05/10/2026</strong><small>Dự kiến 120 Cái</small></div>
        <div><span>Đơn thiếu hàng</span><strong>80 Cái</strong><small>26,7% đơn hàng</small></div>
        <div><span>Rủi ro cam kết</span><strong>Trung bình</strong><small>Cần lập kế hoạch lại</small></div>
      </div>
      <div className="outbound-waterfall" aria-label="Thác số lượng xuất kho mô phỏng">
        {rows.map(row => (
          <article key={row.label}>
            <div><span>{row.label}</span><strong>{row.value}</strong></div>
            <div className="outbound-waterfall-track"><span className={row.tone} style={{ width: Math.max(12, row.value / 3) + '%' }} /></div>
          </article>
        ))}
      </div>
      <div className="outbound-mock-callout warning">
        <strong>Rào chắn ngày cam kết</strong>
        <span>Lập lại kế hoạch phải kiểm ETA nguồn cung, phân bổ hiện tại và chính sách khách hàng; không được tự tạo giữ hàng hoặc hứa ngày mới vượt nguồn cung khả dụng.</span>
      </div>
    </section>
  );
};

const TrackingMock = () => {
  const events = [
    { time: '04/10 08:10', title: 'Đã xuất giao', detail: 'WH-HCM-01 • Rời cổng', tone: 'success' as const },
    { time: '04/10 09:25', title: 'Đang vận chuyển', detail: 'HCM Hub', tone: 'success' as const },
    { time: '04/10 12:40', title: 'Đang giao', detail: 'Route HCM-07', tone: 'warning' as const },
    { time: '—', title: 'Đã giao / POD', detail: 'Chờ chữ ký + ảnh', tone: 'neutral' as const },
  ];
  return (
    <section className="outbound-capability-mock" data-testid="outbound-capability-mock-OUT-10" aria-labelledby="outbound-out-10-title">
      <Header
        id="OUT-10"
        title="Lô giao hàng Tracking / POD Timeline"
        description="Mô phỏng theo dõi logistics sau khi xuất giao, POD và giao thất bại/thử lại mà không tạo thêm biến động tồn kho xuất."
        icon={<Route size={20} />}
      />
      <div className="outbound-mock-metrics">
        <div><span>Lô giao hàng</span><strong>SHP-2026-5108</strong><small>Đã xuất giao</small></div>
        <div><span>Đơn vị vận chuyển</span><strong>CAR-FAST-01</strong><small>TRK-88941002</small></div>
        <div><span>Hiện hành node</span><strong>Route HCM-07</strong><small>Đang giao</small></div>
        <div><span>POD</span><strong>Đang chờ</strong><small>Chữ ký + ảnh</small></div>
      </div>
      <div className="outbound-tracking-timeline">
        {events.map((event, index) => (
          <article key={event.title}>
            <span className={'outbound-tracking-dot ' + event.tone} aria-hidden="true" />
            <time>{event.time}</time>
            <div><strong>{event.title}</strong><span>{event.detail}</span></div>
            <Badge tone={event.tone}>{index < 3 ? 'Đã ghi nhận' : 'Đang chờ'}</Badge>
          </article>
        ))}
      </div>
      <div className="outbound-mock-callout warning">
        <strong>Ngữ nghĩa giao hàng thất bại</strong>
        <span>Giao hàng thất bại chỉ mở quy trình thử lại/trả hàng. Tồn không được tự cộng lại kho cho tới khi phiếu nhận trả hàng/ghi sổ hợp lệ.</span>
      </div>
    </section>
  );
};

const OutboundCapabilityMock = ({ capabilityId }: OutboundMockProps) => {
  if (capabilityId === 'OUT-03') return <AllocationMock />;
  if (capabilityId === 'OUT-04') return <ĐợtMock />;
  if (capabilityId === 'OUT-07') return <StagingLoadingMock />;
  if (capabilityId === 'OUT-08') return <DispatchMock />;
  if (capabilityId === 'OUT-09') return <Đơn thiếu hàngMock />;
  if (capabilityId === 'OUT-10') return <TrackingMock />;
  return null;
};

export default OutboundCapabilityMock;
