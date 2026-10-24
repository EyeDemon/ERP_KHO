// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import CodReconciliation from './CodReconciliation';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('CodReconciliation Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(cleanup);

  it('loads 3-way COD & shipping fee ledger from API and uploads real binary statement file via FormData', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/reports/cod-shipping-reconciliation') {
        return {
          data: {
            totalExpectedCod: 19500000,
            totalActualCod: 18200000,
            totalShippingFeeVariance: 45000,
            discrepancyCount: 1,
            totalShipments: 2,
            matchedShipments: 1,
            items: [
              {
                id: 1,
                trackingNumber: 'GHN-COD-801',
                carrierCode: 'GHN',
                expectedCod: 10000000,
                actualCod: 10000000,
                expectedShippingFee: 30000,
                actualShippingFee: 30000,
                shippingFeeVariance: 0,
                status: 'MATCHED',
              },
              {
                id: 2,
                trackingNumber: 'SPX-COD-802',
                carrierCode: 'SPX',
                expectedCod: 9500000,
                actualCod: 8200000,
                expectedShippingFee: 25000,
                actualShippingFee: 70000,
                shippingFeeVariance: 45000,
                status: 'DISCREPANCY',
              },
            ],
          },
        } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/reports/cod-shipping-reconciliation') {
        expect(body).toBeInstanceOf(FormData);
        const formData = body as FormData;
        expect(formData.get('file')).toBeInstanceOf(File);
        expect(formData.get('carrierCode')).toBe('GHN');
        return {
          data: {
            totalExpectedCod: 19500000,
            totalActualCod: 19500000,
            totalShippingFeeVariance: 0,
            discrepancyCount: 0,
            totalShipments: 2,
            matchedShipments: 2,
            matchRatePct: 100,
            items: [],
          },
        } as never;
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    const view = render(<CodReconciliation />);

    expect(await screen.findByText('19.500.000 đ')).toBeTruthy();
    expect(screen.getByText('18.200.000 đ')).toBeTruthy();
    expect(screen.getByText('Sổ cái Đối soát Chi tiết Từng Vận đơn • 2 vận đơn')).toBeTruthy();
    expect(screen.getByText('MÃ VẬN ĐƠN')).toBeTruthy();
    expect(screen.getByText('COD PHẢI THU')).toBeTruthy();
    expect(screen.getByText('COD THỰC NHẬN')).toBeTruthy();
    expect(screen.getByText('LỆCH PHÍ VC')).toBeTruthy();
    expect(screen.getByText('TRẠNG THÁI')).toBeTruthy();
    expect(view.container.textContent).not.toContain('(ShipmentFinancialLedgers)');
    expect(view.container.textContent).not.toContain('TrackingNumber');
    expect(view.container.textContent).not.toContain('ExpectedCod');
    expect(screen.getByText('GHN-COD-801')).toBeTruthy();
    expect(screen.getByText('SPX-COD-802')).toBeTruthy();
    expect(screen.getByText('Khớp đối soát 3 chiều')).toBeTruthy();
    expect(screen.getByText('Cảnh báo lệch phí')).toBeTruthy();

    const fileInput = screen.getByTestId('input-cod-statement-file');
    const statementFile = new File(['awb,cod\nGHN-COD-801,10000000'], 'cod_statement.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    fireEvent.change(fileInput, { target: { files: [statementFile] } });

    fireEvent.click(screen.getByTestId('btn-submit-cod-reconciliation'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/reports/cod-shipping-reconciliation',
        expect.any(FormData),
        expect.objectContaining({
          headers: { 'Content-Type': 'multipart/form-data' },
        }),
      );
    });

    const notice = await screen.findByTestId('cod-reconciliation-notice');
    expect(notice.textContent).toContain(
      'Đã đối soát 2/2 vận đơn (100%), phát hiện 0 cảnh báo lệch phí/chậm hoàn.',
    );
  });

  it('renders error alert when COD reconciliation upload fails with 422', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { items: [] } } as never);
    vi.mocked(apiClient.post).mockRejectedValue({
      response: {
        status: 422,
        data: {
          errorCode: 'INVALID_CARRIER_STATEMENT',
          message: 'Bảng kê đối soát COD thiếu cột Mã vận đơn.',
          correlationId: 'corr-cod-422',
        },
      },
    });

    render(<CodReconciliation />);

    await screen.findByText(/Chưa có bản ghi vận đơn COD/i);
    fireEvent.click(screen.getByTestId('btn-submit-cod-reconciliation'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Bảng kê đối soát COD thiếu cột Mã vận đơn.');
    expect(alert.textContent).toContain('INVALID_CARRIER_STATEMENT');
    expect(alert.textContent).toContain('corr-cod-422');
  });
});
