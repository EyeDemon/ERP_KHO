export type Warehouse = { id: number; code: string; name: string; timeZoneId: string; calendarConfigured: boolean };
export type Dock = {
  id: number; warehouseId: number; code: string; name: string;
  supportsInbound: boolean; supportsOutbound: boolean; allowedVehicleType?: string | null;
  isTemperatureControlled: boolean; hazardAllowed: boolean; isActive: boolean; rowVersion?: string | null;
};
export type YardSlot = {
  id: number; warehouseId: number; code: string; name: string;
  isActive: boolean; occupied: boolean; occupiedByAppointmentCode?: string | null; rowVersion?: string | null;
};
export type AppointmentEvent = {
  id: number; eventType: string; eventAtUtc: string; actorUserId: number;
  dockId?: number | null; yardSlotId?: number | null; note?: string | null;
};
export type Appointment = {
  id: number; warehouseId: number; warehouseCode: string; warehouseName: string;
  code: string; direction: number; status: number; plannedStartUtc: string; plannedEndUtc: string;
  carrierCode?: string | null; carrierName?: string | null; vehiclePlate?: string | null;
  trailerPlate?: string | null; vehicleType?: string | null;
  requiresTemperatureControl: boolean; hazardous: boolean;
  driverName?: string | null; driverPhone?: string | null; sealNumber?: string | null;
  yardSlotId?: number | null; yardSlotCode?: string | null; dockId?: number | null; dockCode?: string | null;
  arrivedAtUtc?: string | null; checkedInAtUtc?: string | null; dockAssignedAtUtc?: string | null;
  serviceStartedAtUtc?: string | null; serviceCompletedAtUtc?: string | null; checkedOutAtUtc?: string | null;
  exceptionCode?: string | null; note?: string | null; rowVersion?: string | null;
  events?: AppointmentEvent[]; purchaseOrderId?: number | null; purchaseOrderCode?: string | null;
  asnId?: number | null; asnCode?: string | null; importReceiptId?: number | null; importReceiptCode?: string | null;
};

export type AppointmentForm = {
  id?: number; code: string; direction: '0' | '1'; plannedStart: string; plannedEnd: string;
  carrierCode: string; carrierName: string; vehiclePlate: string; trailerPlate: string;
  vehicleType: string; requiresTemperatureControl: boolean; hazardous: boolean; note: string;
  purchaseOrderId: string; asnId: string; importReceiptId: string; rowVersion?: string | null;
};
export type DockForm = {
  id?: number; code: string; name: string; supportsInbound: boolean; supportsOutbound: boolean;
  allowedVehicleType: string; isTemperatureControlled: boolean; hazardAllowed: boolean;
  isActive: boolean; rowVersion?: string | null;
};
export type YardForm = { id?: number; code: string; name: string; isActive: boolean; rowVersion?: string | null };
export type DwellMonitorItem = {
  appointmentId: number; appointmentCode: string; warehouseId: number; warehouseCode: string;
  direction: number; status: number; carrierName?: string | null; vehiclePlate?: string | null;
  driverName?: string | null; yardSlotCode?: string | null; dockCode?: string | null;
  entryAnchorUtc: string; dwellMinutes: number; slaState: 'NORMAL' | 'WARNING' | 'OVERDUE' | string;
};
export type ActionPanel =
  | {
      mode: 'checkin'; appointment: Appointment; vehiclePlate: string; trailerPlate: string;
      driverName: string; driverPhone: string; sealNumber: string; yardSlotId: string; note: string;
    }
  | { mode: 'assignDock'; appointment: Appointment; dockId: string; note: string }
  | { mode: 'exception'; appointment: Appointment; exceptionCode: string; note: string }
  | {
      mode: 'linkInbound'; appointment: Appointment; purchaseOrderId: string;
      asnId: string; importReceiptId: string; note: string;
    };
export type InboundDocOption = { id: number; code: string; supplierName?: string };

export const statusLabel: Record<number, string> = {
  0: 'Nháp', 1: 'Đã xác nhận', 2: 'Đã đến cổng', 3: 'Đã vào bãi',
  4: 'Đang tại cửa kho', 5: 'Đang bốc dỡ', 6: 'Hoàn tất bốc dỡ',
  7: 'Đã hủy', 8: 'Không đến', 9: 'Ngoại lệ',
};

export const statusTone = (status: number) =>
  status === 6
    ? ('success' as const)
    : status === 7 || status === 8 || status === 9
      ? ('danger' as const)
      : status === 0
        ? ('neutral' as const)
        : status === 5
          ? ('warning' as const)
          : ('success' as const);

export const directionLabel = (value: number) => (value === 0 ? 'Nhập kho' : 'Xuất kho');

export const vehicleTypeLabel = (value?: string | null) => {
  if (!value) return 'Mọi loại';
  const map: Record<string, string> = {
    TRUCK: 'Xe tải', CONTAINER: 'Xe đầu kéo / Container', VAN: 'Xe tải nhỏ',
    REEFER: 'Xe lạnh', LORRY: 'Xe tải nặng',
  };
  return map[value.toUpperCase()] ?? value;
};

export const eventTypeLabel = (value: string) =>
  ({
    CREATED: 'Đã tạo', Created: 'Đã tạo',
    CONFIRMED: 'Đã xác nhận', Confirmed: 'Đã xác nhận',
    ARRIVED: 'Xe đã đến', Arrived: 'Xe đã đến',
    CHECKED_IN: 'Đã vào cổng', CheckedIn: 'Đã vào cổng',
    DOCK_ASSIGNED: 'Đã gán cửa kho', DockAssigned: 'Đã gán cửa kho',
    SERVICE_STARTED: 'Bắt đầu phục vụ', ServiceStarted: 'Bắt đầu phục vụ',
    SERVICE_COMPLETED: 'Hoàn thành phục vụ', ServiceCompleted: 'Hoàn thành phục vụ',
    CHECKED_OUT: 'Đã rời cổng', CheckedOut: 'Đã rời cổng',
    CANCELLED: 'Đã hủy', Cancelled: 'Đã hủy',
    NO_SHOW: 'Không đến', NoShow: 'Không đến',
    EXCEPTION_RECORDED: 'Ghi nhận ngoại lệ', ExceptionRecorded: 'Ghi nhận ngoại lệ',
    INBOUND_DOCUMENTS_LINKED: 'Liên kết chứng từ nhập',
    INBOUND_DOC_LINK: 'Liên kết chứng từ nhập',
    InboundDocumentsLinked: 'Liên kết chứng từ nhập',
  })[value] ?? value;

export const timeZoneParts = (date: Date, timeZone: string) => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(date);
  return Object.fromEntries(parts.filter(part => part.type !== 'literal').map(part => [part.type, part.value]));
};

export const localInput = (value: string | null | undefined, timeZone: string) => {
  if (!value) return '';
  const parts = timeZoneParts(new Date(value), timeZone);
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
};

export const zonedLocalToIso = (value: string, timeZone: string) => {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) throw new Error('INVALID_LOCAL_TIME');
  const [, year, month, day, hour, minute] = match;
  const wallClockUtc = Date.UTC(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute), 0);

  const offsetAt = (instantMs: number) => {
    const parts = timeZoneParts(new Date(instantMs), timeZone);
    const represented = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute), Number(parts.second));
    return represented - instantMs;
  };

  let instant = wallClockUtc - offsetAt(wallClockUtc);
  instant = wallClockUtc - offsetAt(instant);
  const result = new Date(instant);
  if (localInput(result.toISOString(), timeZone) !== value) throw new Error('INVALID_LOCAL_TIME');
  return result.toISOString();
};

export const displayWarehouseTime = (value: string, timeZone: string) => {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const parts = timeZoneParts(d, timeZone);
  return `${parts.day}/${parts.month}/${parts.year} ${parts.hour}:${parts.minute}`;
};

export const normalizeVietnameseText = (text?: string | null) => (text ?? '').replace(/Hoạt Động/g, 'Hoạt động');

export const isMaintenanceItem = (isActive: boolean, code?: string | null, name?: string | null) =>
  !isActive || /bảo trì|maintenance/i.test(`${code ?? ''} ${name ?? ''}`);

export const isAppointmentOverdue = (item: Appointment) => {
  if (![0, 1, 2, 3].includes(item.status) || !item.plannedEndUtc) return false;
  const endMs = new Date(item.plannedEndUtc).getTime();
  return Number.isFinite(endMs) && endMs < Date.now();
};

export const emptyAppointment = (nextSeq = 1): AppointmentForm => ({
  code: `APT-2026-${String(nextSeq).padStart(3, '0')}`,
  direction: '0', plannedStart: '', plannedEnd: '', carrierCode: '', carrierName: '',
  vehiclePlate: '', trailerPlate: '', vehicleType: '', requiresTemperatureControl: false,
  hazardous: false, note: '', purchaseOrderId: '', asnId: '', importReceiptId: '',
});

export const mapAppointmentToForm = (appointment: Appointment, warehouseTimeZone: string): AppointmentForm => ({
  id: appointment.id, code: appointment.code, direction: String(appointment.direction) as '0' | '1',
  plannedStart: localInput(appointment.plannedStartUtc, warehouseTimeZone),
  plannedEnd: localInput(appointment.plannedEndUtc, warehouseTimeZone),
  carrierCode: appointment.carrierCode ?? '', carrierName: appointment.carrierName ?? '',
  vehiclePlate: appointment.vehiclePlate ?? '', trailerPlate: appointment.trailerPlate ?? '',
  vehicleType: appointment.vehicleType ?? '', requiresTemperatureControl: appointment.requiresTemperatureControl,
  hazardous: appointment.hazardous, note: appointment.note ?? '',
  purchaseOrderId: appointment.purchaseOrderId ? String(appointment.purchaseOrderId) : '',
  asnId: appointment.asnId ? String(appointment.asnId) : '',
  importReceiptId: appointment.importReceiptId ? String(appointment.importReceiptId) : '',
  rowVersion: appointment.rowVersion,
});

export const buildAppointmentPayload = (appointmentForm: AppointmentForm, warehouseId: number, warehouseTimeZone: string): Record<string, unknown> => {
  const payload: Record<string, unknown> = {
    warehouseId, code: appointmentForm.code.trim(), direction: Number(appointmentForm.direction),
    plannedStart: zonedLocalToIso(appointmentForm.plannedStart, warehouseTimeZone),
    plannedEnd: zonedLocalToIso(appointmentForm.plannedEnd, warehouseTimeZone),
    carrierCode: appointmentForm.carrierCode.trim() || null, carrierName: appointmentForm.carrierName.trim() || null,
    vehiclePlate: appointmentForm.vehiclePlate.trim() || null, trailerPlate: appointmentForm.trailerPlate.trim() || null,
    vehicleType: appointmentForm.vehicleType.trim() || null,
    requiresTemperatureControl: appointmentForm.requiresTemperatureControl, hazardous: appointmentForm.hazardous,
    note: appointmentForm.note.trim() || null, rowVersion: appointmentForm.rowVersion,
  };
  if (appointmentForm.direction === '0') {
    if (appointmentForm.purchaseOrderId.trim()) payload.purchaseOrderId = Number(appointmentForm.purchaseOrderId);
    if (appointmentForm.asnId.trim()) payload.asnId = Number(appointmentForm.asnId);
    if (appointmentForm.importReceiptId.trim()) payload.importReceiptId = Number(appointmentForm.importReceiptId);
  }
  return payload;
};

export const emptyDock = (): DockForm => ({
  code: '', name: '', supportsInbound: true, supportsOutbound: true,
  allowedVehicleType: '', isTemperatureControlled: false, hazardAllowed: false, isActive: true,
});

export const emptyYard = (): YardForm => ({ code: '', name: '', isActive: true });

export const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  const base = response.response?.data?.message || fallback;
  return response.response?.status === 409 ? base + ' Hãy tải lại dữ liệu trước khi thử lại.' : base;
};
