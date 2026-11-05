// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import { ShipmentGhnLiveIntegrationCard } from '../modules/outbound/ShipmentGhnLiveIntegrationCard';
import { ExportReceiptsBatchAwbCard } from '../modules/outbound/ExportReceiptsBatchAwbCard';
import CodReconciliation from './CodReconciliation';
import SaasBilling from './SaasBilling';
import BusinessPartners from './BusinessPartners';
import PlatformControlCenter from './PlatformControlCenter';
import PlatformControlCenterLayout from '../layouts/PlatformControlCenterLayout';
import MainLayout from '../layouts/MainLayout';
import { clearActiveImpersonationState } from '../config/portalMode';
import { resolveCanonicalMockResponse } from '../mocks/canonicalErpWmsMockData';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  logout: vi.fn(),
}));

const permissionState = vi.hoisted(() => ({ granted: new Set<string>() }));
vi.mock('../services/authorization', () => ({
  usePermission: (code: string) => permissionState.granted.has(code),
  hasPermission: (code: string) => permissionState.granted.has(code),
  usePermissionSet: () => permissionState.granted,
  beginPermissionRefresh: () => 1,
  setCurrentPermissions: () => undefined,
  canViewApprovals: () => true,
  canViewStocktakes: () => true,
}));

const MOCK_PLATFORM_TENANTS = [
  {
    tenantId: 1,
    tenantCode: 'TENANT-001',
    companyName: 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO',
    taxCode: '0312345678',
    planCode: 'GROWTH',
    lifecycleStatus: 'ACTIVE',
    usedWarehouses: 1,
    maxWarehouses: 3,
    usedUsers: 6,
    maxUsers: 20,
    currentMonthOrderCount: 6420,
    monthlyOrderQuota: 10000,
  },
];

const MOCK_PLATFORM_INVOICES = [
  {
    id: 1,
    tenantId: 1,
    invoiceCode: 'INV-SAAS-202610-001',
    planCode: 'GROWTH',
    billingPeriod: '10/2026',
    subTotalVnd: 1354545,
    vatRatePercent: 10,
    vatAmountVnd: 135455,
    amountVnd: 1490000,
    buyerTaxCode: '0312345678',
    buyerLegalName: 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO',
    status: 'PAID',
    bankTransactionRef: 'FT2627690123',
    bankCreditTimeVi: '03/10/2026 16:00',
    confirmedAtVi: '03/10/2026 16:15',
    settlementSummaryVi: 'Tự động qua VietQR (Mã giao dịch: FT2627690123)',
    createdAtUtc: '2026-10-01T08:00:00Z',
  },
  {
    id: 2,
    tenantId: 1,
    invoiceCode: 'INV-SAAS-202611-002',
    planCode: 'GROWTH',
    billingPeriod: '11/2026',
    subTotalVnd: 1354545,
    vatRatePercent: 10,
    vatAmountVnd: 135455,
    amountVnd: 1490000,
    buyerTaxCode: '0312345678',
    buyerLegalName: 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO',
    status: 'PENDING',
    createdAtUtc: '2026-11-01T08:00:00Z',
  },
];

describe('Wave 06–08 — Multi-Carrier GHN, Tax Compliance & SaaS Platform Control Center Logic', () => {
  afterEach(() => {
    cleanup();
    clearActiveImpersonationState();
    localStorage.removeItem('tenantLifecycleStatus');
  });

  beforeEach(() => {
    vi.resetAllMocks();
    permissionState.granted.clear();
    ['shipment.read', 'shipment.dispatch', 'inventory.read', 'inventory.write', 'inventory.cost.read', 'catalog.read', 'catalog.write', 'masterdata.manage', 'partner.update', 'export.read', 'export.write'].forEach((p) =>
      permissionState.granted.add(p),
    );
  });

  it('renders ShipmentGhnLiveIntegrationCard with multi-carrier rate shopping and GHN sync-detail + cancel-waybill', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      data: [{ carrierCode: 'GHN', shopId: '235512', shopName: '0397354273', maskedApiToken: '0c06****12be', gatewayMode: 'LIVE_GHN_DEV_SANDBOX', isSandbox: true, fromDistrictId: 1454, fromDistrictName: 'Quận 12', baseShippingFeeVnd: 22000 }],
    } as never);
    vi.mocked(apiClient.post).mockImplementation(async (url) => {
      if (String(url).endsWith('/ghn/sync-detail')) {
        return { data: { shipmentId: 7701, orderCode: 'GHN-L9X8K7', ghnStatus: 'ready_to_pick', mappedShipmentStatus: 'Dispatched', shopId: '235512', correlationId: 'CORR-GHN-01' } } as never;
      }
      return { data: { shipmentId: 7701, orderCode: 'GHN-L9X8K7', cancelled: true, restoredShipmentStatus: 'Ready' } } as never;
    });

    const view = render(<ShipmentGhnLiveIntegrationCard defaultShipmentId={7701} />);
    expect(((await view.findByTestId('select-carrier-provider')) as HTMLSelectElement).value).toBe('GHN');
    fireEvent.change(view.getByTestId('input-ghn-order-code'), { target: { value: 'GHN-L9X8K7' } });
    fireEvent.click(view.getByTestId('btn-ghn-sync-detail'));
    expect((await view.findByTestId('ghn-order-detail-banner')).textContent).toContain('GHN-L9X8K7');
    fireEvent.click(view.getByTestId('btn-ghn-cancel-waybill'));
    await waitFor(() => expect(apiClient.post).toHaveBeenCalledTimes(2));
  });

  it('renders CodReconciliation Decree 117/2025/NĐ-CP and ExportReceiptsBatchAwbCard AgreementDocumentNo', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        items: [{ id: 1, trackingCode: 'GHN-117-001', carrierCode: 'GHN', expectedCodAmount: 1000000, actualRemittedAmount: 965000, shippingFeeDeducted: 20000, varianceAmount: 0, platformTaxWithheldVat1Percent: 10000, platformTaxWithheldPit05Percent: 5000, netRemittanceAfterTax: 965000, status: 'Matched', agingDays: 1, createdAt: '2026-11-01T08:00:00Z' }],
        summary: { totalExpectedCod: 1000000, totalActualRemitted: 965000, totalVariance: 0, totalPlatformVat1Percent: 10000, totalPlatformPit05Percent: 5000, unreconciledOverdue7DaysCount: 0 },
      },
    } as never);
    const codView = render(<CodReconciliation />);
    expect(await codView.findByText(/Khấu trừ thuế Sàn TMĐT \(Nghị định 117\/2025\/NĐ-CP\)/)).toBeTruthy();
    cleanup();

    vi.mocked(apiClient.post).mockResolvedValue({ data: {} } as never);
    const awbView = render(<ExportReceiptsBatchAwbCard />);
    fireEvent.change(awbView.getByTestId('select-einvoice-action'), { target: { value: 'ADJUSTMENT' } });
    const agreementInput = awbView.getByTestId('input-einvoice-agreement-no') as HTMLInputElement;
    expect(agreementInput.required).toBe(true);
    fireEvent.change(agreementInput, { target: { value: 'BB-DC-2026-009' } });
    fireEvent.click(awbView.getByTestId('btn-sync-accounting-einvoice'));
    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith('/api/v1/export-receipts/export-misa-einvoice', expect.objectContaining({ agreementDocumentNo: 'BB-DC-2026-009' })));
  });

  it('renders SaasBilling tax profile form and BusinessPartners non-cash compliance controls', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (String(url).includes('subscription')) {
        return { data: { tenantCode: 'TENANT-001', planCode: 'GROWTH', planName: 'Growth WMS', monthlyPriceVnd: 1490000, status: 'ACTIVE', taxCode: '0312345678', buyerCompanyName: 'Công ty Minh Phát', buyerTaxCode: '0312345678', taxpayerType: 'ENTERPRISE', recentInvoices: [] } } as never;
      }
      if (url === '/api/v1/business-partners') {
        return { data: { items: [{ id: 11, code: 'SUP-01', name: 'Bao bì Minh Phú', isSupplier: true, isCustomer: false, taxCode: '0109876543', taxpayerType: 'ENTERPRISE', bankAccountNumber: '19036889911012', bankName: 'Techcombank', isActive: true }], totalPages: 1 } } as never;
      }
      return { data: [] } as never;
    });
    const billingView = render(<SaasBilling />);
    await waitFor(() => expect((billingView.getByTestId('input-saas-buyer-tax-code') as HTMLInputElement).value).toBe('0318294012'));
    cleanup();

    const partnerView = render(<BusinessPartners />);
    expect((await partnerView.findAllByText(/Thanh toán không tiền mặt >= 5\.000\.000 ₫/)).length).toBeGreaterThan(0);
    cleanup();
  }, 20000);

  it('renders authenticated /platform Overview, ignores ?rolePreview spoofing, enforces SuperAdmin 1-step vs BillingAdmin 4-eyes SoD, and handles sub-routes', async () => {
    let authMePayload: Record<string, unknown> = { username: 'qa_admin', role: 'SuperAdmin', securityDomain: 'PLATFORM' };
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/v1/auth/me') return { data: authMePayload } as never;
      if (url === '/api/v1/platform/dashboard') {
        return {
          data: {
            totalTenants: 1,
            activeTenants: 1,
            suspendedTenants: 0,
            mrrVnd: 1354545,
            arrVnd: 16254540,
            unpaidInvoicesCount: 1,
            paidInvoicesCount: 1,
            unpaidInvoiceDueLabelVi: 'Hạn thanh toán: 05/11/2026',
          },
        } as never;
      }
      if (url === '/api/v1/platform/tenants') return { data: { items: MOCK_PLATFORM_TENANTS } } as never;
      if (url === '/api/v1/platform/billing/invoices') {
        const canonicalBilling = resolveCanonicalMockResponse('get', '/api/v1/platform/billing/invoices', undefined, undefined, { 'x-mock-role': 'SuperAdmin' }).data as Record<string, unknown>;
        return { data: { ...canonicalBilling, items: MOCK_PLATFORM_INVOICES } } as never;
      }
      if (String(url).startsWith('/api/v1/platform/')) {
        const mockRes = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': String(authMePayload.role || 'SuperAdmin') });
        return { data: mockRes.data } as never;
      }
      return { data: {} } as never;
    });
    vi.mocked(apiClient.post).mockResolvedValue({
      data: { sessionId: 'IMP-2026-01', impersonationToken: 'IMP-TOKEN-001', targetTenantCode: 'TENANT-001', targetTenantName: 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)' },
    } as never);

    const overview = render(
      <MemoryRouter initialEntries={['/platform']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route index element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(await overview.findByTestId('platform-control-center-layout')).toBeTruthy();
    expect(overview.getByTestId('platform-page-heading').textContent).toBe('Tổng quan nền tảng');
    expect(overview.getByTestId('platform-domain-badge').textContent).toBe('Miền bảo mật: Quản trị nền tảng');
    expect(overview.getByTestId('platform-role-readonly-badge').textContent).toContain('Chủ dự án · Toàn quyền nền tảng');
    expect(overview.getByTestId('platform-header').querySelectorAll('select').length).toBe(0);
    expect(overview.queryByTestId('select-platform-role-preview')).toBeNull();
    expect(overview.queryByTestId('select-platform-role-simulator')).toBeNull();
    expect(overview.getByTestId('btn-platform-logout').textContent).toBe('Đăng xuất');
    const actionReq = await waitFor(() => {
      const el = overview.getByTestId('platform-action-required-section');
      expect(el.textContent).toMatch(/Hạn thanh toán: 05\/11\/2026/);
      return el;
    });
    const kpiGrid = overview.getByTestId('platform-kpi-grid');
    expect(Boolean(actionReq.compareDocumentPosition(kpiGrid) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);
    expect(overview.queryByTestId('platform-overview-quick-links')).toBeNull();
    expect(overview.queryByTestId('platform-tenants-table')).toBeNull();
    cleanup();

    // SEC-W17-008: ?rolePreview=Viewer query parameter MUST be ignored when /api/v1/auth/me returns SuperAdmin
    const spoofAttemptView = render(
      <MemoryRouter initialEntries={['/platform/billing?rolePreview=Viewer']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await spoofAttemptView.findByTestId('platform-billing-invoices-table')).toBeTruthy();
    expect(spoofAttemptView.getByTestId('platform-role-readonly-badge').textContent).toContain('Chủ dự án · Toàn quyền nền tảng');
    expect(spoofAttemptView.queryByTestId('platform-billing-rbac-forbidden-banner')).toBeNull();
    expect((spoofAttemptView.getByTestId('btn-reconcile-saas-invoice-2') as HTMLButtonElement).disabled).toBe(false);

    // FE-02 / OWN-DEC-PLATFORM-RBAC-FUTURE-READY: SuperAdmin modal has 0 4-eyes fields
    fireEvent.click(spoofAttemptView.getByTestId('btn-reconcile-saas-invoice-2'));
    expect(await spoofAttemptView.findByTestId('modal-confirm-invoice-reconcile')).toBeTruthy();
    expect(spoofAttemptView.queryByTestId('input-reconcile-approver')).toBeNull();
    expect(spoofAttemptView.getByTestId('modal-confirm-invoice-reconcile').textContent).not.toContain('Kiểm soát nội bộ (4 mắt)');
    cleanup();

    // SecurityAuditor read-only role shows BILLING_RECONCILE_FORBIDDEN banner and disables reconcile button
    authMePayload = { username: 'sec_auditor', role: 'SecurityAuditor', securityDomain: 'PLATFORM' };
    const auditorBillingView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await auditorBillingView.findByTestId('platform-billing-rbac-forbidden-banner')).toBeTruthy();
    expect(auditorBillingView.getByTestId('platform-role-readonly-badge').textContent).toContain('Vai trò: SecurityAuditor (Kiểm toán Chỉ đọc)');
    expect(auditorBillingView.getByTestId('platform-billing-rbac-forbidden-banner').textContent).toContain('BILLING_RECONCILE_FORBIDDEN');
    expect((await auditorBillingView.findByTestId('btn-reconcile-saas-invoice-2') as HTMLButtonElement).disabled).toBe(true);
    cleanup();

    // BillingAdmin delegated role enforces 4-eyes SoD, duplicate bank ref, future time, variance, and FormData upload
    authMePayload = { username: 'billing_admin', role: 'BillingAdmin', securityDomain: 'PLATFORM' };
    const billingPlatformView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const revenueSummary = await billingPlatformView.findByTestId('platform-billing-revenue-summary');
    await waitFor(() => expect(revenueSummary.textContent).toContain('ARR (Trước thuế): 16.254.540 ₫/năm'));
    expect(revenueSummary.textContent).toContain('Thu thực tế (Gồm 10% VAT): 1.490.000 ₫/tháng');
    const billingInvoicesTable = await billingPlatformView.findByTestId('platform-billing-invoices-table');
    expect(billingInvoicesTable.textContent).toContain('1.354.545 ₫');
    expect(billingInvoicesTable.textContent).toContain('135.455 ₫');
    expect(billingInvoicesTable.textContent).toContain('1.490.000 ₫');
    expect(billingInvoicesTable.textContent).toContain('10/2026');
    expect(billingInvoicesTable.textContent).toContain('11/2026');
    expect(billingInvoicesTable.textContent).toContain('Ngân hàng ghi có:');
    expect(billingInvoicesTable.textContent).toContain('Xác nhận lúc:');
    await waitFor(() => expect(billingPlatformView.container.textContent).toContain('HUYNH PHI VAN'));
    expect(billingPlatformView.container.textContent).not.toContain('NGUYEN THUY TRANG');
    expect(billingPlatformView.queryByTestId('platform-kpi-grid')).toBeNull();
    fireEvent.click(billingPlatformView.getByTestId('btn-reconcile-saas-invoice-2'));
    expect(await billingPlatformView.findByTestId('modal-confirm-invoice-reconcile')).toBeTruthy();
    expect(billingPlatformView.getByTestId('reconcile-actor-readonly').textContent).toContain('Quản trị Cước phí SaaS');
    expect((billingPlatformView.getByTestId('input-reconcile-approver') as HTMLSelectElement).value).toBe('');
    expect((billingPlatformView.getByTestId('input-reconcile-received-amount') as HTMLInputElement).value).toBe('');
    expect((billingPlatformView.getByTestId('input-reconcile-credit-time') as HTMLInputElement).value).toBe('');
    expect((billingPlatformView.getByTestId('btn-submit-manual-reconcile') as HTMLButtonElement).disabled).toBe(true);

    // 4-eyes SoD check: self-approval by billing_admin must be blocked
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-approver'), { target: { value: 'billing_admin' } });
    expect(billingPlatformView.getByTestId('reconcile-sod-error').textContent).toContain('BILLING_SOD_SELF_APPROVAL_FORBIDDEN');
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-approver'), { target: { value: 'ktt_nguyen_mai' } });

    // Duplicate bank ref guard (409 Conflict)
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-bank-ref'), { target: { value: 'FT2627690123' } });
    expect(billingPlatformView.getByTestId('reconcile-conflict-error').textContent).toContain('BILLING_DUPLICATE_BANK_TRANSFER_REF');
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-bank-ref'), { target: { value: 'FT26276110988' } });
    expect(billingPlatformView.getByTestId('reconcile-conflict-error').textContent).toContain('BILLING_DUPLICATE_BANK_TRANSFER_REF');
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-bank-ref'), { target: { value: 'FT2630698418102' } });

    // Future credit time guard
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-credit-time'), { target: { value: '31/12/2099 23:59' } });
    expect(billingPlatformView.getByTestId('reconcile-future-time-error')).toBeTruthy();
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-credit-time'), { target: { value: '02/11/2026 18:15' } });

    // Variance guard
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-received-amount'), { target: { value: '1400000' } });
    expect(billingPlatformView.getByTestId('checkbox-allow-reconcile-variance')).toBeTruthy();
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-received-amount'), { target: { value: '1490000' } });
    expect((billingPlatformView.getByTestId('input-reconcile-received-amount') as HTMLInputElement).value).toBe('1.490.000');
    expect(billingPlatformView.getByTestId('reconcile-amount-variance-banner').textContent).toContain('Khớp chính xác số tiền hóa đơn (1.490.000 ₫)');

    // Real binary File upload via multipart/form-data
    const proofFile = new File(['%PDF-1.4\n'], 'UNC_VCB_20261102_1490k.pdf', { type: 'application/pdf' });
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-attachment-file'), {
      target: { files: [proofFile] },
    });
    expect(billingPlatformView.getByTestId('reconcile-attached-filename').textContent).toContain('UNC_VCB_20261102_1490k.pdf');
    fireEvent.change(billingPlatformView.getByTestId('input-reconcile-note'), {
      target: { value: 'Đối soát sao kê ngân hàng TPBank khớp 100% số tiền hóa đơn' },
    });

    vi.mocked(apiClient.post).mockRejectedValueOnce({
      response: {
        status: 422,
        data: { errorCode: 'BILLING_SOD_SELF_APPROVAL_FORBIDDEN', message: 'Vi phạm nguyên tắc 4 mắt từ máy chủ.', correlationId: 'corr-reconcile-422' },
      },
    });
    expect((billingPlatformView.getByTestId('btn-submit-manual-reconcile') as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(billingPlatformView.getByTestId('btn-submit-manual-reconcile'));
    const modalServerErr = await billingPlatformView.findByTestId('reconcile-server-error');
    expect(modalServerErr.textContent).toContain('Vi phạm nguyên tắc 4 mắt từ máy chủ.');
    expect(modalServerErr.textContent).toContain('BILLING_SOD_SELF_APPROVAL_FORBIDDEN');
    expect(modalServerErr.textContent).toContain('corr-reconcile-422');
    expect(billingPlatformView.getByTestId('modal-confirm-invoice-reconcile')).toBeTruthy();

    fireEvent.click(billingPlatformView.getByTestId('btn-submit-manual-reconcile'));
    await waitFor(() => {
      const reconcileCalls = vi.mocked(apiClient.post).mock.calls.filter(([u]) => u === '/api/v1/platform/billing/invoices/2/reconcile');
      expect(reconcileCalls.length).toBeGreaterThanOrEqual(2);
      const sentFormData = reconcileCalls[reconcileCalls.length - 1]?.[1] as FormData;
      expect(sentFormData instanceof FormData).toBe(true);
      expect(sentFormData.get('bankTransactionRef')).toBe('FT2630698418102');
      expect(sentFormData.get('receivedAmountVnd')).toBe('1490000');
      expect(sentFormData.get('approverCode')).toBe('ktt_nguyen_mai');
      expect(sentFormData.get('approverUsername')).toBe('ktt_nguyen_mai');
      expect(sentFormData.get('attachmentFile')).toBe(proofFile);
    });
    cleanup();

    authMePayload = { username: 'qa_admin', role: 'SuperAdmin', securityDomain: 'PLATFORM' };
    const quotasPlatformView = render(
      <MemoryRouter initialEntries={['/platform/quotas']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="quotas" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await quotasPlatformView.findByTestId('platform-quotas-utilization-table')).toBeTruthy();
    expect(quotasPlatformView.queryByTestId('platform-tenants-table')).toBeNull();
    cleanup();

    const integrationsView = render(
      <MemoryRouter initialEntries={['/platform/integrations']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="integrations" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await integrationsView.findByTestId('platform-integrations-card')).toBeTruthy();
    expect(integrationsView.queryByTestId('platform-workers-card')).toBeNull();
    expect(integrationsView.queryByTestId('input-impersonation-ticket')).toBeNull();
    await waitFor(() => expect(integrationsView.getByTestId('vietqr-canonical-account').textContent).toContain('98418102005 (HUYNH PHI VAN)'));
    expect(integrationsView.getByTestId('channel-status-shopee').textContent).toContain('Shopee Open Platform: Hoạt động tốt');
    expect(integrationsView.getByTestId('channel-status-tiktok').textContent).toContain('TikTok Shop API: Hoạt động tốt');
    expect(integrationsView.getByTestId('card-integration-webhooks').textContent).toContain('Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)');
    expect(integrationsView.getByTestId('notice-ghn-sandbox-vi').textContent).toContain('Thử nghiệm (Không phát sinh cước thật)');
    expect(integrationsView.getByTestId('card-integration-ghn').textContent).toContain('24 thành công • 0 lỗi');
    expect(integrationsView.getByTestId('card-integration-webhooks').textContent).toContain('31 thành công • 0 lỗi');
    expect(integrationsView.getByTestId('card-integration-vietqr').textContent).toContain('12 thành công • 0 lỗi');
    cleanup();

    const workersView = render(
      <MemoryRouter initialEntries={['/platform/monitoring']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="monitoring" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect((await workersView.findByTestId('worker-last-run-InvoiceLedgerSyncWorker')).textContent).toContain('8 giây trước');
    expect(workersView.queryByTestId('platform-integrations-card')).toBeNull();
    expect(workersView.queryByTestId('input-impersonation-ticket')).toBeNull();
    expect(workersView.getByTestId('worker-last-run-InventoryConsistencyWorker').textContent).toContain('47 giây trước');
    expect(workersView.getByTestId('worker-last-run-WebhookRetryAndCleanupWorker').textContent).toContain('23 giây trước');
    cleanup();

    const supportView = render(
      <MemoryRouter initialEntries={['/platform/support']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="support" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const ticketInput = (await supportView.findByTestId('input-impersonation-ticket')) as HTMLInputElement;
    expect(supportView.queryByTestId('platform-integrations-card')).toBeNull();
    expect(supportView.queryByTestId('platform-workers-card')).toBeNull();
    expect(ticketInput.value).toBe('');
    fireEvent.change(ticketInput, { target: { value: 'SUP-2026-108' } });
    fireEvent.change(supportView.getByTestId('input-impersonation-reason'), {
      target: { value: 'Kiểm tra đối soát vận đơn GHN theo yêu cầu khách hàng' },
    });
    fireEvent.click(supportView.getByTestId('btn-start-impersonation'));
    expect(await supportView.findByTestId('active-impersonation-banner')).toBeTruthy();
    expect(ticketInput.value).toBe('');
    expect((supportView.getByTestId('input-impersonation-reason') as HTMLInputElement).value).toBe('');
    expect(supportView.getByTestId('impersonation-duplicate-guard-msg').textContent).toContain('Doanh nghiệp này đang có phiên hỗ trợ hoạt động');
    expect((supportView.getByTestId('btn-start-impersonation') as HTMLButtonElement).disabled).toBe(true);
    expect(supportView.getByTestId('active-impersonation-sessions-table').textContent).toContain('Chủ dự án (SuperAdmin)');
    expect(supportView.getByTestId('btn-end-session-row-1').textContent).toBe('Kết thúc');
    cleanup();

    localStorage.setItem('tenantLifecycleStatus', 'SUSPENDED');
    const customerView = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<MainLayout />}>
            <Route index element={<div>Trang chủ Kho</div>} />
          </Route>
          <Route path="/platform" element={<div data-testid="platform-redirect-target">Platform Target</div>} />
        </Routes>
      </MemoryRouter>,
    );
    const suspendedBanner = await customerView.findByTestId('tenant-suspended-lock-banner');
    expect(suspendedBanner.textContent).toContain('TENANT-001 (Công ty TNHH Thương mại & Kho vận Đại Phát)');
    expect(customerView.getByTestId('btn-goto-billing-unlock').textContent).toContain('Thanh toán mở khóa ngay →');
    expect(customerView.getByTestId('active-impersonation-ribbon').textContent).toContain('TENANT-001 (Công ty TNHH Thương mại & Kho vận Đại Phát)');
    expect(customerView.getByTestId('customer-portal-impersonation-countdown').textContent).toContain('Thời gian còn lại:');
    fireEvent.click(customerView.getByTestId('btn-exit-impersonation'));
    expect(await customerView.findByTestId('platform-redirect-target')).toBeTruthy();
  });

  it('enforces Wave 09–10 Quota Drawer formatted numeric input and Suspend Modal dual-box + post-suspension lock', async () => {
    let currentLifecycle = 'ACTIVE';
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/v1/auth/me') {
        return { data: { username: 'qa_admin', role: 'SuperAdmin', securityDomain: 'PLATFORM' } } as never;
      }
      if (url === '/api/v1/platform/tenants') {
        return {
          data: {
            items: [
              {
                tenantId: 1,
                tenantCode: 'TENANT-001',
                companyName: 'Doanh nghiệp Kho vận ERP_KHO (Tenant 1)',
                taxCode: '0312345678',
                planCode: 'GROWTH',
                lifecycleStatus: currentLifecycle,
                suspendedReason: currentLifecycle === 'SUSPENDED' ? 'Tạm khóa do quá hạn thanh toán hóa đơn SaaS trên 72 giờ' : null,
                usedWarehouses: 1,
                maxWarehouses: 3,
                usedUsers: 6,
                maxUsers: 20,
                currentMonthOrderCount: 6420,
                monthlyOrderQuota: 10000,
              },
            ],
          },
        } as never;
      }
      return { data: {} } as never;
    });
    vi.mocked(apiClient.post).mockImplementation(async () => {
      currentLifecycle = 'SUSPENDED';
      return { data: { lifecycleStatus: 'SUSPENDED' } } as never;
    });
    vi.mocked(apiClient.put).mockResolvedValue({ data: {} } as never);

    const view = render(
      <MemoryRouter initialEntries={['/platform/tenants']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="tenants" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    const table = await view.findByTestId('platform-tenants-table');
    await view.findByTestId('platform-tenant-row-1');
    expect(table.textContent).toContain('1/3 kho • 6/20 người');
    expect(table.textContent).toContain('6.420 / 10.000 (64%)');
    expect(table.textContent).toContain('Gói Tăng trưởng');
    expect(table.textContent).not.toContain('(Growth)');

    // 1. Open Quota Drawer first while tenant is ACTIVE
    fireEvent.click(view.getByTestId('btn-open-quota-modal-1'));
    expect(await view.findByTestId('drawer-adjust-tenant-quota')).toBeTruthy();
    expect((view.getByTestId('input-quota-max-warehouses') as HTMLInputElement).value).toBe('3');
    expect((view.getByTestId('input-quota-max-users') as HTMLInputElement).value).toBe('20');
    expect((view.getByTestId('input-quota-monthly-orders') as HTMLInputElement).value).toBe('10.000');
    expect(view.getByTestId('quota-old-new-diff').textContent).toContain('Chưa có thay đổi hạn mức');
    expect((view.getByTestId('btn-override-quota') as HTMLButtonElement).disabled).toBe(true);

    fireEvent.change(view.getByTestId('input-quota-max-users'), { target: { value: '3' } });
    expect(await view.findByTestId('quota-below-usage-error')).toBeTruthy();

    fireEvent.change(view.getByTestId('input-quota-max-users'), { target: { value: '30' } });
    fireEvent.change(view.getByTestId('input-quota-max-warehouses'), { target: { value: '5' } });
    fireEvent.change(view.getByTestId('input-quota-monthly-orders'), { target: { value: '20.000' } });
    expect((view.getByTestId('input-quota-monthly-orders') as HTMLInputElement).value).toBe('20.000');
    expect((view.getByTestId('input-quota-effective-until') as HTMLInputElement).value).toBe('');
    const quotaReasonInput = view.getByTestId('input-quota-reason') as HTMLTextAreaElement;
    fireEvent.change(quotaReasonInput, { target: { value: 'Nâng hạn mức kho và đơn hàng theo Phụ lục Hợp đồng B2B' } });
    expect((view.getByTestId('btn-override-quota') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(view.getByTestId('input-quota-contract-ref'), { target: { value: 'PLHĐ-2026-01' } });
    expect((view.getByTestId('btn-override-quota') as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(view.getByTestId('input-quota-effective-until'), { target: { value: '31/12/2026' } });
    expect((view.getByTestId('input-quota-effective-until') as HTMLInputElement).value).toBe('31/12/2026');
    expect(view.getByTestId('vn-date-verbal-helper').textContent).toBe('Hiệu lực đến: Ngày 31 tháng 12 năm 2026');
    expect(await view.findByTestId('quota-pricing-impact-note')).toBeTruthy();
    expect(view.getByTestId('quota-old-new-diff').textContent).toContain('3 kho → 5 kho');
    expect(view.getByTestId('quota-old-new-diff').textContent).toContain('20 tài khoản nhân sự → 30 tài khoản nhân sự');
    expect((view.getByTestId('btn-override-quota') as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(view.getByTestId('btn-override-quota'));

    await waitFor(() =>
      expect(apiClient.put).toHaveBeenCalledWith(
        '/api/v1/platform/tenants/1/quotas',
        expect.objectContaining({ maxWarehouses: 5, maxUsers: 30, monthlyOrderQuota: 20000 }),
      ),
    );

    // 2. Open [...] menu to reveal Suspend action
    fireEvent.click(view.getByTestId('btn-tenant-more-menu-1'));
    expect(await view.findByTestId('tenant-more-menu-popover-1')).toBeTruthy();
    const suspendMenuItem = view.getByTestId('btn-suspend-tenant-1');
    expect(suspendMenuItem.className).toContain('tenant-more-menu-item-danger');
    expect(suspendMenuItem.className).not.toContain('platform-btn-danger');
    fireEvent.click(suspendMenuItem);

    expect(await view.findByTestId('modal-confirm-suspend-tenant')).toBeTruthy();
    expect(view.getByTestId('suspend-modal-impact-box')).toBeTruthy();
    expect(view.getByTestId('suspend-modal-reassurance-box')).toBeTruthy();
    expect(view.getByTestId('select-suspend-category')).toBeTruthy();
    const codeInput = view.getByTestId('input-confirm-tenant-code') as HTMLInputElement;
    const reasonInput = view.getByTestId('input-suspend-reason') as HTMLTextAreaElement;
    const confirmSubmitBtn = view.getByTestId('btn-confirm-suspend-submit') as HTMLButtonElement;
    expect(codeInput.value).toBe('');
    expect(reasonInput.value).toBe('');
    expect(confirmSubmitBtn.disabled).toBe(true);

    fireEvent.change(view.getByTestId('select-suspend-category'), { target: { value: 'OVERDUE_PAYMENT' } });
    fireEvent.change(codeInput, { target: { value: 'TENANT-001' } });
    fireEvent.change(reasonInput, { target: { value: 'Tạm khóa do quá hạn thanh toán hóa đơn SaaS trên 72 giờ' } });
    expect(confirmSubmitBtn.disabled).toBe(false);
    fireEvent.click(confirmSubmitBtn);

    await waitFor(() =>
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/platform/tenants/1/status',
        expect.objectContaining({ status: 'SUSPENDED', confirmationCode: 'TENANT-001' }),
      ),
    );
    expect((await view.findByTestId('platform-action-notice')).textContent).toContain(
      'Đã tạm khóa doanh nghiệp TENANT-001, thu hồi toàn bộ phiên đăng nhập và kết thúc phiên hỗ trợ đang mở.',
    );
    expect(await view.findByTestId('btn-toggle-suspend-reason-1')).toBeTruthy();
    expect(view.getByTestId('suspended-quota-lock-reason-1').textContent).toBe('Khóa đổi hạn mức khi tạm ngưng');
    expect(view.getByTestId('btn-open-quota-modal-1').getAttribute('title')).toBe('Không thể đổi hạn mức khi đang tạm khóa');
  });
});
