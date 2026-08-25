import { describe, expect, it, vi } from 'vitest';
import { createSingleFlight } from './apiClient';

describe('Axios refresh single-flight', () => {
  it('shares one refresh operation across concurrent callers and resets after completion', async () => {
    let resolve!: (value: string) => void;
    const operation = vi.fn(() => new Promise<string>((done) => { resolve = done; }));
    const refresh = createSingleFlight(operation);

    const requests = [refresh(), refresh(), refresh()];
    expect(operation).toHaveBeenCalledTimes(1);
    resolve('access-token');
    await expect(Promise.all(requests)).resolves.toEqual(['access-token', 'access-token', 'access-token']);

    operation.mockResolvedValueOnce('next-token');
    await expect(refresh()).resolves.toBe('next-token');
    expect(operation).toHaveBeenCalledTimes(2);
  });
});
