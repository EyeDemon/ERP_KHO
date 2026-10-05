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
  3: 'Đã check-in',
  4: 'Đã gán dock',
  5: 'Đang phục vụ',
  6: 'Hoàn thành',
  7: 'Đã hủy',
  8: 'No-show',
  9: 'Exception',
};
const statusTone = (status: number) =>
  status === 6 ? 'success' as const
    : status === 7 || status === 8 || status === 9 ? 'danger' as const
      : status === 0 ? 'neutral' as const
        : status === 5 ? 'warning' as const
          : 'success' as const;
const directionLabel = (value: number) => value === 0 ? 'Inbound' : 'Outbound';
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
      if (revision === loadRevisionRef.current) setError(messageOf(failure, 'Không thể tải Dock & Yard.'));
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
      setSuccess('Đã cập nhật trạng thái appointment.');
      setActionPanel(null);
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể cập nhật appointment.'));
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
      setError('Giờ appointment không hợp lệ trong timezone của kho. Hãy kiểm tra lại ngày/giờ.');
      return;
    }
    savingRef.current = true;
    setSaving(true); setError(''); setSuccess('');
    try {
      if (appointmentForm.id) {
        await apiClient.put('/api/dock-yard/appointments/' + appointmentForm.id, payload);
        setSuccess('Đã cập nhật appointment Nháp.');
      } else {
        const logicalAction = 'dock-appointment:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/dock-yard/appointments', payload, { headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
        setSuccess('Đã tạo appointment Nháp.');
      }
      setAppointmentForm(null);
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu appointment.'));
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
      setSuccess('Đã lưu cấu hình dock.');
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu dock.'));
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
      setSuccess('Đã lưu yard slot.');
      await refresh();
    } catch (failure) {
      setError(messageOf(failure, 'Không thể lưu yard slot.'));
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
      setError(messageOf(failure, 'Không thể tải chi tiết appointment.'));
    }
  };

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Vận hành"
        title="Dock & Yard Control"
        description="Điều phối lịch xe, gate check-in, yard slot và dock assignment. Các trạng thái tại yard không tạo inventory effect; tồn kho chỉ thay đổi ở nghiệp vụ receiving/dispatch riêng."
        actions={canManageAppointment ? <button type="button" className="ui-primary-button" onClick={() => openAppointment()}>Tạo appointment</button> : undefined}
      />

      {success && <p role="status" className="ui-success-text">{success}</p>}
      {error && <p role="alert">{error} <button type="button" onClick={() => void refresh()}>Tải lại</button></p>}

      <UiMetricGrid>
        <UiMetric value={appointments.length} label="Appointment trong phạm vi" />
        <UiMetric value={waitingCount} label="Đang chờ / gate" />
        <UiMetric value={inServiceCount} label="Đã gán dock / phục vụ" />
        <UiMetric value={exceptionCount} label="No-show / Exception" />
        <UiMetric value={activeDockCount} label="Dock hoạt động" />
        <UiMetric value={occupiedYardCount} label="Yard slot đang dùng" />
      </UiMetricGrid>

      <UiToolbar>
        <UiToolbarField label="Kho">
          <select aria-label="Kho Dock Yard" value={warehouseId} onChange={event => setWarehouseId(Number(event.target.value))}>
            {warehouses.map(warehouse => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}{warehouse.calendarConfigured ? '' : ' • chưa có lịch'}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Trạng thái">
          <select aria-label="Lọc trạng thái appointment" value={status} onChange={event => setStatus(event.target.value)}>
            <option value="">Tất cả</option>
            {Object.entries(statusLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </UiToolbarField>
        <UiToolbarField label="Tìm kiếm">
          <input aria-label="Tìm appointment" value={search} onChange={event => setSearch(event.target.value)} placeholder="Mã, carrier, xe, tài xế, dock..." />
        </UiToolbarField>
        <button type="button" onClick={() => void refresh()} disabled={!warehouseId || loading}>Làm mới</button>
      </UiToolbar>

      {loading ? <p role="status">Đang tải Dock & Yard...</p> : warehouses.length === 0 ? (
        <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
      ) : (
        <>
          <UiCard title="Appointment Operations">
            <UiTableScroll>
              <table aria-label="Danh sách appointment Dock Yard">
                <thead><tr><th>Appointment</th><th>Khung giờ</th><th>Carrier / Vehicle</th><th>Yard</th><th>Dock</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
                <tbody>
                  {filtered.map(item => (
                    <tr key={item.id}>
                      <td><strong>{item.code}</strong><br /><small>{directionLabel(item.direction)}</small></td>
                      <td>{displayWarehouseTime(item.plannedStartUtc, warehouseTimeZone)}<br /><small>→ {displayWarehouseTime(item.plannedEndUtc, warehouseTimeZone)} • {warehouseTimeZone}</small></td>
                      <td>{item.carrierName || item.carrierCode || '—'}<br /><small>{item.vehiclePlate || 'Chưa có biển số'}</small></td>
                      <td>{item.yardSlotCode || '—'}</td>
                      <td>{item.dockCode || '—'}</td>
                      <td><UiBadge tone={statusTone(item.status)}>{statusLabel[item.status] || item.status}</UiBadge>{item.checkedOutAtUtc && <><br /><small>Đã checkout</small></>}</td>
                      <td>
                        <div className="ui-inline-actions">
                          <button type="button" aria-label={'Chi tiết appointment ' + item.code} onClick={() => void showDetail(item)}>Chi tiết</button>
                          {canManageAppointment && item.status === 0 && <button type="button" aria-label={'Sửa appointment ' + item.code} onClick={() => openAppointment(item)}>Sửa</button>}
                          {canManageAppointment && item.status === 0 && <button type="button" aria-label={'Xác nhận appointment ' + item.code} onClick={() => void runCommand(item, 'confirm')}>Xác nhận</button>}
                          {canCheckIn && item.status === 1 && <button type="button" aria-label={'Ghi nhận xe đến ' + item.code} onClick={() => void runCommand(item, 'arrive')}>Xe đến</button>}
                          {canCheckIn && item.status === 2 && <button type="button" aria-label={'Check-in appointment ' + item.code} onClick={() => setActionPanel({ mode: 'checkin', appointment: item, vehiclePlate: item.vehiclePlate ?? '', trailerPlate: item.trailerPlate ?? '', driverName: '', driverPhone: '', sealNumber: '', yardSlotId: '', note: '' })}>Check-in</button>}
                          {canAssignDock && [3, 4].includes(item.status) && <button type="button" aria-label={(item.status === 4 ? 'Đổi dock appointment ' : 'Gán dock appointment ') + item.code} onClick={() => setActionPanel({ mode: 'assignDock', appointment: item, dockId: item.dockId?.toString() ?? '', note: '' })}>{item.status === 4 ? 'Đổi dock' : 'Gán dock'}</button>}
                          {canManageAppointment && item.status === 4 && <button type="button" aria-label={'Bắt đầu dịch vụ appointment ' + item.code} onClick={() => void runCommand(item, 'start-service')}>Bắt đầu</button>}
                          {canManageAppointment && item.status === 5 && <button type="button" aria-label={'Hoàn thành dịch vụ appointment ' + item.code} onClick={() => void runCommand(item, 'complete')}>Hoàn thành</button>}
                          {canCheckout && (item.status === 6 || (item.status === 9 && Boolean(item.checkedInAtUtc))) && !item.checkedOutAtUtc && <button type="button" aria-label={'Checkout appointment ' + item.code} onClick={() => void runCommand(item, 'checkout')}>Checkout</button>}
                          {canManageAppointment && [0, 1].includes(item.status) && <button type="button" aria-label={'Hủy appointment ' + item.code} onClick={() => window.confirm('Hủy appointment này?') && void runCommand(item, 'cancel')}>Hủy</button>}
                          {canManageAppointment && [1, 2, 3].includes(item.status) && <button type="button" aria-label={'Đánh dấu no-show appointment ' + item.code} onClick={() => void runCommand(item, 'no-show')}>No-show</button>}
                          {canManageAppointment && ![6, 7, 8, 9].includes(item.status) && <button type="button" aria-label={'Ghi nhận exception appointment ' + item.code} onClick={() => setActionPanel({ mode: 'exception', appointment: item, exceptionCode: '', note: '' })}>Exception</button>}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {filtered.length === 0 && <tr><td className="ui-empty-cell" colSpan={7}>Không có appointment phù hợp.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          <UiCard title="Dock Master">
            {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setDockForm(emptyDock())}>Thêm dock</button></div>}
            <UiTableScroll>
              <table aria-label="Danh sách dock">
                <thead><tr><th>Mã</th><th>Tên</th><th>Luồng</th><th>Vehicle</th><th>Nhiệt độ</th><th>Hazard</th><th>Trạng thái</th>{canManageDock && <th>Thao tác</th>}</tr></thead>
                <tbody>{docks.map(dock => <tr key={dock.id}>
                  <td><strong>{dock.code}</strong></td><td>{dock.name}</td>
                  <td>{[dock.supportsInbound ? 'Inbound' : '', dock.supportsOutbound ? 'Outbound' : ''].filter(Boolean).join(' + ')}</td>
                  <td>{dock.allowedVehicleType || 'Mọi loại'}</td><td>{dock.isTemperatureControlled ? 'Có' : 'Không'}</td><td>{dock.hazardAllowed ? 'Cho phép' : 'Không'}</td>
                  <td><UiBadge tone={dock.isActive ? 'success' : 'neutral'}>{dock.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                  {canManageDock && <td><button type="button" aria-label={'Sửa dock ' + dock.code} onClick={() => setDockForm({ id: dock.id, code: dock.code, name: dock.name, supportsInbound: dock.supportsInbound, supportsOutbound: dock.supportsOutbound, allowedVehicleType: dock.allowedVehicleType ?? '', isTemperatureControlled: dock.isTemperatureControlled, hazardAllowed: dock.hazardAllowed, isActive: dock.isActive, rowVersion: dock.rowVersion })}>Sửa</button></td>}
                </tr>)}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          <UiCard title="Yard Slots">
            {canManageDock && <div className="ui-inline-actions"><button type="button" onClick={() => setYardForm(emptyYard())}>Thêm yard slot</button></div>}
            <UiTableScroll>
              <table aria-label="Danh sách yard slot">
                <thead><tr><th>Mã</th><th>Tên</th><th>Trạng thái</th><th>Occupancy</th>{canManageDock && <th>Thao tác</th>}</tr></thead>
                <tbody>{yardSlots.map(slot => <tr key={slot.id}>
                  <td><strong>{slot.code}</strong></td><td>{slot.name}</td><td><UiBadge tone={slot.isActive ? 'success' : 'neutral'}>{slot.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                  <td>{slot.occupied ? <UiBadge tone="warning">{slot.occupiedByAppointmentCode || 'Đang dùng'}</UiBadge> : <UiBadge tone="success">Trống</UiBadge>}</td>
                  {canManageDock && <td><button type="button" aria-label={'Sửa yard slot ' + slot.code} onClick={() => setYardForm({ id: slot.id, code: slot.code, name: slot.name, isActive: slot.isActive, rowVersion: slot.rowVersion })}>Sửa</button></td>}
                </tr>)}</tbody>
              </table>
            </UiTableScroll>
          </UiCard>
        </>
      )}

      {selected && <UiCard title={'Timeline • ' + selected.code}>
        <p><strong>{statusLabel[selected.status]}</strong> • {directionLabel(selected.direction)} • {selected.vehiclePlate || 'Chưa có xe'} • {selected.dockCode || 'Chưa gán dock'}</p>
        <UiTableScroll>
          <table aria-label="Timeline appointment">
            <thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Dock</th><th>Yard</th><th>Ghi chú</th></tr></thead>
            <tbody>{(selected.events ?? []).map(event => <tr key={event.id}>
              <td>{new Date(event.eventAtUtc).toLocaleString('vi-VN')}</td><td>{event.eventType}</td><td>{event.dockId ?? '—'}</td><td>{event.yardSlotId ?? '—'}</td><td>{event.note || '—'}</td>
            </tr>)}</tbody>
          </table>
        </UiTableScroll>
        <div className="ui-inline-actions"><button type="button" onClick={() => setSelected(null)}>Đóng</button></div>
      </UiCard>}

      {appointmentForm && canManageAppointment && <UiCard title={appointmentForm.id ? 'Sửa appointment ' + appointmentForm.code : 'Tạo appointment'}>
        <form className="ui-form-grid" onSubmit={saveAppointment}>
          <label className="ui-stack"><span>Mã *</span><input aria-label="Mã appointment" value={appointmentForm.code} disabled={saving || Boolean(appointmentForm.id)} onChange={event => setAppointmentForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Luồng *</span><select aria-label="Luồng appointment" value={appointmentForm.direction} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, direction: event.target.value as '0' | '1' }))}><option value="0">Inbound</option><option value="1">Outbound</option></select></label>
          <label className="ui-stack"><span>Bắt đầu * • {warehouseTimeZone}</span><input aria-label="Bắt đầu appointment theo timezone kho" type="datetime-local" value={appointmentForm.plannedStart} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, plannedStart: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Kết thúc * • {warehouseTimeZone}</span><input aria-label="Kết thúc appointment theo timezone kho" type="datetime-local" value={appointmentForm.plannedEnd} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, plannedEnd: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Carrier code</span><input aria-label="Carrier code" value={appointmentForm.carrierCode} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, carrierCode: event.target.value }))} /></label>
          <label className="ui-stack"><span>Carrier name</span><input aria-label="Carrier name" value={appointmentForm.carrierName} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, carrierName: event.target.value }))} /></label>
          <label className="ui-stack"><span>Biển số xe dự kiến</span><input aria-label="Biển số xe dự kiến" value={appointmentForm.vehiclePlate} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, vehiclePlate: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-stack"><span>Trailer</span><input aria-label="Trailer dự kiến" value={appointmentForm.trailerPlate} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, trailerPlate: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-stack"><span>Vehicle type</span><input aria-label="Vehicle type" value={appointmentForm.vehicleType} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, vehicleType: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.requiresTemperatureControl} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, requiresTemperatureControl: event.target.checked }))} /> Yêu cầu kiểm soát nhiệt độ</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={appointmentForm.hazardous} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, hazardous: event.target.checked }))} /> Hàng nguy hiểm</label>
          <label className="ui-stack"><span>Ghi chú</span><textarea aria-label="Ghi chú appointment" value={appointmentForm.note} disabled={saving} onChange={event => setAppointmentForm(current => current && ({ ...current, note: event.target.value }))} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu appointment'}</button><button type="button" disabled={saving} onClick={() => setAppointmentForm(null)}>Hủy</button></div>
        </form>
      </UiCard>}

      {dockForm && canManageDock && <UiCard title={dockForm.id ? 'Sửa dock ' + dockForm.code : 'Thêm dock'}>
        <form className="ui-form-grid" onSubmit={saveDock}>
          <label className="ui-stack"><span>Mã *</span><input value={dockForm.code} disabled={saving || Boolean(dockForm.id)} onChange={event => setDockForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Tên *</span><input value={dockForm.name} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
          <label className="ui-stack"><span>Vehicle type</span><input value={dockForm.allowedVehicleType} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, allowedVehicleType: event.target.value.toUpperCase() }))} /></label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsInbound} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, supportsInbound: event.target.checked }))} /> Inbound</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.supportsOutbound} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, supportsOutbound: event.target.checked }))} /> Outbound</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isTemperatureControlled} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, isTemperatureControlled: event.target.checked }))} /> Temperature controlled</label>
          <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.hazardAllowed} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, hazardAllowed: event.target.checked }))} /> Cho phép hazard</label>
          {dockForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={dockForm.isActive} disabled={saving} onChange={event => setDockForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Đang hoạt động</label>}
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu dock</button><button type="button" onClick={() => setDockForm(null)} disabled={saving}>Hủy</button></div>
        </form>
      </UiCard>}

      {yardForm && canManageDock && <UiCard title={yardForm.id ? 'Sửa yard slot ' + yardForm.code : 'Thêm yard slot'}>
        <form className="ui-form-grid" onSubmit={saveYard}>
          <label className="ui-stack"><span>Mã *</span><input value={yardForm.code} disabled={saving || Boolean(yardForm.id)} onChange={event => setYardForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
          <label className="ui-stack"><span>Tên *</span><input value={yardForm.name} disabled={saving} onChange={event => setYardForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
          {yardForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={yardForm.isActive} disabled={saving} onChange={event => setYardForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Đang hoạt động</label>}
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Lưu yard slot</button><button type="button" onClick={() => setYardForm(null)} disabled={saving}>Hủy</button></div>
        </form>
      </UiCard>}

      {actionPanel && <UiCard title={actionPanel.mode === 'checkin' ? 'Gate check-in • ' + actionPanel.appointment.code : actionPanel.mode === 'assignDock' ? 'Gán dock • ' + actionPanel.appointment.code : 'Ghi nhận exception • ' + actionPanel.appointment.code}>
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
          <label className="ui-stack"><span>Trailer</span><input value={actionPanel.trailerPlate} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, trailerPlate: event.target.value.toUpperCase() } : current)} /></label>
          <label className="ui-stack"><span>Tài xế *</span><input value={actionPanel.driverName} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, driverName: event.target.value } : current)} required /></label>
          <label className="ui-stack"><span>Điện thoại</span><input value={actionPanel.driverPhone} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, driverPhone: event.target.value } : current)} /></label>
          <label className="ui-stack"><span>Seal</span><input value={actionPanel.sealNumber} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, sealNumber: event.target.value.toUpperCase() } : current)} /></label>
          <label className="ui-stack"><span>Yard slot</span><select value={actionPanel.yardSlotId} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, yardSlotId: event.target.value } : current)}><option value="">Chưa gán</option>{yardSlots.filter(slot => slot.isActive && !slot.occupied).map(slot => <option key={slot.id} value={slot.id}>{slot.code} — {slot.name}</option>)}</select></label>
          <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'checkin' ? { ...current, note: event.target.value } : current)} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Check-in</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
        </form>}
        {actionPanel.mode === 'assignDock' && <form className="ui-form-grid" onSubmit={event => {
          event.preventDefault();
          void runCommand(actionPanel.appointment, 'assign-dock', { dockId: Number(actionPanel.dockId), note: actionPanel.note || null });
        }}>
          <label className="ui-stack"><span>Dock *</span><select value={actionPanel.dockId} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, dockId: event.target.value } : current)} required><option value="">Chọn dock</option>{docks.filter(dock => dock.isActive).map(dock => <option key={dock.id} value={dock.id}>{dock.code} — {dock.name}</option>)}</select></label>
          <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'assignDock' ? { ...current, note: event.target.value } : current)} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving || !actionPanel.dockId}>Gán dock</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
        </form>}
        {actionPanel.mode === 'exception' && <form className="ui-form-grid" onSubmit={event => {
          event.preventDefault();
          void runCommand(actionPanel.appointment, 'exception', { exceptionCode: actionPanel.exceptionCode, note: actionPanel.note || null });
        }}>
          <label className="ui-stack"><span>Mã exception *</span><select value={actionPanel.exceptionCode} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, exceptionCode: event.target.value } : current)} required>
            <option value="">Chọn nguyên nhân</option><option value="LATE_ARRIVAL">Late arrival</option><option value="EARLY_ARRIVAL">Early arrival</option><option value="VEHICLE_MISMATCH">Vehicle mismatch</option><option value="DOCK_UNAVAILABLE">Dock unavailable</option><option value="DAMAGED_SEAL">Damaged seal</option><option value="CAPACITY_ISSUE">Capacity issue</option><option value="WAITING_TIME_BREACH">Waiting time breach</option>
          </select></label>
          <label className="ui-stack"><span>Ghi chú</span><textarea value={actionPanel.note} onChange={event => setActionPanel(current => current?.mode === 'exception' ? { ...current, note: event.target.value } : current)} /></label>
          <div className="ui-inline-actions"><button type="submit" disabled={saving}>Ghi nhận exception</button><button type="button" onClick={() => setActionPanel(null)} disabled={saving}>Hủy</button></div>
        </form>}
      </UiCard>}
    </UiPage>
  );
};

export default DockYard;
