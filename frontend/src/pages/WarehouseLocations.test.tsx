// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WarehouseLocations from './WarehouseLocations';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const put = vi.mocked(apiClient.put);

const warehouses = [{ id: 1, code: 'WH-HCM-01', name: 'DC Hồ Chí Minh' }];
const locations = [
  { id: 101, warehouseId: 1, code: 'A01-R02-L03-B04', name: 'Ô chính', structurePath: 'ZONE-A/A01/R02/L03/B04', locationType: 'Storage', isActive: true, isBlocked: false, isPickable: true, isReceivable: false, isSystemManaged: false, rowVersion: 'AQ==' },
  { id: 102, warehouseId: 1, code: 'LEGACY-UNMAPPED-01', name: 'Vị trí cũ', structurePath: null, locationType: 'Storage', isActive: true, isBlocked: false, isPickable: true, isReceivable: false, isSystemManaged: false, rowVersion: 'Ag==' },
  { id: 1, warehouseId: 1, code: 'RECEIVING', name: 'Nhận hàng', structurePath: null, locationType: 'Receiving', isActive: true, isBlocked: false, isPickable: false, isReceivable: true, isSystemManaged: true, rowVersion: null },
];

describe('WarehouseLocations production UI', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["location.read","location.manage"]');
    get.mockImplementation(async (url) => {
      if (url === '/api/putaway-tasks/location-warehouses') return { data: warehouses } as never;
      if (url === '/api/putaway-tasks/locations') return { data: locations } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
    put.mockResolvedValue({ data: {} } as never);
  });

  it('renders real hierarchy, unmapped legacy locations and system locations', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    expect(view.getAllByText('A01-R02-L03-B04').length).toBeGreaterThan(0);
    expect(view.getByText('LEGACY-UNMAPPED-01')).toBeTruthy();
    expect(view.getByText('RECEIVING')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Cây cấu trúc vị trí thật' })).toBeTruthy();
    expect(get).toHaveBeenCalledWith('/api/putaway-tasks/locations', { params: { warehouseId: 1 } });
  });

  it('normalizes and submits the five-level StructurePath on create', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    fireEvent.click(view.getByText('Thêm vị trí'));
    fireEvent.change(view.getByLabelText('Mã vị trí'), { target: { value: ' bin-10 ' } });
    fireEvent.change(view.getByLabelText('Tên vị trí'), { target: { value: 'Ô mới' } });
    fireEvent.change(view.getByLabelText('Zone'), { target: { value: ' zone-b ' } });
    fireEvent.change(view.getByLabelText('Aisle'), { target: { value: ' a02 ' } });
    fireEvent.change(view.getByLabelText('Rack'), { target: { value: ' r03 ' } });
    fireEvent.change(view.getByLabelText('Level'), { target: { value: ' l04 ' } });
    fireEvent.change(view.getByLabelText('Bin'), { target: { value: ' b10 ' } });
    fireEvent.submit(view.getByText('Lưu vị trí').closest('form')!);

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/putaway-tasks/locations', expect.objectContaining({
      warehouseId: 1,
      code: 'bin-10',
      structurePath: 'ZONE-B/A02/R03/L04/B10',
      locationType: 'Storage',
      isPickable: true,
      isReceivable: false,
    })));
  });

  it('locks an assigned StructurePath and sends rowVersion on edit', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    const row = view.getAllByText('A01-R02-L03-B04')[0].closest('tr');
    expect(row).toBeTruthy();
    fireEvent.click(row!.querySelector('button')!);
    const zone = view.getByLabelText('Zone') as HTMLInputElement;
    expect(zone.closest('fieldset')?.disabled).toBe(true);
    fireEvent.change(view.getByLabelText('Tên vị trí'), { target: { value: 'Ô chính mới' } });
    fireEvent.submit(view.getByText('Lưu vị trí').closest('form')!);
    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/putaway-tasks/locations/101', expect.objectContaining({
      name: 'Ô chính mới',
      structurePath: 'ZONE-A/A01/R02/L03/B04',
      rowVersion: 'AQ==',
    })));
  });

  it('hides mutation controls from a location reader', async () => {
    localStorage.setItem('permissions', '["location.read"]');
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    expect(view.queryByText('Thêm vị trí')).toBeNull();
    expect(view.queryByText('Gắn cấu trúc / Sửa')).toBeNull();
  });
});
