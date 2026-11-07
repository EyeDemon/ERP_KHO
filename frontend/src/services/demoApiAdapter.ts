import type { InternalAxiosRequestConfig } from 'axios';
import { resolveCanonicalMockResponse } from '../mocks/canonicalErpWmsMockData';

const extractPlainHeaders = (config: InternalAxiosRequestConfig): Record<string, unknown> => {
  const rawHeaders = config.headers as unknown;
  if (!rawHeaders || typeof rawHeaders !== 'object') return {};
  if (typeof (rawHeaders as { toJSON?: () => Record<string, unknown> }).toJSON === 'function') {
    return (rawHeaders as { toJSON: () => Record<string, unknown> }).toJSON();
  }
  return { ...(rawHeaders as Record<string, unknown>) };
};

export const createReadOnlyDemoApiAdapter = (request: InternalAxiosRequestConfig) => {
  return async (config: InternalAxiosRequestConfig = request) => {
    const method = (config.method ?? 'get').toLowerCase();
    const url = config.url ?? '';
    const params = (config.params as Record<string, unknown> | undefined) ?? undefined;
    const headers = extractPlainHeaders(config);

    const resolution = resolveCanonicalMockResponse(method, url, params, config.data, headers);

    if (resolution.status >= 200 && resolution.status < 300) {
      return {
        data: resolution.data,
        status: resolution.status,
        statusText: 'OK',
        headers: {},
        config,
      };
    }

    const message =
      resolution.message ||
      (typeof resolution.data === 'object' && resolution.data !== null && 'message' in resolution.data
        ? String((resolution.data as { message?: unknown }).message ?? 'Request rejected')
        : 'Request rejected');

    return Promise.reject(
      Object.assign(new Error(message), {
        response: {
          status: resolution.status,
          data: resolution.data,
          headers: {},
          config,
        },
        config,
      }),
    );
  };
};
