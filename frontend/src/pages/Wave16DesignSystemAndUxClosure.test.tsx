// @vitest-environment jsdom
import React, { useState } from 'react';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import apiClient from '../services/apiClient';
import { beginPermissionRefresh, setCurrentPermissions } from '../services/authorization';
import { resolveProductionPage } from '../config/productionNavigation';
import MainLayout from '../layouts/MainLayout';
import { PermissionsRoleMatrixSection } from '../modules/governance/PermissionsRoleMatrixSection';
import { PackingKittingBomSection, DEFAULT_KIT_ORDERS } from '../modules/outbound/PackingKittingBomSection';
import CostValuation from './CostValuation';
import { PlatformReconcileInvoiceModal } from '../modules/platform/PlatformReconcileInvoiceModal';
import { PlatformQuotaDrawerAndModals } from '../modules/platform/PlatformQuotaDrawerAndModals';
import { PlatformOverviewRecentEventsCard } from '../modules/platform/PlatformOverviewRecentEventsCard';
import PlatformControlCenterLayout from '../layouts/PlatformControlCenterLayout';
import PlatformControlCenter from './PlatformControlCenter';
import { PlatformAuditLogsTableCard } from '../modules/platform/PlatformAuditLogsTableCard';
import { DashboardQuickActions } from '../components/dashboard/DashboardQuickActions';
import { SaasBillingInvoiceHistoryCard } from '../modules/governance/SaasBillingPlanCardsAndHistory';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import { ImportReceiptListCard } from '../modules/inbound/ImportReceiptListSection';
import {
  WMS_GLOSSARY,
  UiErrorBanner,
  UiSkeleton,
  UiTableScroll,
  UiToolbar,
  UiToolbarField,
  UiTooltip,
} from '../ui/ProductionUi';

vi.mock('../services/apiClient', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
  logout: vi.fn(),
}));

describe('Wave-16 Design System Unification, WCAG & UX Closure', () => {
  afterEach(cleanup);

  beforeEach(() => {
    vi.clearAllMocks();
    const rev = beginPermissionRefresh();
    setCurrentPermissions(
      [
        'permission.read',
        'permission.assign',
        'role.read',
        'receipt.read',
        'putaway.read',
        'sales_order.read',
        'picking.read',
        'packing.read',
        'inventory.cost.read',
      ],
      rev,
    );
  });

  it('UIX-03 & UIX-18: resolves /permissions breadcrumb and renders unique role chips + module filter without duplicate role dropdown', () => {
    const meta = resolveProductionPage('/permissions');
    expect(meta?.section).toBe('Quản trị công ty');
    expect(meta?.label).toBe('Quản trị quyền truy cập');

    const onBatchGrant = vi.fn();
    const view = render(
      <PermissionsRoleMatrixSection
        catalog={[
          { code: 'receipt.read', description: 'Xem phiếu nhập' },
          { code: 'receipt.post', description: 'Ghi sổ phiếu nhập' },
          { code: 'inventory.cost.read', description: 'Xem giá vốn' },
          { code: 'platform.tenant.read', description: 'Quyền nền tảng' },
        ]}
        roles={[
          { id: 91, roleName: 'SuperAdmin', rowVersion: 'v1', permissions: ['receipt.read', 'platform.tenant.read'] },
          { id: 92, roleName: 'BillingAdmin', rowVersion: 'v1', permissions: ['platform.tenant.read'] },
          { id: 93, roleName: 'SupportAdmin', rowVersion: 'v1', permissions: ['platform.tenant.read'] },
          { id: 94, roleName: 'SecurityAuditor', rowVersion: 'v1', permissions: ['platform.tenant.read'] },
          { id: 99, roleName: 'PlatformSuperAdmin', rowVersion: 'v1', permissions: ['receipt.read'] },
          { id: 3, roleName: 'Accountant', rowVersion: 'v1', permissions: ['inventory.cost.read'] },
          { id: 1, roleName: 'Admin', rowVersion: 'v1', permissions: ['receipt.read', 'receipt.post', 'inventory.cost.read', 'platform.tenant.read'] },
          { id: 6, roleName: 'Viewer', rowVersion: 'v1', permissions: ['receipt.read'] },
          { id: 2, roleName: 'Manager', rowVersion: 'v1', permissions: ['receipt.read', 'inventory.cost.read'] },
          { id: 5, roleName: 'WarehouseStaff', rowVersion: 'v1', permissions: ['receipt.read'] },
          { id: 4, roleName: 'Supervisor', rowVersion: 'v1', permissions: ['receipt.read'] },
        ]}
        canReadRoles={true}
        canAssign={true}
        effectiveRoleFilter="Accountant"
        onChangeRoleFilter={vi.fn()}
        permissionSearch=""
        onChangePermissionSearch={vi.fn()}
        pending={false}
        onChangeGrant={vi.fn()}
        onBatchGrantModule={onBatchGrant}
      />,
    );

    const chipTabs = Array.from(
      screen.getByTestId('permissions-role-tabs').querySelectorAll('button[role="tab"]'),
    ).map((b) => b.textContent);
    expect(chipTabs).toEqual([
      'Quản trị công ty (3)',
      'Quản lý kho (2)',
      'Giám sát & QC (1)',
      'Kế toán kho (1)',
      'Nhân viên kho (1)',
      'Kiểm toán nội bộ (1)',
    ]);
    expect(new Set(chipTabs).size).toBe(chipTabs.length);
    expect(view.container.textContent).not.toContain('Vai trò khác');
    expect(view.container.textContent).not.toContain('SuperAdmin');
    expect(view.container.textContent).not.toContain('BillingAdmin');
    expect(view.container.textContent).not.toContain('SupportAdmin');
    expect(view.container.textContent).not.toContain('SecurityAuditor');
    expect(view.container.textContent).not.toContain('PlatformSuperAdmin');
    expect(view.container.textContent).not.toContain('platform.tenant.read');
    expect(screen.queryByLabelText('Lọc theo vai trò')).toBeNull();
    expect(screen.queryByTestId('select-permission-module')).toBeNull();

    const modulePill = screen.getByRole('button', { name: /^Nhập kho \(\d+\)$/ });
    fireEvent.click(modulePill);
    const batchBtn = screen.getByTestId('btn-batch-grant-module');
    expect(batchBtn.textContent).toContain('Cấp quyền nhóm đang chọn (2)');
    fireEvent.click(batchBtn);
    expect(onBatchGrant).toHaveBeenCalledWith(
      expect.objectContaining({ roleName: 'Accountant' }),
      'Nhập kho',
      ['receipt.read', 'receipt.post'],
    );
  });

  it('UIX-04, UIX-17 & UIX-22: supports keyboard Enter/ArrowDown/Escape flyout navigation, accessible icon buttons, and 5-tab mobile nav', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        userId: 1,
        username: 'qa_admin',
        role: 'Admin',
        permissions: ['receipt.read', 'putaway.read', 'sales_order.read', 'picking.read', 'permission.read'],
      },
    } as never);

    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));

    const view = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<MainLayout />}>
            <Route path="*" element={<div>Nội dung trang</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByText('Nội dung trang');

    const inboundBtn = screen.getByTestId('nav-group-nhap-kho');
    expect(inboundBtn.getAttribute('aria-haspopup')).toBe('true');
    expect(inboundBtn.getAttribute('aria-expanded')).toBe('false');

    inboundBtn.focus();
    fireEvent.keyDown(inboundBtn, { key: 'Enter' });

    await waitFor(() => {
      expect(inboundBtn.getAttribute('aria-expanded')).toBe('true');
    });

    const flyout = screen.getByTestId('wms-secondary-flyout');
    const flyoutLinks = Array.from(flyout.querySelectorAll<HTMLAnchorElement>('a.sidebar-nav-link'));
    expect(flyoutLinks.length).toBeGreaterThanOrEqual(2);
    await waitFor(() => {
      expect(document.activeElement).toBe(flyoutLinks[0]);
    });

    fireEvent.keyDown(flyout, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(flyoutLinks[1]);

    fireEvent.keyDown(flyout, { key: 'Escape' });
    expect(inboundBtn.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(inboundBtn);

    const allButtons = Array.from(view.container.querySelectorAll('button'));
    for (const btn of allButtons) {
      const accessibleText = (btn.textContent?.trim() || btn.getAttribute('aria-label')?.trim() || '').length;
      expect(accessibleText).toBeGreaterThan(0);
    }

    // Wave-18 UIX-04: Touch tap at 768x1024 & 1024x768 opens Level 2 Flyout without race-closing on synthetic mouseleave, and closes on outside pointerdown
    for (const tabletWidth of [768, 1024]) {
      Object.defineProperty(window, 'innerWidth', { value: tabletWidth, configurable: true, writable: true });
      window.dispatchEvent(new Event('resize'));
      fireEvent.pointerDown(inboundBtn, { pointerType: 'touch' });
      fireEvent.mouseEnter(inboundBtn);
      fireEvent.click(inboundBtn);
      fireEvent.mouseLeave(inboundBtn.closest('aside') as HTMLElement);
      expect(inboundBtn.getAttribute('aria-expanded')).toBe('true');
      expect(screen.getByTestId('wms-secondary-flyout').className).toContain('is-open');

      fireEvent.pointerDown(screen.getByText('Nội dung trang'), { pointerType: 'touch' });
      expect(inboundBtn.getAttribute('aria-expanded')).toBe('false');
    }

    Object.defineProperty(window, 'innerWidth', { value: 375, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));
    const bottomNav = await screen.findByRole('navigation', { name: 'Điều hướng tác nghiệp nhanh PDA' });
    expect(bottomNav.textContent).toContain('Tổng quan');
    expect(bottomNav.textContent).toContain('Nhận hàng');
    expect(bottomNav.textContent).toContain('Cất kệ');
    expect(bottomNav.textContent).toContain('Lấy hàng');
    expect(bottomNav.textContent).toContain('Menu');
  });

  it('UIX-05, UIX-07, UIX-15 & V-02: enforces Kit reversal reason >= 10 chars with MAC impact and MISA MST 10/13-digit + e-invoice preview modal', async () => {
    const onExecuteKit = vi.fn().mockResolvedValue(undefined);
    render(
      <PackingKittingBomSection
        kitOrders={DEFAULT_KIT_ORDERS}
        kittingVasMode="PRE_KIT"
        setKittingVasMode={vi.fn()}
        kitPlannedQty={5}
        setKitPlannedQty={vi.fn()}
        kitTargetBin="PICK-KIT-01"
        setKitTargetBin={vi.fn()}
        kitEarliestExpiry="31/12/2026"
        kitRolledUpCost={500000}
        canReadCost={true}
        busy=""
        governanceBusy={false}
        kittingVasResult=""
        onExecuteKittingVas={onExecuteKit}
      />,
    );

    expect(screen.getByTestId('kit-bom-preview-table')).toBeTruthy();
    expect(screen.queryByText(/Tổng giá vốn định mức 1 Bộ Kit/i)).toBeNull();
    fireEvent.click(screen.getByTestId('btn-kit-more-KWO-2026-001'));
    fireEvent.click(screen.getByTestId('menu-item-kit-reverse'));

    const kitModal = screen.getByTestId('modal-confirm-kit-reversal');
    expect(kitModal).toBeTruthy();
    expect(kitModal.getAttribute('aria-labelledby')).toBe('kit-reversal-modal-title');
    expect(screen.getByTestId('kit-reversal-impact-summary').textContent).toBe(
      'Đảo lệnh KWO-2026-001 sẽ trừ 20 Bộ Kit khỏi tồn khả dụng, hoàn trả linh kiện về kệ và ghi bút toán đảo giá vốn 10.000.000 ₫',
    );
    expect(screen.getByTestId('kit-reversal-mac-breakdown').textContent).toContain('500.000 ₫ / Bộ × 20 Bộ = 10.000.000 ₫');
    const confirmKitBtn = screen.getByTestId('btn-confirm-kit-reversal') as HTMLButtonElement;
    expect(confirmKitBtn.disabled).toBe(true);

    fireEvent.change(screen.getByTestId('input-kit-reversal-reason'), { target: { value: 'Ngắn' } });
    expect(screen.getByTestId('kit-reversal-char-counter').textContent).toBe('Còn thiếu 6 ký tự (Tối thiểu 10 ký tự)');
    expect(confirmKitBtn.disabled).toBe(true);

    fireEvent.change(screen.getByTestId('input-kit-reversal-reason'), {
      target: { value: 'Khách hủy đơn quà tặng doanh nghiệp cần xả bộ hoàn kệ' },
    });
    expect(confirmKitBtn.disabled).toBe(false);
    fireEvent.click(confirmKitBtn);
    await waitFor(() => {
      expect(onExecuteKit).toHaveBeenCalledWith('DEKIT_DISASSEMBLE');
    });

    vi.mocked(apiClient.get).mockResolvedValue({ data: { grossRevenue: 0, skuValuations: [] } } as never);
    vi.mocked(apiClient.post).mockResolvedValue({ data: { invoiceDraftCode: 'EINV-ND70-2026-777' } } as never);

    render(
      <MemoryRouter initialEntries={['/misa-exports']}>
        <Routes>
          <Route path="/misa-exports" element={<CostValuation />} />
        </Routes>
      </MemoryRouter>,
    );

    const einvBtn = await screen.findByTestId('btn-issue-einvoice-nd70');
    expect(einvBtn.className).toContain('ui-btn-secondary');

    fireEvent.change(screen.getByLabelText('Mã số thuế người mua HĐĐT'), { target: { value: '12345' } });
    fireEvent.click(einvBtn);
    expect(await screen.findByTestId('einvoice-mst-error')).toBeTruthy();
    expect(screen.queryByTestId('modal-confirm-einvoice-draft')).toBeNull();

    fireEvent.change(screen.getByLabelText('Mã số thuế người mua HĐĐT'), { target: { value: '0312345678-001' } });
    fireEvent.change(screen.getByLabelText('Tên đơn vị mua hàng HĐĐT'), { target: { value: 'Công ty Cổ phần Thương mại ABC' } });
    fireEvent.click(einvBtn);

    const draftModal = await screen.findByTestId('modal-confirm-einvoice-draft');
    expect(draftModal.textContent).toContain('0312345678-001');
    expect(draftModal.textContent).toContain('Công ty Cổ phần Thương mại ABC');
    fireEvent.click(screen.getByTestId('btn-confirm-einvoice-draft'));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith(
        '/api/v1/export-receipts/einvoice-nd70-draft',
        expect.objectContaining({
          buyerTaxCode: '0312345678-001',
          buyerName: 'Công ty Cổ phần Thương mại ABC',
        }),
      );
    });
  });

  it('V-02, UIX-11 & UIX-16: guards dirty Reconcile Modal & Quota Drawer dismissal, unifies "tài khoản nhân sự", and renders compact 375px scanner bar', () => {
    const onCloseReconcile = vi.fn();
    render(
      <PlatformReconcileInvoiceModal
        invoice={{ id: 10, invoiceCode: 'INV-SAAS-202611-002', amountVnd: 1490000, status: 'UNPAID' } as never}
        onClose={onCloseReconcile}
        onSubmitReconcile={vi.fn()}
      />,
    );

    expect(screen.getByTestId('reconcile-missing-fields-hint')).toBeTruthy();
    fireEvent.change(screen.getByTestId('input-reconcile-bank-ref'), { target: { value: 'FT26999111222' } });
    fireEvent.click(screen.getByTestId('btn-cancel-reconcile-modal'));
    expect(onCloseReconcile).not.toHaveBeenCalled();
    expect(screen.getByTestId('dirty-dismiss-confirm')).toBeTruthy();
    fireEvent.click(screen.getByTestId('btn-confirm-discard-reconcile'));
    expect(onCloseReconcile).toHaveBeenCalledTimes(1);

    const QuotaHarness: React.FC<{ onClose: () => void }> = ({ onClose }) => {
      const [reason, setReason] = useState('');
      return (
        <PlatformQuotaDrawerAndModals
          modalTenant={null}
          modalTargetStatus="SUSPENDED"
          confirmTenantCode=""
          setConfirmTenantCode={vi.fn()}
          suspendCategory=""
          setSuspendCategory={vi.fn()}
          suspendReason=""
          setSuspendReason={vi.fn()}
          submittingStatus={false}
          isSuspendConfirmValid={false}
          onCloseSuspendModal={vi.fn()}
          onConfirmStatusSubmit={vi.fn()}
          drawerTenant={{
            id: 1,
            tenantCode: 'TENANT-001',
            companyName: 'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO',
            planCode: 'GROWTH',
            lifecycleStatus: 'ACTIVE',
            maxWarehouses: 3,
            maxUsers: 20,
            monthlyOrderQuota: 10000,
          } as never}
          quotaWarehouses={3}
          setQuotaWarehouses={vi.fn()}
          quotaUsers={25}
          setQuotaUsers={vi.fn()}
          quotaOrders={10000}
          formattedQuotaOrders="10.000"
          handleQuotaOrdersChange={vi.fn()}
          quotaContractRef=""
          setQuotaContractRef={vi.fn()}
          quotaEffectiveUntil=""
          setQuotaEffectiveUntil={vi.fn()}
          quotaReason={reason}
          setQuotaReason={setReason}
          usedWh={1}
          usedUsr={6}
          usedOrd={1200}
          canWriteQuota={true}
          submittingQuota={false}
          hasQuotaChanged={true}
          isBelowUsage={false}
          exceedsGrowthTier={false}
          onCloseQuotaDrawer={onClose}
          onQuotaSubmit={vi.fn()}
        />
      );
    };

    const onCloseQuota = vi.fn();
    render(<QuotaHarness onClose={onCloseQuota} />);
    expect(screen.getByTestId('quota-old-new-diff').textContent).toContain('20 tài khoản nhân sự → 25 tài khoản nhân sự');
    expect(screen.getByTestId('quota-old-new-diff').textContent).not.toContain('người →');
    expect(screen.getByTestId('quota-delta-badge-wh').style.whiteSpace).toBe('nowrap');
    expect(screen.getByTestId('quota-delta-badge-usr').style.whiteSpace).toBe('nowrap');
    expect(screen.getByTestId('quota-delta-badge-ord').style.whiteSpace).toBe('nowrap');
    fireEvent.click(screen.getByTestId('btn-close-quota-drawer'));
    expect(onCloseQuota).not.toHaveBeenCalled();
    expect(screen.getByTestId('dirty-dismiss-confirm')).toBeTruthy();
    fireEvent.click(screen.getByTestId('btn-confirm-discard-quota'));
    expect(onCloseQuota).toHaveBeenCalledTimes(1);

    render(
      <SaasBillingInvoiceHistoryCard
        billing={{
          recentInvoices: [
            {
              invoiceCode: 'INV-SAAS-202611-001',
              planName: 'Gói Tăng trưởng (Growth)',
              billingPeriod: '11/2026',
              amountVnd: 1490000,
              transferContent: 'ERPKHO01202611',
              createdAtUtc: '2026-11-01T00:00:00Z',
              status: 'PAID',
            },
          ],
        } as never}
        currentCycleText="01/11/2026 – 30/11/2026 (Kỳ 11/2026)"
        nextCycle={{ dueDateText: '05/11/2026', shortRange: '01/11/2026 – 30/11/2026 (Kỳ 11/2026)' }}
      />,
    );
    expect(screen.getByTestId('saas-invoice-history-table').closest('.ui-table-scroll')).toBeTruthy();

    Object.defineProperty(window, 'innerWidth', { value: 375, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));
    render(
      <MobileBarcodeScannerBar
        resolveViaApi={false}
        stepLabels={['Bước 1: Quét vị trí kệ', 'Bước 2: Quét mã hàng', 'Bước 3: Xác nhận số lượng']}
        onScan={vi.fn()}
      />,
    );
    expect(screen.getByTestId('rf-scan-active-step-label').textContent).toBe('Bước 1/3: Quét vị trí kệ');
    expect(screen.getByTestId('wizard-step-pill-1').textContent).toBe('1');
    expect(screen.getByTestId('wizard-step-pill-2').textContent).toBe('2');
    expect(screen.getByTestId('wizard-step-pill-3').textContent).toBe('3');
    expect(screen.getByTestId('scanner-primary-actions-row').textContent).not.toContain('(Undo)');

    // Wave-18 FND-W18-03: Mobile Import Receipt Cards render formatted VND total vs RBAC masked state
    const sampleReceipt = {
      id: 501,
      code: 'PNK-202611-001',
      status: 'Approved',
      supplierName: 'Công ty Phân phối Cà phê Minh An',
      warehouseId: 1,
      warehouseName: 'Kho Trung tâm (WH01)',
      createdAt: '2026-11-01T08:00:00Z',
      totalAmount: 40500000,
      details: [{ id: 1, productId: 1, expectedQuantity: 100, receivedQuantity: 100, unitPrice: 405000 }],
    };
    const noopAsync = vi.fn().mockResolvedValue(undefined);
    const mobileReceiptsView = render(
      <ImportReceiptListCard
        receipts={[sampleReceipt as never]}
        warehouses={[{ id: 1, code: 'WH01', name: 'Kho Trung tâm' } as never]}
        selectedReceipt={null}
        listWarehouseFilter=""
        setListWarehouseFilter={vi.fn()}
        listStatusFilter=""
        setListStatusFilter={vi.fn()}
        listSearchFilter=""
        setListSearchFilter={vi.fn()}
        printLoadingId={null}
        workflowId={null}
        setWorkflowId={vi.fn()}
        approvingId={null}
        userId={1}
        canObserve={true}
        canCancel={true}
        canApprove={true}
        canApproveQc={true}
        canPost={true}
        canReceive={true}
        receivingDestLocation=""
        handleViewDetails={noopAsync}
        openPrintPreview={noopAsync}
        observeReceipt={noopAsync}
        handleCancel={noopAsync}
        handleApprove={noopAsync}
        runWorkflow={noopAsync}
        fetchData={noopAsync}
        setError={vi.fn()}
        setSuccessMsg={vi.fn()}
      />,
    );
    expect(mobileReceiptsView.getByTestId('import-receipt-mobile-card').textContent).toContain('PNK-202611-001');
    expect(mobileReceiptsView.getByTestId('import-receipt-mobile-meta').textContent).not.toContain('•');
    expect((mobileReceiptsView.getByTestId('import-receipt-mobile-meta') as HTMLElement).style.gap).toBe('6px 12px');
    expect(mobileReceiptsView.getByTestId('import-receipt-mobile-total').textContent).toContain('40.500.000');

    act(() => {
      const revNoCost = beginPermissionRefresh();
      setCurrentPermissions(['receipt.read'], revNoCost);
    });
    expect(mobileReceiptsView.getByTestId('import-receipt-mobile-total').textContent).toContain('Ẩn theo quyền');
  });

  it('Wave-19 V-01 & FND-W19-01: traps Tab/Shift+Tab focus inside modals, restores focus on Escape, wires skip-links, and places Trạng thái in Column 2', async () => {
    Object.defineProperty(window, 'innerWidth', { value: 1440, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));

    render(
      <PackingKittingBomSection
        kitOrders={DEFAULT_KIT_ORDERS}
        kittingVasMode="PRE_KIT"
        setKittingVasMode={vi.fn()}
        kitPlannedQty={5}
        setKitPlannedQty={vi.fn()}
        kitTargetBin="PICK-KIT-01"
        setKitTargetBin={vi.fn()}
        kitEarliestExpiry="31/12/2026"
        kitRolledUpCost={500000}
        canReadCost={true}
        busy=""
        governanceBusy={false}
        kittingVasResult=""
        onExecuteKittingVas={vi.fn()}
      />,
    );

    const kitHeaders = Array.from(
      screen.getByTestId('kit-work-orders-table').querySelectorAll('thead th'),
    ).map((th) => th.textContent?.trim() || '');
    expect(kitHeaders[1]).toContain('Trạng thái');

    const reverseTrigger = screen.getByTestId('btn-reverse-kit-KWO-2026-001');
    reverseTrigger.focus();
    fireEvent.click(reverseTrigger);

    const kitDialog = screen.getByTestId('modal-confirm-kit-reversal');
    const kitFocusable = Array.from(
      kitDialog.querySelectorAll<HTMLElement>('button:not([disabled]), textarea:not([disabled]), input:not([disabled])'),
    );
    expect(kitFocusable.length).toBeGreaterThanOrEqual(2);
    const firstKitEl = kitFocusable[0];
    const lastKitEl = kitFocusable[kitFocusable.length - 1];

    lastKitEl.focus();
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: false });
    expect(document.activeElement).toBe(firstKitEl);

    firstKitEl.focus();
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(lastKitEl);

    fireEvent.keyDown(document, { key: 'Escape' });
    await waitFor(() => {
      expect(screen.queryByTestId('modal-confirm-kit-reversal')).toBeNull();
    });
    expect(document.activeElement).toBe(reverseTrigger);

    const ReconcileTrapHarness: React.FC = () => {
      const [openInv, setOpenInv] = useState<Record<string, unknown> | null>(null);
      return (
        <div>
          <button
            type="button"
            data-testid="trigger-open-reconcile-trap"
            onClick={() => setOpenInv({ id: 10, invoiceCode: 'INV-SAAS-202611-002', amountVnd: 1490000, status: 'UNPAID' })}
          >
            Mở đối soát
          </button>
          <PlatformReconcileInvoiceModal
            invoice={openInv as never}
            onClose={() => setOpenInv(null)}
            onSubmitReconcile={vi.fn()}
          />
        </div>
      );
    };

    render(<ReconcileTrapHarness />);
    const reconcileTrigger = screen.getByTestId('trigger-open-reconcile-trap');
    reconcileTrigger.focus();
    fireEvent.click(reconcileTrigger);

    const reconcileModal = screen.getByTestId('modal-confirm-invoice-reconcile');
    const modalCard = reconcileModal.querySelector('.platform-modal-card') as HTMLElement;
    const modalFocusable = Array.from(
      modalCard.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]):not([type="hidden"]):not(.sr-only), select:not([disabled]), textarea:not([disabled])',
      ),
    );
    const firstModalEl = modalFocusable[0];
    const lastModalEl = modalFocusable[modalFocusable.length - 1];

    lastModalEl.focus();
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: false });
    expect(document.activeElement).toBe(firstModalEl);

    firstModalEl.focus();
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(lastModalEl);

    fireEvent.keyDown(document, { key: 'Escape' });
    await waitFor(() => {
      expect(screen.queryByTestId('modal-confirm-invoice-reconcile')).toBeNull();
    });
    expect(document.activeElement).toBe(reconcileTrigger);

    render(
      <PlatformOverviewRecentEventsCard
        rawDashboardEvents={[]}
        auditLogs={[]}
        invoices={[
          {
            id: 1,
            invoiceCode: 'INV-SAAS-202611-001',
            tenantId: 1,
            planCode: 'GROWTH',
            subTotalVnd: 1354545,
            vatRatePercent: 10,
            vatAmountVnd: 135455,
            amountVnd: 1490000,
            status: 'PAID',
            createdAtUtc: '2026-11-01T00:00:00Z',
          },
        ]}
        onViewAllAuditLogs={vi.fn()}
      />,
    );
    expect(screen.getByTestId('platform-recent-events-card')).toBeTruthy();
    const recentHeaders = Array.from(
      screen.getByTestId('platform-overview-recent-events-table').querySelectorAll('thead th'),
    ).map((th) => th.textContent?.trim() || '');
    expect(recentHeaders[1]).toBe('Trạng thái');

    vi.mocked(apiClient.get).mockResolvedValue({
      data: {
        userId: 99,
        username: 'platform_superadmin',
        role: 'SuperAdmin',
        permissions: ['platform.tenant.read', 'platform.billing.read'],
      },
    } as never);

    const platformLayoutView = render(
      <MemoryRouter initialEntries={['/platform']}>
        <Routes>
          <Route
            path="/platform"
            element={
              <PlatformControlCenterLayout>
                <div>Nội dung Quản trị Nền tảng</div>
              </PlatformControlCenterLayout>
            }
          />
        </Routes>
      </MemoryRouter>,
    );
    await screen.findByText('Nội dung Quản trị Nền tảng');
    const platformSkipLink = platformLayoutView.container.querySelector('a.skip-link') as HTMLAnchorElement;
    expect(platformSkipLink).toBeTruthy();
    expect(platformSkipLink.getAttribute('href')).toBe('#platform-main-content');
    expect(platformLayoutView.container.querySelector('main#platform-main-content')).toBeTruthy();
  });

  it('Wave A-E (A11Y-02, UX-05, A11Y-03, DS-02): verifies keyboard-accessible UiTableScroll, 13-term WMS_GLOSSARY + multi-modal UiTooltip, UiSkeleton, and UiErrorBanner', () => {
    const onRetry = vi.fn();
    render(
      <div>
        <UiTableScroll data-testid="wave-ae-scroll-default">
          <table><tbody><tr><td>SP-01</td></tr></tbody></table>
        </UiTableScroll>
        <UiTableScroll data-testid="wave-ae-scroll-custom" ariaLabel="Bảng tồn kho 9 chiều có thể cuộn ngang">
          <table><tbody><tr><td>SP-02</td></tr></tbody></table>
        </UiTableScroll>
        <UiTooltip term="GS1-128" data-testid="wave-ae-tooltip-gs1" />
        <UiSkeleton lines={3} data-testid="wave-ae-skeleton" />
        <UiErrorBanner
          title="Lỗi kết nối sổ cái tồn kho"
          message="Không thể truy xuất dữ liệu khóa dòng 9 chiều."
          errorCode="INV_LEDGER_UNAVAILABLE"
          correlationId="CORR-WAVE-AE-01"
          onRetry={onRetry}
          data-testid="wave-ae-error-banner"
        />
        <button type="button" data-testid="wave-ae-outside">Ngoài vùng</button>
      </div>,
    );

    const scrollDefault = screen.getByTestId('wave-ae-scroll-default');
    expect(scrollDefault.getAttribute('tabindex')).toBe('0');
    expect(scrollDefault.getAttribute('role')).toBe('region');
    expect(scrollDefault.getAttribute('aria-label')).toBe('Bảng dữ liệu có thể cuộn ngang (dùng phím mũi tên Trái/Phải để cuộn)');

    const scrollCustom = screen.getByTestId('wave-ae-scroll-custom');
    expect(scrollCustom.getAttribute('tabindex')).toBe('0');
    expect(scrollCustom.getAttribute('role')).toBe('region');
    expect(scrollCustom.getAttribute('aria-label')).toBe('Bảng tồn kho 9 chiều có thể cuộn ngang');

    const expectedTerms = ['FEFO', 'FIFO', 'BOM', 'WAC', 'AWB', 'GS1-128', 'RMA', 'MST', 'SoD', 'ĐVVC', 'ĐVT', 'SKU', 'Bin'] as const;
    expect(Object.keys(WMS_GLOSSARY)).toHaveLength(13);
    for (const termKey of expectedTerms) {
      expect(WMS_GLOSSARY[termKey]).toBeDefined();
      expect(WMS_GLOSSARY[termKey].term).toBe(termKey);
      expect(WMS_GLOSSARY[termKey].label.trim().length).toBeGreaterThan(0);
      expect(WMS_GLOSSARY[termKey].description.trim().length).toBeGreaterThan(0);
    }

    const tooltipTrigger = screen.getByRole('button', { name: 'GS1-128' });
    fireEvent.focus(tooltipTrigger);
    const tooltipBubble = screen.getByRole('tooltip');
    expect(tooltipBubble.textContent).toContain(WMS_GLOSSARY['GS1-128'].label);
    expect(tooltipTrigger.getAttribute('aria-describedby')).toBe(tooltipBubble.id);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('tooltip')).toBeNull();

    const tooltipWrap = screen.getByTestId('wave-ae-tooltip-gs1');
    fireEvent.mouseEnter(tooltipWrap);
    expect(screen.getByRole('tooltip')).toBeTruthy();
    fireEvent.mouseLeave(tooltipWrap);
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.pointerDown(tooltipTrigger);
    fireEvent.click(tooltipTrigger);
    expect(screen.getByRole('tooltip')).toBeTruthy();
    fireEvent.click(screen.getByTestId('wave-ae-outside'));
    expect(screen.queryByRole('tooltip')).toBeNull();

    const skeleton = screen.getByTestId('wave-ae-skeleton');
    expect(skeleton.getAttribute('role')).toBe('status');
    expect(skeleton.getAttribute('aria-busy')).toBe('true');
    expect(skeleton.querySelectorAll('.ui-skeleton-line')).toHaveLength(3);

    const errBanner = screen.getByTestId('wave-ae-error-banner');
    expect(errBanner.getAttribute('role')).toBe('alert');
    expect(errBanner.textContent).toContain('INV_LEDGER_UNAVAILABLE • CORR-WAVE-AE-01');
    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('Wave A-E (RES-03 / B2): collapses UiToolbar filters >= index 2 on mobile <= 768px, tracks active badge, preserves state across toggles, and supports Áp dụng / Xóa lọc', () => {
    const onApply = vi.fn();
    const onReset = vi.fn();

    const MobileFilterHarness: React.FC = () => {
      const [docType, setDocType] = useState('RECEIPT');
      const [reasonSearch, setReasonSearch] = useState('');
      return (
        <UiToolbar
          data-testid="wave-ae-mobile-toolbar"
          onApplyFilters={onApply}
          onResetFilters={() => {
            setDocType('');
            setReasonSearch('');
            onReset();
          }}
        >
          <UiToolbarField label="Kho"><select defaultValue="WH01"><option value="WH01">WH01</option></select></UiToolbarField>
          <UiToolbarField label="Mã phiếu"><input defaultValue="" placeholder="PNK-2026..." /></UiToolbarField>
          <UiToolbarField label="Loại chứng từ">
            <select data-testid="wave-ae-sec-doctype" value={docType} onChange={(e) => setDocType(e.target.value)}>
              <option value="">Tất cả</option>
              <option value="RECEIPT">Phiếu nhập</option>
              <option value="ISSUE">Phiếu xuất</option>
            </select>
          </UiToolbarField>
          <UiToolbarField label="Lý do điều chỉnh">
            <input data-testid="wave-ae-sec-reason" value={reasonSearch} onChange={(e) => setReasonSearch(e.target.value)} />
          </UiToolbarField>
          <UiToolbarField label="Ghi chú lô (không kiểm soát)">
            <input data-testid="wave-ae-sec-uncontrolled" defaultValue="" />
          </UiToolbarField>
          <div className="inventory-toolbar-actions" data-testid="wave-ae-inv-actions">
            <button type="button" data-testid="wave-ae-standalone-action">Làm mới</button>
            <button type="button">Xuất Excel Tồn Kho</button>
          </div>
        </UiToolbar>
      );
    };

    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));
    const view = render(<MobileFilterHarness />);
    expect(screen.queryByTestId('btn-toggle-toolbar-filters')).toBeNull();

    act(() => {
      Object.defineProperty(window, 'innerWidth', { value: 390, configurable: true, writable: true });
      window.dispatchEvent(new Event('resize'));
    });

    const toggleBtn = screen.getByTestId('btn-toggle-toolbar-filters');
    expect(toggleBtn.textContent).toContain('Bộ lọc (3)');
    expect((toggleBtn as HTMLButtonElement).style.minHeight).toBe('44px');
    expect(screen.getByTestId('toolbar-active-filter-badge').textContent).toBe('Đang lọc: 1');
    expect(screen.getByTestId('wave-ae-standalone-action')).toBeTruthy();
    expect(screen.getByTestId('wave-ae-inv-actions').closest('.ui-toolbar-primary-bar')).toBeTruthy();

    const secPanel = screen.getByTestId('ui-toolbar-secondary-panel');
    expect(secPanel.hidden).toBe(true);

    fireEvent.click(toggleBtn);
    expect(secPanel.hidden).toBe(false);
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');

    fireEvent.change(screen.getByTestId('wave-ae-sec-reason'), { target: { value: 'Kiểm kê lệch kệ' } });
    const uncontrolledInput = screen.getByTestId('wave-ae-sec-uncontrolled') as HTMLInputElement;
    uncontrolledInput.value = 'LOT-QA-99';
    fireEvent.input(uncontrolledInput);
    expect(screen.getByTestId('toolbar-active-filter-badge').textContent).toBe('Đang lọc: 3');

    // Toggle collapse/expand preserves controlled + uncontrolled state
    fireEvent.click(toggleBtn);
    expect(secPanel.hidden).toBe(true);
    fireEvent.click(toggleBtn);
    expect(secPanel.hidden).toBe(false);
    expect((screen.getByTestId('wave-ae-sec-reason') as HTMLInputElement).value).toBe('Kiểm kê lệch kệ');
    expect((screen.getByTestId('wave-ae-sec-uncontrolled') as HTMLInputElement).value).toBe('LOT-QA-99');

    // [Áp dụng] closes panel keeping state intact
    fireEvent.click(screen.getByTestId('btn-apply-toolbar-filters'));
    expect(onApply).toHaveBeenCalledTimes(1);
    expect(secPanel.hidden).toBe(true);
    expect(screen.getByTestId('toolbar-active-filter-badge').textContent).toBe('Đang lọc: 3');

    // [Xóa lọc] resets secondary filters + badge
    fireEvent.click(toggleBtn);
    fireEvent.click(screen.getByTestId('btn-reset-toolbar-filters'));
    expect(onReset).toHaveBeenCalledTimes(1);
    expect((screen.getByTestId('wave-ae-sec-doctype') as HTMLSelectElement).value).toBe('');
    expect((screen.getByTestId('wave-ae-sec-reason') as HTMLInputElement).value).toBe('');
    expect((screen.getByTestId('wave-ae-sec-uncontrolled') as HTMLInputElement).value).toBe('');
    expect(screen.queryByTestId('toolbar-active-filter-badge')).toBeNull();

    view.unmount();
    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true, writable: true });
    window.dispatchEvent(new Event('resize'));
  });

  it('QA-WAVE-AE-ERROR-STATE-503 & QA-WAVE-AE-REFLOW-320: surfaces HTTP 503 errorCode + correlationId on PlatformControlCenter and enforces 44px touch height on DashboardQuickActions', async () => {
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/api/v1/platform/billing/invoices') {
        throw {
          response: {
            status: 503,
            headers: { 'x-correlation-id': 'corr-wave-ae-plat-503' },
            data: {
              errorCode: 'PLATFORM_BILLING_UPSTREAM_TIMEOUT',
              message: 'Dịch vụ đối soát hóa đơn nền tảng đang tạm gián đoạn.',
            },
          },
        };
      }
      return { data: {} } as never;
    });

    const view = render(
      <MemoryRouter initialEntries={['/platform/billing']}>
        <Routes>
          <Route path="/platform/billing" element={<PlatformControlCenter />} />
        </Routes>
      </MemoryRouter>,
    );

    const alertNotice = await view.findByTestId('platform-action-notice');
    expect(alertNotice.getAttribute('role')).toBe('alert');
    expect(alertNotice.querySelector('.ui-error-banner-message')?.textContent).toBe(
      'Dịch vụ đối soát hóa đơn nền tảng đang tạm gián đoạn.',
    );
    expect(alertNotice.querySelector('.ui-error-banner-message')?.textContent).not.toContain(
      'PLATFORM_BILLING_UPSTREAM_TIMEOUT',
    );
    const techDetails = view.getByTestId('ui-error-banner-tech-details') as HTMLDetailsElement;
    expect(techDetails.open).toBe(false);
    expect(techDetails.textContent).toContain('PLATFORM_BILLING_UPSTREAM_TIMEOUT');
    expect(techDetails.textContent).toContain('corr-wave-ae-plat-503');
    const retryBtn = view.getByTestId('ui-error-banner-retry') as HTMLButtonElement;
    expect(retryBtn.style.minHeight).toBe('44px');
    // N-23: Revenue summary must show "—" rather than false-zero 0 ₫, and invoice table shows load error instead of empty state
    expect(view.getByTestId('platform-billing-revenue-summary').textContent).not.toContain('0 ₫');
    expect(view.getByTestId('platform-billing-load-error').textContent).toContain(
      'Không thể tải danh sách hóa đơn thuê bao SaaS',
    );
    expect(view.container.textContent).not.toContain('Chưa có hóa đơn thuê bao SaaS nào trên hệ thống.');
    view.unmount();

    // UX-04: PlatformAuditLogsTableCard filters auth login spam even when only auth logs exist and renders 100% Vietnamese
    const authSpamOnlyView = render(
      <PlatformAuditLogsTableCard
        auditLogs={[
          {
            id: 901,
            tenantId: 1,
            action: 'Authentication.LoginSucceeded',
            entityName: 'UserSession',
            entityId: 12,
            username: 'qa_admin',
            securityDomain: 'AUTH_SECURITY',
            timestamp: '2026-11-06T03:00:00Z',
          },
        ]}
      />,
    );
    expect(authSpamOnlyView.getByTestId('platform-audit-empty').textContent).toContain(
      'Chưa có sự kiện kiểm toán quản trị nền tảng nào được ghi nhận.',
    );
    expect(authSpamOnlyView.container.textContent).not.toContain('Authentication.LoginSucceeded');
    authSpamOnlyView.unmount();

    const opsAuditView = render(
      <PlatformAuditLogsTableCard
        auditLogs={[
          {
            id: 902,
            tenantId: 1,
            action: 'Platform.TenantQuotaUpdated',
            entityName: 'TenantSubscription',
            entityId: 1,
            username: 'superadmin_ops',
            securityDomain: 'CUSTOMER_TENANT',
            oldValues: '{"maxWarehouses":1,"monthlyOrderQuota":1500}',
            newValues: '{"maxWarehouses":3,"monthlyOrderQuota":10000,"reason":"Phụ lục HĐ B2B tháng 11"}',
            timestamp: '2026-11-06T03:10:00Z',
          },
          {
            id: 903,
            tenantId: 1,
            action: 'PLATFORM_ADMIN_AUTH_SUMMARY',
            actionLabelVi: 'Xác thực quản trị',
            entityName: 'UserSession',
            targetCodeVi: 'TENANT-001',
            actorDisplayVi: 'Chủ dự án (SuperAdmin)',
            summaryVi: 'Tổng hợp phiên xác thực quản trị hợp lệ trong ca vận hành',
            securityDomain: 'PLATFORM',
            securityDomainLabelVi: 'Quản trị nền tảng',
            timestamp: '2026-11-06T03:15:00Z',
          },
        ]}
      />,
    );
    const opsTableText = opsAuditView.getByTestId('platform-audit-table').textContent ?? '';
    expect(opsTableText).toContain('Doanh nghiệp thuê bao');
    expect(opsTableText).toContain('Điều chỉnh hạn mức thuê bao');
    expect(opsTableText).toContain('Kho tối đa: 3');
    expect(opsTableText).toContain('Xác thực quản trị');
    expect(opsTableText).toContain('Chủ dự án (SuperAdmin)');
    expect(opsTableText).not.toContain('CUSTOMER_TENANT');
    expect(opsTableText).not.toContain('UserSession');
    expect(opsTableText).not.toContain('{"maxWarehouses"');
    opsAuditView.unmount();

    const recentEvView = render(
      <PlatformOverviewRecentEventsCard
        rawDashboardEvents={[
          {
            id: 'AUD-101',
            timestampUtc: '2026-11-06T03:20:00Z',
            timestampVi: '06/11/2026 10:20',
            eventTypeCode: 'PLATFORM_BILLING_MANUAL_RECONCILE',
            eventLabelVi: 'Đối soát thanh toán thủ công',
            targetCode: 'INV-SAAS-202611-002',
            actorDisplay: 'Chủ dự án (SuperAdmin)',
            summaryVi: 'Đối soát hóa đơn kỳ 11/2026 khớp 1.490.000 ₫',
            tone: 'success',
          },
          {
            id: 'NOISE-999',
            action: 'Authentication.LoginSucceeded',
            entityName: 'UserSession',
            summaryVi: 'Login spam',
          },
        ]}
        auditLogs={[]}
        invoices={[]}
        onViewAllAuditLogs={() => undefined}
      />,
    );
    const recentEvText = recentEvView.getByTestId('platform-overview-recent-events-table').textContent ?? '';
    expect(recentEvText).toContain('Đối soát thanh toán thủ công');
    expect(recentEvText).toContain('INV-SAAS-202611-002');
    expect(recentEvText).toContain('Đã đối soát');
    expect(recentEvText).not.toContain('Authentication.LoginSucceeded');
    expect(recentEvText).not.toContain('UserSession');
    recentEvView.unmount();

    const quickView = render(
      <MemoryRouter>
        <DashboardQuickActions />
      </MemoryRouter>,
    );
    const codQuickLink = quickView.getByTestId('quick-action-cod-reconciliation') as HTMLAnchorElement;
    expect(codQuickLink.style.minHeight).toBe('44px');
    expect(codQuickLink.style.boxSizing).toBe('border-box');
  });
});
