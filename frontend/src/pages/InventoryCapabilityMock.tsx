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
        <span className="inventory-mock-kicker">KIỂM SOÁT TỒN KHO • {id} • MÔ PHỎNG GIAO DIỆN</span>
        <h2 id={'inventory-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="inventory-mock-scope">
      <Badge tone="neutral">Chỉ đọc</Badge>
      <span>Không gọi API hệ thống thật • Không sửa sổ cái • Không ghi tồn kho</span>
    </div>
  </header>
);

const Trạng tháiControlMock = () => {
  const statuses = [
    { name: 'AVAILABLE', qty: '1.240', eligible: 'Giữ / Phân bổ / Lấy', tone: 'success' as const },
    { name: 'QC_HOLD', qty: '96', eligible: 'Không giữ / không lấy', tone: 'warning' as const },
    { name: 'QUARANTINE', qty: '28', eligible: 'Bị chặn bởi chính sách', tone: 'warning' as const },
    { name: 'DAMAGED', qty: '14', eligible: 'Cần phân loại xử lý', tone: 'danger' as const },
    { name: 'EXPIRED', qty: '6', eligible: 'Không đủ điều kiện', tone: 'danger' as const },
    { name: 'RECALL_BLOCKED', qty: '0', eligible: 'Chỉ phạm vi thu hồi', tone: 'neutral' as const },
  ];

  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-05" aria-labelledby="inventory-inv-05-title">
      <Header
        id="INV-05"
        title="Bảng điều kiện theo trạng thái tồn kho"
        description="Mô phỏng trạng thái tồn kho và điều kiện vận hành; đổi trạng thái phải có quyền, lý do, bằng chứng và kiểm tra khóa."
        icon={<Ban size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>WH-HCM-01</small></div>
        <div><span>Tổng OnHand</span><strong>1.384 Cái</strong><small>Trên mọi trạng thái</small></div>
        <div><span>Đủ điều kiện vận hành</span><strong>1.240 Cái</strong><small>Chỉ AVAILABLE</small></div>
        <div><span>Bị hạn chế</span><strong>144 Cái</strong><small>Giữ / hư hỏng / hết hạn</small></div>
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
        <strong>Rào chắn chuyển trạng thái</strong>
        <span>Không cho mở/chặn/giữ khi thiếu quyền, lý do/bằng chứng hoặc tồn kho đang bị khóa bởi chính sách kiểm kê/sự cố/thu hồi.</span>
      </div>
    </section>
  );
};

const LôSê-riMock = () => {
  const identities = [
    { id: 'LOT-1001-260930', type: 'Lô', expiry: '30/09/2027', status: 'AVAILABLE', location: '3 ô', tone: 'success' as const },
    { id: 'SN-1001-884201', type: 'Sê-ri', expiry: '—', status: 'AVAILABLE', location: 'A01-R02-L03-B04', tone: 'success' as const },
    { id: 'SN-1001-884202', type: 'Sê-ri', expiry: '—', status: 'QC_HOLD', location: 'QC-01', tone: 'warning' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-06" aria-labelledby="inventory-inv-06-title">
      <Header
        id="INV-06"
        title="Trình khám phá Lô / Sê-ri / Hạn dùng"
        description="Mô phỏng tìm định danh, điều kiện hạn dùng và bảo vệ chống trùng cho tồn kho theo dõi lô/sê-ri."
        icon={<ScanLine size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>SKU</span><strong>SKU-1001</strong><small>Theo dõi theo lô</small></div>
        <div><span>Lô đã chọn</span><strong>LOT-1001-260930</strong><small>Hạn dùng 30/09/2027</small></div>
        <div><span>Vị trí</span><strong>3</strong><small>Tổng 420 Cái</small></div>
        <div><span>Thứ hạng FEFO</span><strong>#1</strong><small>Trong các lô đủ điều kiện</small></div>
      </div>
      <div className="inventory-mock-table-scroll">
        <table aria-label="Định danh lô/sê-ri/hạn dùng mô phỏng">
          <thead><tr><th>Định danh</th><th>Loại</th><th>Hạn dùng</th><th>Trạng thái</th><th>Vị trí</th></tr></thead>
          <tbody>
            {identities.map(item => (
              <tr key={item.id}><td><strong>{item.id}</strong></td><td>{item.type}</td><td>{item.expiry}</td><td><Badge tone={item.tone}>{item.status}</Badge></td><td>{item.location}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="inventory-mock-callout danger">
        <strong>Rào chắn định danh trùng</strong>
        <span>Sê-ri đã tồn tại không được tạo lại; lot/expiry phải tuân tracking policy và không được dùng identity lookup để bypass status eligibility.</span>
      </div>
    </section>
  );
};

const LockFreezeMock = () => {
  const affected = [
    { item: 'Khu PICK-A', impact: 'Kiểm tra giữ hàng / phân bổ', status: 'Đóng băng mềm', tone: 'warning' as const },
    { item: '4 nhiệm vụ lấy hàng đang mở', impact: 'Cho hoàn tất nhiệm vụ đang chạy; chặn nhiệm vụ mới', status: 'Được giữ hiệu lực', tone: 'neutral' as const },
    { item: 'Kiểm kê chu kỳ CT-1044', impact: 'Ảnh chụp 09:00', status: 'Chủ sở hữu', tone: 'success' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-07" aria-labelledby="inventory-inv-07-title">
      <Header
        id="INV-07"
        title="Chính sách khóa / đóng băng tồn kho"
        description="Mô phỏng phạm vi khóa và chiến lược đóng băng để bảo vệ công việc kiểm kê/sự cố mà không gây thay đổi mơ hồ."
        icon={<LockKeyhole size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Phạm vi</span><strong>WH-HCM-01 / PICK-A</strong><small>Cấp khu</small></div>
        <div><span>Chiến lược</span><strong>SOFT_FREEZE</strong><small>Phiên bản chính sách 3</small></div>
        <div><span>Lý do</span><strong>CYCLE_COUNT</strong><small>CT-1044</small></div>
        <div><span>Hết hiệu lực</span><strong>11:00</strong><small>04/10/2026</small></div>
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
        <div><strong>HARD_FREEZE</strong><span>Chặn mọi thay đổi tồn kho trong phạm vi trừ lệnh phục hồi tường minh.</span></div>
        <div><strong>SOFT_FREEZE</strong><span>Cho phép công việc đang hoạt động theo chính sách; chặn thay đổi mới không hợp lệ.</span></div>
        <div><strong>SNAPSHOT_ONLY</strong><span>Không chặn; bộ máy chênh lệch phải xử lý biến động sau ảnh chụp.</span></div>
      </div>
    </section>
  );
};

const Đảo giao dịchMock = () => {
  const chain = [
    { ref: 'LED-2026-882177', label: 'MOVE gốc', delta: 'SRC -40 / DST +40', tone: 'neutral' as const, state: 'POSTED' },
    { ref: 'REV-2026-0108', label: 'Đảo giao dịch', delta: 'SRC +40 / DST -40', tone: 'warning' as const, state: 'PROPOSED' },
    { ref: 'COR-2026-0109', label: 'MOVE hiệu chỉnh', delta: 'SRC -40 / DST2 +40', tone: 'success' as const, state: 'NEXT' },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-09" aria-labelledby="inventory-inv-09-title">
      <Header
        id="INV-09"
        title="Inventory Đảo giao dịch / Corrective Chain"
        description="Mô phỏng sửa sai bằng giao dịch đảo và giao dịch hiệu chỉnh; sổ cái gốc luôn bất biến và giữ nguyên nguồn/tương quan."
        icon={<History size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Giao dịch gốc</span><strong>LED-2026-882177</strong><small>MOVE • POSTED</small></div>
        <div><span>Lý do</span><strong>WRONG_DESTINATION</strong><small>Đã đính kèm bằng chứng</small></div>
        <div><span>Tương quan</span><strong>CORR-9A10F</strong><small>Gốc + đảo + hiệu chỉnh</small></div>
        <div><span>Sổ cái gốc</span><strong>Bất biến</strong><small>Không sửa / xóa</small></div>
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
        <strong>Rào chắn khả năng đảo</strong>
        <span>Giao dịch đã đảo hoặc có phụ thuộc hạ nguồn không thể đảo an toàn phải bị chặn và chuyển sang quy trình hiệu chỉnh có kiểm soát.</span>
      </div>
    </section>
  );
};

const TraceabilityMock = () => {
  const nodes = [
    { ref: 'PO-2026-8831', type: 'Đơn mua', detail: 'Nhà cung cấp SUP-0008', tone: 'neutral' as const },
    { ref: 'GR-2026-1045', type: 'Phiếu nhập', detail: '+600 Cái', tone: 'success' as const },
    { ref: 'QC-2026-211', type: 'QC', detail: '590 chấp nhận • 10 hư hỏng', tone: 'warning' as const },
    { ref: 'MOV-2026-773', type: 'Di chuyển nội bộ', detail: 'KHU NHẬN → PICK-A', tone: 'neutral' as const },
    { ref: 'SHP-2026-5108', type: 'Lô giao hàng', detail: '180 Cái • 12 khách hàng', tone: 'success' as const },
    { ref: 'RMA-2026-044', type: 'Trả hàng', detail: '2 RMA đang mở', tone: 'warning' as const },
  ];
  return (
    <section className="inventory-capability-mock" data-testid="inventory-capability-mock-INV-10" aria-labelledby="inventory-inv-10-title">
      <Header
        id="INV-10"
        title="Đồ thị truy vết & phả hệ"
        description="Mô phỏng truy ngược/xuôi theo lô/sê-ri/chứng từ, thể hiện rõ phạm vi ảnh hưởng và trạng thái thiếu liên kết."
        icon={<GitBranch size={20} />}
      />
      <div className="inventory-mock-metrics">
        <div><span>Lô</span><strong>LOT-1001-260930</strong><small>SKU-1001</small></div>
        <div><span>Tồn hiện tại</span><strong>420 Cái</strong><small>3 vị trí</small></div>
        <div><span>Phạm vi đã giao</span><strong>180 Cái</strong><small>12 khách hàng</small></div>
        <div><span>Hàng trả đang mở</span><strong>2</strong><small>Nhánh RMA</small></div>
      </div>
      <div className="inventory-genealogy" aria-label="Truy vết phả hệ mô phỏng">
        {nodes.map((node,index) => (
          <article key={node.ref}>
            <span className={'inventory-genealogy-node ' + node.tone} aria-hidden="true" />
            <div><strong>{node.ref}</strong><span>{node.type}</span><small>{node.detail}</small></div>
            {index < nodes.length - 1 ? <span className="inventory-genealogy-link" aria-hidden="true">→</span> : null}
          </article>
        ))}
      </div>
      <div className="inventory-mock-callout danger">
        <strong>Rào chắn chuỗi chưa đầy đủ</strong>
        <span>Nếu một biến động thiếu liên kết nguồn/tương quan, giao diện phải hiển thị truy vết chưa đầy đủ; không được tự suy đoán phả hệ hoàn chỉnh cho thu hồi/phạm vi ảnh hưởng.</span>
      </div>
    </section>
  );
};

const InventoryCapabilityMock = ({ capabilityId }: InventoryMockProps) => {
  if (capabilityId === 'INV-05') return <Trạng tháiControlMock />;
  if (capabilityId === 'INV-06') return <LôSê-riMock />;
  if (capabilityId === 'INV-07') return <LockFreezeMock />;
  if (capabilityId === 'INV-09') return <Đảo giao dịchMock />;
  if (capabilityId === 'INV-10') return <TraceabilityMock />;
  return null;
};

export default InventoryCapabilityMock;
