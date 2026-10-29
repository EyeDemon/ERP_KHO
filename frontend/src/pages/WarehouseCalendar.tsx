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
type CalendarException = {
  id: number;
  warehouseId: number;
  exceptionType: string;
  windowSummary: string;
  reason: string;
  createdBy?: number | null;
  createdAtUtc: string;
};

const exceptionTypeLabel: Record<string, string> = {
  HOLIDAY_CLOSURE: 'Nghỉ lễ (Đóng kho)',
  MAINTENANCE_WINDOW: 'Bảo trì định kỳ',
  EMERGENCY_OPEN_OVERRIDE: 'Ghi đè mở kho khẩn cấp',
};

const exceptionTypeTone = (type: string): 'danger' | 'warning' | 'success' | 'neutral' =>
  type === 'HOLIDAY_CLOSURE' ? 'danger'
    : type === 'MAINTENANCE_WINDOW' ? 'warning'
      : type === 'EMERGENCY_OPEN_OVERRIDE' ? 'success'
        : 'neutral';

const DEFAULT_TIMEZONE = 'Asia/Ho_Chi_Minh';

const formatTimeZoneDisplay = (tz?: string | null) =>
  !tz || tz === 'Asia/Ho_Chi_Minh'
    ? 'Asia/Ho_Chi_Minh (GMT+7 — Giờ Việt Nam)'
    : tz;

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
  const [timeZoneId, setTimeZoneId] = useState(DEFAULT_TIMEZONE);
  const [days, setDays] = useState<DayForm[]>([]);
  const [shiftForm, setShiftForm] = useState<ShiftForm | null>(null);
  const [exceptions, setExceptions] = useState<CalendarException[]>([]);
  const [exceptionForm, setExceptionForm] = useState({
    exceptionType: 'HOLIDAY_CLOSURE',
    windowDateOrRange: '',
    reason: '',
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const applyCalendar = useCallback((data: CalendarResponse) => {
    const resolvedTimeZone = !data.timeZoneId || data.timeZoneId === 'UTC' ? DEFAULT_TIMEZONE : data.timeZoneId;
    setCalendar({ ...data, timeZoneId: resolvedTimeZone });
    setTimeZoneId(resolvedTimeZone);
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
    if (!targetWarehouseId) { setCalendar(null); setExceptions([]); return; }
    let exError = '';
    const [response, exceptionsResponse] = await Promise.all([
      apiClient.get('/api/warehouses/' + targetWarehouseId + '/calendar'),
      apiClient.get('/api/warehouses/' + targetWarehouseId + '/calendar/exceptions').catch((err: unknown) => {
        const httpErr = err as { response?: { status?: number } };
        if (httpErr?.response?.status && httpErr.response.status >= 400) {
          exError = messageOf(err, 'Không thể tải danh sách ngoại lệ lịch kho.');
        }
        return { data: [] };
      }),
    ]);
    applyCalendar(response.data as CalendarResponse);
    setExceptions(Array.isArray(exceptionsResponse?.data) ? (exceptionsResponse.data as CalendarException[]) : []);
    if (exError) setError(exError);
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

  const saveException = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!warehouseId || saving || !exceptionForm.windowDateOrRange.trim() || !exceptionForm.reason.trim()) return;
    setSaving(true); setError(''); setSuccess('');
    try {
      await apiClient.post('/api/warehouses/' + warehouseId + '/calendar/exceptions', {
        exceptionType: exceptionForm.exceptionType,
        windowDateOrRange: exceptionForm.windowDateOrRange.trim(),
        reason: exceptionForm.reason.trim(),
      });
      setSuccess('Đã ghi nhận ngoại lệ lịch vận hành.');
      setExceptionForm({ exceptionType: 'HOLIDAY_CLOSURE', windowDateOrRange: '', reason: '' });
      await loadCalendar(warehouseId);
    } catch (failure) {
      setError(messageOf(failure, 'Không thể ghi nhận ngoại lệ lịch vận hành.'));
    } finally { setSaving(false); }
  };

  const activeShifts = calendar?.shifts.filter(shift => shift.isActive).length ?? 0;
  const openDays = calendar?.days.filter(day => day.isOpen).length ?? 0;
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [shiftVerification, setShiftVerification] = useState<{ verificationId: string; shiftCoverageStatus: string } | null>(null);
  const [laborOptimization, setLaborOptimization] = useState<{
    optimizationId: string;
    interleavingMode: string;
    ergonomicSafetyStatus: string;
  } | null>(null);

  const verifyShiftCoverage = async () => {
    if (!warehouseId || governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/warehouses/' + warehouseId + '/calendar/verify-shift-coverage', {});
      if (res?.data?.verificationId && res?.data?.shiftCoverageStatus) {
        setShiftVerification({
          verificationId: String(res.data.verificationId),
          shiftCoverageStatus: String(res.data.shiftCoverageStatus),
        });
      } else {
        setShiftVerification(null);
        setError('Phản hồi kiểm định độ phủ ca trực không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setShiftVerification(null);
      setError(messageOf(failure, 'Không thể kiểm định độ phủ ca trực và cửa sổ cắt đơn.'));
    } finally {
      setGovernanceBusy(false);
    }
  };

  const optimizeLaborInterleaving = async () => {
    if (!warehouseId || governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/warehouses/' + warehouseId + '/calendar/optimize-labor-interleaving', {});
      if (res?.data?.optimizationId && res?.data?.interleavingMode) {
        setLaborOptimization({
          optimizationId: String(res.data.optimizationId),
          interleavingMode: String(res.data.interleavingMode),
          ergonomicSafetyStatus: String(res.data.ergonomicSafetyStatus || ''),
        });
      } else {
        setLaborOptimization(null);
        setError('Phản hồi tối ưu đan xen nhiệm vụ không hợp lệ từ máy chủ.');
      }
    } catch (failure) {
      setLaborOptimization(null);
      setError(messageOf(failure, 'Không thể tối ưu đan xen nhiệm vụ và định biên nhân lực.'));
    } finally {
      setGovernanceBusy(false);
    }
  };

  return (
    <UiPage>
      <UiPageHeader eyebrow="Kho & Vị trí" title="Lịch vận hành & Ca kho"
        description="Cấu hình múi giờ, lịch tuần, giờ cắt đơn, năng lực theo ca và ngoại lệ lịch nghỉ lễ / bảo trì / ghi đè khẩn cấp." />
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
        <div className="ui-muted-text ui-auto-actions">Giờ địa phương: {calendar?.localNow ? new Date(calendar.localNow).toLocaleString('vi-VN', { timeZone: timeZoneId || DEFAULT_TIMEZONE }) : '—'} • {formatTimeZoneDisplay(timeZoneId)}</div>
      </UiToolbar>

      {loading ? <p role="status">Đang tải lịch vận hành...</p> : warehouses.length === 0 ? <UiEmptyState title="Không có kho nào trong phạm vi được phép." /> : calendar ? (
        <>
          <UiCard title="Lịch vận hành tuần">
            {!calendar.isConfigured && <p role="status"><UiBadge tone="warning">Chưa cấu hình</UiBadge> Kho chưa có lịch vận hành. Quản lý kho có thể thiết lập lịch chuẩn đầu tiên.</p>}
            <form onSubmit={saveCalendar}>
              <label className="ui-stack">
                <span>Múi giờ kho</span>
                <select
                  aria-label="Warehouse timezone"
                  value={timeZoneId}
                  onChange={event => setTimeZoneId(event.target.value)}
                  disabled={!canManage || saving}
                >
                  <option value="Asia/Ho_Chi_Minh">Asia/Ho_Chi_Minh (GMT+7 — Giờ Việt Nam)</option>
                  <option value="Asia/Bangkok">Asia/Bangkok (GMT+7)</option>
                  <option value="Asia/Singapore">Asia/Singapore (GMT+8)</option>
                  <option value="UTC">UTC</option>
                  {!['Asia/Ho_Chi_Minh', 'Asia/Bangkok', 'Asia/Singapore', 'UTC'].includes(timeZoneId) && (
                    <option value={timeZoneId}>{timeZoneId}</option>
                  )}
                </select>
              </label>
              <div style={{ maxHeight: 180, overflowY: 'auto' }}>
                <UiTableScroll>
                  <table aria-label="Lịch vận hành tuần" className="ui-table-compact">
                    <thead><tr><th>Ngày</th><th>Mở cửa</th><th>Mở</th><th>Đóng</th><th>Hạn cắt đơn nhập</th><th>Hạn cắt đơn xuất</th></tr></thead>
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
              </div>
              {canManage && <div className="ui-inline-actions"><button type="submit" disabled={saving || days.length !== 7}>{saving ? 'Đang lưu...' : 'Lưu lịch tuần'}</button></div>}
            </form>
          </UiCard>

          <UiCard title="Ca làm việc & Năng lực xử lý">
            <div className="ui-inline-actions">{canManage && <button type="button" className="ui-primary-button" onClick={() => openShift()} disabled={!calendar.isConfigured}>Thêm ca</button>}</div>
            <UiTableScroll>
              <table aria-label="Ca làm việc và capacity" className="ui-table-compact" style={{ tableLayout: 'fixed', width: '100%' }}>
                <colgroup>
                  <col style={{ width: canManage ? '17%' : '19%' }} />
                  <col style={{ width: canManage ? '10%' : '12%' }} />
                  <col style={{ width: '7%' }} />
                  <col style={{ width: '8.5%' }} />
                  <col style={{ width: '8%' }} />
                  <col style={{ width: '8%' }} />
                  <col style={{ width: '6.5%' }} />
                  <col style={{ width: '8.5%' }} />
                  <col style={{ width: '7.5%' }} />
                  <col style={{ width: '6%' }} />
                  <col style={{ width: '7%' }} />
                  {canManage && <col style={{ width: '6%' }} />}
                </colgroup>
                <thead><tr><th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Ca</th><th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Khung giờ</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Nhân sự</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Pallet nhập/giờ</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Đơn xuất/giờ</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Cửa nhận/xuất</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Giờ công</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Sức chứa tập kết</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Trạm đóng gói</th><th style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'normal', lineHeight: 1.25 }}>Thiết bị</th><th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Trạng thái</th>{canManage && <th style={{ whiteSpace: 'normal', lineHeight: 1.25 }}>Thao tác</th>}</tr></thead>
                <tbody>
                  {calendar.shifts.map(shift => <tr key={shift.id}>
                    <td><strong style={{ whiteSpace: 'nowrap' }}>{shift.code}</strong><br /><small>{shift.name}</small></td>
                    <td>{shift.startTimeLocal}–{shift.endTimeLocal}{shift.overnight ? ' • +1 ngày' : ''}<br /><small>Nghỉ {shift.breakMinutes} phút</small></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.plannedHeadcount}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.inboundPalletsPerHour ?? '—'}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.outboundOrdersPerHour ?? '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.dockSlots ?? '—'}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.laborHours ?? '—'}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.stagingCapacity ?? '—'}</td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.packingStations ?? '—'}</td><td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{shift.equipmentAvailable ?? '—'}</td>
                    <td><UiBadge tone={shift.isActive ? 'success' : 'neutral'}>{shift.isActive ? 'Hoạt động' : 'Ngừng'}</UiBadge></td>
                    {canManage && <td style={{ whiteSpace: 'nowrap' }}><button type="button" onClick={() => openShift(shift)} style={{ whiteSpace: 'nowrap' }}>Sửa</button></td>}
                  </tr>)}
                  {calendar.shifts.length === 0 && <tr><td className="ui-empty-cell" colSpan={canManage ? 12 : 11}>Chưa có ca làm việc.</td></tr>}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>

          {shiftForm && canManage && <UiCard title={shiftForm.id ? 'Sửa ca ' + shiftForm.code : 'Thêm ca làm việc'}>
            <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }} onSubmit={saveShift}>
              <label className="ui-stack"><span>Mã ca *</span><input aria-label="Mã ca" placeholder="VD: SHIFT-1" value={shiftForm.code} disabled={saving || Boolean(shiftForm.id)} onChange={event => setShiftForm(current => current && ({ ...current, code: event.target.value.toUpperCase() }))} required /></label>
              <label className="ui-stack"><span>Tên ca *</span><input aria-label="Tên ca" placeholder="VD: Ca sáng" value={shiftForm.name} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, name: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Bắt đầu *</span><input aria-label="Giờ bắt đầu ca" type="time" value={shiftForm.startTimeLocal} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, startTimeLocal: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Kết thúc *</span><input aria-label="Giờ kết thúc ca" type="time" value={shiftForm.endTimeLocal} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, endTimeLocal: event.target.value }))} required /></label>
              <label className="ui-stack"><span>Nghỉ (phút)</span><input aria-label="Phút nghỉ ca" type="number" min="0" value={shiftForm.breakMinutes} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, breakMinutes: event.target.value }))} /></label>
              <label className="ui-stack"><span>Định biên nhân sự</span><input aria-label="Planned headcount" type="number" min="0" value={shiftForm.plannedHeadcount} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, plannedHeadcount: event.target.value }))} /></label>
              <label className="ui-stack"><span>Pallet nhập / giờ</span><input aria-label="Inbound pallets per hour" type="number" min="0" step="0.01" value={shiftForm.inboundPalletsPerHour} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, inboundPalletsPerHour: event.target.value }))} /></label>
              <label className="ui-stack"><span>Đơn xuất / giờ</span><input aria-label="Outbound orders per hour" type="number" min="0" step="0.01" value={shiftForm.outboundOrdersPerHour} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, outboundOrdersPerHour: event.target.value }))} /></label>
              <label className="ui-stack"><span>Số cửa nhận/xuất</span><input aria-label="Dock slots" type="number" min="0" value={shiftForm.dockSlots} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, dockSlots: event.target.value }))} /></label>
              <label className="ui-stack"><span>Tổng giờ công</span><input aria-label="Labor hours" type="number" min="0" step="0.01" value={shiftForm.laborHours} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, laborHours: event.target.value }))} /></label>
              <label className="ui-stack"><span>Sức chứa khu tập kết</span><input aria-label="Staging capacity" type="number" min="0" step="0.01" value={shiftForm.stagingCapacity} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, stagingCapacity: event.target.value }))} /></label>
              <label className="ui-stack"><span>Số trạm đóng gói</span><input aria-label="Packing stations" type="number" min="0" value={shiftForm.packingStations} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, packingStations: event.target.value }))} /></label>
              <label className="ui-stack"><span>Thiết bị khả dụng</span><input aria-label="Equipment available" type="number" min="0" value={shiftForm.equipmentAvailable} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, equipmentAvailable: event.target.value }))} /></label>
              {shiftForm.id && <label className="ui-checkbox-label"><input type="checkbox" checked={shiftForm.isActive} disabled={saving} onChange={event => setShiftForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Ca đang hoạt động</label>}
              <div className="ui-inline-actions"><button type="submit" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu ca'}</button><button type="button" disabled={saving} onClick={() => setShiftForm(null)}>Hủy</button></div>
            </form>
          </UiCard>}

          <UiCard title="Lịch nghỉ lễ, Bảo trì & Ghi đè khẩn cấp">
            <div className="ui-inline-wrap">
              <button type="button" disabled={saving || governanceBusy} onClick={() => void verifyShiftCoverage()}>
                Kiểm định Độ phủ Ca trực & Cửa sổ Cắt đơn
              </button>
              {shiftVerification && (
                <UiBadge tone="success">
                  {`Đã kiểm định ${shiftVerification.verificationId} • ${({ '24_7_CUTOFF_ALIGNED': 'Đã đồng bộ độ phủ ca trực & cửa sổ cắt đơn', SHIFT_COVERAGE_VALIDATED: 'Độ phủ ca trực đạt chuẩn' } as Record<string, string>)[shiftVerification.shiftCoverageStatus] || shiftVerification.shiftCoverageStatus}`}
                </UiBadge>
              )}
              <button type="button" disabled={saving || governanceBusy} onClick={() => void optimizeLaborInterleaving()}>
                Tối ưu Đan xen Nhiệm vụ, Định biên Nhân lực & An toàn Công thái học
              </button>
              {laborOptimization && (
                <UiBadge tone="success">
                  {`Đã tối ưu ${laborOptimization.optimizationId} • ${({ DUAL_COMMAND_INTERLEAVED: 'Đan xen chu kỳ kép Cất/Lấy hàng' } as Record<string, string>)[laborOptimization.interleavingMode] || laborOptimization.interleavingMode} • ${({ OSHA_ERGONOMIC_COMPLIANT: 'Đạt chuẩn an toàn công thái học', ERGONOMIC_SAFE: 'An toàn công thái học' } as Record<string, string>)[laborOptimization.ergonomicSafetyStatus] || laborOptimization.ergonomicSafetyStatus}`}
                </UiBadge>
              )}
            </div>
            {canManage && (
              <form className="ui-form-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 10, alignItems: 'end', maxWidth: '100%' }} onSubmit={saveException}>
                <label className="ui-stack">
                  <span>Loại ngoại lệ *</span>
                  <select
                    aria-label="Loại ngoại lệ lịch vận hành"
                    value={exceptionForm.exceptionType}
                    disabled={saving}
                    onChange={event => setExceptionForm(current => ({ ...current, exceptionType: event.target.value }))}
                  >
                    <option value="HOLIDAY_CLOSURE">Nghỉ lễ (Đóng kho)</option>
                    <option value="MAINTENANCE_WINDOW">Bảo trì định kỳ</option>
                    <option value="EMERGENCY_OPEN_OVERRIDE">Ghi đè mở kho khẩn cấp</option>
                  </select>
                </label>
                <label className="ui-stack">
                  <span>Ngày / Khung giờ áp dụng *</span>
                  <input
                    aria-label="Khung giờ ngoại lệ lịch kho"
                    value={exceptionForm.windowDateOrRange}
                    disabled={saving}
                    placeholder="VD: 30/04/2026 - 01/05/2026"
                    onChange={event => setExceptionForm(current => ({ ...current, windowDateOrRange: event.target.value }))}
                    required
                  />
                </label>
                <label className="ui-stack">
                  <span>Lý do điều hành *</span>
                  <input
                    aria-label="Lý do ngoại lệ lịch kho"
                    value={exceptionForm.reason}
                    disabled={saving}
                    placeholder="VD: Nghỉ lễ hoặc bảo trì định kỳ"
                    onChange={event => setExceptionForm(current => ({ ...current, reason: event.target.value }))}
                    required
                  />
                </label>
                <div className="ui-inline-actions">
                  <button type="submit" disabled={saving || !exceptionForm.windowDateOrRange.trim() || !exceptionForm.reason.trim()}>
                    {saving ? 'Đang lưu...' : 'Ghi nhận ngoại lệ lịch vận hành'}
                  </button>
                </div>
              </form>
            )}
            <UiTableScroll>
              <table aria-label="Danh sách ngoại lệ lịch vận hành" className="ui-table-compact">
                <thead>
                  <tr>
                    <th>Loại ngoại lệ</th>
                    <th>Khung thời gian áp dụng</th>
                    <th>Lý do điều hành</th>
                    <th>Thời điểm ghi nhận</th>
                  </tr>
                </thead>
                <tbody>
                  {exceptions.map(item => (
                    <tr key={item.id}>
                      <td>
                        <UiBadge tone={exceptionTypeTone(item.exceptionType)}>
                          {exceptionTypeLabel[item.exceptionType] ?? item.exceptionType}
                        </UiBadge>
                      </td>
                      <td><strong>{item.windowSummary}</strong></td>
                      <td>{item.reason}</td>
                      <td>{new Date(item.createdAtUtc).toLocaleString('vi-VN')}</td>
                    </tr>
                  ))}
                  {exceptions.length === 0 && (
                    <tr>
                      <td className="ui-empty-cell" colSpan={4}>
                        Chưa có ngoại lệ nghỉ lễ, bảo trì hoặc ghi đè khẩn cấp nào được ghi nhận cho kho này.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </UiTableScroll>
          </UiCard>
        </>
      ) : null}
    </UiPage>
  );
};

export default WarehouseCalendar;
