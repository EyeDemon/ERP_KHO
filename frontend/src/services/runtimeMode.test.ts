import { afterEach, describe, expect, it, vi } from 'vitest';
import { isPublicTenantOnboardingEndpoint, isReadOnlyDemoRuntime } from './runtimeMode';

describe('isReadOnlyDemoRuntime & SEC-W17-009 production guard', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('recognizes the stable and generated Vercel read-only demo hosts in non-prod/test mode', () => {
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo.vercel.app')).toBe(true);
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo-e4ik4erc2-bayuuandree99-8132.vercel.app')).toBe(true);
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo-git-feature-er-3b07ac-bayuuandree99-8132.vercel.app')).toBe(true);
  });

  it('fails closed in production builds so demo hostnames cannot bypass real authentication', () => {
    vi.stubEnv('PROD', true);
    vi.stubEnv('VITEST', '');
    vi.stubEnv('MODE', 'production');
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo.vercel.app')).toBe(false);
  });

  it('does not bypass authentication on non-demo hosts', () => {
    expect(isReadOnlyDemoRuntime('erp-wms.example.com')).toBe(false);
    expect(isReadOnlyDemoRuntime('localhost')).toBe(false);
    expect(isReadOnlyDemoRuntime('other-project.vercel.app')).toBe(false);
  });

  it('recognizes only public tenant onboarding route', () => {
    expect(isPublicTenantOnboardingEndpoint('/api/v1/auth/register-tenant')).toBe(true);
    expect(isPublicTenantOnboardingEndpoint('/api/v1/platform/tenants')).toBe(false);
  });
});
