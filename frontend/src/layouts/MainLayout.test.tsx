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
          <Route index element={<div>Production home</div>} />
          <Route path="system-blueprint/*" element={<div>Blueprint content</div>} />
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
    await view.findByText('Blueprint content');
    expect(view.getByText('DEMO / MOCK • READ ONLY')).toBeTruthy();
    expect(view.getByText('17 module groups')).toBeTruthy();
    expect(view.getByText('Nhập kho')).toBeTruthy();
    expect(view.getByText(/Inventory Control/)).toBeTruthy();
    expect(view.getByText('Global Search')).toBeTruthy();
    expect(view.getByText('Golden Scenario Lab')).toBeTruthy();
    expect(view.getByText('Mock Data Lab')).toBeTruthy();
  });


  it('runs blueprint routes without calling the real auth API', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('backend unavailable'));
    const view = renderAt('/system-blueprint');
    await view.findByText('Blueprint content');
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(view.queryByRole('alert')).toBeNull();
    expect(view.getByText('Blueprint runtime')).toBeTruthy();
  });

  it('switches demo persona and reports the simulated warehouse scope', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/system-blueprint/inbound');
    await view.findByText('Blueprint content');
    const selector = view.getByLabelText('Persona mô phỏng');
    fireEvent.change(selector, { target: { value: 'U-DN-MGR' } });
    expect((selector as HTMLSelectElement).value).toBe('U-DN-MGR');
    expect(view.getByText('1 kho scope')).toBeTruthy();
  });

  it('keeps production navigation separate from blueprint-only module menu', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Production home');
    expect(view.getByText('Bản đồ hệ thống')).toBeTruthy();
    expect(view.queryByText('17 module groups')).toBeNull();
    expect(view.queryByText('DEMO / MOCK • READ ONLY')).toBeNull();
  });

  it('keeps the planned WH-02 mock out of production navigation even when location.read is granted', async () => {
    const authorization = await import('../services/authorization');
    vi.mocked(authorization.hasPermission).mockImplementation(code => code === 'location.read');
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: ['location.read'] } });
    const view = renderAt('/');
    await view.findByText('Production home');
    expect(view.queryByRole('link', { name: 'Cấu trúc vị trí' })).toBeNull();
  });

  it('opens production UI routes on the Vercel blueprint demo without calling real auth', async () => {
    vi.mocked(isBlueprintDemoRuntime).mockReturnValue(true);
    vi.mocked(apiClient.get).mockRejectedValue(new Error('backend unavailable'));
    const view = renderAt('/');

    await view.findByText('Production home');
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(view.queryByRole('alert')).toBeNull();
    expect(view.getByText('DEMO RUNTIME • MOCK BACKEND')).toBeTruthy();
    expect(view.getByText('Frontend production UI')).toBeTruthy();
  });

  it('exposes accessible navigation, current location and skip-to-content behavior', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Production home');

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
    expect(view.queryByText('Production home')).toBeNull();
  });
});
