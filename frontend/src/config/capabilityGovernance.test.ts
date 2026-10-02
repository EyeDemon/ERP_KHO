import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from './erpWmsBlueprint';
import {
  getBlueprintGovernanceRows,
  getCapabilityGovernanceProfile,
} from './capabilityGovernance';

const findCapability = (id: string) => {
  for (const module of erpWmsBlueprint) {
    const capability = module.capabilities.find((item) => item.id === id);
    if (capability) return { module, capability };
  }
  throw new Error('Missing capability ' + id);
};

describe('capability governance metadata', () => {
  it('produces governance metadata for every blueprint capability', () => {
    const rows = getBlueprintGovernanceRows(erpWmsBlueprint);
    expect(rows).toHaveLength(131);
    for (const row of rows) {
      expect(row.profile.referencedSpecs.length).toBeGreaterThan(0);
      expect(row.profile.ownerModule.length).toBeGreaterThan(0);
      expect(row.profile.evidence).toHaveLength(9);
    }
  });

  it('tracks the four Screen Matrix review-required screens explicitly', () => {
    for (const id of ['INV-08', 'OUT-05', 'OUT-06', 'TR-01']) {
      const { module, capability } = findCapability(id);
      const profile = getCapabilityGovernanceProfile(module, capability);
      expect(profile.reviewStatus).toBe('Review Required');
      expect(profile.screenReference).toContain('UNMAPPED');
    }
  });

  it('keeps roadmap waves and applicability aligned for representative capabilities', () => {
    const inventory = findCapability('INV-02');
    expect(getCapabilityGovernanceProfile(inventory.module, inventory.capability).releaseWave).toBe(1);

    const inbound = findCapability('IN-07');
    expect(getCapabilityGovernanceProfile(inbound.module, inbound.capability).releaseWave).toBe(2);

    const count = findCapability('CT-07');
    expect(getCapabilityGovernanceProfile(count.module, count.capability).releaseWave).toBe(3);

    const task = findCapability('OV-06');
    expect(getCapabilityGovernanceProfile(task.module, task.capability).releaseWave).toBe(4);

    const kitting = findCapability('AX-09');
    const kittingProfile = getCapabilityGovernanceProfile(kitting.module, kitting.capability);
    expect(kittingProfile.releaseWave).toBe(5);
    expect(kittingProfile.applicability).toBe('INDUSTRY_OPTIONAL');
  });

  it('does not claim planned capabilities are production complete', () => {
    const picking = findCapability('OUT-05');
    const profile = getCapabilityGovernanceProfile(picking.module, picking.capability);
    expect(profile.evidence.find((item) => item.key === 'api')?.status).toBe('spec-only');
    expect(profile.evidence.find((item) => item.key === 'ux')?.status).toBe('covered');
  });
});
