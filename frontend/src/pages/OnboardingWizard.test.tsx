// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import OnboardingWizard from './OnboardingWizard';
import apiClient from '../services/apiClient';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe('OnboardingWizard Page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(cleanup);

  it('provisions tenant, activates 5-step quick-start, and uploads opening balance Excel via FormData', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
      if (url === '/api/v1/permissions/onboarding-wizard/status') {
        return {
          data: {
            completedSteps: 2,
            totalSteps: 5,
            statusText: 'Đang thiết lập',
          },
        } as never;
      }
      if (url === '/api/v1/import-templates/opening-balance') {
        return { data: new Uint8Array([80, 75, 3, 4]) } as never;
      }
      throw new Error(`Unexpected GET ${url}`);
    });

    vi.mocked(apiClient.post).mockImplementation(async (url: string, body?: any) => {
      if (url === '/api/v1/permissions/saas/tenants/provision') {
        return {
          data: {
            completedSteps: 3,
            status: 'Đã cấp phát Tenant & Sơ đồ kho mẫu',
          },
        } as never;
      }
      if (url === '/api/v1/reports/onboarding-wizard/quick-start') {
        return {
          data: {
            completedSteps: 5,
            status: 'Hoàn tất 5/5 bước chuẩn hóa',
          },
        } as never;
      }
      if (url === '/api/v1/data-imports/opening-balance') {
        expect(body).toBeInstanceOf(FormData);
        const formData = body as FormData;
        expect(formData.get('file')).toBeInstanceOf(File);
        return {
          data: {
            importBatchId: 'OB-BATCH-2026',
            importedRows: 15,
            status: 'Đã hạch toán tồn kho đầu kỳ vào sổ cái bất biến',
          },
        } as never;
      }
      throw new Error(`Unexpected POST ${url}`);
    });

    render(<OnboardingWizard />);

    expect(await screen.findByText(/2\/5 bước • Đang thiết lập/i)).toBeTruthy();

    fireEvent.click(screen.getByTestId('btn-provision-tenant'));
    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/permissions/saas/tenants/provision',
        expect.objectContaining({
          tenantCode: 'TENANT-SME-2026',
          templateCode: 'ECOMMERCE_3ZONES',
        }),
      );
    });
    expect(await screen.findByText(/3\/5 bước • Đã cấp phát Tenant & Sơ đồ kho mẫu/i)).toBeTruthy();

    fireEvent.click(screen.getByTestId('btn-quick-start-5steps'));
    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/reports/onboarding-wizard/quick-start',
        expect.objectContaining({
          tenantCode: 'TENANT-SME-2026',
        }),
      );
    });
    expect(await screen.findByText(/5\/5 bước • Hoàn tất 5\/5 bước chuẩn hóa/i)).toBeTruthy();

    const fileInput = screen.getByTestId('input-onboarding-opening-file');
    const openingFile = new File(['sku,qty\nSKU-01,10'], 'opening_balance.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    fireEvent.change(fileInput, { target: { files: [openingFile] } });
    fireEvent.click(screen.getByTestId('btn-upload-opening-balance'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/data-imports/opening-balance',
        expect.any(FormData),
        expect.objectContaining({
          headers: { 'Content-Type': 'multipart/form-data' },
        }),
      );
    });
    expect(await screen.findByText(/Mã lô nhập: OB-BATCH-2026 • Số dòng hợp lệ: 15/i)).toBeTruthy();
  });

  it('displays server error alert when tenant provisioning fails with 422', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { completedSteps: 0, totalSteps: 5 } } as never);
    vi.mocked(apiClient.post).mockRejectedValue({
      response: {
        status: 422,
        data: {
          errorCode: 'TENANT_CODE_DUPLICATE',
          message: 'Mã thuê bao TENANT-SME-2026 đã tồn tại trên hệ thống.',
          correlationId: 'corr-onb-422',
        },
      },
    });

    render(<OnboardingWizard />);

    fireEvent.click(screen.getByTestId('btn-provision-tenant'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Mã thuê bao TENANT-SME-2026 đã tồn tại trên hệ thống.');
    expect(alert.textContent).toContain('TENANT_CODE_DUPLICATE');
    expect(alert.textContent).toContain('corr-onb-422');
  });
});
