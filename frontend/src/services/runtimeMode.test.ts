import { describe, expect, it } from 'vitest';
import { isReadOnlyDemoRuntime } from './runtimeMode';

describe('isReadOnlyDemoRuntime', () => {
  it('recognizes the stable and generated Vercel read-only demo hosts', () => {
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo.vercel.app')).toBe(true);
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo-e4ik4erc2-bayuuandree99-8132.vercel.app')).toBe(true);
    expect(isReadOnlyDemoRuntime('erp-wms-readonly-demo-git-feature-er-3b07ac-bayuuandree99-8132.vercel.app')).toBe(true);
  });

  it('does not bypass authentication on non-demo hosts', () => {
    expect(isReadOnlyDemoRuntime('erp-wms.example.com')).toBe(false);
    expect(isReadOnlyDemoRuntime('localhost')).toBe(false);
    expect(isReadOnlyDemoRuntime('other-project.vercel.app')).toBe(false);
  });
});
