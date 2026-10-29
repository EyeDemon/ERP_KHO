// @vitest-environment jsdom
import { cleanup, fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WarehouseLocations from './WarehouseLocations';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

const get = vi.mocked(apiClient.get);

const warehouses = [{ id: 1, code: 'WH01', name: 'Kho Trung Tâm TP.HCM' }];
const capacities = [
  {
    locationId: 101,
    code: 'A01-R02-L03-B04',
    name: 'Kệ lưu trữ chính B04',
    structurePath: 'ZONE-A/A01/R02/L03/B04',
    storageClass: 'AMBIENT',
    maxWeightKg: 1500,
    usedWeightKg: 600,
    maxVolumeM3: 10,
    usedVolumeM3: 4,
    maxPalletEquivalent: 5,
    usedPalletEquivalent: 2,
    profileIncomplete: false,
    compatibilityConflict: false,
    isActive: true,
    isBlocked: false,
    state: 'Available',
  },
];

const locations = [
  {
    id: 101,
    warehouseId: 1,
    code: 'A01-R02-L03-B04',
    name: 'Kệ lưu trữ chính B04',
    structurePath: 'ZONE-A/A01/R02/L03/B04',
    storageClass: 'AMBIENT',
    maxWeightKg: 1500,
    maxVolumeM3: 10,
    maxPalletEquivalent: 5,
    locationType: 'Storage',
    isActive: true,
    isBlocked: false,
    isPickable: true,
    isReceivable: false,
    isSystemManaged: false,
    rowVersion: 'AQ==',
  },
  {
    id: 1,
    warehouseId: 1,
    code: 'RECEIVING',
    name: 'Cửa nhận hàng',
    structurePath: null,
    locationType: 'Receiving',
    isActive: true,
    isBlocked: false,
    isPickable: false,
    isReceivable: true,
    isSystemManaged: true,
    rowVersion: null,
  },
];

describe('Cấu trúc vị trí kho định tuyến về WarehouseLocations API thật', () => {
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
  });

  it('renders live API warehouse hierarchy and system locations via WarehouseLocations', async () => {
    const view = render(<WarehouseLocations />);
    expect((await view.findAllByText('ZONE-A')).length).toBeGreaterThan(0);
    expect(view.getAllByText('A01-R02-L03-B04').length).toBeGreaterThan(0);

    fireEvent.click(view.getByTestId('tab-view-system'));
    expect(view.getAllByText('RECEIVING').length).toBeGreaterThan(0);
    expect(get).toHaveBeenCalledWith('/api/putaway-tasks/locations', { params: { warehouseId: 1 } });
  });
});
