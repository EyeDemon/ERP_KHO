import { useCallback, useEffect, useState } from 'react';
import apiClient from '../services/apiClient';
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
import {
  decimalOrNull,
  DEFAULT_TIMEZONE,
  emptyShift,
  formatTimeZoneDisplay,
  integerOrNull,
  messageOf,
  type CalendarException,
  type CalendarResponse,
  type DayForm,
  type Shift,
  type ShiftForm,
  type Warehouse,
} from '../modules/inventory/warehouseCalendarShared';
import {
  WarehouseExceptionsSection,
  WarehouseShiftsSection,
} from '../modules/inventory/WarehouseCalendarSections';

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
  const [governanceBusy, setGovernanceBusy] = useState(false);
  const [shiftVerification, setShiftVerification] = useState<{ verificationId: string; shiftCoverageStatus: string } | null>(null);
  const [laborOptimization, setLaborOptimization] = useState<{
    optimizationId: string;
    interleavingMode: string;
    ergonomicSafetyStatus: string;
  } | null>(null);

  const applyCalendar = useCallback((data: CalendarResponse) => {
    const resolvedTimeZone = !data.timeZoneId || data.timeZoneId === 'UTC' ? DEFAULT_TIMEZONE : data.timeZoneId;
    setCalendar({ ...data, timeZoneId: resolvedTimeZone });
    setTimeZoneId(resolvedTimeZone);
    setDays(data.days.map((day) => ({
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
      apiClient.get('/api/v1/warehouses/' + targetWarehouseId + '/calendar'),
      apiClient.get('/api/v1/warehouses/' + targetWarehouseId + '/calendar/exceptions').catch((err: unknown) => {
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
    void apiClient.get('/api/v1/warehouses').then((response) => {
      if (cancelled) return;
      const rows = response.data as Warehouse[];
      setWarehouses(rows);
      setWarehouseId(rows[0]?.id ?? 0);
    }).catch((failure) => {
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
    void loadCalendar(warehouseId).catch((failure) => setError(messageOf(failure, 'Không thể tải lịch vận hành kho.')));
  }, [warehouseId, loading, loadCalendar]);

  const saveCalendar = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!calendar || saving) return;
    setSaving(true); setError(''); setSuccess('');
    try {
      await apiClient.put('/api/v1/warehouses/' + warehouseId + '/calendar', { timeZoneId: timeZoneId.trim(), rowVersion: calendar.rowVersion, days });
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
        await apiClient.put('/api/v1/warehouses/' + warehouseId + '/calendar/shifts/' + shiftForm.id, payload);
        setSuccess('Đã cập nhật ca làm việc.');
      } else {
        await apiClient.post('/api/v1/warehouses/' + warehouseId + '/calendar/shifts', payload);
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
      await apiClient.post('/api/v1/warehouses/' + warehouseId + '/calendar/exceptions', {
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

  const verifyShiftCoverage = async () => {
    if (!warehouseId || governanceBusy) return;
    setGovernanceBusy(true);
    setError('');
    try {
      const res = await apiClient.post('/api/v1/warehouses/' + warehouseId + '/calendar/verify-shift-coverage', {});
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
      const res = await apiClient.post('/api/v1/warehouses/' + warehouseId + '/calendar/optimize-labor-interleaving', {});
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

  const activeShifts = calendar?.shifts.filter((shift) => shift.isActive).length ?? 0;
  const openDays = calendar?.days.filter((day) => day.isOpen).length ?? 0;

  return (
    <UiPage>
      <UiPageHeader
        eyebrow="Kho & Vị trí"
        title="Lịch vận hành & Ca kho"
        description="Cấu hình múi giờ, lịch tuần, giờ cắt đơn, năng lực theo ca và ngoại lệ lịch nghỉ lễ / bảo trì / ghi đè khẩn cấp."
      />
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
          <select aria-label="Kho lịch vận hành" value={warehouseId} onChange={(event) => setWarehouseId(Number(event.target.value))}>
            {warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} — {warehouse.name}</option>)}
          </select>
        </UiToolbarField>
        <div className="ui-muted-text ui-auto-actions">
          Giờ địa phương: {calendar?.localNow ? new Date(calendar.localNow).toLocaleString('vi-VN', { timeZone: timeZoneId || DEFAULT_TIMEZONE }) : '—'} • {formatTimeZoneDisplay(timeZoneId)}
        </div>
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
                  onChange={(event) => setTimeZoneId(event.target.value)}
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
              <div tabIndex={0} role="region" aria-label="Vùng cuộn lịch vận hành tuần" style={{ maxHeight: 180, overflowY: 'auto' }}>
                <UiTableScroll>
                  <table aria-label="Lịch vận hành tuần" className="ui-table-compact">
                    <thead><tr><th>Ngày</th><th>Mở cửa</th><th>Mở</th><th>Đóng</th><th>Hạn cắt đơn nhập</th><th>Hạn cắt đơn xuất</th></tr></thead>
                    <tbody>
                      {calendar.days.map((day, index) => {
                        const form = days[index];
                        return (
                          <tr key={day.dayOfWeek}>
                            <td><strong>{day.dayName}</strong>{day.overnight && <><br /><small>Qua nửa đêm</small></>}</td>
                            <td>
                              <input
                                aria-label={'Mở cửa ' + day.dayName}
                                type="checkbox"
                                checked={form?.isOpen ?? false}
                                disabled={!canManage || saving}
                                onChange={(event) => setDays((current) => current.map((item, i) => i === index ? { ...item, isOpen: event.target.checked, opensAtLocal: event.target.checked ? (item.opensAtLocal || '06:00') : '', closesAtLocal: event.target.checked ? (item.closesAtLocal || '22:00') : '', inboundCutoffLocal: event.target.checked ? item.inboundCutoffLocal : '', outboundCutoffLocal: event.target.checked ? item.outboundCutoffLocal : '' } : item))}
                              />
                            </td>
                            <td><input aria-label={'Giờ mở ' + day.dayName} type="time" value={form?.opensAtLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={(event) => setDays((current) => current.map((item, i) => i === index ? { ...item, opensAtLocal: event.target.value } : item))} /></td>
                            <td><input aria-label={'Giờ đóng ' + day.dayName} type="time" value={form?.closesAtLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={(event) => setDays((current) => current.map((item, i) => i === index ? { ...item, closesAtLocal: event.target.value } : item))} /></td>
                            <td><input aria-label={'Inbound cutoff ' + day.dayName} type="time" value={form?.inboundCutoffLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={(event) => setDays((current) => current.map((item, i) => i === index ? { ...item, inboundCutoffLocal: event.target.value } : item))} /></td>
                            <td><input aria-label={'Outbound cutoff ' + day.dayName} type="time" value={form?.outboundCutoffLocal ?? ''} disabled={!canManage || saving || !form?.isOpen} onChange={(event) => setDays((current) => current.map((item, i) => i === index ? { ...item, outboundCutoffLocal: event.target.value } : item))} /></td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </UiTableScroll>
              </div>
              {canManage && <div className="ui-inline-actions"><button type="submit" disabled={saving || days.length !== 7}>{saving ? 'Đang lưu...' : 'Lưu lịch tuần'}</button></div>}
            </form>
          </UiCard>

          <WarehouseShiftsSection
            calendar={calendar}
            canManage={canManage}
            saving={saving}
            shiftForm={shiftForm}
            setShiftForm={setShiftForm}
            onOpenShift={openShift}
            onSaveShift={(e) => void saveShift(e)}
          />

          <WarehouseExceptionsSection
            canManage={canManage}
            saving={saving}
            governanceBusy={governanceBusy}
            shiftVerification={shiftVerification}
            laborOptimization={laborOptimization}
            exceptionForm={exceptionForm}
            setExceptionForm={setExceptionForm}
            exceptions={exceptions}
            onVerifyShiftCoverage={() => void verifyShiftCoverage()}
            onOptimizeLaborInterleaving={() => void optimizeLaborInterleaving()}
            onSaveException={(e) => void saveException(e)}
          />
        </>
      ) : null}
    </UiPage>
  );
};

export default WarehouseCalendar;
