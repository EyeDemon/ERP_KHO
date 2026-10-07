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
        <span className="warehouse-mock-kicker">KHO & VỊ TRÍ • {id} • MÔ PHỎNG GIAO DIỆN</span>
        <h2 id={'warehouse-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="warehouse-mock-scope">
      <StatusBadge tone="neutral">Chỉ đọc</StatusBadge>
      <span>Không gọi API hệ thống thật • Không ghi cơ sở dữ liệu • Không thay đổi tồn kho</span>
    </div>
  </header>
);

const CapacityPreview = () => {
  const locations = [
    { code: 'A01-R02-L03-B04', weight: '1.260 / 1.500 kg', volume: '8,2 / 10 m³', pallet: '4 / 5', rule: 'Nhiệt độ thường • An toàn thực phẩm', tone: 'warning' as const, status: 'Gần đầy' },
    { code: 'A01-R02-L03-B05', weight: '620 / 1.500 kg', volume: '4,1 / 10 m³', pallet: '2 / 5', rule: 'Nhiệt độ thường • An toàn thực phẩm', tone: 'success' as const, status: 'Có thể cất' },
    { code: 'A01-R02-L03-B08', weight: '0 / 800 kg', volume: '0 / 6 m³', pallet: '0 / 3', rule: 'Chỉ hàng hư hỏng', tone: 'danger' as const, status: 'Không tương thích' },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-03" aria-labelledby="warehouse-wh-03-title">
      <MockHeader
        id="WH-03"
        title="Sức chứa vị trí & Ràng buộc lưu trữ"
        description="Mô phỏng cách WMS kiểm tra khối lượng, thể tích, sức chứa pallet và độ tương thích trước khi cất hàng hoặc di chuyển nội bộ."
        icon={<Gauge size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Tải hàng sắp vào</span><strong>240 kg</strong><small>1 pallet • 1,6 m³</small></div>
        <div><span>Vị trí ứng viên</span><strong>3</strong><small>2 phù hợp • 1 bị loại</small></div>
        <div><span>Ứng viên tốt nhất</span><strong>B05</strong><small>Đã dùng 41% thể tích</small></div>
        <div><span>Hồ sơ ràng buộc</span><strong>FOOD-AMBIENT</strong><small>Phiên bản 4 • mô phỏng</small></div>
      </div>

      <div className="warehouse-capacity-summary">
        <div>
          <div className="warehouse-capacity-label"><span>Khối lượng dự kiến B04</span><strong>100%</strong></div>
          <div className="warehouse-capacity-track" role="progressbar" aria-label="Mức sử dụng khối lượng dự kiến B04" aria-valuemin={0} aria-valuemax={100} aria-valuenow={100}>
            <span className="danger" style={{ width: '100%' }} />
          </div>
          <small>1.260 + 240 = 1.500 kg • chạm giới hạn</small>
        </div>
        <div>
          <div className="warehouse-capacity-label"><span>Khối lượng dự kiến B05</span><strong>57%</strong></div>
          <div className="warehouse-capacity-track" role="progressbar" aria-label="Mức sử dụng khối lượng dự kiến B05" aria-valuemin={0} aria-valuemax={100} aria-valuenow={57}>
            <span className="success" style={{ width: '57%' }} />
          </div>
          <small>620 + 240 = 860 / 1.500 kg • còn dư địa</small>
        </div>
      </div>

      <div className="warehouse-mock-table-scroll">
        <table aria-label="Các vị trí ứng viên về sức chứa mô phỏng">
          <thead><tr><th>Vị trí</th><th>Khối lượng</th><th>Thể tích</th><th>Pallet</th><th>Quy tắc lưu trữ</th><th>Kết quả</th></tr></thead>
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
    { code: 'A01', utilization: 92, status: 'Khẩn cấp', tone: 'danger' as const, detail: 'Ùn tắc • 14 nhiệm vụ' },
    { code: 'A02', utilization: 78, status: 'Bận', tone: 'warning' as const, detail: '8 nhiệm vụ' },
    { code: 'A03', utilization: 54, status: 'Bình thường', tone: 'success' as const, detail: '4 nhiệm vụ' },
    { code: 'B01', utilization: 68, status: 'Bình thường', tone: 'success' as const, detail: '5 nhiệm vụ' },
    { code: 'B02', utilization: 84, status: 'Bận', tone: 'warning' as const, detail: '9 nhiệm vụ' },
    { code: 'B03', utilization: 37, status: 'Bình thường', tone: 'success' as const, detail: '2 nhiệm vụ' },
    { code: 'QC', utilization: 61, status: 'Cần chú ý', tone: 'warning' as const, detail: '6 HU đang chờ' },
    { code: 'STAGE', utilization: 88, status: 'Khẩn cấp', tone: 'danger' as const, detail: 'Còn 45 phút tới giờ chốt cửa kho' },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-04" aria-labelledby="warehouse-wh-04-title">
      <MockHeader
        id="WH-04"
        title="Bản đồ kho & Bản đồ nhiệt"
        description="Tổng quan không gian mô phỏng để xem mức sử dụng, ùn tắc và áp lực vận hành theo khu/dãy mà không biến ảnh chụp thành sự thật tồn kho."
        icon={<MapPinned size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Kho</span><strong>WH-HCM-01</strong><small>DC Hồ Chí Minh</small></div>
        <div><span>Tuổi dữ liệu chụp</span><strong>2 phút</strong><small>Chỉ báo độ mới mô phỏng</small></div>
        <div><span>Khu vực khẩn cấp</span><strong>2</strong><small>A01 • STAGE</small></div>
        <div><span>Mức sử dụng trung bình</span><strong>70%</strong><small>8 khu vực vận hành</small></div>
      </div>

      <div className="warehouse-heatmap" aria-label="Bản đồ nhiệt mức sử dụng kho mô phỏng">
        {cells.map((cell) => (
          <article key={cell.code} className={'warehouse-heatmap-cell ' + cell.tone}>
            <div><strong>{cell.code}</strong><span>{cell.utilization}% sử dụng</span></div>
            <StatusBadge tone={cell.tone}>{cell.status}</StatusBadge>
            <small>{cell.detail}</small>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-callout warning">
        <strong>Rào chắn dữ liệu cũ</strong>
        <span>Bản đồ nhiệt quá ngưỡng độ mới phải yêu cầu làm mới trước khi dùng để điều phối nhiệm vụ hoặc đánh giá sức chứa.</span>
      </div>
    </section>
  );
};

const CalendarPreview = () => {
  const shifts = [
    { name: 'Ca sáng', time: '06:00–14:00', labor: '38 / 42 người', dock: '18 / 24 giờ cửa kho', status: 'Đang chạy', tone: 'success' as const },
    { name: 'Ca chiều', time: '14:00–22:00', labor: '31 / 40 người', dock: '21 / 24 giờ cửa kho', status: 'Sắp đầy', tone: 'warning' as const },
    { name: 'Ca đêm', time: '22:00–06:00', labor: '18 / 24 người', dock: '10 / 16 giờ cửa kho', status: 'Đã lập kế hoạch', tone: 'neutral' as const },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-05" aria-labelledby="warehouse-wh-05-title">
      <MockHeader
        id="WH-05"
        title="Lịch vận hành & Ca làm việc"
        description="Mô phỏng múi giờ kho, giờ mở cửa, ca, giờ chốt và sức chứa theo ngày vận hành."
        icon={<CalendarDays size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Múi giờ</span><strong>Asia/Ho_Chi_Minh</strong><small>UTC+07:00</small></div>
        <div><span>Ngày vận hành</span><strong>04/10/2026</strong><small>Chủ nhật</small></div>
        <div><span>Giờ chốt nhận hàng</span><strong>20:30</strong><small>Nhận xe theo lịch</small></div>
        <div><span>Giờ chốt xuất hàng</span><strong>21:15</strong><small>Bàn giao đơn vị vận chuyển</small></div>
      </div>

      <div className="warehouse-shift-grid">
        {shifts.map((shift) => (
          <article key={shift.name}>
            <div className="warehouse-shift-heading"><div><strong>{shift.name}</strong><span>{shift.time}</span></div><StatusBadge tone={shift.tone}>{shift.status}</StatusBadge></div>
            <dl>
              <div><dt>Nhân lực</dt><dd>{shift.labor}</dd></div>
              <div><dt>Sức chứa cửa kho</dt><dd>{shift.dock}</dd></div>
            </dl>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-table-scroll">
        <table aria-label="Các giờ chốt vận hành mô phỏng">
          <thead><tr><th>Ranh giới</th><th>Giờ</th><th>Quy tắc</th><th>Cách xử lý</th></tr></thead>
          <tbody>
            <tr><td>Lịch nhận hàng</td><td>20:30</td><td>Không nhận xe mới sau giờ chốt</td><td>Quản lý có thể ghi đè với kiểm toán</td></tr>
            <tr><td>Mở lệnh xuất kho</td><td>20:45</td><td>Đợt mới phải phù hợp phần ca còn lại</td><td>Dời sang ca sau</td></tr>
            <tr><td>Bàn giao vận chuyển</td><td>21:15</td><td>Tải hàng phải READY trước giờ chốt</td><td>Ngoại lệ + xếp lịch lại</td></tr>
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
        title="Bảng điều hành cửa kho & sân bãi"
        description="Mô phỏng điều phối phương tiện từ đến cổng → nhận xe → hàng đợi sân bãi → gán cửa kho → hoàn tất phục vụ."
        icon={<Truck size={20} />}
      />

      <div className="warehouse-mock-metrics">
        <div><span>Phương tiện trong sân</span><strong>7</strong><small>3 đang chờ • 4 tại cửa kho</small></div>
        <div><span>Mức sử dụng cửa kho</span><strong>75%</strong><small>3 / 4 cửa kho</small></div>
        <div><span>Chờ &gt; 45 phút</span><strong>2</strong><small>Cần quản lý xử lý</small></div>
        <div><span>Lịch tiếp theo</span><strong>09:40</strong><small>APT-1058 • nhập kho</small></div>
      </div>

      <div className="warehouse-dock-grid">
        {docks.map((dock) => (
          <article key={dock.dock}>
            <div className="warehouse-dock-id"><strong>{dock.dock}</strong><StatusBadge tone={dock.tone}>{dock.status}</StatusBadge></div>
            <dl>
              <div><dt>Phương tiện</dt><dd>{dock.vehicle}</dd></div>
              <div><dt>Lịch hẹn</dt><dd>{dock.appointment}</dd></div>
              <div><dt>Thời gian lưu</dt><dd>{dock.dwell}</dd></div>
            </dl>
          </article>
        ))}
      </div>

      <div className="warehouse-mock-callout danger">
        <strong>Rào chắn gán trùng</strong>
        <span>Cửa kho đã có lịch đang hoạt động không được gán cho phương tiện khác; xung đột phải giữ nguyên ngữ cảnh và yêu cầu làm mới/lập kế hoạch lại.</span>
      </div>
    </section>
  );
};

const CalendarExceptionPreview = () => {
  const rules = [
    { level: 'Lịch công ty', value: 'Chủ nhật: đóng cửa', priority: '1', tone: 'neutral' as const },
    { level: 'Ghi đè cấp kho', value: 'WH-HCM-01 mở 06:00–22:00', priority: '2', tone: 'success' as const },
    { level: 'Ngoại lệ ca', value: 'Ca chiều giảm 25% sức chứa cửa kho', priority: '3', tone: 'warning' as const },
    { level: 'Ghi đè khẩn cấp', value: 'Cửa D04 đóng đến 12:00', priority: '4', tone: 'danger' as const },
  ];

  return (
    <section className="warehouse-capability-mock" data-testid="warehouse-capability-mock-WH-07" aria-labelledby="warehouse-wh-07-title">
      <MockHeader
        id="WH-07"
        title="Thứ tự ưu tiên ngoại lệ lịch vận hành"
        description="Mô phỏng cách ngoại lệ theo công ty, kho, ca và ghi đè khẩn cấp được giải quyết theo thứ tự ưu tiên rõ ràng."
        icon={<ShieldAlert size={20} />}
      />

      <div className="warehouse-exception-flow" aria-label="Thứ tự ưu tiên ngoại lệ lịch mô phỏng">
        {rules.map((rule) => (
          <article key={rule.level}>
            <span className="warehouse-precedence-index">{rule.priority}</span>
            <div><strong>{rule.level}</strong><span>{rule.value}</span></div>
            <StatusBadge tone={rule.tone}>{rule.priority === '4' ? 'Ưu tiên cao nhất' : 'Lớp quy tắc'}</StatusBadge>
          </article>
        ))}
      </div>

      <div className="warehouse-effective-result">
        <div>
          <span>Kết quả hiệu lực • 04/10/2026 10:15</span>
          <strong>Kho mở • Cửa D01–D03 hoạt động • D04 tạm đóng</strong>
          <p>Ghi đè khẩn cấp chỉ thay đổi phạm vi D04; các cửa khác vẫn theo ghi đè cấp kho và sức chứa ca.</p>
        </div>
        <StatusBadge tone="warning">Sức chứa một phần</StatusBadge>
      </div>

      <div className="warehouse-mock-callout warning">
        <strong>Yêu cầu kiểm toán</strong>
        <span>Mỗi lần ghi đè cần lý do, người thực hiện, thời gian hiệu lực và dấu vết kiểm toán; mô phỏng này không ghi cấu hình hệ thống thật.</span>
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
