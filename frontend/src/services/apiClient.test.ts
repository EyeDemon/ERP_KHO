import { shouldRedirectToLogin } from './apiClient';
import { describe, expect, it } from 'vitest';

/**
 * Deterministic unit test suite for Axios 401 redirect guard logic.
 */
export function runApiClientRedirectGuardTests(): { passed: boolean; details: string[] } {
  const details: string[] = [];

  function assert(condition: boolean, testName: string) {
    if (!condition) {
      throw new Error(`Test failed: ${testName}`);
    }
    details.push(`PASS: ${testName}`);
  }

  // 1. Normal 401 on standard page and endpoint -> redirects
  assert(
    shouldRedirectToLogin('/dashboard', '/api/Products', false) === true,
    'Ordinary 401 on /dashboard with /api/Products endpoint redirects to login'
  );

  // 2. 401 when browser is already on /login -> does not redirect
  assert(
    shouldRedirectToLogin('/login', '/api/Products', false) === false,
    '401 when browser is already on /login does not redirect'
  );

  // 3. 401 when failed request is login endpoint -> does not redirect
  assert(
    shouldRedirectToLogin('/login', '/api/Auth/login', false) === false,
    '401 when request is /api/Auth/login does not redirect'
  );

  assert(
    shouldRedirectToLogin('/dashboard', '/api/Auth/login', false) === false,
    '401 on login endpoint even from /dashboard does not redirect'
  );

  // 4. Repeated/concurrent 401 when redirect is already in progress -> does not trigger duplicate redirect
  assert(
    shouldRedirectToLogin('/dashboard', '/api/Products', true) === false,
    'Concurrent/repeated 401 when isRedirecting is true does not trigger duplicate redirect'
  );

  return { passed: true, details };
}

// Self-execution helper
export const runTests = (): { passed: boolean; details: string[] } => {
  return runApiClientRedirectGuardTests();
};

describe('Axios redirect guard', () => {
  it('prevents auth endpoint and duplicate redirect loops', () => {
    const result = runApiClientRedirectGuardTests();
    expect(result.passed).toBe(true);
    expect(result.details).toHaveLength(5);
  });
});
