import type { ReactNode } from 'react';
import { ArrowRightLeft, RefreshCw } from 'lucide-react';
import './Điều chuyểnCapabilityMock.css';

type Props = { capabilityId?: string };
type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const Badge = ({ tone, children }: { tone: Tone; children: ReactNode }) => (
  <span className={'transfer-mock-badge ' + tone}>{children}</span>
);

const Header = ({ id, title, description, icon }: { id: string; title: string; description: string; icon: ReactNode }) => (
  <header className="transfer-mock-header">
    <div className="transfer-mock-title-row">
      <span className="transfer-mock-icon" aria-hidden="true">{icon}</span>
      <div><span className="transfer-mock-kicker">ĐIỀU CHUYỂN & BỔ SUNG • {id} • MÔ PHỎNG GIAO DIỆN</span><h2>{title}</h2></div>
    </div>
    <p>{description}</p>
    <div className="transfer-mock-scope"><Badge tone="neutral">Chỉ đọc</Badge><span>Không gọi API hệ thống thật • Không ghi tồn kho • Không giả ghi sổ</span></div>
  </header>
);

const TransitMock = () => {
  const checkpoints = [
    { label: 'Nguồn trước khi xuất chuyển', qty: '520 Gói', state: 'Đã ghi nhận', tone: 'neutral' as const },
    { label: 'Nguồn sau khi xuất chuyển', qty: '320 Gói', state: '-200', tone: 'warning' as const },
    { label: 'Đang vận chuyển', qty: '200 Gói', state: 'IN_TRANSIT', tone: 'success' as const },
    { label: 'Đích đã nhận', qty: '0 Gói', state: 'Đang chờ', tone: 'neutral' as const },
  ];
  return (
    <section className="transfer-capability-mock" data-testid="transfer-capability-mock-TR-02">
      <Header id="TR-02" title="Đối chiếu tồn kho đang vận chuyển" description="Mô phỏng bảo toàn số lượng qua nguồn → đang vận chuyển → đích theo tham chiếu điều chuyển và chủ sở hữu." icon={<ArrowRightLeft size={20} />} />
      <div className="transfer-mock-metrics">
        <div><span>Điều chuyển</span><strong>TRF-2026-0024</strong><small>WH-HCM-01 → WH-DN-01</small></div>
        <div><span>Đã xuất chuyển</span><strong>200 Gói</strong><small>Đã ghi sổ nguồn</small></div>
        <div><span>Đang vận chuyển</span><strong>200 Gói</strong><small>Chủ sở hữu OWN-COMPANY-01</small></div>
        <div><span>Chênh lệch</span><strong>0</strong><small>Bảo toàn khớp</small></div>
      </div>
      <div className="transfer-conservation-grid">
        {checkpoints.map((item,index) => (
          <article key={item.label}>
            <div><span>{item.label}</span><strong>{item.qty}</strong></div>
            <Badge tone={item.tone}>{item.state}</Badge>
            {index < checkpoints.length - 1 ? <span className="transfer-arrow" aria-hidden="true">→</span> : null}
          </article>
        ))}
      </div>
      <div className="transfer-mock-callout warning">
        <strong>Quy tắc bảo toàn</strong>
        <span>Điều chuyển quantity phải bảo toàn giữa source, transit và destination. Mismatch không được “fix” bằng sửa balance trực tiếp; phải reconcile qua canonical transfer/inventory commands.</span>
      </div>
    </section>
  );
};

const ReplenishmentMock = () => {
  const sources = [
    { location: 'RESERVE-A-04', eligible: '240 Cái', distance: '28 m', score: 91, result: 'Đã chọn', tone: 'success' as const },
    { location: 'RESERVE-B-01', eligible: '120 Cái', distance: '19 m', score: 76, result: 'Chỉ một phần', tone: 'warning' as const },
    { location: 'QC-HOLD-02', eligible: '0', distance: '17 m', score: 0, result: 'Bị loại', tone: 'danger' as const },
  ];
  return (
    <section className="transfer-capability-mock" data-testid="transfer-capability-mock-TR-05">
      <Header id="TR-05" title="Kế hoạch nguồn bổ sung & vị trí lấy hàng" description="Mô phỏng nhu cầu min/max, nguồn dự trữ đủ điều kiện, sức chứa và ngữ nghĩa di chuyển nội bộ cho vị trí lấy hàng." icon={<RefreshCw size={20} />} />
      <div className="transfer-mock-metrics">
        <div><span>Vị trí lấy hàng</span><strong>PICK-A-01</strong><small>SKU-1001</small></div>
        <div><span>Hiện tại / Min / Max</span><strong>60 / 60 / 240</strong><small>Cái</small></div>
        <div><span>Đề xuất</span><strong>180 Cái</strong><small>15 Thùng × 12</small></div>
        <div><span>Dư địa vị trí đích</span><strong>204 Cái</strong><small>Sức chứa cho phép di chuyển</small></div>
      </div>
      <div className="transfer-mock-table-scroll">
        <table aria-label="Các nguồn bổ sung ứng viên mô phỏng">
          <thead><tr><th>Nguồn dự trữ</th><th>Đủ điều kiện</th><th>Di chuyển</th><th>Điểm</th><th>Quyết định</th></tr></thead>
          <tbody>{sources.map(item => <tr key={item.location}><td><strong>{item.location}</strong></td><td>{item.eligible}</td><td>{item.distance}</td><td>{item.score || '—'}</td><td><Badge tone={item.tone}>{item.result}</Badge></td></tr>)}</tbody>
        </table>
      </div>
      <div className="transfer-replenish-flow">
        <div><span>1</span><strong>Phát hiện nhu cầu</strong><small>Vị trí lấy hàng at/below min.</small></div>
        <div><span>2</span><strong>Nguồn đủ điều kiện</strong><small>Đã kiểm tra trạng thái/khóa/chủ sở hữu.</small></div>
        <div><span>3</span><strong>Đã kiểm tra sức chứa</strong><small>Vị trí đích có thể nhận 180.</small></div>
        <div><span>4</span><strong>Tạo nhiệm vụ di chuyển</strong><small>Ghi sổ bảo toàn tổng số lượng kho.</small></div>
      </div>
    </section>
  );
};

const Điều chuyểnCapabilityMock = ({ capabilityId }: Props) => {
  if (capabilityId === 'TR-02') return <TransitMock />;
  if (capabilityId === 'TR-05') return <ReplenishmentMock />;
  return null;
};

export default Điều chuyểnCapabilityMock;
