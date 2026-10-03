import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';
import { coreInteractiveDemoIds, getCapabilityDemoDefinition } from './capabilityDemoScreens';

const allCapabilities = erpWmsBlueprint.flatMap((module) =>
  module.capabilities.map((capability) => ({ module, capability })),
);

describe('interactive capability demo registry', () => {
  it('models every planned capability explicitly', () => {
    const plannedIds = allCapabilities
      .filter(({ capability }) => capability.status === 'planned')
      .map(({ capability }) => capability.id)
      .sort();
    expect(coreInteractiveDemoIds.slice().sort()).toEqual(plannedIds);
    expect(coreInteractiveDemoIds).toHaveLength(99);
  });

  it('provides state, commands and exception behavior for every capability', () => {
    for (const { module, capability } of allCapabilities) {
      const demo = getCapabilityDemoDefinition(capability, module.name, module.flow);
      expect(demo.stages.length).toBeGreaterThan(1);
      expect(demo.fields.length).toBeGreaterThan(0);
      expect(demo.commands.length).toBeGreaterThan(0);
      expect(demo.exceptionTitle.length).toBeGreaterThan(0);
      expect(demo.exceptionDetail.length).toBeGreaterThan(0);
    }
  });

  it('gives every optional capability an advanced feature simulator instead of the generic fallback', () => {
    const optionalEntries = allCapabilities.filter(({ capability }) => capability.status === 'optional');
    expect(optionalEntries).toHaveLength(47);

    for (const { module, capability } of optionalEntries) {
      const demo = getCapabilityDemoDefinition(capability, module.name, module.flow);
      expect(demo.commands).not.toContain('SIMULATE_ACTION');
      expect(demo.fields.some((field) => field.label === 'Applicability')).toBe(true);
      expect(demo.exceptionTitle).not.toBe('MOCK_VALIDATION_EXCEPTION');
    }
  });

  it('gives every live/foundation capability a domain-specific implemented preview', () => {
    const implemented = allCapabilities.filter(({ capability }) =>
      capability.status === 'live' || capability.status === 'foundation'
    );
    expect(implemented).toHaveLength(34);

    for (const { module, capability } of implemented) {
      const demo = getCapabilityDemoDefinition(capability, module.name, module.flow);
      expect(demo.commands).not.toContain('SIMULATE_ACTION');
      expect(demo.exceptionTitle).not.toBe('MOCK_VALIDATION_EXCEPTION');
      expect(demo.fields.some((field) => field.label === 'Production maturity')).toBe(true);
    }
  });

  it('leaves no capability on the generic simulator fallback', () => {
    for (const { module, capability } of allCapabilities) {
      const demo = getCapabilityDemoDefinition(capability, module.name, module.flow);
      expect(demo.exceptionTitle).not.toBe('MOCK_VALIDATION_EXCEPTION');
      expect(demo.commands).not.toContain('SIMULATE_ACTION');
    }
  });

  it('keeps canonical inventory boundaries concrete in detailed core demos', () => {
    const dispatchEntry = allCapabilities.find(({ capability }) => capability.id === 'OUT-08');
    const moveEntry = allCapabilities.find(({ capability }) => capability.id === 'INV-08');
    if (!dispatchEntry || !moveEntry) throw new Error('Core capability missing');

    const dispatch = getCapabilityDemoDefinition(dispatchEntry.capability, dispatchEntry.module.name, dispatchEntry.module.flow);
    const move = getCapabilityDemoDefinition(moveEntry.capability, moveEntry.module.name, moveEntry.module.flow);
    expect(dispatch.exceptionTitle).toBe('DISPATCH_INVENTORY_CONFLICT');
    expect(dispatch.stages).toContain('DISPATCHED');
    expect(move.quantity?.factor).toBe(12);
    expect(move.stages).toContain('POSTED');
  });
});
