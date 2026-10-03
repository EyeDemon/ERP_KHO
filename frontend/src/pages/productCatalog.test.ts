import { describe, expect, it } from 'vitest';
import { categoryChanged, categoryRequest, normalizeBarcodeInput } from './productCatalog';

describe('product catalog contract', () => {
  it('preserves leading zeroes and case while trimming', () => {
    expect(normalizeBarcodeInput(' 001AbC ')).toBe('001AbC');
  });
  it.each(['', 'a b', 'mã-vạch'])('rejects invalid barcode %s', value => {
    expect(() => normalizeBarcodeInput(value)).toThrow(/Barcode/);
  });
  it('uses explicit null only when removing category', () => {
    expect(categoryRequest('')).toEqual({ categoryId: null });
    expect(categoryRequest(7)).toEqual({ categoryId: 7 });
  });
  it('does not issue a category mutation when an old client value is unchanged', () => {
    expect(categoryChanged(undefined, '')).toBe(false);
    expect(categoryChanged(7, 7)).toBe(false);
  });
});
