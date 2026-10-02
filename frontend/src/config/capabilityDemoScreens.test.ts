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
