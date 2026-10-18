// @vitest-environment jsdom
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import apiClient from '../services/apiClient';
import Login, { parseTenantLoginInput } from './Login';

vi.mock('../services/apiClient', () => ({ default: { post: vi.fn(), get: vi.fn() }, setAccessToken: vi.fn() }));
afterEach(cleanup);

it('liên kết nhãn đăng nhập tiếng Việt và không hiển thị lỗi kỹ thuật', async () => {
  vi.mocked(apiClient.post).mockRejectedValue({ response: { status: 500, data: { message: 'SQL provider private detail' } } });
  const view = render(<MemoryRouter><Login /></MemoryRouter>);
  fireEvent.change(view.getByLabelText('Tên đăng nhập'), { target: { value: 'qa-user' } });
  const password = view.getByLabelText('Mật khẩu');
  expect(password.getAttribute('type')).toBe('password');
  expect(password.getAttribute('autocomplete')).toBe('current-password');
  fireEvent.change(password, { target: { value: 'QA_TEST_INPUT_ONLY' } });
  fireEvent.submit(view.getByRole('button', { name: 'Đăng nhập' }).closest('form')!);
  expect((await view.findByRole('alert')).textContent).toBe('Đăng nhập thất bại. Vui lòng kiểm tra thông tin và thử lại.');
  expect(view.queryByText('SQL provider private detail')).toBeNull();
});

it('hỗ trợ đăng nhập Multi-Tenant với Mã doanh nghiệp (Tenant) hoặc user@TENANT-202 và xóa cache phiên cũ khi đổi Tenant', async () => {
  expect(parseTenantLoginInput('admin@tenant-202', '')).toEqual({
    username: 'admin',
    tenantCode: 'TENANT-202',
  });
  expect(parseTenantLoginInput('TENANT-202\\admin', '')).toEqual({
    username: 'admin',
    tenantCode: 'TENANT-202',
  });

  localStorage.setItem('tenantId', '101');
  sessionStorage.setItem('cached_tenant_filter', '101');

  vi.mocked(apiClient.post).mockResolvedValue({
    data: {
      token: 'jwt-202',
      username: 'admin',
      role: 'Admin',
      userId: 2020,
      tenantId: 202,
      tenantCode: 'TENANT-202',
    },
  });
  vi.mocked(apiClient.get).mockResolvedValue({
    data: { permissions: ['warehouse.read'] },
  });

  const view = render(<MemoryRouter><Login /></MemoryRouter>);
  fireEvent.change(view.getByLabelText('Mã doanh nghiệp (Tenant)'), { target: { value: 'tenant-202' } });
  fireEvent.change(view.getByLabelText('Tên đăng nhập'), { target: { value: 'admin' } });
  fireEvent.change(view.getByLabelText('Mật khẩu'), { target: { value: 'Secret!202' } });
  fireEvent.submit(view.getByRole('button', { name: 'Đăng nhập' }).closest('form')!);

  await waitFor(() => {
    expect(apiClient.post).toHaveBeenCalledWith('/api/auth/login', {
      username: 'admin',
      password: 'Secret!202',
      tenantCode: 'TENANT-202',
    });
  });

  expect(localStorage.getItem('tenantId')).toBe('202');
  expect(localStorage.getItem('tenantCode')).toBe('TENANT-202');
  expect(sessionStorage.getItem('cached_tenant_filter')).toBeNull();
});
