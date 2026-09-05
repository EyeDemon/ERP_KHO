import { beforeEach, describe, expect, it, vi } from 'vitest';
import { completeIdempotentAction, idempotencyKeyFor } from './idempotency';

describe('logical action idempotency keys', () => {
  beforeEach(() => vi.stubGlobal('crypto', { randomUUID: vi.fn().mockReturnValueOnce('key-1').mockReturnValueOnce('key-2') }));

  it('reuses a key for retry and rotates only after success', () => {
    expect(idempotencyKeyFor('approve:7')).toBe('key-1');
    expect(idempotencyKeyFor('approve:7')).toBe('key-1');
    completeIdempotentAction('approve:7');
    expect(idempotencyKeyFor('approve:7')).toBe('key-2');
  });
});
