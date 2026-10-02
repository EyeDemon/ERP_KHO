import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';

describe('ERP WMS blueprint registry', () => {
  it('keeps the complete module and capability registry unique', () => {
    expect(erpWmsBlueprint).toHaveLength(17);
    const moduleKeys = erpWmsBlueprint.map((module) => module.key);
    expect(new Set(moduleKeys).size).toBe(moduleKeys.length);

    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    expect(capabilities).toHaveLength(119);
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
        expect(capability.surfaces.length).toBeGreaterThan(0);
        expect(['live', 'foundation', 'planned', 'optional']).toContain(capability.status);
        if (capability.route) expect(capability.route.startsWith('/')).toBe(true);
      }
    }
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
