import React from 'react';
import { UiBadge, UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  type ActionPanel,
  type Appointment,
  directionLabel,
  displayWarehouseTime,
  type Dock,
  type DockForm,
  emptyDock,
  isAppointmentOverdue,
  isMaintenanceItem,
  normalizeVietnameseText,
  statusLabel,
  statusTone,
  vehicleTypeLabel,
} from './DockYardTypes';

export function DockAppointmentsAndDoorsSection({
  activeSectionTab,
  filtered,
  warehouseTimeZone,
  canManageAppointment,
  canCheckIn,
  canCheckout,
  canAssignDock,
  runCommand,
  setActionPanel,
  showDetail,
  openAppointment,
  docks,
  canManageDock,
  setDockForm,
}: {
  activeSectionTab: 'appointments' | 'yard' | 'crossdock' | 'all';
  filtered: Appointment[];
  warehouseTimeZone: string;
  canManageAppointment: boolean;
  canCheckIn: boolean;
  canCheckout: boolean;
  canAssignDock: boolean;
  runCommand: (appointment: Appointment, suffix: string, payload?: Record<string, unknown>) => Promise<void>;
  setActionPanel: React.Dispatch<React.SetStateAction<ActionPanel | null>>;
  showDetail: (appointment: Appointment) => Promise<void>;
  openAppointment: (appointment?: Appointment) => void;
  docks: Dock[];
  canManageDock: boolean;
  setDockForm: React.Dispatch<React.SetStateAction<DockForm | null>>;
}) {
  return (
    <div style={activeSectionTab === 'appointments' || activeSectionTab === 'all' ? { display: 'grid', gap: 16 } : { maxHeight: 0, overflow: 'hidden', opacity: 0, pointerEvents: 'none', margin: 0, padding: 0, border: 0 }}>
      <UiCard title="Vận hành lịch hẹn" subtitle="Danh sách lịch hẹn xe ra vào cổng, gán bãi chờ và điều phối cửa kho theo thời gian thực">
        <UiTableScroll>
          <table className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }} aria-label="Danh sách lịch hẹn cửa kho sân bãi">
            <thead>
              <tr>
                <th style={{ width: '22%' }}>Lịch hẹn / Đơn</th>
                <th style={{ width: '24%' }}>Khung giờ & Xe</th>
                <th style={{ width: '16%' }}>Bãi / Cửa kho</th>
                <th style={{ width: '14%' }}>Trạng thái</th>
                <th style={{ width: '24%' }}>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(item => {
                const overdue = isAppointmentOverdue(item);
                return (
                  <tr key={item.id}>
                    <td style={{ whiteSpace: 'normal', wordBreak: 'break-word' }}>
                      <strong>{item.code}</strong><br />
                      <small>{directionLabel(item.direction)}</small>
                      {(item.purchaseOrderCode || item.asnCode || item.importReceiptCode) && (
                        <div
                          title={[
                            item.purchaseOrderCode ? 'Đơn mua: ' + item.purchaseOrderCode : '',
                            item.asnCode ? 'ASN: ' + item.asnCode : '',
                            item.importReceiptCode ? 'Phiếu nhập: ' + item.importReceiptCode : '',
                          ].filter(Boolean).join(' • ')}
                          style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 4 }}
                        >
                          {item.purchaseOrderCode && (
                            <span style={{ fontSize: 11, padding: '1px 6px', borderRadius: 4, background: '#f1f5f9', color: '#334155', border: '1px solid #cbd5e1', whiteSpace: 'nowrap' }}>
                              PO: {item.purchaseOrderCode}
                            </span>
                          )}
                          {item.asnCode && (
                            <span style={{ fontSize: 11, padding: '1px 6px', borderRadius: 4, background: '#f1f5f9', color: '#334155', border: '1px solid #cbd5e1', whiteSpace: 'nowrap' }}>
                              ASN: {item.asnCode}
                            </span>
                          )}
                          {item.importReceiptCode && (
                            <span style={{ fontSize: 11, padding: '1px 6px', borderRadius: 4, background: '#f1f5f9', color: '#334155', border: '1px solid #cbd5e1', whiteSpace: 'nowrap' }}>
                              PN: {item.importReceiptCode}
                            </span>
                          )}
                        </div>
                      )}
                    </td>
                    <td>
                      <span style={{ fontVariantNumeric: 'tabular-nums' }}>{displayWarehouseTime(item.plannedStartUtc, warehouseTimeZone)}</span>
                      <br />
                      <small style={{ fontVariantNumeric: 'tabular-nums' }}>→ {displayWarehouseTime(item.plannedEndUtc, warehouseTimeZone)} • {warehouseTimeZone === 'Asia/Ho_Chi_Minh' ? 'Giờ VN (GMT+7)' : warehouseTimeZone}</small>
                      <br />
                      <small>{item.carrierName || item.carrierCode || '—'} • {item.vehiclePlate || 'Chưa có biển số'}</small>
                    </td>
                    <td className="ui-cell-nowrap">
                      Bãi: {item.yardSlotCode || '—'}
                      <br />
                      <small>→ Cửa: {item.dockCode || '—'}</small>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                        <span
                          className={`ui-badge ui-badge-${statusTone(item.status)}`}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            padding: '2px 8px',
                            borderRadius: 999,
                            fontSize: 12,
                            fontWeight: 600,
                            whiteSpace: 'nowrap',
                            background: '#eff6ff',
                            color: '#1e40af',
                            border: '1px solid #bfdbfe',
                          }}
                        >
                          {statusLabel[item.status] || item.status}
                        </span>
                        {overdue && (
                          <UiBadge tone="danger">
                            <span style={{ whiteSpace: 'nowrap' }}>Trễ giờ hẹn</span>
                          </UiBadge>
                        )}
                        {item.checkedOutAtUtc && <small style={{ width: '100%' }}>Đã rời cổng</small>}
                      </div>
                    </td>
                    <td>
                      <div className="ui-inline-actions" style={{ gap: 6, flexWrap: 'wrap' }}>
                        {canManageAppointment && item.status === 0 && (
                          <button type="button" aria-label={'Xác nhận lịch hẹn ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => void runCommand(item, 'confirm')}>
                            Xác nhận
                          </button>
                        )}
                        {canCheckIn && item.status === 1 && (
                          <button type="button" aria-label={'Ghi nhận xe đến ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => void runCommand(item, 'arrive')}>
                            Xe đến
                          </button>
                        )}
                        {canCheckIn && item.status === 2 && (
                          <button type="button" aria-label={'Ghi nhận vào cổng cho lịch hẹn ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => setActionPanel({ mode: 'checkin', appointment: item, vehiclePlate: item.vehiclePlate ?? '', trailerPlate: item.trailerPlate ?? '', driverName: '', driverPhone: '', sealNumber: '', yardSlotId: '', note: '' })}>
                            Ghi nhận vào cổng
                          </button>
                        )}
                        {canManageAppointment && item.status === 4 && (
                          <button type="button" aria-label={'Bắt đầu phục vụ lịch hẹn ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => void runCommand(item, 'start-service')}>
                            Bắt đầu
                          </button>
                        )}
                        {canManageAppointment && item.status === 5 && (
                          <button type="button" aria-label={'Hoàn thành phục vụ lịch hẹn ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => void runCommand(item, 'complete')}>
                            Hoàn thành
                          </button>
                        )}
                        {canCheckout && (item.status === 6 || (item.status === 9 && Boolean(item.checkedInAtUtc))) && !item.checkedOutAtUtc && (
                          <button type="button" aria-label={'Ghi nhận rời cổng cho lịch hẹn ' + item.code} style={{ background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 }} onClick={() => void runCommand(item, 'checkout')}>
                            Rời cổng
                          </button>
                        )}
                        {canAssignDock && [3, 4].includes(item.status) && (
                          <button type="button" aria-label={(item.status === 4 ? 'Đổi cửa kho cho lịch hẹn ' : 'Gán cửa kho cho lịch hẹn ') + item.code} style={item.status === 3 ? { background: '#1d4ed8', color: '#ffffff', border: '1px solid #1d4ed8', fontWeight: 600 } : { background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }} onClick={() => setActionPanel({ mode: 'assignDock', appointment: item, dockId: item.dockId?.toString() ?? '', note: '' })}>
                            {item.status === 4 ? 'Đổi cửa kho' : 'Gán cửa kho'}
                          </button>
                        )}
                        <button type="button" aria-label={'Chi tiết lịch hẹn ' + item.code} style={{ background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }} onClick={() => void showDetail(item)}>
                          Chi tiết
                        </button>
                        {canManageAppointment && item.status === 0 && (
                          <button type="button" aria-label={'Sửa lịch hẹn ' + item.code} style={{ background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }} onClick={() => openAppointment(item)}>
                            Sửa
                          </button>
                        )}
                        {canManageAppointment && item.direction === 0 && ![6, 7, 8].includes(item.status) && (
                          <button type="button" aria-label={'Liên kết chứng từ nhập cho lịch hẹn ' + item.code} style={{ background: '#f8fafc', color: '#0f172a', border: '1px solid #cbd5e1' }} onClick={() => setActionPanel({ mode: 'linkInbound', appointment: item, purchaseOrderId: item.purchaseOrderId?.toString() ?? '', asnId: item.asnId?.toString() ?? '', importReceiptId: item.importReceiptId?.toString() ?? '', note: '' })}>
                            Liên kết chứng từ nhập
                          </button>
                        )}
                        {canManageAppointment && [0, 1].includes(item.status) && (
                          <button type="button" aria-label={'Hủy lịch hẹn ' + item.code} style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }} onClick={() => window.confirm('Hủy lịch hẹn này?') && void runCommand(item, 'cancel')}>
                            Hủy
                          </button>
                        )}
                        {canManageAppointment && [1, 2, 3].includes(item.status) && (
                          <button type="button" aria-label={'Đánh dấu không đến cho lịch hẹn ' + item.code} style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }} onClick={() => window.confirm('Xác nhận đánh dấu phương tiện không đến?') && void runCommand(item, 'no-show')}>
                            Không đến
                          </button>
                        )}
                        {canManageAppointment && ![6, 7, 8, 9].includes(item.status) && (
                          <button type="button" aria-label={'Ghi nhận ngoại lệ cho lịch hẹn ' + item.code} title="Ghi nhận sự cố bất thường tại cổng/cửa kho (xe trễ giờ, sai kẹp chì niêm phong, hư hỏng phương tiện hoặc từ chối tiếp nhận)" style={{ border: '1px solid #dc2626', color: '#dc2626', background: '#fff5f5', fontWeight: 600 }} onClick={() => setActionPanel({ mode: 'exception', appointment: item, exceptionCode: '', note: '' })}>
                            Ngoại lệ
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
              {filtered.length === 0 && <tr><td className="ui-empty-cell" colSpan={5}>Không có lịch hẹn phù hợp.</td></tr>}
            </tbody>
          </table>
        </UiTableScroll>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 10, paddingTop: 8, borderTop: '1px solid #e2e8f0', fontSize: 12.5, color: '#475569', fontVariantNumeric: 'tabular-nums' }}>
          <span>Hiển thị {filtered.length === 0 ? 0 : 1}–{filtered.length} / Tổng {filtered.length} lịch hẹn</span>
          <span>Trang 1 / 1 (Cỡ trang: 20)</span>
        </div>
      </UiCard>

      <UiCard title="Danh mục cửa kho" subtitle="Trạng thái hoạt động, bảo trì và cấu hình luồng tiếp nhận trên từng cửa kho">
        {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setDockForm(emptyDock())}>Thêm cửa kho</button></div>}
        <UiTableScroll>
          <table className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }} aria-label="Danh sách cửa kho">
            <thead>
              <tr>
                <th style={{ width: '24%' }}>Cửa kho</th>
                <th style={{ width: '28%' }}>Luồng & Loại xe</th>
                <th style={{ width: '26%' }}>Điều kiện</th>
                <th style={{ width: '22%' }}>Trạng thái & Sửa</th>
              </tr>
            </thead>
            <tbody>
              {docks.map(dock => {
                const maintenance = isMaintenanceItem(dock.isActive, dock.code, dock.name);
                return (
                  <tr key={dock.id}>
                    <td className="ui-cell-nowrap">
                      <strong>{dock.code}</strong>
                      <br />
                      <small>{normalizeVietnameseText(dock.name)}</small>
                    </td>
                    <td>
                      {[dock.supportsInbound ? 'Nhập kho' : '', dock.supportsOutbound ? 'Xuất kho' : ''].filter(Boolean).join(' + ')}
                      <br />
                      <small>{vehicleTypeLabel(dock.allowedVehicleType)}</small>
                    </td>
                    <td>
                      Lạnh: {dock.isTemperatureControlled ? 'Có' : 'Không'}
                      <br />
                      <small>Nguy hiểm: {dock.hazardAllowed ? 'Cho phép' : 'Không'}</small>
                    </td>
                    <td>
                      <div>
                        <UiBadge tone={maintenance ? 'warning' : 'success'}>
                          <span style={{ whiteSpace: 'nowrap' }}>{maintenance ? 'Bảo trì' : 'Hoạt động'}</span>
                        </UiBadge>
                      </div>
                      {canManageDock && (
                        <div className="ui-inline-actions" style={{ marginTop: 6 }}>
                          <button type="button" aria-label={'Sửa cửa kho ' + dock.code} onClick={() => setDockForm({ id: dock.id, code: dock.code, name: dock.name, supportsInbound: dock.supportsInbound, supportsOutbound: dock.supportsOutbound, allowedVehicleType: dock.allowedVehicleType ?? '', isTemperatureControlled: dock.isTemperatureControlled, hazardAllowed: dock.hazardAllowed, isActive: dock.isActive, rowVersion: dock.rowVersion })}>Sửa</button>
                        </div>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </UiTableScroll>
      </UiCard>
    </div>
  );
}
