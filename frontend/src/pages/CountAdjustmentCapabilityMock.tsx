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
        <span className="count-mock-kicker">KIỂM KÊ & ĐIỀU CHỈNH • {id} • MÔ PHỎNG GIAO DIỆN</span>
        <h2 id={'count-' + id.toLowerCase() + '-title'}>{title}</h2>
      </div>
    </div>
    <p>{description}</p>
    <div className="count-mock-scope">
      <Badge tone="neutral">Chỉ đọc</Badge>
      <span>Không gọi API hệ thống thật • Không ghi tồn kho • Không giả phê duyệt/ghi sổ</span>
    </div>
  </header>
);

const BlindCountMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-03" aria-labelledby="count-ct-03-title">
    <Header id="CT-03" title="Thực hiện kiểm kê mù" description="Người kiểm đếm chỉ thấy phạm vi và định danh tồn cần đếm; số lượng hệ thống được ẩn để giảm thiên lệch." icon={<EyeOff size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Phiếu kiểm kê</span><strong>CNT-2026-0098</strong><small>Lần đếm #1</small></div>
      <div><span>Vị trí</span><strong>A01-R02-L03-B04</strong><small>WH-HCM-01</small></div>
      <div><span>Số lượng hệ thống</span><strong>••••</strong><small>Ẩn với người kiểm đếm</small></div>
      <div><span>Thực đếm</span><strong>3 Thùng</strong><small>36 Cái UOM cơ sở</small></div>
    </div>
    <div className="count-blind-panel">
      <div><span>Định danh tồn kho</span><strong>SKU-1001 • LOT-261004-A</strong><small>UOM thao tác: Thùng • hệ số 12</small></div>
      <div><span>Phiếu kiểm kêer instruction</span><strong>Đếm thực tế, không suy đoán theo hệ thống</strong><small>Quét vị trí → quét SKU/lô → nhập số lượng thực đếm</small></div>
    </div>
    <div className="count-mock-callout warning"><strong>Rào chắn thiên lệch</strong><span>Số lượng hệ thống không được lộ qua helper text, placeholder, tooltip hay error message khi blind-count policy đang bật.</span></div>
  </section>
);

const FreezeStrategyMock = () => {
  const rows = [
    { strategy:'HARD_FREEZE', movement:'Chặn', picking:'Chặn', snapshot:'Có', status:'Nghiêm ngặt', tone:'danger' as const },
    { strategy:'SOFT_FREEZE', movement:'Có kiểm soát', picking:'Có điều kiện', snapshot:'Có', status:'Khuyến nghị', tone:'success' as const },
    { strategy:'SNAPSHOT_ONLY', movement:'Cho phép', picking:'Cho phép', snapshot:'Có', status:'Ít gián đoạn', tone:'neutral' as const },
  ];
  return (
    <section className="count-capability-mock" data-testid="count-capability-mock-CT-04" aria-labelledby="count-ct-04-title">
      <Header id="CT-04" title="Phiếu kiểm kê Freeze Chiến lược" description="So sánh Đóng băng cứng, Đóng băng mềm và Chỉ ảnh chụp trước khi mở phạm vi kiểm kê." icon={<LockKeyhole size={20} />} />
      <div className="count-mock-metrics">
        <div><span>Phiếu kiểm kê</span><strong>CNT-2026-0098</strong><small>Khu PICK-A</small></div>
        <div><span>Nhiệm vụ đang mở</span><strong>7</strong><small>2 đang thực hiện</small></div>
        <div><span>Chính sách đã chọn</span><strong>SOFT_FREEZE</strong><small>Hồ sơ kho</small></div>
        <div><span>Ảnh chụp</span><strong>16:10:24</strong><small>Mốc cơ sở có phiên bản</small></div>
      </div>
      <div className="count-mock-table-scroll"><table aria-label="So sánh chiến lược đóng băng kiểm kê mô phỏng"><thead><tr><th>Chiến lược</th><th>Biến động</th><th>Lấy hàng</th><th>Ảnh chụp</th><th>Mức phù hợp</th></tr></thead><tbody>
        {rows.map(r=><tr key={r.strategy}><td><strong>{r.strategy}</strong></td><td>{r.movement}</td><td>{r.picking}</td><td>{r.snapshot}</td><td><Badge tone={r.tone}>{r.status}</Badge></td></tr>)}
      </tbody></table></div>
      <div className="count-mock-callout warning"><strong>Kiểm tra xung đột</strong><span>Công việc đang mở, khóa tồn kho và điều chuyển đang hoạt động trong phạm vi phải được đánh giá trước khi đóng băng; không tự âm thầm hủy nhiệm vụ.</span></div>
    </section>
  );
};

const RecountMock = () => {
  const attempts = [
    { attempt:'#1', counter:'U-COUNTER-01', observed:'41 Cái', result:'Chênh lệch -7', tone:'warning' as const, state:'Đã gửi' },
    { attempt:'#2', counter:'U-COUNTER-02', observed:'43 Cái', result:'Chênh lệch -5', tone:'warning' as const, state:'Đã gửi' },
    { attempt:'Cuối cùng', counter:'Quản lý', observed:'43 Cái', result:'Đã chấp nhận', tone:'success' as const, state:'Cuối cùng' },
  ];
  return (
    <section className="count-capability-mock" data-testid="count-capability-mock-CT-05" aria-labelledby="count-ct-05-title">
      <Header id="CT-05" title="Kiểm đếm lại & lịch sử lần đếm bất biến" description="Mỗi lần đếm là một lần thử bất biến; kết quả cuối đã chấp nhận không ghi đè lịch sử trước đó." icon={<History size={20} />} />
      <div className="count-attempt-list">
        {attempts.map(a=><article key={a.attempt}><div><span>{a.attempt}</span><strong>{a.observed}</strong><small>{a.counter}</small></div><div><span>{a.result}</span><Badge tone={a.tone}>{a.state}</Badge></div></article>)}
      </div>
      <div className="count-mock-callout warning"><strong>Chính sách kiểm đếm lại</strong><span>Giữ người thực hiện, thời gian, số lượng thực đếm và lý do cho từng lần; không sửa lần đếm cũ để “khớp” kết quả cuối.</span></div>
    </section>
  );
};

const VarianceMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-06" aria-labelledby="count-ct-06-title">
    <Header id="CT-06" title="Bàn làm việc xử lý chênh lệch" description="Rà soát ảnh chụp so với kết quả đếm cuối, bằng chứng và ngưỡng để chuyển sang kiểm đếm lại, phê duyệt hoặc điều chỉnh." icon={<Scale size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Ảnh chụp</span><strong>48 Cái</strong><small>Mốc cơ sở đóng băng</small></div>
      <div><span>Cuối cùng count</span><strong>43 Cái</strong><small>Đã chấp nhận recount</small></div>
      <div><span>Chênh lệch</span><strong>-5 Cái</strong><small>-10,42%</small></div>
      <div><span>Ngưỡng</span><strong>±5%</strong><small>Cần phê duyệt</small></div>
    </div>
    <div className="count-resolution-flow">
      <article><span>1</span><div><strong>Rà soát bằng chứng</strong><small>2 lần đếm • ảnh • truy vết biến động</small></div><Badge tone="success">Hoàn tất</Badge></article>
      <article><span>2</span><div><strong>Ngưỡng check</strong><small>|10,42%| &gt; 5%</small></div><Badge tone="danger">Vượt ngưỡng</Badge></article>
      <article><span>3</span><div><strong>Quyết định</strong><small>Không còn cần kiểm đếm lại</small></div><Badge tone="warning">Phê duyệt</Badge></article>
      <article><span>4</span><div><strong>Bản nháp điều chỉnh</strong><small>Chênh lệch có dấu = -5</small></div><Badge tone="neutral">Chưa ghi sổ</Badge></article>
    </div>
  </section>
);

const AdjustmentMock = () => (
  <section className="count-capability-mock" data-testid="count-capability-mock-CT-07" aria-labelledby="count-ct-07-title">
    <Header id="CT-07" title="Inventory Điều chỉnh Phê duyệt & Posting" description="Mô phỏng phân tách nhiệm vụ, chênh lệch có dấu và ranh giới ghi sổ cho điều chỉnh do chênh lệch kiểm kê." icon={<ShieldCheck size={20} />} />
    <div className="count-mock-metrics">
      <div><span>Điều chỉnh</span><strong>ADJ-2026-0077</strong><small>COUNT_VARIANCE</small></div>
      <div><span>Chênh lệch có dấu</span><strong>-5 Cái</strong><small>SKU-1001 • B04</small></div>
      <div><span>Người tạo</span><strong>U-COUNTER-01</strong><small>Không được tự phê duyệt</small></div>
      <div><span>Người phê duyệt</span><strong>U-SUPERVISOR</strong><small>Người khác thực hiện</small></div>
    </div>
    <div className="count-adjustment-chain" aria-label="Chuỗi trạng thái điều chỉnh tồn kho mô phỏng">
      <div><Badge tone="neutral">DRAFT</Badge><span>Đã đính kèm lý do + bằng chứng</span></div>
      <div><Badge tone="warning">SUBMITTED</Badge><span>SoD / threshold validation</span></div>
      <div><Badge tone="success">APPROVED</Badge><span>Chỉ người phê duyệt được ủy quyền</span></div>
      <div><Badge tone="danger">POST</Badge><span>Ranh giới duy nhất thay đổi tồn kho</span></div>
    </div>
    <div className="count-mock-callout danger"><strong>Ranh giới ghi sổ</strong><span>Phê duyệt không đổi OnHand. Chỉ POST điều chỉnh mới tạo biến động tồn kho bất biến; thử lại phải idempotent và kiểm toán được.</span></div>
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
