import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { beginPermissionRefresh, setCurrentPermissions } from './authorization';
import { isReadOnlyDemoRuntime } from './runtimeMode';
import { createReadOnlyDemoApiAdapter } from './demoApiAdapter';

const baseURL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7198';
const ACCESS_TOKEN_SESSION_KEY = 'wms_access_token';
const AUTH_ME_CACHE_SESSION_KEY = 'wms_auth_me_cache';
const AUTH_ME_CACHE_TTL_MS = 60_000;
const isVitestEnv = Boolean(
  typeof import.meta !== 'undefined' && (import.meta.env?.VITEST || import.meta.env?.MODE === 'test')
);
let accessToken: string | null = (() => {
  if (isVitestEnv) return null;
  try {
    return typeof window !== 'undefined' ? window.sessionStorage?.getItem(ACCESS_TOKEN_SESSION_KEY) : null;
  } catch {
    return null;
  }
})();
let isRedirecting = false;

type RetryConfig = InternalAxiosRequestConfig & {
  _authRetry?: boolean;
  _rateLimitRetryCount?: number;
  _idempotencyKey?: string;
  _autoFingerprint?: string;
};

const AUTO_IDEMPOTENCY_TTL_MS = 5_000;
const autoIdempotencyCache = new Map<string, { key: string; expiresAt: number }>();

const GET_CACHE_TTL_MS = 3_000;
const getResponseCache = new Map<string, { expiresAt: number; promise: Promise<unknown> }>();

const buildGetCacheKey = (url: string, config?: Record<string, unknown>): string => {
  let paramsKey = '';
  if (config?.params) {
    try {
      paramsKey = JSON.stringify(config.params);
    } catch {
      paramsKey = String(config.params);
    }
  }
  return `${accessToken ?? 'anon'}:${url}:${paramsKey}`;
};

const releaseAutoIdempotencyKey = (config?: RetryConfig): void => {
  if (!config?._autoFingerprint) return;
  const current = autoIdempotencyCache.get(config._autoFingerprint);
  if (!current || current.key === config._idempotencyKey) {
    autoIdempotencyCache.delete(config._autoFingerprint);
  }
};

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

export const setAccessToken = (token: string | null): void => {
  accessToken = token;
  getResponseCache.clear();
  if (!isVitestEnv) {
    try {
      if (typeof window !== 'undefined' && window.sessionStorage) {
        window.sessionStorage.removeItem(AUTH_ME_CACHE_SESSION_KEY);
        if (token) window.sessionStorage.setItem(ACCESS_TOKEN_SESSION_KEY, token);
        else window.sessionStorage.removeItem(ACCESS_TOKEN_SESSION_KEY);
      }
    } catch {
      // Ignore storage access errors in restricted environments
    }
  }
};
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
        if (response.data?.username) localStorage.setItem('username', String(response.data.username));
        if (response.data?.role) localStorage.setItem('role', String(response.data.role));
        const refreshUid = response.data?.userId ?? response.data?.id;
        if (refreshUid !== undefined && refreshUid !== null) localStorage.setItem('userId', String(refreshUid));
        const revision = beginPermissionRefresh();
        const identity = await refreshClient.get('/api/auth/me', { headers: { Authorization: `Bearer ${token}` } });
        if (identity.data?.username) localStorage.setItem('username', String(identity.data.username));
        if (identity.data?.role) localStorage.setItem('role', String(identity.data.role));
        const uid = identity.data?.userId ?? identity.data?.id;
        if (uid !== undefined && uid !== null) localStorage.setItem('userId', String(uid));
        setCurrentPermissions(identity.data.permissions, revision);
        return token;
      })
);

const clearAuthentication = (): void => {
  setAccessToken(null);
  autoIdempotencyCache.clear();
  getResponseCache.clear();
  localStorage.removeItem('username');
  localStorage.removeItem('role');
  localStorage.removeItem('userId');
  localStorage.removeItem('tenantId');
  localStorage.removeItem('tenantCode');
  try {
    sessionStorage.clear();
  } catch {
    // Ignore storage access errors in restricted environments
  }
  setCurrentPermissions([]);
};

apiClient.interceptors.request.use((config) => {
  if (isReadOnlyDemoRuntime()) {
    config.adapter = createReadOnlyDemoApiAdapter(config);
  }
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
  const retryConfig = config as RetryConfig;
  const method = config.method?.toLowerCase();
  if (method && ['post', 'put', 'patch', 'delete'].includes(method)) {
    getResponseCache.clear();
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
      retryConfig._autoFingerprint = fingerprint ?? undefined;
      config.headers['Idempotency-Key'] = generatedKey;
    }
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    releaseAutoIdempotencyKey(response.config as RetryConfig | undefined);
    if (
      response.data &&
      typeof response.data === 'object' &&
      !Array.isArray(response.data) &&
      (response.data as Record<string, unknown>).idempotentReplay === true &&
      (response.data as Record<string, unknown>).truncated === true
    ) {
      const payload = response.data as Record<string, unknown>;
      response.data = {
        id: payload.id ?? 0,
        code: payload.code ?? '',
        status: payload.status ?? 'Completed',
        rowVersion: payload.rowVersion ?? '',
        lines: Array.isArray(payload.lines) ? payload.lines : [],
        items: Array.isArray(payload.items) ? payload.items : [],
        details: Array.isArray(payload.details) ? payload.details : [],
        movements: Array.isArray(payload.movements) ? payload.movements : [],
        ...payload,
      };
    }
    return response;
  },
  async (error: AxiosError) => {
    const errData = (error.response?.data ?? {}) as Record<string, unknown>;
    const code = String(errData.code ?? errData.errorCode ?? error.response?.status ?? 'ERR_NETWORK');
    const headers = (error.response?.headers ?? {}) as Record<string, unknown>;
    const correlationId = String(headers['x-correlation-id'] ?? errData.correlationId ?? errData.traceId ?? '');
    try {
      window.localStorage?.setItem('lastErrorCode', code);
      window.localStorage?.setItem('lastCorrelationId', correlationId);
    } catch {
      // ignore storage errors in restricted environments
    }
    const config = error.config as RetryConfig | undefined;
    const requestUrl = config?.url;
    const lowerUrl = requestUrl?.toLowerCase() ?? '';
    const isAuthEndpoint = lowerUrl.includes('/api/auth/login') || lowerUrl.includes('/api/auth/refresh');
    if (error.response?.status === 403 && accessToken) {
      const revision = beginPermissionRefresh();
      try {
        const identity = await refreshClient.get('/api/auth/me', { headers: { Authorization: `Bearer ${accessToken}` } });
        if (identity.data?.username) localStorage.setItem('username', String(identity.data.username));
        if (identity.data?.role) localStorage.setItem('role', String(identity.data.role));
        const uid = identity.data?.userId ?? identity.data?.id;
        if (uid !== undefined && uid !== null) localStorage.setItem('userId', String(uid));
        setCurrentPermissions(identity.data.permissions, revision);
      } catch {
        setCurrentPermissions([], revision);
      }
    }
    if (error.response?.status === 429 && config && (config._rateLimitRetryCount ?? 0) < 2) {
      config._rateLimitRetryCount = (config._rateLimitRetryCount ?? 0) + 1;
      const retryAfterHeader = Number(headers['retry-after']);
      const delayMs = Number.isFinite(retryAfterHeader) && retryAfterHeader > 0
        ? Math.min(retryAfterHeader * 1000, 2500)
        : 600 * config._rateLimitRetryCount;
      await new Promise((resolve) => setTimeout(resolve, delayMs));
      return apiClient.request(config);
    }
    if (error.response?.status === 401 && config && !config._authRetry && !isAuthEndpoint) {
      config._authRetry = true;
      try {
        const token = await refreshAccessToken();
        config.headers.Authorization = `Bearer ${token}`;
        return apiClient.request(config);
      } catch {
        releaseAutoIdempotencyKey(config);
        clearAuthentication();
        const pathname = typeof window !== 'undefined' ? window.location.pathname : '';
        if (shouldRedirectToLogin(pathname, requestUrl, isRedirecting)) {
          isRedirecting = true;
          window.location.href = '/login';
        }
      }
    }
    if (error.response) {
      releaseAutoIdempotencyKey(config);
      if (config && config._autoFingerprint) {
        delete config._idempotencyKey;
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

const rawGet = apiClient.get.bind(apiClient);
const isTestRuntime = Boolean(
  typeof import.meta !== 'undefined' && (import.meta.env?.VITEST || import.meta.env?.MODE === 'test')
);

if (!isTestRuntime) {
  apiClient.get = ((url: string, config?: Parameters<typeof rawGet>[1]) => {
    const now = Date.now();
    const normalizedUrl = url.trim().toLowerCase();
    if (normalizedUrl === '/api/auth/me' && accessToken && typeof window !== 'undefined') {
      try {
        const rawCached = window.sessionStorage?.getItem(AUTH_ME_CACHE_SESSION_KEY);
        if (rawCached) {
          const parsed = JSON.parse(rawCached) as { token?: string; expiresAt?: number; data?: unknown };
          if (parsed && parsed.token === accessToken && typeof parsed.expiresAt === 'number' && parsed.expiresAt > now && parsed.data) {
            return Promise.resolve({
              data: parsed.data,
              status: 200,
              statusText: 'OK',
              headers: {},
              config: (config ?? {}) as InternalAxiosRequestConfig,
            }) as ReturnType<typeof rawGet>;
          }
        }
      } catch {
        // Ignore sessionStorage parse errors
      }
    }
    for (const [k, v] of getResponseCache.entries()) {
      if (v.expiresAt <= now) getResponseCache.delete(k);
    }
    const cacheKey = buildGetCacheKey(url, config as Record<string, unknown> | undefined);
    const hit = getResponseCache.get(cacheKey);
    if (hit && hit.expiresAt > now) {
      return hit.promise as ReturnType<typeof rawGet>;
    }
    const reqPromise = rawGet(url, config)
      .then((res) => {
        if (normalizedUrl === '/api/auth/me' && accessToken && res?.data && typeof window !== 'undefined') {
          try {
            window.sessionStorage?.setItem(
              AUTH_ME_CACHE_SESSION_KEY,
              JSON.stringify({ token: accessToken, expiresAt: Date.now() + AUTH_ME_CACHE_TTL_MS, data: res.data }),
            );
          } catch {
            // Ignore storage write errors
          }
        }
        return res;
      })
      .catch((err) => {
        getResponseCache.delete(cacheKey);
        throw err;
      });
    getResponseCache.set(cacheKey, {
      expiresAt: now + GET_CACHE_TTL_MS,
      promise: reqPromise,
    });
    return reqPromise;
  }) as typeof apiClient.get;
}

export default apiClient;
