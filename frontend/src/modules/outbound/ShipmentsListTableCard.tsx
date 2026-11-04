import { useState } from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import type { Shipment } from './shipmentsTypes';
import { statusLabels, tone } from './shipmentsTypes';

interface ShipmentsListTableCardProps {
  rows: Shipment[];
  detail: (id: number) => Promise<void>;
}

export function ShipmentsListTableCard({ rows, detail }: ShipmentsListTableCardProps) {
  const [filterWarehouse, setFilterWarehouse] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [filterSearch, setFilterSearch] = useState('');

  const warehouseFilterOptions = Array.from(new Set(rows.map(x => x.warehouseName).filter(Boolean)));
  const filteredRows = rows.filter(item => {
    if (filterWarehouse && item.warehouseName !== filterWarehouse) return false;
    if (filterStatus && item.status !== filterStatus) return false;
    if (filterSearch.trim()) {
      const q = filterSearch.trim().toLowerCase();
      const matchCode = (item.shipmentCode || '').toLowerCase().includes(q);
      const matchPlate = (item.vehiclePlate || '').toLowerCase().includes(q);
      const matchSource = (item.sourceCode || item.packingSessionCode || '').toLowerCase().includes(q);
      if (!matchCode && !matchPlate && !matchSource) return false;
    }
    return true;
  });

  return (
    <UiCard title="Danh sách chuyến giao hàng">
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: 10,
          marginBottom: 12,
          alignItems: 'end',
        }}
      >
        <label className="ui-stack">
          <span>Kho xuất</span>
          <select
            aria-label="Lọc kho xuất chuyến giao"
            value={filterWarehouse}
            onChange={e => setFilterWarehouse(e.target.value)}
          >
            <option value="">Tất cả kho xuất</option>
            {warehouseFilterOptions.map(wh => (
              <option key={wh} value={wh}>{wh}</option>
            ))}
          </select>
        </label>
        <label className="ui-stack">
          <span>Trạng thái chuyến giao</span>
          <select
            aria-label="Lọc trạng thái chuyến giao"
            value={filterStatus}
            onChange={e => setFilterStatus(e.target.value)}
          >
            <option value="">Tất cả</option>
            <option value="Ready">Sẵn sàng</option>
            <option value="Staging">Đã tập kết</option>
            <option value="Dispatched">Đã xuất bến</option>
            <option value="Delivered">Đã giao</option>
          </select>
        </label>
        <label className="ui-stack">
          <span>Tìm mã chuyến / biển số xe</span>
          <input
            type="search"
            aria-label="Tìm mã chuyến hoặc biển số xe"
            placeholder="Nhập mã chuyến hoặc biển số xe..."
            value={filterSearch}
            onChange={e => setFilterSearch(e.target.value)}
          />
        </label>
      </div>
      {filteredRows.length === 0 ? (
        <p>Chưa có Shipment. Shipment được tạo khi Packing đạt PACKED.</p>
      ) : (
        <UiTableScroll>
          <table aria-label="Danh sách Shipment">
            <thead>
              <tr>
                <th>Mã chuyến</th>
                <th>Đơn nguồn</th>
                <th>Kho</th>
                <th>Trạng thái</th>
                <th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>Số kiện</th>
                <th title="Cửa kho xuất bến được gán và biển số phương tiện vận chuyển">CỬA XUẤT &amp; BIỂN SỐ XE</th>
              </tr>
            </thead>
            <tbody>
              {filteredRows.map(item => (
                <tr key={item.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" style={{ whiteSpace: 'nowrap' }} onClick={() => void detail(item.id)}>{item.shipmentCode}</button>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>{item.sourceCode ?? item.packingSessionCode}</td>
                  <td>{item.warehouseName}</td>
                  <td><UiBadge tone={tone(item.status)}>{statusLabels[item.status] ?? item.status}</UiBadge></td>
                  <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{item.loadedHandlingUnitCount}/{item.handlingUnitCount}</td>
                  <td>{`${item.dockCode ? `Cửa: ${item.dockCode}` : 'Chưa gán cửa'}${item.vehiclePlate ? ` • Xe: ${item.vehiclePlate}` : ''}`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiTableScroll>
      )}
    </UiCard>
  );
}
