import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiTableScroll, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type Warehouse = { id: number; code: string; name: string };
type CalendarDay = { dayOfWeek: number; dayName: string; isOpen: boolean; opensAtLocal?: string | null; closesAtLocal?: string | null; inboundCutoffLocal?: string | null; outboundCutoffLocal?: string | null; overnight: boolean };
type Shift = {
  id: number; warehouseId: number; code: string; name: string; startTimeLocal: string; endTimeLocal: string; overnight: boolean;
  breakMinutes: number; plannedHeadcount: number; inboundPalletsPerHour?: number | null; outboundOrdersPerHour?: number | null;
  dockSlots?: number | null; laborHours?: number | null; stagingCapacity?: number | null; packingStations?: number | null;
  equipmentAvailable?: number | null; isActive: boolean; rowVersion?: string | null;
};
type CalendarResponse = {
  warehouseId: number; warehouseCode: string; warehouseName: string; isConfigured: boolean; timeZoneId: string; localNow: string;
  isOpenNow: boolean; currentShiftCode?: string | null; rowVersion?: string | null; days: CalendarDay[]; shifts: Shift[];
};
type DayForm = Omit<CalendarDay, 'dayName' | 'overnight'>;
type ShiftForm = {
  id?: number; code: string; name: string; startTimeLocal: string; endTimeLocal: string; breakMinutes: string; plannedHeadcount: string;
  inboundPalletsPerHour: string; outboundOrdersPerHour: string; dockSlots: string; laborHours: string; stagingCapacity: string;
  packingStations: string; equipmentAvailable: string; isActive: boolean; rowVersion?: string | null;
};

const emptyShift = (): ShiftForm => ({
  code: '', name: '', startTimeLocal: '06:00', endTimeLocal: '14:00', breakMinutes: '30', plannedHeadcount: '0',
  inboundPalletsPerHour: '', outboundOrdersPerHour: '', dockSlots: '', laborHours: '', stagingCapacity: '', packingStations: '',
  equipmentAvailable: '', isActive: true,
});

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  const base = response.response?.data?.message || fallback;
  return response.response?.status === 409 ? base + ' Hãy tải lại dữ liệu trước khi thử lại.' : base;
};
const decimalOrNull = (value: string) => value.trim() === '' ? null : Number(value);
const integerOrNull = (value: string) => value.trim() === '' ? null : Number(value);

const WarehouseCalendar = () => {
  const canManage = usePermission('warehouse_calendar.manage');
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState(0);
  const [calendar, setCalendar] = useState<CalendarResponse | null>(null);
  const [timeZoneId, setTimeZoneId] = useState('Asia/Ho_Chi_Minh');
  const [days, setDays] = useState<DayForm[]>([]);
  const [shiftForm, setShiftForm] = useState<ShiftForm | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const applyCalendar = useCallback((data: CalendarResponse) => {
    setCalendar(data);
    setTimeZoneId(data.timeZoneId || 'Asia/Ho_Chi_Minh');
    setDays(data.days.map(day => ({
      dayOfWeek: day.dayOfWeek,
      isOpen: day.isOpen,
      opensAtLocal: day.opensAtLocal ?? '',
      closesAtLocal: day.closesAtLocal ?? '',
      inboundCutoffLocal: day.inboundCutoffLocal ?? '',
      outboundCutoffLocal: day.outboundCutoffLocal ?? '',
    })));
  }, []);

  const loadCalendar = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) { setCalendar(null); return; }
    const response = await apiClient.get('/api/warehouses/' + targetWarehouseId + '/calendar');
    applyCalendar(response.data as CalendarResponse);
  }, [applyCalendar]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    void apiClient.get('/api/warehouses').then(response => {
      if (cancelled) return;
      const rows = response.data as Warehouse[];
      setWarehouses(rows);
      setWarehouseId(rows[0]?.id ?? 0);
    }).catch(failure => {
      if (!cancelled) setError(messageOf(failure, 'Không thể tải danh sách kho.'));
    }).finally(() => {
      if (!cancelled) setLoading(false);
    });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (!warehouseId || loading) return;
    setError('');
    setSuccess('');
    void loadCalendar(warehouseId).catch(failure => setError(messageOf(failure, 'Không thể tải lịch vận hành kho.')));
  }, [warehouseId, loading, loadCalendar]);

  const saveCalendar = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!calendar || saving) return;
    setSaving(true); setError(''); setSuccess('');
    try {
      await apiClient.put('/api/warehouses/' + warehouseId + '/calendar', { timeZoneId: timeZoneId.trim(), rowVersion: calendar.rowVersion, days });
      setSuccess('Đã cập nhật lịch vận hành kho.');
      await loadCalendar(warehouseId);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu lịch vận hành kho.'));
    } finally { setSaving(false); }
  };

  const openShift = (shift?: Shift) => {
    setError(''); setSuccess('');
    setShiftForm(shift ? {
      id: shift.id, code: shift.code, name: shift.name, startTimeLocal: shift.startTimeLocal, endTimeLocal: shift.endTimeLocal,
      breakMinutes: String(shift.breakMinutes), plannedHeadcount: String(shift.plannedHeadcount),
      inboundPalletsPerHour: shift.inboundPalletsPerHour?.toString() ?? '', outboundOrdersPerHour: shift.outboundOrdersPerHour?.toString() ?? '',
      dockSlots: shift.dockSlots?.toString() ?? '', laborHours: shift.laborHours?.toString() ?? '', stagingCapacity: shift.stagingCapacity?.toString() ?? '',
      packingStations: shift.packingStations?.toString() ?? '', equipmentAvailable: shift.equipmentAvailable?.toString() ?? '',
      isActive: shift.isActive, rowVersion: shift.rowVersion,
    } : emptyShift());
  };

  const saveShift = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!shiftForm || saving) return;
    const payload = {
      code: shiftForm.code.trim(), name: shiftForm.name.trim(), startTimeLocal: shiftForm.startTimeLocal, endTimeLocal: shiftForm.endTimeLocal,
      breakMinutes: Number(shiftForm.breakMinutes), plannedHeadcount: Number(shiftForm.plannedHeadcount),
      inboundPalletsPerHour: decimalOrNull(shiftForm.inboundPalletsPerHour), outboundOrdersPerHour: decimalOrNull(shiftForm.outboundOrdersPerHour),
      dockSlots: integerOrNull(shiftForm.dockSlots), laborHours: decimalOrNull(shiftForm.laborHours), stagingCapacity: decimalOrNull(shiftForm.stagingCapacity),
      packingStations: integerOrNull(shiftForm.packingStations), equipmentAvailable: integerOrNull(shiftForm.equipmentAvailable),
      isActive: shiftForm.isActive, rowVersion: shiftForm.rowVersion,
    };
    setSaving(true); setError(''); setSuccess('');
    try {
      if (shiftForm.id) {
        await apiClient.put('/api/warehouses/' + warehouseId + '/calendar/shifts/' + shiftForm.id, payload);
        setSuccess('Đã cập nhật ca làm việc.');
      } else {
        await apiClient.post('/api/warehouses/' + warehouseId + '/calendar/shifts', payload);
        setSuccess('Đã tạo ca làm việc.');
      }
      setShiftForm(null);
      await loadCalendar(warehouseId);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu ca làm việc.'));
    } finally { setSaving(false); }
  };

  const activeShifts = calendar?.shifts.filter(shift => shift.isActive).length ?? 0;
  const openDays = calendar?.days.filter(day => day.isOpen).length ?? 0;

  return (
    <UiPage>
      <UiPageHeader eyebrow="Kho & Vị trí" title="Lịch vận hành & Ca kho"
        description="Cấu hình timezone, lịch tuần, cutoff và capacity theo ca. Holiday, maintenance và emergency override được quản lý riêng ở WH-07." />
      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void loadCalendar(warehouseId)}>Tải lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={calendar?.isOpenNow ? 'Đang mở' : 'Đang đóng'} label="Trạng thái vận hành" />
        <UiMetric value={calendar?.currentShiftCode || '—'} label="Ca hiện tại" />
        <UiMetric value={openDays + '/7'} label="Ngày mở cửa / tuần" />
        <UiMetric value={activeShifts} label="Ca đang hoạt động" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho lịch vận hành" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">Local time: {calendar?.localNow ? new Date(calendar.localNow).toLocaleString('vi-VN', { timeZone: calendar.timeZoneId }) : '—'} • {calendar?.timeZoneId || '—'}</div>
      </UiToolbar>

      {loading ? <p role="status">Đang tải lịch vận hành...</p> : warehouses.length === 0 ? <UiEmptyState title="Không có kho nào trong phạm vi được phép." /> : calendar ? (
        <>
          <UiCard title="Lịch vận hành tuần">
            {!calendar.isConfigured && <p role="status"><UiBadge tone="warning">Chưa cấu hình</UiBadge> Kho chưa có lịch vận hành. Manager có thể thiết lập baseline đầu tiên.</p>}
            <form onSubmit={saveCalendar}>
              <label className="ui-stack"><span>Warehouse timezone</span><input aria-label="Warehouse timezone" value={timeZoneId} onChange={event => setTimeZoneId(event.target.value)} disabled={!canManage || saving} placeholder="Asia/Ho_Chi_Minh" /></label>
              <UiTableScroll>
                <table aria-label="Lịch vận hành tuần">
                  <thead><tr><th>Ngày</th><th>Mở cửa</th><th>Mở</th><th>Đóng</th><th>Inbound cutoff</th><th>Outbound cutoff</th></tr></thead>
                  <tbody>{calendar.days.map((day, index) => {
                    const form = days[index];
                    return <tr key={day.dayOfWeek}>
                      <td><strong>{day.dayName}</strong>{day.overnight && <><br /><small>Qua nửa đêm</small></>}</td>
                      <td><input aria-label={'Mở cửa ' + day.dayName} type="checkbox" checked={form?.isOpen ?? false} disabled={!canManage || saving} onChange={event => setDays(current => current.map((item, i) => i === index ? { ...item, isOpen: event.target.checked, opensAtLocal: event.target.checked ? (item.opensAtLocal || '06:00') : '', closesAtLocal: event.target.checked ? (item.closesAtLocal || '22:00') : '', inboundCutoffLocal: event.target.checked ? item.inboundCutoffLocal : '', outboundCutoffLocal: event.target.checked ? item.outboundCutoffLocal : '' } : item))} /></td>
                      <td><input aria-label={'Giờ mở ' + day.dayName} type="time" value={form?.opensAtLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={event => setDays(current => current.map((item, i) => i === index ? { ...item, opensAtLocal: event.target.value } : item))} /></td>
                      <td><input aria-label={'Giờ đóng ' + day.dayName} type="time" value={form?.closesAtLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={event => setDays(current => current.map((item, i) => i === index ? { ...item, closesAtLocal: event.target.value } : item))} /></td>
                      <td><input aria-label={'Inbound cutoff ' + day.dayName} type="time" value={form?.inboundCutoffLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={event => setDays(current => current.map((item, i) => i === index ? { ...item, inboundCutoffLocal: event.target.value } : item))} /></td>
                      <td><input aria-label={'Outbound cutoff ' + day.dayName} type="time" value={form?.outboundCutoffLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={event => setDays(current => current.map((item, i) => i === index ? { ...item, outboundCutoffLocal: event.target.value } : item))} /></td>
                    </tr>;
                  })}</tbody>
                </table>
              </UiTableScroll>
              {canManage && <div className="ui-inline-actions"><button type="submit" disabled={saving || days.length !== 7}>{saving ? 'Đang lưu...' : 'Lưu lịch tuần'}</button></div>}
            </form>
          </UiCard>

          <UiCard title="Ca làm việc & Capacity">
            <div className="ui-inline-actions">{canManage && <button type="button" className="ui-primary-button" onClick={() => openShift()} disabled={!calendar.isConfigured}>Thêm ca</button>}</div>
            <UiTableScroll>
              <table aria-label="Ca làm việc và capacity">
                <thead><tr><th>Ca</th><th>Khung giờ</th><th>Headcount</th><th>Inbound pallet/h</th><th>Outbound order/h</th><th>Dock</th><th>Labor h</th><th>Staging</th><th>Packing</th><th>Equipment</th><th>Trạng thái</th>{canManage && <th>Thao tác</th>}</tr></thead>
                <tbody>
                  {calendar.shifts.map(shift => <tr key={shift.id}>
                    <td><strong>{shift.code}</strong><br /><small>{shift.name}</small></td>
                    <td>{shift.startTimeLocal}–{shift.endTimeLocal}{shift.overnight ? ' • +1 ngày' : ''}<br /><small>Nghỉ {shift.breakMinutes} phút</small></td>
                    <td>{shift.plannedHeadcount}</td><td>{shift.inboundPalletsPerHour ?? '—'}</td><td>{shift.outboundOrdersPerHour ?? '—'}</td>
                    <td>{shift.dockSlots ?? '—'}</td><td>{shift.laborHours ?? '—'}</td><td>{shift.stagingCapacity ?? '—'}</td>
                    <td>{shift.packingStations ?? '—'}</td><td>{shift.equipmentAvailable ?? '—'}</td>
                    <td><UiBadge tone={shift.isActive ? 'success' : 'neutral'}>{shift.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                    {canManage && <td><button type="button" onClick={() => openShift(shift)}>Sửa</button></td>}
                  </tr>)}
                  {calendar.shifts.length === 0 && <tr><td className="ui-empty-cell" colSpan={canManage ? 12 : 11}>Chưa có ca làm việc.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          {shiftForm && canManage && <UiCard title={shiftForm.id ? 'Sửa ca ' + shiftForm.code : 'Thêm ca làm việc'}>
            <form className="ui-form-grid" onSubmit={saveShift}>
              <label className="ui-stack"><span>Mã ca *</span><input aria-label="Mã ca" value={shiftForm.code} disabled={saving || Boolean(shiftForm.id)} onChange={event => setShiftForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
              <label className="ui-stack"><span>Tên ca *</span><input aria-label="Tên ca" value={shiftForm.name} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Bắt đầu *</span><input aria-label="Giờ bắt đầu ca" type="time" value={shiftForm.startTimeLocal} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, startTimeLocal: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Kết thúc *</span><input aria-label="Giờ kết thúc ca" type="time" value={shiftForm.endTimeLocal} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, endTimeLocal: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Nghỉ (phút)</span><input aria-label="Phút nghỉ ca" type="number" min="0" value={shiftForm.breakMinutes} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, breakMinutes: event.target.value }))} /></label>
              <label className="ui-stack"><span>Planned headcount</span><input aria-label="Planned headcount" type="number" min="0" value={shiftForm.plannedHeadcount} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, plannedHeadcount: event.target.value }))} /></label>
              <label className="ui-stack"><span>Inbound pallets/hour</span><input aria-label="Inbound pallets per hour" type="number" min="0" step="0.01" value={shiftForm.inboundPalletsPerHour} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, inboundPalletsPerHour: event.target.value }))} /></label>
              <label className="ui-stack"><span>Outbound orders/hour</span><input aria-label="Outbound orders per hour" type="number" min="0" step="0.01" value={shiftForm.outboundOrdersPerHour} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, outboundOrdersPerHour: event.target.value }))} /></label>
              <label className="ui-stack"><span>Dock slots</span><input aria-label="Dock slots" type="number" min="0" value={shiftForm.dockSlots} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, dockSlots: event.target.value }))} /></label>
              <label className="ui-stack"><span>Labor hours</span><input aria-label="Labor hours" type="number" min="0" step="0.01" value={shiftForm.laborHours} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, laborHours: event.target.value }))} /></label>
              <label className="ui-stack"><span>Staging capacity</span><input aria-label="Staging capacity" type="number" min="0" step="0.01" value={shiftForm.stagingCapacity} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, stagingCapacity: event.target.value }))} /></label>
              <label className="ui-stack"><span>Packing stations</span><input aria-label="Packing stations" type="number" min="0" value={shiftForm.packingStations} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, packingStations: event.target.value }))} /></label>
              <label className="ui-stack"><span>Equipment available</span><input aria-label="Equipment available" type="number" min="0" value={shiftForm.equipmentAvailable} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, equipmentAvailable: event.target.value }))} /></label>
              {shiftForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={shiftForm.isActive} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Ca đang hoạt động</label>}
              <div className="ui-inline-actions"><button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu ca'}</button><button type="button" disabled={saving} onClick={() => setShiftForm(null)}>Hủy</button></div>
            </form>
          </UiCard>}
        </>
      ) : null}
    </UiPage>
  );
};

export default WarehouseCalendar;
