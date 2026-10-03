import { describe, expect, it } from 'vitest';
import { getMockScenarioRuntimeBalances, getMockScenarioRuntimeDefinition, mockScenarioRuntimeDefinitions } from './mockScenarioRuntime';

const totalPhysical = (scenarioId: string, step: number) =>
  getMockScenarioRuntimeBalances(scenarioId, step).reduce((sum, item) => sum + item.onHand + item.inTransit, 0);

describe('shared mock scenario runtime', () => {
  it('maps all golden scenarios into the shared cross-screen runtime', () => {
    expect(Object.keys(mockScenarioRuntimeDefinitions)).toHaveLength(22);
    for (let index = 1; index <= 22; index += 1) {
      const id = 'GS-' + String(index).padStart(2, '0');
      expect(getMockScenarioRuntimeDefinition(id)?.affectedCapabilities.length).toBeGreaterThan(0);
    }
  });

  it('models concurrency and idempotent dispatch without duplicate inventory effects', () => {
    expect(totalPhysical('GS-07', 0)).toBe(10);
    expect(getMockScenarioRuntimeBalances('GS-07', 3)[0].reserved).toBe(8);
    expect(getMockScenarioRuntimeBalances('GS-07', 3)[0].available).toBe(2);
    expect(totalPhysical('GS-08', 0)).toBe(20);
    expect(totalPhysical('GS-08', 1)).toBe(0);
    expect(totalPhysical('GS-08', 3)).toBe(0);
  });

  it('keeps event-only policy and integration scenarios mapped without inventing balances', () => {
    expect(getMockScenarioRuntimeBalances('GS-10', 2)).toEqual([]);
    expect(getMockScenarioRuntimeDefinition('GS-10')?.affectedCapabilities).toContain('MO-10');
    expect(getMockScenarioRuntimeBalances('GS-16', 4)).toEqual([]);
    expect(getMockScenarioRuntimeDefinition('GS-21')?.affectedCapabilities).toContain('AX-15');
  });

  it('conserves network rebalance quantity and controlled repair outcome', () => {
    expect(totalPhysical('GS-17', 1)).toBe(180);
    expect(totalPhysical('GS-17', 3)).toBe(180);
    expect(totalPhysical('GS-17', 4)).toBe(180);
    expect(totalPhysical('GS-20', 1)).toBe(100);
    expect(totalPhysical('GS-20', 2)).toBe(100);
    expect(totalPhysical('GS-20', 3)).toBe(0);
    expect(totalPhysical('GS-20', 4)).toBe(80);
  });

  it('keeps inbound posting and putaway boundaries explicit', () => {
    expect(totalPhysical('GS-01', 1)).toBe(0);
    expect(totalPhysical('GS-01', 2)).toBe(100);
    expect(totalPhysical('GS-01', 4)).toBe(100);
    const final = getMockScenarioRuntimeBalances('GS-01', 4);
    expect(final.find((item) => item.location === 'RECV-01')?.onHand).toBe(0);
    expect(final.find((item) => item.location === 'A01-R02-L03-B04')?.onHand).toBe(100);
  });

  it('keeps reserve/allocate/pick ledger-neutral until dispatch', () => {
    expect(totalPhysical('GS-02', 1)).toBe(100);
    expect(totalPhysical('GS-02', 3)).toBe(100);
    expect(totalPhysical('GS-02', 4)).toBe(80);
    const final = getMockScenarioRuntimeBalances('GS-02', 4)[0];
    expect(final.reserved).toBe(10);
    expect(final.available).toBe(70);
  });

  it('conserves source + transit + destination for transfer', () => {
    expect(totalPhysical('GS-03', 0)).toBe(100);
    expect(totalPhysical('GS-03', 2)).toBe(100);
    expect(totalPhysical('GS-03', 3)).toBe(100);
  });

  it('keeps QC and return inspection ledger-neutral until explicit posting', () => {
    expect(totalPhysical('GS-05', 2)).toBe(0);
    expect(totalPhysical('GS-05', 3)).toBe(95);
    expect(totalPhysical('GS-09', 2)).toBe(0);
    expect(totalPhysical('GS-09', 3)).toBe(6);
  });

  it('keeps reversal and delivery-return corrections explicit', () => {
    expect(totalPhysical('GS-06', 1)).toBe(100);
    expect(totalPhysical('GS-06', 2)).toBe(0);
    expect(totalPhysical('GS-06', 3)).toBe(80);
    expect(totalPhysical('GS-11', 3)).toBe(0);
    expect(totalPhysical('GS-11', 4)).toBe(20);
    expect(getMockScenarioRuntimeBalances('GS-11', 4)[0].available).toBeNull();
    expect(getMockScenarioRuntimeBalances('GS-14', 3)[0].available).toBeNull();
  });

  it('keeps pack and load ledger-neutral until dispatch', () => {
    expect(totalPhysical('GS-12', 1)).toBe(20);
    expect(totalPhysical('GS-12', 3)).toBe(20);
    expect(totalPhysical('GS-12', 4)).toBe(0);
    expect(getMockScenarioRuntimeDefinition('GS-12')?.affectedCapabilities).toContain('OUT-08');
  });

  it('does not mutate inventory during counts before adjustment post', () => {
    expect(totalPhysical('GS-04', 1)).toBe(84);
    expect(totalPhysical('GS-04', 3)).toBe(84);
    expect(totalPhysical('GS-04', 4)).toBe(82);
    expect(getMockScenarioRuntimeDefinition('GS-04')?.affectedCapabilities).toContain('CT-07');
  });
});
