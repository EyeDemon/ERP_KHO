import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';

describe('ERP WMS blueprint registry', () => {
  it('keeps the complete module and capability registry unique', () => {
    expect(erpWmsBlueprint).toHaveLength(17);
    const moduleKeys = erpWmsBlueprint.map((module) => module.key);
    expect(new Set(moduleKeys).size).toBe(moduleKeys.length);

    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    expect(capabilities).toHaveLength(179);
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
    for (const id of ['OV-06', 'OV-07', 'MD-08', 'OUT-10', 'INV-11', 'RP-09', 'RP-10', 'AD-09', 'AD-10', 'AD-11', 'AD-12', 'AD-13', 'AD-14', 'IG-08', 'OP-08', 'OP-09', 'AX-08', 'AX-09', 'AX-10', 'AX-11', 'AX-12', 'AX-13', 'AX-14', 'AX-15', 'AX-16', 'AX-17', 'AX-18', 'AX-19', 'AX-20', 'AX-21', 'AX-22', 'AX-23', 'AX-24', 'AX-25', 'AX-26', 'AX-27', 'AX-28', 'AX-29', 'AX-30', 'AX-31', 'AX-32', 'AX-33', 'AX-34', 'AX-35', 'AX-36', 'AX-37', 'AX-38', 'AX-39', 'AX-40', 'AX-41', 'AX-42', 'MD-09', 'WH-07', 'IG-09', 'IG-10', 'OP-10', 'OP-11', 'OV-08', 'MO-11', 'MO-12']) {
      expect(byId.has(id)).toBe(true);
    }
    expect(byId.get('OUT-05')?.spec.split(/,\s*/)).toContain('36');
    expect(byId.get('INV-09')?.spec).toBe('32');
    expect(byId.get('INV-11')?.spec.split(/,\s*/)).toContain('82');
    expect(byId.get('TR-05')?.spec.split(/,\s*/)).toContain('44');
    expect(byId.get('HU-01')?.spec.split(/,\s*/)).toContain('37');
    expect(byId.get('DY-04')?.spec.split(/,\s*/)).toContain('47');
    expect(byId.get('AD-07')?.spec.split(/,\s*/)).toContain('61');
    expect(byId.get('AX-14')?.spec).toBe('249');
    expect(byId.get('AX-22')?.spec.split(/,\s*/)).toContain('246');
    expect(byId.get('RP-10')?.spec.split(/,\s*/)).toContain('250');
    expect(byId.get('AX-23')?.spec).toBe('89');
    expect(byId.get('AX-31')?.spec).toBe('189');
    expect(byId.get('AX-42')?.spec.split(/,\s*/)).toContain('203');
    expect(byId.get('IG-09')?.spec.split(/,\s*/)).toContain('154');
    expect(byId.get('IG-10')?.spec.split(/,\s*/)).toContain('171');
    expect(byId.get('OP-10')?.spec).toBe('127');
    expect(byId.get('OV-08')?.spec.split(/,\s*/)).toContain('83');
    expect(byId.get('MO-11')?.spec.split(/,\s*/)).toContain('95');
    expect(byId.get('MO-12')?.spec.split(/,\s*/)).toContain('71');
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
