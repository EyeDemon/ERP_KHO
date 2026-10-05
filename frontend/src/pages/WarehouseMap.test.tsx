// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WarehouseMap from './WarehouseMap';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), put: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const put = vi.mocked(apiClient.put);

const warehouses = [{ id: 1, code: 'WH-HCM-01', name: 'DC Hồ Chí Minh' }];
const snapshot = {
  warehouseId: 1,
  generatedAtUtc: new Date().toISOString(),
  items: [
    { locationId: 101, code: 'A01-R02-L03-B04', name: 'Ô B04', structurePath: 'ZONE-A/A01/R02/L03/B04', storageClass: 'AMBIENT', mapX: 5, mapY: 10, mapWidth: 20, mapHeight: 15, utilizationPercent: 86.67, capacityState: 'NearCapacity', recentMovementCount: 12, activePutawayCount: 5, activityLevel: 'High', isActive: true, isBlocked: false, rowVersion: 'AQ==' },
    { locationId: 102, code: 'A01-R02-L03-B05', name: 'Ô B05', structurePath: 'ZONE-A/A01/R02/L03/B05', storageClass: 'AMBIENT', mapX: null, mapY: null, mapWidth: null, mapHeight: null, utilizationPercent: 41.33, capacityState: 'Available', recentMovementCount: 2, activePutawayCount: 0, activityLevel: 'Low', isActive: true, isBlocked: false, rowVersion: 'Ag==' },
  ],
};

describe('WarehouseMap production UI', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    localStorage.setItem('role', 'Admin');
    localStorage.setItem('permissions', '["location.read","location.manage"]');
    get.mockImplementation(async (url) => {
      if (url === '/api/putaway-tasks/location-warehouses') return { data: warehouses } as never;
      if (url === '/api/putaway-tasks/location-map') return { data: snapshot } as never;
      return { data: [] } as never;
    });
    put.mockResolvedValue({ data: {} } as never);
  });

  it('renders mapped and unmapped locations from the real map endpoint', async () => {
    const view = render(<WarehouseMap />);
    expect(await view.findByLabelText('Bản đồ kho thật')).toBeTruthy();
    expect(view.getByRole('table', { name: 'Chi tiết heatmap kho thật' })).toBeTruthy();
    expect(view.getAllByText('A01-R02-L03-B04').length).toBeGreaterThan(0);
    expect(view.getAllByText('A01-R02-L03-B05').length).toBeGreaterThan(0);
    expect(view.getByRole('table', { name: 'Location chưa bố trí bản đồ' })).toBeTruthy();
    expect(get).toHaveBeenCalledWith('/api/putaway-tasks/location-map', { params: { warehouseId: 1 } });
  });

  it('updates an explicit layout with rowVersion', async () => {
    const view = render(<WarehouseMap />);
    const mappedLocation = await view.findByRole('button', { name: 'Vị trí A01-R02-L03-B04' });
    fireEvent.click(mappedLocation);
    fireEvent.change(view.getByLabelText('Map X'), { target: { value: '12.5' } });
    fireEvent.change(view.getByLabelText('Map Y'), { target: { value: '20' } });
    fireEvent.change(view.getByLabelText('Map Width'), { target: { value: '25' } });
    fireEvent.change(view.getByLabelText('Map Height'), { target: { value: '30' } });
    fireEvent.click(view.getByText('Lưu bố trí'));

    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/putaway-tasks/locations/101/layout', {
      mapX: 12.5,
      mapY: 20,
      mapWidth: 25,
      mapHeight: 30,
      rowVersion: 'AQ==',
    }));
  });

  it('keeps layout mutations hidden from a location reader', async () => {
    localStorage.setItem('permissions', '["location.read"]');
    const view = render(<WarehouseMap />);
    const mappedLocation = await view.findByRole('button', { name: 'Vị trí A01-R02-L03-B04' });
    expect(view.queryByText('Sửa bố trí')).toBeNull();
    expect(view.queryByText('Bố trí')).toBeNull();
    expect((mappedLocation as HTMLButtonElement).disabled).toBe(true);
  });
});
