import { describe, expect, it } from 'vitest';
import { searchMockSystem } from './erpWmsMockSearch';

describe('ERP WMS mock global search', () => {
  it('prioritizes exact barcode lookup and resolves it to the product', () => {
    const results = searchMockSystem('8938501000011');
    expect(results[0]).toEqual(expect.objectContaining({
      kind: 'Barcode',
      reference: '8938501000011',
      exact: true,
    }));
    expect(results[0].title).toContain('SKU-1001');
  });

  it('finds product, partner, document, lot and serial references', () => {
    expect(searchMockSystem('SKU-2001').some((item) => item.kind === 'Product')).toBe(true);
    expect(searchMockSystem('SUP-001').some((item) => item.kind === 'Partner')).toBe(true);
    expect(searchMockSystem('GR-2026-1048').some((item) => item.kind === 'Document/Task')).toBe(true);
    expect(searchMockSystem('LOT-1001-260930').some((item) => item.kind === 'Lot/Serial')).toBe(true);
    expect(searchMockSystem('SER-TWS-000128').some((item) => item.kind === 'Lot/Serial')).toBe(true);
  });

  it('applies simulated warehouse scope to operational records', () => {
    expect(searchMockSystem('GR-2026-1048', ['WH-DN-01']).some((item) => item.reference === 'GR-2026-1048')).toBe(false);
    expect(searchMockSystem('GR-2026-1041', ['WH-DN-01']).some((item) => item.reference === 'GR-2026-1041')).toBe(true);
  });

  it('returns no results for an empty query', () => {
    expect(searchMockSystem('   ')).toEqual([]);
  });
});
