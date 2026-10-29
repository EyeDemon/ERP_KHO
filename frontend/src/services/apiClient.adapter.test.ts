// @vitest-environment jsdom
import axios, { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';

const originalAdapter = axios.defaults.adapter;
afterEach(() => { axios.defaults.adapter = originalAdapter; localStorage.clear(); vi.resetModules(); });

describe('Axios adapter upgrade compatibility', () => {
  it.each([403, 404, 409])('does not replay a denied %s mutation; only 403 refreshes permissions', async (status) => {
    vi.resetModules();
    const requests: InternalAxiosRequestConfig[] = [];
    const adapter: AxiosAdapter = async (config) => {
      requests.push(config);
      if (config.url === '/api/auth/me') return { config, data: { permissions: [] }, status: 200, statusText: 'OK', headers: {} };
      throw new AxiosError('denied', undefined, config, undefined, { config, data: {}, status, statusText: 'Denied', headers: {} });
    };
    axios.defaults.adapter = adapter;
    const { default: client, setAccessToken } = await import('./apiClient');
    const { setCurrentPermissions, currentPermissions } = await import('./authorization');
    setAccessToken('synthetic-access');
    setCurrentPermissions(['receipt.post']);
    await expect(client.post('/api/receipts/1/post', {}, { headers: { 'Idempotency-Key': 'synthetic-command' } })).rejects.toMatchObject({ response: { status } });
    expect(requests.filter(r => r.url === '/api/receipts/1/post')).toHaveLength(1);
    expect(requests[0].headers.get('Authorization')).toBe('Bearer synthetic-access');
    expect(requests[0].headers.get('Idempotency-Key')).toBe('synthetic-command');
    expect(requests.filter(r => r.url === '/api/auth/me')).toHaveLength(status === 403 ? 1 : 0);
    expect(currentPermissions()).toEqual(status === 403 ? [] : ['receipt.post']);
  });

  it('refreshes a 401 once and preserves the command key and payload on retry', async () => {
    vi.resetModules();
    const requests: InternalAxiosRequestConfig[] = [];
    axios.defaults.adapter = async (config) => {
      requests.push(config);
      const ok = (data: unknown) => ({ config, data, status: 200, statusText: 'OK', headers: {} });
      if (config.url === '/api/Auth/refresh') return ok({ token: 'synthetic-renewed' });
      if (config.url === '/api/auth/me') return ok({ permissions: ['receipt.post'] });
      if (requests.filter(r => r.url === config.url).length === 1)
        throw new AxiosError('expired', undefined, config, undefined, { config, data: {}, status: 401, statusText: 'Unauthorized', headers: {} });
      return ok({ completed: true });
    };
    const { default: client, setAccessToken } = await import('./apiClient');
    setAccessToken('synthetic-expired');
    await expect(client.post('/api/receipts/1/post', { quantity: 3 }, { headers: { 'Idempotency-Key': 'synthetic-retry' } })).resolves.toMatchObject({ data: { completed: true } });
    expect(requests.map(r => r.url)).toEqual(['/api/receipts/1/post', '/api/Auth/refresh', '/api/auth/me', '/api/receipts/1/post']);
    expect(requests[3].headers.get('Authorization')).toBe('Bearer synthetic-renewed');
    expect(requests[3].headers.get('Idempotency-Key')).toBe('synthetic-retry');
    expect(requests[3].data).toBe('{"quantity":3}');
    expect(requests.every(r => r.withCredentials)).toBe(true);
  });
});
