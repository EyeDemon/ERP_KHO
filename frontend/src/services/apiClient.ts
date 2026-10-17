import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beginPermissionRefresh, setCurrentPermissions } from './authorization';
import { isBlueprintDemoRuntime } from './runtimeMode';
import { createBlueprintDemoApiAdapter } from './demoApiAdapter';

const baseURL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7198';
let accessToken: string | null = null;
let isRedirecting = false;

type RetryConfig = InternalAxiosRequestConfig & { _authRetry?: boolean; _idempotencyKey?: string };

const AUTO_IDEMPOTENCY_TTL_MS = 5_000;
const autoIdempotencyCache = new Map<string, { key: string; expiresAt: number }>();

const buildRequestFingerprint = (method: string, config: Record<string, unknown>): string | null => {
  const url = typeof config.url === 'string' ? config.url.trim() : '';
  if (!url) return null;
  let serializedData = '';
  if (config.data !== undefined && config.data !== null) {
    if (typeof config.data === 'string') {
      serializedData = config.data;
    } else {
      try {
        serializedData = JSON.stringify(config.data);
      } catch {
        serializedData = String(config.data);
      }
    }
  }
  return `${method}:${url}:${serializedData}`;
};

export const setAccessToken = (token: string | null): void => { accessToken = token; };
export const getAccessToken = (): string | null => accessToken;
export const resetRedirectGuard = (): void => { isRedirecting = false; };

export const shouldRedirectToLogin = (pathname: string, requestUrl?: string, redirecting = false): boolean => {
  if (redirecting || pathname === '/login' || pathname.endsWith('/login')) return false;
  const lowerUrl = requestUrl?.toLowerCase() ?? '';
  return !(lowerUrl.includes('/api/auth/login') || lowerUrl.includes('/api/auth/refresh'));
};

const apiClient = axios.create({ baseURL, withCredentials: true, headers: { 'Content-Type': 'application/json' } });
const refreshClient = axios.create({ baseURL, withCredentials: true });

export const createSingleFlight = <T>(operation: () => Promise<T>): (() => Promise<T>) => {
  let pending: Promise<T> | null = null;
  return () => {
    if (!pending) pending = operation().finally(() => { pending = null; });
    return pending;
  };
};

const refreshAccessToken = createSingleFlight<string>(() =>
  refreshClient.post('/api/Auth/refresh')
      .then(async (response) => {
        const token = response.data.token as string;
        setAccessToken(token);
        const revision = beginPermissionRefresh();
        const identity = await refreshClient.get('/api/auth/me', { headers: { Authorization: `Bearer ${token}` } });
        setCurrentPermissions(identity.data.permissions, revision);
        return token;
      })
);

const clearAuthentication = (): void => {
  setAccessToken(null);
  localStorage.removeItem('username');
  localStorage.removeItem('role');
  localStorage.removeItem('userId');
  setCurrentPermissions([]);
};

apiClient.interceptors.request.use((config) => {
  if (isBlueprintDemoRuntime()) {
    config.adapter = createBlueprintDemoApiAdapter(config);
  }
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
  const retryConfig = config as RetryConfig;
  const method = config.method?.toLowerCase();
  if (method && ['post', 'put', 'patch', 'delete'].includes(method)) {
    const existingKey =
      config.headers?.['Idempotency-Key'] ??
      config.headers?.['idempotency-key'] ??
      retryConfig._idempotencyKey;
    if (existingKey) {
      const resolvedKey = String(existingKey);
      retryConfig._idempotencyKey = resolvedKey;
      if (config.headers) {
        config.headers['Idempotency-Key'] = resolvedKey;
      }
    } else if (config.headers) {
      const now = Date.now();
      for (const [fp, entry] of autoIdempotencyCache.entries()) {
        if (entry.expiresAt <= now) autoIdempotencyCache.delete(fp);
      }
      const fingerprint = buildRequestFingerprint(method, config as unknown as Record<string, unknown>);
      const cached = fingerprint ? autoIdempotencyCache.get(fingerprint) : undefined;
      let generatedKey: string;
      if (cached && cached.expiresAt > now) {
        generatedKey = cached.key;
      } else {
        const randomPart =
          typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
            ? crypto.randomUUID()
            : `${now.toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
        generatedKey = `auto-${method}-${randomPart}`;
        if (fingerprint) {
          autoIdempotencyCache.set(fingerprint, {
            key: generatedKey,
            expiresAt: now + AUTO_IDEMPOTENCY_TTL_MS,
          });
        }
      }
      retryConfig._idempotencyKey = generatedKey;
      config.headers['Idempotency-Key'] = generatedKey;
    }
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetryConfig | undefined;
    const requestUrl = config?.url;
    const lowerUrl = requestUrl?.toLowerCase() ?? '';
    const isAuthEndpoint = lowerUrl.includes('/api/auth/login') || lowerUrl.includes('/api/auth/refresh');
    if (error.response?.status === 403 && accessToken) {
      const revision = beginPermissionRefresh();
      try {
        const identity = await refreshClient.get('/api/auth/me', { headers: { Authorization: `Bearer ${accessToken}` } });
        setCurrentPermissions(identity.data.permissions, revision);
      } catch {
        setCurrentPermissions([], revision);
      }
    }
    if (error.response?.status === 401 && config && !config._authRetry && !isAuthEndpoint) {
      config._authRetry = true;
      try {
        const token = await refreshAccessToken();
        config.headers.Authorization = `Bearer ${token}`;
        return apiClient.request(config);
      } catch {
        clearAuthentication();
        const pathname = typeof window !== 'undefined' ? window.location.pathname : '';
        if (shouldRedirectToLogin(pathname, requestUrl, isRedirecting)) {
          isRedirecting = true;
          window.location.href = '/login';
        }
      }
    }
    return Promise.reject(error);
  }
);

export const logout = async (): Promise<void> => {
  try { await apiClient.post('/api/Auth/logout'); } finally {
    clearAuthentication();
    window.location.href = '/login';
  }
};

export const logoutAll = async (): Promise<void> => {
  try { await apiClient.post('/api/Auth/logout-all'); } finally {
    clearAuthentication();
    window.location.href = '/login';
  }
};

export default apiClient;
