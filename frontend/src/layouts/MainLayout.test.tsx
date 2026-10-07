// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MainLayout from './MainLayout';
import apiClient from '../services/apiClient';
import { MockDemoProvider } from '../context/MockDemoContext';
import { isBlueprintDemoRuntime } from '../services/runtimeMode';

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
  isBlueprintDemoRuntime: vi.fn(() => false),
  blueprintDemoReadPermissions: ['warehouse.read'],
}));

const renderAt = (path: string) => render(
  <MemoryRouter initialEntries={[path]}>
    <MockDemoProvider>
      <Routes>
        <Route path="/" element={<MainLayout />}>
          <Route index element={<div>Trang chủ hệ thống thật</div>} />
          <Route path="system-blueprint/*" element={<div>Nội dung bản thiết kế</div>} />
        </Route>
      </Routes>
    </MockDemoProvider>
  </MemoryRouter>,
);

describe('MainLayout blueprint navigation mode', () => {
  beforeEach(() => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(false);
  });

  afterEach(() => {
    cleanup();
    vi.resetAllMocks();
  });

  it('shows all blueprint modules and explicit read-only demo state', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/system-blueprint/inbound');
    await view.findByText('Nội dung bản thiết kế');
    expect(view.getByText('MÔ PHỎNG • CHỈ ĐỌC')).toBeTruthy();
    expect(view.getByText('17 nhóm phân hệ')).toBeTruthy();
    expect(view.getByText('Nhập kho')).toBeTruthy();
    expect(view.getByText(/Kiểm soát tồn kho/)).toBeTruthy();
    expect(view.getByText('Tìm kiếm toàn hệ thống')).toBeTruthy();
    expect(view.getByText('Phòng kịch bản chuẩn')).toBeTruthy();
    expect(view.getByText('Phòng dữ liệu mô phỏng')).toBeTruthy();
  });


  it('runs blueprint routes without calling the real auth API', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('backend unavailable'));
    const view = renderAt('/system-blueprint');
    await view.findByText('Nội dung bản thiết kế');
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(view.queryByRole('alert')).toBeNull();
    expect(view.getByText('Môi trường bản thiết kế')).toBeTruthy();
  });

  it('switches demo persona and reports the simulated warehouse scope', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/system-blueprint/inbound');
    await view.findByText('Nội dung bản thiết kế');
    const selector = view.getByLabelText('Vai trò mô phỏng');
    fireEvent.change(selector, { target: { value: 'U-DN-MGR' } });
    expect((selector as HTMLSelectElement).value).toBe('U-DN-MGR');
    expect(view.getByText('1 kho trong phạm vi')).toBeTruthy();
  });

  it('keeps production navigation separate from blueprint-only module menu', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByText('Bản đồ hệ thống')).toBeTruthy();
    expect(view.queryByText('17 nhóm phân hệ')).toBeNull();
    expect(view.queryByText('MÔ PHỎNG • CHỈ ĐỌC')).toBeNull();
  });

  it('shows the real WH-02 production route when location.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'location.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['location.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Cấu trúc vị trí' }).getAttribute('href')).toBe('/warehouse-structure');
    expect(view.getByRole('link', { name: 'Bản đồ kho' }).getAttribute('href')).toBe('/warehouse-map');
  });

  it('shows the WH-05 production calendar when warehouse.read is granted', async () => {
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

  it('shows the WH-06 Dock & Yard production route with dock appointment read access', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'dock_appointment.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['dock_appointment.read'] } });
    const view = renderAt('/');
    await view.findByText('Trang chủ hệ thống thật');
    expect(view.getByRole('link', { name: 'Cổng, sân bãi & cửa kho' }).getAttribute('href')).toBe('/dock-yard');
  });

  it('opens production UI routes on the Vercel blueprint demo without calling real auth', async () => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(true);
    vi.mocked(apiClient.get).mockRejectedValue(new Error('backend unavailable'));
    const view = renderAt('/');

    await view.findByText('Trang chủ hệ thống thật');
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(view.queryByRole('alert')).toBeNull();
    expect(view.getByText('MÔI TRƯỜNG DEMO • BACKEND MÔ PHỎNG')).toBeTruthy();
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
