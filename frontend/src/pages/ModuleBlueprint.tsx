import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, ArrowRight, Boxes, CheckCircle2, CircleDashed, Database, ExternalLink,
  FileCheck2, LockKeyhole, MonitorSmartphone, Route, ShieldCheck, Smartphone, Workflow
} from 'lucide-react';
import {
  blueprintStatusLabels,
  findBlueprintModule,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import {
  getMockWorkCenter,
  type MockOperationalRecord,
} from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import './SystemBlueprint.css';
import './ModuleBlueprint.css';

const statusIcon = (status: BlueprintStatus) => {
  if (status === 'live') return <CheckCircle2 size={15} />;
  return <CircleDashed size={15} />;
};

const qtyText = (record: MockOperationalRecord) =>
  record.quantity == null ? '—' : `${record.quantity.toLocaleString('vi-VN')} ${record.uom ?? ''}`.trim();

const localTime = (iso: string) => new Date(iso).toLocaleString('vi-VN');

const ModuleBlueprint = () => {
  const { moduleKey } = useParams();
  const module = findBlueprintModule(moduleKey);
  const mockDemo = useMockDemo();
  const workCenter = getMockWorkCenter(moduleKey);
  const [selectedId, setSelectedId] = useState<string | null>(workCenter?.records[0]?.id ?? null);
  const [search, setSearch] = useState('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const records = (workCenter?.records ?? []).filter((record) => mockDemo.canSeeWarehouse(record.warehouse));
  const warehouses = useMemo(
    () => Array.from(new Set(records.map((item) => item.warehouse))).sort(),
    [records],
  );
  const statuses = useMemo(
    () => Array.from(new Set(records.map((item) => item.status))).sort(),
    [records],
  );
  const filteredRecords = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('vi');
    return records.filter((record) => {
      const searchMatch = !q || [
        record.reference,
        record.id,
        record.type,
        record.subject,
        record.productCode,
        record.partnerCode,
        record.location,
        record.owner,
      ].filter(Boolean).join(' ').toLocaleLowerCase('vi').includes(q);
      const warehouseMatch = !warehouseFilter || record.warehouse === warehouseFilter;
      const statusMatch = !statusFilter || record.status === statusFilter;
      return searchMatch && warehouseMatch && statusMatch;
    });
  }, [records, search, statusFilter, warehouseFilter]);

  const selected = useMemo(
    () => filteredRecords.find((item) => item.id === selectedId) ?? filteredRecords[0] ?? records[0],
    [filteredRecords, records, selectedId],
  );

  if (!module) {
    return (
      <div className="module-blueprint-not-found">
        <h1>Không tìm thấy module</h1>
        <Link to="/system-blueprint">Quay lại bản đồ hệ thống</Link>
      </div>
    );
  }

  const liveCount = module.capabilities.filter((item) => item.status === 'live').length;
  const foundationCount = module.capabilities.filter((item) => item.status === 'foundation').length;
  const plannedCount = module.capabilities.filter((item) => item.status === 'planned').length;
  const optionalCount = module.capabilities.filter((item) => item.status === 'optional').length;
  const attentionCount = filteredRecords.filter((item) => item.tone === 'orange' || item.tone === 'red').length;
  const criticalCount = filteredRecords.filter((item) => item.priority === 'Critical').length;
  const activeCount = filteredRecords.filter((item) => item.tone === 'blue').length;

  return (
    <div className="module-blueprint-page">
      <Link to="/system-blueprint" className="module-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="module-hero">
        <div>
          <span className="eyebrow"><Workflow size={16} /> Module Blueprint • Mock Dataset</span>
          <h1>{module.name}</h1>
          <p>{module.description}</p>
        </div>
        <div className="module-stat-grid">
          <div><strong>{module.capabilities.length}</strong><span>Capability</span></div>
          <div><strong>{liveCount}</strong><span>Đã có</span></div>
          <div><strong>{foundationCount}</strong><span>Đã có nền</span></div>
          <div><strong>{plannedCount + optionalCount}</strong><span>Còn lại</span></div>
        </div>
      </section>

      {module.flow && (
        <section className="module-panel">
          <div className="module-panel-title"><Route size={18} /><h2>Luồng nghiệp vụ chuẩn</h2></div>
          <div className="module-flow">
            {module.flow.map((step, index) => (
              <div className="module-flow-node" key={step}>
                <span>{index + 1}</span>
                <strong>{step}</strong>
                {index < module.flow!.length - 1 && <ArrowRight size={16} />}
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="module-workbench">
        <div className="workbench-main">
          <div className="workbench-header">
            <div>
              <span className="workbench-kicker">WORK CENTER • MOCK DATA</span>
              <h2>{module.name}</h2>
              <p>
                Dataset mô phỏng tại {workCenter ? localTime(workCenter.snapshotAt) : '—'} • Persona {mockDemo.selectedUser.name} • {mockDemo.allowedWarehouses.length} kho scope.
                Không gọi API thật và không thay đổi dữ liệu nghiệp vụ.
              </p>
            </div>
            <button type="button" className="demo-primary" disabled title="Mock dataset là read-only">Mock read-only</button>
          </div>

          <div className="demo-kpis">
            <article><span>Mock records</span><strong>{filteredRecords.length}/{records.length}</strong><small>Sau bộ lọc / tổng</small></article>
            <article><span>Đang xử lý</span><strong>{activeCount}</strong><small>Active / in progress</small></article>
            <article><span>Cần chú ý</span><strong>{attentionCount}</strong><small>Warning / exception</small></article>
            <article><span>Critical</span><strong>{criticalCount}</strong><small>Ưu tiên cao nhất</small></article>
          </div>

          <div className="demo-filterbar">
            <input
              aria-label="Tìm trong work center"
              placeholder="Tìm mã, sản phẩm, chứng từ, lot/serial..."
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
            <select aria-label="Kho" value={warehouseFilter} onChange={(event) => setWarehouseFilter(event.target.value)}>
              <option value="">Tất cả kho được phép</option>
              {warehouses.map((warehouse) => <option key={warehouse} value={warehouse}>{warehouse}</option>)}
            </select>
            <select aria-label="Trạng thái" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="">Tất cả trạng thái</option>
              {statuses.map((status) => <option key={status} value={status}>{status}</option>)}
            </select>
            <button type="button" onClick={() => { setSearch(''); setWarehouseFilter(''); setStatusFilter(''); }}>Xóa lọc</button>
          </div>

          <div className="demo-table-wrap">
            <table className="demo-table mock-data-table">
              <thead>
                <tr>
                  <th>Tham chiếu</th>
                  <th>Loại / Nội dung</th>
                  <th>Kho / Vị trí</th>
                  <th>Số lượng</th>
                  <th>Người phụ trách</th>
                  <th>Ưu tiên</th>
                  <th>Trạng thái</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filteredRecords.map((record) => (
                  <tr key={record.id} className={selected?.id === record.id ? 'selected-row' : undefined}>
                    <td><strong>{record.reference}</strong><span>{record.id}</span></td>
                    <td><strong>{record.type}</strong><span>{record.subject}</span></td>
                    <td><strong>{record.warehouse}</strong><span>{record.location ?? '—'}</span></td>
                    <td>{qtyText(record)}</td>
                    <td><strong>{record.owner}</strong><span>{localTime(record.updatedAt)}</span></td>
                    <td><span className={'priority-chip priority-' + record.priority.toLowerCase()}>{record.priority}</span></td>
                    <td><span className={'mock-status tone-' + record.tone}>{record.status}</span></td>
                    <td><button type="button" className="table-detail-button" onClick={() => setSelectedId(record.id)}>Chi tiết</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
            {filteredRecords.length === 0 && <div className="mock-empty">Không có mock record phù hợp bộ lọc.</div>}
          </div>
        </div>

        <aside className="trace-panel">
          {selected && (
            <div className="selected-record">
              <span className="workbench-kicker">SELECTED MOCK RECORD</span>
              <h3>{selected.reference}</h3>
              <p>{selected.subject}</p>
              <dl>
                <div><dt>Loại</dt><dd>{selected.type}</dd></div>
                <div><dt>Trạng thái</dt><dd><span className={'mock-status tone-' + selected.tone}>{selected.status}</span></dd></div>
                <div><dt>Warehouse</dt><dd>{selected.warehouse}</dd></div>
                <div><dt>Location</dt><dd>{selected.location ?? '—'}</dd></div>
                <div><dt>Product</dt><dd>{selected.productCode ?? '—'}</dd></div>
                <div><dt>Partner</dt><dd>{selected.partnerCode ?? '—'}</dd></div>
                <div><dt>Quantity</dt><dd>{qtyText(selected)}</dd></div>
                <div><dt>Owner</dt><dd>{selected.owner}</dd></div>
                <div><dt>Priority</dt><dd>{selected.priority}</dd></div>
                <div><dt>Updated</dt><dd>{localTime(selected.updatedAt)}</dd></div>
              </dl>
              {selected.note && <div className="selected-note">{selected.note}</div>}
            </div>
          )}

          <div className="trace-divider" />
          <span className="workbench-kicker">TRACEABILITY</span>
          <h3>Đường kiểm soát bắt buộc</h3>
          <div className="trace-chain">
            <div><ShieldCheck size={17} /><span><strong>Permission</strong><small>Role + warehouse scope</small></span></div>
            <div><Workflow size={17} /><span><strong>Command / API</strong><small>Explicit business action</small></span></div>
            <div><LockKeyhole size={17} /><span><strong>State + Concurrency</strong><small>Transition + version/idempotency</small></span></div>
            <div><Boxes size={17} /><span><strong>Domain Rules</strong><small>UOM, status, lot/serial, capacity</small></span></div>
            <div><Database size={17} /><span><strong>Ledger / Balance</strong><small>Posting boundary khi có inventory effect</small></span></div>
            <div><FileCheck2 size={17} /><span><strong>Audit / Outbox</strong><small>Evidence và integration event</small></span></div>
          </div>
          <div className="trace-note">
            Mock records chỉ phục vụ demo/test. Capability chưa có backend vẫn giữ đúng nhãn “Theo đặc tả” hoặc
            “Nâng cao”, không giả lập production readiness.
          </div>
        </aside>
      </section>

      <section className="module-panel">
        <div className="module-panel-title"><ShieldCheck size={18} /><h2>Coverage capability</h2></div>
        <div className="demo-table-wrap">
          <table className="demo-table">
            <thead>
              <tr>
                <th>Mã / Capability</th>
                <th>Mục tiêu vận hành</th>
                <th>Surface</th>
                <th>Trạng thái</th>
                <th>Spec</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {module.capabilities.map((capability) => (
                <tr key={capability.id}>
                  <td><strong>{capability.id}</strong><span>{capability.name}</span></td>
                  <td>{capability.goal}</td>
                  <td>
                    <div className="surface-row compact-surfaces">
                      {capability.surfaces.map((surface) => (
                        <span key={surface}>
                          {surface === 'Mobile' ? <Smartphone size={12} /> : <MonitorSmartphone size={12} />}
                          {surface}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <span className={'status-pill compact ' + capability.status}>
                      {statusIcon(capability.status)} {blueprintStatusLabels[capability.status]}
                    </span>
                  </td>
                  <td>{capability.spec}</td>
                  <td>
                    <div className="table-capability-links">
                      <Link className="table-open-link" to={'/system-blueprint/' + module.key + '/' + capability.id}>Preview</Link>
                      {capability.route && <Link className="table-open-link" to={capability.route}><ExternalLink size={14} /> Mở thật</Link>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="module-panel">
        <div className="module-panel-title"><ShieldCheck size={18} /><h2>Nguyên tắc triển khai module</h2></div>
        <div className="module-rule-grid">
          <div><strong>01</strong><span>Không dùng UI state thay cho server authorization.</span></div>
          <div><strong>02</strong><span>Không update balance trực tiếp từ business document.</span></div>
          <div><strong>03</strong><span>Mutation critical phải concurrency-safe và idempotent.</span></div>
          <div><strong>04</strong><span>Ledger đã post là immutable; sửa sai bằng reversal/correction.</span></div>
          <div><strong>05</strong><span>UI phải giữ đúng state-machine semantics và error code contract.</span></div>
          <div><strong>06</strong><span>Chỉ đánh dấu hoàn tất khi có test + QA + release evidence.</span></div>
        </div>
      </section>
    </div>
  );
};

export default ModuleBlueprint;
