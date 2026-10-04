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
const capacities = [
  { locationId: 101, code: 'A01-R02-L03-B04', name: 'Ô chính', structurePath: 'ZONE-A/A01/R02/L03/B04', storageClass: 'AMBIENT', maxWeightKg: 1500, usedWeightKg: 1260, maxVolumeM3: 10, usedVolumeM3: 8.2, maxPalletEquivalent: 5, usedPalletEquivalent: 4, profileIncomplete: false, compatibilityConflict: false, isActive: true, isBlocked: false, state: 'NearCapacity' },
];

const locations = [
  { id: 101, warehouseId: 1, code: 'A01-R02-L03-B04', name: 'Ô chính', structurePath: 'ZONE-A/A01/R02/L03/B04', storageClass: 'AMBIENT', maxWeightKg: 1500, maxVolumeM3: 10, maxPalletEquivalent: 5, locationType: 'Storage', isActive: true, isBlocked: false, isPickable: true, isReceivable: false, isSystemManaged: false, rowVersion: 'AQ==' },
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
      if (url === '/api/putaway-tasks/location-capacity') return { data: capacities } as never;
      return { data: [] } as never;
    });
    post.mockResolvedValue({ data: {} } as never);
    put.mockResolvedValue({ data: {} } as never);
  });

  it('renders real hierarchy, unmapped legacy locations and system locations', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    expect(view.getAllByText('A01-R02-L03-B04').length).toBeGreaterThan(0);
    expect(view.getAllByText('LEGACY-UNMAPPED-01').length).toBeGreaterThan(0);
    expect(view.getAllByText('RECEIVING').length).toBeGreaterThan(0);
    expect(view.getByRole('table', { name: 'Cây cấu trúc vị trí thật' })).toBeTruthy();
    expect(view.getByRole('table', { name: 'Sức chứa vị trí kho thật' })).toBeTruthy();
    expect(view.getAllByText('Gần đầy').length).toBeGreaterThan(0);
    expect(get).toHaveBeenCalledWith('/api/putaway-tasks/locations', { params: { warehouseId: 1 } });
    expect(get).toHaveBeenCalledWith('/api/putaway-tasks/location-capacity', { params: { warehouseId: 1 } });
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
    fireEvent.change(view.getByLabelText('Storage Class vị trí'), { target: { value: 'ambient' } });
    fireEvent.change(view.getByLabelText('Giới hạn trọng lượng kg'), { target: { value: '1200' } });
    fireEvent.submit(view.getByText('Lưu vị trí').closest('form')!);

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/putaway-tasks/locations', expect.objectContaining({
      warehouseId: 1,
      code: 'bin-10',
      structurePath: 'ZONE-B/A02/R03/L04/B10',
      storageClass: 'AMBIENT',
      maxWeightKg: 1200,
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
      updateConstraints: true,
      storageClass: 'AMBIENT',
      maxWeightKg: 1500,
      maxVolumeM3: 10,
      maxPalletEquivalent: 5,
      rowVersion: 'AQ==',
    })));
  });

  it('allows a legacy unmapped location to update capacity without fabricating a StructurePath', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('LEGACY-UNMAPPED-01')).length).toBeGreaterThan(0);
    const row = view.getAllByText('LEGACY-UNMAPPED-01').at(-1)?.closest('tr');
    expect(row).toBeTruthy();
    fireEvent.click(row!.querySelector('button')!);
    fireEvent.change(view.getByLabelText('Storage Class vị trí'), { target: { value: 'ambient' } });
    fireEvent.change(view.getByLabelText('Giới hạn trọng lượng kg'), { target: { value: '500' } });
    fireEvent.submit(view.getByText('Lưu vị trí').closest('form')!);

    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/putaway-tasks/locations/102', expect.objectContaining({
      structurePath: null,
      updateConstraints: true,
      storageClass: 'AMBIENT',
      maxWeightKg: 500,
      rowVersion: 'Ag==',
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
