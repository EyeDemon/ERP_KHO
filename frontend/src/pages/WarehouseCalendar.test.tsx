// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import WarehouseCalendar from './WarehouseCalendar';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const put = vi.mocked(apiClient.put);

const warehouses = [{ id: 1, code: 'WH-HCM-01', name: 'DC Hồ Chí Minh' }];

const calendarPayload = {
  warehouseId: 1,
  warehouseCode: 'WH-HCM-01',
  warehouseName: 'DC Hồ Chí Minh',
  isConfigured: true,
  timeZoneId: 'UTC',
  localNow: '2026-04-30T08:00:00Z',
  isOpenNow: true,
  currentShiftCode: 'SHIFT-1',
  rowVersion: 'v1',
  days: [
    { dayOfWeek: 1, dayName: 'Thứ Hai', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '17:00', outboundCutoffLocal: '18:00', overnight: false },
    { dayOfWeek: 2, dayName: 'Thứ Ba', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '17:00', outboundCutoffLocal: '18:00', overnight: false },
    { dayOfWeek: 3, dayName: 'Thứ Tư', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '17:00', outboundCutoffLocal: '18:00', overnight: false },
    { dayOfWeek: 4, dayName: 'Thứ Năm', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '17:00', outboundCutoffLocal: '18:00', overnight: false },
    { dayOfWeek: 5, dayName: 'Thứ Sáu', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '22:00', inboundCutoffLocal: '17:00', outboundCutoffLocal: '18:00', overnight: false },
    { dayOfWeek: 6, dayName: 'Thứ Bảy', isOpen: true, opensAtLocal: '06:00', closesAtLocal: '18:00', inboundCutoffLocal: '15:00', outboundCutoffLocal: '16:00', overnight: false },
    { dayOfWeek: 0, dayName: 'Chủ Nhật', isOpen: false, opensAtLocal: null, closesAtLocal: null, inboundCutoffLocal: null, outboundCutoffLocal: null, overnight: false },
  ],
  shifts: [],
};

describe('WarehouseCalendar timezone and date placeholder', { timeout: 20000 }, () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["warehouse_calendar.read","warehouse_calendar.manage"]');
    get.mockImplementation(async (url) => {
      if (url === '/api/warehouses') return { data: warehouses } as never;
      if (url === '/api/warehouses/1/calendar') return { data: calendarPayload } as never;
      if (url === '/api/warehouses/1/calendar/exceptions') return { data: [] } as never;
      return { data: [] } as never;
    });
    put.mockResolvedValue({ data: {} } as never);
  });

  afterEach(cleanup);

  it('defaults timezone to Asia/Ho_Chi_Minh (GMT+7 — Giờ Việt Nam) while keeping UTC selectable, and uses dd/mm/yyyy placeholder', async () => {
    const view = render(<WarehouseCalendar />);
    const tzSelect = (await view.findByLabelText('Warehouse timezone', {}, { timeout: 10000 })) as HTMLSelectElement;
    expect(tzSelect.value).toBe('Asia/Ho_Chi_Minh');
    expect(view.getAllByText(/Asia\/Ho_Chi_Minh \(GMT\+7 — Giờ Việt Nam\)/).length).toBeGreaterThan(0);

    const exceptionWindowInput = view.getByLabelText('Khung giờ ngoại lệ lịch kho') as HTMLInputElement;
    expect(exceptionWindowInput.placeholder).toBe('VD: 30/04/2026 - 01/05/2026');

    fireEvent.change(tzSelect, { target: { value: 'UTC' } });
    expect(tzSelect.value).toBe('UTC');
  });
});
