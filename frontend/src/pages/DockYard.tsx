import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { usePermission } from '../services/authorization';
import {
  UiBadge,
  UiCard,
  UiEmptyState,
  UiMetric,
  UiMetricGrid,
  UiPage,
  UiPageHeader,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
} from '../ui/ProductionUi';

type Warehouse = { id: number; code: string; name: string; timeZoneId: string; calendarConfigured: boolean };
type Dock = {
  id: number; warehouseId: number; code: string; name: string; supportsInbound: boolean; supportsOutbound: boolean;
  allowedVehicleType?: string | null; isTemperatureControlled: boolean; hazardAllowed: boolean; isActive: boolean; rowVersion?: string | null;
};
type YardSlot = { id: number; warehouseId: number; code: string; name: string; isActive: boolean; occupied: boolean; occupiedByAppointmentCode?: string | null; rowVersion?: string | null };
type AppointmentEvent = { id: number; eventType: string; eventAtUtc: string; actorUserId: number; dockId?: number | null; yardSlotId?: number | null; note?: string | null };
type Appointment = {
  id: number; warehouseId: number; warehouseCode: string; warehouseName: string; code: string; direction: number; status: number;
  plannedStartUtc: string; plannedEndUtc: string; carrierCode?: string | null; carrierName?: string | null; vehiclePlate?: string | null;
  trailerPlate?: string | null; vehicleType?: string | null; requiresTemperatureControl: boolean; hazardous: boolean;
  driverName?: string | null; driverPhone?: string | null; sealNumber?: string | null; yardSlotId?: number | null; yardSlotCode?: string | null;
  dockId?: number | null; dockCode?: string | null; arrivedAtUtc?: string | null; checkedInAtUtc?: string | null; dockAssignedAtUtc?: string | null;
  serviceStartedAtUtc?: string | null; serviceCompletedAtUtc?: string | null; checkedOutAtUtc?: string | null; exceptionCode?: string | null;
  note?: string | null; rowVersion?: string | null; events?: AppointmentEvent[];
};

type AppointmentForm = {
  id?: number; code: string; direction: '0' | '1'; plannedStart: string; plannedEnd: string; carrierCode: string; carrierName: string;
  vehiclePlate: string; trailerPlate: string; vehicleType: string; requiresTemperatureControl: boolean; hazardous: boolean; note: string; rowVersion?: string | null;
};
type DockForm = {
  id?: number; code: string; name: string; supportsInbound: boolean; supportsOutbound: boolean; allowedVehicleType: string;
  isTemperatureControlled: boolean; hazardAllowed: boolean; isActive: boolean; rowVersion?: string | null;
};
type YardForm = { id?: number; code: string; name: string; isActive: boolean; rowVersion?: string | null };
type ActionPanel =
  | { mode: 'checkin'; appointment: Appointment; vehiclePlate: string; trailerPlate: string; driverName: string; driverPhone: string; sealNumber: string; yardSlotId: string; note: string }
  | { mode: 'assignDock'; appointment: Appointment; dockId: string; note: string }
  | { mode: 'exception'; appointment: Appointment; exceptionCode: string; note: string };

const statusLabel: Record<number, string> = {
  0: 'Nháp',
  1: 'Đã xác nhận',
  2: 'Đã đến',
  3: 'Đã vào cổng',
  4: 'Đã gán cửa kho',
  5: 'Đang phục vụ',
  6: 'Hoàn thành',
  7: 'Đã hủy',
  8: 'Không đến',
  9: 'Ngoại lệ',
};
const statusTone = (status: number) =>
  status === 6 ? 'success' as const
    : status === 7 || status === 8 || status === 9 ? 'danger' as const
      : status === 0 ? 'neutral' as const
        : status === 5 ? 'warning' as const
          : 'success' as const;
const directionLabel = (value: number) => value === 0 ? 'Nhập kho' : 'Xuất kho';

const eventTypeLabel = (value: string) => ({
  CREATED: 'Đã tạo',
  CONFIRMED: 'Đã xác nhận',
  ARRIVED: 'Xe đã đến',
  CHECKED_IN: 'Đã vào cổng',
  DOCK_ASSIGNED: 'Đã gán cửa kho',
  SERVICE_STARTED: 'Bắt đầu phục vụ',
  SERVICE_COMPLETED: 'Hoàn thành phục vụ',
  CHECKED_OUT: 'Đã rời cổng',
  CANCELLED: 'Đã hủy',
  NO_SHOW: 'Không đến',
  EXCEPTION_RECORDED: 'Ghi nhận ngoại lệ',
}[value] ?? value);
const timeZoneParts = (date: Date, timeZone: string) => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date);
  return Object.fromEntries(parts.filter(part => part.type !== 'literal').map(part => [part.type, part.value]));
};

const localInput = (value: string | null | undefined, timeZone: string) => {
  if (!value) return '';
  const parts = timeZoneParts(new Date(value), timeZone);
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
};

const zonedLocalToIso = (value: string, timeZone: string) => {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) throw new Error('INVALID_LOCAL_TIME');
  const [, year, month, day, hour, minute] = match;
  const wallClockUtc = Date.UTC(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute), 0);

  const offsetAt = (instantMs: number) => {
    const parts = timeZoneParts(new Date(instantMs), timeZone);
    const represented = Date.UTC(
      Number(parts.year),
      Number(parts.month) - 1,
      Number(parts.day),
      Number(parts.hour),
      Number(parts.minute),
      Number(parts.second),
    );
    return represented - instantMs;
  };

  let instant = wallClockUtc - offsetAt(wallClockUtc);
  instant = wallClockUtc - offsetAt(instant);
  const result = new Date(instant);
  if (localInput(result.toISOString(), timeZone) !== value) throw new Error('INVALID_LOCAL_TIME');
  return result.toISOString();
};

const displayWarehouseTime = (value: string, timeZone: string) =>
  new Date(value).toLocaleString('vi-VN', { timeZone });
const emptyAppointment = (): AppointmentForm => ({
  code: '', direction: '0', plannedStart: '', plannedEnd: '', carrierCode: '', carrierName: '', vehiclePlate: '', trailerPlate: '',
  vehicleType: '', requiresTemperatureControl: false, hazardous: false, note: '',
});
const emptyDock = (): DockForm => ({
  code: '', name: '', supportsInbound: true, supportsOutbound: true, allowedVehicleType: '', isTemperatureControlled: false,
  hazardAllowed: false, isActive: true,
});
const emptyYard = (): YardForm => ({ code: '', name: '', isActive: true });

const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  const base = response.response?.data?.message || fallback;
  return response.response?.status === 409 ? base + ' Hãy tải lại dữ liệu trước khi thử lại.' : base;
};

const DockYard = () => {
  const canReadDock = usePermission('dock.read');
  const canReadYard = usePermission('yard.read');
  const canManageDock = usePermission('dock.manage');
  const canManageAppointment = usePermission('dock_appointment.manage');
  const canCheckIn = usePermission('yard.checkin');
  const canAssignDock = usePermission('yard.assign_dock');
  const canCheckout = usePermission('yard.checkout');

  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState(0);
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [warehouseTimeZone, setWarehouseTimeZone] = useState('UTC');
  const [docks, setDocks] = useState<Dock[]>([]);
  const [yardSlots, setYardSlots] = useState<YardSlot[]>([]);
  const [selected, setSelected] = useState<Appointment | null>(null);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const loadRevisionRef = useRef(0);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [appointmentForm, setAppointmentForm] = useState<AppointmentForm | null>(null);
  const [dockForm, setDockForm] = useState<DockForm | null>(null);
  const [yardForm, setYardForm] = useState<YardForm | null>(null);
  const [actionPanel, setActionPanel] = useState<ActionPanel | null>(null);

  const load = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) return;
    const revision = ++loadRevisionRef.current;
    setLoading(true);
    setError('');
    try {
      const [appointmentResponse, dockResponse, yardResponse] = await Promise.all([
        apiClient.get('/api/dock-yard/appointments', { params: { warehouseId: targetWarehouseId } }),
        canReadDock
          ? apiClient.get('/api/dock-yard/docks', { params: { warehouseId: targetWarehouseId } })
          : Promise.resolve({ data: [] }),
        canReadYard
          ? apiClient.get('/api/dock-yard/yard-slots', { params: { warehouseId: targetWarehouseId } })
          : Promise.resolve({ data: [] }),
      ]);
      if (revision !== loadRevisionRef.current) return;
      setAppointments(appointmentResponse.data as Appointment[]);
      setDocks(dockResponse.data as Dock[]);
      setYardSlots(yardResponse.data as YardSlot[]);
    } catch (failure) {
      if (revision === loadRevisionRef.current) setError(messageOf(failure, 'Không thể tải dữ liệu cửa kho & sân bãi.'));
    } finally {
      if (revision === loadRevisionRef.current) setLoading(false);
    }
  }, [canReadDock, canReadYard]);

  useEffect(() => {
    let cancelled = false;
    void apiClient.get('/api/dock-yard/warehouses').then(response => {
      if (cancelled) return;
      const rows = response.data as Warehouse[];
      setWarehouses(rows);
      setWarehouseId(rows[0]?.id ?? 0);
    }).catch(failure => {
      if (!cancelled) setError(messageOf(failure, 'Không thể tải danh sách kho.'));
    });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    const warehouse = warehouses.find(item => item.id === warehouseId);
    setWarehouseTimeZone(warehouse?.timeZoneId || 'UTC');
    setSelected(null);
    setAppointmentForm(null);
    setDockForm(null);
    setYardForm(null);
    setActionPanel(null);
    if (!warehouseId) return;
    void load(warehouseId);
  }, [warehouseId, warehouses, load]);

  const filtered = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('vi');
    return appointments.filter(item =>
      (!status || String(item.status) === status)
      && (!q || [item.code, item.carrierName ?? '', item.vehiclePlate ?? '', item.driverName ?? '', item.dockCode ?? '', item.yardSlotCode ?? '']
        .join(' ').toLocaleLowerCase('vi').includes(q))
    );
  }, [appointments, search, status]);

  const waitingCount = appointments.filter(x => [1, 2, 3].includes(x.status)).length;
  const inServiceCount = appointments.filter(x => [4, 5].includes(x.status)).length;
  const exceptionCount = appointments.filter(x => [8, 9].includes(x.status)).length;
  const activeDockCount = docks.filter(x => x.isActive).length;
  const occupiedYardCount = yardSlots.filter(x => x.occupied).length;

  const refresh = async () => {
    await load(warehouseId);
    if (selected) {
      try {
        const response = await apiClient.get('/api/dock-yard/appointments/' + selected.id);
        setSelected(response.data as Appointment);
      } catch {
        setSelected(null);
      }
    }
  };

  const runCommand = async (appointment: Appointment, suffix: string, payload: Record<string, unknown> = {}) => {
    if (!appointment.rowVersion || savingRef.current) return;
    savingRef.current = true;
    setSaving(true); setError(''); setSuccess('');
    try {
      const body = { rowVersion: appointment.rowVersion, ...payload };
      const logicalAction = 'dock-yard:' + suffix + ':' + appointment.id + ':' + appointment.rowVersion + ':' + JSON.stringify(payload);
      await apiClient.post('/api/dock-yard/appointments/' + appointment.id + '/' + suffix, body, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      setSuccess('Đã cập nhật trạng thái lịch hẹn.');
      setActionPanel(null);
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể cập nhật lịch hẹn.'));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const openAppointment = (appointment?: Appointment) => {
    setSuccess(''); setError('');
    if (!appointment) {
      setAppointmentForm(emptyAppointment());
      return;
    }
    setAppointmentForm({
      id: appointment.id,
      code: appointment.code,
      direction: String(appointment.direction) as '0' | '1',
      plannedStart: localInput(appointment.plannedStartUtc, warehouseTimeZone),
      plannedEnd: localInput(appointment.plannedEndUtc, warehouseTimeZone),
      carrierCode: appointment.carrierCode ?? '',
      carrierName: appointment.carrierName ?? '',
      vehiclePlate: appointment.vehiclePlate ?? '',
      trailerPlate: appointment.trailerPlate ?? '',
      vehicleType: appointment.vehicleType ?? '',
      requiresTemperatureControl: appointment.requiresTemperatureControl,
      hazardous: appointment.hazardous,
      note: appointment.note ?? '',
      rowVersion: appointment.rowVersion,
    });
  };

  const saveAppointment = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!appointmentForm || savingRef.current) return;
    if (!appointmentForm.plannedStart || !appointmentForm.plannedEnd) {
      setError('Cần nhập đủ thời gian bắt đầu và kết thúc.');
      return;
    }
    let payload;
    try {
      payload = {
        warehouseId,
        code: appointmentForm.code.trim(),
        direction: Number(appointmentForm.direction),
        plannedStart: zonedLocalToIso(appointmentForm.plannedStart, warehouseTimeZone),
        plannedEnd: zonedLocalToIso(appointmentForm.plannedEnd, warehouseTimeZone),
        carrierCode: appointmentForm.carrierCode.trim() || null,
        carrierName: appointmentForm.carrierName.trim() || null,
        vehiclePlate: appointmentForm.vehiclePlate.trim() || null,
        trailerPlate: appointmentForm.trailerPlate.trim() || null,
        vehicleType: appointmentForm.vehicleType.trim() || null,
        requiresTemperatureControl: appointmentForm.requiresTemperatureControl,
        hazardous: appointmentForm.hazardous,
        note: appointmentForm.note.trim() || null,
        rowVersion: appointmentForm.rowVersion,
      };
    } catch {
      setError('Giờ lịch hẹn không hợp lệ trong múi giờ của kho. Hãy kiểm tra lại ngày/giờ.');
      return;
    }
    savingRef.current = true;
    setSaving(true); setError(''); setSuccess('');
    try {
      if (appointmentForm.id) {
        await apiClient.put('/api/dock-yard/appointments/' + appointmentForm.id, payload);
        setSuccess('Đã cập nhật lịch hẹn Nháp.');
      } else {
        const logicalAction = 'dock-appointment:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/dock-yard/appointments', payload, { headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
        setSuccess('Đã tạo lịch hẹn Nháp.');
      }
      setAppointmentForm(null);
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu lịch hẹn.'));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const saveDock = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!dockForm || savingRef.current) return;
    savingRef.current = true;
    setSaving(true); setError(''); setSuccess('');
    try {
      const payload = {
        code: dockForm.code.trim(),
        name: dockForm.name.trim(),
        supportsInbound: dockForm.supportsInbound,
        supportsOutbound: dockForm.supportsOutbound,
        allowedVehicleType: dockForm.allowedVehicleType.trim() || null,
        isTemperatureControlled: dockForm.isTemperatureControlled,
        hazardAllowed: dockForm.hazardAllowed,
        isActive: dockForm.isActive,
        rowVersion: dockForm.rowVersion,
      };
      if (dockForm.id) await apiClient.put('/api/dock-yard/docks/' + dockForm.id, payload, { params: { warehouseId } });
      else {
        const logicalAction = 'dock:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/dock-yard/docks', payload, { params: { warehouseId }, headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
      }
      setDockForm(null);
      setSuccess('Đã lưu cấu hình cửa kho.');
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu cửa kho.'));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const saveYard = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!yardForm || savingRef.current) return;
    savingRef.current = true;
    setSaving(true); setError(''); setSuccess('');
    try {
      const payload = {
        code: yardForm.code.trim(),
        name: yardForm.name.trim(),
        isActive: yardForm.isActive,
        rowVersion: yardForm.rowVersion,
      };
      if (yardForm.id) await apiClient.put('/api/dock-yard/yard-slots/' + yardForm.id, payload, { params: { warehouseId } });
      else {
        const logicalAction = 'yard-slot:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/dock-yard/yard-slots', payload, { params: { warehouseId }, headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
      }
      setYardForm(null);
      setSuccess('Đã lưu vị trí sân bãi.');
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu vị trí sân bãi.'));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const showDetail = async (appointment: Appointment) => {
    setError('');
    try {
      const response = await apiClient.get('/api/dock-yard/appointments/' + appointment.id);
      setSelected(response.data as Appointment);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể tải chi tiết lịch hẹn.'));
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Điều hành cửa kho & sân bãi"
        description="Điều phối lịch xe, ghi nhận vào cổng, vị trí sân bãi và phân cửa kho. Các trạng thái tại sân bãi không làm thay đổi tồn kho; tồn kho chỉ thay đổi ở nghiệp vụ nhận hàng/xác nhận giao riêng."
        actions={canManageAppointment ? <button type="button" className="ui-primary-button" onClick={() => openAppointment()}>Tạo lịch hẹn</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void refresh()}>Tải lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={appointments.length} label="Lịch hẹn trong phạm vi" />
        <UiMetric value={waitingCount} label="Đang chờ / tại cổng" />
        <UiMetric value={inServiceCount} label="Đã gán cửa kho / phục vụ" />
        <UiMetric value={exceptionCount} label="Không đến / Ngoại lệ" />
        <UiMetric value={activeDockCount} label="Cửa kho hoạt động" />
        <UiMetric value={occupiedYardCount} label="Vị trí sân bãi đang dùng" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho cửa kho sân bãi" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}{warehouse.calendarConfigured ? '' : ' • chưa có lịch'}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Lọc trạng thái lịch hẹn" value={status} onChange={event => setStatus(event.target.value)}>
            <option value="">Tất cả</option>
            {Object.entries(statusLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm kiếm">
          <input aria-label="Tìm lịch hẹn" value={search} onChange={event => setSearch(event.target.value)} placeholder="Mã, đơn vị vận chuyển, xe, tài xế, cửa kho..." />
        </UiToolbarField>
        <button type="button" onClick={() => void refresh()} disabled={!warehouseId || loading}>Làm mới</button>
      </UiToolbar>

      {loading ? <p role="status">Đang tải dữ liệu cửa kho & sân bãi...</p> : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          <UiCard title="Vận hành lịch hẹn">
            <UiTableScroll>
              <table aria-label="Danh sách lịch hẹn cửa kho sân bãi">
                <thead><tr><th>Lịch hẹn</th><th>Khung giờ</th><th>Đơn vị vận chuyển / Phương tiện</th><th>Sân bãi</th><th>Cửa kho</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
                <tbody>
                  {filtered.map(item => (
                    <tr key={item.id}>
                      <td><strong>{item.code}</strong><br /><small>{directionLabel(item.direction)}</small></td>
                      <td>{displayWarehouseTime(item.plannedStartUtc, warehouseTimeZone)}<br /><small>→ {displayWarehouseTime(item.plannedEndUtc, warehouseTimeZone)} • {warehouseTimeZone}</small></td>
                      <td>{item.carrierName || item.carrierCode || '—'}<br /><small>{item.vehiclePlate || 'Chưa có biển số'}</small></td>
                      <td>{item.yardSlotCode || '—'}</td>
                      <td>{item.dockCode || '—'}</td>
                      <td><UiBadge tone={statusTone(item.status)}>{statusLabel[item.status] || item.status}</UiBadge>{item.checkedOutAtUtc && <><br /><small>Đã rời cổng</small></>}</td>
                      <td>
                        <div className="ui-inline-actions">
                          <button type="button" aria-label={'Chi tiết lịch hẹn ' + item.code} onClick={() => void showDetail(item)}>Chi tiết</button>
                          {canManageAppointment && item.status === 0 && <button type="button" aria-label={'Sửa lịch hẹn ' + item.code} onClick={() => openAppointment(item)}>Sửa</button>}
                          {canManageAppointment && item.status === 0 && <button type="button" aria-label={'Xác nhận lịch hẹn ' + item.code} onClick={() => void runCommand(item, 'confirm')}>Xác nhận</button>}
                          {canCheckIn && item.status === 1 && <button type="button" aria-label={'Ghi nhận xe đến ' + item.code} onClick={() => void runCommand(item, 'arrive')}>Xe đến</button>}
                          {canCheckIn && item.status === 2 && <button type="button" aria-label={'Ghi nhận vào cổng cho lịch hẹn ' + item.code} onClick={() => setActionPanel({ mode: 'checkin', appointment: item, vehiclePlate: item.vehiclePlate ?? '', trailerPlate: item.trailerPlate ?? '', driverName: '', driverPhone: '', sealNumber: '', yardSlotId: '', note: '' })}>Ghi nhận vào cổng</button>}
                          {canAssignDock && [3, 4].includes(item.status) && <button type="button" aria-label={(item.status === 4 ? 'Đổi cửa kho cho lịch hẹn ' : 'Gán cửa kho cho lịch hẹn ') + item.code} onClick={() => setActionPanel({ mode: 'assignDock', appointment: item, dockId: item.dockId?.toString() ?? '', note: '' })}>{item.status === 4 ? 'Đổi cửa kho' : 'Gán cửa kho'}</button>}
                          {canManageAppointment && item.status === 4 && <button type="button" aria-label={'Bắt đầu phục vụ lịch hẹn ' + item.code} onClick={() => void runCommand(item, 'start-service')}>Bắt đầu</button>}
                          {canManageAppointment && item.status === 5 && <button type="button" aria-label={'Hoàn thành phục vụ lịch hẹn ' + item.code} onClick={() => void runCommand(item, 'complete')}>Hoàn thành</button>}
                          {canCheckout && (item.status === 6 || (item.status === 9 && Boolean(item.checkedInAtUtc))) && !item.checkedOutAtUtc && <button type="button" aria-label={'Ghi nhận rời cổng cho lịch hẹn ' + item.code} onClick={() => void runCommand(item, 'checkout')}>Rời cổng</button>}
                          {canManageAppointment && [0, 1].includes(item.status) && <button type="button" aria-label={'Hủy lịch hẹn ' + item.code} onClick={() => window.confirm('Hủy lịch hẹn này?') && void runCommand(item, 'cancel')}>Hủy</button>}
                          {canManageAppointment && [1, 2, 3].includes(item.status) && <button type="button" aria-label={'Đánh dấu không đến cho lịch hẹn ' + item.code} onClick={() => void runCommand(item, 'no-show')}>Không đến</button>}
                          {canManageAppointment && ![6, 7, 8, 9].includes(item.status) && <button type="button" aria-label={'Ghi nhận ngoại lệ cho lịch hẹn ' + item.code} onClick={() => setActionPanel({ mode: 'exception', appointment: item, exceptionCode: '', note: '' })}>Ngoại lệ</button>}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {filtered.length === 0 && <tr><td className="ui-empty-cell" colSpan={7}>Không có lịch hẹn phù hợp.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          <UiCard title="Danh mục cửa kho">
            {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setDockForm(emptyDock())}>Thêm cửa kho</button></div>}
            <UiTableScroll>
              <table aria-label="Danh sách cửa kho">
                <thead><tr><th>Mã</th><th>Tên</th><th>Luồng</th><th>Phương tiện</th><th>Nhiệt độ</th><th>Hàng nguy hiểm</th><th>Trạng thái</th>{canManageDock && <th>Thao tác</th>}</tr></thead>
                <tbody>{docks.map(dock => <tr key={dock.id}>
                  <td><strong>{dock.code}</strong></td><td>{dock.name}</td>
                  <td>{[dock.supportsInbound ? 'Nhập kho' : '', dock.supportsOutbound ? 'Xuất kho' : ''].filter(Boolean).join(' + ')}</td>
                  <td>{dock.allowedVehicleType || 'Mọi loại'}</td><td>{dock.isTemperatureControlled ? 'Có' : 'Không'}</td><td>{dock.hazardAllowed ? 'Cho phép' : 'Không'}</td>
                  <td><UiBadge tone={dock.isActive ? 'success' : 'neutral'}>{dock.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                  {canManageDock && <td><button type="button" aria-label={'Sửa cửa kho ' + dock.code} onClick={() => setDockForm({ id: dock.id, code: dock.code, name: dock.name, supportsInbound: dock.supportsInbound, supportsOutbound: dock.supportsOutbound, allowedVehicleType: dock.allowedVehicleType ?? '', isTemperatureControlled: dock.isTemperatureControlled, hazardAllowed: dock.hazardAllowed, isActive: dock.isActive, rowVersion: dock.rowVersion })}>Sửa</button></td>}
                </tr>)}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          <UiCard title="Vị trí sân bãi">
            {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setYardForm(emptyYard())}>Thêm vị trí sân bãi</button></div>}
            <UiTableScroll>
              <table aria-label="Danh sách vị trí sân bãi">
                <thead><tr><th>Mã</th><th>Tên</th><th>Trạng thái</th><th>Tình trạng sử dụng</th>{canManageDock && <th>Thao tác</th>}</tr></thead>
                <tbody>{yardSlots.map(slot => <tr key={slot.id}>
                  <td><strong>{slot.code}</strong></td><td>{slot.name}</td><td><UiBadge tone={slot.isActive ? 'success' : 'neutral'}>{slot.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                  <td>{slot.occupied ? <UiBadge tone="warning">{slot.occupiedByAppointmentCode || 'Đang dùng'}</UiBadge> : <UiBadge tone="success">Trống</UiBadge>}</td>
                  {canManageDock && <td><button type="button" aria-label={'Sửa vị trí sân bãi ' + slot.code} onClick={() => setYardForm({ id: slot.id, code: slot.code, name: slot.name, isActive: slot.isActive, rowVersion: slot.rowVersion })}>Sửa</button></td>}
                </tr>)}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>
        </>
      )}

      {selected && <UiCard title={'Dòng thời gian • ' + selected.code}>
        <p><strong>{statusLabel[selected.status]}</strong> • {directionLabel(selected.direction)} • {selected.vehiclePlate || 'Chưa có xe'} • {selected.dockCode || 'Chưa gán cửa kho'}</p>
        <UiTableScroll>
          <table aria-label="Dòng thời gian lịch hẹn">
            <thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Cửa kho</th><th>Sân bãi</th><th>Ghi chú</th></tr></thead>
            <tbody>{(selected.events ?? []).map(event => <tr key={event.id}>
              <td>{new Date(event.eventAtUtc).toLocaleString('vi-VN')}</td><td>{eventTypeLabel(event.eventType)}</td><td>{event.dockId ?? '—'}</td><td>{event.yardSlotId ?? '—'}</td><td>{event.note || '—'}</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>
        <div className="ui-inline-actions"><button type="button" onClick={() => setSelected(null)}>Đóng</button></div>
      </UiCard>}

      {appointmentForm && canManageAppointment && <UiCard title={appointmentForm.id ? 'Sửa lịch hẹn ' + appointmentForm.code : 'Tạo lịch hẹn'}>
        <form className="ui-form-grid" onSubmit={saveAppointment}>
          <label className="ui-stack"><span>Mã *</span><input aria-label="Mã lịch hẹn" value={appointmentForm.code} disabled={saving || Boolean(appointmentForm.id)} onChange={event => setAppointmentForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Luồng *</span><select aria-label="Luồng lịch hẹn" value={appointmentForm.direction} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, direction: event.target.value as '0' | '1' }))}><option value="0">Nhập kho</option><option value="1">Xuất kho</option></select></label>
          <label className="ui-stack"><span>Bắt đầu * • {warehouseTimeZone}</span><input aria-label="Bắt đầu lịch hẹn theo múi giờ kho" type="datetime-local" value={appointmentForm.plannedStart} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, plannedStart: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Kết thúc * • {warehouseTimeZone}</span><input aria-label="Kết thúc lịch hẹn theo múi giờ kho" type="datetime-local" value={appointmentForm.plannedEnd} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, plannedEnd: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Mã đơn vị vận chuyển</span><input aria-label="Mã đơn vị vận chuyển" value={appointmentForm.carrierCode} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, carrierCode: event.target.value }))} /></label>
          <label className="ui-stack"><span>Tên đơn vị vận chuyển</span><input aria-label="Tên đơn vị vận chuyển" value={appointmentForm.carrierName} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, carrierName: event.target.value }))} /></label>
          <label className="ui-stack"><span>Biển số xe dự kiến</span><input aria-label="Biển số xe dự kiến" value={appointmentForm.vehiclePlate} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, vehiclePlate: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-stack"><span>Rơ-moóc</span><input aria-label="Rơ-moóc dự kiến" value={appointmentForm.trailerPlate} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, trailerPlate: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-stack"><span>Loại phương tiện</span><input aria-label="Loại phương tiện" value={appointmentForm.vehicleType} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, vehicleType: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.requiresTemperatureControl} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, requiresTemperatureControl: event.target.checked }))} /> Yêu cầu kiểm soát nhiệt độ</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.hazardous} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, hazardous: event.target.checked }))} /> Hàng nguy hiểm</label>
          <label className="ui-stack"><span>Ghi chú</span><textarea aria-label="Ghi chú lịch hẹn" value={appointmentForm.note} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, note: event.target.value }))} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu lịch hẹn'}</button><button type="button" disabled={saving} onClick={() => setAppointmentForm(null)}>Hủy</button></div>
        </form>
      </UiCard>}

      {dockForm && canManageDock && <UiCard title={dockForm.id ? 'Sửa cửa kho ' + dockForm.code : 'Thêm cửa kho'}>
        <form className="ui-form-grid" onSubmit={saveDock}>
          <label className="ui-stack"><span>Mã *</span><input value={dockForm.code} disabled={saving || Boolean(dockForm.id)} onChange={event => setDockForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Tên *</span><input value={dockForm.name} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Loại phương tiện</span><input value={dockForm.allowedVehicleType} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, allowedVehicleType: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsInbound} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, supportsInbound: event.target.checked }))} /> Nhập kho</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsOutbound} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, supportsOutbound: event.target.checked }))} /> Xuất kho</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isTemperatureControlled} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, isTemperatureControlled: event.target.checked }))} /> Kiểm soát nhiệt độ</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.hazardAllowed} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, hazardAllowed: event.target.checked }))} /> Cho phép hàng nguy hiểm</label>
          {dockForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isActive} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Đang hoạt động</label>}
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu cửa kho</button><button type="button" onClick={() => setDockForm(null)} disabled={saving}>Hủy</button></div>
        </form>
      </UiCard>}

      {yardForm && canManageDock && <UiCard title={yardForm.id ? 'Sửa vị trí sân bãi ' + yardForm.code : 'Thêm vị trí sân bãi'}>
        <form className="ui-form-grid" onSubmit={saveYard}>
          <label className="ui-stack"><span>Mã *</span><input value={yardForm.code} disabled={saving || Boolean(yardForm.id)} onChange={event => setYardForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Tên *</span><input value={yardForm.name} disabled={saving} onChange={event => setYardForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
          {yardForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={yardForm.isActive} disabled={saving} onChange={event => setYardForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Đang hoạt động</label>}
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu vị trí sân bãi</button><button type="button" onClick={() => setYardForm(null)} disabled={saving}>Hủy</button></div>
        </form>
      </UiCard>}

      {actionPanel && <UiCard title={actionPanel.mode === 'checkin' ? 'Ghi nhận vào cổng • ' + actionPanel.appointment.code : actionPanel.mode === 'assignDock' ? 'Gán cửa kho • ' + actionPanel.appointment.code : 'Ghi nhận ngoại lệ • ' + actionPanel.appointment.code}>
        {actionPanel.mode === 'checkin' && <form className="ui-form-grid" onSubmit={event => {
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
        </form>}
        {actionPanel.mode === 'assignDock' && <form className="ui-form-grid" onSubmit={event => {
          event.preventDefault();
          void runCommand(actionPanel.appointment, 'assign-dock', { dockId: Number(actionPanel.dockId), note: actionPanel.note || null });
        }}>
          <label className="ui-stack"><span>Cửa kho *</span><select value={actionPanel.dockId} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, dockId: event.target.value } : current)} required><option value="">Chọn cửa kho</option>{docks.filter(dock => dock.isActive).map(dock => <option key={dock.id} value={dock.id}>{dock.code} — {dock.name}</option>)}</select></label>
          <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, note: event.target.value } : current)} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving || !actionPanel.dockId}>Gán cửa kho</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
        </form>}
        {actionPanel.mode === 'exception' && <form className="ui-form-grid" onSubmit={event => {
          event.preventDefault();
          void runCommand(actionPanel.appointment, 'exception', { exceptionCode: actionPanel.exceptionCode, note: actionPanel.note || null });
        }}>
          <label className="ui-stack"><span>Mã ngoại lệ *</span><select value={actionPanel.exceptionCode} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, exceptionCode: event.target.value } : current)} required>
            <option value="">Chọn nguyên nhân</option><option value="LATE_ARRIVAL">Đến trễ</option><option value="EARLY_ARRIVAL">Đến sớm</option><option value="VEHICLE_MISMATCH">Sai phương tiện</option><option value="DOCK_UNAVAILABLE">Cửa kho không khả dụng</option><option value="DAMAGED_SEAL">Niêm phong hư hỏng</option><option value="CAPACITY_ISSUE">Vấn đề sức chứa</option><option value="WAITING_TIME_BREACH">Vượt thời gian chờ</option>
          </select></label>
          <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, note: event.target.value } : current)} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Ghi nhận ngoại lệ</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
        </form>}
      </UiCard>}
    </UiPage>
  );
};

export default DockYard;
