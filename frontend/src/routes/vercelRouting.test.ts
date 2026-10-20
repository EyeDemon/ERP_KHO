import { describe, expect, it } from 'vitest';
import routingConfig from '../../vercel.json';

const vercelRoutes: {
  redirects?: Array<{ source: string; destination: string }>;
  rewrites?: Array<{ source: string; destination: string }>;
} = routingConfig;

describe('Vercel SPA entrypoint', () => {
  it('keeps / on the real operations dashboard', () => {
    expect(vercelRoutes.redirects?.some((route) => route.source === '/')).not.toBe(true);
    expect(vercelRoutes.rewrites).toContainEqual({
      source: '/(.*)',
      destination: '/index.html',
    });
  });
});
