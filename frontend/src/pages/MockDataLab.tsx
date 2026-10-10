import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, Boxes, Database, Users, Warehouse } from 'lucide-react';
import {
  mockCapabilityFixtures,
  mockInventoryBalances,
  mockPartners,
  mockProducts,
  mockRecountAttempts,
  mockTransferConservation,
  mockUsers,
  mockWarehouses,
} from '../mocks/erpWmsMockData';
import { useMockDemo } from '../context/MockDemoContext';
import { mockDisplayText, mockStatusLabel } from '../utils/mockDisplayLabels';
import './MockDataLab.css';

type Tab = 'warehouses' | 'products' | 'capabilities' | 'partners' | 'users' | 'inventory' | 'transfers' | 'recounts' | 'scenario';

const tabs: Array<{ key: Tab; label: string }> = [
  { key: 'warehouses', label: 'Kho hàng' },
  { key: 'products', label: 'Sản phẩm & mã vạch' },
  { key: 'capabilities', label: `${Object.keys(mockCapabilityFixtures).length} bộ dữ liệu chức năng` },
  { key: 'partners', label: 'Đối tác' },
  { key: 'users', label: 'Người dùng & phạm vi' },
  { key: 'inventory', label: 'Nhóm tồn kho' },
  { key: 'transfers', label: 'Bảo toàn điều chuyển' },
  { key: 'recounts', label: 'Các lần kiểm đếm lại' },
  { key: 'scenario', label: 'Phiên kịch bản dùng chung' },
];

const MockDataLab = () => {
  const [tab, setTab] = useState<Tab>('warehouses');
  const mockDemo = useMockDemo();

  const rows = useMemo(() => {
    if (tab === 'warehouses') return mockWarehouses;
    if (tab === 'products') return mockProducts;
    if (tab === 'capabilities') return Object.values(mockCapabilityFixtures);
    if (tab === 'partners') return mockPartners;
    if (tab === 'users') return mockUsers;
    if (tab === 'inventory') return mockInventoryBalances;
    if (tab === 'transfers') return mockTransferConservation;
    if (tab === 'recounts') return mockRecountAttempts;
    return mockDemo.scenarioBalances;
  }, [mockDemo.scenarioBalances, tab]);

  return (
    <div className="data-lab-page">
      <Link to="/system-blueprint" className="data-lab-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="data-lab-hero">
        <div>
          <span className="data-lab-eyebrow"><Database size={16} /> ERP WMS • Phòng dữ liệu mô phỏng</span>
          <h1>Dữ liệu mẫu kiểm thử</h1>
          <p>
            Dữ liệu mô phỏng giữ toàn vẹn tham chiếu giữa kho, sản phẩm, mã vạch, đối tác, tồn kho,
            điều chuyển và kiểm kê. Các bảng này chỉ dùng cho bản thiết kế/mô phỏng/kiểm thử, không ghi vào cơ sở dữ liệu thật.
          </p>
        </div>
        <div className="data-lab-summary">
          <div><Warehouse size={17} /><strong>{mockWarehouses.length}</strong><span>Kho</span></div>
          <div><Boxes size={17} /><strong>{mockProducts.length}</strong><span>SKU</span></div>
          <div><Users size={17} /><strong>{mockUsers.length}</strong><span>Người dùng</span></div>
          <div><Database size={17} /><strong>{Object.keys(mockCapabilityFixtures).length}</strong><span>Bộ dữ liệu</span></div>
          <div><Database size={17} /><strong>{mockDemo.activeScenarioId ?? '—'}</strong><span>Kịch bản</span></div>
        </div>
      </section>

      <section className="data-lab-shell">
        <div className="data-lab-tabs" role="tablist">
          {tabs.map((item) => (
            <button
              type="button"
              role="tab"
              aria-selected={tab === item.key}
              className={tab === item.key ? 'active' : undefined}
              key={item.key}
              onClick={() => setTab(item.key)}
            >
              {item.label}
            </button>
          ))}
        </div>

        <div className="data-lab-table-wrap">
          {tab === 'warehouses' && (
            <table className="data-lab-table">
              <thead><tr><th>Mã</th><th>Tên kho</th><th>Thành phố</th><th>Loại</th></tr></thead>
              <tbody>{mockWarehouses.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.city}</td><td>{mockDisplayText(item.type)}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'products' && (
            <table className="data-lab-table">
              <thead><tr><th>SKU</th><th>Tên</th><th>Danh mục</th><th>Đơn vị cơ sở (UOM)</th><th>Kiểu theo dõi</th><th>Mã vạch</th></tr></thead>
              <tbody>{mockProducts.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.category}</td><td>{item.baseUom}</td><td>{mockDisplayText(item.tracking)}</td><td>{item.barcodes.join(', ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'capabilities' && (
            <table className="data-lab-table">
              <thead><tr><th>Bộ dữ liệu mẫu</th><th>Chức năng</th><th>Phân hệ</th><th>Triển khai</th><th>Đặc tả</th><th>Tham chiếu mẫu</th><th>Kho</th><th>Trạng thái mẫu</th></tr></thead>
              <tbody>{Object.values(mockCapabilityFixtures).map((item) => <tr key={item.fixtureId}><td>{item.fixtureId}</td><td>{item.capabilityId} — {item.capabilityName}</td><td>{item.moduleKey}</td><td>{mockDisplayText(item.implementationStatus)}</td><td>{item.spec}</td><td>{item.sampleReference}</td><td>{item.sampleWarehouse}</td><td>{mockStatusLabel(item.sampleStatus)}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'partners' && (
            <table className="data-lab-table">
              <thead><tr><th>Mã</th><th>Tên đối tác</th><th>Vai trò</th></tr></thead>
              <tbody>{mockPartners.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.roles.map(mockDisplayText).join(' / ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'users' && (
            <table className="data-lab-table">
              <thead><tr><th>Người dùng</th><th>Họ tên</th><th>Vai trò</th><th>Phạm vi kho</th></tr></thead>
              <tbody>{mockUsers.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{mockDisplayText(item.role)}</td><td>{item.warehouses.join(', ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'inventory' && (
            <table className="data-lab-table numeric-table">
              <thead><tr><th>Kho</th><th>SKU</th><th>Tồn thực tế</th><th>Đã giữ</th><th>Đã phân bổ</th><th>Khả dụng</th><th>Chờ QC</th><th>Cách ly</th><th>Đang vận chuyển</th></tr></thead>
              <tbody>{mockInventoryBalances.map((item) => <tr key={item.warehouse + item.productCode}><td>{item.warehouse}</td><td>{item.productCode}</td><td>{item.onHand}</td><td>{item.reserved}</td><td>{item.allocated}</td><td>{item.available}</td><td>{item.qcHold}</td><td>{item.quarantine}</td><td>{item.inTransit}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'transfers' && (
            <table className="data-lab-table numeric-table">
              <thead><tr><th>Điều chuyển</th><th>SKU</th><th>Yêu cầu</th><th>Nguồn</th><th>Đang vận chuyển</th><th>Đích</th><th>Kiểm tra</th></tr></thead>
              <tbody>{mockTransferConservation.map((item) => {
                const total = item.source + item.transit + item.destination;
                return <tr key={item.reference}><td>{item.reference}</td><td>{item.productCode}</td><td>{item.requested}</td><td>{item.source}</td><td>{item.transit}</td><td>{item.destination}</td><td><span className={total === item.requested ? 'check-pass' : 'check-fail'}>{total === item.requested ? 'ĐẠT' : 'LỖI'}</span></td></tr>;
              })}</tbody>
            </table>
          )}

          {tab === 'recounts' && (
            <table className="data-lab-table">
              <thead><tr><th>Phiếu kiểm kê</th><th>SKU</th><th>Số lượng hệ thống</th><th>Các lần đếm</th><th>Số lượng chấp nhận cuối</th></tr></thead>
              <tbody>{mockRecountAttempts.map((item) => <tr key={item.countRef}><td>{item.countRef}</td><td>{item.productCode}</td><td>{item.systemQty}</td><td>{item.attempts.map((attempt) => `#${attempt.attempt}: ${attempt.countedQty}${attempt.accepted ? ' ✓' : ''}`).join(' → ')}</td><td>{item.finalAccepted}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'scenario' && (
            <section className="scenario-runtime-panel">
              {mockDemo.activeScenarioId ? (
                <>
                  <div className="scenario-runtime-heading">
                    <div><span>PHIÊN DÙNG CHUNG ĐANG HOẠT ĐỘNG</span><strong>{mockDemo.activeScenarioId} • {mockDemo.activeScenarioTitle}</strong></div>
                    <div>
                      <strong>Bước {mockDemo.activeScenarioStep}</strong>
                      <span>{mockDemo.affectedScenarioCapabilities.length} chức năng liên quan • hiển thị {mockDemo.scenarioBalances.length}/{mockDemo.scenarioTotalBalanceCount}</span>
                      {mockDemo.scenarioHiddenBalanceCount > 0 ? <span>{mockDemo.scenarioHiddenBalanceCount} nhóm tồn bị ẩn bởi phạm vi kho</span> : null}
                    </div>
                  </div>
                  {mockDemo.scenarioBalances.length > 0 ? (
                    <table className="data-lab-table numeric-table">
                      <thead><tr><th>Kho</th><th>Vị trí</th><th>SKU</th><th>Tồn thực tế</th><th>Đã giữ</th><th>Đã phân bổ</th><th>Đã lấy</th><th>Khả dụng</th><th>Chờ QC</th><th>Cách ly</th><th>Đang vận chuyển</th></tr></thead>
                      <tbody>{mockDemo.scenarioBalances.map((item) => (
                        <tr key={item.warehouse + item.location + item.productCode}>
                          <td>{item.warehouse}</td><td>{item.location}</td><td>{item.productCode}</td><td>{item.onHand}</td><td>{item.reserved}</td><td>{item.allocated}</td><td>{item.picked}</td><td>{item.available ?? '—'}</td><td>{item.qcHold}</td><td>{item.quarantine}</td><td>{item.inTransit}</td>
                        </tr>
                      ))}</tbody>
                    </table>
                  ) : (
                    <div className="scenario-runtime-empty">Kịch bản này có phiên sự kiện dùng chung nhưng chưa có ảnh chụp tồn kho chuyên biệt.</div>
                  )}
                  <div className="scenario-runtime-events">
                    <strong>Nhật ký sự kiện dùng chung</strong>
                    {mockDemo.scenarioEventLog.length ? mockDemo.scenarioEventLog.map((event, index) => (
                      <div key={event.label + index}><span>{index + 1}</span><p><strong>{event.label}</strong><small>{event.state} • {event.inventoryEffect}</small></p></div>
                    )) : <p>Kịch bản đã được chọn nhưng chưa chạy bước nào.</p>}
                  </div>
                </>
              ) : (
                <div className="scenario-runtime-empty">Chưa có phiên kịch bản dùng chung. Mở Phòng kịch bản chuẩn và chạy một bước để tạo phiên.</div>
              )}
            </section>
          )}

          <div className="data-lab-row-count">{rows.length} bản ghi trong bộ dữ liệu hiện tại</div>
        </div>
      </section>
    </div>
  );
};

export default MockDataLab;
