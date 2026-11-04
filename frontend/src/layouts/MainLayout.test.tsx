// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MainLayout from './MainLayout';
import apiClient from '../services/apiClient';
import { isReadOnlyDemoRuntime } from '../services/runtimeMode';

vi.mock('../services/apiClient', () => ({
  default: { get: vi.fn() },
  logout: vi.fn(),
}));

vi.mock('../services/authorization', () => ({
  usePermissionSet: vi.fn(),
  canViewApprovals: vi.fn(() => false),
  canViewStocktakes: vi.fn(() => false),
  hasPermission: vi.fn(() => false),
  beginPermissionRefresh: vi.fn(() => 1),
  setCurrentPermissions: vi.fn(),
}));

vi.mock('../services/runtimeMode', () => ({
  isReadOnlyDemoRuntime: vi.fn(() => false),
  readOnlyDemoPermissions: ['warehouse.read'],
}));

const renderAt = (path: string) => render(
  <MemoryRouter initialEntries={[path]}>
    <Routes>
      <Route path="/" element={<MainLayout />}>
        <Route index element={<div>Trang chủ hệ thống thật</div>} />
      </Route>
    </Routes>
  </MemoryRouter>,
);

describe('MainLayout production navigation', () => {
  beforeEach(() => {
    vi.mocked(isReadOnlyDemoRuntime).mockReturnValue(false);
  });

  afterEach(() => {
    cleanup();
    vi.resetAllMocks();
  });

  it('renders clean production navigation without mock menu items', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.queryByText('Bản đồ hệ thống')).toBeNull();
    expect(view.queryByText('17 nhóm phân hệ')).toBeNull();
    expect(view.queryByText('MÔ PHỎNG • CHỈ ĐỌC')).toBeNull();
  });

  it('renders 10 primary functional groups without count badges, navigates directly on 1-item groups, and opens wms-secondary-flyout on multi-item groups', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => ['purchase_order.read', 'asn.read', 'receipt.read'].includes(code));
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['purchase_order.read', 'asn.read', 'receipt.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');

    const primarySidebar = view.getByTestId('wms-primary-sidebar');
    expect(primarySidebar).toBeTruthy();
    expect(primarySidebar.querySelectorAll('.wms-primary-group-count').length).toBe(0);
    expect(view.getByTestId('nav-group-dieu-hanh')).toBeTruthy();
    expect(view.getByTestId('nav-group-nhap-kho')).toBeTruthy();
    expect(view.getByTestId('nav-group-xuat-kho')).toBeTruthy();
    expect(view.getByTestId('nav-group-giao-van')).toBeTruthy();
    expect(view.getByTestId('nav-group-hang-hoan')).toBeTruthy();
    expect(view.getByTestId('nav-group-ton-kho')).toBeTruthy();
    expect(view.getByTestId('nav-group-san-pham')).toBeTruthy();
    expect(view.getByTestId('nav-group-tai-chinh')).toBeTruthy();
    expect(view.getByTestId('nav-group-cau-truc-kho')).toBeTruthy();
    expect(view.getByTestId('nav-group-quan-tri-cong-ty')).toBeTruthy();

    fireEvent.mouseEnter(view.getByTestId('nav-group-hang-hoan'));
    const flyout = view.getByTestId('wms-secondary-flyout');
    expect(flyout.className).not.toContain('is-open');

    fireEvent.mouseEnter(view.getByTestId('nav-group-nhap-kho'));
    expect(flyout.className).toContain('is-open');
    expect(flyout.textContent).toContain('PHÂN HỆ: NHẬP KHO');
    expect(flyout.textContent).toContain('Đơn mua hàng');
    expect(flyout.textContent).toContain('Thông báo nhận hàng trước');
    expect(flyout.textContent).toContain('Phiếu nhập kho');

    fireEvent.mouseDown(document.body);
    expect(flyout.className).not.toContain('is-open');
  });

  it('shows the real warehouse structure and map routes when location.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'location.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['location.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Sơ đồ vị trí kệ' }).getAttribute('href')).toBe('/warehouse-structure');
    expect(view.getByRole('link', { name: 'Bản đồ nhiệt 2D' }).getAttribute('href')).toBe('/warehouse-map');
  });

  it('shows the production calendar when warehouse.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'warehouse.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['warehouse.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Lịch làm việc kho' }).getAttribute('href')).toBe('/warehouse-calendar');
  });

  it('shows Purchase Order and ASN work centers only with their exact read grants', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => ['purchase_order.read', 'asn.read'].includes(code));
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['purchase_order.read', 'asn.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Đơn mua hàng' }).getAttribute('href')).toBe('/purchase-orders');
    expect(view.getByRole('link', { name: 'Thông báo nhận hàng trước' }).getAttribute('href')).toBe('/asns');
    expect(view.queryByRole('link', { name: 'Phiếu nhập kho' })).toBeNull();
  });

  it('shows the Dock & Yard production route with dock appointment read access', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'dock_appointment.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['dock_appointment.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Điều phối bãi xe' }).getAttribute('href')).toBe('/dock-yard');
  });

  it('opens production UI routes on the Vercel read-only demo without calling real auth', async () => {
    vi.mocked(isReadOnlyDemoRuntime).mockReturnValue(true);
    vi.mocked(apiClient.get).mockRejectedValue(new Error('backend unavailable'));
    const view = renderAt('/');

    await view.findByText('Trang chủ hệ thống thật');
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(view.queryByRole('alert')).toBeNull();
    expect(view.getByText('MÔI TRƯỜNG MÔ PHỎNG • PHÍA MÁY CHỦ MÔ PHỎNG')).toBeTruthy();
    expect(view.getByText('Giao diện hệ thống thật')).toBeTruthy();
  });

  it('exposes accessible navigation, current location and skip-to-content behavior', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');

    expect(view.getByText('Bỏ qua điều hướng').getAttribute('href')).toBe('#main-content');
    expect(view.getByRole('navigation', { name: 'Điều hướng nghiệp vụ' })).toBeTruthy();
    expect(view.getByRole('link', { name: 'Tổng quan vận hành' }).getAttribute('aria-current')).toBe('page');

    const menuButton = view.getByRole('button', { name: 'Mở menu điều hướng' });
    expect(menuButton.getAttribute('aria-controls')).toBe('system-sidebar');
    expect(document.getElementById('system-sidebar')).toBeTruthy();
  });

  it('fails closed on production routes when identity verification fails', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('network'));
    const view = renderAt('/');
    await waitFor(() => expect(view.getByRole('alert')).toBeTruthy());
    expect(view.getByRole('alert').textContent).toContain('Không thể xác minh quyền truy cập');
    expect(view.queryByText('Trang chủ hệ thống thật')).toBeNull();
  });
});
