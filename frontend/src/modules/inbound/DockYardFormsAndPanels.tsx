import React from 'react';
import { UiCard, UiTableScroll } from '../../ui/ProductionUi';
import {
  type ActionPanel,
  type Appointment,
  type AppointmentForm,
  directionLabel,
  displayWarehouseTime,
  type Dock,
  type DockForm,
  eventTypeLabel,
  type InboundDocOption,
  statusLabel,
  type YardForm,
  type YardSlot,
} from './DockYardTypes';

export function DockAppointmentFormCard({
  appointmentForm,
  setAppointmentForm,
  saving,
  saveAppointment,
  warehouseTimeZone,
  inboundPos,
  inboundAsns,
  inboundReceipts,
}: {
  appointmentForm: AppointmentForm;
  setAppointmentForm: React.Dispatch<React.SetStateAction<AppointmentForm | null>>;
  saving: boolean;
  saveAppointment: (event: React.FormEvent) => Promise<void>;
  warehouseTimeZone: string;
  inboundPos: InboundDocOption[];
  inboundAsns: InboundDocOption[];
  inboundReceipts: InboundDocOption[];
}) {
  return (
    <UiCard
      title={appointmentForm.id ? 'Sửa lịch hẹn ' + appointmentForm.code : 'Tạo lịch hẹn mới'}
      subtitle="Mã lịch hẹn được sinh tự động theo thứ tự tuần tự (có thể chỉnh sửa nếu cần). Bố cục 3 nhóm nghiệp vụ chuẩn."
    >
      <div className="ui-inline-actions" style={{ marginBottom: 8 }}>
        <button type="button" onClick={() => setAppointmentForm(null)} disabled={saving}>Đóng khung</button>
      </div>
      <form onSubmit={saveAppointment} style={{ display: 'grid', gap: 16 }}>
        <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <div style={{ fontWeight: 600, fontSize: 13, color: '#0f172a', marginBottom: 10 }}>1. Thông tin lịch hẹn & Luồng</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 12, alignItems: 'start' }}>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Mã lịch hẹn (tự sinh) *</span>
              <input aria-label="Mã lịch hẹn" value={appointmentForm.code} disabled={saving || Boolean(appointmentForm.id)} onChange={event => setAppointmentForm(current => current && { ...current, code: event.target.value.toUpperCase() })} required />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Luồng vận hành *</span>
              <select aria-label="Luồng lịch hẹn" value={appointmentForm.direction} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, direction: event.target.value as '0' | '1' })}>
                <option value="0">Nhập kho</option>
                <option value="1">Xuất kho</option>
              </select>
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Bắt đầu * • {warehouseTimeZone}</span>
              <input aria-label="Bắt đầu lịch hẹn theo múi giờ kho" type="datetime-local" value={appointmentForm.plannedStart} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, plannedStart: event.target.value })} required />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Kết thúc * • {warehouseTimeZone}</span>
              <input aria-label="Kết thúc lịch hẹn theo múi giờ kho" type="datetime-local" value={appointmentForm.plannedEnd} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, plannedEnd: event.target.value })} required />
            </label>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, justifyContent: 'flex-start', paddingTop: 4 }}>
              <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.requiresTemperatureControl} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, requiresTemperatureControl: event.target.checked })} /> Yêu cầu kiểm soát nhiệt độ</label>
              <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.hazardous} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, hazardous: event.target.checked })} /> Hàng nguy hiểm</label>
            </div>
          </div>
        </div>

        <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
          <div style={{ fontWeight: 600, fontSize: 13, color: '#0f172a', marginBottom: 10 }}>2. Phương tiện & Đơn vị vận chuyển</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 12, alignItems: 'start' }}>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Mã đơn vị vận chuyển</span>
              <input aria-label="Mã đơn vị vận chuyển" value={appointmentForm.carrierCode} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, carrierCode: event.target.value })} placeholder="VD: CAR-FAST" />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Tên đơn vị vận chuyển</span>
              <input aria-label="Tên đơn vị vận chuyển" value={appointmentForm.carrierName} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, carrierName: event.target.value })} placeholder="VD: Fast Logistics" />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Biển số xe dự kiến</span>
              <input aria-label="Biển số xe dự kiến" value={appointmentForm.vehiclePlate} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, vehiclePlate: event.target.value.toUpperCase() })} placeholder="VD: 51C-882.14" />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Rơ-moóc</span>
              <input aria-label="Rơ-moóc dự kiến" value={appointmentForm.trailerPlate} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, trailerPlate: event.target.value.toUpperCase() })} />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Loại phương tiện</span>
              <input aria-label="Loại phương tiện" value={appointmentForm.vehicleType} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, vehicleType: event.target.value.toUpperCase() })} placeholder="TRUCK / CONTAINER / VAN" />
            </label>
            <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
              <span>Ghi chú điều phối</span>
              <textarea aria-label="Ghi chú lịch hẹn" rows={2} value={appointmentForm.note} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, note: event.target.value })} />
            </label>
          </div>
        </div>

        {appointmentForm.direction === '0' && (
          <div style={{ padding: 12, borderRadius: 8, border: '1px solid #e2e8f0', background: '#f8fafc' }}>
            <div style={{ fontWeight: 600, fontSize: 13, color: '#0f172a', marginBottom: 10 }}>3. Liên kết chứng từ PO / ASN / Phiếu nhập</div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12, alignItems: 'start' }}>
              <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                <span>Đơn mua hàng (PO)</span>
                <select aria-label="ID Đơn mua hàng (PO)" value={appointmentForm.purchaseOrderId} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, purchaseOrderId: event.target.value })}>
                  <option value="">-- Không liên kết PO --</option>
                  {inboundPos.map(po => <option key={po.id} value={String(po.id)}>{po.code}{po.supplierName ? ` — ${po.supplierName}` : ''}</option>)}
                </select>
              </label>
              <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                <span>Thông báo giao hàng (ASN)</span>
                <select aria-label="ID Thông báo giao hàng (ASN)" value={appointmentForm.asnId} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, asnId: event.target.value })}>
                  <option value="">-- Không liên kết ASN --</option>
                  {inboundAsns.map(asn => <option key={asn.id} value={String(asn.id)}>{asn.code}{asn.supplierName ? ` — ${asn.supplierName}` : ''}</option>)}
                </select>
              </label>
              <label className="ui-stack" style={{ display: 'flex', flexDirection: 'column', gap: 6, justifyContent: 'flex-start' }}>
                <span>Phiếu nhập kho (GR)</span>
                <select aria-label="ID Phiếu nhập kho (GR)" value={appointmentForm.importReceiptId} disabled={saving} onChange={event => setAppointmentForm(current => current && { ...current, importReceiptId: event.target.value })}>
                  <option value="">-- Không liên kết Phiếu nhập --</option>
                  {inboundReceipts.map(gr => <option key={gr.id} value={String(gr.id)}>{gr.code}{gr.supplierName ? ` — ${gr.supplierName}` : ''}</option>)}
                </select>
              </label>
            </div>
          </div>
        )}

        <div className="ui-inline-actions">
          <button type="submit" disabled={saving} style={{ background: '#1d4ed8', color: '#ffffff', fontWeight: 600, border: '1px solid #1d4ed8', padding: '8px 16px', borderRadius: 8 }}>
            {saving ? 'Đang lưu...' : 'Lưu lịch hẹn'}
          </button>
          <button type="button" disabled={saving} onClick={() => setAppointmentForm(null)}>Hủy</button>
        </div>
      </form>
    </UiCard>
  );
}

export function DockTimelineAndConfigCards({
  selected,
  setSelected,
  warehouseTimeZone,
  docks,
  yardSlots,
  dockForm,
  setDockForm,
  canManageDock,
  saving,
  saveDock,
  yardForm,
  setYardForm,
  saveYard,
  actionPanel,
  setActionPanel,
  runCommand,
  inboundPos,
  inboundAsns,
  inboundReceipts,
}: {
  selected: Appointment | null;
  setSelected: React.Dispatch<React.SetStateAction<Appointment | null>>;
  warehouseTimeZone: string;
  docks: Dock[];
  yardSlots: YardSlot[];
  dockForm: DockForm | null;
  setDockForm: React.Dispatch<React.SetStateAction<DockForm | null>>;
  canManageDock: boolean;
  saving: boolean;
  saveDock: (event: React.FormEvent) => Promise<void>;
  yardForm: YardForm | null;
  setYardForm: React.Dispatch<React.SetStateAction<YardForm | null>>;
  saveYard: (event: React.FormEvent) => Promise<void>;
  actionPanel: ActionPanel | null;
  setActionPanel: React.Dispatch<React.SetStateAction<ActionPanel | null>>;
  runCommand: (appointment: Appointment, suffix: string, payload?: Record<string, unknown>) => Promise<void>;
  inboundPos: InboundDocOption[];
  inboundAsns: InboundDocOption[];
  inboundReceipts: InboundDocOption[];
}) {
  return (
    <>
      {selected && (
        <UiCard title={'Dòng thời gian • ' + selected.code}>
          <p><strong>{statusLabel[selected.status]}</strong> • {directionLabel(selected.direction)} • {selected.vehiclePlate || 'Chưa có xe'} • {selected.dockCode || 'Chưa gán cửa kho'}</p>
          {(selected.purchaseOrderCode || selected.asnCode || selected.importReceiptCode) && (
            <p className="ui-muted">
              Chứng từ nhập liên kết:{' '}
              {[
                selected.purchaseOrderCode ? 'Đơn mua: ' + selected.purchaseOrderCode : '',
                selected.asnCode ? 'ASN: ' + selected.asnCode : '',
                selected.importReceiptCode ? 'Phiếu nhập: ' + selected.importReceiptCode : '',
              ].filter(Boolean).join(' • ')}
            </p>
          )}
          <UiTableScroll>
            <table className="ui-table-compact" aria-label="Dòng thời gian lịch hẹn">
              <thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Cửa kho</th><th>Sân bãi</th><th>Ghi chú</th></tr></thead>
              <tbody>
                {(selected.events ?? []).map(event => (
                  <tr key={event.id}>
                    <td>{displayWarehouseTime(event.eventAtUtc, warehouseTimeZone)}</td>
                    <td>{eventTypeLabel(event.eventType)}</td>
                    <td>{event.dockId ? docks.find(d => d.id === event.dockId)?.code ?? event.dockId : '—'}</td>
                    <td>{event.yardSlotId ? yardSlots.find(y => y.id === event.yardSlotId)?.code ?? event.yardSlotId : '—'}</td>
                    <td>{event.note || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </UiTableScroll>
          <div className="ui-inline-actions"><button type="button" onClick={() => setSelected(null)}>Đóng</button></div>
        </UiCard>
      )}

      {dockForm && canManageDock && (
        <UiCard title={dockForm.id ? 'Sửa cửa kho ' + dockForm.code : 'Thêm cửa kho'}>
          <form className="ui-form-grid" onSubmit={saveDock}>
            <label className="ui-stack"><span>Mã *</span><input value={dockForm.code} disabled={saving || Boolean(dockForm.id)} onChange={event => setDockForm(current => current && { ...current, code: event.target.value.toUpperCase() })} required /></label>
            <label className="ui-stack"><span>Tên *</span><input value={dockForm.name} disabled={saving} onChange={event => setDockForm(current => current && { ...current, name: event.target.value })} required /></label>
            <label className="ui-stack"><span>Loại phương tiện</span><input value={dockForm.allowedVehicleType} disabled={saving} onChange={event => setDockForm(current => current && { ...current, allowedVehicleType: event.target.value.toUpperCase() })} /></label>
            <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsInbound} disabled={saving} onChange={event => setDockForm(current => current && { ...current, supportsInbound: event.target.checked })} /> Nhập kho</label>
            <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsOutbound} disabled={saving} onChange={event => setDockForm(current => current && { ...current, supportsOutbound: event.target.checked })} /> Xuất kho</label>
            <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isTemperatureControlled} disabled={saving} onChange={event => setDockForm(current => current && { ...current, isTemperatureControlled: event.target.checked })} /> Kiểm soát nhiệt độ</label>
            <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.hazardAllowed} disabled={saving} onChange={event => setDockForm(current => current && { ...current, hazardAllowed: event.target.checked })} /> Cho phép hàng nguy hiểm</label>
            {dockForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isActive} disabled={saving} onChange={event => setDockForm(current => current && { ...current, isActive: event.target.checked })} /> Đang hoạt động</label>}
            <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu cửa kho</button><button type="button" onClick={() => setDockForm(null)} disabled={saving}>Hủy</button></div>
          </form>
        </UiCard>
      )}

      {yardForm && canManageDock && (
        <UiCard title={yardForm.id ? 'Sửa vị trí sân bãi ' + yardForm.code : 'Thêm vị trí sân bãi'}>
          <form className="ui-form-grid" onSubmit={saveYard}>
            <label className="ui-stack"><span>Mã *</span><input value={yardForm.code} disabled={saving || Boolean(yardForm.id)} onChange={event => setYardForm(current => current && { ...current, code: event.target.value.toUpperCase() })} required /></label>
            <label className="ui-stack"><span>Tên *</span><input value={yardForm.name} disabled={saving} onChange={event => setYardForm(current => current && { ...current, name: event.target.value })} required /></label>
            {yardForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={yardForm.isActive} disabled={saving} onChange={event => setYardForm(current => current && { ...current, isActive: event.target.checked })} /> Đang hoạt động</label>}
            <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu vị trí sân bãi</button><button type="button" onClick={() => setYardForm(null)} disabled={saving}>Hủy</button></div>
          </form>
        </UiCard>
      )}

      {actionPanel && (
        <UiCard title={actionPanel.mode === 'checkin' ? 'Ghi nhận vào cổng • ' + actionPanel.appointment.code : actionPanel.mode === 'assignDock' ? 'Gán cửa kho • ' + actionPanel.appointment.code : actionPanel.mode === 'linkInbound' ? 'Liên kết chứng từ nhập • ' + actionPanel.appointment.code : 'Ghi nhận ngoại lệ • ' + actionPanel.appointment.code}>
          {actionPanel.mode === 'checkin' && (
            <form className="ui-form-grid" onSubmit={event => {
              event.preventDefault();
              void runCommand(actionPanel.appointment, 'check-in', {
                vehiclePlate: actionPanel.vehiclePlate,
                trailerPlate: actionPanel.trailerPlate || null,
                driverName: actionPanel.driverName,
                driverPhone: actionPanel.driverPhone || null,
                sealNumber: actionPanel.sealNumber || null,
                yardSlotId: actionPanel.yardSlotId ? Number(actionPanel.yardSlotId) : null,
                note: actionPanel.note || null,
              });
            }}>
              <label className="ui-stack"><span>Biển số xe *</span><input value={actionPanel.vehiclePlate} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, vehiclePlate: event.target.value.toUpperCase() } : current)} required /></label>
              <label className="ui-stack"><span>Rơ-moóc</span><input value={actionPanel.trailerPlate} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, trailerPlate: event.target.value.toUpperCase() } : current)} /></label>
              <label className="ui-stack"><span>Tài xế *</span><input value={actionPanel.driverName} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, driverName: event.target.value } : current)} required /></label>
              <label className="ui-stack"><span>Điện thoại</span><input value={actionPanel.driverPhone} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, driverPhone: event.target.value } : current)} /></label>
              <label className="ui-stack"><span>Số niêm phong</span><input value={actionPanel.sealNumber} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, sealNumber: event.target.value.toUpperCase() } : current)} /></label>
              <label className="ui-stack"><span>Vị trí sân bãi</span><select value={actionPanel.yardSlotId} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, yardSlotId: event.target.value } : current)}><option value="">Chưa gán</option>{yardSlots.filter(slot => slot.isActive && !slot.occupied).map(slot => <option key={slot.id} value={slot.id}>{slot.code} — {slot.name}</option>)}</select></label>
              <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, note: event.target.value } : current)} /></label>
              <div className="ui-inline-actions"><button type="submit" disabled={saving}>Ghi nhận vào cổng</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
            </form>
          )}
          {actionPanel.mode === 'assignDock' && (
            <form className="ui-form-grid" onSubmit={event => {
              event.preventDefault();
              void runCommand(actionPanel.appointment, 'assign-dock', { dockId: Number(actionPanel.dockId), note: actionPanel.note || null });
            }}>
              <label className="ui-stack"><span>Cửa kho *</span><select value={actionPanel.dockId} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, dockId: event.target.value } : current)} required><option value="">Chọn cửa kho</option>{docks.filter(dock => dock.isActive).map(dock => <option key={dock.id} value={dock.id}>{dock.code} — {dock.name}</option>)}</select></label>
              <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, note: event.target.value } : current)} /></label>
              <div className="ui-inline-actions"><button type="submit" disabled={saving || !actionPanel.dockId}>Gán cửa kho</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
            </form>
          )}
          {actionPanel.mode === 'exception' && (
            <form className="ui-form-grid" onSubmit={event => {
              event.preventDefault();
              void runCommand(actionPanel.appointment, 'exception', { exceptionCode: actionPanel.exceptionCode, note: actionPanel.note || null });
            }}>
              <label className="ui-stack"><span>Mã ngoại lệ *</span><select value={actionPanel.exceptionCode} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, exceptionCode: event.target.value } : current)} required>
                <option value="">Chọn nguyên nhân</option><option value="LATE_ARRIVAL">Đến trễ</option><option value="EARLY_ARRIVAL">Đến sớm</option><option value="VEHICLE_MISMATCH">Sai phương tiện</option><option value="DOCK_UNAVAILABLE">Cửa kho không khả dụng</option><option value="DAMAGED_SEAL">Niêm phong hư hỏng</option><option value="CAPACITY_ISSUE">Vấn đề sức chứa</option><option value="WAITING_TIME_BREACH">Vượt thời gian chờ</option>
              </select></label>
              <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, note: event.target.value } : current)} /></label>
              <div className="ui-inline-actions"><button type="submit" disabled={saving}>Ghi nhận ngoại lệ</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
            </form>
          )}
          {actionPanel.mode === 'linkInbound' && (
            <form className="ui-form-grid" onSubmit={event => {
              event.preventDefault();
              void runCommand(actionPanel.appointment, 'link-inbound', {
                purchaseOrderId: actionPanel.purchaseOrderId.trim() ? Number(actionPanel.purchaseOrderId) : null,
                asnId: actionPanel.asnId.trim() ? Number(actionPanel.asnId) : null,
                importReceiptId: actionPanel.importReceiptId.trim() ? Number(actionPanel.importReceiptId) : null,
                note: actionPanel.note.trim() || null,
              });
            }}>
              <label className="ui-stack"><span>Đơn mua hàng (PO)</span><select aria-label="Liên kết ID PO" value={actionPanel.purchaseOrderId} onChange={event => setActionPanel(current => current?.mode === 'linkInbound' ? { ...current, purchaseOrderId: event.target.value } : current)}><option value="">-- Không liên kết PO --</option>{inboundPos.map(po => <option key={po.id} value={String(po.id)}>{po.code}{po.supplierName ? ` — ${po.supplierName}` : ''}</option>)}</select></label>
              <label className="ui-stack"><span>Thông báo giao hàng (ASN)</span><select aria-label="Liên kết ID ASN" value={actionPanel.asnId} onChange={event => setActionPanel(current => current?.mode === 'linkInbound' ? { ...current, asnId: event.target.value } : current)}><option value="">-- Không liên kết ASN --</option>{inboundAsns.map(asn => <option key={asn.id} value={String(asn.id)}>{asn.code}{asn.supplierName ? ` — ${asn.supplierName}` : ''}</option>)}</select></label>
              <label className="ui-stack"><span>Phiếu nhập kho (GR)</span><select aria-label="Liên kết ID GR" value={actionPanel.importReceiptId} onChange={event => setActionPanel(current => current?.mode === 'linkInbound' ? { ...current, importReceiptId: event.target.value } : current)}><option value="">-- Không liên kết Phiếu nhập --</option>{inboundReceipts.map(gr => <option key={gr.id} value={String(gr.id)}>{gr.code}{gr.supplierName ? ` — ${gr.supplierName}` : ''}</option>)}</select></label>
              <label className="ui-stack"><span>Ghi chú</span><textarea aria-label="Ghi chú liên kết chứng từ" value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'linkInbound' ? { ...current, note: event.target.value } : current)} /></label>
              <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu liên kết chứng từ</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
            </form>
          )}
        </UiCard>
      )}
    </>
  );
}
