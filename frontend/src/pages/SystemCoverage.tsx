import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, CircleAlert, Filter, Search, ShieldCheck } from 'lucide-react';
import { blueprintStatusLabels, erpWmsBlueprint, type BlueprintStatus } from '../config/erpWmsBlueprint';
import {
  evidenceStatusLabels,
  getBlueprintGovernanceRows,
  platformStandards,
  type Applicability,
  type EvidenceStatus,
  type ReleaseWave,
} from '../config/capabilityGovernance';
import { getCanonicalDocumentationRegister } from '../config/documentationRegister';
import './SystemCoverage.css';

const representationLabel = (value: string) => ({
  'Capability-linked': 'Liên kết chức năng',
  'Capability-backed': 'Được hỗ trợ bởi chức năng',
  'Platform guardrail': 'Rào chắn nền tảng',
  'Runbook-backed': 'Có sổ tay vận hành',
  'Platform / Governance': 'Nền tảng / Quản trị',
}[value] ?? value);

const categoryLabel = (value: string) => ({
  'Data Platform': 'Nền tảng dữ liệu',
  Engineering: 'Kỹ thuật',
  'UX / Product': 'UX / Sản phẩm',
  'Security / Compliance': 'Bảo mật / Tuân thủ',
  'Operations / Governance': 'Vận hành / Quản trị',
  'Foundation / Core Design': 'Nền tảng / Thiết kế cốt lõi',
  'WMS Domain': 'Nghiệp vụ WMS',
  'Platform Architecture': 'Kiến trúc nền tảng',
  'Delivery / Operations / Finance': 'Bàn giao / Vận hành / Tài chính',
  Integration: 'Tích hợp',
  'Planning / Governance': 'Lập kế hoạch / Quản trị',
  'Frontend / UX': 'Frontend / UX',
  'Advanced WMS / Data': 'WMS nâng cao / Dữ liệu',
  'Platform / Product Governance': 'Nền tảng / Quản trị sản phẩm',
}[value] ?? value);

const applicabilityLabels: Record<Applicability, string> = {
  REQUIRED_CORE: 'Bắt buộc cốt lõi',
  REQUIRED_WHEN_FEATURE_ENABLED: 'Bắt buộc khi bật chức năng',
  INDUSTRY_OPTIONAL: 'Tùy chọn theo ngành',
  IMPLEMENTATION_SPECIFIC: 'Phụ thuộc cách triển khai',
};

const evidenceClass = (status: EvidenceStatus) => 'evidence-badge evidence-' + status.replace('spec-only', 'spec');

const SystemCoverage = () => {
  const rows = useMemo(() => getBlueprintGovernanceRows(erpWmsBlueprint), []);
  const documentation = useMemo(() => getCanonicalDocumentationRegister(erpWmsBlueprint), []);
  const capabilityLinkedDocs = documentation.filter((item) => item.representation === 'Capability-linked');
  const platformDocs = documentation.filter((item) => item.representation === 'Platform / Governance');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState<BlueprintStatus | 'all'>('all');
  const [wave, setWave] = useState<ReleaseWave | 'all'>('all');
  const [applicability, setApplicability] = useState<Applicability | 'all'>('all');

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('vi');
    return rows.filter(({ module, capability, profile }) => {
      const queryMatch = !q || [
        capability.id,
        capability.name,
        capability.goal,
        capability.spec,
        module.name,
        profile.screenReference ?? '',
      ].join(' ').toLocaleLowerCase('vi').includes(q);
      return queryMatch
        && (status === 'all' || capability.status === status)
        && (wave === 'all' || profile.releaseWave === wave)
        && (applicability === 'all' || profile.applicability === applicability);
    });
  }, [applicability, query, rows, status, wave]);

  const reviewRequired = rows.filter((row) => row.profile.reviewStatus === 'Review Required');
  const traceabilityClosed = rows.filter((row) => row.profile.reviewStatus === 'Traceability Closed');
  const implemented = rows.filter((row) => row.capability.status === 'live').length;
  const foundation = rows.filter((row) => row.capability.status === 'foundation').length;
  const specOnly = rows.length - implemented - foundation;

  return (
    <div className="coverage-page">
      <Link to="/system-blueprint" className="coverage-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="coverage-hero">
        <div>
          <span className="coverage-eyebrow"><ShieldCheck size={16} /> Chuỗi hoàn thiện chuẩn</span>
          <h1>Độ phủ & mức sẵn sàng</h1>
          <p>
            Tách rõ độ phủ tài liệu, độ phủ UI của bản thiết kế và bằng chứng triển khai hệ thống thật.
            Có đặc tả không đồng nghĩa chức năng đã sẵn sàng trên hệ thống thật.
          </p>
        </div>
        <div className="coverage-score">
          <strong>{rows.length}</strong>
          <span>chức năng đã truy vết</span>
        </div>
      </section>

      <section className="coverage-kpis">
        <article><strong>{implemented}</strong><span>Hệ thống thật hoàn thiện phạm vi hiện tại</span></article>
        <article><strong>{foundation}</strong><span>Hệ thống thật đã triển khai một phần</span></article>
        <article><strong>{specOnly}</strong><span>Chưa triển khai / tùy chọn</span></article>
        <article><strong>{rows.length}</strong><span>Bản mô phỏng hệ thống tương tác</span></article>
        <article className="traceability-kpi" title={rows.length + ' chức năng có bản xem trước tương tác theo ngữ cảnh'}><strong>{traceabilityClosed.length}</strong><span>Truy vết màn hình đã đóng</span></article>
        <article><strong>{documentation.length}</strong><span>Đặc tả chuẩn đã lập chỉ mục</span></article>
        <article><strong>{platformDocs.length}</strong><span>Tài liệu nền tảng / quản trị</span></article>
      </section>

      <section className="completion-chain">
        <span>Vai trò / Yêu cầu</span><b>→</b><span>UX / Màn hình</span><b>→</b><span>Quyền</span><b>→</b>
        <span>Lệnh / API</span><b>→</b><span>Trạng thái</span><b>→</b><span>Ảnh hưởng tồn kho</span><b>→</b>
        <span>Sự kiện / Lỗi</span><b>→</b><span>Kiểm thử</span><b>→</b><span>Vận hành / Bằng chứng phát hành</span>
      </section>

      <section className="coverage-toolbar">
        <label><Search size={15} /><input aria-label="Tìm độ phủ" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Chức năng, phân hệ, đặc tả, màn hình..." /></label>
        <label><Filter size={15} /><select aria-label="Lọc trạng thái triển khai" value={status} onChange={(e) => setStatus(e.target.value as BlueprintStatus | 'all')}>
          <option value="all">Tất cả mức triển khai</option>
          <option value="live">Hệ thống thật hoàn thiện phạm vi hiện tại</option>
          <option value="foundation">Hệ thống thật đã triển khai một phần</option>
          <option value="planned">Chưa triển khai hệ thống thật</option>
          <option value="optional">Tùy chọn</option>
        </select></label>
        <label><select aria-label="Lọc đợt phát hành" value={wave} onChange={(e) => setWave(e.target.value === 'all' ? 'all' : Number(e.target.value) as ReleaseWave)}>
          <option value="all">Tất cả đợt</option>
          {[0,1,2,3,4,5,6].map((item) => <option key={item} value={item}>Đợt {item}</option>)}
        </select></label>
        <label><select aria-label="Lọc phạm vi áp dụng" value={applicability} onChange={(e) => setApplicability(e.target.value as Applicability | 'all')}>
          <option value="all">Tất cả phạm vi áp dụng</option>
          {Object.entries(applicabilityLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select></label>
      </section>

      {traceabilityClosed.length > 0 && (
        <section className="traceability-closed-panel">
          <div className="traceability-title"><CheckCircle2 size={17} /><div><strong>Ma trận màn hình 229 • Đã đóng truy vết</strong><span>4 khoảng trống màn hình lịch sử đã được ánh xạ tới route bản thiết kế cụ thể. Trạng thái hệ thống thật/phía máy chủ vẫn được theo dõi riêng.</span></div></div>
          <div className="traceability-grid">
            {traceabilityClosed.map(({ module, capability, profile }) => (
              <Link key={capability.id} to={'/system-blueprint/' + module.key + '/' + capability.id}>
                <strong>{capability.id} • {capability.name}</strong>
                <span>{profile.screenReference}</span>
                <small>{profile.reviewFinding}</small>
              </Link>
            ))}
          </div>
        </section>
      )}

      {reviewRequired.length > 0 && (
        <section className="review-required-panel">
          <div className="review-title"><CircleAlert size={17} /><div><strong>Rà soát màn hình còn mở</strong><span>Các mục này vẫn cần ánh xạ/bằng chứng trước khi đóng truy vết.</span></div></div>
          <div className="review-grid">
            {reviewRequired.map(({ module, capability, profile }) => (
              <Link key={capability.id} to={'/system-blueprint/' + module.key + '/' + capability.id}>
                <strong>{capability.id} • {capability.name}</strong>
                <span>{profile.screenReference}</span>
                <small>{profile.reviewFinding}</small>
              </Link>
            ))}
          </div>
        </section>
      )}

      <section className="platform-standards-panel">
        <div className="platform-standards-title">
          <div><strong>Sổ đăng ký tài liệu chuẩn • Đặc tả 1–282</strong><span>Toàn bộ tài liệu Notion được lập chỉ mục; tài liệu không phải chức năng nghiệp vụ vẫn hiện như rào chắn nền tảng/quản trị.</span></div>
          <span>{capabilityLinkedDocs.length} liên kết chức năng • {platformDocs.length} nền tảng/quản trị</span>
        </div>
        <details className="documentation-details">
          <summary>Xem toàn bộ {documentation.length} tài liệu chuẩn</summary>
          <div className="documentation-table-wrap">
            <table className="documentation-table">
              <thead><tr><th>Đặc tả</th><th>Tiêu đề</th><th>Phân loại</th><th>Cách biểu diễn</th><th>Mã chức năng</th></tr></thead>
              <tbody>
                {documentation.map((item) => (
                  <tr key={item.spec}>
                    <td>{item.spec}</td>
                    <td>{item.displayTitle}</td>
                    <td>{categoryLabel(item.category)}</td>
                    <td><span className={'doc-representation ' + (item.representation === 'Capability-linked' ? 'doc-linked' : 'doc-standard')}>{representationLabel(item.representation)}</span></td>
                    <td>{item.capabilityIds.length ? item.capabilityIds.join(', ') : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
        <div className="platform-standards-grid">
          {platformStandards.map((item) => (
            <article key={item.spec}>
              <span>Đặc tả {item.spec} • {categoryLabel(item.category)}</span>
              <strong>{item.displayTitle}</strong>
              <small>{representationLabel(item.representation)}</small>
              {item.mappedCapabilityIds?.length ? <p>Đã ánh xạ: {item.mappedCapabilityIds.join(', ')}</p> : null}
            </article>
          ))}
        </div>
      </section>

      <section className="coverage-table-wrap">
        <table className="coverage-table" data-testid="coverage-table">
          <thead>
            <tr>
              <th>Chức năng</th>
              <th>Phân hệ sở hữu</th>
              <th>Đợt</th>
              <th>Phạm vi áp dụng</th>
              <th>Mức trưởng thành</th>
              <th>Triển khai</th>
              <th>Đặc tả</th>
              <th>Chuỗi bằng chứng</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map(({ module, capability, profile }) => (
              <tr key={capability.id}>
                <td>
                  <Link to={'/system-blueprint/' + module.key + '/' + capability.id}>{capability.id} • {capability.name}</Link>
                  {profile.reviewStatus === 'Review Required' && <span className="review-chip">Cần rà soát</span>}
                  {profile.reviewStatus === 'Traceability Closed' && <span className="traceability-chip">Đã đóng truy vết</span>}
                </td>
                <td>{profile.ownerModule}</td>
                <td>Đợt {profile.releaseWave}</td>
                <td>{applicabilityLabels[profile.applicability]}</td>
                <td>{profile.maturity}</td>
                <td><span className={'impl-chip impl-' + capability.status}>{blueprintStatusLabels[capability.status]}</span></td>
                <td>{profile.referencedSpecs.join(', ')}</td>
                <td>
                  <div className="evidence-mini">
                    {profile.evidence.map((item) => (
                      <span title={item.label + ': ' + item.note} className={evidenceClass(item.status)} key={item.key}>
                        {item.label}: {evidenceStatusLabels[item.status]}
                      </span>
                    ))}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {filtered.length === 0 && <div className="coverage-empty">Không có chức năng phù hợp bộ lọc.</div>}
      </section>

      <section className="coverage-legend">
        <div><CheckCircle2 size={14} /><span><b>Đã có bằng chứng</b> = có bằng chứng phù hợp ở lớp bản thiết kế/triển khai hiện tại.</span></div>
        <div><CircleAlert size={14} /><span><b>Một phần / Chỉ có đặc tả</b> = đã có tài liệu hoặc nền tảng nhưng chưa được phép hiểu là sẵn sàng trên hệ thống thật.</span></div>
      </section>
    </div>
  );
};

export default SystemCoverage;
