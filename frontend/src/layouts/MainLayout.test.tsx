// @vitest-environment jsdom
import { cleanup, render, waitFor } from '@testing-library/react';
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

  it('shows the real warehouse structure and map routes when location.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'location.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['location.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Cấu trúc vị trí' }).getAttribute('href')).toBe('/warehouse-structure');
    expect(view.getByRole('link', { name: 'Bản đồ kho' }).getAttribute('href')).toBe('/warehouse-map');
  });

  it('shows the production calendar when warehouse.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'warehouse.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['warehouse.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Lịch & ca kho' }).getAttribute('href')).toBe('/warehouse-calendar');
  });

  it('shows Purchase Order and ASN work centers only with their exact read grants', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => ['purchase_order.read', 'asn.read'].includes(code));
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['purchase_order.read', 'asn.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Đơn mua (PO)' }).getAttribute('href')).toBe('/purchase-orders');
    expect(view.getByRole('link', { name: 'ASN dự kiến' }).getAttribute('href')).toBe('/asns');
    expect(view.queryByRole('link', { name: 'Phiếu nhập kho' })).toBeNull();
  });

  it('shows the Dock & Yard production route with dock appointment read access', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'dock_appointment.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['dock_appointment.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Cổng, sân bãi & cửa kho' }).getAttribute('href')).toBe('/dock-yard');
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
    expect(view.getByRole('link', { name: 'Tổng quan' }).getAttribute('aria-current')).toBe('page');

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
