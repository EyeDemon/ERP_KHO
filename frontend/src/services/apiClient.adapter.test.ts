// @vitest-environment jsdom
import fs from 'node:fs';
import path from 'node:path';
import axios, { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

const originalAdapter = axios.defaults.adapter;
afterEach(() => {
  axios.defaults.adapter = originalAdapter;
  localStorage.clear();
  sessionStorage.clear();
  vi.resetModules();
});

describe('Axios adapter & SEC-W17-009 sessionStorage hardening', () => {
  it.each([403, 404, 409])('does not replay a denied %s mutation; only 403 refreshes permissions', async (status) => {
    vi.resetModules();
    const requests: InternalAxiosRequestConfig[] = [];
    const adapter: AxiosAdapter = async (config) => {
      requests.push(config);
      if (config.url === '/api/v1/auth/me') {
        return { config, data: { permissions: [] }, status: 200, statusText: 'OK', headers: {} };
      }
      throw new AxiosError('denied', undefined, config, undefined, {
        config,
        data: {},
        status,
        statusText: 'Denied',
        headers: {},
      });
    };
    axios.defaults.adapter = adapter;
    const { default: client, setAccessToken } = await import('./apiClient');
    const { setCurrentPermissions, currentPermissions } = await import('./authorization');
    setAccessToken('synthetic-access');
    setCurrentPermissions(['receipt.post']);
    await expect(
      client.post('/api/v1/receipts/1/post', {}, { headers: { 'Idempotency-Key': 'synthetic-command' } }),
    ).rejects.toMatchObject({ response: { status } });
    expect(requests.filter((r) => r.url === '/api/v1/receipts/1/post')).toHaveLength(1);
    expect(requests[0].headers.get('Authorization')).toBe('Bearer synthetic-access');
    expect(requests[0].headers.get('Idempotency-Key')).toBe('synthetic-command');
    expect(requests.filter((r) => r.url === '/api/v1/auth/me')).toHaveLength(status === 403 ? 1 : 0);
    expect(currentPermissions()).toEqual(status === 403 ? [] : ['receipt.post']);
  });

  it('refreshes a 401 once, preserves command key/payload, and stores renewed JWT only in sessionStorage', async () => {
    vi.resetModules();
    const requests: InternalAxiosRequestConfig[] = [];
    axios.defaults.adapter = async (config) => {
      requests.push(config);
      const ok = (data: unknown) => ({ config, data, status: 200, statusText: 'OK', headers: {} });
      if (config.url === '/api/v1/auth/refresh') return ok({ token: 'synthetic-renewed' });
      if (config.url === '/api/v1/auth/me') return ok({ permissions: ['receipt.post'] });
      if (requests.filter((r) => r.url === config.url).length === 1) {
        throw new AxiosError('expired', undefined, config, undefined, {
          config,
          data: {},
          status: 401,
          statusText: 'Unauthorized',
          headers: {},
        });
      }
      return ok({ completed: true });
    };
    const { default: client, setAccessToken } = await import('./apiClient');
    setAccessToken('synthetic-expired');
    await expect(
      client.post('/api/v1/receipts/1/post', { quantity: 3 }, { headers: { 'Idempotency-Key': 'synthetic-retry' } }),
    ).resolves.toMatchObject({ data: { completed: true } });
    expect(requests.map((r) => r.url)).toEqual([
      '/api/v1/receipts/1/post',
      '/api/v1/auth/refresh',
      '/api/v1/auth/me',
      '/api/v1/receipts/1/post',
    ]);
    expect(requests[3].headers.get('Authorization')).toBe('Bearer synthetic-renewed');
    expect(requests[3].headers.get('Idempotency-Key')).toBe('synthetic-retry');
    expect(requests[3].data).toBe('{"quantity":3}');
    expect(requests.every((r) => r.withCredentials)).toBe(true);
    expect(sessionStorage.getItem('wms_access_token')).toBe('synthetic-renewed');
    expect(localStorage.getItem('token')).toBeNull();
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('jwt')).toBeNull();
  });

  it('purges legacy localStorage JWT keys on setAccessToken and clears all impersonation/tenant keys on clearAuthentication', async () => {
    localStorage.setItem('token', 'legacy-jwt-1');
    localStorage.setItem('accessToken', 'legacy-jwt-2');
    localStorage.setItem('jwt', 'legacy-jwt-3');

    const { setAccessToken, clearAuthentication, getAccessToken } = await import('./apiClient');
    const { setCurrentPermissions, currentPermissions } = await import('./authorization');

    setAccessToken('tab-scoped-jwt');
    expect(getAccessToken()).toBe('tab-scoped-jwt');
    expect(sessionStorage.getItem('wms_access_token')).toBe('tab-scoped-jwt');
    expect(localStorage.getItem('token')).toBeNull();
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('jwt')).toBeNull();

    const keysToSeed = [
      'username',
      'role',
      'userId',
      'user',
      'tenantId',
      'tenantCode',
      'erp_active_impersonation_session',
      'erp_support_impersonation_session',
      'wms_active_support_session',
      'activeImpersonation',
      'erp_platform_active_role',
      'tenantLifecycleStatus',
      'tenantSuspensionReason',
    ];
    for (const k of keysToSeed) localStorage.setItem(k, 'seeded-value');
    sessionStorage.setItem('custom_tab_key', 'seeded');
    setCurrentPermissions(['inventory.read', 'inventory.cost.read']);

    clearAuthentication();

    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('wms_access_token')).toBeNull();
    expect(sessionStorage.getItem('custom_tab_key')).toBeNull();
    expect(currentPermissions()).toEqual([]);
    for (const k of keysToSeed) {
      expect(localStorage.getItem(k)).toBeNull();
    }
  });

  it('enforces static source hygiene on apiClient.ts, demoApiAdapter.ts, and single mock file rule', () => {
    const servicesDir = __dirname;
    const mocksDir = path.resolve(servicesDir, '../mocks');

    const apiClientSource = fs.readFileSync(path.join(servicesDir, 'apiClient.ts'), 'utf8');
    const apiClientLoc = apiClientSource.split(/\r?\n/).length;
    expect(apiClientLoc).toBeLessThanOrEqual(420);
    expect(apiClientSource).not.toMatch(/localStorage\??\.setItem\(\s*['"](?:token|accessToken|jwt)['"]/);
    expect(apiClientSource).not.toMatch(/localStorage\??\.getItem\(\s*['"](?:token|accessToken|jwt)['"]/);
    expect(apiClientSource).not.toMatch(/^\s*import\s+.*from\s+['"]\.\/demoApiAdapter['"]/m);

    const adapterSource = fs.readFileSync(path.join(servicesDir, 'demoApiAdapter.ts'), 'utf8');
    expect(adapterSource.split(/\r?\n/).length).toBeLessThanOrEqual(350);

    const mockFiles = fs.readdirSync(mocksDir).filter((f) => f.endsWith('.ts') || f.endsWith('.tsx'));
    expect(mockFiles).toEqual(['canonicalErpWmsMockData.ts']);
  });
});
