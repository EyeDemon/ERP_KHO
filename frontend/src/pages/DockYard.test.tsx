// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import DockYard from './DockYard';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);

const warehouse = {
  id: 1,
  code: 'WH-HCM-01',
  name: 'DC Hồ Chí Minh',
  timeZoneId: 'Asia/Ho_Chi_Minh',
  calendarConfigured: true,
};

const draftAppointment = {
  id: 1040,
  warehouseId: 1,
  warehouseCode: 'WH-HCM-01',
  warehouseName: 'DC Hồ Chí Minh',
  code: 'APT-2026-1040',
  direction: 0,
  status: 0,
  plannedStartUtc: '2026-10-05T09:00:00Z',
  plannedEndUtc: '2026-10-05T10:00:00Z',
  carrierName: 'Fast Logistics',
  vehiclePlate: '51C-882.14',
  trailerPlate: null,
  vehicleType: 'TRUCK',
  requiresTemperatureControl: false,
  hazardous: false,
  yardSlotId: null,
  yardSlotCode: null,
  dockId: null,
  dockCode: null,
  rowVersion: 'AQEBAQEBAQE=',
  events: [],
};

describe('DockYard production UI', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    get.mockImplementation(async (url) => {
      if (url === '/api/dock-yard/warehouses') return { data: [warehouse] } as never;
      if (url === '/api/dock-yard/appointments') return { data: [draftAppointment] } as never;
      if (url === '/api/dock-yard/docks') return { data: [{ id: 102, warehouseId: 1, code: 'D-02', name: 'Dock 02', supportsInbound: true, supportsOutbound: true, isTemperatureControlled: false, hazardAllowed: false, isActive: true, rowVersion: null }] } as never;
      if (url === '/api/dock-yard/yard-slots') return { data: [{ id: 152, warehouseId: 1, code: 'Y-02', name: 'Yard 02', isActive: true, occupied: false, rowVersion: null }] } as never;
      if (url === '/api/dock-yard/appointments/1040') return { data: draftAppointment } as never;
      return { data: [] } as never;
    });
  });

  it('keeps operational mutations hidden from a read-only Dock/Yard user', async () => {
    localStorage.setItem('permissions', JSON.stringify(['dock.read', 'dock_appointment.read', 'yard.read']));
    const view = render(<DockYard />);

    expect(await view.findByText('APT-2026-1040')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Danh sách lịch hẹn cửa kho sân bãi' })).toBeTruthy();
    expect(view.queryByRole('button', { name: 'Tạo lịch hẹn' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Xác nhận' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Thêm cửa kho' })).toBeNull();
    expect(view.queryByRole('button', { name: 'Thêm vị trí sân bãi' })).toBeNull();
  });

  it('guards a double-click command and sends an idempotency key', async () => {
    localStorage.setItem('permissions', JSON.stringify([
      'dock.read',
      'dock_appointment.read',
      'dock_appointment.manage',
      'yard.read',
    ]));
    let release: ((value: unknown) => void) | undefined;
    post.mockImplementation(() => new Promise(resolve => { release = resolve; }) as never);

    const view = render(<DockYard />);
    const confirm = await view.findByRole('button', { name: 'Xác nhận lịch hẹn APT-2026-1040' });
    fireEvent.click(confirm);
    fireEvent.click(confirm);

    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(
      '/api/dock-yard/appointments/1040/confirm',
      { rowVersion: 'AQEBAQEBAQE=' },
      { headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) },
    );

    release?.({ data: {} });
    await waitFor(() => expect(view.getByText('Đã cập nhật trạng thái lịch hẹn.')).toBeTruthy());
  });

  it('edits appointment wall-clock values in the warehouse timezone, not the browser timezone', async () => {
    localStorage.setItem('permissions', JSON.stringify([
      'dock.read',
      'dock.manage',
      'dock_appointment.read',
      'dock_appointment.manage',
      'yard.read',
    ]));
    const view = render(<DockYard />);

    const edit = await view.findByRole('button', { name: 'Sửa lịch hẹn APT-2026-1040' });
    fireEvent.click(edit);

    const start = view.getByLabelText('Bắt đầu lịch hẹn theo múi giờ kho') as HTMLInputElement;
    const end = view.getByLabelText('Kết thúc lịch hẹn theo múi giờ kho') as HTMLInputElement;
    expect(start.value).toBe('2026-10-05T16:00');
    expect(end.value).toBe('2026-10-05T17:00');
    expect(start.closest('label')?.textContent).toContain('Asia/Ho_Chi_Minh');
  });
});
