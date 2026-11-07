export type Warehouse = { id: number; code: string; name: string };

export type CalendarDay = {
  dayOfWeek: number;
  dayName: string;
  isOpen: boolean;
  opensAtLocal?: string | null;
  closesAtLocal?: string | null;
  inboundCutoffLocal?: string | null;
  outboundCutoffLocal?: string | null;
  overnight: boolean;
};

export type Shift = {
  id: number;
  warehouseId: number;
  code: string;
  name: string;
  startTimeLocal: string;
  endTimeLocal: string;
  overnight: boolean;
  breakMinutes: number;
  plannedHeadcount: number;
  inboundPalletsPerHour?: number | null;
  outboundOrdersPerHour?: number | null;
  dockSlots?: number | null;
  laborHours?: number | null;
  stagingCapacity?: number | null;
  packingStations?: number | null;
  equipmentAvailable?: number | null;
  isActive: boolean;
  rowVersion?: string | null;
};

export type CalendarResponse = {
  warehouseId: number;
  warehouseCode: string;
  warehouseName: string;
  isConfigured: boolean;
  timeZoneId: string;
  localNow: string;
  isOpenNow: boolean;
  currentShiftCode?: string | null;
  rowVersion?: string | null;
  days: CalendarDay[];
  shifts: Shift[];
};

export type DayForm = Omit<CalendarDay, 'dayName' | 'overnight'>;

export type ShiftForm = {
  id?: number;
  code: string;
  name: string;
  startTimeLocal: string;
  endTimeLocal: string;
  breakMinutes: string;
  plannedHeadcount: string;
  inboundPalletsPerHour: string;
  outboundOrdersPerHour: string;
  dockSlots: string;
  laborHours: string;
  stagingCapacity: string;
  packingStations: string;
  equipmentAvailable: string;
  isActive: boolean;
  rowVersion?: string | null;
};

export type CalendarException = {
  id: number;
  warehouseId: number;
  exceptionType: string;
  windowSummary: string;
  reason: string;
  createdBy?: number | null;
  createdAtUtc: string;
};

export const exceptionTypeLabel: Record<string, string> = {
  HOLIDAY_CLOSURE: 'Nghỉ lễ (Đóng kho)',
  MAINTENANCE_WINDOW: 'Bảo trì định kỳ',
  EMERGENCY_OPEN_OVERRIDE: 'Ghi đè mở kho khẩn cấp',
};

export const exceptionTypeTone = (type: string): 'danger' | 'warning' | 'success' | 'neutral' =>
  type === 'HOLIDAY_CLOSURE'
    ? 'danger'
    : type === 'MAINTENANCE_WINDOW'
      ? 'warning'
      : type === 'EMERGENCY_OPEN_OVERRIDE'
        ? 'success'
        : 'neutral';

export const DEFAULT_TIMEZONE = 'Asia/Ho_Chi_Minh';

export const formatTimeZoneDisplay = (tz?: string | null) =>
  !tz || tz === 'Asia/Ho_Chi_Minh'
    ? 'Asia/Ho_Chi_Minh (GMT+7 — Giờ Việt Nam)'
    : tz;

export const emptyShift = (): ShiftForm => ({
  code: '',
  name: '',
  startTimeLocal: '06:00',
  endTimeLocal: '14:00',
  breakMinutes: '30',
  plannedHeadcount: '0',
  inboundPalletsPerHour: '',
  outboundOrdersPerHour: '',
  dockSlots: '',
  laborHours: '',
  stagingCapacity: '',
  packingStations: '',
  equipmentAvailable: '',
  isActive: true,
});

export const messageOf = (failure: unknown, fallback: string) => {
  const response = failure as { response?: { status?: number; data?: { message?: string } } };
  const base = response.response?.data?.message || fallback;
  return response.response?.status === 409 ? base + ' Hãy tải lại dữ liệu trước khi thử lại.' : base;
};

export const decimalOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
export const integerOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
