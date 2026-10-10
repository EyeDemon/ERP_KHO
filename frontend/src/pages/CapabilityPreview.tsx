import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, Boxes, CheckCircle2, CircleDashed, Database, ExternalLink,
  FileText, LockKeyhole, MonitorSmartphone, ShieldCheck, Smartphone, Workflow, Gauge
} from 'lucide-react';
import {
  blueprintStatusLabels,
  findBlueprintModule,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import { getMockCapabilityFixture, getMockWorkCenter } from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import { evidenceStatusLabels, getCapabilityGovernanceProfile } from '../config/capabilityGovernance';
import { getSpecializedScreenPreview } from '../config/reviewRequiredScreens';
import { mockDisplayText, mockStatusLabel, mockTypeLabel } from '../utils/mockDisplayLabels';
import CapabilityInteractiveDemo from './CapabilityInteractiveDemo';
import WarehouseCapabilityMock from './WarehouseCapabilityMock';
import InboundCapabilityMock from './InboundCapabilityMock';
import OutboundCapabilityMock from './OutboundCapabilityMock';
import InventoryCapabilityMock from './InventoryCapabilityMock';
import TransferCapabilityMock from './TransferCapabilityMock';
import CountAdjustmentCapabilityMock from './CountAdjustmentCapabilityMock';
import QualityReturnsCapabilityMock from './QualityReturnsCapabilityMock';
import './CapabilityPreview.css';

const statusIcon = (status: BlueprintStatus) =>
  status === 'live' ? <CheckCircle2 size={15} /> : <CircleDashed size={15} />;

const applicabilityLabel = (value: string) => ({
  REQUIRED_CORE: 'Bắt buộc cốt lõi',
  REQUIRED_WHEN_FEATURE_ENABLED: 'Bắt buộc khi bật chức năng',
  INDUSTRY_OPTIONAL: 'Tùy chọn theo ngành',
  IMPLEMENTATION_SPECIFIC: 'Phụ thuộc cách triển khai',
}[value] ?? value);

const reviewStatusLabel = (value: string) => ({
  'Traceability Closed': 'Đã đóng truy vết',
  'Review Required': 'Cần rà soát',
}[value] ?? value);

const surfaceLabel = (value: string) => ({
  Web: 'Web',
  API: 'API',
  Mobile: 'Di động',
  Worker: 'Tiến trình nền',
}[value] ?? value);


const CapabilityPreview = () => {
  const { moduleKey, capabilityId } = useParams();
  const demo = useMockDemo();
  const module = findBlueprintModule(moduleKey);
  const capability = module?.capabilities.find((item) => item.id === capabilityId);
  const workCenter = getMockWorkCenter(moduleKey);
  const fixture = getMockCapabilityFixture(capabilityId);
  const governance = module && capability ? getCapabilityGovernanceProfile(module, capability) : undefined;
  const specializedPreview = getSpecializedScreenPreview(capabilityId);
  const records = (workCenter?.records ?? []).filter((record) => demo.canSeeWarehouse(record.warehouse));
  const fixtureVisible = fixture ? demo.canSeeWarehouse(fixture.sampleWarehouse) : false;

  if (!module || !capability) {
    return (
      <div className="capability-not-found">
        <h1>Không tìm thấy chức năng</h1>
        <Link to="/system-blueprint">Quay lại bản đồ hệ thống</Link>
      </div>
    );
  }

  return (
    <div className="capability-page">
      <Link to={'/system-blueprint/' + module.key} className="capability-back">
        <ArrowLeft size={16} /> {module.name}
      </Link>

      <section className="capability-hero">
        <div>
          <span className="capability-eyebrow">{capability.id} • Xem trước chức năng</span>
          <h1>{capability.name}</h1>
          <p>{capability.goal}</p>
          <div className="capability-meta">
            <span className={'status-pill ' + capability.status}>{statusIcon(capability.status)} {blueprintStatusLabels[capability.status]}</span>
            <span>Đặc tả {capability.spec}</span>
            {governance && <span>Đợt {governance.releaseWave}</span>}
            {governance && <span>{applicabilityLabel(governance.applicability)}</span>}
            {governance && <span>{governance.maturity}</span>}
            {capability.surfaces.map((surface) => (
              <span key={surfaceLabel(surface)}>
                {surface === 'Mobile' ? <Smartphone size={13} /> : <MonitorSmartphone size={13} />}
                {surface}
              </span>
            ))}
          </div>
        </div>
        {capability.mockRoute ? (
          <Link to={capability.mockRoute} className="capability-real-link">
            <ExternalLink size={16} aria-hidden="true" />
            Mở mô phỏng chuyên biệt
          </Link>
        ) : capability.route ? (
          <span className="capability-preview-badge">ĐÃ CÓ TRÊN HỆ THỐNG THẬT</span>
        ) : (
          <span className="capability-preview-badge">MÔ PHỎNG / XEM TRƯỚC ĐẶC TẢ</span>
        )}
      </section>

      {capability.id === 'INV-11' && (
        <section className="capability-panel" aria-label="Nguồn dữ liệu mô phỏng INV-11">
          <p role="note">
            Số liệu đối chiếu INV-11 là kịch bản minh họa độc lập của Blueprint.
            Không phải số dư Ledger/Balance thời gian thực và không dùng để đối chiếu trực tiếp
            với bản ghi RECON-HCM-0930 hoặc màn Real demo.
          </p>
        </section>
      )}

      {module.flow && (
        <section className="capability-panel">
          <div className="capability-panel-title"><Workflow size={18} /><h2>Luồng nghiệp vụ liên quan</h2></div>
          <div className="capability-flow">
            {module.flow.map((step, index) => (
              <div key={step}><span>{index + 1}</span><strong>{step}</strong></div>
            ))}
          </div>
        </section>
      )}

      <WarehouseCapabilityMock capabilityId={capability.id} />
      <InboundCapabilityMock capabilityId={capability.id} />
      <OutboundCapabilityMock capabilityId={capability.id} />
      <InventoryCapabilityMock capabilityId={capability.id} />
      <TransferCapabilityMock capabilityId={capability.id} />
      <CountAdjustmentCapabilityMock capabilityId={capability.id} />
      <QualityReturnsCapabilityMock capabilityId={capability.id} />

      {specializedPreview && (
        <section className="capability-panel specialized-preview">
          <div className="capability-panel-title"><CheckCircle2 size={18} /><h2>{specializedPreview.title}</h2><span className="traceability-closed-badge">Ma trận màn hình • Đã đóng truy vết</span></div>
          <p className="specialized-subtitle">{specializedPreview.subtitle}</p>
          <div className="specialized-fields">
            {specializedPreview.fields.map((field) => (
              <div key={field.label}><span>{field.label}</span><strong>{field.value}</strong>{field.helper ? <small>{field.helper}</small> : null}</div>
            ))}
          </div>
          <div className="specialized-columns">
            <div><h3>Các bước thực hiện</h3><ol>{specializedPreview.steps.map((step) => <li key={step}>{step}</li>)}</ol></div>
            <div><h3>Kiểm tra / chính sách</h3><ul>{specializedPreview.validations.map((rule) => <li key={rule}>{rule}</li>)}</ul></div>
          </div>
          <div className="specialized-boundary"><strong>Ranh giới tồn kho</strong><span>{specializedPreview.inventoryBoundary}</span></div>
          <div className="specialized-boundary"><strong>Phân quyền</strong><span>{specializedPreview.permissionNote}</span></div>
          <div className="specialized-screen-ref">{specializedPreview.screenReference}</div>
        </section>
      )}

      <CapabilityInteractiveDemo
        capability={capability}
        moduleName={module.name}
        moduleFlow={module.flow}
        sampleReference={fixtureVisible ? fixture?.sampleReference : records[0]?.reference}
        sampleWarehouse={fixtureVisible ? fixture?.sampleWarehouse : records[0]?.warehouse}
        recordCount={records.length}
      />

      <section className="capability-grid-layout">
        <div className="capability-main">
          <section className="capability-panel">
            <div className="capability-panel-title"><FileText size={18} /><h2>Xem trước màn hình</h2></div>
            <div className="preview-toolbar">
              <input aria-label="Tìm dữ liệu mô phỏng của chức năng" placeholder="Tìm mã / SKU / chứng từ..." readOnly value="" />
              <select aria-label="Kho mô phỏng"><option>Tất cả kho được phép</option></select>
              <button type="button" disabled>Mô phỏng chỉ đọc</button>
            </div>

            <div className="capability-kpis">
              <div><span>Bản ghi mẫu</span><strong>{records.length}</strong></div>
              <div><span>Phân hệ</span><strong>{module.name}</strong></div>
              <div><span>Kênh sử dụng</span><strong>{capability.surfaces.map(surfaceLabel).join(' / ')}</strong></div>
              <div><span>Bộ dữ liệu mẫu</span><strong>{fixture?.fixtureId ?? '—'}</strong></div>
            </div>

            {fixture && (
              <div className="capability-fixture-trace">
                <strong>Bộ dữ liệu mẫu của chức năng:</strong>
                <span>{fixture.fixtureId}</span>
                {fixtureVisible ? (
                  <>
                    <span>{fixture.sampleReference}</span>
                    <span>{fixture.sampleWarehouse}</span>
                    <span>{mockStatusLabel(fixture.sampleStatus)}</span>
                  </>
                ) : (
                  <span>Bản ghi mẫu bị ẩn bởi phạm vi kho mô phỏng</span>
                )}
              </div>
            )}

            <div className="capability-records">
              {records.slice(0, 5).map((record) => (
                <article key={record.id}>
                  <div>
                    <span>{mockTypeLabel(record.type)}</span>
                    <strong>{record.reference}</strong>
                    <p>{mockDisplayText(record.subject)}</p>
                  </div>
                  <div>
                    <small>{record.warehouse}</small>
                    <span className={'cap-record-status tone-' + record.tone}>{mockStatusLabel(record.status)}</span>
                  </div>
                </article>
              ))}
            </div>
          </section>

          {capability.surfaces.includes('Mobile') && (
            <section className="capability-panel">
              <div className="capability-panel-title"><Smartphone size={18} /><h2>Xem trước luồng quét trên di động</h2></div>
              <div className="mobile-preview-shell">
                <div className="mobile-preview-top">
                  <strong>ERP WMS</strong>
                  <span>{fixtureVisible ? fixture?.sampleWarehouse : (records[0]?.warehouse ?? 'Không có kho trong phạm vi')}</span>
                </div>
                <div className="mobile-preview-body">
                  <span className="mobile-task-label">NHIỆM VỤ / {capability.id}</span>
                  <h3>{capability.name}</h3>
                  <div className="mobile-scan-box">▣ Quét mã vạch / vị trí / sê-ri</div>
                  <div className="mobile-record-card">
                    <small>Tham chiếu</small>
                    <strong>{fixtureVisible ? fixture?.sampleReference : (records[0]?.reference ?? '—')}</strong>
                    <span>{records[0]?.subject ? mockDisplayText(records[0].subject) : capability.goal}</span>
                  </div>
                  <div className="mobile-quantity-row">
                    <span className="mobile-quantity-label">Số lượng</span>
                    <div><strong>{records[0]?.quantity ?? '—'}</strong><span>{records[0]?.uom ?? 'UOM'}</span></div>
                  </div>
                  <button type="button" disabled>Xác nhận • Mô phỏng chỉ đọc</button>
                </div>
              </div>
            </section>
          )}

          {governance && (
            <section className="capability-panel">
              <div className="capability-panel-title"><Gauge size={18} /><h2>Quản trị & mức hoàn thiện chức năng</h2></div>
              <div className="governance-summary">
                <div><span>Phân hệ sở hữu</span><strong>{governance.ownerModule}</strong></div>
                <div><span>Đợt phát hành</span><strong>Đợt {governance.releaseWave}</strong></div>
                <div><span>Phạm vi áp dụng</span><strong>{applicabilityLabel(governance.applicability)}</strong></div>
                <div><span>Mức trưởng thành</span><strong>{governance.maturity}</strong></div>
                <div><span>Ma trận màn hình</span><strong>{reviewStatusLabel(governance.reviewStatus)}</strong></div>
                <div><span>Đặc tả</span><strong>{governance.referencedSpecs.join(', ')}</strong></div>
              </div>
              <div className="governance-evidence">
                {governance.evidence.map((item) => (
                  <div key={item.key} className={'governance-evidence-item evidence-' + item.status.replace('spec-only', 'spec')}>
                    <strong>{item.label}</strong><span>{evidenceStatusLabels[item.status]}</span><small>{item.note}</small>
                  </div>
                ))}
              </div>
              {governance.reviewFinding ? <div className={'governance-review-note ' + (governance.reviewStatus === 'Traceability Closed' ? 'traceability-closed-note' : '')}><CheckCircle2 size={14} /> {governance.reviewFinding}</div> : null}
            </section>
          )}

          <section className="capability-panel">
            <div className="capability-panel-title"><ShieldCheck size={18} /><h2>Các trạng thái UX dự kiến</h2></div>
            <div className="ux-state-grid">
              <div><strong>Mặc định</strong><span>Danh sách/chi tiết theo quyền và phạm vi kho.</span></div>
              <div><strong>Đang tải</strong><span>Giữ bố cục ổn định, không cho lặp thao tác quan trọng.</span></div>
              <div><strong>Không có dữ liệu</strong><span>Giải thích vì sao không có dữ liệu và hành động hợp lệ tiếp theo.</span></div>
              <div><strong>Kiểm tra dữ liệu</strong><span>Kiểm tra theo từng trường; số lượng luôn rõ UOM/base UOM.</span></div>
              <div><strong>Xung đột</strong><span>409/412 hiển thị hướng phục hồi và tải lại trạng thái mới.</span></div>
              <div><strong>Không có quyền</strong><span>Phân quyền phía máy chủ là nguồn quyết định cuối.</span></div>
              <div><strong>Ngoại lệ</strong><span>Giữ ngữ cảnh/bằng chứng/lý do và chuyển sang quy trình xử lý.</span></div>
              <div><strong>Thành công</strong><span>Tải lại chi tiết chuẩn, hiển thị tham chiếu kiểm toán/sự kiện khi cần.</span></div>
            </div>
          </section>
        </div>

        <aside className="capability-contract">
          <span className="capability-eyebrow">Hợp đồng kỹ thuật</span>
          <h3>Rào chắn bắt buộc</h3>
          <div className="contract-chain">
            <div><ShieldCheck size={17} /><span><strong>Phân quyền</strong><small>Quyền + phạm vi kho</small></span></div>
            <div><Workflow size={17} /><span><strong>Máy trạng thái</strong><small>Lệnh tường minh; không đặt trạng thái tùy ý</small></span></div>
            <div><LockKeyhole size={17} /><span><strong>Đồng thời</strong><small>Phiên bản + bảo vệ tồn kho</small></span></div>
            <div><Boxes size={17} /><span><strong>Toàn vẹn tồn kho</strong><small>Không thay đổi số dư tùy tiện</small></span></div>
            <div><Database size={17} /><span><strong>Sổ cái</strong><small>Ghi sổ bất biến / đảo giao dịch</small></span></div>
            <div><FileText size={17} /><span><strong>Kiểm toán & Outbox</strong><small>Bằng chứng nguyên tử khi có thao tác thay đổi quan trọng</small></span></div>
          </div>

          {governance && (
            <div className="technical-trace">
              <div><strong>Quyền</strong><span>{governance.permissionModel}</span></div>
              <div><strong>Lệnh / API</strong><span>{governance.commandApiModel}</span></div>
              <div><strong>Mô hình trạng thái</strong><span>{governance.stateModel}</span></div>
              <div><strong>Ảnh hưởng tồn kho</strong><span>{governance.inventoryEffect}</span></div>
            </div>
          )}

          <div className="capability-contract-note">
            <strong>Ý nghĩa bản xem trước:</strong> trang này minh họa UX/hợp đồng của chức năng dựa trên đặc tả.
            Nếu trạng thái là “Theo đặc tả” hoặc “Nâng cao”, phía máy chủ chưa được coi là sẵn sàng trên hệ thống thật.
          </div>
        </aside>
      </section>
    </div>
  );
};

export default CapabilityPreview;
