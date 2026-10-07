import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft, ArrowRight, Boxes, CheckCircle2, CircleDashed, Database,
  FileCheck2, LockKeyhole, MonitorSmartphone, Route, ShieldCheck, Smartphone, Workflow
} from 'lucide-react';
import {
  blueprintDemoStatusLabels,
  findBlueprintModule,
  type BlueprintStatus,
} from '../config/erpWmsBlueprint';
import {
  getMô phỏngWorkCenter,
  type Mô phỏngOperationalRecord,
} from '../mô phỏngs/erpWmsMô phỏngData';
import { useMô phỏngDemo } from '../context/Mô phỏngDemoContext';
import './SystemBlueprint.css';
import './ModuleBlueprint.css';

const statusIcon = (status: BlueprintStatus) => {
  if (status === 'live') return <CheckCircle2 size={15} />;
  return <CircleDashed size={15} />;
};

const qtyText = (record: Mô phỏngOperationalRecord) =>
  record.quantity == null ? '—' : `${record.quantity.toLocaleString('vi-VN')} ${record.uom ?? ''}`.trim();

const localTime = (iso: string) => new Date(iso).toLocaleString('vi-VN');

const ModuleBlueprint = () => {
  const { moduleKey } = useParams();
  const module = findBlueprintModule(moduleKey);
  const mô phỏngDemo = useMô phỏngDemo();
  const workCenter = getMô phỏngWorkCenter(moduleKey);
  const [selectedId, setSelectedId] = useState<string | null>(workCenter?.records[0]?.id ?? null);
  const [search, setSearch] = useState('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const records = (workCenter?.records ?? []).filter((record) => mô phỏngDemo.canSeeWarehouse(record.warehouse));
  const warehouses = useMemo(
    () => Array.from(new Set(records.map((item) => item.warehouse))).sort((a, b) => a.localeCompare(b)),
    [records],
  );
  const statuses = useMemo(
    () => Array.from(new Set(records.map((item) => item.status))).sort((a, b) => a.localeCompare(b)),
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
        <h1>Không tìm thấy phân hệ</h1>
        <Link to="/system-blueprint">Quay lại bản đồ hệ thống</Link>
      </div>
    );
  }

  const liveCount = module.capabilities.filter((item) => item.status === 'live').length;
  const foundationCount = module.capabilities.filter((item) => item.status === 'foundation').length;
  const plannedCount = module.capabilities.filter((item) => item.status === 'planned').length;
  const optionalCount = module.capabilities.filter((item) => item.status === 'optional').length;
  const attentionCount = filteredRecords.filter((item) => item.tone === 'orange' || item.tone === 'red').length;
  const criticalCount = filteredRecords.filter((item) => item.priority === 'Khẩn cấp').length;
  const activeCount = filteredRecords.filter((item) => item.tone === 'blue').length;

  return (
    <div className="module-blueprint-page">
      <Link to="/system-blueprint" className="module-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="module-hero">
        <div>
          <span className="eyebrow"><Workflow size={16} /> Bản thiết kế phân hệ • Dữ liệu mô phỏng</span>
          <h1>{module.name}</h1>
          <p>{module.description}</p>
        </div>
        <div className="module-stat-grid">
          <div><strong>{module.capabilities.length}</strong><span>Bản xem trước mô phỏng</span></div>
          <div><strong>{liveCount}</strong><span>Hệ thống thật hoàn thiện</span></div>
          <div><strong>{foundationCount}</strong><span>Hệ thống thật một phần</span></div>
          <div><strong>{plannedCount + optionalCount}</strong><span>Chưa triển khai / tùy chọn</span></div>
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
              <span className="workbench-kicker">TRUNG TÂM CÔNG VIỆC • DỮ LIỆU MÔ PHỎNG</span>
              <h2>{module.name}</h2>
              <p>
                Bộ dữ liệu mô phỏng tại {workCenter ? localTime(workCenter.snapshotAt) : '—'} • Vai trò {mô phỏngDemo.selectedUser.name} • {mô phỏngDemo.allowedWarehouses.length} kho trong phạm vi.
                Không gọi API thật và không thay đổi dữ liệu nghiệp vụ.
              </p>
            </div>
            <button type="button" className="demo-primary" disabled title="Bộ dữ liệu mô phỏng chỉ đọc">Mô phỏng chỉ đọc</button>
          </div>

          <div className="demo-kpis">
            <article><span>Bản ghi mô phỏng</span><strong>{filteredRecords.length}/{records.length}</strong><small>Sau bộ lọc / tổng</small></article>
            <article><span>Đang xử lý</span><strong>{activeCount}</strong><small>Đang hoạt động / đang xử lý</small></article>
            <article><span>Cần chú ý</span><strong>{attentionCount}</strong><small>Cảnh báo / ngoại lệ</small></article>
            <article><span>Khẩn cấp</span><strong>{criticalCount}</strong><small>Ưu tiên cao nhất</small></article>
          </div>

          <div className="demo-filterbar">
            <input
              aria-label="Tìm trong trung tâm công việc"
              placeholder="Tìm mã, sản phẩm, chứng từ, lô/sê-ri..."
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
            <table className="demo-table mô phỏng-data-table">
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
                    <td><span className={'mô phỏng-status tone-' + record.tone}>{record.status}</span></td>
                    <td><button type="button" className="table-detail-button" onClick={() => setSelectedId(record.id)}>Chi tiết</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
            {filteredRecords.length === 0 && <div className="mô phỏng-empty">Không có bản ghi mô phỏng phù hợp bộ lọc.</div>}
          </div>
        </div>

        <aside className="trace-panel">
          {selected && (
            <div className="selected-record">
              <span className="workbench-kicker">BẢN GHI MÔ PHỎNG ĐANG CHỌN</span>
              <h3>{selected.reference}</h3>
              <p>{selected.subject}</p>
              <dl>
                <div><dt>Loại</dt><dd>{selected.type}</dd></div>
                <div><dt>Trạng thái</dt><dd><span className={'mô phỏng-status tone-' + selected.tone}>{selected.status}</span></dd></div>
                <div><dt>Kho</dt><dd>{selected.warehouse}</dd></div>
                <div><dt>Vị trí</dt><dd>{selected.location ?? '—'}</dd></div>
                <div><dt>Sản phẩm</dt><dd>{selected.productCode ?? '—'}</dd></div>
                <div><dt>Đối tác</dt><dd>{selected.partnerCode ?? '—'}</dd></div>
                <div><dt>Số lượng</dt><dd>{qtyText(selected)}</dd></div>
                <div><dt>Người phụ trách</dt><dd>{selected.owner}</dd></div>
                <div><dt>Ưu tiên</dt><dd>{selected.priority}</dd></div>
                <div><dt>Cập nhật</dt><dd>{localTime(selected.updatedAt)}</dd></div>
              </dl>
              {selected.note ? <div className="selected-note">{selected.note}</div> : null}
            </div>
          )}

          <div className="trace-divider" />
          <span className="workbench-kicker">TRUY VẾT</span>
          <h3>Đường kiểm soát bắt buộc</h3>
          <div className="trace-chain">
            <div><ShieldCheck size={17} /><span><strong>Quyền</strong><small>Vai trò + phạm vi kho</small></span></div>
            <div><Workflow size={17} /><span><strong>Lệnh / API</strong><small>Hành động nghiệp vụ tường minh</small></span></div>
            <div><LockKeyhole size={17} /><span><strong>Trạng thái + Đồng thời</strong><small>Chuyển trạng thái + phiên bản/idempotency</small></span></div>
            <div><Boxes size={17} /><span><strong>Quy tắc nghiệp vụ</strong><small>UOM, trạng thái, lô/sê-ri, sức chứa</small></span></div>
            <div><Database size={17} /><span><strong>Sổ cái / Số dư</strong><small>Ranh giới ghi sổ khi có ảnh hưởng tồn kho</small></span></div>
            <div><FileCheck2 size={17} /><span><strong>Kiểm toán / Outbox</strong><small>Bằng chứng và sự kiện tích hợp</small></span></div>
          </div>
          <div className="trace-note">
            Bản ghi mô phỏng chỉ phục vụ demo/test. Chức năng chưa có backend vẫn giữ đúng nhãn “Theo đặc tả” hoặc
            “Nâng cao”, không giả lập mức sẵn sàng của hệ thống thật.
          </div>
        </aside>
      </section>

      <section className="module-panel">
        <div className="module-panel-title"><ShieldCheck size={18} /><h2>Độ phủ chức năng</h2></div>
        <div className="demo-table-wrap">
          <table className="demo-table">
            <thead>
              <tr>
                <th>Mã / Chức năng</th>
                <th>Mục tiêu vận hành</th>
                <th>Bề mặt sử dụng</th>
                <th>Trạng thái</th>
                <th>Spec</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {module.capabilities.map((chức năng) => (
                <tr key={chức năng.id}>
                  <td><strong>{chức năng.id}</strong><span>{chức năng.name}</span></td>
                  <td>{chức năng.goal}</td>
                  <td>
                    <div className="surface-row compact-surfaces">
                      {chức năng.surfaces.map((surface) => (
                        <span key={surface}>
                          {surface === 'Mobile' ? <Smartphone size={12} /> : <MonitorSmartphone size={12} />}
                          {surface}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <span className={'status-pill compact ' + chức năng.status}>
                      {statusIcon(chức năng.status)} {blueprintDemoStatusLabels[chức năng.status]}
                    </span>
                  </td>
                  <td>{chức năng.spec}</td>
                  <td>
                    <div className="table-chức năng-links">
                      <Link className="table-open-link" to={'/system-blueprint/' + module.key + '/' + chức năng.id}>Xem trước</Link>
                      {chức năng.mô phỏngRoute ? <Link className="table-open-link" to={chức năng.mô phỏngRoute}>Mô phỏng chuyên biệt</Link> : null}
                      {chức năng.route ? <span className="table-hệ thống thật-note">Có trong hệ thống thật</span> : null}
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
          <div><strong>01</strong><span>Không dùng trạng thái UI thay cho phân quyền phía máy chủ.</span></div>
          <div><strong>02</strong><span>Không cập nhật số dư trực tiếp từ chứng từ nghiệp vụ.</span></div>
          <div><strong>03</strong><span>Thao tác thay đổi quan trọng phải an toàn đồng thời và idempotent.</span></div>
          <div><strong>04</strong><span>Sổ cái đã ghi là bất biến; sửa sai bằng đảo giao dịch/hiệu chỉnh.</span></div>
          <div><strong>05</strong><span>UI phải giữ đúng ngữ nghĩa máy trạng thái và hợp đồng mã lỗi.</span></div>
          <div><strong>06</strong><span>Chỉ đánh dấu hoàn tất khi có kiểm thử + QA + bằng chứng phát hành.</span></div>
        </div>
      </section>
    </div>
  );
};

export default ModuleBlueprint;
