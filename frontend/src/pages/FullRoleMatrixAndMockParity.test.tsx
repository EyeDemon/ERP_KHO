// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../services/apiClient';
import {
  CANONICAL_APP_ROLES,
  canReadInventoryCost,
  isPlatformRole,
  setCurrentPermissions,
} from '../services/authorization';
import {
  CANONICAL_ROLE_PROFILES,
  resolveCanonicalMockResponse,
  type CanonicalRoleName,
} from '../mocks/canonicalErpWmsMockData';
import {
  clearActiveImpersonationState,
  IMPERSONATION_STORAGE_KEY,
  setActiveImpersonationState,
} from '../config/portalMode';
import PlatformControlCenterLayout from '../layouts/PlatformControlCenterLayout';
import PlatformControlCenter from './PlatformControlCenter';
import { mapRawPlatformTenants } from '../modules/platform/platformBillingHelpers';
import SaasBilling from './SaasBilling';
import { ProductBundleAndGiftSection } from '../modules/catalog/ProductBundleAndGiftSection';
import { PackingKittingAndVideoTab } from '../modules/outbound/PackingKittingAndVideoTab';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
  logout: vi.fn(),
}));

const ALL_TEN_ROLES: readonly CanonicalRoleName[] = [
  'SuperAdmin',
  'BillingAdmin',
  'SupportAdmin',
  'SecurityAuditor',
  'Admin',
  'Manager',
  'Supervisor',
  'Accountant',
  'WarehouseStaff',
  'Viewer',
];

const CUSTOMER_ROLES: readonly CanonicalRoleName[] = [
  'Admin',
  'Manager',
  'Supervisor',
  'Accountant',
  'WarehouseStaff',
  'Viewer',
];

describe('Wave-17 Full 10-Role RBAC Matrix, Security Isolation & Canonical Mock Parity', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    window.sessionStorage.clear();
    clearActiveImpersonationState();
    setCurrentPermissions([]);
  });

  afterEach(() => {
    cleanup();
    clearActiveImpersonationState();
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it('verifies exact permission counts, securityDomain, warehouse scope, and inventory.cost.read parity across all 10 canonical roles', () => {
    expect([...CANONICAL_APP_ROLES]).toEqual([...ALL_TEN_ROLES]);

    const expectedCounts: Record<CanonicalRoleName, number> = {
      SuperAdmin: 151,
      BillingAdmin: 7,
      SupportAdmin: 5,
      SecurityAuditor: 4,
      Admin: 141,
      Manager: 98,
      Supervisor: 54,
      Accountant: 42,
      WarehouseStaff: 36,
      Viewer: 18,
    };

    const expectedCostRead: Record<CanonicalRoleName, boolean> = {
      SuperAdmin: true,
      BillingAdmin: false,
      SupportAdmin: false,
      SecurityAuditor: false,
      Admin: true,
      Manager: true,
      Supervisor: false,
      Accountant: true,
      WarehouseStaff: false,
      Viewer: false,
    };

    for (const role of ALL_TEN_ROLES) {
      const profile = CANONICAL_ROLE_PROFILES[role];
      expect(profile).toBeDefined();
      expect(profile.permissions.length).toBe(expectedCounts[role]);
      expect(profile.permissionCount).toBe(expectedCounts[role]);
      expect(profile.canReadInventoryCost).toBe(expectedCostRead[role]);
      expect(profile.permissions.includes('inventory.cost.read')).toBe(expectedCostRead[role]);

      const expectedDomain = ['SuperAdmin', 'BillingAdmin', 'SupportAdmin', 'SecurityAuditor'].includes(role)
        ? 'PLATFORM'
        : 'CUSTOMER_TENANT';
      expect(profile.securityDomain).toBe(expectedDomain);
      expect(isPlatformRole(role)).toBe(expectedDomain === 'PLATFORM');

      window.localStorage.setItem('role', role);
      setCurrentPermissions([...profile.permissions]);
      expect(canReadInventoryCost()).toBe(expectedCostRead[role]);

      // Mock adapter /api/v1/auth/me parity
      const meRes = resolveCanonicalMockResponse('get', '/api/v1/auth/me', undefined, undefined, {
        'x-mock-role': role,
      });
      expect(meRes.status).toBe(200);
      expect((meRes.data as Record<string, unknown>).role).toBe(role);
      expect((meRes.data as Record<string, unknown>).securityDomain).toBe(expectedDomain);

      // Mock adapter /api/v1/reports/owner-gross-profit cost guard parity
      const gpRes = resolveCanonicalMockResponse('get', '/api/v1/reports/owner-gross-profit', undefined, undefined, {
        'x-mock-role': role,
      });
      if (expectedCostRead[role]) {
        expect(gpRes.status).toBe(200);
      } else {
        expect(gpRes.status).toBe(403);
        expect(gpRes.errorCode).toBe('MISSING_INVENTORY_COST_READ');
      }

      // Mock adapter /api/v1/platform/dashboard domain isolation parity
      const platRes = resolveCanonicalMockResponse('get', '/api/v1/platform/dashboard', undefined, undefined, {
        'x-mock-role': role,
      });
      if (expectedDomain === 'PLATFORM') {
        expect(platRes.status).toBe(200);
      } else {
        expect(platRes.status).toBe(403);
        expect(platRes.errorCode).toBe('PLATFORM_DOMAIN_FORBIDDEN');
      }
    }

    // Warehouse scope enforcement: WH01-scoped roles get 404 on read and 403 on write to WH02
    for (const wh01OnlyRole of ['Manager', 'Supervisor', 'WarehouseStaff', 'Viewer'] as const) {
      const readWh2 = resolveCanonicalMockResponse('get', '/api/v1/inventory/current?warehouseId=2', undefined, undefined, {
        'x-mock-role': wh01OnlyRole,
      });
      expect(readWh2.status).toBe(404);
      expect(readWh2.errorCode).toBe('WAREHOUSE_OUT_OF_SCOPE');

      const writeWh2 = resolveCanonicalMockResponse('post', '/api/v1/importreceipts/1/express-post', undefined, { warehouseId: 2 }, {
        'x-mock-role': wh01OnlyRole,
      });
      expect(writeWh2.status).toBe(403);
      expect(writeWh2.errorCode).toBe('WAREHOUSE_SCOPE_FORBIDDEN');
    }
  });

  it('enforces SEC-W17-008 & FE-02 on /platform: redirects unauthenticated to /login, blocks all 6 Customer roles with 403 even with ?rolePreview=SuperAdmin, and differentiates all 4 Platform roles', async () => {
    // 1. Unauthenticated -> redirects to /login
    vi.mocked(apiClient.get).mockRejectedValueOnce({ response: { status: 401 } });
    const unauthView = render(
      <MemoryRouter initialEntries={['/platform']}>
        <Routes>
          <Route path="/login" element={<div data-testid="login-redirect-page">Trang đăng nhập</div>} />
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route index element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await unauthView.findByTestId('login-redirect-page')).toBeTruthy();
    cleanup();

    // 2. All 6 Customer Tenant roles -> 403 platform-domain-forbidden-alert even when URL has ?rolePreview=SuperAdmin
    for (const customerRole of CUSTOMER_ROLES) {
      vi.mocked(apiClient.get).mockImplementation(async (url) => {
        const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': customerRole });
        if (res.status >= 400) throw { response: { status: res.status, data: res.data } };
        return { data: res.data } as never;
      });

      const forbiddenView = render(
        <MemoryRouter initialEntries={['/platform?rolePreview=SuperAdmin']}>
          <Routes>
            <Route path="/platform" element={<PlatformControlCenterLayout />}>
              <Route index element={<PlatformControlCenter />} />
            </Route>
          </Routes>
        </MemoryRouter>,
      );

      const alert = await forbiddenView.findByTestId('platform-domain-forbidden-alert');
      expect(alert.textContent).toContain('403');
      expect(alert.textContent).toContain('PLATFORM_PERMISSION_REQUIRED');
      expect(forbiddenView.queryByTestId('platform-control-center-layout')).toBeNull();
      cleanup();
    }

    // 2b. SEC-W17-001 & SEC-W17-008: Customer Tenant user named 'qa_admin' MUST be rejected with 403 (zero username backdoor)
    vi.mocked(apiClient.get).mockResolvedValue({
      data: { username: 'qa_admin', role: 'Admin', securityDomain: 'CUSTOMER_TENANT' },
    } as never);
    const spoofedQaAdminView = render(
      <MemoryRouter initialEntries={['/platform']}>
        <Routes>
          <Route
            path="/platform/*"
            element={
              <PlatformControlCenterLayout>
                <PlatformControlCenter />
              </PlatformControlCenterLayout>
            }
          />
        </Routes>
      </MemoryRouter>,
    );
    const qaSpoofAlert = await spoofedQaAdminView.findByTestId('platform-domain-forbidden-alert');
    expect(qaSpoofAlert.textContent).toContain('403');
    expect(qaSpoofAlert.textContent).toContain('PLATFORM_PERMISSION_REQUIRED');
    expect(spoofedQaAdminView.queryByTestId('platform-control-center-layout')).toBeNull();
    cleanup();

    // 3. SuperAdmin -> Solo-Owner read-only badge + 1-step billing reconcile (0 4-eyes fields)
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'SuperAdmin' });
      return { data: res.data } as never;
    });
    const superView = render(
      <MemoryRouter initialEntries={['/platform/billing?rolePreview=Viewer']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await superView.findByTestId('platform-billing-invoices-table')).toBeTruthy();
    expect(superView.getByTestId('platform-role-readonly-badge').textContent).toContain('Chủ dự án · Toàn quyền nền tảng');
    expect(superView.getByTestId('platform-header').querySelectorAll('select').length).toBe(0);
    fireEvent.click(superView.getByTestId('btn-reconcile-saas-invoice-2'));
    expect(await superView.findByTestId('modal-confirm-invoice-reconcile')).toBeTruthy();
    expect(superView.queryByTestId('input-reconcile-approver')).toBeNull();
    cleanup();

    // 4. BillingAdmin -> 4-eyes approver field required
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'BillingAdmin' });
      return { data: res.data } as never;
    });
    const billingAdminView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await billingAdminView.findByTestId('platform-billing-invoices-table')).toBeTruthy();
    fireEvent.click(billingAdminView.getByTestId('btn-reconcile-saas-invoice-2'));
    expect(await billingAdminView.findByTestId('input-reconcile-approver')).toBeTruthy();
    cleanup();

    // 5. SupportAdmin -> Billing reconcile disabled + SEC-W17-009 sessionStorage-only impersonation state
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'SupportAdmin' });
      return { data: res.data } as never;
    });
    const supportAdminView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await supportAdminView.findByTestId('platform-billing-rbac-forbidden-banner')).toBeTruthy();
    expect((await supportAdminView.findByTestId('btn-reconcile-saas-invoice-2') as HTMLButtonElement).disabled).toBe(true);
    setActiveImpersonationState({
      sessionCode: 'IMP-SEC-009',
      impersonationToken: 'TOK-SESSION-ONLY',
      tenantId: 1,
      tenantCode: 'TENANT-001',
      tenantName: 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO',
      ticketCode: 'SUP-909',
      reason: 'Kiểm tra bảo mật lưu trữ phiên hỗ trợ',
      startedByUsername: 'qa_support_admin',
      startedAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date(Date.now() + 1800_000).toISOString(),
      readOnlyMode: true,
    });
    expect(window.localStorage.getItem(IMPERSONATION_STORAGE_KEY)).toBeNull();
    expect(window.sessionStorage.getItem(IMPERSONATION_STORAGE_KEY)).toContain('TOK-SESSION-ONLY');
    cleanup();

    // 6. SEC-W17-008 children mode (matching AppRoutes.tsx): activeRole & actorUsername propagate via PlatformIdentityContext
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'SupportAdmin' });
      return { data: res.data } as never;
    });
    const supportChildrenView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route
            path="/platform/*"
            element={
              <PlatformControlCenterLayout>
                <PlatformControlCenter />
              </PlatformControlCenterLayout>
            }
          />
        </Routes>
      </MemoryRouter>,
    );
    expect(await supportChildrenView.findByTestId('platform-billing-rbac-forbidden-banner')).toBeTruthy();
    expect(supportChildrenView.getByTestId('platform-active-role-badge').textContent).toBe('SupportAdmin');
    expect((await supportChildrenView.findByTestId('btn-reconcile-saas-invoice-2') as HTMLButtonElement).disabled).toBe(true);
    cleanup();

    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'BillingAdmin' });
      return { data: res.data } as never;
    });
    const billingChildrenView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route
            path="/platform/*"
            element={
              <PlatformControlCenterLayout>
                <PlatformControlCenter />
              </PlatformControlCenterLayout>
            }
          />
        </Routes>
      </MemoryRouter>,
    );
    expect(await billingChildrenView.findByTestId('platform-billing-invoices-table')).toBeTruthy();
    expect(billingChildrenView.getByTestId('platform-active-role-badge').textContent).toBe('BillingAdmin');
    fireEvent.click(billingChildrenView.getByTestId('btn-reconcile-saas-invoice-2'));
    expect(await billingChildrenView.findByTestId('input-reconcile-approver')).toBeTruthy();
    expect(billingChildrenView.getByTestId('reconcile-actor-readonly').textContent).toContain('Quản trị Cước phí SaaS');
    cleanup();
  });

  it('enforces SEC-W17-013 fail-closed inventory.cost.read across all 6 Customer roles in ProductBundleAndGiftSection and PackingKittingAndVideoTab', async () => {
    for (const role of CUSTOMER_ROLES) {
      const profile = CANONICAL_ROLE_PROFILES[role];
      window.localStorage.setItem('role', role);
      setCurrentPermissions([...profile.permissions]);

      vi.mocked(apiClient.get).mockImplementation(async (url) => {
        if (String(url).includes('/kit-work-orders')) {
          return {
            data: [
              {
                id: 1,
                workOrderCode: 'KWO-2026-001',
                workOrderType: 'Assemble',
                kitProductId: 101,
                kitProductCode: 'QA-KIT-01',
                kitProductName: 'Bộ Quà Tặng Cà Phê',
                baseUomCode: 'BO',
                conversionRateSnapshot: 1,
                plannedKitQty: 5,
                completedKitQty: 5,
                rolledUpUnitCost: 500000,
                status: 'Completed',
              },
            ],
          } as never;
        }
        return { data: [] } as never;
      });

      const bundleView = render(
        <MemoryRouter>
          <ProductBundleAndGiftSection
            activeTab="bundle-gift"
            products={[]}
            activeUomProduct={null}
            mutating={false}
            mutate={async (fn) => fn()}
            setNotice={() => undefined}
            setError={() => undefined}
          />
        </MemoryRouter>,
      );

      const table = await bundleView.findByTestId('kit-work-orders-table');
      await waitFor(() => expect(table.textContent).toContain('KWO-2026-001'));
      if (profile.canReadInventoryCost) {
        expect(table.textContent).toContain('500.000');
      } else {
        expect(table.textContent).toContain('Ẩn theo quyền');
        expect(table.textContent).not.toContain('500.000');
      }
      cleanup();

      const packingView = render(
        <MemoryRouter initialEntries={['/packing-sessions']}>
          <PackingKittingAndVideoTab
            active={true}
            sessions={[]}
            selected={null}
            huType="CARTON"
            busy=""
            governanceBusy={false}
            setGovernanceBusy={() => undefined}
            videoEvidences={[]}
            setVideoEvidences={() => undefined}
            setVideoNotice={() => undefined}
            setError={() => undefined}
          />
        </MemoryRouter>,
      );

      const costChip = await packingView.findByTestId('packing-kit-rolledup-cost');
      if (profile.canReadInventoryCost) {
        await waitFor(() => expect(costChip.textContent).toContain('500.000 ₫'));
      } else {
        expect(costChip.textContent).toContain('Ẩn theo quyền');
        expect(costChip.textContent).not.toContain('500.000');
      }
      cleanup();
    }
  });

  it('verifies FE-01 On-Demand VietQR lifecycle and ISO/IEC 18004 QR SVG against canonical mock data', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const res = resolveCanonicalMockResponse('get', String(url), undefined, undefined, { 'x-mock-role': 'Admin' });
      return { data: res.data } as never;
    });

    const view = render(
      <MemoryRouter>
        <SaasBilling />
      </MemoryRouter>,
    );

    expect(await screen.findByTestId('btn-generate-vietqr')).toBeTruthy();
    expect(screen.queryByTestId('vietqr-payment-panel')).toBeNull();
    expect(screen.queryByTestId('vietqr-qr-box')).toBeNull();
    expect(/\b1[45]:\d{2}\b/.test(view.container.textContent ?? '')).toBe(false);

    fireEvent.click(screen.getByTestId('btn-generate-vietqr'));
    expect(await screen.findByTestId('vietqr-payment-panel')).toBeTruthy();
    expect(screen.getByTestId('vietqr-account-number').textContent).toBe('98418102005');
    expect(screen.getByTestId('vietqr-account-holder').textContent).toBe('HUYNH PHI VAN');
    expect(screen.getByTestId('vietqr-amount-display').textContent).toContain('1.490.000 ₫');
    expect(screen.getByTestId('vietqr-vat-breakdown').textContent).toBe(
      'Trước thuế: 1.354.545 ₫ + Thuế GTGT 10%: 135.455 ₫',
    );
    expect(screen.getByTestId('vietqr-transfer-content').textContent).toBe('ERPKHOINVSAAS202611002');
    const qrSvg = screen.getByTestId('barcode-qr-svg');
    expect(Number(qrSvg.getAttribute('data-qr-size'))).toBeGreaterThanOrEqual(21);

    fireEvent.click(screen.getByTestId('btn-close-vietqr-panel'));
    expect(screen.queryByTestId('vietqr-payment-panel')).toBeNull();
    expect(screen.queryByTestId('vietqr-qr-box')).toBeNull();
  });

  it('QA-W17-R1-001 & QA-W17-R1-002: enforces honest 0 order fallback, hides unauthorized platform nav items, prevents BillingAdmin 403 /integrations/health fetch, and blocks direct /platform/integrations access with 403', async () => {
    // 1. QA-W17-R1-002: currentMonthOrderCount = 0 stays 0 (never fabricated to 6420)
    const mappedZero = mapRawPlatformTenants([
      { tenantId: 1, tenantCode: 'TENANT-001', companyName: 'CÔNG TY A', currentMonthOrderCount: 0, monthlyOrderQuota: 10000 },
    ]);
    expect(mappedZero[0]?.currentMonthOrderCount).toBe(0);

    // 2. QA-W17-R1-001: BillingAdmin on /platform/billing does NOT call /api/v1/platform/integrations/health and hides unauthorized nav links
    const requestedUrls: string[] = [];
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      const u = String(url);
      requestedUrls.push(u);
      const res = resolveCanonicalMockResponse('get', u, undefined, undefined, { 'x-mock-role': 'BillingAdmin' });
      return { data: res.data } as never;
    });

    const billingView = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="billing" element={<PlatformControlCenter />} />
            <Route path="integrations" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(await billingView.findByTestId('platform-billing-invoices-table')).toBeTruthy();
    expect(billingView.queryByTestId('nav-platform-integrations')).toBeNull();
    expect(billingView.queryByTestId('nav-platform-support')).toBeNull();
    expect(billingView.getByTestId('nav-platform-billing')).toBeTruthy();
    expect(requestedUrls.some((u) => u.includes('/platform/integrations/health'))).toBe(false);
    expect(billingView.queryByTestId('platform-action-notice')).toBeNull();
    cleanup();

    // 3. QA-W17-R1-001 & QA-W17-R1-002: BillingAdmin visiting /platform/integrations directly gets explicit 403 alert and zero hardcoded GHN ShopId/Token
    requestedUrls.length = 0;
    const directIntView = render(
      <MemoryRouter initialEntries={['/platform/integrations']}>
        <Routes>
          <Route path="/platform" element={<PlatformControlCenterLayout />}>
            <Route path="integrations" element={<PlatformControlCenter />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    const forbiddenAlert = await directIntView.findByTestId('platform-integrations-forbidden-alert');
    expect(forbiddenAlert.textContent).toContain('403');
    expect(forbiddenAlert.textContent).toContain('PLATFORM_PERMISSION_REQUIRED');
    expect(directIntView.queryByTestId('platform-integrations-card')).toBeNull();
    expect(directIntView.container.textContent).not.toContain('235512');
    expect(directIntView.container.textContent).not.toContain('0c06****12be');
    expect(requestedUrls.some((u) => u.includes('/platform/integrations/health'))).toBe(false);
  });
});
