// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import HandoverManifests from './HandoverManifests';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('HandoverManifests Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(cleanup);

  it('loads handover manifests with Vietnamese status badges, scans valid AWB, and seals manifest with vehiclePlate and driverName', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/handover-manifests') {
        return {
          data: {
            items: [
              {
                id: 101,
                manifestCode: 'HND-GHN-20261024-001',
                carrierCode: 'GHN',
                vehiclePlate: '51C-888.99',
                driverName: 'Nguyễn Văn Giao',
                totalPackages: 12,
                status: 'SEALED',
                createdAt: '2026-10-24T08:00:00Z',
              },
              {
                id: 102,
                manifestCode: 'HND-SPX-20261024-002',
                carrierCode: 'SPX',
                vehiclePlate: '51D-111.22',
                driverName: 'Lê Văn Nhận',
                lines: [{ awbCode: 'SPX-1' }, { awbCode: 'SPX-2' }],
                status: 'OPEN',
                createdAt: '2026-10-24T08:15:00Z',
              },
            ],
          },
        } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/handover-manifests/scan-awb') {
        return {
          data: {
            awbCode: body?.trackingNumber ?? 'GHN-AWB-9001',
            carrierCode: body?.carrierCode ?? 'GHN',
            orderCode: 'SO-2026-9001',
            status: 'Sẵn sàng bàn giao',
          },
        } as never;
      }
      if (url === '/api/v1/handover-manifests/confirm-handover') {
        return {
          data: {
            manifestCode: 'HND-GHN-20261024-003',
            status: 'SEALED',
          },
        } as never;
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    const view = render(<HandoverManifests />);

    expect(await screen.findByText('HND-GHN-20261024-001')).toBeTruthy();
    expect(screen.getByText('51C-888.99')).toBeTruthy();
    expect(screen.getByText('Đã niêm phong & Bàn giao')).toBeTruthy();
    expect(screen.getByText('Đang mở nhận kiện')).toBeTruthy();
    expect(view.container.textContent).not.toContain('(Handover Manifests)');

    fireEvent.change(screen.getByTestId('input-scan-awb'), { target: { value: 'GHN-AWB-9001' } });
    fireEvent.click(screen.getByTestId('btn-scan-awb'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/handover-manifests/scan-awb',
        expect.objectContaining({
          trackingNumber: 'GHN-AWB-9001',
          carrierCode: 'GHN',
        }),
      );
    });
    expect(await screen.findByText('SO-2026-9001')).toBeTruthy();

    fireEvent.change(screen.getByTestId('input-vehicle-plate'), { target: { value: '51D-777.88' } });
    fireEvent.change(screen.getByTestId('input-driver-name'), { target: { value: 'Trần Văn Tài' } });
    fireEvent.click(screen.getByTestId('btn-confirm-handover-manifest'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/handover-manifests/confirm-handover',
        expect.objectContaining({
          carrierCode: 'GHN',
          vehiclePlate: '51D-777.88',
          driverName: 'Trần Văn Tài',
          awbCodes: ['GHN-AWB-9001'],
        }),
      );
    });
    expect(await screen.findByText(/Đã niêm phong & xuất kho bàn giao biên bản HND-GHN-20261024-003/i)).toBeTruthy();
  });

  it('renders critical red alert when scanning an AWB whose order was cancelled mid-flight (409 ORDER_CANCELLED_PUTBACK_REQUIRED)', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { items: [] } } as never);
    vi.mocked(apiClient.post).mockRejectedValue({
      response: {
        status: 409,
        data: {
          errorCode: 'ORDER_CANCELLED_PUTBACK_REQUIRED',
          message: 'Đơn hàng đã bị hủy trên sàn.',
        },
      },
    });

    render(<HandoverManifests />);

    await screen.findByText(/Chưa có biên bản bàn giao bưu tá nào/i);
    fireEvent.change(screen.getByTestId('input-scan-awb'), { target: { value: 'SPX-CANCEL-409' } });
    fireEvent.click(screen.getByTestId('btn-scan-awb'));

    const criticalBanner = await screen.findByTestId('handover-critical-alert');
    expect(criticalBanner.textContent).toContain('ORDER_CANCELLED_PUTBACK_REQUIRED');
    expect(criticalBanner.textContent).toContain('SPX-CANCEL-409');
  });
});
