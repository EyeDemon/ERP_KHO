import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { usePermission } from '../services/authorization';
import {
  UiEmptyState, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiToolbar, UiToolbarField,
} from '../ui/ProductionUi';
import './DockYard.css';
import {
  type ActionPanel, type Appointment, type AppointmentForm, buildAppointmentPayload, type Dock, type DockForm,
  type DwellMonitorItem, emptyAppointment, type InboundDocOption, localInput, mapAppointmentToForm,
  messageOf, statusLabel, type Warehouse, type YardForm, type YardSlot,
} from '../modules/inbound/DockYardTypes';
import { DockAppointmentFormCard, DockTimelineAndConfigCards } from '../modules/inbound/DockYardFormsAndPanels';
import { DockAppointmentsAndDoorsSection } from '../modules/inbound/DockYardAppointmentsSection';
import { DockYardSlotsAndCrossDockSection } from '../modules/inbound/DockYardSlotsAndCrossDockSection';

const DockYard = () => {
  const canReadDock = usePermission('dock.read');
  const canReadYard = usePermission('yard.read');
  const canManageDock = usePermission('dock.manage');
  const canManageAppointment = usePermission('dock_appointment.manage');
  const canCheckIn = usePermission('yard.checkin');
  const canAssignDock = usePermission('yard.assign_dock');
  const canCheckout = usePermission('yard.checkout');

  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [inboundPos, setInboundPos] = useState<InboundDocOption[]>([]);
  const [inboundAsns, setInboundAsns] = useState<InboundDocOption[]>([]);
  const [inboundReceipts, setInboundReceipts] = useState<InboundDocOption[]>([]);
  const [warehouseId, setWarehouseId] = useState(0);
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [dwellItems, setDwellItems] = useState<DwellMonitorItem[]>([]);
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
  const [crossDockResult, setCrossDockResult] = useState<{
    dispatchId: string; dockDoorCode: string; stagedHuCount: number; status: string;
  } | null>(null);
  const [crossDockBusy, setCrossDockBusy] = useState(false);
  const [activeSectionTab, setActiveSectionTab] = useState<'appointments' | 'yard' | 'crossdock' | 'all'>('appointments');

  const load = useCallback(async (targetWarehouseId: number) => {
    if (!targetWarehouseId) return;
    const revision = ++loadRevisionRef.current;
    setLoading(true); setError('');
    try {
      const [appointmentResponse, dwellResponse, dockResponse, yardResponse] = await Promise.all([
        apiClient.get('/api/v1/dock-yard/appointments', { params: { warehouseId: targetWarehouseId } }),
        apiClient.get('/api/v1/dock-yard/dwell-monitor', { params: { warehouseId: targetWarehouseId } }).catch(err => {
          if (err?.response?.status && err.response.status >= 400 && revision === loadRevisionRef.current) {
            setError(messageOf(err, 'Không thể tải giám sát thời gian lưu bãi.'));
          }
          return { data: [] };
        }),
        canReadDock ? apiClient.get('/api/v1/dock-yard/docks', { params: { warehouseId: targetWarehouseId } }) : Promise.resolve({ data: [] }),
        canReadYard ? apiClient.get('/api/v1/dock-yard/yard-slots', { params: { warehouseId: targetWarehouseId } }) : Promise.resolve({ data: [] }),
      ]);
      if (revision !== loadRevisionRef.current) return;
      setAppointments(appointmentResponse.data as Appointment[]);
      setDwellItems(Array.isArray(dwellResponse?.data) ? (dwellResponse.data as DwellMonitorItem[]) : []);
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
    void apiClient.get('/api/v1/dock-yard/warehouses')
      .then(response => {
        if (cancelled) return;
        const rows = response.data as Warehouse[];
        setWarehouses(rows);
        setWarehouseId(rows[0]?.id ?? 0);
      })
      .catch(failure => { if (!cancelled) setError(messageOf(failure, 'Không thể tải danh sách kho.')); });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    const warehouse = warehouses.find(item => item.id === warehouseId);
    setWarehouseTimeZone(warehouse?.timeZoneId || 'UTC');
    setSelected(null); setAppointmentForm(null); setDockForm(null); setYardForm(null); setActionPanel(null);
    if (!warehouseId) return;
    void load(warehouseId);
  }, [warehouseId, warehouses, load]);

  const filtered = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('vi');
    return appointments.filter(
      item =>
        (!status || String(item.status) === status) &&
        (!q ||
          [
            item.code, item.carrierName ?? '', item.vehiclePlate ?? '', item.driverName ?? '',
            item.dockCode ?? '', item.yardSlotCode ?? '', item.purchaseOrderCode ?? '',
            item.asnCode ?? '', item.importReceiptCode ?? '',
          ].join(' ').toLocaleLowerCase('vi').includes(q)),
    );
  }, [appointments, search, status]);

  useEffect(() => {
    if (!canManageAppointment) return;
    let cancelled = false;
    void Promise.all([
      Promise.resolve(apiClient.get('/api/v1/purchase-orders')).catch(() => ({ data: [] })),
      Promise.resolve(apiClient.get('/api/v1/asns')).catch(() => ({ data: [] })),
      Promise.resolve(apiClient.get('/api/v1/import-receipts')).catch(() => ({ data: [] })),
    ]).then(([poRes, asnRes, grRes]) => {
      if (cancelled) return;
      const poRows = Array.isArray(poRes?.data) ? poRes.data : Array.isArray((poRes?.data as { items?: unknown[] })?.items) ? (poRes.data as { items: InboundDocOption[] }).items : [];
      const asnRows = Array.isArray(asnRes?.data) ? asnRes.data : Array.isArray((asnRes?.data as { items?: unknown[] })?.items) ? (asnRes.data as { items: InboundDocOption[] }).items : [];
      const grRows = Array.isArray(grRes?.data) ? grRes.data : Array.isArray((grRes?.data as { items?: unknown[] })?.items) ? (grRes.data as { items: InboundDocOption[] }).items : [];
      setInboundPos(poRows); setInboundAsns(asnRows); setInboundReceipts(grRows);
    });
    return () => { cancelled = true; };
  }, [canManageAppointment]);

  const waitingCount = appointments.filter(x => [1, 2, 3].includes(x.status)).length;
  const inServiceCount = appointments.filter(x => [4, 5].includes(x.status)).length;
  const exceptionCount = appointments.filter(x => [8, 9].includes(x.status)).length;
  const activeDockCount = docks.filter(x => x.isActive).length;
  const occupiedYardCount = yardSlots.filter(x => x.occupied).length;
  const inboundActiveCount = appointments.filter(x => x.direction === 0 && ![6, 7, 8].includes(x.status)).length;
  const outboundActiveCount = appointments.filter(x => x.direction !== 0 && ![6, 7, 8].includes(x.status)).length;
  const crossDockMatchedCount = Math.min(inboundActiveCount, outboundActiveCount);

  const refresh = async () => {
    await load(warehouseId);
    if (selected) {
      try {
        const response = await apiClient.get('/api/v1/dock-yard/appointments/' + selected.id);
        setSelected(response.data as Appointment);
      } catch { setSelected(null); }
    }
  };

  const runCommand = async (appointment: Appointment, suffix: string, payload: Record<string, unknown> = {}) => {
    if (!appointment.rowVersion || savingRef.current) return;
    savingRef.current = true; setSaving(true); setError(''); setSuccess('');
    try {
      const body = { rowVersion: appointment.rowVersion, ...payload };
      const logicalAction = 'dock-yard:' + suffix + ':' + appointment.id + ':' + appointment.rowVersion + ':' + JSON.stringify(payload);
      await apiClient.post('/api/v1/dock-yard/appointments/' + appointment.id + '/' + suffix, body, { headers: idempotencyHeaders(logicalAction) });
      completeIdempotentAction(logicalAction);
      setSuccess('Đã cập nhật trạng thái lịch hẹn.'); setActionPanel(null); await refresh();
    } catch (failure) { setError(messageOf(failure, 'Không thể cập nhật lịch hẹn.')); }
    finally { savingRef.current = false; setSaving(false); }
  };

  const openAppointment = (appointment?: Appointment) => {
    setSuccess(''); setError('');
    if (!appointment) {
      const nextSeq = appointments.length + 1;
      const defaultStart = localInput(new Date(Date.now() + 30 * 60 * 1000).toISOString(), warehouseTimeZone);
      const defaultEnd = localInput(new Date(Date.now() + 90 * 60 * 1000).toISOString(), warehouseTimeZone);
      setAppointmentForm({ ...emptyAppointment(nextSeq), plannedStart: defaultStart, plannedEnd: defaultEnd });
      return;
    }
    setAppointmentForm(mapAppointmentToForm(appointment, warehouseTimeZone));
  };

  const saveAppointment = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!appointmentForm || savingRef.current) return;
    if (!appointmentForm.plannedStart || !appointmentForm.plannedEnd) return setError('Cần nhập đủ thời gian bắt đầu và kết thúc.');
    let payload: Record<string, unknown>;
    try {
      payload = buildAppointmentPayload(appointmentForm, warehouseId, warehouseTimeZone);
    } catch {
      return setError('Giờ lịch hẹn không hợp lệ trong múi giờ của kho. Hãy kiểm tra lại ngày/giờ.');
    }
    savingRef.current = true; setSaving(true); setError(''); setSuccess('');
    try {
      if (appointmentForm.id) {
        await apiClient.put('/api/v1/dock-yard/appointments/' + appointmentForm.id, payload);
        setSuccess('Đã cập nhật lịch hẹn Nháp.');
      } else {
        const logicalAction = 'dock-appointment:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/v1/dock-yard/appointments', payload, { headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
        setSuccess('Đã tạo lịch hẹn Nháp.');
      }
      setAppointmentForm(null); await refresh();
    } catch (failure) { setError(messageOf(failure, 'Không thể lưu lịch hẹn.')); }
    finally { savingRef.current = false; setSaving(false); }
  };

  const saveDock = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!dockForm || savingRef.current) return;
    savingRef.current = true; setSaving(true); setError(''); setSuccess('');
    try {
      const payload = {
        code: dockForm.code.trim(), name: dockForm.name.trim(),
        supportsInbound: dockForm.supportsInbound, supportsOutbound: dockForm.supportsOutbound,
        allowedVehicleType: dockForm.allowedVehicleType.trim() || null,
        isTemperatureControlled: dockForm.isTemperatureControlled, hazardAllowed: dockForm.hazardAllowed,
        isActive: dockForm.isActive, rowVersion: dockForm.rowVersion,
      };
      if (dockForm.id) await apiClient.put('/api/v1/dock-yard/docks/' + dockForm.id, payload, { params: { warehouseId } });
      else {
        const logicalAction = 'dock:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/v1/dock-yard/docks', payload, { params: { warehouseId }, headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
      }
      setDockForm(null); setSuccess('Đã lưu cấu hình cửa kho.'); await refresh();
    } catch (failure) { setError(messageOf(failure, 'Không thể lưu cửa kho.')); }
    finally { savingRef.current = false; setSaving(false); }
  };

  const saveYard = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!yardForm || savingRef.current) return;
    savingRef.current = true; setSaving(true); setError(''); setSuccess('');
    try {
      const payload = { code: yardForm.code.trim(), name: yardForm.name.trim(), isActive: yardForm.isActive, rowVersion: yardForm.rowVersion };
      if (yardForm.id) await apiClient.put('/api/v1/dock-yard/yard-slots/' + yardForm.id, payload, { params: { warehouseId } });
      else {
        const logicalAction = 'yard-slot:create:' + warehouseId + ':' + JSON.stringify(payload);
        await apiClient.post('/api/v1/dock-yard/yard-slots', payload, { params: { warehouseId }, headers: idempotencyHeaders(logicalAction) });
        completeIdempotentAction(logicalAction);
      }
      setYardForm(null); setSuccess('Đã lưu vị trí sân bãi.'); await refresh();
    } catch (failure) { setError(messageOf(failure, 'Không thể lưu vị trí sân bãi.')); }
    finally { savingRef.current = false; setSaving(false); }
  };

  const showDetail = async (appointment: Appointment) => {
    setError('');
    try {
      const response = await apiClient.get('/api/v1/dock-yard/appointments/' + appointment.id);
      setSelected(response.data as Appointment);
    } catch (failure) { setError(messageOf(failure, 'Không thể tải chi tiết lịch hẹn.')); }
  };

  return (
    <UiPage>
      <div className="dock-yard-page">
        <UiPageHeader
          eyebrow="Vận hành"
          title="Điều hành cửa kho & sân bãi"
          description="Điều phối lịch xe, ghi nhận vào cổng, vị trí sân bãi và phân cửa kho. Các trạng thái tại sân bãi không làm thay đổi tồn kho; tồn kho chỉ thay đổi ở nghiệp vụ nhận hàng/xác nhận giao riêng."
          actions={
            <div className="ui-inline-actions" style={{ gap: 8, flexWrap: 'wrap' }}>
              {canManageAppointment && (
                <button type="button" aria-label="Tạo lịch hẹn" className="ui-primary-button" style={{ background: '#0284c7', color: '#ffffff', fontWeight: 600, border: '1px solid #0284c7', padding: '8px 16px', borderRadius: 8 }} onClick={() => (appointmentForm && !appointmentForm.id ? setAppointmentForm(null) : openAppointment())}>
                  {appointmentForm && !appointmentForm.id ? 'Thu gọn biểu mẫu' : '+ Tạo lịch hẹn'}
                </button>
              )}
              <button type="button" style={{ background: '#f8fafc', color: '#0f172a', fontWeight: 600, border: '1px solid #cbd5e1', padding: '8px 14px', borderRadius: 8 }} onClick={() => setActiveSectionTab(prev => (prev === 'crossdock' ? 'appointments' : 'crossdock'))}>
                {activeSectionTab === 'crossdock' ? 'Quay lại Lịch hẹn & Cửa kho' : 'Mở điều phối trung chuyển'}
              </button>
            </div>
          }
        />

        {success && <p role="status" className="ui-success-text">{success}</p>}
        {error && <p role="alert">{error} <button type="button" onClick={() => void refresh()}>Tải lại</button></p>}

        {appointmentForm && canManageAppointment && (
          <DockAppointmentFormCard appointmentForm={appointmentForm} setAppointmentForm={setAppointmentForm} saving={saving} saveAppointment={saveAppointment} warehouseTimeZone={warehouseTimeZone} inboundPos={inboundPos} inboundAsns={inboundAsns} inboundReceipts={inboundReceipts} />
        )}

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

        {loading ? (
          <p role="status">Đang tải dữ liệu cửa kho & sân bãi...</p>
        ) : warehouses.length === 0 ? (
          <UiEmptyState title="Không có kho nào trong phạm vi được phép." />
        ) : (
          <>
            <div role="tablist" aria-label="Phân khu điều hành cửa kho và sân bãi" style={{ display: 'flex', flexWrap: 'wrap', gap: 8, padding: 6, borderRadius: 10, background: '#f1f5f9', border: '1px solid #cbd5e1' }}>
              {[
                { id: 'appointments' as const, label: `1. Lịch hẹn & Cửa kho (${filtered.length})` },
                { id: 'yard' as const, label: `2. Vị trí sân bãi & Giám sát lưu bãi (${yardSlots.length})` },
                { id: 'crossdock' as const, label: '3. Điều phối trung chuyển thẳng' },
                { id: 'all' as const, label: 'Xem tất cả phân khu' },
              ].map(tab => (
                <button key={tab.id} type="button" role="tab" aria-selected={activeSectionTab === tab.id} onClick={() => setActiveSectionTab(tab.id)} style={{ flex: '1 1 180px', minHeight: 38, fontWeight: 600, borderRadius: 8, background: activeSectionTab === tab.id ? '#0284c7' : '#ffffff', color: activeSectionTab === tab.id ? '#ffffff' : '#0f172a', border: `1px solid ${activeSectionTab === tab.id ? '#0284c7' : '#cbd5e1'}` }}>
                  {tab.label}
                </button>
              ))}
            </div>

            <DockAppointmentsAndDoorsSection
              activeSectionTab={activeSectionTab} filtered={filtered} warehouseTimeZone={warehouseTimeZone}
              canManageAppointment={canManageAppointment} canCheckIn={canCheckIn} canCheckout={canCheckout} canAssignDock={canAssignDock}
              runCommand={runCommand} setActionPanel={setActionPanel} showDetail={showDetail} openAppointment={openAppointment}
              docks={docks} canManageDock={canManageDock} setDockForm={setDockForm}
            />

            <DockYardSlotsAndCrossDockSection
              activeSectionTab={activeSectionTab} canManageDock={canManageDock} setYardForm={setYardForm} yardSlots={yardSlots} dwellItems={dwellItems}
              inboundActiveCount={inboundActiveCount} outboundActiveCount={outboundActiveCount} crossDockMatchedCount={crossDockMatchedCount}
              crossDockBusy={crossDockBusy} setCrossDockBusy={setCrossDockBusy} crossDockResult={crossDockResult} setCrossDockResult={setCrossDockResult}
              warehouseId={warehouseId} docks={docks} setError={setError}
            />
          </>
        )}

        <DockTimelineAndConfigCards
          selected={selected} setSelected={setSelected} warehouseTimeZone={warehouseTimeZone} docks={docks} yardSlots={yardSlots}
          dockForm={dockForm} setDockForm={setDockForm} canManageDock={canManageDock} saving={saving} saveDock={saveDock}
          yardForm={yardForm} setYardForm={setYardForm} saveYard={saveYard} actionPanel={actionPanel} setActionPanel={setActionPanel}
          runCommand={runCommand} inboundPos={inboundPos} inboundAsns={inboundAsns} inboundReceipts={inboundReceipts}
        />
      </div>
    </UiPage>
  );
};

export default DockYard;
