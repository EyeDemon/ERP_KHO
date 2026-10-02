import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';

describe('ERP WMS blueprint registry', () => {
  it('keeps the complete module and capability registry unique', () => {
    expect(erpWmsBlueprint).toHaveLength(17);
    const moduleKeys = erpWmsBlueprint.map((module) => module.key);
    expect(new Set(moduleKeys).size).toBe(moduleKeys.length);

    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    expect(capabilities).toHaveLength(131);
    const ids = capabilities.map((capability) => capability.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('requires every capability to carry goal, surface, status and spec traceability', () => {
    for (const module of erpWmsBlueprint) {
      expect(module.name.trim().length).toBeGreaterThan(0);
      expect(module.description.trim().length).toBeGreaterThan(0);
      expect(module.capabilities.length).toBeGreaterThan(0);
      for (const capability of module.capabilities) {
        expect(capability.name.trim().length).toBeGreaterThan(0);
        expect(capability.goal.trim().length).toBeGreaterThan(0);
        expect(capability.spec.trim().length).toBeGreaterThan(0);
        expect(capability.spec).not.toMatch(/\d+\s*-\s*\d+/);
        expect(capability.surfaces.length).toBeGreaterThan(0);
        expect(['live', 'foundation', 'planned', 'optional']).toContain(capability.status);
        if (capability.route) expect(capability.route.startsWith('/')).toBe(true);
      }
    }
  });

  it('keeps canonical audit additions explicit and mapped to primary specs', () => {
    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    const byId = new Map(capabilities.map((capability) => [capability.id, capability]));
    for (const id of ['OV-06', 'OV-07', 'MD-08', 'OUT-10', 'INV-11', 'AD-09', 'AD-10', 'AD-11', 'IG-08', 'AX-08', 'AX-09', 'AX-10']) {
      expect(byId.has(id)).toBe(true);
    }
    expect(byId.get('OUT-05')?.spec.split(/,\s*/)).toContain('36');
    expect(byId.get('INV-09')?.spec).toBe('32');
    expect(byId.get('INV-11')?.spec.split(/,\s*/)).toContain('82');
    expect(byId.get('TR-05')?.spec.split(/,\s*/)).toContain('44');
    expect(byId.get('HU-01')?.spec.split(/,\s*/)).toContain('37');
    expect(byId.get('DY-04')?.spec.split(/,\s*/)).toContain('47');
    expect(byId.get('AD-07')?.spec.split(/,\s*/)).toContain('61');
  });

  it('keeps critical core modules in the blueprint', () => {
    const keys = new Set(erpWmsBlueprint.map((module) => module.key));
    for (const key of [
      'inbound',
      'outbound',
      'inventory-control',
      'transfer-replenishment',
      'count-adjustment',
      'quality-returns',
      'administration',
      'integration',
      'mobile',
      'operations-resilience',
    ]) {
      expect(keys.has(key)).toBe(true);
    }
  });
});
