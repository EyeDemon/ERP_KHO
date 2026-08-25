import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';

const baseURL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7198';
let accessToken: string | null = null;
let isRedirecting = false;

type RetryConfig = InternalAxiosRequestConfig & { _authRetry?: boolean };

export const setAccessToken = (token: string | null): void => { accessToken = token; };
export const getAccessToken = (): string | null => accessToken;
export const resetRedirectGuard = (): void => { isRedirecting = false; };

export const shouldRedirectToLogin = (pathname: string, requestUrl?: string, redirecting = false): boolean => {
  if (redirecting || pathname === '/login' || pathname.endsWith('/login')) return false;
  return !(requestUrl?.includes('/api/Auth/login') || requestUrl?.includes('/api/Auth/refresh'));
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
      .then((response) => {
        const token = response.data.token as string;
        setAccessToken(token);
        return token;
      })
);

const clearAuthentication = (): void => {
  setAccessToken(null);
  localStorage.removeItem('username');
  localStorage.removeItem('role');
};

apiClient.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetryConfig | undefined;
    const requestUrl = config?.url;
    const isAuthEndpoint = requestUrl?.includes('/api/Auth/login') || requestUrl?.includes('/api/Auth/refresh');
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
