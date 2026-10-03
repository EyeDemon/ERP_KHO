import { describe, expect, it } from 'vitest';
import { isBlueprintDemoRuntime } from './runtimeMode';

describe('isBlueprintDemoRuntime', () => {
  it('recognizes the stable and generated Vercel blueprint demo hosts', () => {
    expect(isBlueprintDemoRuntime('erp-wms-blueprint-demo.vercel.app')).toBe(true);
    expect(isBlueprintDemoRuntime('erp-wms-blueprint-demo-e4ik4erc2-bayuuandree99-8132.vercel.app')).toBe(true);
    expect(isBlueprintDemoRuntime('erp-wms-blueprint-demo-git-feature-er-3b07ac-bayuuandree99-8132.vercel.app')).toBe(true);
  });

  it('does not bypass authentication on non-demo hosts', () => {
    expect(isBlueprintDemoRuntime('erp-wms.example.com')).toBe(false);
    expect(isBlueprintDemoRuntime('localhost')).toBe(false);
    expect(isBlueprintDemoRuntime('other-project.vercel.app')).toBe(false);
  });
});
