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
import { SaasBillingInvoiceHistoryCard } from '../modules/governance/SaasBillingPlanCardsAndHistory';
import { MobileBarcodeScannerBar } from '../components/MobileBarcodeScannerBar';
import { ImportReceiptListCard } from '../modules/inbound/ImportReceiptListSection';

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
});
