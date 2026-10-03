// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WarehouseStructure from './WarehouseStructure';
import apiClient from '../services/apiClient';
import { usePermission } from '../services/authorization';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn() },
}));

vi.mock('../services/authorization', () => ({
  usePermission: vi.fn(),
}));

const warehouse = { id: 1, code: 'WH-HCM-01', name: 'DC Hồ Chí Minh', isActive: true };
const structure = {
  warehouseId: 1,
  warehouseCode: 'WH-HCM-01',
  warehouseName: 'DC Hồ Chí Minh',
  zones: [{
    id: 10,
    warehouseId: 1,
    code: 'ZONE-A',
    name: 'Khu lưu trữ A',
    zoneType: 'STORAGE',
    pickPriority: 10,
    putawayPriority: 10,
    isActive: true,
    rowVersion: 'AAAAAAAAAAA=',
    aisles: [{
      id: 20,
      zoneId: 10,
      code: 'A01',
      name: 'Dãy 01',
      rowVersion: 'AAAAAAAAAAA=',
      racks: [{
        id: 30,
        aisleId: 20,
        code: 'R02',
        name: 'Kệ 02',
        rowVersion: 'AAAAAAAAAAA=',
        levels: [{
          id: 40,
          rackId: 30,
          levelNo: 3,
          rowVersion: 'AAAAAAAAAAA=',
          locations: [{
            id: 101,
            warehouseId: 1,
            zoneId: 10,
            zoneCode: 'ZONE-A',
            rackLevelId: 40,
            aisleCode: 'A01',
            rackCode: 'R02',
            levelNo: 3,
            code: 'A01-R02-L03-B04',
            name: 'Ô A01-R02-L03-B04',
            barcode: 'LOC-HCM-A01-R02-L03-B04',
            locationType: 'Storage',
            isActive: true,
            isBlocked: false,
            isPickable: true,
            isReceivable: false,
            isSystemManaged: false,
            rowVersion: 'AAAAAAAAAAA=',
          }],
        }],
      }],
    }],
    locations: [],
  }],
  unmappedLocations: [],
  systemLocations: [{
    id: 1,
    warehouseId: 1,
    code: 'RECEIVING',
    name: 'Vị trí nhận hàng',
    locationType: 'Receiving',
    isActive: true,
    isBlocked: false,
    isPickable: false,
    isReceivable: true,
    isSystemManaged: true,
  }],
};

describe('Cấu trúc vị trí kho', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/warehouses') return { data: [warehouse] } as never;
      if (url === '/api/warehouses/1/structure') return { data: structure } as never;
      throw new Error('unexpected GET ' + url);
    });
  });

  afterEach(cleanup);

  it('renders the canonical hierarchy read-only for a viewer', async () => {
    vi.mocked(usePermission).mockReturnValue(false);
    const view = render(<WarehouseStructure />);

    await view.findByText('A01-R02-L03-B04');
    expect(view.getAllByText('ZONE-A').length).toBeGreaterThan(0);
    expect(view.getByText('Dãy 01')).toBeTruthy();
    expect(view.getByText('Kệ 02')).toBeTruthy();
    expect(view.getByText('Tầng 3')).toBeTruthy();
    expect(view.getByText('Vị trí nhận hàng')).toBeTruthy();
    expect(view.queryByText('Thêm cấu trúc vật lý')).toBeNull();
    expect(view.queryByText('Thêm ô / vị trí')).toBeNull();
    expect(apiClient.post).not.toHaveBeenCalled();
    expect(apiClient.put).not.toHaveBeenCalled();
    expect(apiClient.patch).not.toHaveBeenCalled();
  });

  it('shows separate structure and location management forms only with matching permissions', async () => {
    vi.mocked(usePermission).mockImplementation(code => code === 'warehouse_zone.manage' || code === 'location.manage');
    const view = render(<WarehouseStructure />);

    await view.findByText('Thêm cấu trúc vật lý');
    expect(view.getByText('Thêm ô / vị trí')).toBeTruthy();
    expect(view.getByLabelText('Kho cấu trúc vị trí')).toBeTruthy();
    expect(view.getByText(/Mã cấu trúc và mã vị trí không đổi/)).toBeTruthy();
  });

  it('creates a zone with the canonical warehouse-scoped endpoint', async () => {
    vi.mocked(usePermission).mockImplementation(code => code === 'warehouse_zone.manage');
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} } as never);
    const view = render(<WarehouseStructure />);

    await view.findByText('Thêm cấu trúc vật lý');
    const code = view.getByPlaceholderText('Ví dụ: ZONE-A');
    fireEvent.change(code, { target: { value: 'zone-b' } });
    const nameInput = view.getAllByText('Tên').map(label => label.parentElement?.querySelector('input')).find(Boolean) as HTMLInputElement;
    fireEvent.change(nameInput, { target: { value: 'Khu B' } });
    fireEvent.click(view.getByRole('button', { name: 'Tạo cấu trúc' }));

    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith('/api/warehouses/1/zones', expect.objectContaining({
      code: 'ZONE-B',
      name: 'Khu B',
      zoneType: 'STORAGE',
    })));
  });
});
