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
        <span className="inbound-mock-kicker">NHẬP KHO • {id} • MÔ PHỎNG GIAO DIỆN</span>
        <h2 id={'inbound-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="inbound-mock-scope">
      <Badge tone="neutral">Chỉ đọc</Badge>
      <span>Không gọi API hệ thống thật • Không ghi tồn kho • Không giả thao tác thay đổi thành công</span>
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
        title="Đối chiếu Đơn mua / ASN"
        description="Mô phỏng hàng nhập dự kiến từ PO và ASN, tách rõ dữ liệu dự kiến khỏi Phiếu nhập và Tồn thực tế."
        icon={<ClipboardList size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>PO</span><strong>PO-2026-8831</strong><small>Nhà cung cấp SUP-0008</small></div>
        <div><span>ASN</span><strong>ASN-2026-4172</strong><small>ETA 16:30 • 04/10</small></div>
        <div><span>Dự kiến</span><strong>120 Thùng</strong><small>3 SKU</small></div>
        <div><span>Ảnh hưởng tồn kho</span><strong>0</strong><small>Chỉ tăng khi ghi sổ Phiếu nhập</small></div>
      </div>
      <div className="inbound-mock-table-scroll">
        <table aria-label="Đối chiếu PO/ASN mô phỏng">
          <thead><tr><th>SKU</th><th>Số lượng PO</th><th>Số lượng ASN</th><th>Chênh lệch</th><th>Kiểm tra</th></tr></thead>
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
        <strong>Dự kiến ≠ Đã nhận</strong>
        <span>PO/ASN chỉ tạo kỳ vọng nhận hàng. Không được dùng thời điểm ASN đến để tăng Tồn thực tế hoặc bỏ qua ranh giới tiếp nhận/QC/ghi sổ.</span>
      </div>
    </section>
  );
};

const AppointmentMock = () => {
  const appointments = [
    { time: '08:30–09:00', ref: 'APT-1048', vehicle: '51D-482.16', dock: 'D01', status: 'Đã nhận xe', tone: 'success' as const },
    { time: '09:10–09:40', ref: 'APT-1052', vehicle: '43C-218.08', dock: 'D02', status: 'Đã đến', tone: 'warning' as const },
    { time: '09:40–10:10', ref: 'APT-1058', vehicle: '51C-778.21', dock: 'D03', status: 'Đã xác nhận', tone: 'neutral' as const },
    { time: '10:00–10:30', ref: 'APT-1060', vehicle: '60C-113.84', dock: 'D03', status: 'Xung đột cửa kho', tone: 'danger' as const },
  ];

  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-02" aria-labelledby="inbound-in-02-title">
      <Header
        id="IN-02"
        title="Bảng lịch nhận hàng"
        description="Mô phỏng lịch xe vào kho với trạng thái đến, nhận xe tại cổng và gán cửa kho tách biệt, có xung đột/vắng mặt rõ ràng."
        icon={<CalendarClock size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Khung giờ</span><strong>08:00–12:00</strong><small>WH-HCM-01</small></div>
        <div><span>Lịch hẹn</span><strong>4</strong><small>3 hợp lệ • 1 xung đột</small></div>
        <div><span>Mức sử dụng cửa kho</span><strong>67%</strong><small>2 / 3 đang dùng</small></div>
        <div><span>Trễ / vắng mặt</span><strong>1</strong><small>Cần phân loại xử lý</small></div>
      </div>
      <div className="inbound-appointment-list" aria-label="Dòng thời gian lịch nhận hàng mô phỏng">
        {appointments.map((item) => (
          <article key={item.ref}>
            <time>{item.time}</time>
            <div><strong>{item.ref}</strong><span>{item.vehicle} • Cửa {item.dock}</span></div>
            <Badge tone={item.tone}>{item.status}</Badge>
          </article>
        ))}
      </div>
      <div className="inbound-mock-callout danger">
        <strong>Rào chắn xung đột cửa kho</strong>
        <span>D03 không thể nhận hai lịch hẹn chồng khung giờ khi sức chứa chỉ một phương tiện. Xung đột phải được lập kế hoạch lại, không tự ghi đè phân công.</span>
      </div>
    </section>
  );
};

const DiscrepancyMock = () => {
  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-05" aria-labelledby="inbound-in-05-title">
      <Header
        id="IN-05"
        title="Xử lý nhận thừa / thiếu"
        description="Mô phỏng xử lý chênh lệch dự kiến/thực nhận theo dung sai, lý do và ranh giới phê duyệt trước khi tiếp tục luồng."
        icon={<Scale size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Dự kiến</span><strong>100 Thùng</strong><small>Ảnh chụp PO / ASN</small></div>
        <div><span>Thực nhận</span><strong>108 Thùng</strong><small>Số lượng tiếp nhận</small></div>
        <div><span>Chênh lệch</span><strong>+8%</strong><small>+8 Thùng</small></div>
        <div><span>Dung sai</span><strong>±2%</strong><small>Cần phê duyệt</small></div>
      </div>
      <div className="inbound-discrepancy-grid">
        <article>
          <span>1 • Phát hiện</span><strong>OVER_RECEIPT</strong><p>Số lượng thực nhận vượt ảnh chụp dự kiến.</p><Badge tone="danger">Ngoài dung sai</Badge>
        </article>
        <article>
          <span>2 • Lý do</span><strong>SUPPLIER_OVER_SHIP</strong><p>Gắn lý do + ghi chú/bằng chứng bắt buộc.</p><Badge tone="warning">Cần bằng chứng</Badge>
        </article>
        <article>
          <span>3 • Phê duyệt</span><strong>PENDING_APPROVAL</strong><p>Không tự xử lý khi vượt ngưỡng.</p><Badge tone="warning">Quản lý rà soát</Badge>
        </article>
        <article>
          <span>4 • Xử lý</span><strong>Chấp nhận / Từ chối phần thừa</strong><p>Kết quả phải đối chiếu khớp với số lượng đã nhận.</p><Badge tone="neutral">Chưa quyết định</Badge>
        </article>
      </div>
      <div className="inbound-mock-callout warning">
        <strong>Rào chắn ghi sổ</strong>
        <span>Chênh lệch vượt dung sai phải có cách xử lý hợp lệ trước ranh giới tiếp theo; mô phỏng không thay đổi Phiếu nhập hoặc Tồn kho.</span>
      </div>
    </section>
  );
};

const QcMock = () => {
  const disposition = [
    { bucket: 'Chấp nhận', qty: 90, tone: 'success' as const },
    { bucket: 'Hư hỏng', qty: 4, tone: 'warning' as const },
    { bucket: 'Từ chối', qty: 2, tone: 'danger' as const },
  ];
  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-06" aria-labelledby="inbound-in-06-title">
      <Header
        id="IN-06"
        title="Kiểm tra chất lượng hàng nhập"
        description="Mô phỏng danh sách kiểm tra, bằng chứng và phân loại xử lý; tổng Chấp nhận + Hư hỏng + Từ chối phải cân bằng Đã nhận."
        icon={<ShieldCheck size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>Phiếu nhập</span><strong>GR-2026-1045</strong><small>SKU-1001</small></div>
        <div><span>Đã nhận</span><strong>96 Cái</strong><small>Cần QC</small></div>
        <div><span>Bằng chứng</span><strong>3 mục</strong><small>2 ảnh • 1 checklist</small></div>
        <div><span>Cân bằng</span><strong>96 / 96</strong><small>Phân loại đã cân bằng</small></div>
      </div>
      <div className="inbound-qc-layout">
        <div>
          <h3>Tiêu chí kiểm tra</h3>
          <ul>
            <li><span aria-hidden="true">✓</span><strong>Tình trạng bao bì</strong><small>Đạt • mẫu 12/12</small></li>
            <li><span aria-hidden="true">✓</span><strong>Nhãn / lô khớp</strong><small>Đạt • LOT-261004-A</small></li>
            <li><span aria-hidden="true">!</span><strong>Hư hỏng quan sát được</strong><small>6 đơn vị cần phân loại xử lý</small></li>
          </ul>
        </div>
        <div>
          <h3>Phân loại xử lý</h3>
          {disposition.map((item) => (
            <div className="inbound-disposition-row" key={item.bucket}>
              <span>{item.bucket}</span><strong>{item.qty} Cái</strong><Badge tone={item.tone}>{item.bucket}</Badge>
            </div>
          ))}
        </div>
      </div>
      <div className="inbound-mock-callout warning">
        <strong>Quy tắc cân bằng QC</strong>
        <span>90 + 4 + 2 = 96. Nếu phân loại xử lý không cân bằng với Đã nhận thì READY_TO_POST phải bị chặn.</span>
      </div>
    </section>
  );
};

const PutawayRuleMock = () => {
  const candidates = [
    { code: 'A01-R02-L03-B04', compatibility: 'Đạt', capacity: 'Sát giới hạn', distance: '18 m', score: '92', result: 'Khuyến nghị', tone: 'success' as const },
    { code: 'A02-R01-L01-B02', compatibility: 'Đạt', capacity: 'Đạt', distance: '32 m', score: '78', result: 'Đủ điều kiện', tone: 'neutral' as const },
    { code: 'B01-R03-L02-B07', compatibility: 'Không đạt', capacity: 'Đạt', distance: '21 m', score: '—', result: 'Từ chối', tone: 'danger' as const },
  ];

  return (
    <section className="inbound-capability-mock" data-testid="inbound-capability-mock-IN-09" aria-labelledby="inbound-in-09-title">
      <Header
        id="IN-09"
        title="Giải thích khuyến nghị cất hàng"
        description="Mô phỏng lọc + xếp hạng ứng viên theo độ tương thích, trạng thái, sức chứa và chi phí di chuyển; khuyến nghị luôn hiển thị lý do."
        icon={<MapPin size={20} />}
      />
      <div className="inbound-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>Thực phẩm • Nhiệt độ thường</small></div>
        <div><span>Nguồn</span><strong>RECEIVING-01</strong><small>WH-HCM-01</small></div>
        <div><span>Số lượng</span><strong>20 Thùng</strong><small>240 Cái UOM cơ sở</small></div>
        <div><span>Khuyến nghị</span><strong>B04</strong><small>Điểm 92 / 100</small></div>
      </div>
      <div className="inbound-mock-table-scroll">
        <table aria-label="Các vị trí cất hàng ứng viên mô phỏng">
          <thead><tr><th>Ứng viên</th><th>Tương thích</th><th>Sức chứa</th><th>Di chuyển</th><th>Điểm</th><th>Kết quả</th></tr></thead>
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
        <span>Cùng hồ sơ lưu trữ + đúng trạng thái + đủ sức chứa sau tải dự kiến + gần nguồn nhất trong nhóm điểm cao.</span>
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
