import { describe, expect, it } from 'vitest';
import { erpWmsBlueprint } from '../config/erpWmsBlueprint';
import {
  mockCapabilityFixtures,
  mockCapabilitySampleRecordIds,
  mockInventoryBalances,
  mockPartners,
  mockProducts,
  mockRecordCount,
  mockRecountAttempts,
  mockTransferConservation,
  mockWarehouses,
  mockWorkCenters,
} from './erpWmsMockData';

describe('ERP WMS complete mock dataset', () => {
  it('covers every blueprint module with operational records', () => {
    const moduleKeys = erpWmsBlueprint.map((module) => module.key).sort();
    const fixtureKeys = Object.keys(mockWorkCenters).sort();
    expect(fixtureKeys).toEqual(moduleKeys);
    expect(mockRecordCount).toBeGreaterThanOrEqual(60);
    for (const module of erpWmsBlueprint) {
      expect(mockWorkCenters[module.key].records.length).toBeGreaterThanOrEqual(4);
    }
  });

  it('provides a dedicated fixture for every capability', () => {
    const capabilities = erpWmsBlueprint.flatMap((module) => module.capabilities);
    expect(Object.keys(mockCapabilityFixtures)).toHaveLength(capabilities.length);
    for (const module of erpWmsBlueprint) {
      for (const capability of module.capabilities) {
        const fixture = mockCapabilityFixtures[capability.id];
        expect(fixture).toBeTruthy();
        expect(fixture.capabilityId).toBe(capability.id);
        expect(fixture.moduleKey).toBe(module.key);
        expect(fixture.sampleRecordId).not.toBe('NO-SAMPLE');
        expect(fixture.sampleReference).not.toBe('NO-SAMPLE');
      }
    }
  });

  it('maps newly audited capabilities to semantically relevant mock records', () => {
    for (const [capabilityId, recordId] of Object.entries(mockCapabilitySampleRecordIds)) {
      const fixture = mockCapabilityFixtures[capabilityId];
      expect(fixture).toBeTruthy();
      expect(fixture.sampleRecordId).toBe(recordId);
      expect(fixture.sampleReference).not.toBe('NO-SAMPLE');
    }
  });

  it('keeps record ids unique and core references valid', () => {
    const warehouseCodes = new Set(mockWarehouses.map((item) => item.code));
    const productCodes = new Set(mockProducts.map((item) => item.code));
    const partnerCodes = new Set(mockPartners.map((item) => item.code));
    const ids: string[] = [];

    for (const workCenter of Object.values(mockWorkCenters)) {
      expect(Number.isNaN(Date.parse(workCenter.snapshotAt))).toBe(false);
      for (const record of workCenter.records) {
        ids.push(record.id);
        expect(warehouseCodes.has(record.warehouse)).toBe(true);
        if (record.productCode) expect(productCodes.has(record.productCode)).toBe(true);
        if (record.partnerCode) expect(partnerCodes.has(record.partnerCode)).toBe(true);
        if (record.quantity != null) expect(record.quantity).toBeGreaterThanOrEqual(0);
        expect(Number.isNaN(Date.parse(record.updatedAt))).toBe(false);
      }
    }

    expect(new Set(ids).size).toBe(ids.length);
  });

  it('keeps inventory availability and allocation invariants coherent', () => {
    for (const balance of mockInventoryBalances) {
      expect(balance.onHand).toBeGreaterThanOrEqual(0);
      expect(balance.reserved).toBeGreaterThanOrEqual(0);
      expect(balance.allocated).toBeGreaterThanOrEqual(0);
      expect(balance.available).toBeGreaterThanOrEqual(0);
      expect(balance.allocated).toBeLessThanOrEqual(balance.reserved);
      expect(balance.reserved).toBeLessThanOrEqual(balance.onHand);
      const eligibleOnHand = balance.onHand - balance.qcHold - balance.quarantine;
      expect(balance.reserved).toBeLessThanOrEqual(eligibleOnHand);
      expect(balance.available).toBe(eligibleOnHand - balance.reserved);
    }
  });

  it('conserves transfer quantity across source, transit and destination', () => {
    for (const transfer of mockTransferConservation) {
      expect(transfer.source).toBeGreaterThanOrEqual(0);
      expect(transfer.transit).toBeGreaterThanOrEqual(0);
      expect(transfer.destination).toBeGreaterThanOrEqual(0);
      expect(transfer.source + transfer.transit + transfer.destination).toBe(transfer.requested);
    }
  });

  it('preserves recount attempts and marks one final accepted result', () => {
    for (const recount of mockRecountAttempts) {
      expect(recount.attempts.length).toBeGreaterThanOrEqual(2);
      expect(recount.attempts.map((item) => item.attempt)).toEqual(
        recount.attempts.map((_, index) => index + 1),
      );
      const accepted = recount.attempts.filter((item) => item.accepted);
      expect(accepted).toHaveLength(1);
      expect(accepted[0].countedQty).toBe(recount.finalAccepted);
      expect(recount.attempts.at(-1)?.accepted).toBe(true);
    }
  });

  it('does not present an in-progress count as completed before approval/post flow', () => {
    const countRecords = mockWorkCenters['count-adjustment'].records;
    const activeCountRefs = new Set(['CC-2026-0142', 'CC-2026-0143', 'FC-2026-0021', 'ADJ-2026-0088']);
    for (const record of countRecords.filter((item) => activeCountRefs.has(item.reference))) {
      expect(record.status).not.toBe('COMPLETED');
      expect(record.status).not.toBe('POSTED');
    }
  });
});
