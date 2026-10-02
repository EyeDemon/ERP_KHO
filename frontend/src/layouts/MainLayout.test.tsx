// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MainLayout from './MainLayout';
import apiClient from '../services/apiClient';
import { MockDemoProvider } from '../context/MockDemoContext';

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
    expect(view.getByText('Golden Scenario Lab')).toBeTruthy();
    expect(view.getByText('Mock Data Lab')).toBeTruthy();
  });

  it('keeps production navigation separate from blueprint-only module menu', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ data: { permissions: [] } });
    const view = renderAt('/');
    await view.findByText('Production home');
    expect(view.getByText('Bản đồ hệ thống')).toBeTruthy();
    expect(view.queryByText('17 module groups')).toBeNull();
    expect(view.queryByText('DEMO / MOCK • READ ONLY')).toBeNull();
  });

  it('fails closed when identity verification fails', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('network'));
    const view = renderAt('/system-blueprint');
    await waitFor(() => expect(view.getByRole('alert')).toBeTruthy());
    expect(view.getByRole('alert').textContent).toContain('Không thể xác minh quyền truy cập');
    expect(view.queryByText('Blueprint content')).toBeNull();
  });
});
