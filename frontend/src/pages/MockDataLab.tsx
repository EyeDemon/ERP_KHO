import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, Boxes, Database, Users, Warehouse } from 'lucide-react';
import {
  mockInventoryBalances,
  mockPartners,
  mockProducts,
  mockRecountAttempts,
  mockTransferConservation,
  mockUsers,
  mockWarehouses,
} from '../mocks/erpWmsMockData';
import './MockDataLab.css';

type Tab = 'warehouses' | 'products' | 'partners' | 'users' | 'inventory' | 'transfers' | 'recounts';

const tabs: Array<{ key: Tab; label: string }> = [
  { key: 'warehouses', label: 'Warehouses' },
  { key: 'products', label: 'Products & Barcode' },
  { key: 'partners', label: 'Business Partners' },
  { key: 'users', label: 'Users & Scope' },
  { key: 'inventory', label: 'Inventory Buckets' },
  { key: 'transfers', label: 'Transfer Conservation' },
  { key: 'recounts', label: 'Recount Attempts' },
];

const MockDataLab = () => {
  const [tab, setTab] = useState<Tab>('warehouses');

  const rows = useMemo(() => {
    if (tab === 'warehouses') return mockWarehouses;
    if (tab === 'products') return mockProducts;
    if (tab === 'partners') return mockPartners;
    if (tab === 'users') return mockUsers;
    if (tab === 'inventory') return mockInventoryBalances;
    if (tab === 'transfers') return mockTransferConservation;
    return mockRecountAttempts;
  }, [tab]);

  return (
    <div className="data-lab-page">
      <Link to="/system-blueprint" className="data-lab-back"><ArrowLeft size={16} /> Bản đồ hệ thống</Link>

      <section className="data-lab-hero">
        <div>
          <span className="data-lab-eyebrow"><Database size={16} /> ERP WMS • Mock Data Lab</span>
          <h1>Fixture dữ liệu kiểm thử</h1>
          <p>
            Dữ liệu demo có referential integrity giữa warehouse, product, barcode, partner, inventory,
            transfer và count. Các bảng này chỉ dùng cho blueprint/demo/test, không ghi vào database thật.
          </p>
        </div>
        <div className="data-lab-summary">
          <div><Warehouse size={17} /><strong>{mockWarehouses.length}</strong><span>Kho</span></div>
          <div><Boxes size={17} /><strong>{mockProducts.length}</strong><span>SKU</span></div>
          <div><Users size={17} /><strong>{mockUsers.length}</strong><span>User</span></div>
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
              <thead><tr><th>Code</th><th>Tên kho</th><th>Thành phố</th><th>Loại</th></tr></thead>
              <tbody>{mockWarehouses.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.city}</td><td>{item.type}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'products' && (
            <table className="data-lab-table">
              <thead><tr><th>SKU</th><th>Tên</th><th>Category</th><th>Base UOM</th><th>Tracking</th><th>Barcodes</th></tr></thead>
              <tbody>{mockProducts.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.category}</td><td>{item.baseUom}</td><td>{item.tracking}</td><td>{item.barcodes.join(', ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'partners' && (
            <table className="data-lab-table">
              <thead><tr><th>Code</th><th>Tên đối tác</th><th>Roles</th></tr></thead>
              <tbody>{mockPartners.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.roles.join(' / ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'users' && (
            <table className="data-lab-table">
              <thead><tr><th>User</th><th>Họ tên</th><th>Role</th><th>Warehouse scope</th></tr></thead>
              <tbody>{mockUsers.map((item) => <tr key={item.code}><td>{item.code}</td><td>{item.name}</td><td>{item.role}</td><td>{item.warehouses.join(', ')}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'inventory' && (
            <table className="data-lab-table numeric-table">
              <thead><tr><th>Warehouse</th><th>SKU</th><th>OnHand</th><th>Reserved</th><th>Allocated</th><th>Available</th><th>QC Hold</th><th>Quarantine</th><th>In Transit</th></tr></thead>
              <tbody>{mockInventoryBalances.map((item) => <tr key={item.warehouse + item.productCode}><td>{item.warehouse}</td><td>{item.productCode}</td><td>{item.onHand}</td><td>{item.reserved}</td><td>{item.allocated}</td><td>{item.available}</td><td>{item.qcHold}</td><td>{item.quarantine}</td><td>{item.inTransit}</td></tr>)}</tbody>
            </table>
          )}

          {tab === 'transfers' && (
            <table className="data-lab-table numeric-table">
              <thead><tr><th>Transfer</th><th>SKU</th><th>Requested</th><th>Source</th><th>Transit</th><th>Destination</th><th>Check</th></tr></thead>
              <tbody>{mockTransferConservation.map((item) => {
                const total = item.source + item.transit + item.destination;
                return <tr key={item.reference}><td>{item.reference}</td><td>{item.productCode}</td><td>{item.requested}</td><td>{item.source}</td><td>{item.transit}</td><td>{item.destination}</td><td><span className={total === item.requested ? 'check-pass' : 'check-fail'}>{total === item.requested ? 'PASS' : 'FAIL'}</span></td></tr>;
              })}</tbody>
            </table>
          )}

          {tab === 'recounts' && (
            <table className="data-lab-table">
              <thead><tr><th>Count</th><th>SKU</th><th>System Qty</th><th>Attempts</th><th>Final Accepted</th></tr></thead>
              <tbody>{mockRecountAttempts.map((item) => <tr key={item.countRef}><td>{item.countRef}</td><td>{item.productCode}</td><td>{item.systemQty}</td><td>{item.attempts.map((attempt) => `#${attempt.attempt}: ${attempt.countedQty}${attempt.accepted ? ' ✓' : ''}`).join(' → ')}</td><td>{item.finalAccepted}</td></tr>)}</tbody>
            </table>
          )}

          <div className="data-lab-row-count">{rows.length} records trong dataset hiện tại</div>
        </div>
      </section>
    </div>
  );
};

export default MockDataLab;
