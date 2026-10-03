import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';
import { canonicalSpecTitles, getCanonicalDocumentationRegister } from './documentationRegister';

describe('canonical documentation register', () => {
  it('indexes the complete 1-282 Notion documentation set', () => {
    expect(Object.keys(canonicalSpecTitles)).toHaveLength(282);
    const rows = getCanonicalDocumentationRegister(erpWmsBlueprint);
    expect(rows).toHaveLength(282);
    expect(rows[0].spec).toBe(1);
    expect(rows.at(-1)?.spec).toBe(282);
  });

  it('links canonical specs to owning blueprint capabilities when applicable', () => {
    const rows = getCanonicalDocumentationRegister(erpWmsBlueprint);
    expect(rows.find((row) => row.spec === 89)?.capabilityIds).toContain('AX-23');
    expect(rows.find((row) => row.spec === 249)?.capabilityIds).toContain('AX-14');
    expect(rows.find((row) => row.spec === 171)?.capabilityIds).toContain('IG-10');
  });

  it('keeps architecture standards visible without inventing business capabilities', () => {
    const rows = getCanonicalDocumentationRegister(erpWmsBlueprint);
    const archival = rows.find((row) => row.spec === 93);
    expect(archival?.representation).toBe('Platform / Governance');
    expect(archival?.capabilityIds).toEqual([]);
  });
});
