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
    const missingPlannedIds = plannedIds.filter((id) => !coreInteractiveDemoIds.includes(id));
    expect(missingPlannedIds).toEqual([]);
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
    expect(implemented.map(({ capability }) => capability.id)).toContain('WH-02');
    expect(implemented.map(({ capability }) => capability.id)).toContain('WH-03');
    expect(implemented.map(({ capability }) => capability.id)).toContain('WH-04');
    expect(implemented.map(({ capability }) => capability.id)).toContain('WH-05');
    expect(implemented.map(({ capability }) => capability.id)).toContain('WH-06');

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

  it('keeps the WH-02 Blueprint preview separate while reflecting its live production implementation', () => {
    const entry = allCapabilities.find(({ capability }) => capability.id === 'WH-02');
    if (!entry) throw new Error('WH-02 capability missing');

    expect(entry.capability.status).toBe('live');
    expect(entry.capability.route).toBe('/warehouse-structure');
    expect(entry.capability.mockRoute).toBe('/system-blueprint/warehouse-structure/WH-02/workbench');
    expect(coreInteractiveDemoIds).toContain('WH-02');

    const demo = getCapabilityDemoDefinition(entry.capability, entry.module.name, entry.module.flow);
    expect(demo.exceptionTitle).toBe('WAREHOUSE_HIERARCHY_RULE_VIOLATION');
    expect(demo.stages).toContain('Bin / Location');
    expect(demo.commands).toContain('SHOW_UNMAPPED_LOCATIONS');
    expect(demo.fields).toContainEqual({ label: 'Production maturity', value: 'LIVE' });
    expect(demo.fields).toContainEqual({ label: 'Production route', value: '/warehouse-structure' });
  });

  it('reflects WH-03 live capacity enforcement while keeping its Blueprint preview separate', () => {
    const entry = allCapabilities.find(({ capability }) => capability.id === 'WH-03');
    if (!entry) throw new Error('WH-03 capability missing');

    expect(entry.capability.status).toBe('live');
    expect(entry.capability.route).toBe('/warehouse-structure');
    expect(coreInteractiveDemoIds).toContain('WH-03');

    const demo = getCapabilityDemoDefinition(entry.capability, entry.module.name, entry.module.flow);
    expect(demo.exceptionTitle).toBe('LOCATION_CAPACITY_EXCEEDED');
    expect(demo.fields).toContainEqual({ label: 'Production maturity', value: 'LIVE' });
    expect(demo.fields).toContainEqual({ label: 'Production route', value: '/warehouse-structure' });
    expect(demo.commands).toContain('VERIFY_PUTAWAY_GUARD');
  });

  it('reflects WH-04 live warehouse map while keeping its Blueprint preview separate', () => {
    const entry = allCapabilities.find(({ capability }) => capability.id === 'WH-04');
    if (!entry) throw new Error('WH-04 capability missing');

    expect(entry.capability.status).toBe('live');
    expect(entry.capability.route).toBe('/warehouse-map');

    const demo = getCapabilityDemoDefinition(entry.capability, entry.module.name, entry.module.flow);
    expect(demo.exceptionTitle).toBe('MAP_STALE_SNAPSHOT');
    expect(demo.fields).toContainEqual({ label: 'Production maturity', value: 'LIVE' });
    expect(demo.fields).toContainEqual({ label: 'Production route', value: '/warehouse-map' });
    expect(demo.commands).toContain('OPEN_PRODUCTION_MAP');
  });

  it('reflects WH-05 live warehouse calendar while keeping its Blueprint preview separate', () => {
    const entry = allCapabilities.find(({ capability }) => capability.id === 'WH-05');
    if (!entry) throw new Error('WH-05 capability missing');

    expect(entry.capability.status).toBe('live');
    expect(entry.capability.route).toBe('/warehouse-calendar');

    const demo = getCapabilityDemoDefinition(entry.capability, entry.module.name, entry.module.flow);
    expect(demo.exceptionTitle).toBe('WAREHOUSE_CALENDAR_VALIDATION_FAILED');
    expect(demo.fields).toContainEqual({ label: 'Production maturity', value: 'LIVE' });
    expect(demo.fields).toContainEqual({ label: 'Production route', value: '/warehouse-calendar' });
    expect(demo.commands).toContain('OPEN_PRODUCTION_CALENDAR');
  });

  it('reflects WH-06 live Dock & Yard while keeping its Blueprint preview separate', () => {
    const entry = allCapabilities.find(({ capability }) => capability.id === 'WH-06');
    if (!entry) throw new Error('WH-06 capability missing');

    expect(entry.capability.status).toBe('live');
    expect(entry.capability.route).toBe('/dock-yard');

    const demo = getCapabilityDemoDefinition(entry.capability, entry.module.name, entry.module.flow);
    expect(demo.exceptionTitle).toBe('DOCK_DOUBLE_ASSIGNMENT');
    expect(demo.fields).toContainEqual({ label: 'Production maturity', value: 'LIVE' });
    expect(demo.fields).toContainEqual({ label: 'Production route', value: '/dock-yard' });
    expect(demo.commands).toContain('OPEN_PRODUCTION_DOCK_YARD');
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
